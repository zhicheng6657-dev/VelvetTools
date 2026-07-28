using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VelvetTools.Common;
using VelvetTools.Settings;

namespace VelvetTools.Modules.Chat;

/// <summary>
/// AI 对话服务：统一走 OpenAI 兼容的 /chat/completions（千问、Kimi、豆包、DeepSeek、
/// 智谱、硅基流动等主流服务商均提供该协议端点），支持 SSE 流式输出。
/// 所有密钥由用户在设置中自行填写，应用不内置。
/// </summary>
public sealed class ChatService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public sealed record StreamDelta(string? Content, string? Reasoning);

    /// <summary>流式对话。onDelta 在后台线程回调，调用方负责切回 UI 线程。</summary>
    public async Task<ChatMessage> SendAsync(
        ChatProvider provider,
        IEnumerable<ChatMessage> history,
        string systemPrompt,
        double temperature,
        bool stream,
        Action<StreamDelta> onDelta,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw new InvalidOperationException($"尚未配置「{provider.Name}」的 API Key，请在 设置 → AI 对话 中填写。");
        if (string.IsNullOrWhiteSpace(provider.Model))
            throw new InvalidOperationException($"尚未选择「{provider.Name}」的模型。");

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });

        foreach (var m in history)
        {
            string role = m.Role switch { ChatRole.User => "user", ChatRole.Assistant => "assistant", _ => "system" };

            // 文档正文/检索结果只发给模型，界面上不显示
            string outgoing = string.IsNullOrEmpty(m.HiddenContext) ? m.Content : m.HiddenContext + m.Content;

            if (m.HasImages)
            {
                // 视觉消息：content 变成 [{type:text},{type:image_url}] 数组
                var parts = new List<object>();
                if (!string.IsNullOrWhiteSpace(outgoing))
                    parts.Add(new { type = "text", text = outgoing });

                foreach (var path in m.Images!)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        string mime = Path.GetExtension(path).ToLowerInvariant() switch
                        {
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".gif" => "image/gif",
                            ".webp" => "image/webp",
                            ".bmp" => "image/bmp",
                            _ => "image/png",
                        };
                        parts.Add(new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mime};base64,{Convert.ToBase64String(bytes)}" },
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"读取图片失败，已跳过：{path} —— {ex.Message}");
                    }
                }
                messages.Add(new { role, content = parts });
            }
            else
            {
                messages.Add(new { role, content = outgoing });
            }
        }

        var body = new
        {
            model = provider.Model,
            messages,
            temperature,
            stream,
        };

        string url = provider.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {provider.ApiKey}");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req,
            stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"{provider.Name} 返回 {(int)resp.StatusCode}：{Describe(err)}");
        }

        return stream
            ? await ReadStreamAsync(resp, onDelta, ct)
            : await ReadOnceAsync(resp, onDelta, ct);
    }

    private static async Task<ChatMessage> ReadOnceAsync(HttpResponseMessage resp, Action<StreamDelta> onDelta, CancellationToken ct)
    {
        string json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        string content = message.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        string? reasoning = message.TryGetProperty("reasoning_content", out var r) ? r.GetString() : null;
        onDelta(new StreamDelta(content, reasoning));

        var result = new ChatMessage { Role = ChatRole.Assistant, Content = content, Reasoning = reasoning };
        ReadUsage(doc.RootElement, result);
        return result;
    }

    /// <summary>读取 usage 字段（各家都遵循 OpenAI 的 prompt/completion_tokens 命名）。</summary>
    private static void ReadUsage(JsonElement root, ChatMessage target)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out int pt))
            target.PromptTokens = pt;
        if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out int ct))
            target.CompletionTokens = ct;
    }

    private static async Task<ChatMessage> ReadStreamAsync(HttpResponseMessage resp, Action<StreamDelta> onDelta, CancellationToken ct)
    {
        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        int? promptTokens = null, completionTokens = null;

        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(s, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string payload = line[5..].Trim();
            if (payload.Length == 0) continue;
            if (payload == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(payload);

                // 多数服务商在最后一个分片里带 usage
                if (doc.RootElement.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                {
                    if (u.TryGetProperty("prompt_tokens", out var pEl) && pEl.TryGetInt32(out int pv)) promptTokens = pv;
                    if (u.TryGetProperty("completion_tokens", out var cEl2) && cEl2.TryGetInt32(out int cv)) completionTokens = cv;
                }

                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;
                if (!choices[0].TryGetProperty("delta", out var delta)) continue;

                string? dc = delta.TryGetProperty("content", out var cEl) ? cEl.GetString() : null;
                string? dr = delta.TryGetProperty("reasoning_content", out var rEl) ? rEl.GetString() : null;

                if (!string.IsNullOrEmpty(dc)) content.Append(dc);
                if (!string.IsNullOrEmpty(dr)) reasoning.Append(dr);
                if (dc is not null || dr is not null) onDelta(new StreamDelta(dc, dr));
            }
            catch (JsonException)
            {
                // 忽略心跳/非法分片
            }
        }

        return new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = content.ToString(),
            Reasoning = reasoning.Length > 0 ? reasoning.ToString() : null,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
        };
    }

    /// <summary>把服务商的错误体压成一句人话。</summary>
    private static string Describe(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? raw;
                if (err.TryGetProperty("message", out var msg)) return msg.GetString() ?? raw;
            }
            if (doc.RootElement.TryGetProperty("message", out var m2)) return m2.GetString() ?? raw;
        }
        catch { }
        return raw.Length > 240 ? raw[..240] + "…" : raw;
    }

    /// <summary>测试连通性：优先用 /models（不消耗额度），失败再退回一次最短对话。</summary>
    public async Task<string> TestAsync(ChatProvider provider, CancellationToken ct = default)
    {
        try
        {
            var models = await FetchModelsAsync(provider, ct);
            if (models.Count > 0)
                return $"连接正常，可用对话模型 {models.Count} 个";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Info("测试连接：/models 不可用，改用最短对话验证 —— " + ex.Message);
        }

        var probe = new[] { new ChatMessage { Role = ChatRole.User, Content = "hi" } };
        var msg = await SendAsync(provider, probe, "", 0, stream: false, _ => { }, ct);
        return msg.Content.Length > 0 ? "连接正常" : "连接成功但返回为空";
    }

    /// <summary>
    /// 从服务商拉取模型列表（OpenAI 兼容的 GET /models）。
    /// 各家返回的 id 里混着 embedding / rerank / tts / 图像等非对话模型，这里按规则过滤。
    /// 拉不到时抛异常，由调用方决定是否回退到内置候选。
    /// </summary>
    public async Task<List<string>> FetchModelsAsync(ChatProvider provider, CancellationToken ct = default, bool includeEmbeddings = false)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw new InvalidOperationException("请先填写 API Key");

        string url = provider.BaseUrl.TrimEnd('/') + "/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {provider.ApiKey}");

        using var resp = await Http.SendAsync(req, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"获取模型列表失败（{(int)resp.StatusCode}）：{Describe(json)}");

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("返回格式不是 OpenAI 兼容的模型列表");

        var ids = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (includeEmbeddings || IsChatModel(id)) ids.Add(id);
        }

        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    /// <summary>排除明显不是对话模型的 id（各家命名不同，按关键词兜底）。</summary>
    private static bool IsChatModel(string id)
    {
        string s = id.ToLowerInvariant();
        string[] excluded =
        {
            "embedding", "embed", "rerank", "tts", "whisper", "speech", "audio", "voice",
            "moderation", "image", "dall-e", "stable-diffusion", "flux", "cogview", "wanx",
            "ocr", "video", "sora", "kolors", "bge-", "gte-", "text-similarity",
        };
        return !excluded.Any(k => s.Contains(k));
    }
}
