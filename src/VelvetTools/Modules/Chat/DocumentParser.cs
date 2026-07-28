using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using VelvetTools.Common;

namespace VelvetTools.Modules.Chat;

public sealed record ParsedDocument(
    string FileName,
    string Text,
    int CharCount,
    string Kind,
    int IndexedCharCount,
    bool IsTruncated)
{
    /// <summary>拼进对话上下文的格式（带文件名分隔，方便模型引用）。</summary>
    public string ToContextBlock() =>
        $"===== 文件：{FileName} =====\n{Text}\n===== 文件结束：{FileName} =====";
}

/// <summary>
/// 文档解析器：把用户拖入/选择的文件提取成纯文本，塞进对话上下文。
///
/// 依赖：PdfPig（Apache-2.0，PDF）、DocumentFormat.OpenXml（MIT，Word/Excel/PPT）。
/// 纯文本与代码文件直接按编码读取，无需第三方库。
/// 超长文档会被截断——上下文窗口有限，且大多数问题只需要开头部分。
/// </summary>
public static class DocumentParser
{
    /// <summary>单个文件提取的最大字符数（约等于 3 万汉字，够绝大多数场景）。</summary>
    public const int MaxCharsPerFile = 60_000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".ini", ".conf", ".cfg", ".toml", ".env", ".sql", ".html", ".htm", ".css", ".scss",
        ".cs", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".py", ".js", ".ts", ".jsx", ".tsx",
        ".go", ".rs", ".rb", ".php", ".swift", ".sh", ".bat", ".ps1", ".lua", ".r", ".m",
        ".vue", ".svelte", ".gradle", ".props", ".targets", ".csproj", ".sln", ".gitignore",
    };

    /// <summary>该扩展名是否支持解析（用于文件选择器过滤与拖放判定）。</summary>
    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pdf" or ".docx" or ".xlsx" or ".pptx" || TextExtensions.Contains(ext);
    }

    public const string FileDialogFilter =
        "支持的文档|*.pdf;*.docx;*.xlsx;*.pptx;*.txt;*.md;*.csv;*.json;*.xml;*.log;*.cs;*.py;*.js;*.ts;*.java;*.go;*.rs;*.html;*.css;*.sql|" +
        "PDF|*.pdf|Word|*.docx|Excel|*.xlsx|PowerPoint|*.pptx|文本与代码|*.txt;*.md;*.csv;*.json;*.xml;*.log;*.cs;*.py;*.js;*.ts|所有文件|*.*";

    /// <summary>按对话附件的默认上限解析文件。</summary>
    public static Task<ParsedDocument> ParseAsync(string path, CancellationToken ct = default) =>
        ParseAsync(path, MaxCharsPerFile, ct);

    /// <summary>
    /// 解析文件。知识库可以传入更高上限；普通对话仍保持 6 万字，避免一次请求撑爆上下文。
    /// 解析失败会抛出带原因的异常，由调用方提示用户。
    /// </summary>
    public static async Task<ParsedDocument> ParseAsync(string path, int maxChars, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("文件不存在", path);
        if (maxChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChars));

        var info = new FileInfo(path);
        if (info.Length > 60 * 1024 * 1024)
            throw new InvalidOperationException("文件超过 60MB，暂不支持");

        string name = Path.GetFileName(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();

        // 多取一小段才能判断是否被截断；各解析器在达到这个软上限后尽早停止，
        // 避免知识库导入大 PDF 时把整本书一次性留在内存。
        int extractionLimit = maxChars > int.MaxValue - Math.Max(16_384, maxChars / 5)
            ? int.MaxValue
            : maxChars + Math.Max(16_384, maxChars / 5);

        // 解析可能较慢（大 PDF），放后台线程
        var (text, kind) = await Task.Run(() => ext switch
        {
            ".pdf" => (ExtractPdf(path, extractionLimit, ct), "PDF"),
            ".docx" => (ExtractWord(path, extractionLimit, ct), "Word"),
            ".xlsx" => (ExtractExcel(path, extractionLimit, ct), "Excel"),
            ".pptx" => (ExtractPowerPoint(path, extractionLimit, ct), "PowerPoint"),
            _ => (ExtractPlainText(path, extractionLimit, ct), "文本"),
        }, ct);

        ct.ThrowIfCancellationRequested();
        text = text.Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("没能从文件中提取到文字（可能是扫描件或纯图片文档，可改用截图 OCR）");

        int original = text.Length;
        bool truncated = original > maxChars;
        int indexed = Math.Min(original, maxChars);
        if (truncated)
            text = text[..maxChars] + $"\n\n…（文档过长，本次索引到 {maxChars:N0} 字）";

        Logger.Info($"文档解析完成：{name}（{kind}，提取 {original} 字，使用 {indexed} 字）");
        return new ParsedDocument(name, text, original, kind, indexed, truncated);
    }

    // ==================== 各格式提取 ====================
    private static string ExtractPdf(string path, int limit, CancellationToken ct)
    {
        var sb = new StringBuilder();
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        int page = 0;
        foreach (var p in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            page++;
            sb.AppendLine($"--- 第 {page} 页 ---");
            sb.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p));
            sb.AppendLine();
            if (sb.Length > limit) break;
        }
        return sb.ToString();
    }

    private static string ExtractWord(string path, int limit, CancellationToken ct)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        var sb = new StringBuilder();
        foreach (var element in body.ChildElements)
        {
            ct.ThrowIfCancellationRequested();
            string text = element.InnerText;
            if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
            if (sb.Length > limit) break;
        }
        return sb.ToString();
    }

    private static string ExtractExcel(string path, int limit, CancellationToken ct)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null) return "";

        var shared = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sb = new StringBuilder();

        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            ct.ThrowIfCancellationRequested();
            if (sheet.Id?.Value is null) continue;
            var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
            var worksheet = part.Worksheet;
            if (worksheet is null) continue;
            sb.AppendLine($"--- 工作表：{sheet.Name} ---");

            foreach (var row in worksheet.Descendants<Row>())
            {
                ct.ThrowIfCancellationRequested();
                var cells = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    string value = cell.CellValue?.InnerText ?? "";
                    // 共享字符串表：单元格里存的是索引
                    if (cell.DataType?.Value == CellValues.SharedString
                        && shared is not null && int.TryParse(value, out int idx)
                        && idx >= 0 && idx < shared.ChildElements.Count)
                    {
                        value = shared.ChildElements[idx].InnerText;
                    }
                    cells.Add(value);
                }
                if (cells.Any(c => c.Length > 0))
                    sb.AppendLine(string.Join("\t", cells));

                if (sb.Length > limit) break;
            }
            sb.AppendLine();
            if (sb.Length > limit) break;
        }
        return sb.ToString();
    }

    private static string ExtractPowerPoint(string path, int limit, CancellationToken ct)
    {
        using var doc = PresentationDocument.Open(path, false);
        var parts = doc.PresentationPart?.SlideParts;
        if (parts is null) return "";

        var sb = new StringBuilder();
        int index = 0;
        foreach (var slide in parts)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            sb.AppendLine($"--- 第 {index} 页幻灯片 ---");
            var slideContent = slide.Slide;
            if (slideContent is null) continue;
            var texts = slideContent.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t));
            foreach (var t in texts) sb.AppendLine(t);
            sb.AppendLine();
            if (sb.Length > limit) break;
        }
        return sb.ToString();
    }

    private static string ExtractPlainText(string path, int limit, CancellationToken ct)
    {
        // 严格按 UTF-8 流式读取，遇到无效字节再回退 GBK。只读到软上限，
        // 避免 60MB 日志文件先整体展开成上百 MB UTF-16 字符串再截断。
        try
        {
            return ReadText(path, new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true), limit, ct);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return ReadText(path, Encoding.GetEncoding("GBK"), limit, ct);
        }
    }

    private static string ReadText(string path, Encoding encoding, int limit, CancellationToken ct)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        var sb = new StringBuilder(Math.Min(limit, 256_000));
        var buffer = new char[8192];
        while (sb.Length < limit)
        {
            ct.ThrowIfCancellationRequested();
            int count = reader.Read(buffer, 0, Math.Min(buffer.Length, limit - sb.Length));
            if (count <= 0) break;
            sb.Append(buffer, 0, count);
        }
        return sb.ToString();
    }
}
