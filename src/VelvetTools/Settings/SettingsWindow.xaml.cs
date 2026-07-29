using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VelvetTools.Common;
using VelvetTools.Modules.Translate;

namespace VelvetTools.Settings;

public partial class SettingsWindow : GlassWindow
{
    private readonly List<(string Name, string Label, HotkeyCaptureBox Box)> _hotkeyBoxes = new();
    private bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        EscapeAction = EscAction.Hide;

        foreach (var (_, name) in TranslateService.Languages)
            TransLangBox.Items.Add(name);

        BuildHotkeyRows();
        LoadFromSettings();

        string releaseVersion = typeof(SettingsWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0]
            ?? typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        string displayVersion = releaseVersion == "0.0.1-beta.1"
            ? "Beta 0.01"
            : $"v{releaseVersion}";
        VersionText.Text = $"{displayVersion} · GPL-3.0-or-later";
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void ShowSettings()
    {
        LoadFromSettings();
        SaveStatus.Text = "";
        Show();
        Activate();
    }

    /// <summary>仅供 --smoke 截图覆盖各设置页，不作为产品导航 API。</summary>
    internal void SelectPageForSelfTest(int index)
        => Nav.SelectedIndex = Math.Clamp(index, 0, Nav.Items.Count - 1);

    private void BuildHotkeyRows()
    {
        var defs = new (string Name, string Label)[]
        {
            ("Screenshot", "区域截图"),
            ("ScreenshotOcr", "截图 OCR"),
            ("ScreenshotTranslate", "截图翻译"),
            ("ColorPicker", "屏幕取色"),
            ("ClipboardHistory", "剪贴板历史"),
            ("Launcher", "快速启动器"),
            ("Chat", "AI 对话"),
            ("Search", "文件搜索"),
        };

        foreach (var (name, label) in defs)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("BodyText"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var box = new HotkeyCaptureBox
            {
                Style = (Style)FindResource("GlassButton"),
                MinHeight = 46,
                Padding = new Thickness(16, 0, 16, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "点击后按下新的键盘组合；Esc 取消，Backspace 清除",
            };
            box.CanCommit = gesture => CanAcceptHotkey(box, gesture);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);

            HotkeyPanel.Children.Add(grid);
            _hotkeyBoxes.Add((name, label, box));
        }
    }

