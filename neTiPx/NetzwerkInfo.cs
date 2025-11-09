using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Management;

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
                NetworkInterface? adapter = null;

                // Versuche zunächst, per NetConnectionID (WMI) das passende Interface zu finden
                try
                {
                    if (!string.IsNullOrWhiteSpace(adapterName))
                    {
                        // WMI-Abfrage: Win32_NetworkAdapter.NetConnectionID
                        var safeName = adapterName.Replace("'", "''");
                        var query = $"SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID = '{safeName}'";
                        using var searcher = new ManagementObjectSearcher(query);
                        var results = searcher.Get();
                        foreach (ManagementObject mo in results)
                        {
                            // Versuche zuerst über MAC-Adresse zu matchen
                            var mac = (mo["MACAddress"] as string) ?? string.Empty;
                            if (!string.IsNullOrEmpty(mac))
                            {
                                string normMac = mac.Replace(":", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
                                adapter = NetworkInterface.GetAllNetworkInterfaces()
                                    .FirstOrDefault(n =>
                                    {
                                        var phys = n.GetPhysicalAddress();
                                        var physStr = phys != null ? phys.ToString() : string.Empty;
                                        return !string.IsNullOrEmpty(physStr) && physStr.Replace("-", string.Empty).ToUpperInvariant() == normMac;
                                    });
                                if (adapter != null)
                                    break;
                            }

                            // Wenn MAC nicht half, versuche InterfaceIndex
                            var idxObj = mo["InterfaceIndex"] ?? mo["Index"];
                            if (idxObj != null && int.TryParse(idxObj.ToString(), out int idx))
                            {
                                adapter = NetworkInterface.GetAllNetworkInterfaces()
                                    .FirstOrDefault(n =>
                                    {
                                        try
                                        {
                                            var ipv4Props = n.GetIPProperties().GetIPv4Properties();
                                            return ipv4Props != null && ipv4Props.Index == idx;
                                        }
                                        catch
                                        {
                                            return false;
                                        }
                                    });
                                if (adapter != null)
                                    break;
                            }
                        }
                    }
                }
                catch (Exception wex)
                {
                    Console.WriteLine($"[NetzwerkInfo] WMI-Abfrage fehlgeschlagen: {wex.Message}");
                }

                // Fallback: Suche per Interface-Name (bisheriges Verhalten)
                if (adapter == null)
                {
                    adapter = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(a => a.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
                }

                if (adapter == null)
                {
                    Console.WriteLine($"[NetzwerkInfo] Kein Adapter mit Name/NetConnectionID '{adapterName}' gefunden.");
                    return null;
                }

                var props = adapter.GetIPProperties();
                string[,] infos = new string[InfoNamen.Length, 2];

                infos[0, 0] = "Name";
                infos[0, 1] = adapter.Name;

                // IPv4
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?
                    .Address.ToString() ?? "-";
                infos[1, 0] = "IPv4";
                infos[1, 1] = ipv4;

                // IPv6 - ALLE sammeln
                var ipv6Adressen = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString())
                    .ToList();

                infos[2, 0] = "IPv6";
                infos[2, 1] = ipv6Adressen.Any()
                    ? string.Join(Environment.NewLine, ipv6Adressen)
                    : "-";

                // Gateway
                infos[3, 0] = "Gateway";
                infos[3, 1] = props.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "-";

                // DNS
                var dnsList = props.DnsAddresses.Select(d => d.ToString()).ToList();
                infos[4, 0] = "DNS1";
                infos[4, 1] = dnsList.Count > 0 ? dnsList[0] : "-";

                infos[5, 0] = "DNS2";
                infos[5, 1] = dnsList.Count > 1 ? dnsList[1] : "-";

                // MAC-Adresse
                infos[6, 0] = "MAC";
                infos[6, 1] = string.Join(":", adapter.GetPhysicalAddress()
                    .GetAddressBytes()
                    .Select(b => b.ToString("X2")));

                // Debug-Ausgabe
                Console.WriteLine($"[NetzwerkInfo] Adapter '{adapterName}' erfolgreich eingelesen:");
                for (int i = 0; i < infos.GetLength(0); i++)
                {
                    var label = infos[i, 0];
                    var value = infos[i, 1]?.Replace(Environment.NewLine, " | "); // Zeilenumbruch im Log schöner darstellen
                    Console.WriteLine($"  {label,-8}: {value}");
                }

                return infos;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetzwerkInfo] Fehler: {ex.Message}");
                return null;
            }
        }
    }
}
