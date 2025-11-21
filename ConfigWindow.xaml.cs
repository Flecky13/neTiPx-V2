using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;

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

        private class IpTabData
        {
            public int Index;
            public TabItem Tab;
            public ComboBox AdapterCombo;
            public TextBox TxtName;
            public RadioButton RbDhcp;
            public RadioButton RbManual;
            public TextBox TxtIP;
            public TextBox TxtSubnet;
            public TextBox TxtGateway;
            public TextBox TxtDNS;
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

                // Refresh lists when the user switches tabs so INI is always authoritative
                try
                {
                    if (this.FindName("TabControlMain") is TabControl tc)
                    {
                        tc.SelectionChanged += TabControlMain_SelectionChanged;
                    }
                }
                catch { }
            }
            finally
            {
                ExitSuspendEvents();
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
                string iniPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
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
                    if (header.IndexOf("Adapter", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadAdapters();
                    }
                    else if (header.IndexOf("IP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LoadIpSettings();
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
                var iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
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
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern: " + ex.Message);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
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

                    var iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
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
            var spName = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            spName.Children.Add(new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var txtName = new TextBox { Width = 260 };
            spName.Children.Add(txtName);
            panel.Children.Add(spName);

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
            var sel = (data.AdapterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
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
                var iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
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

                var iniPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                WriteDictToIni(iniPfad, values);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Speichern der IP-Einstellungen: " + ex.Message);
            }
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
    }
}
