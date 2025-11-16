using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace neTiPx
{
    public partial class MainWindow : Window
    {
        // Internet- / Timer-Logik wurde ins ViewModel verschoben

        public MainWindow()
        {
            InitializeComponent();
            Debug.WriteLine("[MainWindow] Konstruktor initialisiert");

            //timer.Interval = TimeSpan.FromMinutes(1);
            //timer.Tick += Timer_Tick;

            // Positionierung nach Rendern
            this.ContentRendered += MainWindow_ContentRendered;
            this.SizeChanged += MainWindow_SizeChanged;
            // Direkt auf die Tray-Wrapper-Events abonnieren, damit Show/Hide auch nach Hide() funktionieren
            try
            {
                TrayIcon.TrayMouseEnter += (s, e) => ShowWindowFromTray();
                TrayIcon.TrayMouseLeave += (s, e) => HideWindowToTray();
                TrayIcon.ConfigSelected += (s, e) =>
                {
                    try
                    {
                        // Verstecken und Config-Fenster öffnen
                        this.Hide();
                        var cfg = new ConfigWindow();
                        cfg.Owner = this;
                        cfg.ShowDialog();
                        // nach Schließen: verbleiben hidden (App im Tray)
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Fehler beim Öffnen ConfigWindow: {ex.Message}");
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Fehler beim Abonnieren der Tray-Events: {ex.Message}");
            }
            UpdateGui();
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            PositionWindowBottomRight();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PositionWindowBottomRight();
        }

        private void PositionWindowBottomRight()
        {
            var workArea = SystemParameters.WorkArea;

            double windowWidth = this.ActualWidth;
            double windowHeight = this.ActualHeight;

            this.Left = workArea.Right - windowWidth - 10;
            this.Top = workArea.Bottom - windowHeight - 10;
        }

        // Wird vom Tray-Wrapper bei Maus-Over aufgerufen
        public void ShowWindowFromTray()
        {
            Debug.WriteLine("[MainWindow] ShowWindowFromTray aufgerufen");
            Dispatcher.Invoke(async () =>
            {
                // Wenn das Fenster vorher verborgen war, zeigen
                try
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    PositionWindowBottomRight();

                    // Daten aktualisieren via ViewModel
                    UpdateGui();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Fehler in ShowWindowFromTray(): {ex.Message}");
                }
                this.Activate();
            });
        }

        // Wird vom Tray-Wrapper bei Maus-Leave aufgerufen
        public void HideWindowToTray()
        {
            Debug.WriteLine("[MainWindow] HideWindowToTray aufgerufen");
            Dispatcher.Invoke(() =>
            {
                // Komplett ausblenden
                this.Hide();
            });
        }



        private async void Window_Loaded(object? sender, RoutedEventArgs? e)
        {
            UpdateGui();
            PositionWindowBottomRight();
        }

        public async void UpdateGui()
        {
            var vm = this.DataContext as MainWindowViewModel;
            if (vm == null)
                return;

            // IP
            Dispatcher.Invoke(() => { ISPInfos.Content = "Lade IP..."; });
            var ip = await vm.LadeExterneIPAsync();
            Dispatcher.Invoke(() => { ISPInfos.Content = ip ?? "Fehler beim Laden der IP"; });

            // Netzwerkinfos
            var infos = vm.HoleNetzwerkInfos();
            SetzeNetzwerkInfosInUI(infos);
        }

        private void Window_Closed(object? sender, EventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Window_Closed");
        }

        // Netzwerk- und IP-Aktualisierung wurde ins ViewModel verschoben

        private void SetzeNetzwerkInfosInUI((string[,]? nic1, string[,]? nic2) infos)
        {
            static (string labels, string values) FormatInfos(string[,]? arr)
            {
                if (arr == null) return ("", "");

                var sbLabels = new System.Text.StringBuilder();
                var sbValues = new System.Text.StringBuilder();

                int rows = arr.GetLength(0);
                for (int i = 0; i < rows; i++)
                {
                    string label = arr[i, 0] ?? "";
                    string value = arr[i, 1] ?? "";
                    var valueLines = value.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    sbLabels.AppendLine(label);
                    sbValues.AppendLine(valueLines.Length > 0 ? valueLines[0] : "-");

                    for (int v = 1; v < valueLines.Length; v++)
                    {
                        sbLabels.AppendLine("");
                        sbValues.AppendLine(valueLines[v]);
                    }
                    sbLabels.AppendLine("");
                    sbValues.AppendLine("");
                }

                return (sbLabels.ToString().TrimEnd('\r', '\n'), sbValues.ToString().TrimEnd('\r', '\n'));
            }

            var (labels1, values1) = FormatInfos(infos.nic1);
            var (labels2, values2) = FormatInfos(infos.nic2);

            Dispatcher.Invoke(() =>
            {
                if (infos.nic1 != null && infos.nic1.GetLength(0) > 0) NIC1_Name.Content = infos.nic1[0, 1] ?? NIC1_Name.Content;

                NIC1_INFOS1.Text = labels1;
                NIC1_INFOS2.Text = values1;

                // NIC2: falls keine Infos vorhanden sind, komplette Sektion ausblenden
                bool hasNic2 = infos.nic2 != null && infos.nic2.GetLength(0) > 0;
                if (hasNic2)
                {
                    NIC2_Name.Visibility = Visibility.Visible;
                    NIC2_INFOS1.Visibility = Visibility.Visible;
                    NIC2_INFOS2.Visibility = Visibility.Visible;

                    NIC2_Name.Content = infos.nic2[0, 1] ?? NIC2_Name.Content;
                    NIC2_INFOS1.Text = labels2;
                    NIC2_INFOS2.Text = values2;

                    // Restore row sizes for separator, header and infos
                    if (LayoutGrid != null && LayoutGrid.RowDefinitions.Count > 7)
                    {
                        LayoutGrid.RowDefinitions[5].Height = new GridLength(5);
                        LayoutGrid.RowDefinitions[6].Height = new GridLength(25);
                        LayoutGrid.RowDefinitions[7].Height = GridLength.Auto;
                    }
                }
                else
                {
                    // Keine zweite NIC: ausblenden und RowHeights auf 0 setzen
                    NIC2_Name.Visibility = Visibility.Collapsed;
                    NIC2_INFOS1.Visibility = Visibility.Collapsed;
                    NIC2_INFOS2.Visibility = Visibility.Collapsed;

                    if (LayoutGrid != null && LayoutGrid.RowDefinitions.Count > 7)
                    {
                        LayoutGrid.RowDefinitions[5].Height = new GridLength(0);
                        LayoutGrid.RowDefinitions[6].Height = new GridLength(0);
                        LayoutGrid.RowDefinitions[7].Height = new GridLength(0);
                    }
                }
            });
        }
    }
}
