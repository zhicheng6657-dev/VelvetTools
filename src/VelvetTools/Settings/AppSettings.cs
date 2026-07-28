using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using VelvetTools.Common;

namespace VelvetTools.Settings;

/// <summary>应用设置（JSON 持久化于 %AppData%\VelvetTools\settings.json）。</summary>
public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public ScreenshotSettings Screenshot { get; set; } = new();
    public OcrSettings Ocr { get; set; } = new();
    public TranslateSettings Translate { get; set; } = new();
    public ClipboardSettings Clipboard { get; set; } = new();
    public ColorPickerSettings ColorPicker { get; set; } = new();
    public ChatSettings Chat { get; set; } = new();
    public SearchSettings Search { get; set; } = new();

    // ---------- 持久化 ----------
    private static readonly string FilePath = Path.Combine(Logger.DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
                loaded.General.MigrateTaskbarItems();
                return loaded;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取设置失败，使用默认设置", ex);
        }
        var fresh = new AppSettings();
        fresh.General.MigrateTaskbarItems();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error("保存设置失败", ex);
        }
    }
}

public sealed class GeneralSettings
{
    /// <summary>system / dark / light</summary>
    public string Theme { get; set; } = "system";

    /// <summary>通过计划任务始终以最高权限运行（配置一次后免 UAC）。</summary>
    public bool AlwaysRunAsAdmin { get; set; }
    public bool ShowFloatWindow { get; set; } = false;

    /// <summary>任务栏内嵌信息条（CPU/内存/网速）。</summary>
    public bool ShowTaskbarBar { get; set; } = false;

    /// <summary>信息栏显示哪些项（全部关闭则只保留普通托盘图标）。</summary>
    public bool TaskbarShowNet { get; set; } = true;

    /// <summary>旧版合并开关（CPU·内存 / 温度），仅用于升级迁移，不再直接使用。</summary>
    public bool TaskbarShowCpuMem { get; set; } = true;
    public bool TaskbarShowTemp { get; set; } = true;

    /// <summary>监控项逐项开关；null 表示旧存档尚未迁移，加载时由 <see cref="MigrateTaskbarItems"/> 按合并开关补齐。</summary>
    public bool? TaskbarShowCpu { get; set; }
    public bool? TaskbarShowMem { get; set; }
    public bool? TaskbarShowCpuTemp { get; set; }
    public bool? TaskbarShowGpuTemp { get; set; }
    public bool? TaskbarShowDiskTemp { get; set; }

    /// <summary>把旧版的合并开关拆成逐项开关，保留用户原有选择。</summary>
    public void MigrateTaskbarItems()
    {
        TaskbarShowCpu ??= TaskbarShowCpuMem;
        TaskbarShowMem ??= TaskbarShowCpuMem;
        TaskbarShowCpuTemp ??= TaskbarShowTemp;
        TaskbarShowGpuTemp ??= TaskbarShowTemp;
        TaskbarShowDiskTemp ??= TaskbarShowTemp;
    }
    public double? FloatX { get; set; }
    public double? FloatY { get; set; }
}

public sealed class HotkeySettings
{
    public string Screenshot { get; set; } = "Ctrl+Alt+A";
    public string ScreenshotOcr { get; set; } = "Ctrl+Alt+O";
    public string ScreenshotTranslate { get; set; } = "Ctrl+Alt+T";
    public string ColorPicker { get; set; } = "Ctrl+Alt+P";
    public string ClipboardHistory { get; set; } = "Ctrl+Alt+V";
    public string Launcher { get; set; } = "Alt+Space";
}

public sealed class ScreenshotSettings
{
    public bool AutoCopy { get; set; } = true;
    public bool AutoSaveFile { get; set; } = false;
    public string SaveDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VelvetTools");
}

/// <summary>OpenAI 兼容接口配置（也适用于各家中转/本地推理服务）。</summary>
public sealed class ApiProfile
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class OcrSettings
{
    /// <summary>windows = 系统本地离线 OCR；openai = OpenAI 兼容视觉接口。</summary>
    public string Provider { get; set; } = "windows";
    public ApiProfile OpenAi { get; set; } = new() { Model = "gpt-4o-mini" };
}

public sealed class TranslateSettings
{
    /// <summary>openai / deepl / baidu</summary>
    public string Provider { get; set; } = "openai";
    public string TargetLang { get; set; } = "zh";
    public ApiProfile OpenAi { get; set; } = new() { Model = "gpt-4o-mini" };
    public string DeepLApiKey { get; set; } = "";
    public bool DeepLUseFreeApi { get; set; } = true;
    public string BaiduAppId { get; set; } = "";
    public string BaiduSecret { get; set; } = "";
}

public sealed class ClipboardSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxItems { get; set; } = 200;
    public bool AutoPaste { get; set; } = true;
    public bool CaptureImages { get; set; } = true;
}

public sealed class ColorPickerSettings
{
    /// <summary>hex / rgb / hsl</summary>
    public string CopyFormat { get; set; } = "hex";
    public List<string> History { get; set; } = new();
}

