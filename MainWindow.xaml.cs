using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace neTiPx
{
    public partial class MainWindow : Window
    {
        private DateTime _suppressShowUntil = DateTime.MinValue;
        private readonly TimeSpan _suppressDuration = TimeSpan.FromSeconds(5);
        private ConfigWindow? _activeConfigWindow = null;
        // Internet- / Timer-Logik wurde ins ViewModel verschoben

        public MainWindow()
        {
            InitializeComponent();
            Debug.WriteLine("[MainWindow] Konstruktor initialisiert");

            // Globalen Dispatcher Exception Handler registrieren
            Application.Current.Dispatcher.UnhandledException += (s, e) =>
            {
                e.Handled = true;
            };

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
                        OpenOrFocusConfigWindow(tab => tab.SelectAdapterTab());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Fehler beim Öffnen ConfigWindow: {ex.Message}");
                    }
                };
                TrayIcon.InfoSelected += (s, e) =>
                {
                    try
                    {
                        OpenOrFocusConfigWindow(tab => tab.SelectInfoTab());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Fehler beim Öffnen Info-Tab: {ex.Message}");
                    }
                };
                TrayIcon.IpSettingsSelected += (s, e) =>
                {
                    try
                    {
                        OpenOrFocusConfigWindow(tab => tab.SelectIpTab());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Fehler beim Öffnen ConfigWindow (IP Settings): {ex.Message}");
                    }
                };
                TrayIcon.ToolsSelected += (s, e) =>
                {
                    try
                    {
                        OpenOrFocusConfigWindow(tab => tab.SelectToolsTab());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Fehler beim Öffnen Tools-Tab: {ex.Message}");
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

            // Automatische Update-Prüfung beim App-Start
            _ = CheckForUpdatesOnStartupAsync();
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                // Kurze Verzögerung um sicherzustellen, dass UI vollständig geladen ist
                await Task.Delay(500);

                // Erstelle ConfigWindow nur für Update-Prüfung (wird nicht angezeigt)
                var cfg = new ConfigWindow();
                cfg.Owner = this;
                await cfg.CheckForUpdatesAutoAsync();
                cfg.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Fehler bei automatischer Update-Prüfung: {ex.Message}");
            }
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
            // Unterdrückung prüfen
            if (DateTime.Now < _suppressShowUntil)
            {
                Debug.WriteLine($"[MainWindow] Show unterdrückt bis {_suppressShowUntil}");
                return;
            }
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

                    // Falls ConfigWindow offen und IP Settings Tab aktiv: Gateway-Ping-Timer starten
                    if (_activeConfigWindow != null && _activeConfigWindow.IsLoaded)
                    {
                        _activeConfigWindow.RestartGatewayPingTimerIfIpTabActive();
                    }
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
                // Falls ConfigWindow offen: Gateway-Ping-Timer stoppen
                if (_activeConfigWindow != null && _activeConfigWindow.IsLoaded)
                {
                    _activeConfigWindow.StopAllGatewayPingTimers();
                }
            });
        }



        private async void Window_Loaded(object? sender, RoutedEventArgs? e)
        {
            UpdateGui();
            PositionWindowBottomRight();
        }

        private async void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Sofort verbergen (synchron auf UI-Thread)
                Dispatcher.Invoke(() => this.Hide(), DispatcherPriority.Send);
                Debug.WriteLine("[MainWindow] Mouseklick erkannt ");// Nur auf Rechtsklick reagieren

// nur reagieren auf Klicks, Fenster sofort verbergen....
/*
                if (e.ChangedButton != MouseButton.Right)
                    return;

                Debug.WriteLine("[MainWindow] Rechtsklick erkannt -> Fenster sofort verbergen und Tray-Menü anzeigen");

                // kurz warten, damit das Shell-Tray Zeit hat, den Fokus zu verarbeiten
                await Task.Delay(20);

                // ContextMenu des Tray-Icons anzeigen
                try
                {
                    TrayIcon?.ShowContextMenuAtCursor();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Fehler beim Anzeigen des Tray-Menüs: {ex.Message}");
                }
*/
                // Unterdrücken, damit das Fenster nicht sofort wieder aufgeht
                _suppressShowUntil = DateTime.Now.Add(_suppressDuration);
                Debug.WriteLine($"[MainWindow] Show unterdrückt bis {_suppressShowUntil}");

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Fehler in Window_PreviewMouseDown: {ex.Message}");
            }
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

        private void OpenOrFocusConfigWindow(Action<ConfigWindow>? selectTab = null)
        {
            try
            {
                // Prüfen ob ConfigWindow bereits geöffnet ist
                if (_activeConfigWindow != null && _activeConfigWindow.IsLoaded)
                {
                    try
                    {
                        // Fenster in den Vordergrund bringen
                        if (_activeConfigWindow.WindowState == WindowState.Minimized)
                        {
                            _activeConfigWindow.WindowState = WindowState.Normal;
                        }

                        // Topmost kurz setzen um sicherzustellen dass Fenster in den Vordergrund kommt
                        _activeConfigWindow.Topmost = true;
                        _activeConfigWindow.Topmost = false;

                        _activeConfigWindow.Activate();
                        _activeConfigWindow.Focus();

                        // Tab wechseln falls gewünscht
                        selectTab?.Invoke(_activeConfigWindow);

                        Debug.WriteLine("[MainWindow] ConfigWindow bereits offen - fokussiert und Tab gewechselt");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] Fehler bei fokussierten Fenster: {ex.Message}\n{ex.StackTrace}");
                        _activeConfigWindow = null;
                    }
                }

                // Neues ConfigWindow erstellen
                this.Hide();
                var cfg = new ConfigWindow();
                cfg.Owner = this;
                _activeConfigWindow = cfg;

                // Cleanup wenn Fenster geschlossen wird
                cfg.Closed += (s, e) =>
                {
                    _activeConfigWindow = null;
                    Debug.WriteLine("[MainWindow] ConfigWindow geschlossen");
                };

                // Tab-Auswahl nach Laden
                if (selectTab != null)
                {
                    cfg.Loaded += (s, e) =>
                    {
                        try
                        {
                            selectTab(cfg);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MainWindow] Fehler bei selectTab: {ex.Message}");
                        }
                    };
                }

                cfg.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Fehler in OpenOrFocusConfigWindow: {ex.Message}");
                this.Show();
            }
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

            // Capture display names / nullables before entering the lambda to satisfy nullable analysis
            string? nic1Title = null;
            if (infos.nic1 != null && infos.nic1.GetLength(0) > 0) nic1Title = infos.nic1[0, 1];

            string? nic2Title = null;
            bool hasNic2 = false;
            if (infos.nic2 != null && infos.nic2.GetLength(0) > 0)
            {
                hasNic2 = true;
                nic2Title = infos.nic2[0, 1];
            }

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(nic1Title)) NIC1_Name.Content = nic1Title;

                NIC1_INFOS1.Text = labels1;
                NIC1_INFOS2.Text = values1;

                if (hasNic2)
                {
                    NIC2_Name.Visibility = Visibility.Visible;
                    NIC2_INFOS1.Visibility = Visibility.Visible;
                    NIC2_INFOS2.Visibility = Visibility.Visible;

                    if (!string.IsNullOrEmpty(nic2Title)) NIC2_Name.Content = nic2Title;
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
