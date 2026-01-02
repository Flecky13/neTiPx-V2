using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

namespace neTiPx
{
    public class WifiNetwork
    {
        public string SSID { get; set; }
        public string BSSID { get; set; }
        public int SignalStrengthDbm { get; set; }
        public int SignalStrengthPercent { get; set; }

        public string SignalSymbol
        {
            get
            {
                if (SignalStrengthPercent >= 75) return "📶";
                if (SignalStrengthPercent >= 50) return "📳";
                if (SignalStrengthPercent >= 25) return "📴";
                return "❌";
            }
        }

        public WifiNetwork(string ssid, string bssid, int signalDbm)
        {
            SSID = ssid;
            BSSID = bssid;
            SignalStrengthDbm = signalDbm;
            // Convert dBm to percentage (typical range: -30 to -90 dBm)
            SignalStrengthPercent = Math.Max(0, Math.Min(100, 2 * (signalDbm + 100)));
        }
    }

    public class WifiScanner
    {
        // Native WiFi API imports
        [DllImport("wlanapi.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint WlanGetNetworkBssList(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pSsid, uint dot11BssType, bool bSecurityEnabled, IntPtr pReserved, out IntPtr ppWlanBssList);

        [DllImport("wlanapi.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern uint WlanScan(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        private const uint WLAN_API_VERSION = 2;
        private const uint DOT11_BSS_TYPE_ANY = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_INTERFACE_INFO_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
        }

        // DOT11_SSID structure: 4 bytes length + 32 bytes data
        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        // WLAN_BSS_ENTRY - Struktur basierend auf realem Hex-Dump (nicht Vistumbler!)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WLAN_BSS_ENTRY
        {
            public uint uSSIDLength;                  // Offset 0-3
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;                     // Offset 4-35
            public uint uPhyId;                       // Offset 36-39
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11BssId;                 // Offset 40-45
            public ushort padding1;                   // Offset 46-47 (PADDING!)
            public uint dot11BssType;                 // Offset 48-51
            public uint dot11BssPhyType;              // Offset 52-55
            public int lRssi;                         // Offset 56-59
            public uint uLinkQuality;                 // Offset 60-63
            public byte bInRegDomain;                 // Offset 64
            public byte padding2;                     // Offset 65 (padding)
            public ushort usBeaconPeriod;             // Offset 66-67
            public ulong ullTimestamp;                // Offset 68-75
            public ulong ullHostTimestamp;            // Offset 76-83
            public ushort usCapabilityInformation;    // Offset 84-85
            public ushort padding3;                   // Offset 86-87
            public uint ulChCenterFrequency;          // Offset 88-91
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 67)]
            public uint[] wlanRateSet;                // Offset 92-359 (268 bytes = 67 * 4)
            public uint ulIeOffset;                   // Offset 360-363
            public uint ulIeSize;                     // Offset 364-367
        }

        public static List<WifiNetwork> ScanWifiNetworks()
        {
            var networks = new List<WifiNetwork>();

            try
            {
                uint negotiatedVersion = 0;
                IntPtr clientHandle = IntPtr.Zero;

                // Open WLAN handle
                uint result = WlanOpenHandle(WLAN_API_VERSION, IntPtr.Zero, out negotiatedVersion, out clientHandle);
                if (result != 0)
                    return networks;

                try
                {
                    // Enumerate interfaces
                    IntPtr interfaceListPtr = IntPtr.Zero;
                    result = WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceListPtr);
                    if (result != 0)
                        return networks;

                    try
                    {
                        WLAN_INTERFACE_INFO_LIST interfaceList = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST>(interfaceListPtr);

                        // Iterate through each interface
                        for (uint i = 0; i < interfaceList.dwNumberOfItems; i++)
                        {
                            IntPtr interfaceInfoPtr = new IntPtr(interfaceListPtr.ToInt64() + Marshal.SizeOf(typeof(WLAN_INTERFACE_INFO_LIST)) + (int)(i * 532)); // 532 is size of WLAN_INTERFACE_INFO

                            // Get interface GUID (first 16 bytes of WLAN_INTERFACE_INFO)
                            Guid interfaceGuid = Marshal.PtrToStructure<Guid>(interfaceInfoPtr);

                            // Trigger an active scan to get fresh network list
                            System.Diagnostics.Trace.WriteLine($"[WiFi] Triggering scan for interface {interfaceGuid}");
                            uint scanResult = WlanScan(clientHandle, ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                            if (scanResult == 0)
                            {
                                // Wait a moment for scan to complete
                                System.Threading.Thread.Sleep(3000);
                                System.Diagnostics.Trace.WriteLine($"[WiFi] Scan completed, retrieving results");
                            }
                            else
                            {
                                System.Diagnostics.Trace.WriteLine($"[WiFi] Scan failed with code {scanResult}, using cached results");
                            }

                            // Get BSS list
                            IntPtr bssListPtr = IntPtr.Zero;
                            result = WlanGetNetworkBssList(clientHandle, ref interfaceGuid, IntPtr.Zero, DOT11_BSS_TYPE_ANY, false, IntPtr.Zero, out bssListPtr);

                            if (result == 0 && bssListPtr != IntPtr.Zero)
                            {
                                try
                                {
                                    // WLAN_BSS_LIST structure: dwTotalSize (4 bytes), dwNumberOfItems (4 bytes), then entries
                                    uint dwTotalSize = (uint)Marshal.ReadInt32(bssListPtr);
                                    uint dwNumberOfItems = (uint)Marshal.ReadInt32(new IntPtr(bssListPtr.ToInt64() + 4));

                                    System.Diagnostics.Trace.WriteLine($"[WiFi] Found {dwNumberOfItems} networks, total size: {dwTotalSize} bytes");

                                    // Sanity check
                                    if (dwNumberOfItems > 100 || dwNumberOfItems == 0 || dwTotalSize < 100)
                                    {
                                        System.Diagnostics.Trace.WriteLine($"[WiFi] Suspicious values or no networks found");
                                        return networks;
                                    }

                                    IntPtr bssEntryPtr = new IntPtr(bssListPtr.ToInt64() + 8);

                                    for (uint j = 0; j < dwNumberOfItems; j++)
                                    {
                                        try
                                        {
                                            // Use Marshal.PtrToStructure to read the entry (360 bytes fixed size)
                                            WLAN_BSS_ENTRY entry = Marshal.PtrToStructure<WLAN_BSS_ENTRY>(bssEntryPtr);

                                            // Extract SSID directly from structure fields
                                            string ssid = "";
                                            if (entry.uSSIDLength > 0 && entry.uSSIDLength <= 32)
                                            {
                                                ssid = System.Text.Encoding.UTF8.GetString(entry.ucSSID, 0, (int)entry.uSSIDLength);
                                                if (string.IsNullOrWhiteSpace(ssid))
                                                    ssid = "(Hidden Network)";
                                            }
                                            else if (entry.uSSIDLength == 0)
                                            {
                                                ssid = "(Hidden Network)";
                                            }
                                            else
                                            {
                                                System.Diagnostics.Trace.WriteLine($"[WiFi] Entry {j}: Invalid SSID length={entry.uSSIDLength}");
                                                ssid = "(Invalid SSID)";
                                            }

                                            // Extract BSSID and RSSI from the structure
                                            string bssidStr = BitConverter.ToString(entry.dot11BssId);
                                            int rssi = entry.lRssi;

                                            // Only add if RSSI is reasonable (-100 to 0 dBm)
                                            if (rssi >= -100 && rssi <= 0 && entry.uSSIDLength <= 32)
                                            {
                                                networks.Add(new WifiNetwork(ssid, bssidStr, rssi));
                                                System.Diagnostics.Trace.WriteLine($"[WiFi] Entry {j}: {ssid}, BSSID={bssidStr}, RSSI={rssi}");
                                            }

                                            // Move to next entry using FIXED 360 byte size (like Vistumbler)
                                            bssEntryPtr = new IntPtr(bssEntryPtr.ToInt64() + 360);
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Trace.WriteLine($"[WiFi] Error processing entry {j}: {ex.Message}");
                                            break;
                                        }
                                    }

                                    System.Diagnostics.Trace.WriteLine($"[WiFi] Successfully added {networks.Count} networks");
                                }
                                finally
                                {
                                    WlanFreeMemory(bssListPtr);
                                }
                            }
                        }
                    }
                    finally
                    {
                        WlanFreeMemory(interfaceListPtr);
                    }
                }
                finally
                {
                    WlanCloseHandle(clientHandle, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[WiFi] Error in ScanWifiNetworks: {ex.Message}");
            }

            // Remove duplicates and sort by signal strength
            return networks
                .GroupBy(n => n.BSSID)
                .Select(g => g.First())
                .OrderByDescending(n => n.SignalStrengthDbm)
                .ToList();
        }
    }
}
