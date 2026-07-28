using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using VelvetTools.Common;
using VelvetTools.Common.Interop;

namespace VelvetTools.Modules.Clipboard;

public enum ClipType { Text, Image, Files }

public sealed class ClipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipType Type { get; set; }
    public string? Text { get; set; }
    public string? ImageFile { get; set; }
    public List<string>? Files { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }

    public string Preview => Type switch
    {
        ClipType.Text => (Text ?? "").Length > 300 ? Text![..300] : Text ?? "",
        ClipType.Image => "[图片]",
        _ => string.Join("  ", (Files ?? new()).Select(Path.GetFileName)),
    };
}

/// <summary>
/// 剪贴板历史：监听 WM_CLIPBOARDUPDATE，记录文本/图片/文件，本地 JSON + PNG 存储。
/// 尊重 "Clipboard Viewer Ignore" 排除格式（密码管理器复制的内容不会入库）。
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private static readonly string StoreDir = Path.Combine(Logger.DataDir, "clipboard");
    private static readonly string StoreFile = Path.Combine(StoreDir, "entries.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly MessageWindow _window;
    private readonly System.Windows.Threading.DispatcherTimer _saveDebounce;
    private bool _listening;
    private DateTime _suppressUntil = DateTime.MinValue;

    public List<ClipEntry> Entries { get; } = new();
    public event Action? Changed;

    public ClipboardService(MessageWindow window)
    {
        _window = window;
        Directory.CreateDirectory(StoreDir);
        LoadEntries();

        _saveDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800),
        };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveEntries(); };

        _window.AddHook((msg, _, _) =>
        {
            if (msg != Native.WM_CLIPBOARDUPDATE) return false;
            OnClipboardUpdate();
            return true;
        });

        if (App.Services.Settings.Clipboard.Enabled)
            Start();
    }

    public void Start()
    {
        if (_listening) return;
        _listening = Native.AddClipboardFormatListener(_window.Handle);
    }

    public void Stop()
    {
        if (!_listening) return;
        Native.RemoveClipboardFormatListener(_window.Handle);
        _listening = false;
    }

    private void OnClipboardUpdate()
    {
        if (!App.Services.Settings.Clipboard.Enabled) return;
        if (DateTime.Now < _suppressUntil) return; // 自己写入剪贴板导致的事件

        try
        {
            IDataObject? data = null;
            for (int i = 0; i < 3 && data is null; i++)
            {
                try { data = System.Windows.Clipboard.GetDataObject(); }
                catch { Thread.Sleep(50); }
            }
            if (data is null) return;

            // 密码管理器等敏感来源的标准排除格式
            var formats = data.GetFormats(false);
            if (formats.Any(f => f is "Clipboard Viewer Ignore" or "ExcludeClipboardContentFromMonitorProcessing"))
                return;

            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    AddEntry(new ClipEntry { Type = ClipType.Files, Files = files.ToList() });
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                string text = (data.GetData(DataFormats.UnicodeText) as string) ?? "";
                if (string.IsNullOrWhiteSpace(text)) return;
                if (text.Length > 200_000) text = text[..200_000];
                AddEntry(new ClipEntry { Type = ClipType.Text, Text = text });
            }
            else if (App.Services.Settings.Clipboard.CaptureImages && System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is null) return;
                string file = Path.Combine(StoreDir, $"img_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                using (var fs = File.Create(file))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(fs);
                }
                AddEntry(new ClipEntry { Type = ClipType.Image, ImageFile = file });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取剪贴板失败", ex);
        }
    }

    private void AddEntry(ClipEntry entry)
    {
        // 与最近一条文本重复 → 移到最前
        if (entry.Type == ClipType.Text)
        {
            var dup = Entries.FirstOrDefault(e => e.Type == ClipType.Text && e.Text == entry.Text);
            if (dup is not null)
            {
                Entries.Remove(dup);
                dup.Time = DateTime.Now;
                Entries.Insert(0, dup);
                ScheduleSave();
                Changed?.Invoke();
                return;
            }
        }

        Entries.Insert(0, entry);
        EnforceLimit();
        ScheduleSave();
        Changed?.Invoke();
    }

    private void EnforceLimit()
    {
        int max = Math.Max(20, App.Services.Settings.Clipboard.MaxItems);
        while (Entries.Count > max)
        {
            var victim = Entries.LastOrDefault(e => !e.Pinned) ?? Entries[^1];
            Entries.Remove(victim);
            if (victim.ImageFile is not null)
            {
                try { File.Delete(victim.ImageFile); } catch { }
            }
        }
    }

    public void Delete(ClipEntry entry)
    {
        Entries.Remove(entry);
        if (entry.ImageFile is not null)
        {
            try { File.Delete(entry.ImageFile); } catch { }
        }
        ScheduleSave();
        Changed?.Invoke();
    }

    public void TogglePin(ClipEntry entry)
    {
        entry.Pinned = !entry.Pinned;
        ScheduleSave();
        Changed?.Invoke();
    }

    public void Clear(bool keepPinned = true)
    {
        var removed = Entries.Where(e => !keepPinned || !e.Pinned).ToList();
        foreach (var e in removed)
        {
            Entries.Remove(e);
            if (e.ImageFile is not null)
            {
                try { File.Delete(e.ImageFile); } catch { }
            }
        }
        ScheduleSave();
        Changed?.Invoke();
    }

    /// <summary>把条目写回剪贴板（不触发自采集）。</summary>
    public bool SetClipboard(ClipEntry entry)
    {
        _suppressUntil = DateTime.Now.AddMilliseconds(600);
        try
        {
            switch (entry.Type)
            {
                case ClipType.Text:
                    System.Windows.Clipboard.SetText(entry.Text ?? "");
                    break;
                case ClipType.Image when entry.ImageFile is not null && File.Exists(entry.ImageFile):
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(entry.ImageFile);
                    img.EndInit();
                    img.Freeze();
                    System.Windows.Clipboard.SetImage(img);
                    break;
                case ClipType.Files when entry.Files is not null:
                    var col = new System.Collections.Specialized.StringCollection();
                    col.AddRange(entry.Files.Where(File.Exists).Concat(entry.Files.Where(Directory.Exists)).ToArray());
                    if (col.Count == 0) return false;
                    System.Windows.Clipboard.SetFileDropList(col);
                    break;
                default:
                    return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("写入剪贴板失败", ex);
            return false;
        }
    }

    /// <summary>回贴：写剪贴板 → 还原前台窗口 → 模拟 Ctrl+V。</summary>
    public void Paste(ClipEntry entry, IntPtr targetWindow)
    {
        if (!SetClipboard(entry)) return;
        if (targetWindow != IntPtr.Zero)
        {
            Native.SetForegroundWindow(targetWindow);
            Thread.Sleep(120);
        }
        if (App.Services.Settings.Clipboard.AutoPaste)
            Native.SendCtrlV();
    }

    private void ScheduleSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void LoadEntries()
    {
        try
        {
            if (!File.Exists(StoreFile)) return;
            var list = JsonSerializer.Deserialize<List<ClipEntry>>(File.ReadAllText(StoreFile), JsonOpts);
            if (list is not null)
            {
                Entries.AddRange(list.Where(e =>
                    e.Type != ClipType.Image || (e.ImageFile is not null && File.Exists(e.ImageFile))));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("读取剪贴板历史失败", ex);
        }
    }

    private void SaveEntries()
    {
        try
        {
            File.WriteAllText(StoreFile, JsonSerializer.Serialize(Entries, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error("保存剪贴板历史失败", ex);
        }
    }

    public void Dispose()
    {
        Stop();
        SaveEntries();
    }
}
