using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IconKind = FluentIcons.Common.Icon;
using VelvetTools.Common;
using VelvetTools.Modules.Chat;
using VelvetTools.Settings;

namespace VelvetTools.Modules.Knowledge;

public partial class KnowledgeWindow : GlassWindow
{
    private KnowledgeBase? _current;
    private bool _loading;
    private CancellationTokenSource? _cts;

    public KnowledgeWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;
        HideInsteadOfClose = true;

        ReloadBases();
    }

    public void ShowKnowledge()
    {
        ReloadBases();
        Show();
        Activate();
    }

    private KnowledgeService Service => App.Services.Knowledge;

    // ==================== 知识库列表 ====================
    private void ReloadBases()
    {
        _loading = true;
        BaseList.Items.Clear();

        foreach (var kb in Service.Store.Bases)
        {
            var stack = new StackPanel();
            var title = new TextBlock { Text = kb.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            stack.Children.Add(title);

            var sub = new TextBlock
            {
                Text = $"{kb.Documents.Count} 个文档 · {kb.Chunks.Count} 块",
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
            };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            stack.Children.Add(sub);

            var item = new ListBoxItem { Content = stack, Tag = kb };
            var menu = new ContextMenu();
            AddMenu(menu, "重命名", () => RenameBase(kb));
            AddMenu(menu, "清空内容", () => ClearBase(kb));
            menu.Items.Add(new Separator());
            AddMenu(menu, "删除知识库", () => DeleteBase(kb));
            item.ContextMenu = menu;

            BaseList.Items.Add(item);
        }

        if (BaseList.Items.Count > 0)
        {
            int idx = _current is null ? 0 : Math.Max(0, Service.Store.Bases.IndexOf(_current));
            BaseList.SelectedIndex = Math.Min(idx, BaseList.Items.Count - 1);
            _current = (BaseList.SelectedItem as ListBoxItem)?.Tag as KnowledgeBase;
        }
        else
        {
            _current = null;
        }

        _loading = false;
        RenderCurrent();

        static void AddMenu(ContextMenu menu, string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
    }

    private void OnBaseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _current = (BaseList.SelectedItem as ListBoxItem)?.Tag as KnowledgeBase;
        RenderCurrent();
    }

    private void OnNewBaseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("新建知识库", "我的知识库") { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

        try
        {
            _current = Service.Store.Create(dialog.Value.Trim());
            ReloadBases();
        }
        catch (Exception ex)
        {
            Logger.Error("新建知识库失败", ex);
            Toast.Show("新建失败：" + ex.Message, 4000);
        }
    }

    private void RenameBase(KnowledgeBase kb)
    {
        var dialog = new InputDialog("重命名知识库", kb.Name) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        string oldName = kb.Name;
        kb.Name = dialog.Value.Trim();
        if (!Service.Store.Save())
        {
            kb.Name = oldName;
            Toast.Show("重命名失败：无法写入本地知识库", 4000);
            return;
        }
        ReloadBases();
    }

    private void ClearBase(KnowledgeBase kb)
    {
        if (MessageBox.Show($"确定清空「{kb.Name}」的全部文档与向量吗？\n\n" +
                            "如果只是更换嵌入模型，请取消并使用「重建索引」，无需删除文档。",
                "清空知识库", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (!Service.Store.Clear(kb))
        {
            Toast.Show("清空失败：原数据仍然保留", 4000);
            return;
        }
        ReloadBases();
        Toast.Show("已清空知识库");
    }

    private void DeleteBase(KnowledgeBase kb)
    {
        if (MessageBox.Show($"确定删除知识库「{kb.Name}」吗？此操作不可恢复。",
                "删除知识库", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        if (!Service.Store.Delete(kb))
        {
            Toast.Show("删除失败：知识库仍然保留", 4000);
            return;
        }
        if (_current == kb) _current = null;
        ReloadBases();
    }

    // ==================== 当前库内容 ====================
    private void RenderCurrent()
    {
        bool has = _current is not null;
        bool busy = _cts is not null;
        AddDocBtn.IsEnabled = has && !busy;
        TestBtn.IsEnabled = has && _current!.Chunks.Count > 0 && !busy;
        RebuildBtn.IsEnabled = has && _current!.Chunks.Count > 0 && !busy;
        EmbedProviderBox.IsEnabled = has && !busy;
        EmbedModelBox.IsEnabled = has && !busy;

        if (!has)
        {
            BaseTitle.Text = "知识库";
            BaseSubtitle.Text = "左侧新建一个知识库开始使用";
            DocList.Items.Clear();
            EmptyTitle.Text = "还没有知识库";
            EmptyDesc.Text = "点左上角「新建知识库」，把常用文档灌进去；" +
                             "之后在 AI 对话里开启「知识库」，提问会先检索相关片段再作答。";
            EmptyHint.Visibility = Visibility.Visible;
            _loading = true;
            EmbedProviderBox.Items.Clear();
            EmbedModelBox.Items.Clear();
            EmbedModelBox.Text = "";
            _loading = false;
            return;
        }

        EmptyTitle.Text = "还没有文档";
        EmptyDesc.Text = "添加 PDF / Word / Excel / 文本 等文档后，对话时开启「知识库」即可基于它们作答";

        var kb = _current!;
        var boundProvider = App.Services.Settings.Chat.Providers
            .FirstOrDefault(p => p.Id == kb.EmbedProviderId);
        string providerName = boundProvider?.Name
            ?? (kb.EmbedProviderId.Length > 0 ? $"{kb.EmbedProviderId}（配置已删除）" : "未绑定");
        string repair = kb.MissingVectorCount > 0
            ? $" · 缺 {kb.MissingVectorCount} 个向量，请重建"
            : "";
        BaseTitle.Text = kb.Name;
        BaseSubtitle.Text = kb.Chunks.Count > 0
            ? $"{kb.Documents.Count} 个文档 · {kb.Chunks.Count} 个片段 · 已索引 {kb.TotalChars:N0} 字" +
              $" · {providerName} / {kb.EmbedModel}（{kb.Dimension} 维）{repair}"
            : "还没有内容，先选好嵌入服务商和模型再添加文档";

        _loading = true;
        EmbedProviderBox.Items.Clear();
        var providers = App.Services.Settings.Chat.Providers;
        foreach (var provider in providers)
            EmbedProviderBox.Items.Add(new ComboBoxItem { Content = provider.Name, Tag = provider });

        int providerIndex;
        if (kb.Chunks.Count > 0)
        {
            providerIndex = providers.FindIndex(p => p.Id == kb.EmbedProviderId);
            if (providerIndex < 0)
            {
                EmbedProviderBox.Items.Insert(0, new ComboBoxItem
                {
                    Content = $"配置已删除：{kb.EmbedProviderId}",
                    Tag = null,
                });
                providerIndex = 0;
            }
            EmbedModelBox.Text = kb.EmbedModel;
            EmbedModelBox.ToolTip = "添加文档必须沿用当前绑定；需要换模型时选择新配置并点「重建索引」";
        }
        else
        {
            providerIndex = providers.FindIndex(p =>
                p.Id == App.Services.Settings.Chat.ActiveProviderId);
            if (providerIndex < 0 && providers.Count > 0) providerIndex = 0;
            EmbedModelBox.Text = App.Services.Settings.Chat.EmbedModel;
            EmbedModelBox.ToolTip = "可从下拉列表选择，也可手动输入服务商支持的嵌入模型 ID";
        }
        EmbedProviderBox.SelectedIndex = providerIndex;
        EmbedModelBox.Items.Clear();
        _loading = false;

        DocList.Items.Clear();
        foreach (var doc in kb.Documents)
            DocList.Items.Add(BuildDocItem(kb, doc));
        EmptyHint.Visibility = kb.Documents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ListBoxItem BuildDocItem(KnowledgeBase kb, KnowledgeDocument doc)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = AppIconFactory.Create(IconKind.Document, 15, (Brush)FindResource("AccentLightBrush"));
        grid.Children.Add(icon);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = doc.FileName, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(name);

        var meta = new TextBlock
        {
            Text = doc.WasTruncated
                ? $"已索引 {doc.IndexedCharCount:N0} / 提取 {doc.CharCount:N0} 字 · {doc.ChunkCount} 块 · {doc.Added:yyyy-MM-dd HH:mm}"
                : $"{(doc.IndexedCharCount > 0 ? doc.IndexedCharCount : doc.CharCount):N0} 字 · {doc.ChunkCount} 块 · {doc.Added:yyyy-MM-dd HH:mm}",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        stack.Children.Add(meta);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        var remove = new Button
        {
            Content = AppIconFactory.Create(IconKind.Delete, 14),
            Style = (Style)FindResource("IconButton"),
            Width = 28, Height = 28,
            ToolTip = "从知识库移除",
        };
        remove.Click += (_, _) =>
        {
            if (MessageBox.Show($"确定从「{kb.Name}」移除文档「{doc.FileName}」及其全部向量吗？",
                    "移除文档", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (!Service.Store.RemoveDocument(kb, doc))
            {
                Toast.Show("移除失败：原文档仍然保留", 4000);
                return;
            }
            RenderCurrent();
            ReloadBases();
            Toast.Show($"已移除 {doc.FileName}");
        };
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        return new ListBoxItem { Content = grid, Tag = doc };
    }

    // ==================== 添加文档 ====================
    private async void OnAddDocumentClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var target = _current;
        var provider = CurrentEmbedProvider();
        if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            Toast.Show("请先选择已配置 API Key 的嵌入服务商", 4000);
            App.Services.ShowSettingsWindow();
            return;
        }

        string model = EmbedModelBox.Text.Trim();
        if (model.Length == 0)
        {
            Toast.Show("请先填写或选择嵌入模型", 3500);
            EmbedModelBox.Focus();
            return;
        }

        if (target.Chunks.Count > 0
            && (!string.Equals(target.EmbedProviderId, provider.Id, StringComparison.Ordinal)
                || !string.Equals(target.EmbedModel, model, StringComparison.Ordinal)))
        {
            Toast.Show("当前选择与已有索引不一致；请先点「重建索引」，或切回原服务商和模型", 5000);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = DocumentParser.FileDialogFilter,
            Title = "选择要加入知识库的文档",
        };
        if (dialog.ShowDialog() != true) return;

        // 记住用户选的嵌入模型，下次新建库时默认带上
        App.Services.Settings.Chat.EmbedModel = model;
        App.Services.Settings.Save();

        _cts = new CancellationTokenSource();
        SetBusy(true);
        string currentFile = "";
        var progress = new Progress<string>(msg =>
            StatusText.Text = currentFile.Length > 0 ? $"{currentFile} · {msg}" : msg);
        int succeeded = 0;
        var failures = new List<string>();

        try
        {
            foreach (var path in dialog.FileNames)
            {
                _cts.Token.ThrowIfCancellationRequested();
                currentFile = System.IO.Path.GetFileName(path);
                StatusText.Text = $"正在处理 {currentFile}…";
                try
                {
                    var added = await Service.AddDocumentAsync(
                        target, path, provider, model, progress, _cts.Token);
                    succeeded++;
                    if (added.WasTruncated)
                        failures.Add($"{added.FileName}：超过 {KnowledgeService.MaxKnowledgeCharsPerFile:N0} 字，已按上限索引");
                    ReloadBases();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error($"加入知识库失败：{currentFile}", ex);
                    failures.Add($"{currentFile}：{ex.Message}");
                }
            }

            StatusText.Text = failures.Count == 0
                ? $"完成：已加入 {succeeded} 个文档"
                : $"完成：{succeeded} 个成功，{failures.Count} 个有提示/失败";
            if (failures.Count == 0)
                Toast.Show($"已加入 {succeeded} 个文档");
            else
                Toast.Show(string.Join("\n", failures.Take(3)), 6000);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"已取消，成功加入 {succeeded} 个文档";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
            RenderCurrent();
            ReloadBases();
        }
    }

    private ChatProvider? CurrentEmbedProvider() =>
        (EmbedProviderBox.SelectedItem as ComboBoxItem)?.Tag as ChatProvider;

    private void OnEmbedProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        EmbedModelBox.Items.Clear();
        var provider = CurrentEmbedProvider();
        if (provider is null)
        {
            EmbedModelBox.Text = "";
            return;
        }

        if (_current is { Chunks.Count: > 0 } kb
            && string.Equals(kb.EmbedProviderId, provider.Id, StringComparison.Ordinal))
            EmbedModelBox.Text = kb.EmbedModel;
        else
            EmbedModelBox.Text = "";
    }

    /// <summary>展开嵌入模型下拉时，从服务商拉取模型清单并挑出嵌入类。</summary>
    private async void OnEmbedDropDownOpened(object? sender, EventArgs e)
    {
        if (EmbedModelBox.Items.Count > 0) return;

        var provider = CurrentEmbedProvider();
        if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKey)) return;

        try
        {
            var all = await App.Services.Chat.FetchModelsAsync(provider, includeEmbeddings: true);
            var embeds = all.Where(IsEmbeddingModel).ToList();
            foreach (var m in (embeds.Count > 0 ? embeds : all)) EmbedModelBox.Items.Add(m);
        }
        catch (Exception ex)
        {
            Logger.Info("拉取嵌入模型失败：" + ex.Message);
        }
    }

    private static bool IsEmbeddingModel(string id)
    {
        string s = id.ToLowerInvariant();
        return s.Contains("embed") || s.Contains("bge") || s.Contains("gte") || s.Contains("m3e");
    }

    private async void OnRebuildIndexClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Chunks.Count == 0) return;
        var target = _current;
        var provider = CurrentEmbedProvider();
        string model = EmbedModelBox.Text.Trim();

        if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            Toast.Show("请选择已配置 API Key 的嵌入服务商", 4000);
            return;
        }
        if (model.Length == 0)
        {
            Toast.Show("请先填写或选择嵌入模型", 3500);
            EmbedModelBox.Focus();
            return;
        }

        if (MessageBox.Show(
                $"将用「{provider.Name} / {model}」重新生成 {target.Chunks.Count} 个片段的向量。\n\n" +
                "这会调用嵌入 API；全部成功前旧索引仍然保留。确定继续吗？",
                "重建知识库索引", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        App.Services.Settings.Chat.EmbedModel = model;
        App.Services.Settings.Save();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        var progress = new Progress<string>(msg => StatusText.Text = msg);

        try
        {
            await Service.RebuildIndexAsync(target, provider, model, progress, _cts.Token);
            StatusText.Text = $"索引已重建：{target.Chunks.Count} 块 / {target.Dimension} 维";
            Toast.Show("知识库索引已安全重建");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消，旧索引未改变";
        }
        catch (Exception ex)
        {
            Logger.Error("重建知识库索引失败", ex);
            StatusText.Text = "重建失败，旧索引仍然可用";
            Toast.Show("重建失败：" + ex.Message, 5000);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
            ReloadBases();
        }
    }

    // ==================== 试检索 ====================
    private async void OnTestSearchClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Chunks.Count == 0) return;

        var dialog = new InputDialog("试检索", "输入一个问题") { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

        var provider = App.Services.Settings.Chat.Providers
            .FirstOrDefault(p => p.Id == _current.EmbedProviderId);
        if (provider is null)
        {
            Toast.Show("建库时使用的服务商配置已被删除；请先选择新服务商并重建索引", 5000);
            return;
        }

        StatusText.Text = "检索中…";
        try
        {
            var hits = await Service.SearchAsync(_current, dialog.Value.Trim(), provider,
                App.Services.Settings.Chat.KnowledgeTopK, App.Services.Settings.Chat.KnowledgeMinScore);

            StatusText.Text = $"检索到 {hits.Count} 个相关片段";
            if (hits.Count == 0)
            {
                Toast.Show("没有检索到相关内容，可以调低设置里的相关度阈值");
                return;
            }

            string preview = string.Join("\n\n", hits.Select((h, i) =>
                $"【K{i + 1}】{h.Chunk.DocumentName} 第 {h.Chunk.Index + 1} 段" +
                $"（综合 {h.Score:0.000} / 语义 {h.DenseScore:0.000} / 关键词 {h.KeywordScore:0.000}）\n{h.Chunk.Text}"));
            Ocr.TextResultWindow.ShowText("试检索结果", preview);
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            Toast.Show("检索失败：" + ex.Message, 4000);
        }
    }

    // ==================== 杂项 ====================
    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCancelOperationClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "正在取消…";
        _cts?.Cancel();
    }

    private void SetBusy(bool busy)
    {
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelImportBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        NewBaseBtn.IsEnabled = !busy;
        BaseList.IsEnabled = !busy;

        bool has = _current is not null;
        EmbedProviderBox.IsEnabled = has && !busy;
        EmbedModelBox.IsEnabled = has && !busy;
        AddDocBtn.IsEnabled = has && !busy;
        TestBtn.IsEnabled = has && _current!.Chunks.Count > 0 && !busy;
        RebuildBtn.IsEnabled = has && _current!.Chunks.Count > 0 && !busy;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
