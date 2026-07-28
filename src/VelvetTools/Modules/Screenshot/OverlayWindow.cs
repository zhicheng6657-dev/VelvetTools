using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FluentIcons.Wpf;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.Screenshot;

public enum CaptureAction { Copy, Save, SaveAs, Pin, Ocr, Translate }

public sealed record CaptureSelection(System.Drawing.Bitmap Image, CaptureAction Action);

/// <summary>一次截图会话：跨多显示器的全部遮罩窗口共享同一个会话。</summary>
public sealed class OverlaySession
{
    public TaskCompletionSource<CaptureSelection?> Tcs { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<OverlayWindow> Windows { get; } = new();

    public void Complete(CaptureSelection? result)
    {
        foreach (var w in Windows.ToList())
        {
            try { w.Close(); } catch { }
        }
        Windows.Clear();
        Tcs.TrySetResult(result);
    }
}

/// <summary>
/// 截图遮罩窗口。交互对标 Snipaste/PixPin：
/// 悬停识别窗口 → 单击/拖拽选区 → 八向手柄二次调整（方向键微调）→
/// 标注（矩形/椭圆/直线/箭头/画笔/荧光笔/马赛克/序号/文字，三档粗细，Shift 约束，撤销/重做）。
/// 快捷键：Enter/双击=复制，Ctrl+S=另存，F3=钉住，C=取色（选区前），Esc=取消。
/// </summary>
public sealed class OverlayWindow : Window
{
    private enum Phase { Pick, Drag, Edit }
    private enum Tool { None, Rect, Ellipse, Line, Arrow, Pen, Highlight, Mosaic, Step, Text }
    private enum DragKind { None, Move, Resize }

    // ---------- 标注模型（DIP 坐标，窗口本地） ----------
    private abstract class Ann { public List<UIElement> Visuals { get; } = new(); }
    private sealed class ShapeAnn : Ann { public Rect R; public bool IsEllipse; public Color C; public double W; }
    private sealed class LineAnn : Ann { public Point A, B; public Color C; public double W; public bool HasHead; }
    private sealed class PenAnn : Ann { public List<Point> Pts = new(); public Color C; public double W; public bool Highlight; }
    private sealed class MosaicAnn : Ann { public Rect R; public int Block; }
    private sealed class StepAnn : Ann { public Point P; public int N; public Color C; public double D; }
    private sealed class TextAnn : Ann { public Point P; public TextBox Box = null!; public Color C; public double Size; }

    private readonly ScreenShotData _shot;
    private readonly OverlaySession _session;
    private readonly CaptureAction? _directAction;

    private readonly Canvas _canvas = new();
    private readonly Canvas _annLayer = new();
    private readonly RectangleGeometry _fullGeo = new();
    private readonly RectangleGeometry _holeGeo = new();
    private readonly Border _selBorder;
    private readonly TextBlock _sizeLabel;
    private readonly Border _sizeLabelHost;
    private readonly Border _hintHost;
    private readonly Border _toolbar;
    private readonly StackPanel _toolOptions = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly List<(Tool tool, Border btn)> _toolButtons = new();
    private readonly List<(Color color, Border chip)> _colorChips = new();
    private readonly List<(double width, Border btn)> _widthButtons = new();
    private readonly List<(int block, Border btn)> _mosaicButtons = new();
    private readonly List<Border> _handles = new();

    // 放大镜
    private readonly Border _loupe;
    private readonly ImageBrush _zoomBrush;
    private readonly TextBlock _loupeText;

    private readonly List<System.Drawing.Rectangle> _windowRects;

    private Phase _phase = Phase.Pick;
    private Tool _tool = Tool.None;
    private Color _color = Color.FromRgb(0xFF, 0x4D, 0x4F);
    private double _stroke = 3;
    private int _mosaicBlock = 10;
    private Point _start;
    private bool _mouseDown;
    private Rect _selection = Rect.Empty;
    private Rect _candidate = Rect.Empty;
    private readonly List<Ann> _anns = new();
    private readonly List<Ann> _redo = new();
    private Ann? _drawing;
    private DragKind _dragKind = DragKind.None;
    private int _handleIndex = -1;
    private Rect _dragOrigin;
    private Point _dragStart;

    // 吸管：从画面取任意颜色作为标注色
    private bool _pickingColor;
    private Border? _eyedropperBtn;
    private Border? _customChip;

    // 调色板里的“自定义颜色”槽位在同一次会话内跨截图保留
    private static int[]? _paletteCustomColors;

