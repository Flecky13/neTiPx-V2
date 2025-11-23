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
        private readonly System.Timers.Timer? _leaveTimer;
        private System.Drawing.Point _enterCursorPosition = new System.Drawing.Point(0, 0);

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

        private static readonly RoutedEvent ConfigSelectedEvent = EventManager.RegisterRoutedEvent(
            "ConfigSelected", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private static readonly RoutedEvent IpSettingsSelectedEvent = EventManager.RegisterRoutedEvent(
            "IpSettingsSelected", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

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

        public event RoutedEventHandler ConfigSelected
        {
            add => AddHandler(ConfigSelectedEvent, value);
            remove => RemoveHandler(ConfigSelectedEvent, value);
        }

        public event RoutedEventHandler IpSettingsSelected
        {
            add => AddHandler(IpSettingsSelectedEvent, value);
            remove => RemoveHandler(IpSettingsSelectedEvent, value);
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

            // Timer zur zuverlässigen Erkennung von MouseLeave
            _leaveTimer = new System.Timers.Timer(800) { AutoReset = false };
            _leaveTimer.Elapsed += LeaveTimer_Elapsed;

            Application.Current.Exit += (obj, args) => { _notifyIcon?.Dispose(); _leaveTimer?.Dispose(); };
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
            try { _leaveTimer?.Dispose(); } catch { }
        }

        // --------------------------------------------------------

        private void NotifyIcon_MouseMove(object? sender, MouseEventArgs e)
        {
            _lastMouseMove = DateTime.Now;

            // Wenn erstes MouseMove -> Enter

            if (!_isMouseOver)
            {
                _isMouseOver = true;
                // speichere die Cursor-Position beim Eintritt, damit Stillstand nicht als Verlassen gewertet wird
                try { _enterCursorPosition = System.Windows.Forms.Cursor.Position; } catch { _enterCursorPosition = new System.Drawing.Point(0,0); }
                RaiseEvent(new RoutedEventArgs(TrayMouseEnterEvent));
                Debug.WriteLine("[NotifyIconWrapper] MouseOver erkannt");
            }

            // Restart hide timer: wenn nach Interval kein MouseMove kommt, wird Leave ausgelöst
            try
            {
                if (_leaveTimer != null)
                {
                    _leaveTimer.Stop();
                    _leaveTimer.Start();
                }
            }
            catch (ObjectDisposedException) { }
        }

        private void LeaveTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // Auf UI-Thread ausführen
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                // Sicherheitscheck: Nur auslösen, wenn sich der Cursor tatsächlich vom Eintrittspunkt wegbewegt hat.
                var timer = _leaveTimer;
                if (timer == null) return;

                var current = System.Windows.Forms.Cursor.Position;
                // Distanz zum Eintrittspunkt
                int dx = current.X - _enterCursorPosition.X;
                int dy = current.Y - _enterCursorPosition.Y;
                var distSq = dx * dx + dy * dy;
                const int leaveDistancePx = 16; // wenn Cursor sich mehr als 32px entfernt hat -> Leave
                if (distSq >= leaveDistancePx * leaveDistancePx)
                {
                    Debug.WriteLine("[NotifyIconWrapper] MouseLeave erkannt (Cursor bewegt)");
                    _isMouseOver = false;
                    RaiseEvent(new RoutedEventArgs(TrayMouseLeaveEvent));
                }
                else
                {
                    // Cursor steht noch in der Nähe des Icons -> keinen Leave auslösen, Timer neu starten
                    try { timer.Stop(); timer.Start(); } catch (ObjectDisposedException) { }
                }
            }));
        }

        // --------------------------------------------------------

        private ContextMenuStrip CreateContextMenu()
        {
            var configItem = new ToolStripMenuItem("Config");
            configItem.Click += ConfigItemOnClick;

            var ipItem = new ToolStripMenuItem("IP Settings");
            ipItem.Click += IpItemOnClick;

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += ExitItemOnClick;

            return new ContextMenuStrip { Items = { configItem, ipItem, exitItem } };
        }

        private void ConfigItemOnClick(object? sender, EventArgs args)
        {
            Debug.WriteLine("[NotifyIconWrapper] - ConfigItemOnClick ");
            RaiseEvent(new RoutedEventArgs(ConfigSelectedEvent));
        }

        private void IpItemOnClick(object? sender, EventArgs args)
        {
            Debug.WriteLine("[NotifyIconWrapper] - IpItemOnClick ");
            RaiseEvent(new RoutedEventArgs(IpSettingsSelectedEvent));
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

        // Zeigt das ContextMenuStrip an der aktuellen Cursor-Position an.
        public void ShowContextMenuAtCursor()
        {
            try
            {
                var cms = _notifyIcon?.ContextMenuStrip;
                if (cms == null) return;
                var pos = System.Windows.Forms.Cursor.Position;
                cms.Show(pos);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[NotifyIconWrapper] ShowContextMenuAtCursor failed: " + ex.Message);
            }
        }

        public class NotifyRequestRecord
        {
            public string Title { get; set; } = "";
            public string Text { get; set; } = "";
            public int Duration { get; set; } = 1000;
            public ToolTipIcon Icon { get; set; } = ToolTipIcon.Info;
        }
    }
}
