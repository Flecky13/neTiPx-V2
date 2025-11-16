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

        public ConfigWindow()
        {
            InitializeComponent();
            LoadAdapters();
        }

        private void LoadAdapters()
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(a => a.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(a => a.GetPhysicalAddress() != null && a.GetPhysicalAddress().GetAddressBytes().Length > 0)
                    .ToList();

                // Load existing selections from config.ini
                string iniPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                string? sel1 = null, sel2 = null;
                if (System.IO.File.Exists(iniPfad))
                {
                    var lines = System.IO.File.ReadAllLines(iniPfad);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Adapter1", StringComparison.OrdinalIgnoreCase))
                            sel1 = line.Split('=')[1].Trim();
                        else if (line.StartsWith("Adapter2", StringComparison.OrdinalIgnoreCase))
                            sel2 = line.Split('=')[1].Trim();
                    }
                }

                foreach (var a in adapters)
                {
                    string display = a.Name + " - " + a.Description;
                    var cb = new CheckBox { Content = display, Tag = a.Name, Margin = new Thickness(4) };
                    if (!string.IsNullOrEmpty(sel1) && sel1.Equals(a.Name, StringComparison.OrdinalIgnoreCase)) cb.IsChecked = true;
                    if (!string.IsNullOrEmpty(sel2) && sel2.Equals(a.Name, StringComparison.OrdinalIgnoreCase)) cb.IsChecked = true;
                    cb.Checked += Cb_CheckedChanged;
                    cb.Unchecked += Cb_CheckedChanged;
                    _checkboxes.Add(cb);
                    AdaptersPanel.Children.Add(cb);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Laden der Adapter: " + ex.Message);
            }
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
                    cb.IsChecked = false;
                }
                MessageBox.Show("Maximal 2 Adapter können ausgewählt werden.");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var selected = _checkboxes.Where(c => c.IsChecked == true).Select(c => c.Tag?.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();

            string iniPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            var lines = new List<string>();
            lines.Add($"Adapter1={(selected.Count > 0 ? selected[0] : string.Empty)}");
            lines.Add($"Adapter2={(selected.Count > 1 ? selected[1] : string.Empty)}");

            try
            {
                System.IO.File.WriteAllLines(iniPfad, lines);
                MessageBox.Show("Konfiguration gespeichert.");
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
    }
}