    public OverlayWindow(ScreenShotData shot, OverlaySession session, CaptureAction? directAction)
    {
        _shot = shot;
        _session = session;
        _directAction = directAction;
        session.Windows.Add(this);
        _windowRects = CollectWindowRects();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;
        Left = 0; Top = 0; Width = 200; Height = 200;

        var source = CaptureService.ToBitmapSource(shot.Bitmap);
        var image = new Image { Source = source, Stretch = Stretch.Fill };

        var dimPath = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)),
            Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { _fullGeo, _holeGeo } },
        };

        _selBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xFF)),
            BorderThickness = new Thickness(1.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        _sizeLabel = new TextBlock { Foreground = Brushes.White, FontSize = 12 };
        _sizeLabelHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x14, 0x24)),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(8, 3, 8, 3),
            Child = _sizeLabel,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        var hint = new TextBlock
        {
            Text = directAction switch
            {
                CaptureAction.Ocr => "悬停高亮窗口，单击选中 · 拖拽自由选区 · C 取色 · Esc 取消",
                CaptureAction.Translate => "选择要翻译的区域 · 单击选窗口 · Esc 取消",
                _ => "单击选窗口 / 拖拽选区 · 选定后可调整与标注 · Enter 复制 · C 取色 · Esc 取消",
            },
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 14,
        };
        _hintHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x1A, 0x14, 0x24)),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(16, 8, 16, 8),
            Child = hint,
            IsHitTestVisible = false,
        };

        // ---------- 放大镜 ----------
        _zoomBrush = new ImageBrush(source) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        var zoomRect = new System.Windows.Shapes.Rectangle { Width = 110, Height = 110, Fill = _zoomBrush };
        RenderOptions.SetBitmapScalingMode(zoomRect, BitmapScalingMode.NearestNeighbor);
        var centerBox = new Border
        {
            Width = 10, Height = 10,
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _loupeText = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        };
        var loupeStack = new StackPanel();
        loupeStack.Children.Add(new Grid { Children = { zoomRect, centerBox }, Width = 110, Height = 110 });
        loupeStack.Children.Add(_loupeText);
        _loupe = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1A, 0x14, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(6),
            Child = loupeStack,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        // ---------- 工具栏 ----------
        var toolbarStack = new StackPanel();
        if (directAction is null)
        {
            toolbarStack.Children.Add(BuildToolRow());
            var optionHost = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xDB, 0xE3)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(3, 3, 3, 0),
                Child = _toolOptions,
            };
            optionHost.SetBinding(VisibilityProperty,
                new System.Windows.Data.Binding(nameof(Visibility)) { Source = _toolOptions });
            toolbarStack.Children.Add(optionHost);
        }
        else
        {
            toolbarStack.Children.Add(BuildActionRow());
        }
        _toolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9D, 0xA9, 0xB7)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(3),
            Child = toolbarStack,
            Visibility = Visibility.Collapsed,
        };

        // ---------- 选区手柄 ----------
        for (int i = 0; i < 8; i++)
        {
            var handle = new Border
            {
                Width = 9, Height = 9,
                CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xFF)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.2),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false, // 命中由窗口级坐标判断
            };
            _handles.Add(handle);
        }

        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(_canvas);
        _canvas.Children.Add(dimPath);
        _canvas.Children.Add(_annLayer);
        _canvas.Children.Add(_selBorder);
        foreach (var handle in _handles) _canvas.Children.Add(handle);
        _canvas.Children.Add(_sizeLabelHost);
        _canvas.Children.Add(_hintHost);
        _canvas.Children.Add(_loupe);
        _canvas.Children.Add(_toolbar);
        Content = root;

        Loaded += (_, _) =>
        {
            _fullGeo.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            _hintHost.UpdateLayout();
            Canvas.SetLeft(_hintHost, (ActualWidth - _hintHost.ActualWidth) / 2);
            Canvas.SetTop(_hintHost, 56);
        };

        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += OnUp;
        KeyDown += OnKey;

        // 多显示器：鼠标移到哪块屏，键盘焦点就跟到哪块屏的遮罩，
        // 否则 Enter/Ctrl+S 会打到启动时光标所在的那块屏上
        MouseEnter += (_, _) => { if (!IsActive) Activate(); };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var b = _shot.Screen.Bounds;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_SHOWWINDOW);
        };
    }

    // ==================== 工具栏 ====================
    private StackPanel BuildToolRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        void AddTool(Tool tool, IconKind icon, string tip, double fontSize = 16, double rotation = 0)
        {
            var btn = MakeIconSquare(icon, tip, fontSize);
            if (rotation != 0 && btn.Child is FrameworkElement glyph)
            {
                glyph.RenderTransformOrigin = new Point(0.5, 0.5);
                glyph.RenderTransform = new RotateTransform(rotation);
            }
            btn.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SetTool(_tool == tool ? Tool.None : tool);
            };
            _toolButtons.Add((tool, btn));
            row.Children.Add(btn);
        }

        AddTool(Tool.Rect, IconKind.RectangleLandscape, "矩形");
        AddTool(Tool.Ellipse, IconKind.Circle, "椭圆");
        AddTool(Tool.Line, IconKind.Line, "直线（Shift 吸附角度）");
        AddTool(Tool.Arrow, IconKind.ArrowUpRight, "箭头");
        AddTool(Tool.Pen, IconKind.Pen, "画笔");
        AddTool(Tool.Highlight, IconKind.Highlight, "荧光笔");
        AddTool(Tool.Mosaic, IconKind.Blur, "马赛克");
        AddTool(Tool.Step, IconKind.NumberCircle1, "序号标记");
        AddTool(Tool.Text, IconKind.TextT, "文字");

        row.Children.Add(MakeSeparator());

        var undo = MakeIconSquare(IconKind.ArrowUndo, "撤销 (Ctrl+Z)");
        undo.MouseLeftButtonUp += (_, e) => { e.Handled = true; Undo(); };
        row.Children.Add(undo);
        var redo = MakeIconSquare(IconKind.ArrowRedo, "重做 (Ctrl+Y)");
        redo.MouseLeftButtonUp += (_, e) => { e.Handled = true; Redo(); };
        row.Children.Add(redo);

        row.Children.Add(MakeSeparator());
        AddActionButtons(row);
        return row;
    }

    /// <summary>
    /// 只展示当前工具真正需要的参数：线条类显示粗细和颜色，文字显示字号和颜色，
    /// 序号显示大小和颜色，马赛克只显示颗粒大小。未选工具时参数区完全隐藏。
    /// </summary>
    private void BuildToolOptions(Tool tool)
    {
        _toolOptions.Children.Clear();
        _widthButtons.Clear();
        _colorChips.Clear();
        _mosaicButtons.Clear();
        _eyedropperBtn = null;
        _customChip = null;
        _pickingColor = false;

        if (tool == Tool.None)
        {
            _toolOptions.Visibility = Visibility.Collapsed;
            return;
        }

        bool width = tool is Tool.Rect or Tool.Ellipse or Tool.Line or Tool.Arrow
            or Tool.Pen or Tool.Highlight or Tool.Step or Tool.Text;
        bool color = tool is not Tool.Mosaic;

        if (tool == Tool.Mosaic)
        {
            _toolOptions.Children.Add(MakeOptionLabel("颗粒"));
            foreach (var (block, text) in new[] { (6, "小"), (10, "中"), (16, "大") })
            {
                var button = MakeLabelSquare(text, $"马赛克颗粒 {text}", 12);
                button.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _mosaicBlock = block;
                    UpdateMosaicHighlight();
                };
                _mosaicButtons.Add((block, button));
                _toolOptions.Children.Add(button);
            }
            UpdateMosaicHighlight();
        }

        if (width)
        {
            _toolOptions.Children.Add(MakeOptionLabel(tool == Tool.Text ? "字号" : tool == Tool.Step ? "大小" : "粗细"));
            foreach (var (value, glyph, fontSize) in tool == Tool.Text
                         ? new[] { (2.0, "14", 11.0), (3.0, "17", 11.0), (5.0, "22", 11.0) }
                         : new[] { (2.0, "●", 7.0), (3.0, "●", 10.0), (5.0, "●", 13.0) })
            {
                var button = MakeLabelSquare(glyph,
                    tool == Tool.Text ? $"字号 {TextSizeFor(value):0}" : $"粗细 {value:0}", fontSize);
                button.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _stroke = value;
                    UpdateWidthHighlight();
                };
                _widthButtons.Add((value, button));
                _toolOptions.Children.Add(button);
            }
            UpdateWidthHighlight();
        }

        if (color)
        {
            if (_toolOptions.Children.Count > 0) _toolOptions.Children.Add(MakeSeparator());
            _toolOptions.Children.Add(MakeOptionLabel("颜色"));
            foreach (var c in new[]
                     {
                         Color.FromRgb(0xFF, 0x4D, 0x4F), Color.FromRgb(0xFF, 0x9F, 0x0A),
                         Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x3B, 0x82, 0xF6),
                         Color.FromRgb(0x3D, 0x8B, 0xFF), Colors.White, Color.FromRgb(0x11, 0x11, 0x14),
                     })
            {
                var chip = new Border
                {
                    Width = 18, Height = 18,
                    CornerRadius = new CornerRadius(0),
                    Margin = new Thickness(3, 0, 3, 0),
                    Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x91, 0x9C, 0xA8)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
                };
                var selectedColor = c;
                chip.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _color = selectedColor;
                    UpdateChipHighlight();
                };
                _colorChips.Add((c, chip));
                _toolOptions.Children.Add(chip);
            }

            var eyedropper = MakeIconSquare(IconKind.Eyedropper, "从画面吸取颜色");
            eyedropper.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _pickingColor = !_pickingColor;
                SetButtonSelected(eyedropper, _pickingColor);
                Cursor = _pickingColor ? Cursors.Cross : (_tool == Tool.None ? Cursors.Arrow : Cursors.Cross);
            };
            _eyedropperBtn = eyedropper;
            _toolOptions.Children.Add(eyedropper);

            var palette = MakeIconSquare(IconKind.Color, "调色板：自选任意颜色");
            palette.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenColorPalette();
            };
            _toolOptions.Children.Add(palette);

            _customChip = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = new CornerRadius(0),
                Margin = new Thickness(3, 0, 3, 0),
                Background = new SolidColorBrush(_color),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x91, 0x9C, 0xA8)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "当前自定义颜色",
                Visibility = Visibility.Collapsed,
            };
            _toolOptions.Children.Add(_customChip);
            UpdateChipHighlight();
        }

        _toolOptions.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(RepositionToolbar, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private StackPanel BuildActionRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        AddActionButtons(row);
        return row;
    }

    private void AddActionButtons(Panel row)
    {
        void AddAction(IconKind icon, CaptureAction action, string tip, bool primary = false)
        {
            var btn = MakeIconSquare(icon, tip);
            SetButtonSelected(btn, primary);
            btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; CompleteWith(action); };
            row.Children.Add(btn);
        }

        AddAction(IconKind.Copy, CaptureAction.Copy, "复制（Enter / 双击）", primary: true);
        AddAction(IconKind.Save, CaptureAction.Save, "保存到默认目录");
        AddAction(IconKind.SaveCopy, CaptureAction.SaveAs, "另存为（Ctrl+S）");
        AddAction(IconKind.Pin, CaptureAction.Pin, "钉住（F3）");
        AddAction(IconKind.ScanText, CaptureAction.Ocr, "OCR 识别");
        AddAction(IconKind.Translate, CaptureAction.Translate, "翻译");

        var cancel = MakeIconSquare(IconKind.Dismiss, "取消（Esc）");
        cancel.ToolTip = "取消 (Esc)";
        cancel.MouseLeftButtonUp += (_, e) => { e.Handled = true; _session.Complete(null); };
        row.Children.Add(cancel);
    }

    private static Border MakeIconSquare(IconKind icon, string tip, double fontSize = 16) => new()
    {
        Width = 30, Height = 30,
        CornerRadius = new CornerRadius(0),
        Margin = new Thickness(0),
        Background = IdleToolBrush(),
        Cursor = Cursors.Hand,
        ToolTip = tip,
        Child = AppIconFactory.Create(icon, fontSize, ToolbarTextBrush()),
    };

    private static Border MakeLabelSquare(string label, string tip, double fontSize = 12) => new()
    {
        Width = 30, Height = 30,
        CornerRadius = new CornerRadius(0),
        Margin = new Thickness(0),
        Background = IdleToolBrush(),
        Cursor = Cursors.Hand,
        ToolTip = tip,
        Child = new TextBlock
        {
            Text = label,
            Foreground = ToolbarTextBrush(),
            FontSize = fontSize,
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static System.Windows.Shapes.Rectangle MakeSeparator() => new()
    {
        Width = 1, Height = 20,
        Fill = new SolidColorBrush(Color.FromRgb(0xD4, 0xDB, 0xE3)),
        Margin = new Thickness(4, 0, 4, 0),
    };

    private static TextBlock MakeOptionLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x67, 0x75)),
        FontSize = 11,
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static SolidColorBrush SelectedToolBrush()
        => new(Color.FromRgb(0x1E, 0x88, 0xE5));

    private static SolidColorBrush IdleToolBrush()
        => new(Colors.Transparent);

    private static SolidColorBrush ToolbarTextBrush()
        => new(Color.FromRgb(0x1D, 0x28, 0x36));

    private static void SetButtonSelected(Border button, bool selected)
    {
        button.Background = selected ? SelectedToolBrush() : IdleToolBrush();
        var foreground = selected ? Brushes.White : ToolbarTextBrush();
        if (button.Child is TextBlock text)
            text.Foreground = foreground;
        else if (button.Child is FluentIcon icon)
            icon.Foreground = foreground;
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        foreach (var (t, btn) in _toolButtons)
            SetButtonSelected(btn, t == tool);
        BuildToolOptions(tool);
        if (_phase == Phase.Edit)
            Cursor = tool == Tool.None ? Cursors.Arrow : Cursors.Cross;
    }

    private void UpdateChipHighlight()
    {
        bool matchedPreset = false;
        foreach (var (c, chip) in _colorChips)
        {
            bool hit = c == _color;
            matchedPreset |= hit;
            chip.BorderBrush = hit
                ? new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5))
                : new SolidColorBrush(Color.FromRgb(0x91, 0x9C, 0xA8));
            chip.BorderThickness = new Thickness(hit ? 2 : 1);
        }

        // 自定义色时把当前色显示出来并高亮
        if (_customChip is not null)
        {
            _customChip.Background = new SolidColorBrush(_color);
            _customChip.BorderBrush = matchedPreset
                ? new SolidColorBrush(Color.FromRgb(0x91, 0x9C, 0xA8))
                : new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
            _customChip.BorderThickness = new Thickness(matchedPreset ? 1 : 2);
            _customChip.Visibility = matchedPreset ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>吸管模式下取画面像素作为标注色。</summary>
    private void PickColorAt(Point pos)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int px = Math.Clamp((int)(pos.X * dpi.DpiScaleX), 0, _shot.Bitmap.Width - 1);
        int py = Math.Clamp((int)(pos.Y * dpi.DpiScaleY), 0, _shot.Bitmap.Height - 1);
        var c = _shot.Bitmap.GetPixel(px, py);

        _color = Color.FromRgb(c.R, c.G, c.B);
        UpdateChipHighlight();

        _pickingColor = false;
        if (_eyedropperBtn is not null)
            SetButtonSelected(_eyedropperBtn, false);
        Cursor = _tool == Tool.None ? Cursors.Arrow : Cursors.Cross;
        Toast.Show($"标注颜色已设为 #{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    /// <summary>
    /// 系统调色板：预设色之外自选任意颜色。用 WinForms ColorDialog 而不自绘，
    /// 因为它自带 HSV 面板与 16 个自定义槽位，且项目已引用 WinForms，零新增依赖。
    /// </summary>
    private void OpenColorPalette()
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(_color.R, _color.G, _color.B),
        };
        if (_paletteCustomColors is not null)
            dialog.CustomColors = _paletteCustomColors;

        // 把全屏遮罩设为 owner，对话框才能浮在 Topmost 窗口之上。
        var owner = new Win32WindowHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        bool accepted = dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK;
        _paletteCustomColors = dialog.CustomColors;
        if (!accepted) return;

        _color = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        UpdateChipHighlight();
        Toast.Show($"标注颜色已设为 #{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
    }

    private sealed class Win32WindowHandle : System.Windows.Forms.IWin32Window
    {
        public Win32WindowHandle(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    private void UpdateWidthHighlight()
    {
        foreach (var (w, btn) in _widthButtons)
            SetButtonSelected(btn, Math.Abs(w - _stroke) < 0.1);
    }

    private void UpdateMosaicHighlight()
    {
        foreach (var (block, button) in _mosaicButtons)
            SetButtonSelected(button, block == _mosaicBlock);
    }

    private static double TextSizeFor(double stroke) => stroke switch { <= 2 => 14, >= 5 => 22, _ => 17 };
    private double TextFontSize => TextSizeFor(_stroke);
    private double StepDiameter => 18 + _stroke * 2.4;

    // ==================== 鼠标 ====================
    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(_canvas);

        if (e.ChangedButton == MouseButton.Right)
        {
            if (_phase == Phase.Edit) ResetToPick();
            else _session.Complete(null);
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        if (_phase == Phase.Edit)
        {
            // 吸管优先：点哪取哪
            if (_pickingColor)
            {
                PickColorAt(pos);
                return;
            }

            if (_tool == Tool.None)
            {
                if (e.ClickCount == 2 && _selection.Contains(pos))
                {
                    CompleteWith(CaptureAction.Copy);
                    return;
                }
                int handle = HitHandle(pos);
                if (handle >= 0)
                {
                    _dragKind = DragKind.Resize;
                    _handleIndex = handle;
                    _dragOrigin = _selection;
                    _dragStart = pos;
                    CaptureMouse();
                }
                else if (_selection.Contains(pos))
                {
                    _dragKind = DragKind.Move;
                    _dragOrigin = _selection;
                    _dragStart = pos;
                    Cursor = Cursors.SizeAll;
                    CaptureMouse();
                }
                return;
            }

            if (!_selection.Contains(pos)) return;
            BeginAnnotation(pos);
            return;
        }

        _start = pos;
        _mouseDown = true;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_canvas);

        if (_phase == Phase.Edit)
        {
            if (_dragKind == DragKind.Move)
            {
                var delta = pos - _dragStart;
                var moved = new Rect(_dragOrigin.X + delta.X, _dragOrigin.Y + delta.Y,
                    _dragOrigin.Width, _dragOrigin.Height);
                moved.X = Math.Clamp(moved.X, 0, Math.Max(0, ActualWidth - moved.Width));
                moved.Y = Math.Clamp(moved.Y, 0, Math.Max(0, ActualHeight - moved.Height));
                _selection = moved;
                UpdateEditChrome();
            }
            else if (_dragKind == DragKind.Resize)
            {
                _selection = ResizeByHandle(_dragOrigin, _handleIndex, pos - _dragStart);
                UpdateEditChrome();
            }
            else if (_drawing is not null)
            {
                UpdateAnnotation(pos);
            }
            else if (_tool == Tool.None)
            {
                int handle = HitHandle(pos);
                Cursor = handle switch
                {
                    0 or 4 => Cursors.SizeNWSE,
                    2 or 6 => Cursors.SizeNESW,
                    1 or 5 => Cursors.SizeNS,
                    3 or 7 => Cursors.SizeWE,
                    _ => _selection.Contains(pos) ? Cursors.SizeAll : Cursors.Arrow,
                };
            }
            return;
        }

        UpdateLoupe(pos);

        if (_mouseDown && (_phase == Phase.Drag || (pos - _start).Length > 4))
        {
            _phase = Phase.Drag;
            _selection = new Rect(_start, pos);
            ShowSelectionVisual(_selection);
        }
        else if (_phase == Phase.Pick)
        {
            _candidate = DetectWindowRect(pos);
            ShowSelectionVisual(_candidate);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_phase == Phase.Edit)
        {
            if (_dragKind != DragKind.None)
            {
                _dragKind = DragKind.None;
                _handleIndex = -1;
                ReleaseMouseCapture();
                RepositionToolbar();
                return;
            }
            EndAnnotation();
            return;
        }

        if (!_mouseDown) return;
        _mouseDown = false;
        ReleaseMouseCapture();

        var pos = e.GetPosition(_canvas);
        if (_phase == Phase.Drag)
        {
            _selection = new Rect(_start, pos);
            if (_selection.Width < 5 || _selection.Height < 5)
                _selection = _candidate.IsEmpty ? new Rect(0, 0, ActualWidth, ActualHeight) : _candidate;
        }
        else
        {
            _selection = _candidate.IsEmpty ? new Rect(0, 0, ActualWidth, ActualHeight) : _candidate;
        }

        if (_directAction is { } action)
            CompleteWith(action);
        else
            EnterEdit();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        // 正在标注文本框里打字时，所有按键都归编辑框（否则 Enter 会直接结束截图、
        // Esc 会取消整次截图、Ctrl+Z 撤销的是标注而不是文字）
        if (e.OriginalSource is TextBox)
        {
            if (e.Key == Key.Escape)
            {
                // Esc 只退出文本编辑，不取消截图
                Keyboard.ClearFocus();
                Focus();
                e.Handled = true;
            }
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (e.Key)
        {
            case Key.Escape:
                _session.Complete(null);
                return;
            case Key.Z when ctrl && shift:
                Redo(); return;
            case Key.Z when ctrl:
                Undo(); return;
            case Key.Y when ctrl:
                Redo(); return;
            case Key.C when ctrl && _phase == Phase.Edit:
                CompleteWith(CaptureAction.Copy); return;
            case Key.S when ctrl && _phase == Phase.Edit:
                CompleteWith(CaptureAction.SaveAs); return;
            case Key.F3 when _phase == Phase.Edit:
                CompleteWith(CaptureAction.Pin); return;
            // 只认已确定的选区：_candidate 只是悬停高亮，还没被用户点选
            case Key.Enter when !_selection.IsEmpty:
                CompleteWith(_directAction ?? CaptureAction.Copy);
                return;
            case Key.C when _phase != Phase.Edit:
            {
                // 选区前按 C：复制光标处颜色
                var pos = Mouse.GetPosition(_canvas);
                var dpi = VisualTreeHelper.GetDpi(this);
                int px = Math.Clamp((int)(pos.X * dpi.DpiScaleX), 0, _shot.Bitmap.Width - 1);
                int py = Math.Clamp((int)(pos.Y * dpi.DpiScaleY), 0, _shot.Bitmap.Height - 1);
                var c = _shot.Bitmap.GetPixel(px, py);
                string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                try { System.Windows.Clipboard.SetText(hex); } catch { }
                Toast.Show($"已复制颜色 {hex}");
                return;
            }
        }

        // 方向键微调（Edit 阶段）：移动选区；Shift+方向键 = 调整大小
        if (_phase == Phase.Edit && _tool == Tool.None &&
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            double dx = e.Key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
            double dy = e.Key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
            if (shift)
            {
                _selection = new Rect(_selection.X, _selection.Y,
                    Math.Max(5, _selection.Width + dx), Math.Max(5, _selection.Height + dy));
            }
            else
            {
                _selection = new Rect(
                    Math.Clamp(_selection.X + dx, 0, Math.Max(0, ActualWidth - _selection.Width)),
                    Math.Clamp(_selection.Y + dy, 0, Math.Max(0, ActualHeight - _selection.Height)),
                    _selection.Width, _selection.Height);
            }
            UpdateEditChrome();
            RepositionToolbar();
            e.Handled = true;
        }
    }

    // ==================== 选区手柄 ====================
    private Point HandleCenter(int index) => index switch
    {
        0 => _selection.TopLeft,
        1 => new Point(_selection.X + _selection.Width / 2, _selection.Y),
        2 => _selection.TopRight,
        3 => new Point(_selection.Right, _selection.Y + _selection.Height / 2),
        4 => _selection.BottomRight,
        5 => new Point(_selection.X + _selection.Width / 2, _selection.Bottom),
        6 => _selection.BottomLeft,
        _ => new Point(_selection.X, _selection.Y + _selection.Height / 2),
    };

    private int HitHandle(Point pos)
    {
        if (_selection.IsEmpty) return -1;
        for (int i = 0; i < 8; i++)
        {
            var c = HandleCenter(i);
            if (Math.Abs(pos.X - c.X) <= 7 && Math.Abs(pos.Y - c.Y) <= 7)
                return i;
        }
        return -1;
    }

    private static Rect ResizeByHandle(Rect origin, int handle, Vector delta)
    {
        double left = origin.Left, top = origin.Top, right = origin.Right, bottom = origin.Bottom;
        if (handle is 0 or 6 or 7) left += delta.X;
        if (handle is 2 or 3 or 4) right += delta.X;
        if (handle is 0 or 1 or 2) top += delta.Y;
        if (handle is 4 or 5 or 6) bottom += delta.Y;
        return new Rect(
            Math.Min(left, right), Math.Min(top, bottom),
            Math.Max(5, Math.Abs(right - left)), Math.Max(5, Math.Abs(bottom - top)));
    }

    private void PositionHandles()
    {
        bool show = _phase == Phase.Edit && !_selection.IsEmpty;
        for (int i = 0; i < 8; i++)
        {
            var handle = _handles[i];
            handle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) continue;
            var c = HandleCenter(i);
            Canvas.SetLeft(handle, c.X - handle.Width / 2);
            Canvas.SetTop(handle, c.Y - handle.Height / 2);
        }
    }

    private void UpdateEditChrome()
    {
        ShowSelectionVisual(_selection);
        _annLayer.Clip = new RectangleGeometry(_selection);
        PositionHandles();
    }

    private void EnterEdit()
    {
        _phase = Phase.Edit;
        Cursor = Cursors.Arrow;
        _loupe.Visibility = Visibility.Collapsed;
        _hintHost.Visibility = Visibility.Collapsed;
        UpdateEditChrome();
        _toolbar.Visibility = Visibility.Visible;
        RepositionToolbar();
    }

    /// <summary>仅供 --smoke 生成截图工具栏的确定状态，便于视觉回归。</summary>
    internal FrameworkElement PrepareToolbarForSelfTest()
    {
        double width = Math.Max(360, Math.Min(920, ActualWidth - 80));
        double height = Math.Max(180, Math.Min(360, ActualHeight - 180));
        _selection = new Rect(
            Math.Max(30, (ActualWidth - width) / 2),
            Math.Max(30, (ActualHeight - height) / 2 - 40),
            width,
            height);
        EnterEdit();
        SetTool(Tool.Pen);
        _toolbar.UpdateLayout();
        RepositionToolbar();
        return _toolbar;
    }

    private void RepositionToolbar()
    {
        if (_toolbar.Visibility != Visibility.Visible) return;
        _toolbar.UpdateLayout();
        double tx = _selection.Right - _toolbar.ActualWidth;
        double ty = _selection.Bottom + 10;
        if (ty + _toolbar.ActualHeight > ActualHeight - 4)
            ty = Math.Max(4, _selection.Top - _toolbar.ActualHeight - 10);
        tx = Math.Clamp(tx, 4, Math.Max(4, ActualWidth - _toolbar.ActualWidth - 4));
        Canvas.SetLeft(_toolbar, tx);
        Canvas.SetTop(_toolbar, ty);
    }

    private void ResetToPick()
    {
        foreach (var ann in _anns)
            foreach (var v in ann.Visuals)
                _annLayer.Children.Remove(v);
        _anns.Clear();
        _redo.Clear();
        _drawing = null;
        _dragKind = DragKind.None;
        SetTool(Tool.None);

        _phase = Phase.Pick;
        Cursor = Cursors.Cross;
        _selection = Rect.Empty;
        _candidate = Rect.Empty;
        _holeGeo.Rect = Rect.Empty;
        _selBorder.Visibility = Visibility.Collapsed;
        _sizeLabelHost.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = Visibility.Collapsed;
        _hintHost.Visibility = Visibility.Visible;
        _annLayer.Clip = null;
        PositionHandles();
    }

    private void ShowSelectionVisual(Rect rect)
    {
        if (rect.IsEmpty)
        {
            _holeGeo.Rect = Rect.Empty;
            _selBorder.Visibility = Visibility.Collapsed;
            _sizeLabelHost.Visibility = Visibility.Collapsed;
            return;
        }

        _fullGeo.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
        _holeGeo.Rect = rect;

        _selBorder.Visibility = Visibility.Visible;
        _selBorder.Width = Math.Max(0, rect.Width);
        _selBorder.Height = Math.Max(0, rect.Height);
        Canvas.SetLeft(_selBorder, rect.X);
        Canvas.SetTop(_selBorder, rect.Y);

        var dpi = VisualTreeHelper.GetDpi(this);
        _sizeLabel.Text = $"{(int)Math.Round(rect.Width * dpi.DpiScaleX)} × {(int)Math.Round(rect.Height * dpi.DpiScaleY)}";
        _sizeLabelHost.Visibility = Visibility.Visible;
        _sizeLabelHost.UpdateLayout();
        double ly = rect.Y - _sizeLabelHost.ActualHeight - 6;
        if (ly < 4) ly = rect.Y + 6;
        Canvas.SetLeft(_sizeLabelHost, Math.Max(4, rect.X));
        Canvas.SetTop(_sizeLabelHost, ly);
    }

    // ==================== 放大镜 ====================
    private void UpdateLoupe(Point pos)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int px = Math.Clamp((int)(pos.X * dpi.DpiScaleX), 0, _shot.Bitmap.Width - 1);
        int py = Math.Clamp((int)(pos.Y * dpi.DpiScaleY), 0, _shot.Bitmap.Height - 1);

        var c = _shot.Bitmap.GetPixel(px, py);
        _loupeText.Text = $"{px}, {py}\n#{c.R:X2}{c.G:X2}{c.B:X2}  {c.R},{c.G},{c.B}";

        const int cells = 11;
        _zoomBrush.Viewbox = new Rect(px - cells / 2.0 + 0.5, py - cells / 2.0 + 0.5, cells, cells);

        _loupe.Visibility = Visibility.Visible;
        _loupe.UpdateLayout();
        double lx = pos.X + 22, ly = pos.Y + 22;
        if (lx + _loupe.ActualWidth > ActualWidth - 8) lx = pos.X - _loupe.ActualWidth - 22;
        if (ly + _loupe.ActualHeight > ActualHeight - 8) ly = pos.Y - _loupe.ActualHeight - 22;
        Canvas.SetLeft(_loupe, Math.Max(4, lx));
        Canvas.SetTop(_loupe, Math.Max(4, ly));
    }

    // ==================== 标注 ====================
    private void BeginAnnotation(Point pos)
    {
        _redo.Clear();
        switch (_tool)
        {
            case Tool.Rect or Tool.Ellipse:
            {
                CaptureMouse();
                var ann = new ShapeAnn { R = new Rect(pos, pos), IsEllipse = _tool == Tool.Ellipse, C = _color, W = _stroke };
                System.Windows.Shapes.Shape shape = _tool == Tool.Ellipse
                    ? new System.Windows.Shapes.Ellipse()
                    : new System.Windows.Shapes.Rectangle();
                shape.Stroke = new SolidColorBrush(_color);
                shape.StrokeThickness = _stroke;
                ann.Visuals.Add(shape);
                _annLayer.Children.Add(shape);
                _drawing = ann;
                _start = pos;
                break;
            }
            case Tool.Line or Tool.Arrow:
            {
                CaptureMouse();
                var ann = new LineAnn { A = pos, B = pos, C = _color, W = _stroke, HasHead = _tool == Tool.Arrow };
                var line = new System.Windows.Shapes.Line
                {
                    Stroke = new SolidColorBrush(_color), StrokeThickness = _stroke,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                };
                ann.Visuals.Add(line);
                _annLayer.Children.Add(line);
                if (ann.HasHead)
                {
                    var head = new System.Windows.Shapes.Polygon { Fill = new SolidColorBrush(_color) };
                    ann.Visuals.Add(head);
                    _annLayer.Children.Add(head);
                }
                _drawing = ann;
                _start = pos;
                break;
            }
            case Tool.Pen or Tool.Highlight:
            {
                CaptureMouse();
                bool highlight = _tool == Tool.Highlight;
                var color = highlight ? Color.FromArgb(0x59, _color.R, _color.G, _color.B) : _color;
                var ann = new PenAnn { C = _color, W = _stroke, Highlight = highlight };
                ann.Pts.Add(pos);
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = highlight ? _stroke * 3.5 : _stroke,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = highlight ? PenLineCap.Flat : PenLineCap.Round,
                    StrokeEndLineCap = highlight ? PenLineCap.Flat : PenLineCap.Round,
                };
                poly.Points.Add(pos);
                ann.Visuals.Add(poly);
                _annLayer.Children.Add(poly);
                _drawing = ann;
                break;
            }
            case Tool.Mosaic:
            {
                CaptureMouse();
                var ann = new MosaicAnn { R = new Rect(pos, pos), Block = _mosaicBlock };
                var preview = new System.Windows.Shapes.Rectangle
                {
                    Stroke = Brushes.White, StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                };
                ann.Visuals.Add(preview);
                _annLayer.Children.Add(preview);
                _drawing = ann;
                _start = pos;
                break;
            }
            case Tool.Step:
            {
                int n = _anns.OfType<StepAnn>().Select(a => a.N).DefaultIfEmpty(0).Max() + 1;
                double d = StepDiameter;
                var ann = new StepAnn { P = pos, N = n, C = _color, D = d };
                var grid = new Grid { Width = d, Height = d, IsHitTestVisible = false };
                grid.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Fill = new SolidColorBrush(_color),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                });
                grid.Children.Add(new TextBlock
                {
                    Text = n.ToString(),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = d * 0.52,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                Canvas.SetLeft(grid, pos.X - d / 2);
                Canvas.SetTop(grid, pos.Y - d / 2);
                ann.Visuals.Add(grid);
                _annLayer.Children.Add(grid);
                _anns.Add(ann);
                break;
            }
            case Tool.Text:
            {
                var box = new TextBox
                {
                    MinWidth = 40,
                    FontSize = TextFontSize,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                    Foreground = new SolidColorBrush(_color),
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3, 1, 3, 1),
                    CaretBrush = Brushes.White,
                    AcceptsReturn = true,
                };
                var ann = new TextAnn { P = pos, Box = box, C = _color, Size = TextFontSize };
                ann.Visuals.Add(box);
                Canvas.SetLeft(box, pos.X);
                Canvas.SetTop(box, pos.Y);
                _annLayer.Children.Add(box);
                _anns.Add(ann);
                box.Focus();
                break;
            }
        }
    }

    private void UpdateAnnotation(Point pos)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_drawing)
        {
            case ShapeAnn s:
                s.R = shift ? SquareRect(_start, pos) : new Rect(_start, pos);
                PlaceShape((System.Windows.Shapes.Shape)s.Visuals[0], s.R);
                break;
            case LineAnn a:
                a.B = shift ? SnapAngle(_start, pos) : pos;
                UpdateLineVisual(a);
                break;
            case PenAnn p:
                p.Pts.Add(pos);
                ((System.Windows.Shapes.Polyline)p.Visuals[0]).Points.Add(pos);
                break;
            case MosaicAnn m:
                m.R = new Rect(_start, pos);
                PlaceShape((System.Windows.Shapes.Shape)m.Visuals[0], m.R);
                break;
        }
    }

    private static Rect SquareRect(Point origin, Point pos)
    {
        double size = Math.Max(Math.Abs(pos.X - origin.X), Math.Abs(pos.Y - origin.Y));
        double x = pos.X >= origin.X ? origin.X : origin.X - size;
        double y = pos.Y >= origin.Y ? origin.Y : origin.Y - size;
        return new Rect(x, y, size, size);
    }

    private static Point SnapAngle(Point origin, Point pos)
    {
        var v = pos - origin;
        double angle = Math.Atan2(v.Y, v.X);
        double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        double len = v.Length;
        return origin + new Vector(Math.Cos(snapped) * len, Math.Sin(snapped) * len);
    }

    private void EndAnnotation()
    {
        ReleaseMouseCapture();
        if (_drawing is null) return;
        var ann = _drawing;
        _drawing = null;

        bool tooSmall = ann switch
        {
            ShapeAnn s => s.R.Width < 4 && s.R.Height < 4,
            LineAnn a => (a.B - a.A).Length < 6,
            PenAnn p => p.Pts.Count < 2,
            MosaicAnn m => m.R.Width < 6 || m.R.Height < 6,
            _ => false,
        };
        if (tooSmall)
        {
            foreach (var v in ann.Visuals) _annLayer.Children.Remove(v);
            return;
        }

        if (ann is MosaicAnn mosaic)
        {
            foreach (var v in mosaic.Visuals) _annLayer.Children.Remove(v);
            mosaic.Visuals.Clear();

            var dpi = VisualTreeHelper.GetDpi(this);
            var phys = ToPhysical(mosaic.R, dpi);
            using var region = CaptureService.Crop(_shot.Bitmap, phys);
            using var pixelated = Pixelate(region, Math.Max(4, (int)(mosaic.Block * dpi.DpiScaleX)));
            var img = new Image
            {
                Source = CaptureService.ToBitmapSource(pixelated),
                Width = mosaic.R.Width,
                Height = mosaic.R.Height,
                Stretch = Stretch.Fill,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(img, mosaic.R.X);
            Canvas.SetTop(img, mosaic.R.Y);
            mosaic.Visuals.Add(img);
            _annLayer.Children.Add(img);
        }

        _anns.Add(ann);
    }

    private static void PlaceShape(System.Windows.Shapes.Shape shape, Rect r)
    {
        shape.Width = Math.Max(0, r.Width);
        shape.Height = Math.Max(0, r.Height);
        Canvas.SetLeft(shape, r.X);
        Canvas.SetTop(shape, r.Y);
    }

    private static void UpdateLineVisual(LineAnn a)
    {
        var line = (System.Windows.Shapes.Line)a.Visuals[0];
        var v = a.B - a.A;
        double len = v.Length;
        if (len < 0.1) return;

        if (a.HasHead)
        {
            var dir = v / len;
            double headLen = Math.Min(16, 6 + len * 0.12);
            var back = a.B - dir * headLen;
            line.X1 = a.A.X; line.Y1 = a.A.Y;
            line.X2 = back.X; line.Y2 = back.Y;

            var head = (System.Windows.Shapes.Polygon)a.Visuals[1];
            var normal = new Vector(-dir.Y, dir.X) * headLen * 0.45;
            head.Points = new PointCollection { a.B, back + normal, back - normal };
        }
        else
        {
            line.X1 = a.A.X; line.Y1 = a.A.Y;
            line.X2 = a.B.X; line.Y2 = a.B.Y;
        }
    }

    private void Undo()
    {
        if (_anns.Count == 0) return;
        var ann = _anns[^1];
        _anns.RemoveAt(_anns.Count - 1);
        foreach (var v in ann.Visuals) _annLayer.Children.Remove(v);
        _redo.Add(ann);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        var ann = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (var v in ann.Visuals) _annLayer.Children.Add(v);
        _anns.Add(ann);
    }

    // ==================== 完成 ====================
    private void CompleteWith(CaptureAction action)
    {
        if (_selection.IsEmpty || _selection.Width < 1 || _selection.Height < 1)
            _selection = new Rect(0, 0, ActualWidth, ActualHeight);

        var dpi = VisualTreeHelper.GetDpi(this);
        var cropped = CaptureService.Crop(_shot.Bitmap, ToPhysical(_selection, dpi));
        RenderAnnotations(cropped, dpi);
        _session.Complete(new CaptureSelection(cropped, action));
    }

    private System.Drawing.Rectangle ToPhysical(Rect r, DpiScale dpi) => new(
        (int)Math.Round(r.X * dpi.DpiScaleX),
        (int)Math.Round(r.Y * dpi.DpiScaleY),
        (int)Math.Round(r.Width * dpi.DpiScaleX),
        (int)Math.Round(r.Height * dpi.DpiScaleY));

    /// <summary>把标注绘制到裁剪后的位图（DIP → 选区内物理坐标）。顺序与屏幕一致。</summary>
    private void RenderAnnotations(System.Drawing.Bitmap bmp, DpiScale dpi)
    {
        if (_anns.Count == 0) return;

        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        float X(double x) => (float)((x - _selection.X) * sx);
        float Y(double y) => (float)((y - _selection.Y) * sy);
        System.Drawing.Color GC(Color c) => System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        foreach (var ann in _anns)
        {
            switch (ann)
            {
                case ShapeAnn s:
                {
                    using var pen = new System.Drawing.Pen(GC(s.C), (float)(s.W * sx))
                    { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                    var rect = new System.Drawing.RectangleF(X(s.R.X), Y(s.R.Y), (float)(s.R.Width * sx), (float)(s.R.Height * sy));
                    if (s.IsEllipse) g.DrawEllipse(pen, rect);
                    else g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    break;
                }
                case LineAnn a:
                {
                    using var pen = new System.Drawing.Pen(GC(a.C), (float)(a.W * sx))
                    {
                        StartCap = System.Drawing.Drawing2D.LineCap.Round,
                        EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    };
                    if (a.HasHead)
                        pen.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4f, 6f, true);
                    g.DrawLine(pen, X(a.A.X), Y(a.A.Y), X(a.B.X), Y(a.B.Y));
                    break;
                }
                case PenAnn p when p.Pts.Count >= 2:
                {
                    var color = p.Highlight ? System.Drawing.Color.FromArgb(0x59, p.C.R, p.C.G, p.C.B) : GC(p.C);
                    float width = (float)((p.Highlight ? p.W * 3.5 : p.W) * sx);
                    using var pen = new System.Drawing.Pen(color, width)
                    {
                        LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
                        StartCap = p.Highlight ? System.Drawing.Drawing2D.LineCap.Flat : System.Drawing.Drawing2D.LineCap.Round,
                        EndCap = p.Highlight ? System.Drawing.Drawing2D.LineCap.Flat : System.Drawing.Drawing2D.LineCap.Round,
                    };
                    var pts = p.Pts.Select(pt => new System.Drawing.PointF(X(pt.X), Y(pt.Y))).ToArray();
                    g.DrawLines(pen, pts);
                    break;
                }
                case MosaicAnn m:
                {
                    var rect = new System.Drawing.Rectangle(
                        (int)X(m.R.X), (int)Y(m.R.Y),
                        (int)(m.R.Width * sx), (int)(m.R.Height * sy));
                    PixelateInPlace(bmp, rect, Math.Max(4, (int)(m.Block * sx)), g);
                    break;
                }
                case StepAnn st:
                {
                    float d = (float)(st.D * sx);
                    float cx = X(st.P.X) - d / 2, cy = Y(st.P.Y) - d / 2;
                    using var brush = new System.Drawing.SolidBrush(GC(st.C));
                    using var ring = new System.Drawing.Pen(System.Drawing.Color.White, (float)(1.5 * sx));
                    g.FillEllipse(brush, cx, cy, d, d);
                    g.DrawEllipse(ring, cx, cy, d, d);
                    using var font = new System.Drawing.Font("Segoe UI", d * 0.5f,
                        System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                    using var fmt = new System.Drawing.StringFormat
                    {
                        Alignment = System.Drawing.StringAlignment.Center,
                        LineAlignment = System.Drawing.StringAlignment.Center,
                    };
                    g.DrawString(st.N.ToString(), font, System.Drawing.Brushes.White,
                        new System.Drawing.RectangleF(cx, cy + d * 0.02f, d, d), fmt);
                    break;
                }
                case TextAnn t when !string.IsNullOrWhiteSpace(t.Box.Text):
                {
                    using var font = new System.Drawing.Font("Microsoft YaHei UI", (float)(t.Size * sx),
                        System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                    using var brush = new System.Drawing.SolidBrush(GC(t.C));
                    using var shadow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x90, 0, 0, 0));
                    float tx = X(t.P.X) + (float)(4 * sx), ty = Y(t.P.Y) + (float)(2 * sy);
                    g.DrawString(t.Box.Text, font, shadow, tx + 1.2f, ty + 1.2f);
                    g.DrawString(t.Box.Text, font, brush, tx, ty);
                    break;
                }
            }
        }
    }

    private static System.Drawing.Bitmap Pixelate(System.Drawing.Bitmap src, int block)
    {
        int sw = Math.Max(1, src.Width / block), sh = Math.Max(1, src.Height / block);
        var result = new System.Drawing.Bitmap(src.Width, src.Height);
        using var small = new System.Drawing.Bitmap(sw, sh);
        using (var gs = System.Drawing.Graphics.FromImage(small))
        {
            gs.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            gs.DrawImage(src, new System.Drawing.Rectangle(0, 0, sw, sh));
        }
        using (var gr = System.Drawing.Graphics.FromImage(result))
        {
            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            gr.DrawImage(small, new System.Drawing.Rectangle(0, 0, src.Width, src.Height));
        }
        return result;
    }

    private static void PixelateInPlace(System.Drawing.Bitmap bmp, System.Drawing.Rectangle rect, int block, System.Drawing.Graphics g)
    {
        rect.Intersect(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
        if (rect.Width < 2 || rect.Height < 2) return;
        using var region = bmp.Clone(rect, bmp.PixelFormat);
        using var pixelated = Pixelate(region, block);
        var oldMode = g.InterpolationMode;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.DrawImage(pixelated, rect);
        g.InterpolationMode = oldMode;
    }

    // ==================== 窗口识别 ====================
    private Rect DetectWindowRect(Point posDip)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var b = _shot.Screen.Bounds;
        int px = b.X + (int)(posDip.X * dpi.DpiScaleX);
        int py = b.Y + (int)(posDip.Y * dpi.DpiScaleY);

        foreach (var r in _windowRects)
        {
            if (!r.Contains(px, py)) continue;
            var clipped = System.Drawing.Rectangle.Intersect(r, b);
            if (clipped.Width < 10 || clipped.Height < 10) continue;
            return new Rect(
                (clipped.X - b.X) / dpi.DpiScaleX,
                (clipped.Y - b.Y) / dpi.DpiScaleY,
                clipped.Width / dpi.DpiScaleX,
                clipped.Height / dpi.DpiScaleY);
        }
        return Rect.Empty;
    }

    private static List<System.Drawing.Rectangle> CollectWindowRects()
    {
        var rects = new List<System.Drawing.Rectangle>();
        int myPid = Environment.ProcessId;

        Win.EnumWindows((hwnd, _) =>
        {
            if (!Win.IsWindowVisible(hwnd) || Win.IsIconic(hwnd)) return true;

            Win.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == myPid) return true;

            if (Win.DwmGetWindowAttribute(hwnd, 14 /*DWMWA_CLOAKED*/, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            if (Win.DwmGetWindowAttribute(hwnd, 9 /*EXTENDED_FRAME_BOUNDS*/, out Win.RECT r, Marshal.SizeOf<Win.RECT>()) != 0)
                return true;

            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w < 40 || h < 40) return true;

            rects.Add(new System.Drawing.Rectangle(r.Left, r.Top, w, h));
            return true;
        }, IntPtr.Zero);

        return rects;
    }

    private static class Win
    {
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _shot.Bitmap.Dispose();
    }
}
