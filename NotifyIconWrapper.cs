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
        
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(NotifyIconWrapper), new PropertyMetadata(
                (d, e) =>
                {
                    var notifyIcon = ((NotifyIconWrapper)d)._notifyIcon;
                    if (notifyIcon == null)
                        return;
                    notifyIcon.Text = (string)e.NewValue;
                }));

        private static readonly DependencyProperty NotifyRequestProperty =
            DependencyProperty.Register("NotifyRequest", typeof(NotifyRequestRecord), typeof(NotifyIconWrapper),
                new PropertyMetadata(
                    (d, e) =>
                    {
                        var r = (NotifyRequestRecord)e.NewValue;
                        ((NotifyIconWrapper)d)._notifyIcon?.ShowBalloonTip(r.Duration, r.Title, r.Text, r.Icon);
                    }));
        
        private static readonly RoutedEvent ShowSelectedEvend = EventManager.RegisterRoutedEvent("ShowSelected",
            RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private static readonly RoutedEvent HideSelectedEvend = EventManager.RegisterRoutedEvent("HideSelected",
            RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private static readonly RoutedEvent ExitSelectedEvend = EventManager.RegisterRoutedEvent("ExitSelected",
            RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(NotifyIconWrapper));

        private readonly NotifyIcon? _notifyIcon;

        public NotifyIconWrapper()
        {
            if (DesignerProperties.GetIsInDesignMode(this))
                return;
            _notifyIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };
            _notifyIcon.DoubleClick += ShowItemOnClick;
            Application.Current.Exit += (obj, args) => { _notifyIcon.Dispose(); };
        }
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public NotifyRequestRecord NotifyRequest
        {
            get => (NotifyRequestRecord)GetValue(NotifyRequestProperty);
            set => SetValue(NotifyRequestProperty, value);
        }
        

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }

        public event RoutedEventHandler ShowSelected
        {
            add => AddHandler(ShowSelectedEvend, value);
            remove => RemoveHandler(ShowSelectedEvend, value);
        }

        public event RoutedEventHandler HideSelected
        {
            add => AddHandler(HideSelectedEvend, value);
            remove => RemoveHandler(HideSelectedEvend, value);
        }

        public event RoutedEventHandler ExitSelected
        {
            add => AddHandler(ExitSelectedEvend, value);
            remove => RemoveHandler(ExitSelectedEvend, value);
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var openItem = new ToolStripMenuItem("Show");
            openItem.Click += ShowItemOnClick;
            var hideItem = new ToolStripMenuItem("Hide");
            hideItem.Click += HideItemOnClick;
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += ExitItemOnClick;
            var contextMenu = new ContextMenuStrip {Items = {openItem, hideItem, exitItem }};
            return contextMenu;
        }
        private async void ShowItemOnClick(object? sender, EventArgs eventArgs)
        {
            Debug.WriteLine("[NotifyIconWrapper] - ShowItemOnClick ");
            //await MainWindow.UpdateWindowsAPP();
            var args = new RoutedEventArgs(ShowSelectedEvend);
            RaiseEvent(args);
        }

        private void HideItemOnClick(object? sender, EventArgs eventArgs)
        {
            Debug.WriteLine("[NotifyIconWrapper] - HideItemOnClick ");
            var args = new RoutedEventArgs(HideSelectedEvend);
            RaiseEvent(args);
        }
        private void ExitItemOnClick(object? sender, EventArgs eventArgs)
        {
            Debug.WriteLine("[NotifyIconWrapper] - ExitItemOnClick ");
            var args = new RoutedEventArgs(ExitSelectedEvend);
            RaiseEvent(args);
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