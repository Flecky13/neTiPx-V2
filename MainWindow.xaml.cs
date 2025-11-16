using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace neTiPx
{
    public partial class MainWindow : Window
    {
        private readonly Internet internet = new Internet();
        private readonly DispatcherTimer timer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            Debug.WriteLine("[MainWindow] Konstruktor initialisiert");

            timer.Interval = TimeSpan.FromMinutes(1);
            timer.Tick += Timer_Tick;

            // Positionierung nach Rendern
            this.ContentRendered += MainWindow_ContentRendered;
            this.SizeChanged += MainWindow_SizeChanged;
            
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

        public async Task UpdateWindowsAPP()
        {
            Debug.WriteLine("[MainWindow] UpdateWindowsAPP() aufgerufen");
            await AktualisiereIPAsync();
            var infos = AktualisiereNetzwerkInfos();
            SetzeNetzwerkInfosInUI(infos);

            timer.Start();
            Debug.WriteLine("[MainWindow] Timer gestartet");

            PositionWindowBottomRight();
        }

        private async void Window_Loaded(object? sender, RoutedEventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Window_Loaded -> Starte erstes IP-Update");
            await AktualisiereIPAsync();
            var infos = AktualisiereNetzwerkInfos();
            SetzeNetzwerkInfosInUI(infos);
            timer.Start();
            Debug.WriteLine("[MainWindow] Timer gestartet");
            PositionWindowBottomRight();
        }

        private void window_Activated(object? sender, EventArgs? e)
        {
            PositionWindowBottomRight();
        }

        private async void Timer_Tick(object? sender, EventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Aktualieseiren IP-Update ausgelöst");
            await AktualisiereIPAsync();
            var infos = AktualisiereNetzwerkInfos();
            SetzeNetzwerkInfosInUI(infos);
        }

        private void Window_Closed(object? sender, EventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Window_Closed -> Timer stoppen");
            timer.Tick -= Timer_Tick;
            timer.Stop();
        }

        private async Task AktualisiereIPAsync()
        {
            try
            {
                Debug.WriteLine("[MainWindow] AktualisiereIPAsync() gestartet");
                ISPInfos.Content = "Lade IP...";
                await internet.LadeExterneIPAsync();
                ISPInfos.Content = $"{internet.IPAdresse}";
                Debug.WriteLine($"[MainWindow] IP aktualisiert: {internet.IPAdresse}");
            }
            catch (Exception ex)
            {
                ISPInfos.Content = "Fehler beim Laden der IP";
                Debug.WriteLine($"[MainWindow] Fehler: {ex.Message}");
            }
        }

        private (string[,]? nic1, string[,]? nic2) AktualisiereNetzwerkInfos()
        {
            try
            {
                string iniPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!System.IO.File.Exists(iniPfad))
                {
                    Console.WriteLine("[NetzwerkInfo] Keine INI gefunden");
                    return (null, null);
                }

                var lines = System.IO.File.ReadAllLines(iniPfad);
                string? nic1 = null, nic2 = null;
                foreach (var line in lines)
                {
                    if (line.StartsWith("Adapter1", StringComparison.OrdinalIgnoreCase))
                        nic1 = line.Split('=')[1].Trim();
                    else if (line.StartsWith("Adapter2", StringComparison.OrdinalIgnoreCase))
                        nic2 = line.Split('=')[1].Trim();
                }

                string[,]? info1 = null, info2 = null;
                if (!string.IsNullOrEmpty(nic1)) info1 = NetzwerkInfo.HoleNetzwerkInfo(nic1);
                if (!string.IsNullOrEmpty(nic2)) info2 = NetzwerkInfo.HoleNetzwerkInfo(nic2);
                return (info1, info2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainWindow] Netzwerk-Update-Fehler: {ex.Message}");
                return (null, null);
            }
        }

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
                if (infos.nic2 != null && infos.nic2.GetLength(0) > 0) NIC2_Name.Content = infos.nic2[0, 1] ?? NIC2_Name.Content;

                NIC1_INFOS1.Text = labels1;
                NIC1_INFOS2.Text = values1;
                NIC2_INFOS1.Text = labels2;
                NIC2_INFOS2.Text = values2;
            });
        }
    }
}