    private bool CanAcceptHotkey(HotkeyCaptureBox source, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return true;
        var duplicate = _hotkeyBoxes.FirstOrDefault(item =>
            !ReferenceEquals(item.Box, source)
            && string.Equals(item.Box.Gesture, gesture, StringComparison.OrdinalIgnoreCase));
        if (duplicate.Box is null) return true;

        Toast.Show($"“{gesture}”已用于{duplicate.Label}，未修改当前快捷键", 3600);
        return false;
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = App.Services.Settings;

        ThemeSystemRadio.IsChecked = s.General.Theme is not ("dark" or "light");
        ThemeDarkRadio.IsChecked = s.General.Theme == "dark";
        ThemeLightRadio.IsChecked = s.General.Theme == "light";

        AutoStartCheck.IsChecked = StartupManager.IsAutoStartEnabled();
        TaskbarBarCheck.IsChecked = s.General.ShowTaskbarBar;
        BarNetCheck.IsChecked = s.General.TaskbarShowNet;
        BarCpuCheck.IsChecked = s.General.TaskbarShowCpu == true;
        BarMemCheck.IsChecked = s.General.TaskbarShowMem == true;
        BarCpuTempCheck.IsChecked = s.General.TaskbarShowCpuTemp == true;
        BarGpuTempCheck.IsChecked = s.General.TaskbarShowGpuTemp == true;
        BarDiskTempCheck.IsChecked = s.General.TaskbarShowDiskTemp == true;
        BarItemsSection.Visibility = s.General.ShowTaskbarBar ? Visibility.Visible : Visibility.Collapsed;
        FloatCheck.IsChecked = s.General.ShowFloatWindow;
        AlwaysAdminCheck.IsChecked = s.General.AlwaysRunAsAdmin || StartupManager.TaskExists();

        // 已是管理员时整行隐藏，不做多余提示
        bool isAdmin = Elevation.IsAdmin;
        AdminStateText.Text = "当前为普通权限";
        ElevateRow.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
        ElevateDivider.Visibility = ElevateRow.Visibility;

        foreach (var (name, _, box) in _hotkeyBoxes)
            box.Gesture = GetHotkey(s, name);

        ShotCopyCheck.IsChecked = s.Screenshot.AutoCopy;
        ShotSaveCheck.IsChecked = s.Screenshot.AutoSaveFile;
        ShotDirBox.Text = s.Screenshot.SaveDir;

        OcrProviderBox.SelectedIndex = s.Ocr.Provider == "openai" ? 1 : 0;
        OpenAiUrlBox.Text = s.Ocr.OpenAi.BaseUrl;
        OpenAiKeyBox.Text = s.Ocr.OpenAi.ApiKey;
        OpenAiModelBox.Text = s.Ocr.OpenAi.Model;

        TransProviderBox.SelectedIndex = s.Translate.Provider switch { "deepl" => 1, "baidu" => 2, _ => 0 };
        int langIdx = Array.FindIndex(TranslateService.Languages, l => l.Code == s.Translate.TargetLang);
        TransLangBox.SelectedIndex = Math.Max(0, langIdx);
        DeepLKeyBox.Text = s.Translate.DeepLApiKey;
        DeepLFreeCheck.IsChecked = s.Translate.DeepLUseFreeApi;
        BaiduIdBox.Text = s.Translate.BaiduAppId;
        BaiduKeyBox.Text = s.Translate.BaiduSecret;

        ClipEnableCheck.IsChecked = s.Clipboard.Enabled;
        ClipImageCheck.IsChecked = s.Clipboard.CaptureImages;
        ClipPasteCheck.IsChecked = s.Clipboard.AutoPaste;
        ClipMaxBox.Text = s.Clipboard.MaxItems.ToString();
        _loading = false;

        ReloadChatPage();
        ReloadSearchPage();
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        string mode = ReferenceEquals(sender, ThemeDarkRadio) ? "dark"
                    : ReferenceEquals(sender, ThemeLightRadio) ? "light"
                    : "system";
        var s = App.Services.Settings;
        s.General.Theme = mode;
        s.Save();
        Common.ThemeManager.SetMode(mode);
    }

    private void OnElevateClick(object sender, RoutedEventArgs e)
        => Elevation.RestartAsAdmin();

