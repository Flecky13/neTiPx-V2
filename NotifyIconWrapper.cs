using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace neTiPx
{
    public class NotifyIconWrapper : FrameworkElement, IDisposable
    {
        // --- MouseOver Events ---
        private static readonly RoutedEvent TrayMouseEnterEvent = EventManager.RegisterRoutedEvent(
            "TrayMouseEnter", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private static readonly RoutedEvent TrayMouseLeaveEvent = EventManager.RegisterRoutedEvent(
            "TrayMouseLeave", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        public event RoutedEventHandler TrayMouseEnter
        {
            add => AddHandler(TrayMouseEnterEvent, value);
            remove => RemoveHandler(TrayMouseEnterEvent, value);
        }

        public event RoutedEventHandler TrayMouseLeave
        {
            add => AddHandler(TrayMouseLeaveEvent, value);
            remove => RemoveHandler(TrayMouseLeaveEvent, value);
        }

        private bool _isMouseOver = false;
        private DateTime _lastMouseMove = DateTime.MinValue;
        private bool _leaveCheckerRunning = false;

        // --------------------------------------------------------

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(NotifyIconWrapper), new PropertyMetadata(
                (d, e) =>
                {
                    var wrapper = (NotifyIconWrapper)d;
                    wrapper._notifyIcon?.Text = (string)e.NewValue;
                }));

        private static readonly DependencyProperty NotifyRequestProperty =
            DependencyProperty.Register("NotifyRequest", typeof(NotifyRequestRecord), typeof(NotifyIconWrapper),
                new PropertyMetadata(
                    (d, e) =>
                    {
                        var wrapper = (NotifyIconWrapper)d;
                        var r = (NotifyRequestRecord)e.NewValue;
                        wrapper._notifyIcon?.ShowBalloonTip(r.Duration, r.Title, r.Text, r.Icon);
                    }));

        private static readonly RoutedEvent ShowSelectedEvent = EventManager.RegisterRoutedEvent(
            "ShowSelected", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private static readonly RoutedEvent HideSelectedEvent = EventManager.RegisterRoutedEvent(
            "HideSelected", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private static readonly RoutedEvent ExitSelectedEvent = EventManager.RegisterRoutedEvent(
            "ExitSelected", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        public event RoutedEventHandler ShowSelected
        {
            add => AddHandler(ShowSelectedEvent, value);
            remove => RemoveHandler(ShowSelectedEvent, value);
        }

        public event RoutedEventHandler HideSelected
        {
            add => AddHandler(HideSelectedEvent, value);
            remove => RemoveHandler(HideSelectedEvent, value);
        }

        public event RoutedEventHandler ExitSelected
        {
            add => AddHandler(ExitSelectedEvent, value);
            remove => RemoveHandler(ExitSelectedEvent, value);
        }

        // --------------------------------------------------------

        private readonly NotifyIcon? _notifyIcon;

        public NotifyIconWrapper()
        {
            // Null-sicher im Designer
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                _notifyIcon = null;
                return;
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            _notifyIcon.DoubleClick += ShowItemOnClick;

            // MouseOver aktivieren
            _notifyIcon.MouseMove += NotifyIcon_MouseMove;

            Application.Current.Exit += (obj, args) => { _notifyIcon?.Dispose(); };
        }

        // --------------------------------------------------------

        public NotifyRequestRecord NotifyRequest
        {
            get => (NotifyRequestRecord)GetValue(NotifyRequestProperty);
            set => SetValue(NotifyRequestProperty, value);
        }
        
        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
        
        // --------------------------------------------------------

        private void NotifyIcon_MouseMove(object? sender, MouseEventArgs e)
        {
            _lastMouseMove = DateTime.Now;

            if (!_isMouseOver)
            {
                _isMouseOver = true;
                RaiseEvent(new RoutedEventArgs(TrayMouseEnterEvent));
                Debug.WriteLine("[NofifyIconWrapper] MouseOver erkannt");
            }
            
            StartLeaveChecker();
        }

        private async void StartLeaveChecker()
        {
            if (_leaveCheckerRunning)
                return;

            _leaveCheckerRunning = true;

            while ((DateTime.Now - _lastMouseMove).TotalMilliseconds < 350)
                await Task.Delay(2000);
            Debug.WriteLine("[NofifyIconWrapper] MouseLeave erkannt");
            _isMouseOver = false;
            RaiseEvent(new RoutedEventArgs(TrayMouseLeaveEvent));

            _leaveCheckerRunning = false;
        }

        // --------------------------------------------------------

        private ContextMenuStrip CreateContextMenu()
        {
            var openItem = new ToolStripMenuItem("Show");
            openItem.Click += ShowItemOnClick;

            var hideItem = new ToolStripMenuItem("Hide");
            hideItem.Click += HideItemOnClick;

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += ExitItemOnClick;

            return new ContextMenuStrip { Items = { openItem, hideItem, exitItem } };
        }

        private void ShowItemOnClick(object? sender, EventArgs args)
        {
            Debug.WriteLine("[NotifyIconWrapper] - ShowItemOnClick ");
            RaiseEvent(new RoutedEventArgs(ShowSelectedEvent));
        }

        private void HideItemOnClick(object? sender, EventArgs args)
        {
            Debug.WriteLine("[NotifyIconWrapper] - HideItemOnClick ");
            RaiseEvent(new RoutedEventArgs(HideSelectedEvent));
        }

        private void ExitItemOnClick(object? sender, EventArgs args)
        {
            Debug.WriteLine("[NotifyIconWrapper] - ExitItemOnClick ");
            RaiseEvent(new RoutedEventArgs(ExitSelectedEvent));
        }

        // --------------------------------------------------------

        public class NotifyRequestRecord
        {
            public string Title { get; set; } = "";
            public string Text { get; set; } = "";
            public int Duration { get; set; } = 1000;
            public ToolTipIcon Icon { get; set; } = ToolTipIcon.Info;
        }
    }
}
