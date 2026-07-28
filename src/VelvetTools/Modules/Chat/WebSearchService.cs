using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using VelvetTools.Common;
using VelvetTools.Settings;

namespace VelvetTools.Modules.Chat;

public sealed record WebResult(string Title, string Url, string Snippet);

/// <summary>
/// 联网搜索：给模型补充实时信息。
///
/// 默认用 DuckDuckGo 的 HTML 端点（**无需任何 API Key**，开箱即用），
/// 可选切换到 Tavily / Bing（需用户自备密钥，结果质量更好、更稳定）。
/// 搜索结果以"检索到的网页摘要"形式拼进本轮提问，不修改历史消息。
/// </summary>
public sealed class WebSearchService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    static WebSearchService()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
    }

    public async Task<List<WebResult>> SearchAsync(string query, WebSearchSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();

        return settings.Provider switch
        {
            "tavily" => await SearchTavilyAsync(query, settings, ct),
            "bing" => await SearchBingAsync(query, settings, ct),
            _ => await SearchDuckDuckGoAsync(query, settings.MaxResults, ct),
        };
    }

    // ==================== DuckDuckGo（免密钥） ====================
    /// <summary>
    /// 走 html.duckduckgo.com 的无脚本端点并解析结果块。
    /// 这是页面结构解析，DuckDuckGo 改版时可能失效 —— 失败会抛出提示，
    /// 用户可在设置里换成 Tavily/Bing。
    /// </summary>
    private static async Task<List<WebResult>> SearchDuckDuckGoAsync(string query, int max, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://html.duckduckgo.com/html/")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = query,
                ["kl"] = "cn-zh",
            }),
        };

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync(ct);

        var results = new List<WebResult>();

        // 结果块：<a class="result__a" href="...">标题</a> … <a class="result__snippet">摘要</a>
        var linkPattern = new Regex(
            "<a[^>]+class=\"result__a\"[^>]+href=\"(?<href>[^\"]+)\"[^>]*>(?<title>.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var snippetPattern = new Regex(
            "class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var links = linkPattern.Matches(html);
        var snippets = snippetPattern.Matches(html);

        for (int i = 0; i < links.Count && results.Count < max; i++)
        {
            string href = HttpUtility.HtmlDecode(links[i].Groups["href"].Value);
            // DuckDuckGo 会把真实地址包在 /l/?uddg= 跳转里
            var real = Regex.Match(href, @"uddg=([^&]+)");
            if (real.Success) href = HttpUtility.UrlDecode(real.Groups[1].Value);
            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

            string title = StripHtml(links[i].Groups["title"].Value);
            string snippet = i < snippets.Count ? StripHtml(snippets[i].Groups["snippet"].Value) : "";
            if (title.Length == 0) continue;

            results.Add(new WebResult(title, href, snippet));
        }

        if (results.Count == 0)
            throw new InvalidOperationException("没有解析到搜索结果（DuckDuckGo 可能改版或被网络拦截），可在设置里换用 Tavily / Bing");

        return results;
    }

    // ==================== Tavily（需密钥，专为 LLM 设计） ====================
    private static async Task<List<WebResult>> SearchTavilyAsync(string query, WebSearchSettings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.TavilyApiKey))
            throw new InvalidOperationException("尚未配置 Tavily API Key");

        var body = new
        {
            api_key = s.TavilyApiKey,
            query,
            max_results = s.MaxResults,
            search_depth = "basic",
            include_answer = false,
        };

        using var resp = await Http.PostAsync("https://api.tavily.com/search",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Tavily 返回 {(int)resp.StatusCode}：{Truncate(json)}");

        using var doc = JsonDocument.Parse(json);
        var results = new List<WebResult>();
        if (doc.RootElement.TryGetProperty("results", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                results.Add(new WebResult(
                    item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : ""));
            }
        }
        return results;
    }

    // ==================== Bing Web Search（需 Azure 密钥） ====================
    private static async Task<List<WebResult>> SearchBingAsync(string query, WebSearchSettings s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.BingApiKey))
            throw new InvalidOperationException("尚未配置 Bing 搜索密钥");

        string url = $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count={s.MaxResults}&mkt=zh-CN";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", s.BingApiKey);

        using var resp = await Http.SendAsync(req, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Bing 返回 {(int)resp.StatusCode}：{Truncate(json)}");

        using var doc = JsonDocument.Parse(json);
        var results = new List<WebResult>();
        if (doc.RootElement.TryGetProperty("webPages", out var pages)
            && pages.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                results.Add(new WebResult(
                    item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    item.TryGetProperty("snippet", out var sn) ? sn.GetString() ?? "" : ""));
            }
        }
        return results;
    }

    // ==================== 拼装成给模型的上下文 ====================
    /// <summary>把搜索结果拼成模型可用的检索上下文，并要求其标注引用来源。</summary>
    public static string BuildContext(string query, List<WebResult> results)
    {
        if (results.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine($"以下是针对「{query}」的联网检索结果（{DateTime.Now:yyyy-MM-dd HH:mm}）：");
        sb.AppendLine();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.Title}");
            sb.AppendLine($"    来源：{r.Url}");
            if (!string.IsNullOrWhiteSpace(r.Snippet))
                sb.AppendLine($"    摘要：{Collapse(r.Snippet)}");
            sb.AppendLine();
        }
        sb.AppendLine("请基于以上结果回答用户的问题；引用具体信息时用 [序号] 标注来源。" +
                      "如果检索结果与问题无关或不足以回答，请如实说明并给出你自己的判断。");
        return sb.ToString();
    }

    private static string StripHtml(string html) =>
        HttpUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")).Trim();

    private static string Collapse(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 300 ? s[..300] + "…" : s;
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] + "…" : s;
}
