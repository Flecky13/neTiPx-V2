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
    }

}
