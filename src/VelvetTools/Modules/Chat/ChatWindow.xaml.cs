using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;
using VelvetTools.Settings;

namespace VelvetTools.Modules.Chat;

public partial class ChatWindow : GlassWindow
{
    private readonly ChatStore _store = new();
    private ChatSession _session = null!;
    private CancellationTokenSource? _cts;
    private bool _loading;

    // 流式输出时被追加的可视元素
    private TextBlock? _streamingText;
    private TextBlock? _reasoningText;
    private Border? _reasoningHost;
    private Expander? _reasoningExpander;
    private Expander? _lastRenderedReasoning;

    public ChatWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;

        App.Services.Settings.Chat.EnsurePresets();
        ReloadProviders();

        if (_store.Sessions.Count > 0)
        {
            _session = _store.Sessions[0];
            RestoreSessionProfile();
            RenderSession();
        }
        else
        {
            NewSession();
        }
        ReloadSessionList();
        UpdateComposerState();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        _cts?.Cancel();
        _store.Save();
        Hide();
    }

    public void ShowChat()
    {
        App.Services.Settings.Chat.EnsurePresets();
        ReloadProviders();
        RestoreSessionProfile();
        if (WebSearchToggle.IsChecked != true)
            WebSearchToggle.IsChecked = App.Services.Settings.Chat.WebSearch.EnabledByDefault;
        if (KnowledgeToggle.IsChecked == true)
            ReloadKnowledgeBases();
        Show();
        Activate();
        InputBox.Focus();
    }

    /// <summary>仅供 --smoke 截图：展示空白工作台，不修改或保存用户会话。</summary>
    public void PrepareEmptyStateForSelfTest()
    {
        MessageList.Items.Clear();
        InputBox.Text = "";
        EmptyHint.Visibility = Visibility.Visible;
        SessionTitleText.Text = "新对话";
        SafetyHint.Visibility = ActualWidth >= 920
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateResponsiveLayout();
    }

    /// <summary>仅供 --smoke 截图：使用不落盘的示例内容核对消息层级与输入区。</summary>
    public void PrepareConversationStateForSelfTest()
    {
        MessageList.Items.Clear();
        EmptyHint.Visibility = Visibility.Collapsed;
        SessionTitleText.Text = "整理版本发布说明";

        AppendBubble(new ChatMessage
        {
            Role = ChatRole.User,
            Content = "请把这次工具箱更新整理成一份简洁的发布说明，重点写清界面、文件搜索和 AI 对话的变化。",
        });
        AppendBubble(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Reasoning =
                "先确认发布说明需要覆盖的范围，再把变化归并为用户最容易理解的三组，" +
                "最后补上安装与回归建议。",
            Content =
                "这次更新主要围绕三个部分展开：\n\n" +
                "- **AI 对话**：重新整理会话栏、消息阅读区与输入工具，让模型和附件选择更顺手。\n" +
                "- **文件搜索**：完善索引状态检查与降级路径，减少首次使用时无结果的情况。\n" +
                "- **界面细节**：统一窗口间距、字体和图标，并补充窄窗口适配。\n\n" +
                "发布前建议再完成一次安装、卸载与深色模式回归。",
        });

        InputBox.Text = "继续把它改成适合 GitHub Releases 的版本";
        InputBox.CaretIndex = InputBox.Text.Length;
        UpdateComposerState();
        UpdateResponsiveLayout();
        MessageScroll.ScrollToEnd();
    }

    /// <summary>仅供 --smoke 截图：展开最终一条消息的思考过程，核对折叠层级。</summary>
    public void PrepareReasoningStateForSelfTest()
    {
        PrepareConversationStateForSelfTest();
        InputBox.Text = "";
        _lastRenderedReasoning?.SetCurrentValue(Expander.IsExpandedProperty, true);
        UpdateComposerState();
    }

    /// <summary>仅供 --smoke 截图：展开模型选择菜单，核对弹层位置和选中态。</summary>
    public void PrepareModelPickerForSelfTest()
    {
        PrepareEmptyStateForSelfTest();
        ModelBox.IsDropDownOpen = true;
    }

    public void CloseModelPickerForSelfTest() => ModelBox.IsDropDownOpen = false;

    // ==================== 模型 ====================
    private void ReloadProviders()
    {
        // 服务商、Base URL、API Key 与模型清单只在设置页管理。
        // 对话页读取当前服务商，仅保留一次会话内的模型切换。
        ReloadModels();
    }

    private void ReloadModels()
    {
        _loading = true;
        var chat = App.Services.Settings.Chat;
        var provider = CurrentProvider();
        ModelBox.Items.Clear();
        if (provider is not null)
        {
            foreach (var m in provider.Models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                ModelBox.Items.Add(m);
            if (!string.IsNullOrWhiteSpace(provider.Model) && !ModelBox.Items.Contains(provider.Model))
                ModelBox.Items.Add(provider.Model);
            ModelBox.SelectedItem = provider.Model;
        }
        _loading = false;
        UpdateStatus();
    }

    private ChatProvider? CurrentProvider()
    {
        return App.Services.Settings.Chat.Active;
    }

    private static string DisplayModel(string? model)
    {
        string value = model?.Trim() ?? "";
        return value.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "自动选择" : value;
    }

    private void OnModelChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var provider = CurrentProvider();
        if (provider is null) return;
        string model = (ModelBox.SelectedItem as string ?? "").Trim();
        if (model.Length == 0 || model == provider.Model) return;
        provider.Model = model;
        if (_session is not null)
        {
            _session.ProviderId = provider.Id;
            _session.Model = model;
            _session.Updated = DateTime.Now;
            _store.Save();
        }
        App.Services.Settings.Save();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var provider = CurrentProvider();
        // 模型框空着时给个提示，不然看着像坏了
        string selectedModel = ModelBox.SelectedItem as string ?? "";
        ModelPlaceholder.Visibility = string.IsNullOrWhiteSpace(selectedModel)
            ? Visibility.Visible : Visibility.Collapsed;

        if (provider is null)
        {
            StatusText.Text = "● 未选择服务商";
            StatusText.ToolTip = null;
            StatusText.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            StatusText.Text = "● 未配置 API Key";
            StatusText.ToolTip = $"{provider.Name} 尚未配置 API Key，点击右侧齿轮前往设置";
            StatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
        else if (string.IsNullOrWhiteSpace(selectedModel))
        {
            StatusText.Text = $"● {provider.Name} · 待选模型";
            StatusText.ToolTip = "请到设置页获取或填写模型，再回到这里选择";
            StatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
        else
        {
            StatusText.Text = $"● {provider.Name} · 已就绪";
            StatusText.ToolTip = $"{provider.Name} · {selectedModel}";
            StatusText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        EmptySubHint.Text = string.IsNullOrWhiteSpace(provider.ApiKey)
            ? "先到 设置 → AI 对话 填写 API Key"
            : $"{provider.Name} · {DisplayModel(provider.Model)}";
    }

    // ==================== 会话 ====================
    private void NewSession()
    {
        var provider = CurrentProvider();
        _session = _store.NewSession(provider?.Id ?? "", provider?.Model ?? "");
        RenderSession();
        ReloadSessionList();
    }

    private void OnNewChatClick(object sender, RoutedEventArgs e)
    {
        // 当前会话没内容就不重复新建
        if (_session.Messages.Count == 0) { InputBox.Focus(); return; }
        NewSession();
        InputBox.Focus();
    }

    private void OnClearSessionsClick(object sender, RoutedEventArgs e)
    {
        if (_store.Sessions.Count == 0) return;
        if (MessageBox.Show("确定清空全部对话记录吗？此操作不可恢复。",
                "清空对话", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _cts?.Cancel();
        _store.Clear();
        NewSession();
        Toast.Show("对话记录已清空");
    }

    private void OnSessionSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        SessionSearchHint.Visibility = SessionSearchBox.Text.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
        ReloadSessionList();
    }

    private void ReloadSessionList()
    {
        _loading = true;
        SessionList.Items.Clear();

        string q = SessionSearchBox?.Text.Trim() ?? "";
        // 搜索同时匹配标题与消息内容
        var sessions = q.Length == 0
            ? _store.Sessions
            : _store.Sessions.Where(s =>
                s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Messages.Any(m => m.Content.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var s in sessions)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Text = s.Title,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            row.Children.Add(title);
            var timestamp = new TextBlock
            {
                Text = s.Updated.ToString(s.Updated.Date == DateTime.Today ? "HH:mm" : "MM-dd"),
                FontSize = 9.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            timestamp.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            Grid.SetColumn(timestamp, 1);
            row.Children.Add(timestamp);

            var item = new ListBoxItem { Content = row, Tag = s };
            var menu = new ContextMenu();
            var del = new MenuItem { Header = "删除对话" };
            del.Click += (_, _) =>
            {
                _store.Delete(s);
                if (_session == s)
                {
                    if (_store.Sessions.Count > 0)
                    {
                        _session = _store.Sessions[0];
                        RestoreSessionProfile();
                        RenderSession();
                    }
                    else NewSession();
                }
                ReloadSessionList();
            };
            var rename = new MenuItem { Header = "重命名" };
            rename.Click += (_, _) => RenameSession(s);
            menu.Items.Add(rename);
            menu.Items.Add(del);
            item.ContextMenu = menu;

            SessionList.Items.Add(item);
        }

        SessionList.SelectedItem = SessionList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, _session));
        SessionCountText.Text = q.Length == 0
            ? $"{_store.Sessions.Count} 个对话"
            : $"{SessionList.Items.Count} / {_store.Sessions.Count} 个对话";
        ClearSessionsBtn.IsEnabled = _store.Sessions.Count > 0;
        SessionTitleText.Text = _session?.Title ?? "新对话";
        _loading = false;
    }

    private void RenameSession(ChatSession s)
    {
        var dialog = new InputDialog("重命名对话", s.Title) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
        {
            s.Title = dialog.Value.Trim();
            _store.Save();
            ReloadSessionList();
        }
    }

    private void OnSessionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SessionList.SelectedItem is ListBoxItem { Tag: ChatSession s } && s != _session)
        {
            _cts?.Cancel();
            _session = s;
            RestoreSessionProfile();
            RenderSession();
        }
    }

    /// <summary>切换会话时恢复它上次使用的服务商/模型，而不是悄悄改用全局最后一次选择。</summary>
    private void RestoreSessionProfile()
    {
        if (_session is null || string.IsNullOrWhiteSpace(_session.ProviderId)) return;

        var chat = App.Services.Settings.Chat;
        int providerIndex = chat.Providers.FindIndex(p => p.Id == _session.ProviderId);
        if (providerIndex < 0) return;

        _loading = true;
        chat.ActiveProviderId = chat.Providers[providerIndex].Id;
        _loading = false;
        ReloadModels();

        var provider = chat.Providers[providerIndex];
        if (!string.IsNullOrWhiteSpace(_session.Model))
        {
            _loading = true;
            if (!ModelBox.Items.Contains(_session.Model))
                ModelBox.Items.Add(_session.Model);
            ModelBox.SelectedItem = _session.Model;
            provider.Model = _session.Model;
            _loading = false;
        }
        UpdateStatus();
    }

    private void RenderSession()
    {
        MessageList.Items.Clear();
        foreach (var m in _session.Messages)
            AppendBubble(m);
        EmptyHint.Visibility = _session.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionTitleText.Text = string.IsNullOrWhiteSpace(_session.Title) ? "新对话" : _session.Title;
        UpdateResponsiveLayout();
        ScrollToEnd();
    }

    // ==================== 输入区 ====================
    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateComposerState();
    }

    private void UpdateComposerState()
    {
        bool empty = InputBox.Text.Length == 0;
        InputPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        // 有附件时即使没打字也允许发（"看看这张图"这类场景）
        SendBtn.IsEnabled = !empty || _pendingImages.Count > 0 || _pendingDocs.Count > 0;
    }

    // ==================== 消息渲染 ====================
    /// <summary>
    /// 一条消息的外壳。助手使用文档式通栏正文，用户输入使用轻描边整行；
    /// 两者共享同一阅读列，长回答和代码块不会被窄气泡挤压。
    /// </summary>
    private FrameworkElement BuildMessageShell(bool isUser, out StackPanel content)
    {
        content = new StackPanel();

        if (isUser)
        {
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.Margin = new Thickness(0, 0, 0, 18);
            return content;
        }

        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.Margin = new Thickness(22, 0, 22, 18);
        return content;
    }

    /// <summary>鼠标移到消息上才浮出的操作条，平时不占视觉重量。</summary>
    private StackPanel BuildMessageActions(ChatMessage m, FrameworkElement shell)
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(m.IsUser ? 0 : -2, 5, 0, 0),
            HorizontalAlignment = m.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Opacity = 0,
        };

        Add(IconKind.Copy, "复制", () =>
        {
            try { System.Windows.Clipboard.SetText(m.Content); Toast.Show("已复制"); } catch { }
        });

        if (m.IsUser)
        {
            Add(IconKind.Edit, "重新编辑", () =>
            {
                InputBox.Text = m.Content;
                InputBox.CaretIndex = InputBox.Text.Length;
                InputBox.Focus();
            });
        }
        else
        {
            Add(IconKind.ArrowClockwise, "重新生成", () => _ = RegenerateAsync(m));
        }

        Add(IconKind.Delete, "删除这条", () =>
        {
            _session.Messages.Remove(m);
            _store.Save();
            RenderSession();
        });

        // 淡入淡出比直接切 Visibility 稳，不会让下方内容跳动
        shell.MouseEnter += (_, _) => bar.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
        shell.MouseLeave += (_, _) => bar.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        return bar;

        void Add(IconKind icon, string tip, Action action)
        {
            var btn = new Button
            {
                Content = AppIconFactory.Create(icon, 12),
                Style = (Style)FindResource("IconButton"),
                Width = 24,
                Height = 24,
                ToolTip = tip,
            };
            btn.Click += (_, _) => action();
            bar.Children.Add(btn);
        }
    }

    private void AppendBubble(ChatMessage m)
    {
        EmptyHint.Visibility = Visibility.Collapsed;

        var shell = BuildMessageShell(m.IsUser, out var container);

        // 思维链（可折叠）
        if (!string.IsNullOrWhiteSpace(m.Reasoning))
        {
            var disclosure = BuildReasoning(
                m.Reasoning,
                out _,
                out _,
                out var reasoningExpander);
            _lastRenderedReasoning = reasoningExpander;
            container.Children.Add(disclosure);
        }

        // 文档附件胶囊
        if (m.Attachments is { Count: > 0 })
        {
            var wrap = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = m.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            foreach (var name in m.Attachments)
            {
                var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
                chipContent.Children.Add(AppIconFactory.Create(
                    IconKind.Document,
                    12,
                    (Brush)FindResource("TextSecondaryBrush")));
                chipContent.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 11.5,
                    Margin = new Thickness(5, 0, 0, 0),
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                });
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 5, 4),
                    Child = chipContent,
                };
                chip.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
                wrap.Children.Add(chip);
            }
            container.Children.Add(wrap);
        }

        // 图片附件缩略图（在气泡正文之上）
        if (m.HasImages)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var path in m.Images!)
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 320;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();

                    var thumb = new Image
                    {
                        Source = bmp,
                        MaxWidth = 180,
                        MaxHeight = 180,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 6, 6),
                        Cursor = Cursors.Hand,
                        ToolTip = path,
                    };
                    string captured = path;
                    thumb.MouseLeftButtonUp += (_, _) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            { FileName = captured, UseShellExecute = true });
                        }
                        catch { }
                    };
                    wrap.Children.Add(thumb);
                }
                catch { }
            }
            if (wrap.Children.Count > 0) container.Children.Add(wrap);
        }

        FrameworkElement body;
        if (m.IsUser)
        {
            // 用户消息保持纯文本（可选中复制），不做 Markdown 解析
            var text = new TextBox
            {
                Text = m.Content,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                IsTabStop = false,
                Padding = new Thickness(0),
            };
            text.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            text.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, "AccentBrush");
            body = text;
        }
        else
        {
            // 助手回复走 Markdown 渲染：代码块、列表、标题、行内代码都能正常显示
            var stack = new StackPanel();
            foreach (var element in MarkdownRenderer.Render(m.Content, this))
                stack.Children.Add(element);
            body = stack;
        }

        // Hermes 式消息层级：用户输入是一条轻描边行，助手回复保持文档式通栏。
        if (m.IsUser)
        {
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 9, 14, 9),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(1),
                Child = body,
            };
            bubble.SetResourceReference(Border.BackgroundProperty, "ChatUserBrush");
            bubble.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            container.Children.Add(bubble);
        }
        else
        {
            container.Children.Add(body);
        }

        // 右键：消息级操作（悬浮操作条之外的低频项放这里）
        var menu = new ContextMenu();
        AddItem("复制这条消息", () =>
        {
            try { System.Windows.Clipboard.SetText(m.Content); Toast.Show("已复制"); } catch { }
        });

        if (!m.IsUser)
        {
            AddItem("重新生成", () => _ = RegenerateAsync(m));
        }
        else
        {
            AddItem("重新编辑（放回输入框）", () =>
            {
                InputBox.Text = m.Content;
                InputBox.CaretIndex = InputBox.Text.Length;
                InputBox.Focus();
            });
        }

        menu.Items.Add(new Separator());
        AddItem("删除这条消息", () =>
        {
            _session.Messages.Remove(m);
            _store.Save();
            RenderSession();
        });
        AddItem("删除此条及之后", () =>
        {
            int idx = _session.Messages.IndexOf(m);
            if (idx >= 0)
            {
                _session.Messages.RemoveRange(idx, _session.Messages.Count - idx);
                _store.Save();
                RenderSession();
            }
        });
        shell.ContextMenu = menu;

        void AddItem(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        // 知识库命中来源
        if (m.KnowledgeSources is { Count: > 0 })
        {
            var panel = new StackPanel { Margin = new Thickness(4, 5, 0, 0), MaxWidth = 660 };
            var head = new TextBlock
            {
                Text = $"知识库片段（{m.KnowledgeSources.Count}）",
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 3),
            };
            head.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            panel.Children.Add(head);

            foreach (var src in m.KnowledgeSources)
            {
                var line = new TextBlock { Text = "· " + src, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                panel.Children.Add(line);
            }
            container.Children.Add(panel);
        }
        // 联网检索来源（可点开原网页）
        if (m.WebSources is { Count: > 0 })
        {
            var panel = new StackPanel { Margin = new Thickness(4, 5, 0, 0), MaxWidth = 660 };
            panel.Children.Add(new TextBlock
            {
                Text = $"联网来源（{m.WebSources.Count}）",
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 3),
            });
            ((TextBlock)panel.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

            for (int i = 0; i < m.WebSources.Count; i++)
            {
                var parts = m.WebSources[i].Split('|', 2);
                string title = parts[0];
                string url = parts.Length > 1 ? parts[1] : "";

                var link = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                var hyper = new System.Windows.Documents.Hyperlink(
                    new System.Windows.Documents.Run($"[{i + 1}] {title}"))
                { ToolTip = url };
                hyper.Click += (_, _) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        { FileName = url, UseShellExecute = true });
                    }
                    catch { }
                };
                hyper.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentLightBrush");
                link.Inlines.Add(hyper);
                panel.Children.Add(link);
            }
            container.Children.Add(panel);
        }

        // token 用量（服务商返回 usage 时才显示）
        if (!m.IsUser && (m.PromptTokens is not null || m.CompletionTokens is not null))
        {
            var usage = new TextBlock
            {
                Text = $"输入 {m.PromptTokens ?? 0} · 输出 {m.CompletionTokens ?? 0} tokens",
                FontSize = 10.5,
                Margin = new Thickness(4, 4, 0, 0),
            };
            usage.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            container.Children.Add(usage);
        }

        container.Children.Add(BuildMessageActions(m, shell));
        MessageList.Items.Add(shell);
    }

    private Border BuildReasoning(
        string initial,
        out TextBlock body,
        out Border host,
        out Expander expander,
        bool live = false)
    {
        body = new TextBlock
        {
            Text = initial,
            FontSize = 12,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        expander = new Expander
        {
            Header = live ? "思考中…" : "已思考",
            Style = (Style)FindResource("ReasoningExpander"),
            IsExpanded = live,
            Content = body,
        };
        host = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(-5, 0, 0, 5),
            Child = expander,
        };
        return host;
    }

    private void ScrollToEnd() => Dispatcher.BeginInvoke(() => MessageScroll.ScrollToEnd(),
        System.Windows.Threading.DispatcherPriority.Background);

    // ==================== 图片附件 ====================
    private readonly List<string> _pendingImages = new();

    private readonly List<ParsedDocument> _pendingDocs = new();

    private void OnAttachClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var image = new MenuItem { Header = "添加图片" };
        image.Click += (_, _) => PickImages();
        menu.Items.Add(image);

        var file = new MenuItem { Header = "选择文档（PDF / Word / Excel / PPT / 文本）" };
        file.Click += (_, _) => PickDocuments();
        menu.Items.Add(file);

        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void PickImages()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|所有文件|*.*",
            Title = "选择要发送的图片",
        };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames) AddAttachment(path);
    }

    private async void PickDocuments()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = DocumentParser.FileDialogFilter,
            Title = "选择要解析的文档",
        };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames) await AddDocumentAsync(path);
    }

    private async Task AddDocumentAsync(string path)
    {
        if (_pendingDocs.Any(d => d.FileName == System.IO.Path.GetFileName(path))) return;
        if (_pendingDocs.Count >= 5) { Toast.Show("一次最多带 5 个文档"); return; }

        try
        {
            StatusText.Text = "正在解析文档…";
            var doc = await DocumentParser.ParseAsync(path);
            _pendingDocs.Add(doc);
            RefreshAttachments();
            StatusText.Text = "";
            Toast.Show($"已解析 {doc.FileName}（{doc.Kind}，发送 {doc.IndexedCharCount:N0} 字" +
                       (doc.IsTruncated ? "，已截断）" : "）"));
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            Logger.Error("解析文档失败", ex);
            Toast.Show("解析失败：" + ex.Message, 4000);
        }
    }

    /// <summary>联网搜索开关（每个窗口会话内有效，默认值来自设置）。</summary>
    private void OnWebSearchToggled(object sender, RoutedEventArgs e) => UpdateInputHint();

    // ==================== 知识库 ====================
    private void OnKnowledgeToggled(object sender, RoutedEventArgs e)
    {
        bool on = KnowledgeToggle.IsChecked == true;
        KnowledgeBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        if (on)
        {
            ReloadKnowledgeBases();
            if (KnowledgeBox.Items.Count == 0)
            {
                KnowledgeToggle.IsChecked = false;
                KnowledgeBox.Visibility = Visibility.Collapsed;
                Toast.Show("还没有知识库，先去创建一个", 3500);
                App.Services.ShowKnowledgeWindow();
                return;
            }
        }
        UpdateInputHint();
    }

    private void ReloadKnowledgeBases()
    {
        var bases = App.Services.Knowledge.Store.Bases.Where(b => b.Chunks.Count > 0).ToList();
        string activeId = App.Services.Settings.Chat.ActiveKnowledgeBaseId;

        _loading = true;
        KnowledgeBox.Items.Clear();
        foreach (var kb in bases)
            KnowledgeBox.Items.Add(new ComboBoxItem { Content = kb.Name, Tag = kb });

        int idx = bases.FindIndex(b => b.Id == activeId);
        KnowledgeBox.SelectedIndex = idx >= 0 ? idx : (bases.Count > 0 ? 0 : -1);
        _loading = false;
    }

    private void OnKnowledgeBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if ((KnowledgeBox.SelectedItem as ComboBoxItem)?.Tag is Knowledge.KnowledgeBase kb)
        {
            App.Services.Settings.Chat.ActiveKnowledgeBaseId = kb.Id;
            App.Services.Settings.Save();
        }
    }

    private void UpdateInputHint()
    {
        bool web = WebSearchToggle.IsChecked == true;
        bool kb = KnowledgeToggle.IsChecked == true;

        InputHint.Text = (web, kb) switch
        {
            (true, true) => "已开启联网 + 知识库",
            (true, false) => "已开启联网搜索",
            (false, true) => "已开启知识库检索",
            _ => "Enter 发送 · Shift+Enter 换行 · 可粘贴图片",
        };
    }

    private void AddAttachment(string path)
    {
        if (_pendingImages.Contains(path)) return;
        if (_pendingImages.Count >= 6) { Toast.Show("一次最多带 6 张图片"); return; }
        _pendingImages.Add(path);
        RefreshAttachments();
    }

    /// <summary>把剪贴板里的位图落成临时文件再作为附件（Ctrl+V 粘贴截图）。</summary>
    private void AddClipboardImage()
    {
        try
        {
            var img = System.Windows.Clipboard.GetImage();
            if (img is null) return;

            string dir = System.IO.Path.Combine(Logger.DataDir, "chat-images");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"paste_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

            using (var fs = System.IO.File.Create(path))
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                encoder.Save(fs);
            }
            AddAttachment(path);
        }
        catch (Exception ex)
        {
            Logger.Error("粘贴图片失败", ex);
        }
    }

    private void RefreshAttachments()
    {
        UpdateComposerState();
        AttachmentList.Items.Clear();

        // 文档以文件名胶囊展示
        foreach (var doc in _pendingDocs.ToList())
        {
            var label = new TextBlock
            {
                Text = $"{doc.FileName}  ·  {doc.IndexedCharCount:N0} 字" +
                       (doc.IsTruncated ? "（已截断）" : ""),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var remove = new Button
            {
                Content = AppIconFactory.Create(IconKind.Dismiss, 10),
                Width = 18, Height = 18,
                FontSize = 9,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("IconButton"),
                ToolTip = "移除",
            };
            var captured = doc;
            remove.Click += (_, _) => { _pendingDocs.Remove(captured); RefreshAttachments(); };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = AppIconFactory.Create(IconKind.Document, 12, (Brush)FindResource("AccentLightBrush"));
            icon.Margin = new Thickness(0, 0, 6, 0);
            panel.Children.Add(icon);
            panel.Children.Add(label);
            panel.Children.Add(remove);

            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 4, 5, 4),
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(1),
                Child = panel,
                ToolTip = "解析后的文本会随本轮提问一起发送",
            };
            chip.SetResourceReference(Border.BackgroundProperty, "ControlBrush");
            chip.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            AttachmentList.Items.Add(chip);
        }

        foreach (var path in _pendingImages.ToList())
        {
            var thumb = new Image { Width = 54, Height = 54, Stretch = Stretch.UniformToFill };
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 108;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                thumb.Source = bmp;
            }
            catch { }

            var remove = new Button
            {
                Content = AppIconFactory.Create(IconKind.Dismiss, 10),
                Width = 18, Height = 18,
                FontSize = 9,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                Style = (Style)FindResource("IconButton"),
                ToolTip = "移除",
            };
            string captured = path;
            remove.Click += (_, _) => { _pendingImages.Remove(captured); RefreshAttachments(); };

            var host = new Border
            {
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 6, 0),
                ClipToBounds = true,
                BorderThickness = new Thickness(1),
                Child = new Grid { Children = { thumb, remove } },
                ToolTip = path,
            };
            host.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            AttachmentList.Items.Add(host);
        }
        AttachmentList.Visibility = (_pendingImages.Count + _pendingDocs.Count) > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== 发送 ====================
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V 且剪贴板是图片：作为附件而不是粘贴成文本
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control
            && System.Windows.Clipboard.ContainsImage())
        {
            e.Handled = true;
            AddClipboardImage();
            return;
        }

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            _ = SendAsync();
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => _ = SendAsync();

    /// <summary>重新生成：丢弃这条助手回复及其之后的内容，用同样的上文重发。</summary>
    private async Task RegenerateAsync(ChatMessage assistantMsg)
    {
        if (_cts is not null) return;

        int idx = _session.Messages.IndexOf(assistantMsg);
        if (idx < 0) return;

        _session.Messages.RemoveRange(idx, _session.Messages.Count - idx);
        _store.Save();
        RenderSession();

        // 上一条用户消息即为本次请求的最后输入
        if (_session.Messages.LastOrDefault()?.Role != ChatRole.User)
        {
            Toast.Show("没有可重新生成的提问");
            return;
        }
        await GenerateAsync();
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task SendAsync()
    {
        if (_cts is not null) return; // 正在生成
        string input = InputBox.Text.Trim();
        if (input.Length == 0 && _pendingImages.Count == 0 && _pendingDocs.Count == 0) return;

        var provider = CurrentProvider();
        if (provider is null) return;
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            Toast.Show($"请先在 设置 → AI 对话 中填写「{provider.Name}」的 API Key", 4000);
            App.Services.ShowSettingsWindow();
            return;
        }

        InputBox.Text = "";

        // 文档正文拼进这一轮的提问里（气泡只显示用户原话，避免几万字刷屏）
        string contextPrefix = "";
        if (_pendingDocs.Count > 0)
        {
            contextPrefix = string.Join("\n\n", _pendingDocs.Select(d => d.ToContextBlock())) + "\n\n";
        }

        var userMsg = new ChatMessage
        {
            Role = ChatRole.User,
            Content = input,
            Images = _pendingImages.Count > 0 ? _pendingImages.ToList() : null,
            Attachments = _pendingDocs.Count > 0 ? _pendingDocs.Select(d => d.FileName).ToList() : null,
            HiddenContext = contextPrefix.Length > 0 ? contextPrefix : null,
        };
        _pendingImages.Clear();
        _pendingDocs.Clear();
        RefreshAttachments();
        _session.Messages.Add(userMsg);
        _session.ProviderId = provider.Id;
        _session.Model = provider.Model;
        _session.Updated = DateTime.Now;
        _session.AutoTitle();
        AppendBubble(userMsg);
        ReloadSessionList();
        ScrollToEnd();

        // 知识库检索：把相关片段作为隐藏上下文并入这条提问
        if (KnowledgeToggle.IsChecked == true && input.Length > 0
            && (KnowledgeBox.SelectedItem as ComboBoxItem)?.Tag is Knowledge.KnowledgeBase kb)
        {
            StatusText.Text = "正在检索知识库…";
            try
            {
                var chat = App.Services.Settings.Chat;
                if (!App.Services.Knowledge.Store.Bases.Any(b => b.Id == kb.Id))
                    throw new InvalidOperationException("所选知识库已经被删除，请重新选择");

                var embedProvider = chat.Providers.FirstOrDefault(p => p.Id == kb.EmbedProviderId)
                    ?? throw new InvalidOperationException(
                        $"知识库「{kb.Name}」绑定的嵌入服务商配置已被删除，请到知识库管理中重建索引");

                var hits = await App.Services.Knowledge.SearchAsync(
                    kb, input, embedProvider, chat.KnowledgeTopK, chat.KnowledgeMinScore);

                if (hits.Count > 0)
                {
                    userMsg.HiddenContext = (userMsg.HiddenContext ?? "")
                        + Knowledge.KnowledgeService.BuildContext(input, hits) + "\n\n";
                    userMsg.KnowledgeSources = hits
                        .Select((h, i) => $"【K{i + 1}】{h.Chunk.DocumentName} 第 {h.Chunk.Index + 1} 段 · {h.Score:0.00}")
                        .ToList();
                    RenderSession();
                    StatusText.Text = $"知识库命中 {hits.Count} 个片段";
                }
                else
                {
                    StatusText.Text = "知识库里没有相关内容";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("知识库检索失败", ex);
                StatusText.Text = "知识库检索失败";
                Toast.Show("知识库检索失败：" + ex.Message, 4000);
            }
        }

        // 联网搜索：先检索，把结果作为隐藏上下文并入这条提问
        if (WebSearchToggle.IsChecked == true && input.Length > 0)
        {
            StatusText.Text = "正在联网检索…";
            try
            {
                var results = await App.Services.WebSearch.SearchAsync(
                    input, App.Services.Settings.Chat.WebSearch);

                if (results.Count > 0)
                {
                    userMsg.HiddenContext = (userMsg.HiddenContext ?? "")
                        + WebSearchService.BuildContext(input, results) + "\n\n";
                    userMsg.WebSources = results.Select(r => $"{r.Title}|{r.Url}").ToList();
                    RenderSession();   // 重画气泡以显示来源
                    StatusText.Text = $"已检索 {results.Count} 条结果";
                }
                else
                {
                    StatusText.Text = "未检索到结果，按原问题作答";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("联网搜索失败", ex);
                StatusText.Text = "联网检索失败，按原问题作答";
                Toast.Show("联网搜索失败：" + ex.Message, 4000);
            }
        }

        await GenerateAsync();
    }

    /// <summary>基于当前会话内容请求一次回复（发送与重新生成共用）。</summary>
    private async Task GenerateAsync()
    {
        if (_cts is not null) return;

        var provider = CurrentProvider();
        if (provider is null) return;

        // 助手气泡（流式追加）
        var placeholder = new ChatMessage { Role = ChatRole.Assistant, Content = "" };
        AppendStreamingBubble();

        SendBtn.IsEnabled = false;
        SendBtn.Visibility = Visibility.Collapsed;
        StopBtn.Visibility = Visibility.Visible;
        _cts = new CancellationTokenSource();

        var chat = App.Services.Settings.Chat;
        var eligibleHistory = _session.Messages.Where(m => m.Role != ChatRole.System);
        var history = chat.ContextMessages <= 0
            ? eligibleHistory.ToList()
            : eligibleHistory.TakeLast(Math.Max(2, chat.ContextMessages)).ToList();

        try
        {
            var result = await App.Services.Chat.SendAsync(
                provider, history, chat.SystemPrompt, chat.Temperature, chat.Stream,
                delta => Dispatcher.BeginInvoke(() => OnDelta(delta)),
                _cts.Token);

            placeholder.Content = result.Content;
            placeholder.Reasoning = result.Reasoning;
            _session.Messages.Add(placeholder);
            _session.Updated = DateTime.Now;
            _store.Save();

            // 流式过程中是纯文本追加（渲染成本低），收尾时整段重排成 Markdown，
            // 代码块/列表/标题才会正确显示
            RenderSession();
        }
        catch (OperationCanceledException)
        {
            // 保留已生成的部分
            placeholder.Content = _streamingText?.Text ?? "";
            if (placeholder.Content.Length > 0)
            {
                _session.Messages.Add(placeholder);
                _store.Save();
            }
            else if (MessageList.Items.Count > 0)
            {
                MessageList.Items.RemoveAt(MessageList.Items.Count - 1);
            }
            StatusText.Text = "已停止生成";
        }
        catch (Exception ex)
        {
            Logger.Error("AI 对话失败", ex);
            if (_streamingText is not null)
            {
                _streamingText.Text = "⚠ " + ex.Message;
                _streamingText.Foreground = (Brush)FindResource("DangerBrush");
            }
        }
        finally
        {
            if (_reasoningExpander is not null)
            {
                _reasoningExpander.Header = "已思考";
                _reasoningExpander.IsExpanded = false;
            }
            _cts?.Dispose();
            _cts = null;
            _streamingText = null;
            _reasoningText = null;
            _reasoningHost = null;
            _reasoningExpander = null;
            SendBtn.IsEnabled = true;
            SendBtn.Visibility = Visibility.Visible;
            StopBtn.Visibility = Visibility.Collapsed;
            ReloadSessionList();
            ScrollToEnd();
        }
    }

    private void AppendStreamingBubble()
    {
        var shell = BuildMessageShell(isUser: false, out var container);

        _reasoningHost = BuildReasoning(
            "",
            out var reasoningBody,
            out _,
            out var reasoningExpander,
            live: true);
        _reasoningHost.Visibility = Visibility.Collapsed;
        _reasoningText = reasoningBody;
        _reasoningExpander = reasoningExpander;
        container.Children.Add(_reasoningHost);

        _streamingText = new TextBlock
        {
            Text = "…",
            FontSize = 14,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
        };
        _streamingText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        container.Children.Add(_streamingText);

        MessageList.Items.Add(shell);
        EmptyHint.Visibility = Visibility.Collapsed;
        ScrollToEnd();
    }

    private void OnDelta(ChatService.StreamDelta delta)
    {
        if (_streamingText is null) return;

        if (!string.IsNullOrEmpty(delta.Reasoning) && _reasoningText is not null && _reasoningHost is not null)
        {
            _reasoningHost.Visibility = Visibility.Visible;
            _reasoningText.Text += delta.Reasoning;
        }

        if (!string.IsNullOrEmpty(delta.Content))
        {
            if (_streamingText.Text == "…") _streamingText.Text = "";
            _streamingText.Text += delta.Content;
        }

        // 用户没有手动上滚时才自动跟随
        if (MessageScroll.VerticalOffset >= MessageScroll.ScrollableHeight - 120)
            MessageScroll.ScrollToEnd();
    }

    // ==================== 杂项 ====================
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        if (StatusPill is null || Composer is null) return;

        // 先保住会话、阅读列和输入框；模型控件在窄窗口隐藏后仍可从设置中修改。
        SidebarColumn.Width = new GridLength(
            ActualWidth >= 1500 ? 316 :
            ActualWidth < 880 ? 194 : 228);
        ModelSelectorHost.Visibility = Visibility.Visible;
        InputHint.Visibility = ActualWidth >= 980 ? Visibility.Visible : Visibility.Collapsed;
        SafetyHint.Visibility = ActualWidth >= 920 ? Visibility.Visible : Visibility.Collapsed;

        double horizontal = ActualWidth < 900 ? 18 : 28;
        Composer.Margin = new Thickness(horizontal, 6, horizontal, 8);
        double messageTop = MessageList.Items.Count == 0
            ? 18
            : ActualWidth >= 1500 ? 88 : 36;
        MessageScroll.Padding = ActualWidth < 900
            ? new Thickness(24, messageTop, 24, 10)
            : new Thickness(48, messageTop, 48, 10);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeIcon is null || ChromeRoot is null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Icon = maximized ? IconKind.FullScreenMinimize : IconKind.Square;
        MaximizeButton.ToolTip = maximized ? "还原" : "最大化";
        ChromeRoot.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(14);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2)
        {
            OnMaximizeClick(sender, e);
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => App.Services.ShowSettingsWindow();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    // ==================== 导出 ====================
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_session.Messages.Count == 0)
        {
            Toast.Show("当前对话还没有内容");
            return;
        }

        var menu = new ContextMenu();
        AddItem("导出为 Markdown", () => Export("md"));
        AddItem("导出为纯文本", () => Export("txt"));
        AddItem("导出为 JSON", () => Export("json"));
        menu.Items.Add(new Separator());
        AddItem("复制全部到剪贴板", () =>
        {
            try
            {
                System.Windows.Clipboard.SetText(BuildMarkdown());
                Toast.Show("整段对话已复制");
            }
            catch { }
        });
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;

        void AddItem(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
    }

    private void Export(string format)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SanitizeFileName(_session.Title),
            DefaultExt = "." + format,
            Filter = format switch
            {
                "md" => "Markdown 文件|*.md",
                "json" => "JSON 文件|*.json",
                _ => "文本文件|*.txt",
            },
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            string content = format switch
            {
                "json" => System.Text.Json.JsonSerializer.Serialize(_session,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                    }),
                "txt" => BuildPlainText(),
                _ => BuildMarkdown(),
            };
            System.IO.File.WriteAllText(dialog.FileName, content, System.Text.Encoding.UTF8);
            Toast.Show("已导出：" + System.IO.Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            Logger.Error("导出对话失败", ex);
            Toast.Show("导出失败：" + ex.Message);
        }
    }

    private string BuildMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {_session.Title}").AppendLine();
        sb.AppendLine($"> 模型：{_session.Model}　·　导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}").AppendLine();
        foreach (var m in _session.Messages)
        {
            sb.AppendLine(m.IsUser ? "## 我" : $"## {CurrentProvider()?.Name ?? "助手"}").AppendLine();
            sb.AppendLine(m.Content).AppendLine();
        }
        return sb.ToString();
    }

    private string BuildPlainText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in _session.Messages)
            sb.AppendLine(m.IsUser ? "我：" : "助手：").AppendLine(m.Content).AppendLine();
        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 60 ? name[..60] : name;
    }
}

/// <summary>极简单行输入对话框（重命名会话用）。</summary>
public sealed class InputDialog : GlassWindow
{
    private readonly TextBox _box;
    public string Value => _box.Text;

    public InputDialog(string title, string initial)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        EscapeAction = EscAction.Close;
        DragMoveEnabled = true;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("TitleText"),
            Margin = new Thickness(0, 0, 0, 12),
        });

        _box = new TextBox { Text = initial, Style = (Style)FindResource("GlassTextBox") };
        stack.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button { Content = "确定", Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "取消", Style = (Style)FindResource("GlassButton") };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = stack;
        Loaded += (_, _) => { _box.SelectAll(); _box.Focus(); };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; Close(); }
        };
    }
}
