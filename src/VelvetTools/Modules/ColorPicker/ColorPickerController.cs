using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FluentIconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;
using VelvetTools.Common.Interop;
using VelvetTools.Modules.Screenshot;

namespace VelvetTools.Modules.ColorPicker;

/// <summary>屏幕取色器：放大镜跟随光标，点击取色，展示 HEX/RGB/HSL 并记录历史。</summary>
public sealed class ColorPickerController
{
    private sealed class PickSession
    {
        public TaskCompletionSource<System.Drawing.Color?> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ColorOverlayWindow> Windows { get; } = new();

        public void Complete(System.Drawing.Color? color)
        {
            foreach (var w in Windows.ToList())
            {
                try { w.Close(); } catch { }
            }
            Windows.Clear();
            Tcs.TrySetResult(color);
        }
    }

    private PickSession? _session;

    public async Task PickAsync()
    {
        if (_session is not null) return;
        try
        {
            var shots = CaptureService.CaptureAllScreens();
            _session = new PickSession();

            ColorOverlayWindow? cursorWindow = null;
            Native.GetCursorPos(out var cursor);
            foreach (var shot in shots)
            {
                var w = new ColorOverlayWindow(shot, _session.Complete);
                _session.Windows.Add(w);
                w.Show();
                if (shot.Screen.Bounds.Contains(cursor.X, cursor.Y))
                    cursorWindow = w;
            }
            cursorWindow?.Activate();

            var color = await _session.Tcs.Task;
            _session = null;
            if (color is null) return;

            OnPicked(color.Value);
        }
        catch (Exception ex)
        {
            _session = null;
            Logger.Error("取色失败", ex);
            Toast.Show("取色失败：" + ex.Message);
        }
    }

    private static void OnPicked(System.Drawing.Color c)
    {
        var settings = App.Services.Settings.ColorPicker;
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        settings.History.Remove(hex);
        settings.History.Insert(0, hex);
        if (settings.History.Count > 16)
            settings.History.RemoveRange(16, settings.History.Count - 16);
        App.Services.Settings.Save();

        string copied = settings.CopyFormat switch
        {
            "rgb" => $"rgb({c.R}, {c.G}, {c.B})",
            "hsl" => ToHslString(c),
            _ => hex,
        };
        try { System.Windows.Clipboard.SetText(copied); } catch { }

        new ColorResultWindow(c).Show();
        Toast.Show($"已复制 {copied}");
    }

    internal static string ToHslString(System.Drawing.Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2;
        double d = max - min;
        if (d == 0) { h = 0; s = 0; }
        else
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
            else if (max == g) h = ((b - r) / d + 2) / 6;
            else h = ((r - g) / d + 4) / 6;
        }
        return $"hsl({h * 360:0}, {s * 100:0}%, {l * 100:0}%)";
    }
}

/// <summary>取色遮罩（每显示器一个）：放大镜 + 像素网格 + 颜色标签。</summary>
public sealed class ColorOverlayWindow : Window
{
    private const int Zoom = 12;      // 每物理像素放大倍数
    private const int GridCells = 11; // 放大镜边长（像素数，奇数保证有中心）

    private readonly ScreenShotData _shot;
    private readonly Action<System.Drawing.Color?> _complete;
    private readonly Border _loupe;
    private readonly System.Windows.Shapes.Rectangle _zoomRect;
    private readonly ImageBrush _zoomBrush;
    private readonly TextBlock _label;
    private System.Drawing.Color _current;

    public ColorOverlayWindow(ScreenShotData shot, Action<System.Drawing.Color?> complete)
    {
        _shot = shot;
        _complete = complete;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = System.Windows.Media.Brushes.Black;
        Left = 0; Top = 0; Width = 200; Height = 200;

        var source = CaptureService.ToBitmapSource(shot.Bitmap);
        var image = new System.Windows.Controls.Image { Source = source, Stretch = Stretch.Fill };

        _zoomBrush = new ImageBrush(source)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        double loupeSize = GridCells * Zoom;
        _zoomRect = new System.Windows.Shapes.Rectangle
        {
            Width = loupeSize,
            Height = loupeSize,
            Fill = _zoomBrush,
        };
        RenderOptions.SetBitmapScalingMode(_zoomRect, BitmapScalingMode.NearestNeighbor);

        // 中心像素高亮框
        var centerBox = new Border
        {
            Width = Zoom,
            Height = Zoom,
            BorderBrush = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _label = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        };

        var loupeStack = new StackPanel();
        loupeStack.Children.Add(new Grid { Children = { _zoomRect, centerBox }, Width = loupeSize, Height = loupeSize });
        loupeStack.Children.Add(_label);

        _loupe = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF0, 0x1E, 0x1E, 0x28)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = loupeStack,
        };

        var canvas = new Canvas();
        canvas.Children.Add(_loupe);

        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(canvas);
        Content = root;

        MouseMove += (_, e) => UpdateLoupe(e.GetPosition(this));
        MouseLeftButtonUp += (_, _) => _complete(_current);
        MouseRightButtonUp += (_, _) => _complete(null);

        // Esc 取消：用 PreviewKeyDown 保证不被子元素吞掉；
        // 鼠标进入本屏幕时抢焦点，多显示器下哪个屏都能按 Esc
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _complete(null);
            }
        };
        MouseEnter += (_, _) => { if (!IsActive) Activate(); };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var b = _shot.Screen.Bounds;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_SHOWWINDOW);
        };
        Loaded += (_, _) =>
        {
            Focusable = true;
            Focus();
            Keyboard.Focus(this);
            UpdateLoupe(Mouse.GetPosition(this));
        };
    }

    private void UpdateLoupe(System.Windows.Point pos)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int px = Math.Clamp((int)(pos.X * dpi.DpiScaleX), 0, _shot.Bitmap.Width - 1);
        int py = Math.Clamp((int)(pos.Y * dpi.DpiScaleY), 0, _shot.Bitmap.Height - 1);

        _current = _shot.Bitmap.GetPixel(px, py);
        _label.Text = $"#{_current.R:X2}{_current.G:X2}{_current.B:X2}  ·  {_current.R},{_current.G},{_current.B}";

        double half = GridCells / 2.0;
        _zoomBrush.Viewbox = new Rect(px - half + 0.5, py - half + 0.5, GridCells, GridCells);

        double lx = pos.X + 24;
        double ly = pos.Y + 24;
        _loupe.UpdateLayout();
        if (lx + _loupe.ActualWidth > ActualWidth - 8) lx = pos.X - _loupe.ActualWidth - 24;
        if (ly + _loupe.ActualHeight > ActualHeight - 8) ly = pos.Y - _loupe.ActualHeight - 24;
        Canvas.SetLeft(_loupe, Math.Max(4, lx));
        Canvas.SetTop(_loupe, Math.Max(4, ly));
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _shot.Bitmap.Dispose();
    }
}

