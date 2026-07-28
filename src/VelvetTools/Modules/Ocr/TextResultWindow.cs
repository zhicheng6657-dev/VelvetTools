using System.Windows;
using System.Windows.Controls;
using VelvetTools.Common;
using VelvetTools.Modules.Translate;

namespace VelvetTools.Modules.Ocr;

/// <summary>OCR 结果窗口：可编辑文本 + 复制/翻译。</summary>
public sealed class TextResultWindow : GlassWindow
{
    private readonly TextBox _textBox;

    public static void ShowText(string title, string text)
    {
        var w = new TextResultWindow(title, text);
        w.Show();
        w.Activate();
    }

    private TextResultWindow(string title, string text)
    {
        Title = title;
        Width = 520;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = true;
        Topmost = true;
        EscapeAction = EscAction.Close;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("TitleText"),
            Margin = new Thickness(2, 0, 0, 10),
        };
        header.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        root.Children.Add(header);

        _textBox = new TextBox
        {
            Text = text,
            Style = (Style)FindResource("GlassTextBox"),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        Grid.SetRow(_textBox, 1);
        root.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var copyBtn = new Button { Content = "复制全部", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
        copyBtn.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(_textBox.Text); Toast.Show("已复制识别文本"); } catch { }
        };
        buttons.Children.Add(copyBtn);

        var translateBtn = new Button { Content = "翻译", Style = (Style)FindResource("GlassButton"), Margin = new Thickness(0, 0, 8, 0) };
        translateBtn.Click += (_, _) => TranslateWindow.Open(_textBox.Text, autoTranslate: true);
        buttons.Children.Add(translateBtn);

        var closeBtn = new Button { Content = "关闭", Style = (Style)FindResource("GlassButton") };
        closeBtn.Click += (_, _) => Close();
        buttons.Children.Add(closeBtn);

        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }
}
