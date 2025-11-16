using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace neTiPx
{
    public class MainWindowViewModel : ObservableRecipient
    {
        private NotifyIconWrapper.NotifyRequestRecord? _notifyRequest;
        private bool _showInTaskbar;
        private WindowState _windowState;
        private readonly Internet internet = new Internet();
        private readonly DispatcherTimer timer = new DispatcherTimer();

        public MainWindowViewModel()
        {
            LoadedCommand = new RelayCommand(Loaded);
            ClosingCommand = new RelayCommand<CancelEventArgs>(Closing);

            NotifyIconShowCommand = new RelayCommand(() => { WindowNormal(); });
            NotifyIconHideCommand = new RelayCommand(() => { WindowMinimized(); });
            NotifyIconExitCommand = new RelayCommand(() => { Application.Current.Shutdown(); });
            Debug.WriteLine("[MainWindowViewModel] Konstruktor initialisiert");

        }

        private void WindowNormal()
        {
            WindowState = WindowState.Normal;

            Debug.WriteLine("[MainWindowViewModel] WindowNormal");

        }
        private void WindowMinimized()
        {
            WindowState = WindowState.Minimized;

            Debug.WriteLine("[MainWindowViewModel] WindowMinimized");
        }

        public ICommand LoadedCommand { get; }
        public ICommand ClosingCommand { get; }
        public ICommand NotifyIconShowCommand { get; }
        public ICommand NotifyIconExitCommand { get; }
        public ICommand NotifyIconHideCommand { get; }
        public WindowState WindowState
        {
            get => _windowState;
            set
            {
                ShowInTaskbar = true;
                SetProperty(ref _windowState, value);
                ShowInTaskbar = value != WindowState.Minimized;
            }
        }

        public bool ShowInTaskbar
        {
            get => _showInTaskbar;
            set => SetProperty(ref _showInTaskbar, value);
        }


        public NotifyIconWrapper.NotifyRequestRecord? NotifyRequest
        {
            get => _notifyRequest;
            set => SetProperty(ref _notifyRequest, value);
        }


        private void Loaded()
        {
            WindowState = WindowState.Minimized;
            Debug.WriteLine($"[MainWindowViewModel] Loaded {WindowState}");
        }

        private void Closing(CancelEventArgs? e)
        {
            if (e == null)
                return;
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }

        // Expose IP / Netzwerk-Aktualisierung für das View (MainWindow)
        public async System.Threading.Tasks.Task<string?> LadeExterneIPAsync()
        {
            try
            {
                Debug.WriteLine("[MainWindowViewModel] LadeExterneIPAsync() gestartet");
                await internet.LadeExterneIPAsync();
                Debug.WriteLine($"[MainWindowViewModel] IP geladen: {internet.IPAdresse}");
                return internet.IPAdresse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowViewModel] Fehler LadeExterneIPAsync: {ex.Message}");
                return null;
            }
        }

        public (string[,]? nic1, string[,]? nic2) HoleNetzwerkInfos()
        {
            try
            {
                string iniPfad = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!System.IO.File.Exists(iniPfad))
                {
                    Debug.WriteLine("[MainWindowViewModel] Keine INI gefunden");
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
                Debug.WriteLine($"[MainWindowViewModel] Netzwerk-Update-Fehler: {ex.Message}");
                return (null, null);
            }
        }
    }

}