/// <summary>取色结果小窗：色板 + 各格式一键复制 + 历史色带。</summary>
public sealed class ColorResultWindow : GlassWindow
{
    private bool _pinned;
    private bool _closing;

    public ColorResultWindow(System.Drawing.Color c)
    {
        EscapeAction = EscAction.Close;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DragMoveEnabled = true;

        // 点击窗口以外的任何地方（桌面/别的程序）就关闭；拖动窗口本身不会触发。
        // Esc/关闭按钮触发的 Close() 进行中窗口会先失焦，若再调 Close()
        // WPF 会抛“窗口关闭期间无法调用 Close”，故用 _closing 拦住重入。
        Deactivated += (_, _) =>
        {
            if (!_pinned && !_closing) Close();
        };

        var stack = new StackPanel { Margin = new Thickness(16), MinWidth = 260 };

        var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        header.Children.Add(new Border
        {
            Width = 44, Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        });
        var title = new TextBlock
        {
            Text = "取色结果",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        header.Children.Add(title);

        // 图钉：钉住后失焦不再自动关闭，方便对照取色
        var pin = new Button
        {
            Content = AppIconFactory.Create(FluentIconKind.Pin, 15),
            Style = (Style)FindResource("IconButton"),
            ToolTip = "钉住（不自动关闭）",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 0, 0),
        };
        pin.Click += (_, _) =>
        {
            _pinned = !_pinned;
            pin.Foreground = (System.Windows.Media.Brush)FindResource(_pinned ? "AccentBrush" : "TextSecondaryBrush");
            pin.ToolTip = _pinned ? "已钉住，点此取消" : "钉住（不自动关闭）";
        };
        header.Children.Add(pin);

        var closeBtn = new Button
        {
            Content = AppIconFactory.Create(FluentIconKind.Dismiss, 14),
            Style = (Style)FindResource("IconButton"),
            ToolTip = "关闭 (Esc)",
        };
        closeBtn.Click += (_, _) => Close();
        header.Children.Add(closeBtn);

        stack.Children.Add(header);

        stack.Children.Add(MakeRow($"#{c.R:X2}{c.G:X2}{c.B:X2}"));
        stack.Children.Add(MakeRow($"rgb({c.R}, {c.G}, {c.B})"));
        stack.Children.Add(MakeRow(ColorPickerController.ToHslString(c)));

        var history = App.Services.Settings.ColorPicker.History;
        if (history.Count > 1)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "历史",
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 4),
            });
            var wrap = new WrapPanel();
            foreach (var hex in history.Take(12))
            {
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    var chip = new Border
                    {
                        Width = 24, Height = 24,
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 0, 6, 6),
                        Background = new SolidColorBrush(col),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        ToolTip = hex,
                    };
                    chip.MouseLeftButtonUp += (_, _) =>
                    {
                        try { System.Windows.Clipboard.SetText(hex); Toast.Show($"已复制 {hex}"); } catch { }
                    };
                    wrap.Children.Add(chip);
                }
                catch { }
            }
            stack.Children.Add(wrap);
        }

        Content = stack;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
        if (e.Cancel) _closing = false;
    }

    private UIElement MakeRow(string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(tb);

        var btn = new Button
        {
            Content = "复制",
            Style = (Style)FindResource("GlassButton"),
            Padding = new Thickness(10, 3, 10, 3),
        };
        btn.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(text); Toast.Show($"已复制 {text}"); } catch { }
        };
        Grid.SetColumn(btn, 1);
        grid.Children.Add(btn);
        return grid;
    }
}
