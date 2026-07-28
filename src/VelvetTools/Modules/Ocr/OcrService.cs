using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VelvetTools.Common;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace VelvetTools.Modules.Ocr;

/// <summary>
/// OCR 服务：默认走 Windows 系统自带的离线 OCR（免费、无需配置，思路同
/// PowerToys Text Extractor / Text-Grab，均为 MIT）；可选切换 OpenAI 兼容视觉接口。
/// </summary>
public sealed class OcrService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<string> RecognizeAsync(Bitmap bmp)
    {
        var settings = App.Services.Settings.Ocr;
        return settings.Provider switch
        {
            "openai" => await RecognizeViaOpenAiAsync(bmp, settings.OpenAi),
            _ => await RecognizeViaWindowsAsync(bmp),
        };
    }

    // ---------- Windows 本地 OCR ----------
    private static async Task<string> RecognizeViaWindowsAsync(Bitmap bmp)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? TryLang("zh-Hans-CN") ?? TryLang("zh-CN") ?? TryLang("en-US");
        if (engine is null)
            throw new InvalidOperationException(
                "系统未安装 OCR 语言包。请到 设置 > 时间和语言 > 语言和区域 > 语言选项 中添加“光学字符识别”，或在 Velvet Tools 设置中改用 OpenAI 兼容接口。");

        using var scaled = EnsureWithinLimit(bmp, (int)OcrEngine.MaxImageDimension);
        var software = await ToSoftwareBitmapAsync(scaled ?? bmp);
        try
        {
            var result = await engine.RecognizeAsync(software);
            bool cjk = engine.RecognizerLanguage.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                       || engine.RecognizerLanguage.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (var line in result.Lines)
            {
                string text = cjk
                    ? string.Concat(line.Words.Select(w => w.Text))
                    : string.Join(' ', line.Words.Select(w => w.Text));
                sb.AppendLine(text);
            }
            return sb.ToString().TrimEnd();
        }
        finally { software.Dispose(); }

        static OcrEngine? TryLang(string tag)
        {
            try { return OcrEngine.TryCreateFromLanguage(new Language(tag)); }
            catch { return null; }
        }
    }

    /// <summary>Windows OCR 有最大边长限制，超出时等比缩小；返回 null 表示无需缩放。</summary>
    private static Bitmap? EnsureWithinLimit(Bitmap bmp, int maxDim)
    {
        int max = Math.Max(bmp.Width, bmp.Height);
        if (max <= maxDim) return null;
        double scale = (double)maxDim / max;
        return new Bitmap(bmp, (int)(bmp.Width * scale), (int)(bmp.Height * scale));
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    // ---------- OpenAI 兼容视觉接口 ----------
    private static async Task<string> RecognizeViaOpenAiAsync(Bitmap bmp, Settings.ApiProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
            throw new InvalidOperationException("尚未配置 OCR 接口密钥，请打开 设置 > OCR 与翻译 填写。");

        string base64 = Modules.Screenshot.CaptureService.ToBase64Png(bmp);
        var body = new
        {
            model = string.IsNullOrWhiteSpace(profile.Model) ? "gpt-4o-mini" : profile.Model,
            temperature = 0,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "识别图片中的所有文字，按原有排版输出纯文本。不要添加任何解释、前缀或代码块标记。" },
                        new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64}" } },
                    },
                },
            },
        };

        string url = profile.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {profile.ApiKey}");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OCR 接口返回 {(int)resp.StatusCode}：{Truncate(json)}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
