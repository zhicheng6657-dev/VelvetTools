using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VelvetTools.Common.Interop;

namespace VelvetTools.Common;

/// <summary>右下角轻量提示气泡（不抢焦点）。</summary>
public static class Toast
{
    private static ToastWindow? _window;

    public static void Show(string message, int durationMs = 2200)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            _window ??= new ToastWindow();
            _window.ShowMessage(message, durationMs);
        });
    }

    private sealed class ToastWindow : Window
    {
        private readonly TextBlock _text;
        private readonly DispatcherTimer _timer;

        public ToastWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;

            // 颜色一律走主题资源：写死深色会让浅色主题下的提示变成突兀黑块
            _text = new TextBlock
            {
                FontSize = 13,
                MaxWidth = 420,
                TextWrapping = TextWrapping.Wrap,
            };
            _text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _text.SetResourceReference(TextBlock.FontFamilyProperty, "AppFont");

            var host = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 9, 14, 9),
                Child = _text,
            };
            host.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            host.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            Content = host;

            _timer = new DispatcherTimer();
            _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                GlassEffect.ApplyThemedHwnd(hwnd);
            };
        }

        public void ShowMessage(string message, int durationMs)
        {
            _text.Text = message;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(durationMs);

            Show();
            UpdateLayout();
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 16;
            Top = wa.Bottom - ActualHeight - 16;
            _timer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (ReferenceEquals(_window, this)) _window = null;
        }
    }
}
