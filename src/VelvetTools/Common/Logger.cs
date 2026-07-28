using System.IO;

namespace VelvetTools.Common;

/// <summary>简易文件日志：%AppData%\VelvetTools\logs\app.log。</summary>
public static class Logger
{
    private static readonly object Lock = new();

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VelvetTools");

    public static string LogFile { get; } = Path.Combine(DataDir, "logs", "app.log");

    public static void Init()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
        try
        {
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 1_000_000)
                File.Move(LogFile, Path.ChangeExtension(LogFile, ".old.log"), overwrite: true);
        }
        catch { }
    }

    public static void Info(string msg) => Append("INFO", msg);
    public static void Warn(string msg) => Append("WARN", msg);
    public static void Error(string msg, Exception? ex = null)
        => Append("ERR ", ex is null ? msg : $"{msg} :: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Append(string level, string msg)
    {
        try
        {
            lock (Lock)
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
