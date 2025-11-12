using System;
using System.Diagnostics;
using System.IO;
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
            timer.Tick += Timer_Tick; // separate Methode
            Debug.WriteLine("Code will run every 1 minute. Press any key to exit.");
        }

        private async void Window_Loaded(object? sender, RoutedEventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Window_Loaded -> Starte erstes IP-Update");
            await AktualisiereIPAsync();
            var infos = AktualisiereNetzwerkInfos();
            SetzeNetzwerkInfosInUI(infos);
            timer.Start();
            Debug.WriteLine("[MainWindow] Timer gestartet");
        }

        // Timer Tick Handler
        private async void Timer_Tick(object? sender, EventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Aktualieseiren IP-Update ausgelöst");
            await AktualisiereIPAsync();
            var infos = AktualisiereNetzwerkInfos();
            SetzeNetzwerkInfosInUI(infos);
        }

        // Stoppe Timer beim Schließen des Fensters
        private void Window_Closed(object? sender, EventArgs? e)
        {
            Debug.WriteLine("[MainWindow] Window_Closed -> Timer stoppen");
            timer.Tick -= Timer_Tick;
            timer.Stop();
        }

        // Lädt die IP und aktualisiert das Label
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
            Debug.WriteLine("[MainWindow] AktualisiereNetzwerkInfos() gestartet");
            try
            {
                string iniPfad = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!File.Exists(iniPfad))
                {
                    Console.WriteLine("[NetzwerkInfo] Keine INI gefunden");
                    return (null, null);
                }
                Debug.WriteLine($"[MainWindow] Ini Datei : {iniPfad}");

                var lines = File.ReadAllLines(iniPfad);
                string? nic1 = null, nic2 = null;
                foreach (var line in lines)
                {
                    if (line.StartsWith("Adapter1", StringComparison.OrdinalIgnoreCase))
                    {
                        nic1 = line.Split('=')[1].Trim();
                        Debug.WriteLine($"[MainWindow] Aus Ini ermitelter Adapter1 : {nic1}");
                    }
                    else if (line.StartsWith("Adapter2", StringComparison.OrdinalIgnoreCase))
                    {
                        nic2 = line.Split('=')[1].Trim();
                        Debug.WriteLine($"[MainWindow] Aus Ini ermitelter Adapter2 : {nic2}");
                    }
                }

                string[,]? info1 = null, info2 = null;
                if (!string.IsNullOrEmpty(nic1))
                {
                    info1 = NetzwerkInfo.HoleNetzwerkInfo(nic1);
                }

                if (!string.IsNullOrEmpty(nic2))
                {
                    info2 = NetzwerkInfo.HoleNetzwerkInfo(nic2);
                }
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

                    // Zerlege value in einzelne Zeilen (z.B. mehrere IPv6-Adressen)
                    var valueLines = value
                        .Replace("\r\n", "\n")
                        .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    if (valueLines.Length == 0)
                    {
                        // Kein Wert -> eine leere Zeile mit Label
                        sbLabels.AppendLine(label);
                        sbValues.AppendLine("-");
                    }
                    else
                    {
                        // Erste Zeile: Label + erste Value
                        sbLabels.AppendLine(label);
                        sbValues.AppendLine(valueLines[0]);

                        // Für jede weitere Value-Zeile: linke Spalte leer, rechte Spalte die zusätzliche Value
                        for (int v = 1; v < valueLines.Length; v++)
                        {
                            sbLabels.AppendLine(""); // leere Zelle links
                            sbValues.AppendLine(valueLines[v]);
                        }
                    }
                    sbLabels.AppendLine(""); // leere Zelle links
                    sbValues.AppendLine(""); // leere Zelle rechts
                }

                // Entferne das letzte Newline
                return (sbLabels.ToString().TrimEnd('\r', '\n'), sbValues.ToString().TrimEnd('\r', '\n'));
            }

            var (labels1, values1) = FormatInfos(infos.nic1);
            var (labels2, values2) = FormatInfos(infos.nic2);

            Debug.WriteLine($"[MainWindow] Labels 1:\n{labels1}\nValues 1:\n{values1}");
            Debug.WriteLine($"[MainWindow] Labels 2:\n{labels2}\nValues 2:\n{values2}");

            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (infos.nic1 != null && infos.nic1.GetLength(0) > 0)
                        NIC1_Name.Content = infos.nic1[0, 1] ?? NIC1_Name.Content;

                    if (infos.nic2 != null && infos.nic2.GetLength(0) > 0)
                        NIC2_Name.Content = infos.nic2[0, 1] ?? NIC2_Name.Content;

                    // Stelle sicher, dass die UI-Elemente TextWrapping erlauben und monospace setzen (optional)
                    NIC1_INFOS1.Text = string.IsNullOrEmpty(labels1) ? "" : labels1;
                    NIC1_INFOS2.Text = string.IsNullOrEmpty(values1) ? "" : values1;

                    NIC2_INFOS1.Text = string.IsNullOrEmpty(labels2) ? "" : labels2;
                    NIC2_INFOS2.Text = string.IsNullOrEmpty(values2) ? "" : values2;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Fehler beim Schreiben in UI: {ex.Message}");
                }
            });
        }


    }
}
