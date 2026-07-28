using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VelvetTools.Common;

namespace VelvetTools.Modules.Chat;

public enum ChatRole { System, User, Assistant }

public sealed class ChatMessage
{
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    /// <summary>推理模型的思维链（如 deepseek-reasoner），单独折叠展示。</summary>
    public string? Reasoning { get; set; }

    /// <summary>随消息发送的图片（本地路径），走视觉模型的 image_url 传参。</summary>
    public List<string>? Images { get; set; }

    /// <summary>随消息带上的文档文件名（仅用于气泡展示）。</summary>
    public List<string>? Attachments { get; set; }

    /// <summary>联网检索用到的来源，格式 "标题|网址"（仅用于气泡展示）。</summary>
    public List<string>? WebSources { get; set; }

    /// <summary>知识库命中的片段描述（仅用于气泡展示）。</summary>
    public List<string>? KnowledgeSources { get; set; }

    /// <summary>
    /// 只发给模型、不在界面显示的前置内容（文档正文、联网检索结果）。
    /// 分开存是为了让气泡里只显示用户原话，几万字的文档不刷屏。
    /// </summary>
    public string? HiddenContext { get; set; }

    /// <summary>本次回复消耗的 token（服务商返回 usage 时填充）。</summary>
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    [JsonIgnore] public bool IsUser => Role == ChatRole.User;
    [JsonIgnore] public bool HasImages => Images is { Count: > 0 };
}

public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新对话";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
    public string ProviderId { get; set; } = "";
    public string Model { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>用首条用户消息自动命名。</summary>
    public void AutoTitle()
    {
        if (Title != "新对话") return;
        var first = Messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Content;
        if (string.IsNullOrWhiteSpace(first)) return;
        var line = first.Replace('\n', ' ').Trim();
        Title = line.Length > 20 ? line[..20] + "…" : line;
    }
}

/// <summary>对话历史本地存储（%AppData%\VelvetTools\chats.json）。</summary>
public sealed class ChatStore
{
    private static readonly string FilePath = Path.Combine(Logger.DataDir, "chats.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public List<ChatSession> Sessions { get; private set; } = new();

    public ChatStore() => Load();

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Sessions = JsonSerializer.Deserialize<List<ChatSession>>(File.ReadAllText(FilePath), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            Logger.Error("读取对话历史失败", ex);
            Sessions = new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // 只保留最近 100 个会话，避免无限增长
            var trimmed = Sessions.OrderByDescending(s => s.Updated).Take(100).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(trimmed, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error("保存对话历史失败", ex);
        }
    }

    public ChatSession NewSession(string providerId, string model)
    {
        var session = new ChatSession { ProviderId = providerId, Model = model };
        Sessions.Insert(0, session);
        return session;
    }

    public void Delete(ChatSession session)
    {
        Sessions.Remove(session);
        Save();
    }

    public void Clear()
    {
        Sessions.Clear();
        Save();
    }
}
