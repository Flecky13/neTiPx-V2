using System;
using System.Windows;

namespace neTiPx
{
    public partial class WifiDetailsWindow : Window
    {
        public WifiDetailsWindow(WifiNetwork network)
        {
            InitializeComponent();

            if (network == null)
            {
                MessageBox.Show("Keine Netzwerk-Informationen verfügbar.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            DisplayNetworkDetails(network);
        }

        private void DisplayNetworkDetails(WifiNetwork network)
        {
            // Header
            TxtSignalSymbol.Text = network.SignalSymbol;
            TxtSSID.Text = network.SSID;
            TxtSignalStrength.Text = $"{network.SignalStrengthDbm} dBm ({network.SignalStrengthPercent}%)";

            // Basic Information
            TxtSSIDValue.Text = network.SSID;
            TxtBSSIDValue.Text = network.BSSID;
            TxtNetworkType.Text = network.NetworkType;
            TxtSecured.Text = network.IsSecured ? "🔒 Ja" : "🔓 Nein";
            TxtLastSeen.Text = network.LastSeen.ToString("dd.MM.yyyy HH:mm:ss");
            TxtPhyId.Text = network.PhyId.ToString();

            // Signal Information
            TxtSignalDbm.Text = $"{network.SignalStrengthDbm} dBm";
            TxtSignalPercent.Text = $"{network.SignalStrengthPercent}%";
            ProgressSignal.Value = network.SignalStrengthPercent;

            // Set progress bar color based on signal strength
            if (network.SignalStrengthPercent >= 75)
                ProgressSignal.Foreground = System.Windows.Media.Brushes.Green;
            else if (network.SignalStrengthPercent >= 50)
                ProgressSignal.Foreground = System.Windows.Media.Brushes.Orange;
            else
                ProgressSignal.Foreground = System.Windows.Media.Brushes.Red;

            TxtLinkQuality.Text = $"{network.LinkQuality}%";
            ProgressLinkQuality.Value = network.LinkQuality;

            // Channel & Frequency
            TxtChannel.Text = network.Channel > 0 ? $"Kanal {network.Channel}" : "Unbekannt";
            TxtFrequency.Text = network.Frequency > 0 ? $"{network.Frequency} MHz" : "Unbekannt";

            // Add band information
            string band = "";
            if (network.Frequency >= 2412 && network.Frequency <= 2484)
                band = " (2.4 GHz Band)";
            else if (network.Frequency >= 5160 && network.Frequency <= 5885)
                band = " (5 GHz Band)";
            else if (network.Frequency >= 5955 && network.Frequency <= 7115)
                band = " (6 GHz Band - Wi-Fi 6E)";
            TxtFrequency.Text += band;

            TxtPhyType.Text = network.PhyType;
            TxtBeaconInterval.Text = network.BeaconInterval > 0 ? $"{network.BeaconInterval} ms" : "Unbekannt";

            // Supported Rates
            if (network.SupportedRates != null && network.SupportedRates.Length > 0)
            {
                TxtSupportedRates.Text = string.Join(", ", network.SupportedRates);
            }
            else
            {
                TxtSupportedRates.Text = "Keine Informationen verfügbar";
            }

            // Technical Details
            TxtCapabilities.Text = FormatCapabilities(network.Capabilities);
            TxtRegDomain.Text = network.InRegDomain ? "Ja" : "Nein";
        }

        private string FormatCapabilities(ushort capabilities)
        {
            var caps = new System.Collections.Generic.List<string>();

            if ((capabilities & 0x0001) != 0) caps.Add("ESS");
            if ((capabilities & 0x0002) != 0) caps.Add("IBSS");
            if ((capabilities & 0x0010) != 0) caps.Add("Privacy");
            if ((capabilities & 0x0020) != 0) caps.Add("Short Preamble");
            if ((capabilities & 0x0040) != 0) caps.Add("PBCC");
            if ((capabilities & 0x0080) != 0) caps.Add("Channel Agility");
            if ((capabilities & 0x0100) != 0) caps.Add("Spectrum Mgmt");
            if ((capabilities & 0x0400) != 0) caps.Add("Short Slot Time");
            if ((capabilities & 0x0800) != 0) caps.Add("APSD");
            if ((capabilities & 0x2000) != 0) caps.Add("DSSS-OFDM");
            if ((capabilities & 0x4000) != 0) caps.Add("Delayed BA");
            if ((capabilities & 0x8000) != 0) caps.Add("Immediate BA");

            return caps.Count > 0 ? string.Join(", ", caps) : $"0x{capabilities:X4}";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
