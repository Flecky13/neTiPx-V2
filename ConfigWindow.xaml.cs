using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
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
        private readonly List<CheckBox> _checkboxes = new List<CheckBox>();
        private int _suspendEventCount = 0;

        private bool EventsSuspended => _suspendEventCount > 0;
        private void EnterSuspendEvents() => _suspendEventCount++;
        private void ExitSuspendEvents() { if (_suspendEventCount > 0) _suspendEventCount--; }

        // Dynamic IP tabs
        private const int MaxIpTabs = 10;
        private readonly List<IpTabData> _ipTabs = new List<IpTabData>();

        // Ping tools
        private const int MaxPingEntries = 6;
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

        private class IpTabData
        {
            public int Index;
            public TabItem? Tab;
            public ComboBox? AdapterCombo;
            public TextBox? TxtName;
            public RadioButton? RbDhcp;
            public RadioButton? RbManual;
            public TextBox? TxtIP;
            public TextBox? TxtSubnet;
            public TextBox? TxtGateway;
            public TextBox? TxtDNS;
            public System.Windows.Threading.DispatcherTimer? PingTimer;
            public Label? LblGwStatus;
        }

        public ConfigWindow()
        {
            InitializeComponent();
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
                        var ip = data.TxtIP?.Text.Trim() ?? string.Empty;
                        var mask = data.TxtSubnet?.Text.Trim() ?? string.Empty;
                        var gw = data.TxtGateway?.Text.Trim() ?? string.Empty;
                        var dns = data.TxtDNS?.Text.Trim() ?? string.Empty;

                        if (!IsValidIPv4(ip) || !IsValidIPv4(mask))
                        {
                            MessageBox.Show("IP oder Subnetz ungültig. Bitte prüfen.");
                            return;
                        }

                        string addrCmd = $"netsh interface ipv4 set address name=\"{ni.Name}\" source=static addr={ip} mask={mask}";
                        if (IsValidIPv4(gw)) addrCmd += $" gateway={gw} gwmetric=1";
                        commands.Add(addrCmd);

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
                    }
                    catch { }

                    MessageBox.Show("Einstellungen angewendet.");
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
                        try
                        {
                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            var ver = asm.GetName().Version?.ToString() ?? "?";
                            if (this.FindName("InfoText") is TextBlock tb)
                            {
                                tb.Inlines.Clear();
                                tb.FontFamily = new System.Windows.Media.FontFamily("Consolas");
                                tb.FontSize = 12;

                                tb.Inlines.Add(new Run("neTiPx - Netzwerk Infos - by Pedro Tepe"));
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new LineBreak());

                                if (this.FindName("InfoVersionText") is TextBlock vt)
                                {
                                    vt.Text = "Version: " + ver;
                                }

                                tb.Inlines.Add(new Run("Lizenz:"));
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new Run("Dieses Programm steht unter der im Repository angegebenen Lizenz."));
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new LineBreak());

                                tb.Inlines.Add(new Run("Autor:"));
                                tb.Inlines.Add(new LineBreak());
                                var email = "github@hometepe.de";
                                var mailText = "Flecky13 - " + email;
                                var mailLink = new Hyperlink(new Run(mailText)) { NavigateUri = new Uri("mailto:" + email) };
                                mailLink.Click += (s2, e2) =>
                                {
                                    try { Process.Start(new ProcessStartInfo(mailLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                                };
                                tb.Inlines.Add(mailLink);
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new LineBreak());

                                tb.Inlines.Add(new Run("Repository:"));
                                tb.Inlines.Add(new LineBreak());
                                var repoUrl = "https://github.com/Flecky13/neTiPx-V2";
                                var repoLink = new Hyperlink(new Run(repoUrl)) { NavigateUri = new Uri(repoUrl) };
                                repoLink.Click += (s3, e3) =>
                                {
                                    try { Process.Start(new ProcessStartInfo(repoLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                                };
                                tb.Inlines.Add(repoLink);
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new LineBreak());

                                // Support link (BuyMeACoffee)
                                tb.Inlines.Add(new Run("Support:"));
                                tb.Inlines.Add(new LineBreak());
                                var supportUrl = "https://buymeacoffee.com/pedrotepe";
                                var supportLink = new Hyperlink(new Run(supportUrl)) { NavigateUri = new Uri(supportUrl) };
                                supportLink.Click += (s4, e4) =>
                                {
                                    try { Process.Start(new ProcessStartInfo(supportLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                                };
                                tb.Inlines.Add(supportLink);
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new LineBreak());

                                tb.Inlines.Add(new Run("Beschreibung:"));
                                tb.Inlines.Add(new LineBreak());
                                tb.Inlines.Add(new Run("Kleine Tray-App zur Anzeige von Netzwerk- und IP-Informationen."));
                            }

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
                        catch { }
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
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
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

                // If opened from MainWindow, refresh its display immediately
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        mw.UpdateGui();
                    }
                }
                catch { }

                MessageBox.Show("Konfiguration gespeichert.");

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
            this.Close();
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

            // Container
            var panel = new StackPanel { Margin = new Thickness(6) };

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

            // Fields grid
            var border = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), CornerRadius = new CornerRadius(4) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 4; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0
            var lblIp = new TextBlock { Text = "IP:", Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lblIp, 0); Grid.SetColumn(lblIp, 0); grid.Children.Add(lblIp);
            var txtIP = new TextBox { Margin = new Thickness(6) }; Grid.SetRow(txtIP, 0); Grid.SetColumn(txtIP, 1); grid.Children.Add(txtIP);

            // Row 1
            var lblSubnet = new TextBlock { Text = "Subnetz:", Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lblSubnet, 1); Grid.SetColumn(lblSubnet, 0); grid.Children.Add(lblSubnet);
            var txtSubnet = new TextBox { Margin = new Thickness(6) }; Grid.SetRow(txtSubnet, 1); Grid.SetColumn(txtSubnet, 1); grid.Children.Add(txtSubnet);

            // Row 2
            var lblGw = new TextBlock { Text = "Gateway:", Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lblGw, 2); Grid.SetColumn(lblGw, 0); grid.Children.Add(lblGw);
            var txtGw = new TextBox { Margin = new Thickness(6) }; Grid.SetRow(txtGw, 2); Grid.SetColumn(txtGw, 1); grid.Children.Add(txtGw);

            // Row 3
            var lblDns = new TextBlock { Text = "DNS:", Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lblDns, 3); Grid.SetColumn(lblDns, 0); grid.Children.Add(lblDns);
            var txtDns = new TextBox { Margin = new Thickness(6) }; Grid.SetRow(txtDns, 3); Grid.SetColumn(txtDns, 1); grid.Children.Add(txtDns);

            border.Child = grid;
            panel.Children.Add(border);

            var tab = new TabItem { Header = $"IP #{index}", Content = panel };

            // Populate adapters from global INI Adapter1/Adapter2
            if (iniValues.TryGetValue("Adapter1", out var a1) && !string.IsNullOrEmpty(a1)) cbAdapter.Items.Add(new ComboBoxItem { Content = a1, Tag = a1 });
            if (iniValues.TryGetValue("Adapter2", out var a2) && !string.IsNullOrEmpty(a2)) cbAdapter.Items.Add(new ComboBoxItem { Content = a2, Tag = a2 });
            if (cbAdapter.Items.Count > 0) cbAdapter.SelectedIndex = 0;

            // Wire events
            cbAdapter.SelectionChanged += (s, e) => { if (!EventsSuspended && rbManual.IsChecked == true) LoadSystemValuesIntoTab(data); };
            rbDhcp.Checked += (s, e) => { if (!EventsSuspended) SetIpFieldsEnabledForTab(data, false); };
            rbManual.Checked += (s, e) => { if (!EventsSuspended) { SetIpFieldsEnabledForTab(data, true); LoadSystemValuesIntoTab(data); } };

            data.Tab = tab;
            data.AdapterCombo = cbAdapter;
            data.RbDhcp = rbDhcp;
            data.RbManual = rbManual;
            data.TxtIP = txtIP;
            data.TxtSubnet = txtSubnet;
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
                if (iniValues.TryGetValue(profileKey + ".Mode", out var pm) && pm.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    rbManual.IsChecked = true; SetIpFieldsEnabledForTab(data, true); loaded = true;
                }
                else if (iniValues.TryGetValue(profileKey + ".Mode", out var _))
                {
                    rbDhcp.IsChecked = true; SetIpFieldsEnabledForTab(data, false); loaded = true;
                }
                if (iniValues.TryGetValue(profileKey + ".IP", out var pip)) txtIP.Text = pip;
                if (iniValues.TryGetValue(profileKey + ".Subnet", out var psn)) txtSubnet.Text = psn;
                if (iniValues.TryGetValue(profileKey + ".GW", out var pgw)) txtGw.Text = pgw;
                if (iniValues.TryGetValue(profileKey + ".DNS", out var pdns)) txtDns.Text = pdns;
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
                if (iniValues.TryGetValue(prefix + "Mode", out var im) && im.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    rbManual.IsChecked = true; SetIpFieldsEnabledForTab(data, true);
                }
                else { rbDhcp.IsChecked = true; SetIpFieldsEnabledForTab(data, false); }

                if (iniValues.TryGetValue(prefix + "IP", out var ip)) txtIP.Text = ip;
                if (iniValues.TryGetValue(prefix + "Subnet", out var sn)) txtSubnet.Text = sn;
                if (iniValues.TryGetValue(prefix + "GW", out var gw)) txtGw.Text = gw;
                if (iniValues.TryGetValue(prefix + "DNS", out var dns)) txtDns.Text = dns;
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

            return data;
        }

        private void SetIpFieldsEnabledForTab(IpTabData data, bool enabled)
        {
            if (data == null) return;
            if (data.TxtIP != null) data.TxtIP.IsEnabled = enabled;
            if (data.TxtSubnet != null) data.TxtSubnet.IsEnabled = enabled;
            if (data.TxtGateway != null) data.TxtGateway.IsEnabled = enabled;
            if (data.TxtDNS != null) data.TxtDNS.IsEnabled = enabled;
        }

        private void LoadSystemValuesIntoTab(IpTabData data)
        {
            if (data == null) return;
            var sel = (data.AdapterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(sel)) return;
            var sys = GetSystemIpv4Settings(sel);
            if (sys != null)
            {
                if (data.TxtIP != null) data.TxtIP.Text = sys.Value.ip ?? string.Empty;
                if (data.TxtSubnet != null) data.TxtSubnet.Text = sys.Value.subnet ?? string.Empty;
                if (data.TxtGateway != null) data.TxtGateway.Text = sys.Value.gateway ?? string.Empty;
                if (data.TxtDNS != null) data.TxtDNS.Text = sys.Value.dns ?? string.Empty;
            }
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
        }

        private void BtnRemoveIpTab_Click(object sender, RoutedEventArgs e)
        {
            if (IpTabsControl == null) return;
            if (IpTabsControl.SelectedItem is TabItem ti)
            {
                var data = _ipTabs.FirstOrDefault(t => t.Tab == ti);
                if (data != null)
                {
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
                    values[$"{name}.Mode"] = (t.RbDhcp != null && t.RbDhcp.IsChecked == true) ? "DHCP" : "Manual";
                    values[$"{name}.IP"] = t.TxtIP?.Text.Trim() ?? string.Empty;
                    values[$"{name}.Subnet"] = t.TxtSubnet?.Text.Trim() ?? string.Empty;
                    values[$"{name}.GW"] = t.TxtGateway?.Text.Trim() ?? string.Empty;
                    values[$"{name}.DNS"] = t.TxtDNS?.Text.Trim() ?? string.Empty;
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
            var p = new PingEntryData();
            _pingEntries.Add(p);
            CreatePingRow(p, _pingEntries.Count - 1);
        }

        private void BtnRemovePingEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_pingEntries.Count == 0) return;
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
            try { StopAllPingTimers(); } catch { }
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

                        var asm = System.Reflection.Assembly.GetExecutingAssembly();
                        var ver = asm.GetName().Version?.ToString() ?? "?";
                        if (this.FindName("InfoText") is TextBlock tb)
                        {
                            tb.Inlines.Clear();
                            tb.FontFamily = new System.Windows.Media.FontFamily("Consolas");
                            tb.FontSize = 12;

                            tb.Inlines.Add(new Run("neTiPx - Netzwerk Infos - by Pedro Tepe"));
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new LineBreak());

                            if (this.FindName("InfoVersionText") is TextBlock vt)
                            {
                                vt.Text = "Version: " + ver;
                            }

                            tb.Inlines.Add(new Run("Lizenz:"));
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new Run("Dieses Programm steht unter der im Repository angegebenen Lizenz."));
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new LineBreak());

                            tb.Inlines.Add(new Run("Autor:"));
                            tb.Inlines.Add(new LineBreak());
                            // Mailto hyperlink
                            var email = "github@hometepe.de";
                            var mailText = "Flecky13 - " + email;
                            var mailLink = new Hyperlink(new Run(mailText)) { NavigateUri = new Uri("mailto:" + email) };
                            mailLink.Click += (s2, e2) =>
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo(mailLink.NavigateUri.AbsoluteUri) { UseShellExecute = true });
                                }
                                catch { }
                            };
                            tb.Inlines.Add(mailLink);
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new LineBreak());

                            tb.Inlines.Add(new Run("Repository:"));
                            tb.Inlines.Add(new LineBreak());
                            var repoUrl = "https://github.com/Flecky13/neTiPx-V2";
                            var repoLink = new Hyperlink(new Run(repoUrl)) { NavigateUri = new Uri(repoUrl) };
                            repoLink.Click += (s3, e3) =>
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo(repoLink.NavigateUri.AbsoluteUri) { UseShellExecute = true });
                                }
                                catch { }
                            };
                            tb.Inlines.Add(repoLink);
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new LineBreak());

                            // Support link (BuyMeACoffee)
                            tb.Inlines.Add(new Run("Support:"));
                            tb.Inlines.Add(new LineBreak());
                            var supportUrl = "https://buymeacoffee.com/pedrotepe";
                            var supportLink = new Hyperlink(new Run(supportUrl)) { NavigateUri = new Uri(supportUrl) };
                            supportLink.Click += (s4, e4) =>
                            {
                                try { Process.Start(new ProcessStartInfo(supportLink.NavigateUri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                            };
                            tb.Inlines.Add(supportLink);
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new LineBreak());

                            tb.Inlines.Add(new Run("Beschreibung:"));
                            tb.Inlines.Add(new LineBreak());
                            tb.Inlines.Add(new Run("Kleine Tray-App zur Anzeige von Netzwerk- und IP-Informationen."));
                        }

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
                    finally
                    {
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
                if (this.FindName("TabControlMain") is TabControl tc)
                {
                    EnterSuspendEvents();
                    try
                    {
                        for (int i = 0; i < tc.Items.Count; i++)
                        {
                            if (tc.Items[i] is TabItem ti && (ti.Header?.ToString() ?? string.Empty).IndexOf("Tools", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                tc.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    finally { ExitSuspendEvents(); }
                }
            }
            catch { }
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
                                $"Möchten Sie die neue Version jetzt herunterladen?",
                                "Update verfügbar",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);

                            if (result == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo(latestRelease.HtmlUrl) { UseShellExecute = true });
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Fehler beim Öffnen des Browsers: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
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
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "neTiPx-UpdateChecker");
                client.Timeout = TimeSpan.FromSeconds(10);

                var url = "https://api.github.com/repos/Flecky13/neTiPx-V2/releases/latest";
                Trace.WriteLine($"[UpdateCheck] Rufe GitHub API ab: {url}");
                Console.WriteLine($"[UpdateCheck] Rufe GitHub API ab: {url}");
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
        }    }
}