/// <summary>一个 AI 服务商的配置（内置预设可被用户覆盖）。</summary>
public sealed class ChatProvider
{
    /// <summary>预设标识（qwen/kimi/doubao/deepseek/zhipu/openai/custom…），自定义时为 guid。</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    /// <summary>可选模型列表（逗号分隔展示用），Model 为当前选中项。</summary>
    public string Models { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>用户新增的自定义服务商（可删除）。</summary>
    public bool IsCustom { get; set; }
    /// <summary>Models 是否来自 /models 接口的真实拉取（false 说明是旧版残留，启动时清掉）。</summary>
    public bool ModelsFromApi { get; set; }
}

public sealed class WebSearchSettings
{
    /// <summary>duckduckgo（免密钥，默认）/ tavily / bing</summary>
    public string Provider { get; set; } = "duckduckgo";
    public int MaxResults { get; set; } = 5;
    public string TavilyApiKey { get; set; } = "";
    public string BingApiKey { get; set; } = "";
    /// <summary>默认是否对每条提问都联网（也可在对话窗单独开关）。</summary>
    public bool EnabledByDefault { get; set; }
}

public sealed class ChatSettings
{
    public List<ChatProvider> Providers { get; set; } = new();
    public WebSearchSettings WebSearch { get; set; } = new();
    /// <summary>当前使用的服务商 Id。</summary>
    public string ActiveProviderId { get; set; } = "";
    public string SystemPrompt { get; set; } = "你是一个乐于助人的助手，回答简洁准确。";
    public double Temperature { get; set; } = 0.7;
    /// <summary>随请求携带的历史消息条数（不含系统提示）；0 表示不限制。</summary>
    public int ContextMessages { get; set; } = 20;
    public bool Stream { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+C";

    /// <summary>建知识库用的嵌入模型（记住上次选择）。</summary>
    public string EmbedModel { get; set; } = "";
    /// <summary>检索返回的片段数。</summary>
    public int KnowledgeTopK { get; set; } = 5;
    /// <summary>稠密向量语义相似度下限（0~1）；高关键词覆盖可作为精确命中补回。</summary>
    public double KnowledgeMinScore { get; set; } = 0.25;
    /// <summary>当前对话默认使用的知识库 Id（空 = 不启用）。</summary>
    public string ActiveKnowledgeBaseId { get; set; } = "";

    /// <summary>内置服务商清单：只预置各家官方的 OpenAI 兼容端点地址，
    /// 不预置任何模型名——模型一律通过 /models 按密钥实际可用情况拉取。</summary>
    public static List<ChatProvider> BuiltinPresets() => new()
    {
        new ChatProvider
        {
            Id = "qwen", Name = "通义千问",
            BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Model = "",
        },
        new ChatProvider
        {
            Id = "kimi", Name = "Kimi（月之暗面）",
            BaseUrl = "https://api.moonshot.cn/v1",
            Model = "",
        },
        new ChatProvider
        {
            Id = "doubao", Name = "豆包（火山方舟）",
            BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
            Model = "",
        },
        new ChatProvider
        {
            Id = "deepseek", Name = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "",
        },
        new ChatProvider
        {
            Id = "zhipu", Name = "智谱 GLM",
            BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            Model = "",
        },
        new ChatProvider
        {
            Id = "siliconflow", Name = "硅基流动",
            BaseUrl = "https://api.siliconflow.cn/v1",
            Model = "",
        },
        new ChatProvider
        {
            Id = "openai", Name = "OpenAI 兼容接口",
            BaseUrl = "https://api.openai.com/v1",
            Model = "",
        },
    };

    /// <summary>
    /// 早期版本给每个服务商预填的默认模型。现在模型一律靠 /models 拉取，
    /// 这张表只用来认出旧存档里的残留值并清掉。
    /// </summary>
    private static readonly Dictionary<string, string> LegacyDefaultModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["qwen"] = "qwen-max",
        ["kimi"] = "moonshot-v1-8k",
        ["deepseek"] = "deepseek-chat",
        ["zhipu"] = "glm-4-flash",
        ["siliconflow"] = "deepseek-ai/DeepSeek-V3",
        ["openai"] = "gpt-4o-mini",
    };

    /// <summary>补齐缺失的内置预设（升级时新增的预设也能出现），保留用户已填写的密钥。</summary>
    public void EnsurePresets()
    {
        foreach (var preset in BuiltinPresets())
        {
            var existing = Providers.FirstOrDefault(p => p.Id == preset.Id);
            if (existing is null)
            {
                Providers.Add(preset);
            }
            else
            {
                // 官方端点/模型清单以最新预设为准，但不覆盖用户的密钥与所选模型
                existing.Name = preset.Name;
                if (string.IsNullOrWhiteSpace(existing.BaseUrl)) existing.BaseUrl = preset.BaseUrl;
                // 模型清单只来自 /models 接口，这里不回填任何默认值。
                // 旧版本曾把内置候选写进存档，没拉取过就清掉，
                // 避免用户看到"我没获取过却有一堆模型"的残留。
                if (!existing.ModelsFromApi)
                {
                    existing.Models = "";
                    // 只清掉和旧内置默认值一模一样的，手动填过的不动
                    if (LegacyDefaultModels.TryGetValue(existing.Id, out var legacy) &&
                        string.Equals(existing.Model, legacy, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Model = "";
                    }
                }
            }
        }
        if (string.IsNullOrWhiteSpace(ActiveProviderId) || Providers.All(p => p.Id != ActiveProviderId))
            ActiveProviderId = Providers.FirstOrDefault()?.Id ?? "";
    }

    public ChatProvider? Active => Providers.FirstOrDefault(p => p.Id == ActiveProviderId);
}

public sealed class SearchSettings
{
    /// <summary>Everything 未运行时是否提示安装。</summary>
    public bool PromptWhenMissing { get; set; } = true;
    public int MaxResults { get; set; } = 200;
    public bool MatchCase { get; set; }
    public bool MatchWholeWord { get; set; }
    public bool RegexMode { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+F";
}
