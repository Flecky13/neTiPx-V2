using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace neTiPx 
{
    public class NetzwerkInfo
    {
        private static readonly string[] InfoNamen =
        {
            "Name",
            "MAC",
            "IPv4",
            "Gateway4",
            "DNS4",
            "IPv6",
            "Gateway6",
            "DNS6"
        };

        public static string[,]? HoleNetzwerkInfo(string adapterName)
        {
            try
            {
                NetworkInterface? adapter = FindeAdapter(adapterName);
                if (adapter == null)
                {
                    Console.WriteLine($"[NetzwerkInfo] Kein Adapter mit Name/NetConnectionID '{adapterName}' gefunden.");
                    return null;
                }

                // Wenn Adapter DOWN → nur Basisinfos zurückgeben
                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    Console.WriteLine($"[NetzwerkInfo] Adapter '{adapter.Name}' ist DOWN → nur Basisdaten.");

                    string[,] infosDown = new string[3, 2];

                    int idx = 0;

                    infosDown[idx, 0] = "Name";
                    infosDown[idx++, 1] = adapter.Name;

                    infosDown[idx, 0] = "MAC";
                    infosDown[idx++, 1] = string.Join(":", adapter.GetPhysicalAddress()
                        .GetAddressBytes()
                        .Select(b => b.ToString("X2")));

                    infosDown[idx, 0] = "Status";
                    infosDown[idx++, 1] = "Keine Verbindung";

                    return infosDown;
                }

                // Adapter ist UP → vollständige Infos
                var props = adapter.GetIPProperties();
                string[,] infos = new string[InfoNamen.Length, 2];

                int index = 0;

                infos[index, 0] = "Name";
                infos[index++, 1] = adapter.Name;

                infos[index, 0] = "MAC";
                infos[index++, 1] = string.Join(":", adapter.GetPhysicalAddress()
                    .GetAddressBytes()
                    .Select(b => b.ToString("X2")));

                // IPv4
                var ipv4 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();
                infos[index, 0] = "IPv4";
                infos[index++, 1] = ipv4.Any() ? string.Join(Environment.NewLine, ipv4) : "-";

                // IPv4 Gateways
                var gw4 = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(g => g.Address.ToString())
                    .ToList();
                infos[index, 0] = "Gateway4";
                infos[index++, 1] = gw4.Any() ? string.Join(Environment.NewLine, gw4) : "-";

                // DNS4
                var dns4 = props.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToList();
                infos[index, 0] = "DNS4";
                infos[index++, 1] = dns4.Any() ? string.Join(Environment.NewLine, dns4) : "-";

                // IPv6
                var ipv6 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString())
                    .ToList();
                infos[index, 0] = "IPv6";
                infos[index++, 1] = ipv6.Any() ? string.Join(Environment.NewLine, ipv6) : "-";

                // IPv6 Gateways
                var gw6 = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(g => g.Address.ToString())
                    .ToList();
                infos[index, 0] = "Gateway6";
                infos[index++, 1] = gw6.Any() ? string.Join(Environment.NewLine, gw6) : "-";

                // DNS6
                var dns6 = props.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(d => d.ToString())
                    .ToList();
                infos[index, 0] = "DNS6";
                infos[index++, 1] = dns6.Any() ? string.Join(Environment.NewLine, dns6) : "-";

                return infos;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetzwerkInfo] Fehler: {ex.Message}");
                return null;
            }
        }

        private static NetworkInterface? FindeAdapter(string adapterName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(adapterName))
                    return null;

                string safeName = adapterName.Replace("'", "''");
                var query = $"SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID = '{safeName}'";
                using var searcher = new ManagementObjectSearcher(query);
                var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    var mac = (mo["MACAddress"] as string) ?? string.Empty;
                    if (!string.IsNullOrEmpty(mac))
                    {
                        string normMac = mac.Replace(":", "").Replace("-", "").ToUpperInvariant();
                        var adapter = NetworkInterface.GetAllNetworkInterfaces()
                            .FirstOrDefault(n => n.GetPhysicalAddress().ToString().ToUpperInvariant() == normMac);
                        if (adapter != null)
                            return adapter;
                    }
                }

                // Fallback: direkter Vergleich per Name
                return NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(a => a.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetzwerkInfo] FindeAdapter-Fehler: {ex.Message}");
                return null;
            }
        }
    }
}
