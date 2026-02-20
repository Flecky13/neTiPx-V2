using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Windows.Documents;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace neTiPx
{
    public partial class ConfigWindow : Window
    {
    // --- Gateway-Ping-Timer Steuerung für MainWindow ---
    public void StopAllGatewayPingTimers()
    {
        foreach (var t in _ipTabs)
        {
            try { t.PingTimer?.Stop(); } catch { }
        }
    }

    // Stoppe Gateway-Ping-Timer auch beim Minimieren
    private void ConfigWindow_StateChanged(object? sender, EventArgs e)
    {
        if (this.WindowState == WindowState.Minimized)
        {
            StopAllGatewayPingTimers();
        }
        else if (this.WindowState == WindowState.Normal || this.WindowState == WindowState.Maximized)
        {
            RestartGatewayPingTimerIfIpTabActive();
        }
    }

    public void RestartGatewayPingTimerIfIpTabActive()
    {
        if (this.FindName("TabControlMain") is TabControl tc && tc.SelectedItem is TabItem ti)
        {
            var header = ti.Header?.ToString() ?? string.Empty;
            bool isIpSettingsTab = header.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                  header.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isIpSettingsTab && this.FindName("IpTabsControl") is TabControl ipTabCtrl && ipTabCtrl.SelectedItem is TabItem selectedIpTab)
            {
                var selectedData = _ipTabs.FirstOrDefault(t => t.Tab == selectedIpTab);
                if (selectedData != null)
                {
                    StartGatewayPingTimer(selectedData);
                }
            }
        }
    }
        private readonly List<CheckBox> _checkboxes = new List<CheckBox>();
        private int _suspendEventCount = 0;
        private bool _adapterTabHasChanges = false;
        private bool _pingTabHasChanges = false;
        private bool _allowCloseWithErrors = false;

        private bool EventsSuspended => _suspendEventCount > 0;
        private void EnterSuspendEvents() => _suspendEventCount++;
        private void ExitSuspendEvents() { if (_suspendEventCount > 0) _suspendEventCount--; }

        // Dynamic IP tabs
        private const int MaxIpTabs = 10;
        private readonly List<IpTabData> _ipTabs = new List<IpTabData>();

        // Drag & Drop support for reordering tabs
        private TabItem? _draggedTab = null;
        private Point _dragStartPoint;


        // Ping tools
        private const int MaxPingEntries = 10;
        private const int PingTimeoutMs = 2000;
        private readonly List<PingEntryData> _pingEntries = new List<PingEntryData>();
        private bool _keepPingTimersRunning = false;
        private bool _hasAskedAboutPingTimers = false;

        private class PingEntryData
        {
            public CheckBox? Enabled;
            public TextBox? TxtIp;
            // stats
            public DateTime? StartTime;
            public int SentCount;
            public int MissCount;
            public Label? LblStart;
            public Label? LblMiss;
            public Label? LblLoss;
            public Label? LblStatus;
            public System.Windows.Threading.DispatcherTimer? Timer;
            public Grid? RowPanel;
        }

        private class IpAddressEntry
        {
            public IpTabData? OwnerTab;
            public TextBox? TxtIP;
            public TextBox? TxtSubnet;
            public Button? BtnRemove;
            public Button? BtnUp;
            public Button? BtnDown;
            public Grid? RowPanel;
        }

        private class IpTabData
        {
            public int Index;
            public TabItem? Tab;
            public ComboBox? AdapterCombo;
            public TextBox? TxtName;
            public RadioButton? RbDhcp;
            public RadioButton? RbManual;
            public List<IpAddressEntry> IpEntries = new List<IpAddressEntry>();
            public StackPanel? IpEntriesPanel;
            public Button? BtnAddIp;
            public TextBox? TxtGateway;
            public TextBox? TxtDNS;
            public System.Windows.Threading.DispatcherTimer? PingTimer;
            public Label? LblGwStatus;
            public bool HasChanges = false;
        }

        public ConfigWindow()
        {
            InitializeComponent();
            this.StateChanged += ConfigWindow_StateChanged;
            // Prevent event handlers from reacting during initialization
            EnterSuspendEvents();
            try
            {
                // Load UI state after InitializeComponent
                LoadAdapters();
                LoadIpSettings();
                // Load Tools -> Ping entries
                try { LoadPingSettings(); } catch { }

                // Refresh lists when the user switches tabs so INI is always authoritative
                try
                {
                    if (this.FindName("TabControlMain") is TabControl tc)
                    {
                        tc.SelectionChanged += TabControlMain_SelectionChanged;

                        // Set initial button visibility based on currently selected tab
                        if (tc.SelectedItem is TabItem ti)
                        {
                            var header = ti.Header?.ToString() ?? string.Empty;
                            bool isIpSettingsTab = header.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                  header.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (this.FindName("BtnApply") is Button btnApply)
                            {
                                btnApply.Visibility = isIpSettingsTab ? Visibility.Visible : Visibility.Collapsed;
                            }

                            if (this.FindName("BtnSave") is Button btnSave)
                            {
                                btnSave.Visibility = header.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0
                                    ? Visibility.Collapsed
                                    : Visibility.Visible;
                            }
                        }
                    }
                    // Also attach selection handler for the dynamic IP tabs control
                    try
                    {
                        if (this.FindName("IpTabsControl") is TabControl itc)
                        {
                            itc.SelectionChanged += IpTabsControl_SelectionChanged;
                        }
                    }
                    catch { }
                }
                catch { }
            }
            finally
            {
                ExitSuspendEvents();
            }

            // Restore last selected IP tab AFTER events are enabled
            Dispatcher.BeginInvoke(new Action(() => RestoreLastSelectedIpTabName()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Status message display
        private System.Windows.Threading.DispatcherTimer? _statusTimer;

        private void SetStatusMessage(string message, int autoCloseSeconds = 5)
        {
            if (this.FindName("StatusTextBlock") is TextBlock statusTextBlock)
            {
                statusTextBlock.Text = message;

                // Clear existing timer
                _statusTimer?.Stop();

                if (autoCloseSeconds > 0)
                {
                    // Create new timer to clear message after specified seconds
                    _statusTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(autoCloseSeconds)
                    };
                    _statusTimer.Tick += (s, e) =>
                    {
                        if (this.FindName("StatusTextBlock") is TextBlock stb)
                        {
                            stb.Text = "";
                        }
                        _statusTimer?.Stop();
                    };
                    _statusTimer.Start();
                }
            }
        }


            private async void BtnApply_Click(object sender, RoutedEventArgs e)
            {
                try
                {
                    if (IpTabsControl == null) return;
                    if (!(IpTabsControl.SelectedItem is TabItem ti))
                    {
                        MessageBox.Show("Kein IP-Tab ausgewählt.");
                        return;
                    }
                    var data = _ipTabs.FirstOrDefault(t => t.Tab == ti);
                    if (data == null)
                    {
                        MessageBox.Show("Tab-Daten nicht gefunden.");
                        return;
                    }

                    var adapterKey = (data.AdapterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                    if (string.IsNullOrEmpty(adapterKey))
                    {
                        MessageBox.Show("Kein Adapter im Tab ausgewählt.");
                        return;
                    }

                    // Find network interface by Name or Description or display
                    var ni = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(n => string.Equals(n.Name, adapterKey, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(n.Description, adapterKey, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(n.Name + " - " + n.Description, adapterKey, StringComparison.OrdinalIgnoreCase));
                    if (ni == null)
                    {
                        MessageBox.Show($"Netzwerkadapter '{adapterKey}' nicht gefunden.");
                        return;
                    }

                    // Determine mode
                    bool isDhcp = data.RbDhcp != null && data.RbDhcp.IsChecked == true;

                    var commands = new List<string>();
                    if (isDhcp)
                    {
                        // set DHCP for IP and DNS in a single elevated call
                        commands.Add($"netsh interface ipv4 set address name=\"{ni.Name}\" source=dhcp");
                        commands.Add($"netsh interface ipv4 set dns name=\"{ni.Name}\" source=dhcp");
                    }
                    else
                    {
                        // Get IP entries
                        var ipEntries = data.IpEntries?.Where(e => !string.IsNullOrWhiteSpace(e.TxtIP?.Text)).ToList() ?? new List<IpAddressEntry>();

                        if (ipEntries.Count == 0)
                        {
                            MessageBox.Show("Mindestens eine IP-Adresse ist erforderlich.");
                            return;
                        }

                        var gw = data.TxtGateway?.Text.Trim() ?? string.Empty;
                        var dns = data.TxtDNS?.Text.Trim() ?? string.Empty;

                        // Set first IP
                        var firstIp = ipEntries[0];
                        var ip = firstIp.TxtIP?.Text.Trim() ?? string.Empty;
                        var mask = firstIp.TxtSubnet?.Text.Trim() ?? string.Empty;

                        // Validate IP, Gateway and Subnet
                        var (isValid, errorMsg) = ValidateIpGatewaySubnet(ip, mask, gw);
                        if (!isValid)
                        {
                            MessageBox.Show($"Validierungsfehler:\n\n{errorMsg}");
                            return;
                        }

                        string addrCmd = $"netsh interface ipv4 set address name=\"{ni.Name}\" source=static addr={ip} mask={mask}";
                        if (IsValidIPv4(gw)) addrCmd += $" gateway={gw} gwmetric=1";
                        commands.Add(addrCmd);

                        // Add additional IPs
                        for (int i = 1; i < ipEntries.Count; i++)
                        {
                            var additionalIp = ipEntries[i].TxtIP?.Text.Trim() ?? string.Empty;
                            var additionalMask = ipEntries[i].TxtSubnet?.Text.Trim() ?? string.Empty;

                            if (!string.IsNullOrEmpty(additionalIp))
                            {
                                // Validate additional IP
                                var (additionalValid, additionalError) = ValidateIpGatewaySubnet(additionalIp, additionalMask, string.Empty);
                                if (!additionalValid)
                                {
                                    MessageBox.Show($"Validierungsfehler in IP #{i + 1}:\n\n{additionalError}");
                                    return;
                                }
                                commands.Add($"netsh interface ipv4 add address name=\"{ni.Name}\" addr={additionalIp} mask={additionalMask}");
                            }
                        }

                        if (IsValidIPv4(dns))
                        {
                            commands.Add($"netsh interface ipv4 set dns name=\"{ni.Name}\" source=static addr={dns} register=primary");
                        }
                    }

                    // Execute all netsh commands in one elevated process to avoid multiple UAC prompts
                    if (commands.Count > 0)
                    {
                        if (!RunNetshCommandsElevated(commands))
                        {
                            MessageBox.Show("Fehler beim Anwenden der Netzwerkeinstellungen.");
                            return;
                        }
                    }

                    // On success: mark tab as applied and persist
                    try { if (data.Tab != null) data.Tab.Foreground = Brushes.Green; } catch { }
                    // Reset other tabs with same adapter to default black
                    foreach (var other in _ipTabs)
                    {
                        if (other == data) continue;
                        var otherAdapter = (other.AdapterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(adapterKey) && string.Equals(adapterKey, otherAdapter, StringComparison.OrdinalIgnoreCase))
                        {
                            try { if (other.Tab != null) other.Tab.Foreground = Brushes.Black; } catch { }
                        }
                    }

                    // Save settings so applied state and values persist
                    try
                    {
                        var iniPath = ConfigFileHelper.GetConfigIniPath();
                        var values = ReadIniToDict(iniPath);
                        SaveIpSettings(values);

                        // Reset change markers after successful save
                        foreach (var t in _ipTabs)
                        {
                            t.HasChanges = false;
                            if (t.Tab != null && t.Tab.Header is string header)
                            {
                                t.Tab.Header = header.Replace(" *", "");
                            }
                        }
                    }
                    catch { }

                    // Show success status message
                    SetStatusMessage("Einstellungen erfolgreich angewendet.", 5);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Fehler beim Anwenden: " + ex.Message);
                }
            }

            private bool RunNetshCommandsElevated(IEnumerable<string> commands)
            {
                try
                {
                    // Join commands with & so cmd.exe executes them sequentially in one elevated process
                    var cmdLine = string.Join(" & ", commands);
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c " + cmdLine,
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) return false;
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // User probably cancelled UAC
                    MessageBox.Show("Berechtigung erforderlich: Starte Anwendung mit Administratorrechten oder bestätige die UAC-Eingabe.");
                    return false;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Fehler beim Ausführen von netsh: " + ex.Message);
                    return false;
                }
            }
        private void LoadAdapters()
        {
            try
            {
                // clear previous controls if reloading
                _checkboxes.Clear();
                if (AdaptersPanel != null) AdaptersPanel.Children.Clear();

                // Read Adapter selections from INI (Authoritative)
                string iniPfad = ConfigFileHelper.GetConfigIniPath();
                var ini = ReadIniToDict(iniPfad);
                string? sel1 = null, sel2 = null;
                if (ini.TryGetValue("Adapter1", out var v1)) sel1 = v1;
                if (ini.TryGetValue("Adapter2", out var v2)) sel2 = v2;

                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(a => a.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(a => a.GetPhysicalAddress() != null && a.GetPhysicalAddress().GetAddressBytes().Length > 0)
                    .ToList();

                EnterSuspendEvents();
                try
                {
                    foreach (var a in adapters)
                    {
                        string display = a.Name + " - " + a.Description;
                        var cb = new CheckBox { Content = display, Tag = a.Name, Margin = new Thickness(4) };
                        // Match INI value flexibly against Name, Description or the displayed string
                        string displayToCompare = display;
                        if (!string.IsNullOrEmpty(sel1))
                        {
                            var s = sel1.Trim();
                            if (s.Equals(a.Name, StringComparison.OrdinalIgnoreCase) || s.Equals(a.Description, StringComparison.OrdinalIgnoreCase) || s.Equals(displayToCompare, StringComparison.OrdinalIgnoreCase)) cb.IsChecked = true;
                        }
                        if (!string.IsNullOrEmpty(sel2))
                        {
                            var s = sel2.Trim();
                            if (s.Equals(a.Name, StringComparison.OrdinalIgnoreCase) || s.Equals(a.Description, StringComparison.OrdinalIgnoreCase) || s.Equals(displayToCompare, StringComparison.OrdinalIgnoreCase)) cb.IsChecked = true;
                        }
                        cb.Checked += Cb_CheckedChanged;
                        cb.Unchecked += Cb_CheckedChanged;
                        _checkboxes.Add(cb);
                        if (AdaptersPanel != null) AdaptersPanel.Children.Add(cb);
                    }
                }
                finally
                {
                    ExitSuspendEvents();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Laden der Adapter: " + ex.Message);
            }
        }

        private void TabControlMain_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (EventsSuspended) return;
            try
            {
                // Check if we're leaving the Adapter tab and if there are changes
                if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is TabItem oldTab)
                {
                    var oldHeader = oldTab.Header?.ToString() ?? string.Empty;
                    bool wasAdapterTab = oldHeader.IndexOf("Adapter", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool wasIpSettingsTab = oldHeader.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                           oldHeader.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0;

                    // Check Adapter Tab for changes
                    if (wasAdapterTab && _adapterTabHasChanges)
                    {
                        var result = MessageBox.Show(
                            "Sie haben Änderungen vorgenommen.\n\nMöchten Sie diese speichern?",
                            "Änderungen speichern?",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            // Save changes
                            try
                            {
                                var selected = _checkboxes.Where(c => c.IsChecked == true).Select(c => c.Tag?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                                var iniPath = ConfigFileHelper.GetConfigIniPath();
                                var values = ReadIniToDict(iniPath);

                                // Save adapter selections
                                values["Adapter1"] = (selected.Count > 0 ? (selected[0] ?? string.Empty) : string.Empty);
                                values["Adapter2"] = (selected.Count > 1 ? (selected[1] ?? string.Empty) : string.Empty);

                                SaveIpSettings(values);
                                _adapterTabHasChanges = false;
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Fehler beim Speichern: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Discard changes
                            _adapterTabHasChanges = false;
                        }
                    }

                    // Check IP Settings Tab for changes
                    if (wasIpSettingsTab)
                    {
                        // Check if any tab has changes
                        bool hasChanges = _ipTabs.Any(t => t.Tab?.Header?.ToString()?.Contains("*") == true) || _ipTabs.Any(t => t.HasChanges);
                        if (hasChanges)
                        {
                            var result = MessageBox.Show(
                                "Sie haben Änderungen vorgenommen.\n\nMöchten Sie diese speichern?",
                                "Änderungen speichern?",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                // Save changes
                                try
                                {
                                    var iniPath = ConfigFileHelper.GetConfigIniPath();
                                    var values = ReadIniToDict(iniPath);
                                    SaveIpSettings(values);
                                    // Reset change markers
                                    foreach (var t in _ipTabs)
                                    {
                                        t.HasChanges = false;
                                        if (t.Tab != null && t.Tab.Header is string header)
                                        {
                                            t.Tab.Header = header.Replace(" *", "");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Fehler beim Speichern: {ex.Message}");
                                }
                            }
                            else
                            {
                                // Discard changes - reset markers
                                foreach (var t in _ipTabs)
                                {
                                    t.HasChanges = false;
                                    if (t.Tab != null && t.Tab.Header is string header)
                                    {
                                        t.Tab.Header = header.Replace(" *", "");
                                    }
                                }
                            }
                        }
                    }
                }

                // Check if we're leaving the Tools/Ping tab and if there are changes
                if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is TabItem removedTab)
                {
                    var removedHeader = removedTab.Header?.ToString() ?? string.Empty;
                    bool wasToolsTab = removedHeader.IndexOf("Tools", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (wasToolsTab && _pingTabHasChanges)
                    {
                        var result = MessageBox.Show(
                            "Sie haben Änderungen vorgenommen.\n\nMöchten Sie diese speichern?",
                            "Änderungen speichern?",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            // Save changes
                            try
                            {
                                var iniPath = ConfigFileHelper.GetConfigIniPath();
                                var values = ReadIniToDict(iniPath);
                                SaveIpSettings(values);
                                _pingTabHasChanges = false;
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Fehler beim Speichern: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Discard changes
                            _pingTabHasChanges = false;
                        }
                    }
                }

                // Only handle if the event originates from the TabControl itself
                if (e.OriginalSource != sender) return;
                if (sender is TabControl tc && tc.SelectedItem is TabItem ti)
                {
                    var header = ti.Header?.ToString() ?? string.Empty;
                    bool isIpSettingsTab = header.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                          header.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (this.FindName("BtnApply") is Button btnApply)
                    {
                        btnApply.Visibility = isIpSettingsTab ? Visibility.Visible : Visibility.Collapsed;
                    }

                    // Hide "Speichern" button in Info tab
                    if (this.FindName("BtnSave") is Button btnSave)
                    {
                        btnSave.Visibility = header.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    }

                    // Stop all IP tab gateway ping timers when leaving IP Settings tab
                    if (!isIpSettingsTab)
                    {
                        foreach (var t in _ipTabs)
                        {
                            try { t.PingTimer?.Stop(); } catch { }
                        }
                    }

                    // Check if leaving Tools/Ping tab
                    bool isToolsTab = header.IndexOf("Tools", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isToolsTab && !_hasAskedAboutPingTimers)
                    {
                        // Check if any ping timers are running
                        bool anyPingRunning = _pingEntries.Any(p => p.Timer != null && p.Timer.IsEnabled);
                        if (anyPingRunning)
                        {
                            // Only ask if background mode is enabled
                            if (_keepPingTimersRunning)
                            {
                                _hasAskedAboutPingTimers = true;
                                var result = MessageBox.Show(
                                    "Soll die Erreichbarkeitsprüfung (Ping) im Hintergrund weiterlaufen?",
                                    "Ping-Überwachung",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question);
                                _keepPingTimersRunning = (result == MessageBoxResult.Yes);

                                // Update checkbox to reflect user decision
                                UpdatePingBackgroundCheckbox();
                            }

                            if (!_keepPingTimersRunning)
                            {
                                // Stop all ping timers
                                foreach (var p in _pingEntries)
                                {
                                    try { p.Timer?.Stop(); } catch { }
                                }
                            }
                        }
                    }
                    else if (isToolsTab)
                    {
                        // Reset flag when returning to Tools tab
                        _hasAskedAboutPingTimers = false;

                        // Always restart ping timers for enabled entries when returning to tab
                        foreach (var p in _pingEntries)
                        {
                            if (p.Enabled != null && p.Enabled.IsChecked == true && p.Timer != null && !p.Timer.IsEnabled)
                            {
                                try { p.Timer.Start(); } catch { }
                            }
                        }

                        // Update checkbox status
                        UpdatePingBackgroundCheckbox();
                    }

                    if (header.IndexOf("Adapter", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadAdapters();
                    }
                    else if (isIpSettingsTab)
                    {
                        LoadIpSettings();
                        // Restart timer for currently selected IP tab after loading
                        if (this.FindName("IpTabsControl") is TabControl ipTabCtrl && ipTabCtrl.SelectedItem is TabItem selectedIpTab)
                        {
                            var selectedData = _ipTabs.FirstOrDefault(t => t.Tab == selectedIpTab);
                            if (selectedData != null)
                            {
                                StartGatewayPingTimer(selectedData);
                            }
                        }
                    }
                    else if (header.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Populate Info tab content when selected
                        InitializeInfoPage();
                    }
                }
            }
            catch { }
        }

        private void Cb_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            // Limit to max 2 checked
            var checkedCount = _checkboxes.Count(c => c.IsChecked == true);
            if (checkedCount > 2)
            {
                // Uncheck the last changed
                if (sender is CheckBox cb)
                {
                    EnterSuspendEvents();
                    try { cb.IsChecked = false; }
                    finally { ExitSuspendEvents(); }
                }
                MessageBox.Show("Maximal 2 Adapter können ausgewählt werden.");
            }

            // Mark adapter tab as changed
            if (!EventsSuspended)
            {
                _adapterTabHasChanges = true;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (HasValidationErrors())
            {
                MessageBox.Show("Es liegen Fehler in den IP-Adressen vor. Speichern ist aktuell nicht moeglich.");
                return;
            }
            var selected = _checkboxes.Where(c => c.IsChecked == true).Select(c => c.Tag?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();

            try
            {
                // Read existing INI values, update Adapter1/Adapter2 and then persist along with IP-settings
                var iniPath = ConfigFileHelper.GetConfigIniPath();
                var values = ReadIniToDict(iniPath);

                values["Adapter1"] = (selected.Count > 0 ? (selected[0] ?? string.Empty) : string.Empty);
                values["Adapter2"] = (selected.Count > 1 ? (selected[1] ?? string.Empty) : string.Empty);

                // Save IP settings into the same dictionary and persist
                SaveIpSettings(values);

                // Reset changes flag for all tabs after successful save
                foreach (var t in _ipTabs)
                {
                    t.HasChanges = false;
                    // Remove asterisk from tab header
                    if (t.Tab != null && t.Tab.Header is string header)
                    {
                        t.Tab.Header = header.Replace(" *", "");
                    }
                }

                // Reset adapter tab changes flag
                _adapterTabHasChanges = false;

                // Reset ping tab changes flag
                _pingTabHasChanges = false;

                // Save the currently selected IP tab position
                if (IpTabsControl?.SelectedItem is TabItem selectedTab)
                {
                    var selectedData = _ipTabs.FirstOrDefault(t => t.Tab == selectedTab);
                    if (selectedData != null)
                    {
                        SaveLastSelectedIpTabName(selectedData);
                    }
                }

                // If opened from MainWindow, refresh its display immediately
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        mw.UpdateGui();
                    }
                }
                catch { }

                // Show success status message
                SetStatusMessage("Konfiguration erfolgreich gespeichert.", 5);

                // Close the config window after saving
                //this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern: " + ex.Message);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (HasValidationErrors())
            {
                var result = MessageBox.Show(
                    "Es liegen Fehler in den IP-Adressen vor.\n\nOhne Speichern schliessen?",
                    "Fehler vorhanden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _allowCloseWithErrors = true;
                    this.Close();
                }
                return;
            }

            // Check if any IP tab has changes or adapter tab has changes or ping tab has changes
            bool hasIpChanges = _ipTabs.Any(t => t.Tab?.Header?.ToString()?.Contains("*") == true) || _ipTabs.Any(t => t.HasChanges);
            bool hasAdapterChanges = _adapterTabHasChanges;
            bool hasPingChanges = _pingTabHasChanges;
            bool hasChanges = hasIpChanges || hasAdapterChanges || hasPingChanges;

            if (hasChanges)
            {
                var result = MessageBox.Show(
                    "Sie haben Änderungen vorgenommen.\n\nMöchten Sie diese speichern, bevor Sie das Fenster schliessen?",
                    "Änderungen speichern?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Save changes
                    try
                    {
                        var selected = _checkboxes.Where(c => c.IsChecked == true).Select(c => c.Tag?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        var iniPath = ConfigFileHelper.GetConfigIniPath();
                        var values = ReadIniToDict(iniPath);

                        // Save adapter selections
                        values["Adapter1"] = (selected.Count > 0 ? (selected[0] ?? string.Empty) : string.Empty);
                        values["Adapter2"] = (selected.Count > 1 ? (selected[1] ?? string.Empty) : string.Empty);

                        SaveIpSettings(values);

                        // Reset change markers
                        foreach (var t in _ipTabs)
                        {
                            t.HasChanges = false;
                            if (t.Tab != null && t.Tab.Header is string header)
                            {
                                t.Tab.Header = header.Replace(" *", "");
                            }
                        }
                        _adapterTabHasChanges = false;
                        _pingTabHasChanges = false;

                        // Save the currently selected IP tab position
                        if (IpTabsControl?.SelectedItem is TabItem selectedTab)
                        {
                            var selectedData = _ipTabs.FirstOrDefault(t => t.Tab == selectedTab);
                            if (selectedData != null)
                            {
                                SaveLastSelectedIpTabName(selectedData);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Fehler beim Speichern: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    // Discard changes - reset markers
                    foreach (var t in _ipTabs)
                    {
                        t.HasChanges = false;
                        if (t.Tab != null && t.Tab.Header is string header)
                        {
                            t.Tab.Header = header.Replace(" *", "");
                        }
                    }
                    _adapterTabHasChanges = false;
                    _pingTabHasChanges = false;
                    _adapterTabHasChanges = false;
                }
            }

            this.Close();
        }

        private void InitializeInfoPage()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version?.ToString() ?? "?";

            // Version Text setzen
            if (this.FindName("InfoVersionText") is TextBlock vt)
            {
                vt.Text = "Version: " + ver;
            }

            // Rechte Spalte: Hauptinformationen
            if (this.FindName("InfoText") is TextBlock tb)
            {
                tb.Inlines.Clear();
                tb.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                tb.FontSize = 14;

                tb.Inlines.Add(new Run("neTiPx - Netzwerk Infos") { FontWeight = System.Windows.FontWeights.Bold, FontSize = 16 });
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run("Ein leichtgewichtiges Windows-Netzwerk-Informations- und Konfigurations-Tool"));
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new LineBreak());

                tb.Inlines.Add(new Run("Beschreibung:") { FontWeight = System.Windows.FontWeights.Bold });
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run("neTiPx zeigt Informationen über aktive Netzwerkadapter im Tray an und ermöglicht die einfache Verwaltung von IP-Profilen für schnelle Netzwerkumschaltungen."));
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new LineBreak());

                tb.Inlines.Add(new Run("Hauptfunktionen:") { FontWeight = System.Windows.FontWeights.Bold });
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run("• Netzwerk-Übersicht mit externen/lokalen IPs\n• Verwaltung mehrerer IP-Profile pro Adapter\n• Manuell oder DHCP Konfiguration\n• Mehrere IPs pro Profil\n• Intelligente IP-Validierung\n• Ping-Monitor (bis zu 10 Einträge)\n• WiFi-Netzwerk-Scanner mit Details"));
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new LineBreak());

                tb.Inlines.Add(new Run("Lizenz:") { FontWeight = System.Windows.FontWeights.Bold });
                tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run("Siehe LICENSE im GitHub-Repository"));
            }

            // Linke Spalte unten: Autor, Repository, Support
            if (this.FindName("InfoLeftBottom") is StackPanel leftPanel)
            {
                leftPanel.Children.Clear();

                // Autor
                var autorLabel = new TextBlock
                {
                    Text = "Autor:",
                    FontWeight = System.Windows.FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                leftPanel.Children.Add(autorLabel);

                var email = "github@hometepe.de";
                var mailText = "Flecky13 (Pedro Tepe)";
                var autorTextBlock = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 12), TextWrapping = System.Windows.TextWrapping.Wrap };
                var mailLink = new Hyperlink(new Run(mailText)) { NavigateUri = new Uri("mailto:" + email) };
                mailLink.Click += (s2, e2) =>
                {
                    try { Process.Start(new ProcessStartInfo(mailLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                };
                autorTextBlock.Inlines.Add(mailLink);
                leftPanel.Children.Add(autorTextBlock);

                // Repository
                var repoLabel = new TextBlock
                {
                    Text = "Repository:",
                    FontWeight = System.Windows.FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                leftPanel.Children.Add(repoLabel);

                var repoUrl = "https://github.com/Flecky13/neTiPx-V2";
                var repoTextBlock = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 12), TextWrapping = System.Windows.TextWrapping.Wrap };
                var repoLink = new Hyperlink(new Run("GitHub")) { NavigateUri = new Uri(repoUrl) };
                repoLink.Click += (s3, e3) =>
                {
                    try { Process.Start(new ProcessStartInfo(repoLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                };
                repoTextBlock.Inlines.Add(repoLink);
                leftPanel.Children.Add(repoTextBlock);

                // Support
                var supportLabel = new TextBlock
                {
                    Text = "Support:",
                    FontWeight = System.Windows.FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                leftPanel.Children.Add(supportLabel);

                var supportUrl = "https://buymeacoffee.com/pedrotepe";
                var supportTextBlock = new TextBlock { FontSize = 11, TextWrapping = System.Windows.TextWrapping.Wrap };
                var supportLink = new Hyperlink(new Run("Buy Me a Coffee")) { NavigateUri = new Uri(supportUrl), Foreground = System.Windows.Media.Brushes.DodgerBlue };
                supportLink.Click += (s4, e4) =>
                {
                    try { Process.Start(new ProcessStartInfo(supportLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                };
                supportTextBlock.Inlines.Add(supportLink);
                leftPanel.Children.Add(supportTextBlock);
            }

            // Icon laden
            if (this.FindName("InfoImage") is Image img)
            {
                try
                {
                    var imgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "toolicon_transparent.png");
                    if (System.IO.File.Exists(imgPath))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        img.Source = bmp;
                    }
                }
                catch { }
            }
        }

        private void LoadIpSettings()
        {
            // Load all IP tabs from INI
            try
            {
                if (IpTabsControl == null) return;
                EnterSuspendEvents();
                try
                {
                    // clear existing dynamic tabs
                    _ipTabs.Clear();
                    IpTabsControl.Items.Clear();

                    var iniPath = ConfigFileHelper.GetConfigIniPath();
                    var values = ReadIniToDict(iniPath);

                    int created = 0;

                    // Preferred format: IpProfileNames = name1,name2,... and keys like name.Adapter
                    if (values.TryGetValue("IpProfileNames", out var profileList) && !string.IsNullOrWhiteSpace(profileList))
                    {
                        var names = profileList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                        int idx = 1;
                        foreach (var nm in names)
                        {
                            var data = CreateIpTab(idx++, values, nm);
                            _ipTabs.Add(data);
                            IpTabsControl.Items.Add(data.Tab);
                            created++;
                        }
                    }
                    else
                    {
                        // Fallback to legacy IpTabN keys
                        for (int i = 1; i <= MaxIpTabs; i++)
                        {
                            string prefix = $"IpTab{i}"; // keys: IpTab{n}Adapter, IpTab{n}Mode, IpTab{n}IP, ...
                            if (values.ContainsKey(prefix + "Adapter") || values.ContainsKey(prefix + "Mode") || values.ContainsKey(prefix + "IP"))
                            {
                                var data = CreateIpTab(i, values, null);
                                _ipTabs.Add(data);
                                IpTabsControl.Items.Add(data.Tab);
                                created++;
                            }
                        }
                    }

                    // If none found, create one default tab
                    if (created == 0)
                    {
                        var data = CreateIpTab(1, values, null);
                        _ipTabs.Add(data);
                        IpTabsControl.Items.Add(data.Tab);
                    }
                }
                finally { ExitSuspendEvents(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Laden der IP-Einstellungen: " + ex.Message);
            }
        }

        private IpTabData CreateIpTab(int index, Dictionary<string, string> iniValues, string? profileName = null)
        {
            var data = new IpTabData();
            data.Index = index;

            // Main container with scrolling
            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel { Margin = new Thickness(6) };
            scrollViewer.Content = panel;

            // Name (editable) - used as profile/section name in INI
            // Top row: name on left, GW status on right
            var spTop = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            spTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            spTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var spName = new StackPanel { Orientation = Orientation.Horizontal };
            spName.Children.Add(new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var txtName = new TextBox { Width = 160 };
            spName.Children.Add(txtName);
            Grid.SetColumn(spName, 0);
            spTop.Children.Add(spName);

            // Gateway status label (top-right)
            var lblGwStatus = new Label { Content = "GW: -", Background = Brushes.LightGray, Padding = new Thickness(6,2,6,2), Margin = new Thickness(8,0,0,0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(lblGwStatus, 1);
            spTop.Children.Add(lblGwStatus);

            panel.Children.Add(spTop);

            // Adapter selection
            var spAdapter = new StackPanel { Orientation = Orientation.Horizontal };
            spAdapter.Children.Add(new TextBlock { Text = "Adapter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var cbAdapter = new ComboBox { Width = 320 };
            spAdapter.Children.Add(cbAdapter);
            panel.Children.Add(spAdapter);

            // Mode
            var spMode = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
            spMode.Children.Add(new TextBlock { Text = "Modus:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var rbDhcp = new RadioButton { Content = "DHCP", Margin = new Thickness(0, 0, 8, 0) };
            var rbManual = new RadioButton { Content = "Manuell" };
            spMode.Children.Add(rbDhcp);
            spMode.Children.Add(rbManual);
            panel.Children.Add(spMode);

            // Gateway & DNS (top section)
            var borderGwDns = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 8) };
            var gridGwDns = new Grid();
            gridGwDns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gridGwDns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridGwDns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gridGwDns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Gateway
            var lblGw = new TextBlock { Text = "Gateway:", Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lblGw, 0); Grid.SetColumn(lblGw, 0); gridGwDns.Children.Add(lblGw);
            var txtGw = new TextBox { Margin = new Thickness(6) };
            Grid.SetRow(txtGw, 0); Grid.SetColumn(txtGw, 1); gridGwDns.Children.Add(txtGw);

            // Row 1: DNS
            var lblDns = new TextBlock { Text = "DNS:", Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lblDns, 1); Grid.SetColumn(lblDns, 0); gridGwDns.Children.Add(lblDns);
            var txtDns = new TextBox { Margin = new Thickness(6) };
            Grid.SetRow(txtDns, 1); Grid.SetColumn(txtDns, 1); gridGwDns.Children.Add(txtDns);

            borderGwDns.Child = gridGwDns;
            panel.Children.Add(borderGwDns);

            // IP Addresses section
            var spIpSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            var spIpHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            spIpHeader.Children.Add(new TextBlock { Text = "IP-Adressen & Subnetze:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            var btnAddIp = new Button { Content = "➕ Hinzufügen", Width = 120, Margin = new Thickness(8, 0, 0, 0) };
            spIpHeader.Children.Add(btnAddIp);
            spIpSection.Children.Add(spIpHeader);

            // IP Entries container with header
            var borderIpEntries = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), CornerRadius = new CornerRadius(4) };
            var gridIpHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            gridIpHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridIpHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridIpHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            gridIpHeader.Children.Add(new TextBlock { Text = "IP-Adresse", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetColumn(gridIpHeader.Children[gridIpHeader.Children.Count - 1], 0);

            gridIpHeader.Children.Add(new TextBlock { Text = "Subnetz", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetColumn(gridIpHeader.Children[gridIpHeader.Children.Count - 1], 1);

            gridIpHeader.Children.Add(new TextBlock { Text = "Aktion", FontWeight = FontWeights.Bold });
            Grid.SetColumn(gridIpHeader.Children[gridIpHeader.Children.Count - 1], 2);

            var ipEntriesPanel = new StackPanel();
            data.IpEntriesPanel = ipEntriesPanel;

            var spIpEntriesContainer = new StackPanel();
            spIpEntriesContainer.Children.Add(gridIpHeader);
            spIpEntriesContainer.Children.Add(ipEntriesPanel);

            borderIpEntries.Child = spIpEntriesContainer;
            spIpSection.Children.Add(borderIpEntries);
            panel.Children.Add(spIpSection);

            var tab = new TabItem { Header = $"IP #{index}", Content = scrollViewer };

            // Apply custom style to prevent binding errors when TabItem is created without parent TabControl
            if (this.TryFindResource("DynamicTabItemStyle") is Style tabItemStyle)
            {
                tab.Style = tabItemStyle;
            }

            // Populate adapters from global INI Adapter1/Adapter2
            if (iniValues.TryGetValue("Adapter1", out var a1) && !string.IsNullOrEmpty(a1)) cbAdapter.Items.Add(new ComboBoxItem { Content = a1, Tag = a1 });
            if (iniValues.TryGetValue("Adapter2", out var a2) && !string.IsNullOrEmpty(a2)) cbAdapter.Items.Add(new ComboBoxItem { Content = a2, Tag = a2 });
            if (cbAdapter.Items.Count > 0) cbAdapter.SelectedIndex = 0;

            // Wire events
            cbAdapter.SelectionChanged += (s, e) => { if (!EventsSuspended) { MarkIpTabAsChanged(data); if (rbManual.IsChecked == true) { LoadSystemValuesIntoTab(data); } else if (rbDhcp.IsChecked == true) { LoadSystemValuesIntoTab(data); } } };
            rbDhcp.Checked += (s, e) =>
            {
                if (!EventsSuspended)
                {
                    MarkIpTabAsChanged(data);
                    SetIpFieldsEnabledForTab(data, false);
                    btnAddIp.IsEnabled = false;
                    LoadSystemValuesIntoTab(data);
                    UpdateIpEntryValidationForTab(data);
                    UpdateSaveApplyButtonsState();
                }
            };
            rbManual.Checked += (s, e) =>
            {
                if (!EventsSuspended)
                {
                    MarkIpTabAsChanged(data);
                    SetIpFieldsEnabledForTab(data, true);
                    btnAddIp.IsEnabled = true;
                    LoadSystemValuesIntoTab(data);
                    UpdateIpEntryValidationForTab(data);
                    UpdateSaveApplyButtonsState();
                }
            };
            btnAddIp.Click += (s, e) =>
            {
                if (data.IpEntries != null && data.IpEntries.Count > 0)
                {
                    var lastEntry = data.IpEntries[data.IpEntries.Count - 1];
                    if (!TryValidateIpEntry(lastEntry, allowEmpty: false, out var errorMsg))
                    {
                        MessageBox.Show(errorMsg);
                        return;
                    }
                }
                MarkIpTabAsChanged(data);
                AddIpEntryToTab(data);
                UpdateSaveApplyButtonsState();
            };
            txtName.TextChanged += (s, e) => { if (!EventsSuspended) MarkIpTabAsChanged(data); };
            txtGw.TextChanged += (s, e) => { if (!EventsSuspended) MarkIpTabAsChanged(data); };
            txtDns.TextChanged += (s, e) => { if (!EventsSuspended) MarkIpTabAsChanged(data); };

            data.Tab = tab;
            data.AdapterCombo = cbAdapter;
            data.RbDhcp = rbDhcp;
            data.RbManual = rbManual;
            data.BtnAddIp = btnAddIp;
            data.TxtGateway = txtGw;
            data.TxtDNS = txtDns;
            data.TxtName = txtName;
            data.LblGwStatus = lblGwStatus;

            // Initialize with any existing values: prefer profileName-based keys (profile.Key), otherwise legacy IpTab{index}
            string profileKey = profileName ?? string.Empty;
            bool loaded = false;
            if (!string.IsNullOrEmpty(profileKey))
            {
                if (iniValues.TryGetValue(profileKey + ".Adapter", out var pa) && !string.IsNullOrEmpty(pa))
                {
                    for (int i = 0; i < cbAdapter.Items.Count; i++)
                    {
                        if ((cbAdapter.Items[i] as ComboBoxItem)?.Content?.ToString()?.Equals(pa, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            cbAdapter.SelectedIndex = i; break;
                        }
                    }
                    loaded = true;
                }
                bool isDhcpMode = false;
                if (iniValues.TryGetValue(profileKey + ".Mode", out var pm) && pm.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    rbManual.IsChecked = true; SetIpFieldsEnabledForTab(data, true); loaded = true;
                }
                else if (iniValues.TryGetValue(profileKey + ".Mode", out var _))
                {
                    rbDhcp.IsChecked = true; SetIpFieldsEnabledForTab(data, false); loaded = true; isDhcpMode = true;
                }
                if (iniValues.TryGetValue(profileKey + ".GW", out var pgw)) txtGw.Text = pgw;
                if (iniValues.TryGetValue(profileKey + ".DNS", out var pdns)) txtDns.Text = pdns;

                // Load multiple IP/Subnet entries
                int ipCount = 1;
                while (iniValues.TryGetValue($"{profileKey}.IP_{ipCount}", out var pip) && !string.IsNullOrEmpty(pip))
                {
                    iniValues.TryGetValue($"{profileKey}.Subnet_{ipCount}", out var psn);
                    AddIpEntryToTab(data, pip, psn ?? string.Empty);
                    ipCount++;
                }

                // If DHCP mode and no saved IP entries, load current system values
                if (isDhcpMode && ipCount == 1)
                {
                    if (iniValues.TryGetValue(profileKey + ".Adapter", out var adapterName) && !string.IsNullOrEmpty(adapterName))
                    {
                        var sys = GetSystemIpv4Settings(adapterName);
                        if (sys != null)
                        {
                            AddIpEntryToTab(data, sys.Value.ip ?? string.Empty, sys.Value.subnet ?? string.Empty);
                        }
                        else
                        {
                            AddIpEntryToTab(data); // At least one empty entry
                        }
                    }
                    else
                    {
                        AddIpEntryToTab(data); // At least one empty entry
                    }
                }
                else if (ipCount == 1)
                {
                    AddIpEntryToTab(data); // At least one empty entry
                }
            }

            if (!loaded)
            {
                string prefix = $"IpTab{index}";
                if (iniValues.TryGetValue(prefix + "Adapter", out var ia) && !string.IsNullOrEmpty(ia))
                {
                    for (int i = 0; i < cbAdapter.Items.Count; i++)
                    {
                        if ((cbAdapter.Items[i] as ComboBoxItem)?.Content?.ToString()?.Equals(ia, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            cbAdapter.SelectedIndex = i; break;
                        }
                    }
                }

                bool isLegacyDhcp = false;
                if (iniValues.TryGetValue(prefix + "Mode", out var im) && im.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    rbManual.IsChecked = true; SetIpFieldsEnabledForTab(data, true);
                }
                else { rbDhcp.IsChecked = true; SetIpFieldsEnabledForTab(data, false); isLegacyDhcp = true; }

                if (iniValues.TryGetValue(prefix + "GW", out var gw)) txtGw.Text = gw;
                if (iniValues.TryGetValue(prefix + "DNS", out var dns)) txtDns.Text = dns;

                // Legacy: single IP/Subnet
                if (iniValues.TryGetValue(prefix + "IP", out var ip) || iniValues.TryGetValue(prefix + "Subnet", out var sn))
                {
                    AddIpEntryToTab(data,
                        iniValues.TryGetValue(prefix + "IP", out var legacyIp) ? legacyIp : "",
                        iniValues.TryGetValue(prefix + "Subnet", out var legacySn) ? legacySn : "");
                }
                else if (isLegacyDhcp)
                {
                    // DHCP mode with no saved values: load current system values
                    if (iniValues.TryGetValue(prefix + "Adapter", out var adapterName) && !string.IsNullOrEmpty(adapterName))
                    {
                        var sys = GetSystemIpv4Settings(adapterName);
                        if (sys != null)
                        {
                            AddIpEntryToTab(data, sys.Value.ip ?? string.Empty, sys.Value.subnet ?? string.Empty);
                        }
                        else
                        {
                            AddIpEntryToTab(data); // At least one empty entry
                        }
                    }
                    else
                    {
                        AddIpEntryToTab(data); // At least one empty entry
                    }
                }
                else
                {
                    AddIpEntryToTab(data); // At least one empty entry
                }
            }

            // Name handling: set after load so profileKey is updated correctly
            string usedName = profileName ?? string.Empty;
            if (string.IsNullOrEmpty(usedName))
            {
                string legacyPrefix = $"IpTab{index}";
                if (iniValues.TryGetValue(legacyPrefix + "Adapter", out var _)
                    || iniValues.TryGetValue(legacyPrefix + "Mode", out var _)
                    || iniValues.TryGetValue(legacyPrefix + "IP", out var _))
                {
                    usedName = legacyPrefix;
                }
            }
            if (string.IsNullOrEmpty(usedName)) usedName = $"IP #{index}";
            txtName.Text = usedName;
            tab.Header = usedName;

            // Update header when name changes
            txtName.TextChanged += (s, e) =>
            {
                var newName = txtName.Text.Trim();
                if (string.IsNullOrEmpty(newName)) newName = $"IP #{data.Index}";
                tab.Header = newName;
            };

            // Enable Drag & Drop for tab reordering
            SetupTabDragDrop(tab);

            return data;
        }

        private void SetupTabDragDrop(TabItem tab)
        {
            tab.AllowDrop = true;
            tab.PreviewMouseLeftButtonDown += TabItem_PreviewMouseLeftButtonDown;
            tab.PreviewMouseMove += TabItem_PreviewMouseMove;
            tab.DragOver += TabItem_DragOver;
            tab.Drop += TabItem_Drop;
        }

        private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TabItem tabItem)
            {
                _dragStartPoint = e.GetPosition(null);
            }
        }

        private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is TabItem tabItem)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                // Start drag if moved enough
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _draggedTab = tabItem;
                    DragDrop.DoDragDrop(tabItem, tabItem, DragDropEffects.Move);
                    _draggedTab = null;
                }
            }
        }

        private void TabItem_DragOver(object sender, DragEventArgs e)
        {
            if (_draggedTab != null && sender is TabItem targetTab && _draggedTab != targetTab)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TabItem_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (_draggedTab != null && sender is TabItem targetTab && _draggedTab != targetTab && IpTabsControl != null)
                {
                    // Find positions
                    int draggedIndex = IpTabsControl.Items.IndexOf(_draggedTab);
                    int targetIndex = IpTabsControl.Items.IndexOf(targetTab);

                    if (draggedIndex >= 0 && targetIndex >= 0)
                    {
                        // Reorder in TabControl
                        IpTabsControl.Items.RemoveAt(draggedIndex);
                        IpTabsControl.Items.Insert(targetIndex, _draggedTab);

                        // Reorder in _ipTabs list
                        var draggedData = _ipTabs.FirstOrDefault(t => t.Tab == _draggedTab);
                        if (draggedData != null)
                        {
                            _ipTabs.Remove(draggedData);
                            _ipTabs.Insert(targetIndex, draggedData);
                        }

                        // Select the moved tab
                        IpTabsControl.SelectedItem = _draggedTab;

                        // Mark as changed
                        MarkIpTabAsChanged(draggedData ?? new IpTabData());

                        Debug.WriteLine($"[ConfigWindow] Tab von Position {draggedIndex} nach {targetIndex} verschoben");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigWindow] Fehler beim Drag & Drop: {ex.Message}");
            }
        }

        private void SetIpFieldsEnabledForTab(IpTabData data, bool enabled)
        {
            if (data == null) return;
            if (data.TxtGateway != null) data.TxtGateway.IsEnabled = enabled;
            if (data.TxtDNS != null) data.TxtDNS.IsEnabled = enabled;
            if (data.BtnAddIp != null) data.BtnAddIp.IsEnabled = enabled;
            if (data.IpEntries != null)
            {
                foreach (var entry in data.IpEntries)
                {
                    if (entry.TxtIP != null) entry.TxtIP.IsEnabled = enabled;
                    if (entry.TxtSubnet != null) entry.TxtSubnet.IsEnabled = enabled;
                    if (entry.BtnRemove != null) entry.BtnRemove.IsEnabled = enabled;
                    if (entry.BtnUp != null) entry.BtnUp.IsEnabled = enabled;
                    if (entry.BtnDown != null) entry.BtnDown.IsEnabled = enabled;
                }
            }
        }

        private void LoadSystemValuesIntoTab(IpTabData data)
        {
            if (data == null) return;
            var sel = (data.AdapterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(sel)) return;
            var sys = GetSystemIpv4Settings(sel);
            if (sys != null)
            {
                if (data.TxtGateway != null) data.TxtGateway.Text = sys.Value.gateway ?? string.Empty;
                if (data.TxtDNS != null) data.TxtDNS.Text = sys.Value.dns ?? string.Empty;

                // Clear existing IP entries and load first IP from system
                if (data.IpEntries != null && data.IpEntriesPanel != null)
                {
                    data.IpEntries.Clear();
                    data.IpEntriesPanel.Children.Clear();
                    AddIpEntryToTab(data, sys.Value.ip ?? string.Empty, sys.Value.subnet ?? string.Empty);
                }
            }
        }

        private void MarkIpTabAsChanged(IpTabData data)
        {
            if (!EventsSuspended)
            {
                data.HasChanges = true;
                // Debug: Add a visual indicator
                if (data.Tab != null)
                {
                    // Mark tab header with asterisk if not already marked
                    var header = data.Tab.Header?.ToString() ?? string.Empty;
                    if (!header.Contains("*"))
                    {
                        data.Tab.Header = header + " *";
                    }
                }
            }
        }

        private void AddIpEntryToTab(IpTabData data, string ipAddress = "", string subnet = "")
        {
            if (data == null || data.IpEntriesPanel == null) return;

            var entry = new IpAddressEntry();
            entry.OwnerTab = data;

            // Create row grid
            var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // IP Address TextBox
            var txtIP = new TextBox { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(4, 4, 4, 4) };
            txtIP.Text = ipAddress;
            Grid.SetColumn(txtIP, 0);
            rowGrid.Children.Add(txtIP);

            // Subnet TextBox
            var txtSubnet = new TextBox { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(4, 4, 4, 4) };
            txtSubnet.Text = subnet;
            Grid.SetColumn(txtSubnet, 1);
            rowGrid.Children.Add(txtSubnet);

            // Action buttons: remove + move up/down
            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var btnRemove = new Button { Content = "X", Width = 32, Height = 28 };
            var btnUp = new Button { Content = "▲", Width = 32, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
            var btnDown = new Button { Content = "▼", Width = 32, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
            actionPanel.Children.Add(btnRemove);
            actionPanel.Children.Add(btnUp);
            actionPanel.Children.Add(btnDown);
            Grid.SetColumn(actionPanel, 2);
            rowGrid.Children.Add(actionPanel);

            // Remove button click handler
            btnRemove.Click += (s, e) =>
            {
                MarkIpTabAsChanged(data);
                data.IpEntriesPanel.Children.Remove(rowGrid);
                data.IpEntries.Remove(entry);
                UpdateIpEntryMoveButtons(data);
                UpdateSaveApplyButtonsState();
            };

            btnUp.Click += (s, e) =>
            {
                var fromIndex = data.IpEntries.IndexOf(entry);
                if (fromIndex <= 0) return;

                data.IpEntries.RemoveAt(fromIndex);
                data.IpEntries.Insert(fromIndex - 1, entry);

                if (entry.RowPanel != null)
                {
                    data.IpEntriesPanel.Children.Remove(entry.RowPanel);
                    data.IpEntriesPanel.Children.Insert(fromIndex - 1, entry.RowPanel);
                }

                MarkIpTabAsChanged(data);
                UpdateIpEntryMoveButtons(data);
                UpdateSaveApplyButtonsState();
            };

            btnDown.Click += (s, e) =>
            {
                var fromIndex = data.IpEntries.IndexOf(entry);
                if (fromIndex < 0 || fromIndex >= data.IpEntries.Count - 1) return;

                data.IpEntries.RemoveAt(fromIndex);
                data.IpEntries.Insert(fromIndex + 1, entry);

                if (entry.RowPanel != null)
                {
                    data.IpEntriesPanel.Children.Remove(entry.RowPanel);
                    data.IpEntriesPanel.Children.Insert(fromIndex + 1, entry.RowPanel);
                }

                MarkIpTabAsChanged(data);
                UpdateIpEntryMoveButtons(data);
                UpdateSaveApplyButtonsState();
            };

            // Add change tracking for IP and Subnet fields
            txtIP.TextChanged += (s, e) =>
            {
                if (!EventsSuspended) MarkIpTabAsChanged(data);
                ApplyIpEntryValidation(entry);
                UpdateSaveApplyButtonsState();
            };
            txtSubnet.TextChanged += (s, e) =>
            {
                if (!EventsSuspended) MarkIpTabAsChanged(data);
                ApplyIpEntryValidation(entry);
                UpdateSaveApplyButtonsState();
            };

            // Add to panel
            data.IpEntriesPanel.Children.Add(rowGrid);
            entry.TxtIP = txtIP;
            entry.TxtSubnet = txtSubnet;
            entry.BtnRemove = btnRemove;
            entry.BtnUp = btnUp;
            entry.BtnDown = btnDown;
            entry.RowPanel = rowGrid;

            data.IpEntries.Add(entry);

            UpdateIpEntryMoveButtons(data);

            // Apply current enabled state
            txtIP.IsEnabled = data.RbManual?.IsChecked ?? false;
            txtSubnet.IsEnabled = data.RbManual?.IsChecked ?? false;
            btnRemove.IsEnabled = data.RbManual?.IsChecked ?? false;
            btnUp.IsEnabled = data.RbManual?.IsChecked ?? false;
            btnDown.IsEnabled = data.RbManual?.IsChecked ?? false;

            ApplyIpEntryValidation(entry);
            UpdateSaveApplyButtonsState();
        }

        private void UpdateIpEntryMoveButtons(IpTabData data)
        {
            if (data.IpEntries == null || data.IpEntries.Count == 0) return;

            int count = data.IpEntries.Count;
            for (int i = 0; i < count; i++)
            {
                var entry = data.IpEntries[i];
                if (entry.BtnUp == null || entry.BtnDown == null) continue;

                if (count == 1)
                {
                    entry.BtnUp.IsEnabled = false;
                    entry.BtnDown.IsEnabled = false;
                    entry.BtnUp.Content = "";
                    entry.BtnDown.Content = "";
                }
                else if (i == 0)
                {
                    entry.BtnUp.IsEnabled = false;
                    entry.BtnDown.IsEnabled = true;
                    entry.BtnUp.Content = "";
                    entry.BtnDown.Content = "▼";
                }
                else if (i == count - 1)
                {
                    entry.BtnUp.IsEnabled = true;
                    entry.BtnDown.IsEnabled = false;
                    entry.BtnUp.Content = "▲";
                    entry.BtnDown.Content = "";
                }
                else
                {
                    entry.BtnUp.IsEnabled = true;
                    entry.BtnDown.IsEnabled = true;
                    entry.BtnUp.Content = "▲";
                    entry.BtnDown.Content = "▼";
                }
            }
        }

        private void ApplyIpEntryValidation(IpAddressEntry entry)
        {
            if (entry.TxtIP == null || entry.TxtSubnet == null) return;

            var ip = entry.TxtIP.Text.Trim();
            var subnet = entry.TxtSubnet.Text.Trim();

            bool requireFilled = entry.OwnerTab != null && entry.OwnerTab.RbManual != null && entry.OwnerTab.RbManual.IsChecked == true;
            bool ipValid = requireFilled ? (!string.IsNullOrEmpty(ip) && IsValidIPv4(ip)) : (string.IsNullOrEmpty(ip) || IsValidIPv4(ip));
            bool subnetValid = requireFilled ? (!string.IsNullOrEmpty(subnet) && IsValidSubnetMask(subnet)) : (string.IsNullOrEmpty(subnet) || IsValidSubnetMask(subnet));

            if (ipValid)
            {
                entry.TxtIP.BorderBrush = SystemColors.ControlDarkBrush;
                entry.TxtIP.BorderThickness = new Thickness(1);
            }
            else
            {
                entry.TxtIP.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 170, 153));
                entry.TxtIP.BorderThickness = new Thickness(1.5);
            }

            if (subnetValid)
            {
                entry.TxtSubnet.BorderBrush = SystemColors.ControlDarkBrush;
                entry.TxtSubnet.BorderThickness = new Thickness(1);
            }
            else
            {
                entry.TxtSubnet.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 170, 153));
                entry.TxtSubnet.BorderThickness = new Thickness(1.5);
            }
        }

        private void UpdateIpEntryValidationForTab(IpTabData data)
        {
            if (data.IpEntries == null) return;
            foreach (var entry in data.IpEntries)
            {
                ApplyIpEntryValidation(entry);
            }
        }

        private bool HasValidationErrors()
        {
            foreach (var t in _ipTabs)
            {
                bool requireFilled = t.RbManual != null && t.RbManual.IsChecked == true;
                if (!requireFilled) continue;

                foreach (var entry in t.IpEntries)
                {
                    if (!IsIpEntryValid(entry, requireFilled: true)) return true;
                }
            }
            return false;
        }

        private void UpdateSaveApplyButtonsState()
        {
            bool hasErrors = HasValidationErrors();
            if (this.FindName("BtnSave") is Button btnSave) btnSave.IsEnabled = !hasErrors;
            if (this.FindName("BtnApply") is Button btnApply) btnApply.IsEnabled = !hasErrors;
        }

        private void IpAdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Legacy: no-op (we now use per-tab adapter combos). Keep method to avoid XAML hookup issues.
        }


        private void LoadIpForAdapter(string adapterKey)
        {
            // Legacy global loader is no longer used; per-tab loader implemented.
        }

        private void RbMode_Checked(object sender, RoutedEventArgs e)
        {
            // Legacy: per-tab handlers are used; keep for compatibility but no-op.
        }

        private (string? ip, string? subnet, string? gateway, string? dns)? GetSystemIpv4Settings(string adapterName)
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                var ni = adapters.FirstOrDefault(n => string.Equals(n.Name, adapterName, StringComparison.OrdinalIgnoreCase) || string.Equals(n.Description, adapterName, StringComparison.OrdinalIgnoreCase));
                if (ni == null) return null;

                var props = ni.GetIPProperties();

                // IP + Subnet
                string? ip = null;
                string? subnet = null;
                var uni = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (uni != null)
                {
                    ip = uni.Address?.ToString();
                    // try IPv4 mask first, fallback to prefix length
                    try
                    {
                        subnet = uni.IPv4Mask?.ToString();
                    }
                    catch { subnet = null; }
                    if (string.IsNullOrEmpty(subnet))
                    {
                        try
                        {
                            // derive from prefix length
                            var prefixProp = uni.GetType().GetProperty("PrefixLength");
                            if (prefixProp != null)
                            {
                                var prefixVal = prefixProp.GetValue(uni);
                                if (prefixVal is int p && p >= 0 && p <= 32)
                                {
                                    uint mask = p == 0 ? 0 : 0xFFFFFFFF << (32 - p);
                                    var bytes = new byte[] { (byte)((mask >> 24) & 0xFF), (byte)((mask >> 16) & 0xFF), (byte)((mask >> 8) & 0xFF), (byte)(mask & 0xFF) };
                                    subnet = new System.Net.IPAddress(bytes).ToString();
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Gateway
                string? gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();

                // DNS (first IPv4 DNS)
                string? dns = props.DnsAddresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();

                return (ip, subnet, gw, dns);
            }
            catch
            {
                return null;
            }
        }

        private void SetIpFieldsEnabled(bool enabled)
        {
            // Legacy - no-op. Per-tab enable/disable is handled by SetIpFieldsEnabledForTab.
        }

        // Legacy no-arg SaveIpSettings left intentionally empty. Use SaveIpSettings(Dictionary<string,string>) overload.
        private void SaveIpSettings()
        {
        }

        private Dictionary<string, string> ReadIniToDict(string iniPfad)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (System.IO.File.Exists(iniPfad))
            {
                foreach (var line in System.IO.File.ReadAllLines(iniPfad))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                    var parts = line.Split(new[] { '=' }, 2);
                    values[parts[0].Trim()] = parts[1].Trim();
                }
            }
            return values;
        }

        private void BtnAddIpTab_Click(object sender, RoutedEventArgs e)
        {
            if (IpTabsControl == null) return;
            if (_ipTabs.Count >= MaxIpTabs)
            {
                MessageBox.Show($"Maximal {MaxIpTabs} IP-Tabs erlaubt.");
                return;
            }

            // Mark that changes have been made (new profile created)
            MarkIpTabAsChanged(new IpTabData { HasChanges = true });

            EnterSuspendEvents();
            try
            {
                int nextIndex = 1;
                while (_ipTabs.Any(t => t.Index == nextIndex)) nextIndex++;
                var iniPath = ConfigFileHelper.GetConfigIniPath();
                var values = ReadIniToDict(iniPath);
                var data = CreateIpTab(nextIndex, values);
                _ipTabs.Add(data);
                IpTabsControl.Items.Add(data.Tab);
                IpTabsControl.SelectedItem = data.Tab;
            }
            finally { ExitSuspendEvents(); }

            // Load system values for the newly created tab
            if (IpTabsControl.SelectedItem is TabItem ti)
            {
                var data = _ipTabs.FirstOrDefault(t => t.Tab == ti);
                if (data != null)
                {
                    LoadSystemValuesIntoTab(data);
                    // Mark the new tab as changed since it was just created
                    MarkIpTabAsChanged(data);
                }
            }
        }

        private void BtnRemoveIpTab_Click(object sender, RoutedEventArgs e)
        {
            if (IpTabsControl == null) return;
            if (IpTabsControl.SelectedItem is TabItem ti)
            {
                var data = _ipTabs.FirstOrDefault(t => t.Tab == ti);
                if (data != null)
                {
                    // Mark that changes have been made (profile deleted)
                    // Get a different tab to mark as changed
                    var otherTab = _ipTabs.FirstOrDefault(t => t != data);
                    if (otherTab != null)
                    {
                        MarkIpTabAsChanged(otherTab);
                    }
                    else
                    {
                        // If this is the last tab, mark it before deleting
                        MarkIpTabAsChanged(data);
                    }

                    _ipTabs.Remove(data);
                    IpTabsControl.Items.Remove(data.Tab);
                }
            }
        }

        private void WriteDictToIni(string iniPfad, Dictionary<string, string> values)
        {
            // Ensure Adapter1/Adapter2 are written first to keep file readable
            var outLines = new List<string>();
            if (values.TryGetValue("Adapter1", out var a1)) outLines.Add("Adapter1 = " + a1);
            if (values.TryGetValue("Adapter2", out var a2)) outLines.Add("Adapter2 = " + a2);

            // Then other keys
            foreach (var kv in values)
            {
                if (kv.Key.Equals("Adapter1", StringComparison.OrdinalIgnoreCase) || kv.Key.Equals("Adapter2", StringComparison.OrdinalIgnoreCase)) continue;
                outLines.Add(kv.Key + " = " + kv.Value);
            }

            System.IO.File.WriteAllLines(iniPfad, outLines);
        }

        // Overload: save into provided dictionary and write INI
        private void SaveIpSettings(Dictionary<string, string> values)
        {
            try
            {
                // Remove any existing IpTab* entries first so deleted tabs are cleared
                var keys = values.Keys.ToList();
                foreach (var k in keys)
                {
                    if (k.StartsWith("IpTab", StringComparison.OrdinalIgnoreCase))
                    {
                        values.Remove(k);
                    }
                }
                // Also remove any previous profile-based keys (Name.Field)
                var suffixes = new[] { ".Adapter", ".Mode", ".IP", ".Subnet", ".GW", ".DNS" };
                keys = values.Keys.ToList();
                foreach (var k in keys)
                {
                    if (k.Equals("IpProfileNames", StringComparison.OrdinalIgnoreCase))
                    {
                        values.Remove(k);
                        continue;
                    }
                    foreach (var s in suffixes)
                    {
                        if (k.EndsWith(s, StringComparison.OrdinalIgnoreCase) && k.IndexOf('.') > 0)
                        {
                            values.Remove(k);
                            break;
                        }
                    }
                    // Also remove old IP_X and Subnet_X entries
                    if (k.Contains(".IP_") || k.Contains(".Subnet_"))
                    {
                        values.Remove(k);
                    }
                }

                // Validate profile names are unique and persist settings using the profile name as key
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var profileNames = new List<string>();
                foreach (var t in _ipTabs)
                {
                    var name = t.TxtName?.Text.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) name = $"IP #{t.Index}";
                    if (usedNames.Contains(name))
                    {
                        MessageBox.Show($"Profilname '{name}' ist mehrfach vorhanden. Bitte eindeutige Namen vergeben.");
                        return; // abort save
                    }
                    usedNames.Add(name);
                    profileNames.Add(name);

                    var adapter = (t.AdapterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
                    values[$"{name}.Adapter"] = adapter;
                    var isManual = (t.RbManual != null && t.RbManual.IsChecked == true);
                    values[$"{name}.Mode"] = isManual ? "Manual" : "DHCP";
                    values[$"{name}.GW"] = t.TxtGateway?.Text.Trim() ?? string.Empty;
                    values[$"{name}.DNS"] = t.TxtDNS?.Text.Trim() ?? string.Empty;

                    // Save multiple IP/Subnet entries
                    int ipIndex = 1;
                    if (t.IpEntries != null && isManual)
                    {
                        // Validate IP/Gateway/Subnet combination for manual mode
                        string firstIp = string.Empty;
                        string firstSubnet = string.Empty;
                        bool hasValidEntries = false;

                        foreach (var entry in t.IpEntries)
                        {
                            var ip = entry.TxtIP?.Text.Trim() ?? string.Empty;
                            var subnet = entry.TxtSubnet?.Text.Trim() ?? string.Empty;
                            if (!string.IsNullOrEmpty(ip))
                            {
                                if (ipIndex == 1)
                                {
                                    firstIp = ip;
                                    firstSubnet = subnet;

                                    // Validate first IP with gateway and subnet
                                    var gw = values[$"{name}.GW"] ?? string.Empty;
                                    var (isValid, errorMsg) = ValidateIpGatewaySubnet(ip, subnet, gw);
                                    if (!isValid)
                                    {
                                        MessageBox.Show($"Validierungsfehler in Profil '{name}':\n\n{errorMsg}");
                                        return; // abort save
                                    }
                                }
                                else
                                {
                                    // Validate additional IPs
                                    var (isValid, errorMsg) = ValidateIpGatewaySubnet(ip, subnet, string.Empty);
                                    if (!isValid)
                                    {
                                        MessageBox.Show($"Validierungsfehler in Profil '{name}', IP #{ipIndex}:\n\n{errorMsg}");
                                        return; // abort save
                                    }
                                }

                                values[$"{name}.IP_{ipIndex}"] = ip;
                                values[$"{name}.Subnet_{ipIndex}"] = subnet;
                                hasValidEntries = true;
                                ipIndex++;
                            }
                        }

                        if (!hasValidEntries && isManual)
                        {
                            MessageBox.Show($"Profil '{name}' ist im Manuell-Modus, hat aber keine gültigen IP-Adressen.");
                            return; // abort save
                        }
                    }

                    // Remove old single IP/Subnet keys if they exist
                    values.Remove($"{name}.IP");
                    values.Remove($"{name}.Subnet");
                }

                // Save the ordered list of profile names
                values["IpProfileNames"] = string.Join(",", profileNames);

                // Persist Tools -> Ping entries
                // Remove any previous Tools.Ping* keys
                var oldKeys = values.Keys.ToList();
                foreach (var k in oldKeys)
                {
                    if (k.StartsWith("Tools.Ping", StringComparison.OrdinalIgnoreCase) || k.Equals("Tools.PingCount", StringComparison.OrdinalIgnoreCase))
                    {
                        values.Remove(k);
                    }
                }

                // Save current ping entries
                values["Tools.PingCount"] = _pingEntries.Count.ToString();
                for (int i = 0; i < _pingEntries.Count; i++)
                {
                    var p = _pingEntries[i];
                    values[$"Tools.Ping{i}.Enabled"] = (p.Enabled != null && p.Enabled.IsChecked == true) ? "1" : "0";
                    values[$"Tools.Ping{i}.IP"] = p.TxtIp?.Text.Trim() ?? string.Empty;
                    // no timeout persisted per-entry (fixed timeout used)
                }

                // Save Ping table column widths from header grid
                try
                {
                    if (this.FindName("PingHeaderGrid") is Grid headerGrid && headerGrid.ColumnDefinitions.Count >= 13)
                    {
                        values["Tools.PingColActivWidth"] = headerGrid.ColumnDefinitions[0].Width.Value.ToString("F0");
                        values["Tools.PingColIpWidth"] = headerGrid.ColumnDefinitions[2].Width.Value.ToString("F0");
                        values["Tools.PingColStartWidth"] = headerGrid.ColumnDefinitions[4].Width.Value.ToString("F0");
                        values["Tools.PingColMissWidth"] = headerGrid.ColumnDefinitions[6].Width.Value.ToString("F0");
                        values["Tools.PingColLossWidth"] = headerGrid.ColumnDefinitions[8].Width.Value.ToString("F0");
                        values["Tools.PingColStatusWidth"] = headerGrid.ColumnDefinitions[10].Width.Value.ToString("F0");
                        // Save the actions column width (column 12)
                        values["Tools.PingColActionsWidth"] = headerGrid.ColumnDefinitions[12].Width.Value.ToString("F0");
                    }
                }
                catch { }

                var iniPfad = ConfigFileHelper.GetConfigIniPath();
                WriteDictToIni(iniPfad, values);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern der IP-Einstellungen: " + ex.Message);
            }
        }

        // --- Ping Tools: UI creation, timers and persistence ---
        private void CreatePingRow(PingEntryData data, int index)
        {
            var panel = this.FindName("PingEntriesPanel") as StackPanel;
            var headerGrid = this.FindName("PingHeaderGrid") as Grid;
            if (panel == null || headerGrid == null) return;

            // Create Grid matching header column structure
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };

            // Alternierende Hintergrundfarben
            var bgBrush = (index % 2 == 0)
                ? TryFindResource("PingRowEvenBg") as SolidColorBrush
                : TryFindResource("PingRowOddBg") as SolidColorBrush;
            row.Background = bgBrush ?? new SolidColorBrush(Color.FromRgb(250, 250, 250));

            // Copy column definitions from header grid
            for (int i = 0; i < headerGrid.ColumnDefinitions.Count; i++)
            {
                var colDef = new ColumnDefinition();
                colDef.Width = headerGrid.ColumnDefinitions[i].Width;
                // Bind width changes so columns resize together
                colDef.SetBinding(ColumnDefinition.WidthProperty, new System.Windows.Data.Binding("Width")
                {
                    Source = headerGrid.ColumnDefinitions[i],
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });
                row.ColumnDefinitions.Add(colDef);
            }

            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2,0,2,0) };
            var txtIp = new TextBox { Style = TryFindResource("PingIpTextBox") as Style };
            var lblStart = new Label { Content = "-", Style = TryFindResource("PingCellLabel") as Style };
            var lblMiss = new Label { Content = "0", Style = TryFindResource("PingCellLabel") as Style };
            var lblLoss = new Label { Content = "0%", Style = TryFindResource("PingCellLabel") as Style };
            var lblStatus = new Label { Content = "-", Style = TryFindResource("PingCellLabel") as Style };

            // per-row action buttons in last column
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var btnReset = new Button { Width = 50, Content = "↻", ToolTip = "Reset", Style = TryFindResource("PingActionButton") as Style, FontSize = 16, Padding = new Thickness(2,0,2,0) };
            var btnDelete = new Button { Width = 30, Content = "🗑", ToolTip = "Löschen", Style = TryFindResource("PingActionButton") as Style };
            btnPanel.Children.Add(btnReset);
            btnPanel.Children.Add(btnDelete);

            Grid.SetColumn(cb, 0);
            Grid.SetColumn(txtIp, 2);
            Grid.SetColumn(lblStart, 4);
            Grid.SetColumn(lblMiss, 6);
            Grid.SetColumn(lblLoss, 8);
            Grid.SetColumn(lblStatus, 10);
            Grid.SetColumn(btnPanel, 12);

            row.Children.Add(cb);
            row.Children.Add(txtIp);
            row.Children.Add(lblStart);
            row.Children.Add(lblMiss);
            row.Children.Add(lblLoss);
            row.Children.Add(lblStatus);
            row.Children.Add(btnPanel);

            panel.Children.Add(row);

            data.Enabled = cb;
            data.TxtIp = txtIp;
            data.LblStart = lblStart;
            data.LblMiss = lblMiss;
            data.LblLoss = lblLoss;
            data.LblStatus = lblStatus;
            // hook events
            txtIp.TextChanged += (s, ev) =>
            {
                // Mark ping tab as changed
                if (!EventsSuspended) _pingTabHasChanges = true;

                var ip = txtIp.Text.Trim();
                bool valid = IsValidHostOrIP(ip);
                // disable checkbox if invalid
                cb.IsEnabled = valid;
                if (!valid)
                {
                    cb.IsChecked = false;
                    txtIp.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 170, 153)); // Dezenteres Rot #FFAA99
                    txtIp.BorderThickness = new Thickness(1.5);
                }
                else
                {
                    txtIp.BorderBrush = SystemColors.ControlDarkBrush;
                    txtIp.BorderThickness = new Thickness(1);
                }
            };

            cb.Checked += (s, ev) =>
            {
                // Mark ping tab as changed
                if (!EventsSuspended) _pingTabHasChanges = true;

                // during load operations we suppress validations
                if (EventsSuspended) return;
                // validate on check
                var ip = txtIp.Text.Trim();
                if (!IsValidHostOrIP(ip))
                {
                    MessageBox.Show("Ungültige IP-Adresse oder Hostname.");
                    cb.IsChecked = false;
                }
            };

            cb.Unchecked += (s, ev) =>
            {
                // Mark ping tab as changed
                if (!EventsSuspended) _pingTabHasChanges = true;
            };

            btnReset.Click += (s, ev) =>
            {
                data.SentCount = 0;
                data.MissCount = 0;
                data.StartTime = null;
                if (data.LblStart != null) data.LblStart.Content = "-";
                if (data.LblMiss != null) data.LblMiss.Content = "0";
                if (data.LblLoss != null) data.LblLoss.Content = "0%";
                if (data.LblStatus != null) { data.LblStatus.Content = "-"; data.LblStatus.Background = Brushes.LightGray; }
            };

            btnDelete.Click += (s, ev) =>
            {
                // Mark ping tab as changed
                _pingTabHasChanges = true;

                try { data.Timer?.Stop(); } catch { }
                try { if (data.RowPanel != null) (this.FindName("PingEntriesPanel") as StackPanel)?.Children.Remove(data.RowPanel); } catch { }
                _pingEntries.Remove(data);
            };
            data.RowPanel = row;

            // Timer for this row
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += async (s, ev) =>
            {
                try
                {
                    if (data.Enabled == null || data.TxtIp == null || data.LblStatus == null) return;
                    if (data.Enabled.IsChecked != true)
                    {
                        // update start label to reflect inactivity
                        data.LblStart?.Dispatcher.Invoke(() =>
                        {
                            if (data.LblStart != null) data.LblStart.Content = "-";
                            if (data.LblMiss != null) data.LblMiss.Content = "0";
                            if (data.LblLoss != null) data.LblLoss.Content = "0%";
                            data.LblStatus.Content = "-";
                            data.LblStatus.Background = Brushes.LightGray;
                        });
                        return;
                    }

                    var ip = data.TxtIp.Text.Trim();
                    if (string.IsNullOrEmpty(ip))
                    {
                        data.LblStatus.Dispatcher.Invoke(() =>
                        {
                            data.LblStatus.Content = "no IP";
                            data.LblStatus.Background = Brushes.IndianRed;
                        });
                        return;
                    }

                    int timeout = PingTimeoutMs;

                    // increment sent count
                    data.SentCount++;
                    if (data.StartTime == null) data.StartTime = DateTime.Now;

                    using var p = new System.Net.NetworkInformation.Ping();
                    try
                    {
                        var reply = await p.SendPingAsync(ip, timeout);
                        if (reply.Status == IPStatus.Success)
                        {
                            // success
                            data.LblStatus.Dispatcher.Invoke(() =>
                            {
                                data.LblStatus.Content = $"{reply.RoundtripTime} ms";
                                data.LblStatus.Background = Brushes.LightGreen;
                            });
                        }
                        else
                        {
                            // failed
                            data.MissCount++;
                            data.LblStatus.Dispatcher.Invoke(() =>
                            {
                                data.LblStatus.Content = "no reply";
                                data.LblStatus.Background = Brushes.IndianRed;
                            });
                        }
                    }
                    catch
                    {
                        data.MissCount++;
                        data.LblStatus.Dispatcher.Invoke(() =>
                        {
                            data.LblStatus.Content = "error";
                            data.LblStatus.Background = Brushes.IndianRed;
                        });
                    }

                    // update stats labels
                    data.LblStart?.Dispatcher.Invoke(() =>
                    {
                        if (data.LblStart != null)
                        {
                            if (data.StartTime != null)
                            {
                                var secs = (int)(DateTime.Now - data.StartTime.Value).TotalSeconds;
                                data.LblStart.Content = $"vor {secs}s";
                            }
                            else data.LblStart.Content = "-";
                        }
                    });
                    data.LblMiss?.Dispatcher.Invoke(() => { if (data.LblMiss != null) data.LblMiss.Content = data.MissCount.ToString(); });
                    data.LblLoss?.Dispatcher.Invoke(() =>
                    {
                        if (data.LblLoss != null)
                        {
                            if (data.SentCount > 0)
                            {
                                var loss = (int)Math.Round(100.0 * data.MissCount / data.SentCount);
                                data.LblLoss.Content = loss + "%";
                            }
                            else data.LblLoss.Content = "0%";
                        }
                    });
                }
                catch { }
            };

            data.Timer = timer;
            // Start timer
            try { timer.Start(); } catch { }
        }

        private void ChkPingBackground_Changed(object sender, RoutedEventArgs e)
        {
            if (EventsSuspended) return;

            // Mark ping tab as changed
            _pingTabHasChanges = true;

            if (this.FindName("ChkPingBackground") is CheckBox chk)
            {
                _keepPingTimersRunning = chk.IsChecked == true;

                if (!_keepPingTimersRunning)
                {
                    // Stop all ping timers
                    foreach (var p in _pingEntries)
                    {
                        try { p.Timer?.Stop(); } catch { }
                    }
                }
                else
                {
                    // Start ping timers for enabled entries
                    foreach (var p in _pingEntries)
                    {
                        if (p.Enabled != null && p.Enabled.IsChecked == true && p.Timer != null)
                        {
                            try { p.Timer.Start(); } catch { }
                        }
                    }
                }
            }
        }

        private void UpdatePingBackgroundCheckbox()
        {
            if (this.FindName("ChkPingBackground") is CheckBox chk)
            {
                EnterSuspendEvents();
                try
                {
                    chk.IsChecked = _keepPingTimersRunning;
                }
                finally
                {
                    ExitSuspendEvents();
                }
            }
        }

        private void BtnAddPingEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_pingEntries.Count >= MaxPingEntries)
            {
                MessageBox.Show($"Maximal {MaxPingEntries} Ping-Einträge erlaubt.");
                return;
            }
            // Mark ping tab as changed
            _pingTabHasChanges = true;

            var p = new PingEntryData();
            _pingEntries.Add(p);
            CreatePingRow(p, _pingEntries.Count - 1);
        }

        private void BtnRemovePingEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_pingEntries.Count == 0) return;
            // Mark ping tab as changed
            _pingTabHasChanges = true;

            // remove last entry
            var last = _pingEntries[_pingEntries.Count - 1];
            try { last.Timer?.Stop(); } catch { }
            try { if (last.RowPanel != null) (this.FindName("PingEntriesPanel") as StackPanel)?.Children.Remove(last.RowPanel); } catch { }
            _pingEntries.RemoveAt(_pingEntries.Count - 1);
        }

        private void BtnResetAllPingEntries_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _pingEntries)
            {
                p.SentCount = 0;
                p.MissCount = 0;
                p.StartTime = null;
                if (p.LblStart != null) p.LblStart.Content = "-";
                if (p.LblMiss != null) p.LblMiss.Content = "0";
                if (p.LblLoss != null) p.LblLoss.Content = "0%";
                if (p.LblStatus != null) { p.LblStatus.Content = "-"; p.LblStatus.Background = Brushes.LightGray; }
            }
        }

        private void StopAllPingTimers()
        {
            foreach (var p in _pingEntries)
            {
                try { p.Timer?.Stop(); } catch { }
            }
        }

        private void LoadPingSettings()
        {
            try
            {
                var iniPath = ConfigFileHelper.GetConfigIniPath();
                var values = ReadIniToDict(iniPath);

                // Load and apply saved column widths to header grid before creating rows
                try
                {
                    if (this.FindName("PingHeaderGrid") is Grid headerGrid && headerGrid.ColumnDefinitions.Count >= 13)
                    {
                        if (values.TryGetValue("Tools.PingColActivWidth", out var w0) && double.TryParse(w0, out var v0) && v0 > 0)
                            headerGrid.ColumnDefinitions[0].Width = new GridLength(v0);
                        if (values.TryGetValue("Tools.PingColIpWidth", out var w2) && double.TryParse(w2, out var v2) && v2 > 0)
                            headerGrid.ColumnDefinitions[2].Width = new GridLength(v2);
                        if (values.TryGetValue("Tools.PingColStartWidth", out var w4) && double.TryParse(w4, out var v4) && v4 > 0)
                            headerGrid.ColumnDefinitions[4].Width = new GridLength(v4);
                        if (values.TryGetValue("Tools.PingColMissWidth", out var w6) && double.TryParse(w6, out var v6) && v6 > 0)
                            headerGrid.ColumnDefinitions[6].Width = new GridLength(v6);
                        if (values.TryGetValue("Tools.PingColLossWidth", out var w8) && double.TryParse(w8, out var v8) && v8 > 0)
                            headerGrid.ColumnDefinitions[8].Width = new GridLength(v8);
                        if (values.TryGetValue("Tools.PingColStatusWidth", out var w10) && double.TryParse(w10, out var v10) && v10 > 0)
                            headerGrid.ColumnDefinitions[10].Width = new GridLength(v10);
                        // Load the actions column width (column 12)
                        if (values.TryGetValue("Tools.PingColActionsWidth", out var w12) && double.TryParse(w12, out var v12) && v12 > 0)
                            headerGrid.ColumnDefinitions[12].Width = new GridLength(v12);
                    }
                }
                catch { }

                int count = 0;
                if (values.TryGetValue("Tools.PingCount", out var sc)) int.TryParse(sc, out count);
                for (int i = 0; i < count; i++)
                {
                        var p = new PingEntryData();
                        _pingEntries.Add(p);
                        CreatePingRow(p, i);
                        // First set IP (so TextChanged can set enabled state), then set Enabled flag
                        if (values.TryGetValue($"Tools.Ping{i}.IP", out var ip))
                        {
                            if (p.TxtIp != null) p.TxtIp.Text = ip;
                            if (p.Enabled != null)
                            {
                                // set enabled state based on validity (but don't trigger Checked validation during load)
                                p.Enabled.IsEnabled = IsValidHostOrIP(ip);
                            }
                        }
                        // Always start with disabled pings (user must manually enable after app start)
                        if (p.Enabled != null) p.Enabled.IsChecked = false;
                            // no per-entry timeout to load (fixed timeout used)
                }
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (HasValidationErrors() && !_allowCloseWithErrors)
            {
                var result = MessageBox.Show(
                    "Es liegen Fehler in den IP-Adressen vor.\n\nOhne Speichern schliessen?",
                    "Fehler vorhanden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _allowCloseWithErrors = true;
            }

            try { StopAllPingTimers(); } catch { }
            try { StopAllGatewayPingTimers(); } catch { }
            base.OnClosing(e);
        }

        private void IpTabsControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (EventsSuspended) return;
            try
            {
                if (e.OriginalSource != sender) return;
                if (!(sender is TabControl tc)) return;

                // Stop timers on all tabs
                foreach (var t in _ipTabs)
                {
                    try { t.PingTimer?.Stop(); } catch { }
                }

                // Start timer for selected tab only (if any)
                if (tc.SelectedItem is TabItem ti)
                {
                    var data = _ipTabs.FirstOrDefault(t => t.Tab == ti);
                    if (data != null)
                    {
                        StartGatewayPingTimer(data);
                    }
                }
            }
            catch { }
        }

        private void StartGatewayPingTimer(IpTabData data)
        {
            try
            {
                // Ensure existing timer stopped
                try { data.PingTimer?.Stop(); } catch { }

                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(3);
                timer.Tick += async (s, ev) =>
                {
                    try
                    {
                        // Always use the current gateway of the selected network interface
                        var selAdapter = (data.AdapterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                        string gw = string.Empty;
                        if (!string.IsNullOrEmpty(selAdapter))
                        {
                            var sys = GetSystemIpv4Settings(selAdapter);
                            if (sys != null) gw = sys.Value.gateway ?? string.Empty;
                        }

                        if (string.IsNullOrEmpty(gw))
                        {
                            // update label to unknown (no gateway found on NIC)
                            data.LblGwStatus?.Dispatcher.Invoke(() =>
                            {
                                if (data.LblGwStatus != null)
                                {
                                    data.LblGwStatus.Content = "GW: -";
                                    data.LblGwStatus.Background = Brushes.LightGray;
                                }
                            });
                            return;
                        }

                        using var p = new System.Net.NetworkInformation.Ping();
                        try
                        {
                            var reply = await p.SendPingAsync(gw, 2000);
                            data.LblGwStatus?.Dispatcher.Invoke(() =>
                            {
                                if (data.LblGwStatus == null) return;
                                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                                {
                                    data.LblGwStatus.Content = $"GW: {gw} ({reply.RoundtripTime} ms)";
                                    data.LblGwStatus.Background = Brushes.LightGreen;
                                }
                                else
                                {
                                    data.LblGwStatus.Content = $"GW: {gw} (no reply)";
                                    data.LblGwStatus.Background = Brushes.IndianRed;
                                }
                            });
                        }
                        catch
                        {
                            data.LblGwStatus?.Dispatcher.Invoke(() =>
                            {
                                if (data.LblGwStatus == null) return;
                                data.LblGwStatus.Content = $"GW: {gw} (error)";
                                data.LblGwStatus.Background = Brushes.IndianRed;
                            });
                        }
                    }
                    catch { }
                };

                data.PingTimer = timer;
                // run immediately then start
                try { timer.Start(); } catch { }
            }
            catch { }
        }

        private bool IsValidIPv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (System.Net.IPAddress.TryParse(ip, out var addr))
            {
                return addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
            }
            return false;
        }

        private bool IsValidSubnetMask(string subnet)
        {
            if (!IsValidIPv4(subnet)) return false;
            var bytes = System.Net.IPAddress.Parse(subnet).GetAddressBytes();
            uint mask = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

            if (mask == 0) return false;

            bool seenZero = false;
            for (int i = 31; i >= 0; i--)
            {
                bool bitSet = (mask & (1u << i)) != 0;
                if (!bitSet) seenZero = true;
                else if (seenZero) return false;
            }

            return true;
        }

        private bool TryValidateIpEntry(IpAddressEntry entry, bool allowEmpty, out string errorMessage)
        {
            errorMessage = string.Empty;
            var ip = entry.TxtIP?.Text.Trim() ?? string.Empty;
            var subnet = entry.TxtSubnet?.Text.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(subnet))
            {
                if (allowEmpty) return true;
                errorMessage = "Bitte IP-Adresse und Subnetzmaske angeben, bevor eine neue Zeile hinzugefuegt wird.";
                return false;
            }

            if (string.IsNullOrEmpty(ip))
            {
                errorMessage = "IP-Adresse ist erforderlich.";
                return false;
            }
            if (!IsValidIPv4(ip))
            {
                errorMessage = $"IP-Adresse '{ip}' ist ungueltig.";
                return false;
            }

            if (string.IsNullOrEmpty(subnet))
            {
                errorMessage = "Subnetzmaske ist erforderlich.";
                return false;
            }
            if (!IsValidSubnetMask(subnet))
            {
                errorMessage = $"Subnetzmaske '{subnet}' ist ungueltig.";
                return false;
            }

            return true;
        }

        private bool IsIpEntryValid(IpAddressEntry entry, bool requireFilled)
        {
            var ip = entry.TxtIP?.Text.Trim() ?? string.Empty;
            var subnet = entry.TxtSubnet?.Text.Trim() ?? string.Empty;

            if (requireFilled)
            {
                if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(subnet)) return false;
                if (!IsValidIPv4(ip)) return false;
                if (!IsValidSubnetMask(subnet)) return false;
                return true;
            }

            if (string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(subnet)) return true;
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(subnet)) return false;
            if (!IsValidIPv4(ip)) return false;
            if (!IsValidSubnetMask(subnet)) return false;
            return true;
        }

        private (bool valid, string errorMessage) ValidateIpGatewaySubnet(string ip, string subnet, string gateway)
        {
            // Validate IP
            if (string.IsNullOrWhiteSpace(ip))
            {
                return (false, "IP-Adresse ist erforderlich.");
            }
            if (!IsValidIPv4(ip))
            {
                return (false, $"IP-Adresse '{ip}' ist ungültig.");
            }

            // Validate Subnet
            if (string.IsNullOrWhiteSpace(subnet))
            {
                return (false, "Subnetmaske ist erforderlich.");
            }
            if (!IsValidSubnetMask(subnet))
            {
                return (false, $"Subnetmaske '{subnet}' ist ungültig.");
            }

            // Only validate gateway if it's not empty
            if (!string.IsNullOrWhiteSpace(gateway))
            {
                if (!IsValidIPv4(gateway))
                {
                    return (false, $"Gateway '{gateway}' ist ungültig.");
                }

                // Check if gateway is in the same subnet as the IP
                if (!IsIPInSubnet(ip, gateway, subnet))
                {
                    return (false, $"Gateway '{gateway}' passt nicht zum Subnetz der IP '{ip}' mit Maske '{subnet}'.\n\nDas Gateway muss im gleichen Subnetz wie die IP-Adresse liegen.");
                }
            }

            return (true, "");
        }

        private bool IsIPInSubnet(string ip, string testIp, string subnet)
        {
            try
            {
                if (!System.Net.IPAddress.TryParse(ip, out var ipAddr) ||
                    !System.Net.IPAddress.TryParse(testIp, out var testAddr) ||
                    !System.Net.IPAddress.TryParse(subnet, out var subnetAddr))
                {
                    return false;
                }

                // Convert to uint for bitwise operations
                var ipBytes = ipAddr.GetAddressBytes();
                var testBytes = testAddr.GetAddressBytes();
                var subnetBytes = subnetAddr.GetAddressBytes();

                uint ipUint = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];
                uint testUint = ((uint)testBytes[0] << 24) | ((uint)testBytes[1] << 16) | ((uint)testBytes[2] << 8) | testBytes[3];
                uint subnetUint = ((uint)subnetBytes[0] << 24) | ((uint)subnetBytes[1] << 16) | ((uint)subnetBytes[2] << 8) | subnetBytes[3];

                // Calculate network address by applying subnet mask
                uint ipNetwork = ipUint & subnetUint;
                uint testNetwork = testUint & subnetUint;

                return ipNetwork == testNetwork;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidHostOrIP(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            // Check if it's a valid IPv4 address
            if (System.Net.IPAddress.TryParse(input, out var addr))
            {
                return addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
            }

            // Check if it's a valid hostname/DNS name
            // Valid hostname characters: alphanumeric, hyphen, dot
            // Must not start/end with hyphen or dot
            var trimmed = input.Trim();
            if (trimmed.Length > 253) return false; // Max DNS name length
            if (trimmed.StartsWith(".") || trimmed.EndsWith(".") || trimmed.StartsWith("-") || trimmed.EndsWith("-")) return false;

            // Split by dots and validate each label
            var labels = trimmed.Split('.');
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label) || label.Length > 63) return false; // Max label length
                if (label.StartsWith("-") || label.EndsWith("-")) return false;
                if (!System.Text.RegularExpressions.Regex.IsMatch(label, @"^[a-zA-Z0-9-]+$")) return false;
            }

            return true;
        }

        private void UpdateButtonVisibility(int selectedTabIndex)
        {
            try
            {
                if (this.FindName("TabControlMain") is TabControl tc && selectedTabIndex >= 0 && selectedTabIndex < tc.Items.Count)
                {
                    if (tc.Items[selectedTabIndex] is TabItem ti)
                    {
                        var header = ti.Header?.ToString() ?? string.Empty;
                        bool isIpSettingsTab = header.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                              header.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (this.FindName("BtnApply") is Button btnApply)
                        {
                            btnApply.Visibility = isIpSettingsTab ? Visibility.Visible : Visibility.Collapsed;
                        }

                        // Hide "Speichern" button in Info tab
                        if (this.FindName("BtnSave") is Button btnSave)
                        {
                            btnSave.Visibility = header.IndexOf("Info", StringComparison.OrdinalIgnoreCase) >= 0
                                ? Visibility.Collapsed
                                : Visibility.Visible;
                        }
                    }
                }
            }
            catch { }
        }

        public void SelectAdapterTab()
        {
            try
            {
                if (this.FindName("TabControlMain") is TabControl tc)
                {
                    EnterSuspendEvents();
                    try
                    {
                        tc.SelectedIndex = 0;
                        UpdateButtonVisibility(0);
                        // Ensure adapters are loaded
                        LoadAdapters();
                    }
                    finally
                    {
                        ExitSuspendEvents();
                    }
                }
            }
            catch { }
        }

        public void SelectIpTab()
        {
            try
            {
                if (this.FindName("TabControlMain") is TabControl tc)
                {
                    EnterSuspendEvents();
                    try
                    {
                        tc.SelectedIndex = 1;
                        UpdateButtonVisibility(1);
                        // Ensure combo is filled/loaded
                        LoadIpSettings();
                    }
                    finally
                    {
                        ExitSuspendEvents();
                    }
                }
            }
            catch { }
        }

        public void SelectInfoTab()
        {
            try
            {
                if (this.FindName("TabControlMain") is TabControl tc)
                {
                    EnterSuspendEvents();
                    try
                    {
                        // Info tab is the fourth tab (index 3: 0=Adapter, 1=IP Settings, 2=Tools, 3=Info)
                        tc.SelectedIndex = 3;
                        InitializeInfoPage();
                    }
                    finally
                    {
                        UpdateButtonVisibility(3);
                        ExitSuspendEvents();
                    }
                }
            }
            catch { }
        }

        public void SelectToolsTab()
        {
            try
            {
                if (this.FindName("TabControlMain") is not TabControl tc)
                {
                    Trace.WriteLine("[ConfigWindow] TabControlMain nicht gefunden");
                    return;
                }

                EnterSuspendEvents();
                try
                {
                    // Find and select Tools tab
                    int toolsTabIndex = -1;
                    for (int i = 0; i < tc.Items.Count; i++)
                    {
                        if (tc.Items[i] is TabItem ti && (ti.Header?.ToString() ?? string.Empty).IndexOf("Tools", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            toolsTabIndex = i;
                            break;
                        }
                    }

                    if (toolsTabIndex >= 0)
                    {
                        tc.SelectedIndex = toolsTabIndex;
                        UpdateButtonVisibility(toolsTabIndex);

                        // Ensure Ping tab is selected by default in ToolsTabControl
                        // Use Dispatcher to ensure controls are fully rendered
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (this.FindName("ToolsTabControl") is TabControl toolsTab && toolsTab.Items.Count > 0)
                                {
                                    toolsTab.SelectedIndex = 0;
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[ConfigWindow] Error setting ToolsTab index: {ex.Message}");
                            }
                        }));
                    }
                }
                finally
                {
                    ExitSuspendEvents();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ConfigWindow] Error in SelectToolsTab: {ex.Message}");
                Trace.WriteLine($"[ConfigWindow] Stack Trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Startet automatische Update-Prüfung beim App-Start (ohne Button-UI)
        /// </summary>
        public async Task CheckForUpdatesAutoAsync()
        {
            try
            {
                Trace.WriteLine("[UpdateCheck] ========== Automatische Update-Prüfung gestartet ==========");
                Console.WriteLine("[UpdateCheck] ========== Automatische Update-Prüfung gestartet ==========");

                var currentVersion = GetCurrentVersion();
                Trace.WriteLine($"[UpdateCheck] Aktuelle Version: {currentVersion}");
                Console.WriteLine($"[UpdateCheck] Aktuelle Version: {currentVersion}");

                var latestRelease = await GetLatestGitHubReleaseAsync();

                if (latestRelease == null)
                {
                    Trace.WriteLine("[UpdateCheck] Fehler: Konnte keine Release-Informationen abrufen");
                    Console.WriteLine("[UpdateCheck] Fehler: Konnte keine Release-Informationen abrufen");
                    return;
                }

                Trace.WriteLine($"[UpdateCheck] GitHub Tag: {latestRelease.TagName}");
                Console.WriteLine($"[UpdateCheck] GitHub Tag: {latestRelease.TagName}");
                var latestVersion = ParseVersion(latestRelease.TagName);
                Trace.WriteLine($"[UpdateCheck] Geparste Version: {latestVersion}");
                Console.WriteLine($"[UpdateCheck] Geparste Version: {latestVersion}");

                if (latestVersion > currentVersion)
                {
                    Trace.WriteLine($"[UpdateCheck] Neue Version verfügbar: {latestVersion} > {currentVersion}");
                    Console.WriteLine($"[UpdateCheck] Neue Version verfügbar: {latestVersion} > {currentVersion}");

                    // Zeige Update-Dialog
                    var dialog = new UpdateCheckDialog(currentVersion.ToString(), latestVersion.ToString());

                    var result = dialog.ShowDialog();

                    if (result == true && dialog.UserWantsUpdate)
                    {
                        Trace.WriteLine($"[UpdateCheck] Benutzer möchte updaten - zeige Info Seite");
                        Console.WriteLine($"[UpdateCheck] Benutzer möchte updaten - zeige Info Seite");

                        // Öffne Info-Tab in neuem ConfigWindow
                        SelectInfoTab();
                        this.ShowDialog();
                    }
                }
                else
                {
                    Trace.WriteLine($"[UpdateCheck] Bereits aktuell: {currentVersion} >= {latestVersion}");
                    Console.WriteLine($"[UpdateCheck] Bereits aktuell: {currentVersion} >= {latestVersion}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[UpdateCheck] Fehler bei Auto-Check: {ex.Message}");
                Console.WriteLine($"[UpdateCheck] Fehler bei Auto-Check: {ex.Message}");
                // Fehler bei Auto-Check werden stillschweigend ignoriert
            }
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("[UpdateCheck] ========== Update-Check gestartet ==========");
            Console.WriteLine("[UpdateCheck] ========== Update-Check gestartet ==========");

            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                btn.Content = "Überprüfe...";

                try
                {
                    var currentVersion = GetCurrentVersion();
                    Trace.WriteLine($"[UpdateCheck] Aktuelle Version: {currentVersion}");
                    Console.WriteLine($"[UpdateCheck] Aktuelle Version: {currentVersion}");

                    var latestRelease = await GetLatestGitHubReleaseAsync();

                    if (latestRelease == null)
                    {
                        Trace.WriteLine("[UpdateCheck] Fehler: Konnte keine Release-Informationen abrufen");
                        Console.WriteLine("[UpdateCheck] Fehler: Konnte keine Release-Informationen abrufen");
                        MessageBox.Show("Konnte Update-Informationen nicht abrufen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Trace.WriteLine($"[UpdateCheck] GitHub Tag: {latestRelease.TagName}");
                    Console.WriteLine($"[UpdateCheck] GitHub Tag: {latestRelease.TagName}");
                    var latestVersion = ParseVersion(latestRelease.TagName);
                    Trace.WriteLine($"[UpdateCheck] Geparste Version: {latestVersion}");
                    Console.WriteLine($"[UpdateCheck] Geparste Version: {latestVersion}");

                    if (latestVersion > currentVersion)
                    {
                        Trace.WriteLine($"[UpdateCheck] Neue Version verfügbar: {latestVersion} > {currentVersion}");
                        Console.WriteLine($"[UpdateCheck] Neue Version verfügbar: {latestVersion} > {currentVersion}");
                        var result = MessageBox.Show(
                            $"Neue Version verfügbar!\n\n" +
                            $"Installierte Version: {currentVersion}\n" +
                            $"Verfügbare Version: {latestVersion}\n\n" +
                            $"Möchten Sie die neue Version jetzt installieren?",
                            "Update verfügbar",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            Trace.WriteLine($"[UpdateCheck] Starte Download für Version {latestVersion}");
                            Console.WriteLine($"[UpdateCheck] Starte Download für Version {latestVersion}");
                            await DownloadAndInstallUpdateAsync(latestRelease);
                        }
                    }
                    else
                    {
                        Trace.WriteLine($"[UpdateCheck] Bereits aktuell: {currentVersion} >= {latestVersion}");
                        Console.WriteLine($"[UpdateCheck] Bereits aktuell: {currentVersion} >= {latestVersion}");
                        MessageBox.Show(
                            $"Sie verwenden bereits die neueste Version.\n\nVersion: {currentVersion}",
                            "Auf dem neuesten Stand",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[UpdateCheck] Fehler: {ex.Message}");
                    Console.WriteLine($"[UpdateCheck] Fehler: {ex.Message}");
                    Trace.WriteLine($"[UpdateCheck] Stack Trace: {ex.StackTrace}");
                    Console.WriteLine($"[UpdateCheck] Stack Trace: {ex.StackTrace}");
                    MessageBox.Show($"Fehler bei der Update-Überprüfung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.Content = "Nach Updates suchen";
                    btn.IsEnabled = true;
                }
            }
        }

        private async Task DownloadAndInstallUpdateAsync(GitHubRelease release)
        {
            try
            {
                Trace.WriteLine("[UpdateCheck] ========== Download und Installation gestartet ==========");
                Console.WriteLine("[UpdateCheck] ========== Download und Installation gestartet ==========");

                // Debug: Zeige alle Assets
                if (release.Assets != null && release.Assets.Count > 0)
                {
                    Trace.WriteLine($"[UpdateCheck] Gefundene Assets: {release.Assets.Count}");
                    Console.WriteLine($"[UpdateCheck] Gefundene Assets: {release.Assets.Count}");
                    foreach (var asset in release.Assets)
                    {
                        Trace.WriteLine($"[UpdateCheck]   - {asset.Name} ({asset.Size} bytes)");
                        Console.WriteLine($"[UpdateCheck]   - {asset.Name} ({asset.Size} bytes)");
                    }
                }
                else
                {
                    Trace.WriteLine("[UpdateCheck] Warnung: Keine Assets im Release gefunden!");
                    Console.WriteLine("[UpdateCheck] Warnung: Keine Assets im Release gefunden!");
                }

                // Find Setup-Installer (neTiPx_Setup_*.exe)
                var setupAsset = release.Assets?.FirstOrDefault(a =>
                    a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (setupAsset == null)
                {
                    Trace.WriteLine("[UpdateCheck] Fehler: Kein Setup-Installer im Release gefunden");
                    Console.WriteLine("[UpdateCheck] Fehler: Kein Setup-Installer im Release gefunden");
                    MessageBox.Show("Fehler: Kein Setup-Installer (neTiPx_Setup_*.exe) im Release gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Trace.WriteLine($"[UpdateCheck] Setup-Installer gefunden: {setupAsset.Name}");
                Console.WriteLine($"[UpdateCheck] Setup-Installer gefunden: {setupAsset.Name}");
                Trace.WriteLine($"[UpdateCheck] Download-URL: {setupAsset.BrowserDownloadUrl}");
                Console.WriteLine($"[UpdateCheck] Download-URL: {setupAsset.BrowserDownloadUrl}");
                Trace.WriteLine($"[UpdateCheck] Dateigröße: {setupAsset.Size / 1024 / 1024} MB");
                Console.WriteLine($"[UpdateCheck] Dateigröße: {setupAsset.Size / 1024 / 1024} MB");

                // Download to temp file
                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), setupAsset.Name);
                Trace.WriteLine($"[UpdateCheck] Temp-Datei-Pfad: {tempPath}");
                Console.WriteLine($"[UpdateCheck] Temp-Datei-Pfad: {tempPath}");

                try
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "neTiPx-UpdateInstaller");
                        client.Timeout = TimeSpan.FromSeconds(300); // 5 Minuten

                        Trace.WriteLine("[UpdateCheck] Starte HTTP GET Request...");
                        Console.WriteLine("[UpdateCheck] Starte HTTP GET Request...");

                        using (var response = await client.GetAsync(setupAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            Trace.WriteLine($"[UpdateCheck] HTTP Status: {(int)response.StatusCode} {response.StatusCode}");
                            Console.WriteLine($"[UpdateCheck] HTTP Status: {(int)response.StatusCode} {response.StatusCode}");

                            if (!response.IsSuccessStatusCode)
                            {
                                throw new Exception($"HTTP Error {(int)response.StatusCode}: {response.StatusCode}");
                            }

                            var totalBytes = response.Content.Headers.ContentLength ?? -1;
                            Trace.WriteLine($"[UpdateCheck] Download-Größe von Server: {(totalBytes / 1024 / 1024)} MB");
                            Console.WriteLine($"[UpdateCheck] Download-Größe von Server: {(totalBytes / 1024 / 1024)} MB");

                            Trace.WriteLine("[UpdateCheck] Erstelle Temp-Datei...");
                            Console.WriteLine("[UpdateCheck] Erstelle Temp-Datei...");

                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            using (var fileStream = System.IO.File.Create(tempPath))
                            {
                                await contentStream.CopyToAsync(fileStream);
                            }

                            Trace.WriteLine($"[UpdateCheck] Datei-Download abgeschlossen");
                            Console.WriteLine($"[UpdateCheck] Datei-Download abgeschlossen");

                            // Verify file was written
                            if (System.IO.File.Exists(tempPath))
                            {
                                var fileInfo = new System.IO.FileInfo(tempPath);
                                Trace.WriteLine($"[UpdateCheck] Datei vorhanden: {fileInfo.Length} bytes");
                                Console.WriteLine($"[UpdateCheck] Datei vorhanden: {fileInfo.Length} bytes");
                            }
                            else
                            {
                                throw new Exception("Temp-Datei wurde nicht erstellt!");
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    Trace.WriteLine($"[UpdateCheck] HTTP-Request-Fehler: {ex.Message}");
                    Console.WriteLine($"[UpdateCheck] HTTP-Request-Fehler: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[UpdateCheck] Fehler beim Download: {ex.Message}");
                    Console.WriteLine($"[UpdateCheck] Fehler beim Download: {ex.Message}");
                    Trace.WriteLine($"[UpdateCheck] Stack Trace: {ex.StackTrace}");
                    Console.WriteLine($"[UpdateCheck] Stack Trace: {ex.StackTrace}");
                    throw;
                }

                Trace.WriteLine($"[UpdateCheck] Download abgeschlossen: {tempPath}");
                Console.WriteLine($"[UpdateCheck] Download abgeschlossen: {tempPath}");

                // Verify installer file exists
                if (!System.IO.File.Exists(tempPath))
                {
                    throw new Exception($"Installer-Datei konnte nicht erstellt werden: {tempPath}");
                }

                var installerFileInfo = new System.IO.FileInfo(tempPath);
                Trace.WriteLine($"[UpdateCheck] Installer-Dateigröße: {installerFileInfo.Length} bytes");
                Console.WriteLine($"[UpdateCheck] Installer-Dateigröße: {installerFileInfo.Length} bytes");

                // Close current app
                Trace.WriteLine("[UpdateCheck] Schließe aktuelle Instanz...");
                Console.WriteLine("[UpdateCheck] Schließe aktuelle Instanz...");

                // Start installer
                Trace.WriteLine($"[UpdateCheck] Starte Installer: {tempPath}");
                Console.WriteLine($"[UpdateCheck] Starte Installer: {tempPath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                // Wait a bit then exit current application
                await Task.Delay(1000);

                Trace.WriteLine("[UpdateCheck] Beende aktuelle Instanz...");
                Console.WriteLine("[UpdateCheck] Beende aktuelle Instanz...");
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[UpdateCheck] Fehler bei Update-Installation: {ex.Message}");
                Console.WriteLine($"[UpdateCheck] Fehler bei Update-Installation: {ex.Message}");
                Trace.WriteLine($"[UpdateCheck] Stack Trace: {ex.StackTrace}");
                Console.WriteLine($"[UpdateCheck] Stack Trace: {ex.StackTrace}");
                MessageBox.Show($"Fehler bei der Update-Installation: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Version GetCurrentVersion()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            return asm.GetName().Version ?? new Version(0, 0, 0, 0);
        }

        private Version ParseVersion(string tagName)
        {
            // Remove 'v' prefix if present (e.g., "v1.6.1.1" -> "1.6.1.1")
            var versionString = tagName.TrimStart('v', 'V');
            Trace.WriteLine($"[UpdateCheck] Parse Version - Input: '{tagName}' -> Bereinigt: '{versionString}'");
            Console.WriteLine($"[UpdateCheck] Parse Version - Input: '{tagName}' -> Bereinigt: '{versionString}'");

            if (Version.TryParse(versionString, out var version))
            {
                Trace.WriteLine($"[UpdateCheck] Parse Version - Erfolgreich: {version}");
                Console.WriteLine($"[UpdateCheck] Parse Version - Erfolgreich: {version}");
                return version;
            }

            Trace.WriteLine($"[UpdateCheck] Parse Version - Fehler beim Parsen von '{versionString}'");
            Console.WriteLine($"[UpdateCheck] Parse Version - Fehler beim Parsen von '{versionString}'");
            return new Version(0, 0, 0, 0);
        }

        private async Task<GitHubRelease?> GetLatestGitHubReleaseAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "neTiPx-UpdateChecker");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var url = "https://api.github.com/repos/Flecky13/neTiPx-V2/releases/latest";
                    Trace.WriteLine($"[UpdateCheck] Rufe GitHub API ab: {url}");
                    Console.WriteLine($"[UpdateCheck] Rufe GitHub API ab: {url}");

                    try
                    {
                        var response = await client.GetStringAsync(url);
                        Trace.WriteLine($"[UpdateCheck] API Antwort erhalten (Länge: {response.Length} Zeichen)");
                        Console.WriteLine($"[UpdateCheck] API Antwort erhalten (Länge: {response.Length} Zeichen)");

                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        };

                        var release = JsonSerializer.Deserialize<GitHubRelease>(response, options);

                        if (release != null)
                        {
                            Trace.WriteLine($"[UpdateCheck] Deserialisiert - TagName: '{release.TagName}', Name: '{release.Name}'");
                            Console.WriteLine($"[UpdateCheck] Deserialisiert - TagName: '{release.TagName}', Name: '{release.Name}'");
                        }

                        return release;
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Trace.WriteLine("[UpdateCheck] Fehler 404: Keine veröffentlichten Releases gefunden.");
                        Console.WriteLine("[UpdateCheck] Fehler 404: Keine veröffentlichten Releases gefunden.");
                        Trace.WriteLine("[UpdateCheck] Versuche alternativen Endpoint (alle Releases)...");
                        Console.WriteLine("[UpdateCheck] Versuche alternativen Endpoint (alle Releases)...");

                        // Fallback: Versuche alle Releases zu holen und nimm die neueste
                        var altUrl = "https://api.github.com/repos/Flecky13/neTiPx-V2/releases?per_page=1";
                        var altResponse = await client.GetStringAsync(altUrl);

                        var options2 = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                        };

                        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(altResponse, options2);

                        if (releases?.Count > 0)
                        {
                            Trace.WriteLine("[UpdateCheck] Alte Release gefunden (Fallback)");
                            Console.WriteLine("[UpdateCheck] Alte Release gefunden (Fallback)");
                            return releases[0];
                        }

                        Trace.WriteLine("[UpdateCheck] Auch Fallback-Endpoint hat keine Releases");
                        Console.WriteLine("[UpdateCheck] Auch Fallback-Endpoint hat keine Releases");
                        return null;
                    }
                    catch (HttpRequestException ex)
                    {
                        Trace.WriteLine($"[UpdateCheck] HTTP-Fehler: {ex.Message}");
                        Console.WriteLine($"[UpdateCheck] HTTP-Fehler: {ex.Message}");
                        if (ex.StatusCode.HasValue)
                        {
                            Trace.WriteLine($"[UpdateCheck] Status Code: {(int)ex.StatusCode}");
                            Console.WriteLine($"[UpdateCheck] Status Code: {(int)ex.StatusCode}");
                        }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[UpdateCheck] Fehler beim GitHub API Aufruf: {ex.Message}");
                Console.WriteLine($"[UpdateCheck] Fehler beim GitHub API Aufruf: {ex.Message}");
                throw;
            }
        }

        private class GitHubRelease
        {
            [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("html_url")]
            public string HtmlUrl { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("assets")]
            public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();
        }

        private class ReleaseAsset
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("size")]
            public long Size { get; set; }
        }

        // --- WiFi Networks Tab ---
        private async void BtnScanWifi_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = false;
                    btn.Content = "Scanne...";

                    try
                    {
                        // Run scan on background thread
                        var networks = await Task.Run(() => WifiScanner.ScanWifiNetworks());

                        // Update UI on main thread
                        await this.Dispatcher.InvokeAsync(() =>
                        {
                            DisplayWifiNetworks(networks);
                        });
                    }
                    finally
                    {
                        btn.Content = "Netzwerke scannen";
                        btn.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WiFi] Fehler beim Scannen: {ex.Message}");
                Console.WriteLine($"[WiFi] Fehler beim Scannen: {ex.Message}");

                if (sender is Button btn2)
                {
                    btn2.Content = "Netzwerke scannen";
                    btn2.IsEnabled = true;
                }
            }
        }

        private void DisplayWifiNetworks(List<WifiNetwork> networks)
        {
            try
            {
                if (this.FindName("WifiDataGrid") is not System.Windows.Controls.DataGrid dataGrid)
                {
                    Trace.WriteLine("[WiFi] WifiDataGrid konnte nicht gefunden werden");
                    return;
                }

                // Always reset ItemsSource first to clear any existing bindings
                dataGrid.ItemsSource = null;

                if (networks == null || networks.Count == 0)
                {
                    Trace.WriteLine("[WiFi] Keine Netzwerke gefunden");
                    return;
                }

                dataGrid.ItemsSource = networks;
                Trace.WriteLine($"[WiFi] {networks.Count} Netzwerke angezeigt");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WiFi] Fehler beim Anzeigen: {ex.Message}");
                Trace.WriteLine($"[WiFi] Stack Trace: {ex.StackTrace}");
                Console.WriteLine($"[WiFi] Fehler beim Anzeigen: {ex.Message}");
            }
        }

        private void WifiDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.DataGrid dataGrid && dataGrid.SelectedItem is WifiNetwork network)
                {
                    var detailsWindow = new WifiDetailsWindow(network)
                    {
                        Owner = this
                    };
                    detailsWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[WiFi] Fehler beim Öffnen des Detail-Fensters: {ex.Message}");
                MessageBox.Show($"Fehler beim Anzeigen der Details: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Speichert den Index des aktuell ausgewählten IP-Tabs in der INI-Datei.
        /// </summary>
        private void SaveLastSelectedIpTabName(IpTabData data)
        {
            try
            {
                // Finde die Position dieses Tabs im TabControl
                int tabIndex = -1;
                if (IpTabsControl != null)
                {
                    for (int i = 0; i < IpTabsControl.Items.Count; i++)
                    {
                        if (IpTabsControl.Items[i] == data.Tab)
                        {
                            tabIndex = i;
                            break;
                        }
                    }
                }

                if (tabIndex >= 0)
                {
                    var iniPath = ConfigFileHelper.GetConfigIniPath();
                    var values = ReadIniToDict(iniPath);

                    // Speichere die Position (0-basiert) des aktuellen Tabs
                    values["LastSelectedIpTabPosition"] = tabIndex.ToString();

                    // Schreibe zurück
                    WriteDictToIni(iniPath, values);

                    Debug.WriteLine($"[ConfigWindow] Gespeichert: LastSelectedIpTabPosition = {tabIndex}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigWindow] Fehler beim Speichern des IP-Tabs: {ex.Message}");
            }
        }

        /// <summary>
        /// Stellt den zuletzt ausgewählten IP-Tab wieder her und wählt ihn aus.
        /// </summary>
        private void RestoreLastSelectedIpTabName()
        {
            try
            {
                if (IpTabsControl == null || _ipTabs.Count == 0)
                {
                    Debug.WriteLine("[ConfigWindow] IpTabsControl oder _ipTabs ist leer");
                    return;
                }

                var iniPath = ConfigFileHelper.GetConfigIniPath();
                var values = ReadIniToDict(iniPath);

                if (values.TryGetValue("LastSelectedIpTabPosition", out var posStr) && int.TryParse(posStr, out var position))
                {
                    Debug.WriteLine($"[ConfigWindow] Versuche, Tab an Position {position} wiederherzustellen");

                    // Wähle den Tab an dieser Position
                    if (position >= 0 && position < IpTabsControl.Items.Count)
                    {
                        IpTabsControl.SelectedIndex = position;
                        Debug.WriteLine($"[ConfigWindow] IP-Tab an Position {position} wiederhergestellt");
                        return;
                    }
                    else
                    {
                        Debug.WriteLine($"[ConfigWindow] Position {position} ist außerhalb des Bereichs (Tabs: {IpTabsControl.Items.Count})");
                    }
                }

                // Fallback: Wähle ersten Tab
                if (IpTabsControl.Items.Count > 0)
                {
                    IpTabsControl.SelectedIndex = 0;
                    Debug.WriteLine("[ConfigWindow] Fallback: Erster IP-Tab ausgewählt");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigWindow] Fehler beim Wiederherstellen des IP-Tabs: {ex.Message}");
            }
        }
    }
}
