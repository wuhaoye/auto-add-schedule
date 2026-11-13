using System.IO;
using System.Text;

namespace AutoAddSchedule.Core;

/// <summary>轻量本地日志。写入 %AppData%\AutoAddSchedule\logs\app.log，超限自动轮转。</summary>
public static class AppLog
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void Init()
    {
        lock (Gate)
        {
            AppPaths.EnsureDirectories();
            _initialized = true;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : message + " | " + exception);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                if (!_initialized)
                {
                    AppPaths.EnsureDirectories();
                    _initialized = true;
                }

                RotateIfNeeded();
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine;
                File.AppendAllText(AppPaths.LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不得影响主流程。
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(AppPaths.LogFilePath);
        if (!file.Exists || file.Length < MaxFileBytes)
        {
            return;
        }

        for (var i = 2; i >= 1; i--)
        {
            var older = Path.Combine(AppPaths.LogDirectory, "app." + i + ".log");
            if (File.Exists(older))
            {
                File.Move(older, Path.Combine(AppPaths.LogDirectory, "app." + (i + 1) + ".log"), overwrite: true);
            }
        }

        File.Move(AppPaths.LogFilePath, Path.Combine(AppPaths.LogDirectory, "app.1.log"), overwrite: true);
    }
}
