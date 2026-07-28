using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using VelvetTools.Common;

namespace VelvetTools.Modules.Chat;

/// <summary>
/// 轻量 Markdown 渲染器（自研，无第三方依赖）。
/// 覆盖 AI 回复里真正高频的语法：围栏代码块、行内代码、标题、无序/有序列表、
/// 引用、粗体/斜体、链接、分隔线、表格。刻意不做完整 CommonMark ——
/// 那需要引入解析库，而聊天场景用不上嵌套块引用之类的边角语法。
///
/// 代码块单独渲染成带语言标签与"复制"按钮的卡片（等宽字体、可横向滚动），
/// 这是聊天客户端观感差异最大的一块。
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>把 Markdown 文本渲染成一列 WPF 元素。</summary>
    public static IEnumerable<UIElement> Render(string markdown, FrameworkElement resourceScope)
    {
        var blocks = new List<UIElement>();
        if (string.IsNullOrEmpty(markdown)) return blocks;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var buffer = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // ---- 围栏代码块 ----
            var fence = Regex.Match(line, @"^\s*```+\s*(\S*)\s*$");
            if (fence.Success)
            {
                FlushText(buffer, blocks, resourceScope);

                string lang = fence.Groups[1].Value;
                var code = new List<string>();
                i++;
                while (i < lines.Length && !Regex.IsMatch(lines[i], @"^\s*```+\s*$"))
                {
                    code.Add(lines[i]);
                    i++;
                }
                blocks.Add(BuildCodeCard(string.Join("\n", code), lang, resourceScope));
                continue;
            }

            // ---- 分隔线 ----
            if (Regex.IsMatch(line, @"^\s*([-*_])\s*(\1\s*){2,}$"))
            {
                FlushText(buffer, blocks, resourceScope);
                var rule = new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 8, 0, 8),
                };
                rule.SetResourceReference(Border.BackgroundProperty, "HairlineBrush");
                blocks.Add(rule);
                continue;
            }

            // ---- 标题 ----
            var heading = Regex.Match(line, @"^(#{1,4})\s+(.+)$");
            if (heading.Success)
            {
                FlushText(buffer, blocks, resourceScope);
                int level = heading.Groups[1].Value.Length;
                var tb = new TextBlock
                {
                    FontSize = level switch { 1 => 18, 2 => 16, 3 => 14.5, _ => 13.5 },
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, level <= 2 ? 10 : 8, 0, 4),
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                AppendInline(tb, heading.Groups[2].Value, resourceScope);
                blocks.Add(tb);
                continue;
            }

            buffer.Add(line);
        }

        FlushText(buffer, blocks, resourceScope);
        return blocks;
    }

    // ==================== 文本段 ====================
    private static void FlushText(List<string> buffer, List<UIElement> blocks, FrameworkElement scope)
    {
        if (buffer.Count == 0) return;
        var lines = buffer.ToList();
        buffer.Clear();

        // 去掉首尾空行
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return;

        var para = new List<string>();
        foreach (var line in lines)
        {
            var bullet = Regex.Match(line, @"^\s*([-*+]|\d+[.)])\s+(.*)$");
            var quote = Regex.Match(line, @"^\s*>\s?(.*)$");

            if (bullet.Success)
            {
                FlushParagraph(para, blocks, scope);
                blocks.Add(BuildListItem(bullet.Groups[1].Value, bullet.Groups[2].Value, scope));
            }
            else if (quote.Success)
            {
                FlushParagraph(para, blocks, scope);
                blocks.Add(BuildQuote(quote.Groups[1].Value, scope));
            }
            else if (line.Trim().Length == 0)
            {
                FlushParagraph(para, blocks, scope);
            }
            else
            {
                para.Add(line);
            }
        }
        FlushParagraph(para, blocks, scope);
    }

    private static void FlushParagraph(List<string> para, List<UIElement> blocks, FrameworkElement scope)
    {
        if (para.Count == 0) return;
        string text = string.Join("\n", para);
        para.Clear();

        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 6),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        AppendInline(tb, text, scope);
        blocks.Add(tb);
    }

    private static UIElement BuildListItem(string marker, string content, FrameworkElement scope)
    {
        var grid = new Grid { Margin = new Thickness(6, 1, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool ordered = char.IsDigit(marker[0]);
        var dot = new TextBlock
        {
            Text = ordered ? marker : "•",
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Top,
        };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        grid.Children.Add(dot);

        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21 };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        AppendInline(body, content, scope);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private static UIElement BuildQuote(string content, FrameworkElement scope)
    {
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        AppendInline(body, content, scope);

        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 6) };
        panel.Children.Add(bar);
        panel.Children.Add(body);
        return panel;
    }

    // ==================== 代码卡片 ====================
    private static UIElement BuildCodeCard(string code, string language, FrameworkElement scope)
    {
        var header = new Grid { Margin = new Thickness(10, 5, 6, 3) };
        var langText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "代码" : language,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        langText.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        header.Children.Add(langText);

        var copyBtn = new Button
        {
            Content = "复制",
            FontSize = 11,
            Padding = new Thickness(9, 2, 9, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
        };
        copyBtn.SetResourceReference(FrameworkElement.StyleProperty, "GlassButton");
        copyBtn.Click += (_, _) =>
        {
            try
            {
                System.Windows.Clipboard.SetText(code);
                copyBtn.Content = "已复制";
                Toast.Show("代码已复制");
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, _) => { timer.Stop(); copyBtn.Content = "复制"; };
                timer.Start();
            }
            catch { }
        };
        header.Children.Add(copyBtn);

        var codeText = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, monospace"),
            FontSize = 12.5,
            Padding = new Thickness(10, 4, 10, 8),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsTabStop = false,
            MaxHeight = 420,
        };
        codeText.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        codeText.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentBrush");

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(codeText);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 8),
            Child = stack,
        };
        card.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    // ==================== 行内语法 ====================
    private static readonly Regex InlinePattern = new(
        @"(?<code>`[^`\n]+`)" +
        @"|(?<bold>\*\*[^*\n]+\*\*|__[^_\n]+__)" +
        @"|(?<italic>\*[^*\n]+\*|_[^_\n]+_)" +
        @"|(?<link>\[[^\]\n]+\]\([^)\s]+\))",
        RegexOptions.Compiled);

    private static void AppendInline(TextBlock target, string text, FrameworkElement scope)
    {
        int pos = 0;
        foreach (Match m in InlinePattern.Matches(text))
        {
            if (m.Index > pos)
                target.Inlines.Add(new Run(text[pos..m.Index]));

            if (m.Groups["code"].Success)
            {
                string inner = m.Value.Trim('`');
                var run = new Run(inner)
                {
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    FontSize = 12.5,
                };
                var span = new Span(run);
                span.SetResourceReference(TextElement.BackgroundProperty, "ControlBrush");
                span.SetResourceReference(TextElement.ForegroundProperty, "AccentLightBrush");
                target.Inlines.Add(span);
            }
            else if (m.Groups["bold"].Success)
            {
                target.Inlines.Add(new Bold(new Run(m.Value.Trim('*', '_'))));
            }
            else if (m.Groups["italic"].Success)
            {
                target.Inlines.Add(new Italic(new Run(m.Value.Trim('*', '_'))));
            }
            else if (m.Groups["link"].Success)
            {
                var linkMatch = Regex.Match(m.Value, @"\[([^\]]+)\]\(([^)]+)\)");
                string label = linkMatch.Groups[1].Value;
                string url = linkMatch.Groups[2].Value;
                var hyperlink = new Hyperlink(new Run(label)) { ToolTip = url };
                hyperlink.RequestNavigate += (_, _) => OpenUrl(url);
                hyperlink.Click += (_, _) => OpenUrl(url);
                hyperlink.SetResourceReference(TextElement.ForegroundProperty, "AccentLightBrush");
                target.Inlines.Add(hyperlink);
            }

            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
            target.Inlines.Add(new Run(text[pos..]));
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Error("打开链接失败", ex);
        }
    }

    /// <summary>把 Markdown 转成纯文本（导出/复制时用）。</summary>
    public static string ToPlainText(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (Regex.IsMatch(line, @"^\s*```")) continue;
            sb.AppendLine(Regex.Replace(line, @"[*_`#>]", ""));
        }
        return sb.ToString().Trim();
    }
}
