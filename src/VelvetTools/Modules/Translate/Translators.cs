using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VelvetTools.Settings;

namespace VelvetTools.Modules.Translate;

/// <summary>
/// 翻译服务：OpenAI 兼容 / DeepL / 百度翻译。所有密钥由用户在设置中自行填写，
/// 应用不内置、不上传任何密钥。
/// </summary>
public sealed class TranslateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public static readonly (string Code, string Name)[] Languages =
    {
        ("zh", "中文"), ("en", "英语"), ("ja", "日语"), ("ko", "韩语"),
        ("fr", "法语"), ("de", "德语"), ("es", "西班牙语"), ("ru", "俄语"),
    };

    public async Task<string> TranslateAsync(string text, string? targetLang = null)
    {
        var s = App.Services.Settings.Translate;
        targetLang ??= s.TargetLang;
        return s.Provider switch
        {
            "deepl" => await ViaDeepLAsync(text, targetLang, s),
            "baidu" => await ViaBaiduAsync(text, targetLang, s),
            _ => await ViaOpenAiAsync(text, targetLang, s.OpenAi),
        };
    }

    // ---------- OpenAI 兼容 ----------
    private static async Task<string> ViaOpenAiAsync(string text, string lang, ApiProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
            throw new InvalidOperationException("尚未配置翻译接口密钥，请打开 设置 > OCR 与翻译 填写。");

        string langName = Languages.FirstOrDefault(l => l.Code == lang).Name ?? "中文";
        var body = new
        {
            model = string.IsNullOrWhiteSpace(profile.Model) ? "gpt-4o-mini" : profile.Model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = $"你是专业翻译引擎。把用户给出的文本翻译成{langName}，只输出译文，保留原有换行，不要任何解释。" },
                new { role = "user", content = text },
            },
        };

        string url = profile.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"翻译接口返回 {(int)resp.StatusCode}：{Truncate(json)}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
    }

    // ---------- DeepL ----------
    private static async Task<string> ViaDeepLAsync(string text, string lang, TranslateSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.DeepLApiKey))
            throw new InvalidOperationException("尚未配置 DeepL API Key。");

        string host = s.DeepLUseFreeApi ? "https://api-free.deepl.com" : "https://api.deepl.com";
        string target = lang.ToUpperInvariant() switch { "ZH" => "ZH", "JA" => "JA", "KO" => "KO", var x => x };

        using var req = new HttpRequestMessage(HttpMethod.Post, host + "/v2/translate");
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {s.DeepLApiKey}");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = text,
            ["target_lang"] = target,
        });

        using var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DeepL 返回 {(int)resp.StatusCode}：{Truncate(json)}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString()?.Trim() ?? "";
    }

    // ---------- 百度翻译 ----------
    private static async Task<string> ViaBaiduAsync(string text, string lang, TranslateSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.BaiduAppId) || string.IsNullOrWhiteSpace(s.BaiduSecret))
            throw new InvalidOperationException("尚未配置百度翻译 APP ID / 密钥。");

        string to = lang switch { "ja" => "jp", "ko" => "kor", "fr" => "fra", "es" => "spa", var x => x };
        string salt = Random.Shared.Next(100000, 999999).ToString();
        string sign = Md5Hex(s.BaiduAppId + text + salt + s.BaiduSecret);

        string url = "https://fanyi-api.baidu.com/api/trans/vip/translate" +
                     $"?q={Uri.EscapeDataString(text)}&from=auto&to={to}&appid={s.BaiduAppId}&salt={salt}&sign={sign}";

        using var resp = await Http.GetAsync(url);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error_code", out var err))
        {
            string msg = doc.RootElement.TryGetProperty("error_msg", out var m) ? m.GetString() ?? "" : "";
            throw new InvalidOperationException($"百度翻译错误 {err}：{msg}");
        }

        var sb = new StringBuilder();
        foreach (var item in doc.RootElement.GetProperty("trans_result").EnumerateArray())
            sb.AppendLine(item.GetProperty("dst").GetString());
        return sb.ToString().TrimEnd();
    }

    private static string Md5Hex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