    /// <summary>任务栏样式/显示项改动立即生效；逐项开关仅在信息栏开启时展示。</summary>
    private void OnTaskbarOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var g = App.Services.Settings.General;
        g.ShowTaskbarBar = TaskbarBarCheck.IsChecked == true;
        g.TaskbarShowNet = BarNetCheck.IsChecked == true;
        g.TaskbarShowCpu = BarCpuCheck.IsChecked == true;
        g.TaskbarShowMem = BarMemCheck.IsChecked == true;
        g.TaskbarShowCpuTemp = BarCpuTempCheck.IsChecked == true;
        g.TaskbarShowGpuTemp = BarGpuTempCheck.IsChecked == true;
        g.TaskbarShowDiskTemp = BarDiskTempCheck.IsChecked == true;
        // 旧版合并开关同步维护，方便回退到旧存档格式时不丢选择。
        g.TaskbarShowCpuMem = g.TaskbarShowCpu == true || g.TaskbarShowMem == true;
        g.TaskbarShowTemp = g.TaskbarShowCpuTemp == true || g.TaskbarShowGpuTemp == true || g.TaskbarShowDiskTemp == true;
        BarItemsSection.Visibility = g.ShowTaskbarBar ? Visibility.Visible : Visibility.Collapsed;
        App.Services.Settings.Save();
        App.Services.SyncTaskbarBar();
    }

    /// <summary>只读查询系统、厂商接口及用户已运行监控工具的 WMI；本软件不加载内核驱动。</summary>
    private void OnTempCheckClick(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;

        bool anyTempChecked = BarCpuTempCheck.IsChecked == true
            || BarGpuTempCheck.IsChecked == true
            || BarDiskTempCheck.IsChecked == true;
        if (anyTempChecked
            && App.Services.Hardware.Latest.CpuTemp is null
            && App.Services.Hardware.Latest.GpuTemp is null
            && App.Services.Hardware.Latest.DiskTemp is null)
        {
            Toast.Show("未检测到温度读数；Windows 并非在所有设备上公开 CPU 温度，本软件不会为此强行安装内核驱动", 5000);
        }
        OnTaskbarOptionChanged(sender, e);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var s = App.Services.Settings;
        var oldHotkeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["screenshot"] = s.Hotkeys.Screenshot,
            ["ocr"] = s.Hotkeys.ScreenshotOcr,
            ["translate"] = s.Hotkeys.ScreenshotTranslate,
            ["colorpicker"] = s.Hotkeys.ColorPicker,
            ["clipboard"] = s.Hotkeys.ClipboardHistory,
            ["launcher"] = s.Hotkeys.Launcher,
            ["chat"] = s.Chat.Hotkey,
            ["search"] = s.Search.Hotkey,
        };

        bool wantAuto = AutoStartCheck.IsChecked == true;
        bool wantAdmin = AlwaysAdminCheck.IsChecked == true;
        try
        {
            if (wantAdmin && !Elevation.IsAdmin)
            {
                // 普通权限下弹一次 UAC 就地完成配置，不再要求“先重启再保存一次”。
                if (StartupManager.TryApplyAdminElevated(wantAuto))
                {
                    s.General.AlwaysRunAsAdmin = true;
                }
                else
                {
                    // 用户取消了 UAC：回退到普通模式，但自启选择不能丢。
                    s.General.AlwaysRunAsAdmin = false;
                    AlwaysAdminCheck.IsChecked = false;
                    StartupManager.Apply(wantAuto, alwaysAdmin: false);
                    Toast.Show("未获得管理员授权，“始终最高权限”未开启；开机自启设置已按普通权限保存", 5000);
                }
            }
            else if (!wantAdmin && !Elevation.IsAdmin && StartupManager.TaskExists())
            {
                // 关闭最高权限：任务多由管理员创建，普通权限删不掉时自动弹 UAC 重试。
                if (StartupManager.TryRemoveTaskElevated())
                {
                    s.General.AlwaysRunAsAdmin = false;
                    StartupManager.Apply(wantAuto, alwaysAdmin: false);
                }
                else
                {
                    // 任务还在（可能带登录触发器），不能再叠加 Run 键造成双重自启。
                    AlwaysAdminCheck.IsChecked = true;
                    s.General.AlwaysRunAsAdmin = true;
                    Toast.Show("未获得管理员授权，无法删除最高权限计划任务，开关保持开启", 5000);
                }
            }
            else
            {
                s.General.AlwaysRunAsAdmin = wantAdmin;
                StartupManager.Apply(wantAuto, wantAdmin);
            }
        }
        catch (Exception ex)
        {
            Toast.Show("配置自启/权限失败：" + ex.Message, 4000);
        }
        s.General.ShowTaskbarBar = TaskbarBarCheck.IsChecked == true;
        s.General.ShowFloatWindow = FloatCheck.IsChecked == true;

        foreach (var (name, _, box) in _hotkeyBoxes)
            SetHotkey(s, name, box.Gesture.Trim());

        s.Screenshot.AutoCopy = ShotCopyCheck.IsChecked == true;
        s.Screenshot.AutoSaveFile = ShotSaveCheck.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(ShotDirBox.Text)) s.Screenshot.SaveDir = ShotDirBox.Text.Trim();

        s.Ocr.Provider = OcrProviderBox.SelectedIndex == 1 ? "openai" : "windows";
        s.Ocr.OpenAi.BaseUrl = OpenAiUrlBox.Text.Trim();
        s.Ocr.OpenAi.ApiKey = OpenAiKeyBox.Text.Trim();
        s.Ocr.OpenAi.Model = OpenAiModelBox.Text.Trim();

        // OpenAI 配置 OCR 与翻译共用一份
        s.Translate.OpenAi.BaseUrl = s.Ocr.OpenAi.BaseUrl;
        s.Translate.OpenAi.ApiKey = s.Ocr.OpenAi.ApiKey;
        s.Translate.OpenAi.Model = s.Ocr.OpenAi.Model;

        s.Translate.Provider = TransProviderBox.SelectedIndex switch { 1 => "deepl", 2 => "baidu", _ => "openai" };
        s.Translate.TargetLang = TranslateService.Languages[Math.Max(0, TransLangBox.SelectedIndex)].Code;
        s.Translate.DeepLApiKey = DeepLKeyBox.Text.Trim();
        s.Translate.DeepLUseFreeApi = DeepLFreeCheck.IsChecked == true;
        s.Translate.BaiduAppId = BaiduIdBox.Text.Trim();
        s.Translate.BaiduSecret = BaiduKeyBox.Text.Trim();

        s.Clipboard.Enabled = ClipEnableCheck.IsChecked == true;
        s.Clipboard.CaptureImages = ClipImageCheck.IsChecked == true;
        s.Clipboard.AutoPaste = ClipPasteCheck.IsChecked == true;
        if (int.TryParse(ClipMaxBox.Text, out int max)) s.Clipboard.MaxItems = Math.Clamp(max, 20, 2000);

        // AI 对话
        SaveChatProvider();
        s.Chat.SystemPrompt = ChatSystemBox.Text;
        s.Chat.Stream = ChatStreamCheck.IsChecked == true;
        s.Chat.Temperature = Math.Round(ChatTempSlider.Value, 1);
        if (int.TryParse(ChatContextBox.Text, out int ctx)) s.Chat.ContextMessages = Math.Clamp(ctx, 0, 100);

        // 联网搜索
        var web = s.Chat.WebSearch;
        web.Provider = WebProviderBox.SelectedIndex switch { 1 => "tavily", 2 => "bing", _ => "duckduckgo" };
        web.EnabledByDefault = WebDefaultCheck.IsChecked == true;
        web.TavilyApiKey = TavilyKeyBox.Text.Trim();
        web.BingApiKey = BingKeyBox.Text.Trim();
        if (int.TryParse(WebMaxBox.Text, out int wm)) web.MaxResults = Math.Clamp(wm, 1, 15);

        // 知识库
        if (int.TryParse(KbTopKBox.Text, out int topK)) s.Chat.KnowledgeTopK = Math.Clamp(topK, 1, 20);
        if (double.TryParse(KbMinScoreBox.Text, out double minScore))
            s.Chat.KnowledgeMinScore = Math.Clamp(minScore, 0, 0.95);

        // 文件搜索
        if (int.TryParse(SearchMaxBox.Text, out int sm)) s.Search.MaxResults = Math.Clamp(sm, 20, 2000);

        // 应用运行时状态
        var failures = App.Services.ApplyHotkeys();
        foreach (var failure in failures)
        {
            if (oldHotkeys.TryGetValue(failure.Name, out string? oldValue))
                RestoreHotkeySetting(s, failure.Name, oldValue);
        }
        if (failures.Count > 0)
        {
            // 管理器已经保留旧系统注册；UI 和持久化设置也一起回滚，
            // 避免下次启动继续尝试一个已知冲突的组合。
            foreach (var (name, _, box) in _hotkeyBoxes)
                box.Gesture = GetHotkey(s, name);
        }
        s.Save();

        App.Services.SyncFloatWindow();
        App.Services.SyncClipboardListener();

        SaveStatus.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
        if (failures.Count > 0)
            Toast.Show("部分热键未生效：" + string.Join("；", failures), 4000);
        else
            Toast.Show("设置已保存并生效");
    }

    private static void RestoreHotkeySetting(AppSettings s, string name, string value)
    {
        switch (name)
        {
            case "screenshot": s.Hotkeys.Screenshot = value; break;
            case "ocr": s.Hotkeys.ScreenshotOcr = value; break;
            case "translate": s.Hotkeys.ScreenshotTranslate = value; break;
            case "colorpicker": s.Hotkeys.ColorPicker = value; break;
            case "clipboard": s.Hotkeys.ClipboardHistory = value; break;
            case "launcher": s.Hotkeys.Launcher = value; break;
            case "chat": s.Chat.Hotkey = value; break;
            case "search": s.Search.Hotkey = value; break;
        }
    }

    private static string GetHotkey(AppSettings s, string name) => name switch
    {
        "Screenshot" => s.Hotkeys.Screenshot,
        "ScreenshotOcr" => s.Hotkeys.ScreenshotOcr,
        "ScreenshotTranslate" => s.Hotkeys.ScreenshotTranslate,
        "ColorPicker" => s.Hotkeys.ColorPicker,
        "ClipboardHistory" => s.Hotkeys.ClipboardHistory,
        "Launcher" => s.Hotkeys.Launcher,
        "Chat" => s.Chat.Hotkey,
        "Search" => s.Search.Hotkey,
        _ => "",
    };

    private static void SetHotkey(AppSettings s, string name, string value)
    {
        switch (name)
        {
            case "Screenshot": s.Hotkeys.Screenshot = value; break;
            case "ScreenshotOcr": s.Hotkeys.ScreenshotOcr = value; break;
            case "ScreenshotTranslate": s.Hotkeys.ScreenshotTranslate = value; break;
            case "ColorPicker": s.Hotkeys.ColorPicker = value; break;
            case "ClipboardHistory": s.Hotkeys.ClipboardHistory = value; break;
            case "Launcher": s.Hotkeys.Launcher = value; break;
            case "Chat": s.Chat.Hotkey = value; break;
            case "Search": s.Search.Hotkey = value; break;
        }
    }

    // ==================== AI 对话 ====================
    private ChatProvider? CurrentChatProvider()
    {
        var list = App.Services.Settings.Chat.Providers;
        int i = ChatProviderBox.SelectedIndex;
        return i >= 0 && i < list.Count ? list[i] : null;
    }

    private void ReloadChatPage()
    {
        var chat = App.Services.Settings.Chat;
        chat.EnsurePresets();

        _loading = true;
        ChatProviderBox.Items.Clear();
        foreach (var p in chat.Providers) ChatProviderBox.Items.Add(p.Name);
        int idx = chat.Providers.FindIndex(p => p.Id == chat.ActiveProviderId);
        ChatProviderBox.SelectedIndex = idx >= 0 ? idx : 0;
        _loading = false;

        BindChatProvider();

        ChatSystemBox.Text = chat.SystemPrompt;
        ChatStreamCheck.IsChecked = chat.Stream;
        ChatTempSlider.Value = chat.Temperature;
        ChatTempText.Text = chat.Temperature.ToString("0.0");
        ChatContextBox.Text = chat.ContextMessages.ToString();

        var web = chat.WebSearch;
        WebProviderBox.SelectedIndex = web.Provider switch { "tavily" => 1, "bing" => 2, _ => 0 };
        WebDefaultCheck.IsChecked = web.EnabledByDefault;
        TavilyKeyBox.Text = web.TavilyApiKey;
        BingKeyBox.Text = web.BingApiKey;
        WebMaxBox.Text = web.MaxResults.ToString();

        KbTopKBox.Text = chat.KnowledgeTopK.ToString();
        KbMinScoreBox.Text = chat.KnowledgeMinScore.ToString("0.00");
    }

    private void OnOpenKnowledgeClick(object sender, RoutedEventArgs e)
        => App.Services.ShowKnowledgeWindow();

    private void BindChatProvider()
    {
        var p = CurrentChatProvider();
        if (p is null) return;

        ChatUrlBox.Text = p.BaseUrl;
        ChatKeyBox.Password = p.ApiKey;
        ChatKeyRevealBox.Text = "";
        ChatKeyRevealBox.Visibility = Visibility.Collapsed;
        ChatKeyBox.Visibility = Visibility.Visible;
        ChatKeyRevealToggle.IsChecked = false;

        ChatModelBox.Items.Clear();
        foreach (var m in p.Models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            ChatModelBox.Items.Add(m);
        ChatModelBox.Text = p.Model;

        ChatModelHint.Text = p.Id == "doubao"
            ? "模型（豆包填推理接入点 ID，形如 ep-2024xxxx-xxxxx）"
            : "模型";
        ChatDeleteBtn.Visibility = p.IsCustom ? Visibility.Visible : Visibility.Collapsed;
        ChatTestResult.Text = "";
        UpdateChatProviderStatus(p);
    }

    private void UpdateChatProviderStatus(ChatProvider p)
    {
        bool hasKey = !string.IsNullOrWhiteSpace(ChatKeyBox.Password);
        bool hasModel = !string.IsNullOrWhiteSpace(ChatModelBox.Text);
        string brush = hasKey && hasModel ? "SuccessBrush" : "WarningBrush";
        ChatProviderStatusDot.Fill = (System.Windows.Media.Brush)FindResource(brush);
        ChatProviderStatusText.Text = !hasKey
            ? "尚未填写 API Key"
            : !hasModel
                ? "密钥已填写，请获取或输入模型"
                : $"{p.Name} · {ChatModelBox.Text.Trim()} · 配置就绪";
    }

    private void SaveChatProvider()
    {
        var p = CurrentChatProvider();
        if (p is null) return;
        p.BaseUrl = ChatUrlBox.Text.Trim();
        p.ApiKey = ChatKeyBox.Password.Trim();
        p.Model = ChatModelBox.Text.Trim();
    }

    private void OnChatKeyRevealDown(object sender, MouseButtonEventArgs e)
    {
        ChatKeyRevealBox.Text = ChatKeyBox.Password;
        ChatKeyBox.Visibility = Visibility.Collapsed;
        ChatKeyRevealBox.Visibility = Visibility.Visible;
        ChatKeyRevealToggle.IsChecked = true;
    }

    private void OnChatKeyRevealUp(object sender, MouseButtonEventArgs e) => HideChatKey();

    private void OnChatKeyRevealLeave(object sender, MouseEventArgs e) => HideChatKey();

    private void HideChatKey()
    {
        ChatKeyRevealBox.Text = "";
        ChatKeyRevealBox.Visibility = Visibility.Collapsed;
        ChatKeyBox.Visibility = Visibility.Visible;
        ChatKeyRevealToggle.IsChecked = false;
    }

    private void OnChatProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var p = CurrentChatProvider();
        if (p is null) return;
        App.Services.Settings.Chat.ActiveProviderId = p.Id;
        BindChatProvider();
    }

    private void OnChatAddProviderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Modules.Chat.InputDialog("新增服务商", "我的服务") { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

        var chat = App.Services.Settings.Chat;
        var provider = new ChatProvider
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = dialog.Value.Trim(),
            BaseUrl = "https://",
            IsCustom = true,
        };
        chat.Providers.Add(provider);
        chat.ActiveProviderId = provider.Id;
        App.Services.Settings.Save();
        ReloadChatPage();
    }

    private void OnChatDeleteProviderClick(object sender, RoutedEventArgs e)
    {
        var p = CurrentChatProvider();
        if (p is null || !p.IsCustom) return;
        if (MessageBox.Show($"确定删除服务商「{p.Name}」吗？", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var chat = App.Services.Settings.Chat;
        chat.Providers.Remove(p);
        chat.ActiveProviderId = chat.Providers.FirstOrDefault()?.Id ?? "";
        App.Services.Settings.Save();
        ReloadChatPage();
    }

    private void OnChatTempChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        ChatTempText.Text = e.NewValue.ToString("0.0");
    }

    /// <summary>从服务商 /models 拉取真实可用的模型清单，替换掉内置候选。</summary>
    private async void OnChatFetchModelsClick(object sender, RoutedEventArgs e)
    {
        SaveChatProvider();
        var p = CurrentChatProvider();
        if (p is null) return;

        ChatFetchBtn.IsEnabled = false;
        ChatTestResult.Text = "正在获取模型列表…";
        ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("TextTertiaryBrush");
        try
        {
            var models = await App.Services.Chat.FetchModelsAsync(p);
            if (models.Count == 0)
            {
                ChatTestResult.Text = "该密钥下没有可用的对话模型";
                ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                return;
            }

            string current = ChatModelBox.Text.Trim();
            p.Models = string.Join(",", models);
            p.ModelsFromApi = true;

            _loading = true;
            ChatModelBox.Items.Clear();
            foreach (var m in models) ChatModelBox.Items.Add(m);
            // 原选中的模型仍在列表里就保留，否则选第一个
            ChatModelBox.Text = models.Contains(current) ? current : models[0];
            _loading = false;

            p.Model = ChatModelBox.Text;
            App.Services.Settings.Save();

            ChatTestResult.Text = $"✓ 获取到 {models.Count} 个对话模型";
            ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            UpdateChatProviderStatus(p);
        }
        catch (Exception ex)
        {
            ChatTestResult.Text = "✕ " + ex.Message + "（该服务商可能不支持列表接口，可手动输入模型名）";
            ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
        finally
        {
            ChatFetchBtn.IsEnabled = true;
        }
    }

    private async void OnChatTestClick(object sender, RoutedEventArgs e)
    {
        SaveChatProvider();
        var p = CurrentChatProvider();
        if (p is null) return;

        ChatTestBtn.IsEnabled = false;
        ChatTestResult.Text = "测试中…";
        ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("TextTertiaryBrush");
        try
        {
            string result = await App.Services.Chat.TestAsync(p);
            ChatTestResult.Text = "✓ " + result;
            ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            UpdateChatProviderStatus(p);
            App.Services.Settings.Save();
        }
        catch (Exception ex)
        {
            ChatTestResult.Text = "✕ " + ex.Message;
            ChatTestResult.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
        finally
        {
            ChatTestBtn.IsEnabled = true;
        }
    }

    // ==================== 文件搜索 ====================
    private void ReloadSearchPage()
    {
        SearchMaxBox.Text = App.Services.Settings.Search.MaxResults.ToString();
        RefreshEverythingStatus();
    }

    private void RefreshEverythingStatus()
    {
        bool running = Modules.Search.EverythingClient.IsUsable;
        bool connected = Modules.Search.EverythingClient.IsRunning;
        bool installed = Modules.Search.EverythingBootstrap.ResolveExe() is not null;

        EvStatusDot.Fill = (System.Windows.Media.Brush)FindResource(
            running ? "SuccessBrush" : installed ? "WarningBrush" : "TextTertiaryBrush");
        EvStatusText.Text = running
            ? "索引引擎运行中，搜索可用"
            : connected ? "索引引擎已连接，但索引仍为空（可在文件搜索页重新准备）"
            : installed ? "索引引擎已就绪（首次搜索时自动启动）"
            : "未找到索引引擎，请重新安装本软件";
    }

    private void OnEverythingCheckClick(object sender, RoutedEventArgs e)
    {
        RefreshEverythingStatus();
        long defaultItems = Modules.Search.EverythingSdk.IndexedItemCount;
        Toast.Show(!Modules.Search.EverythingClient.IsUsable
            ? "Everything 尚未形成可用索引，请打开文件搜索并等待准备完成"
            : defaultItems > 0
                ? $"Everything 搜索可用，默认实例已索引 {defaultItems:N0} 项"
                : "Everything 搜索可用，正在使用 VelvetTools 独立索引");
    }

    private void OnEverythingDownloadClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = "https://www.voidtools.com/zh-cn/downloads/", UseShellExecute = true });

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var pages = new FrameworkElement[]
        {
            PageGeneral, PageHotkeys, PageScreenshot, PageApi, PageClipboard, PageChat, PageSearch, PageAbout,
        };
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowseDirClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = ShotDirBox.Text,
            Description = "选择截图保存目录",
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ShotDirBox.Text = dialog.SelectedPath;
    }

    private void OnOpenDataDirClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = Logger.DataDir, UseShellExecute = true });

    private void OnOpenLogClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = Logger.LogFile, UseShellExecute = true });

    private void OnOpenProjectClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/zhicheng6657-dev/VelvetTools-cess",
            UseShellExecute = true,
        });

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
