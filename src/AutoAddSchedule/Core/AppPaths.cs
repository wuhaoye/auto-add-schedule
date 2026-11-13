using System.IO;

namespace AutoAddSchedule.Core;

/// <summary>
/// 应用使用的全部本地路径。配置、日程、日志存放在 %AppData%\AutoAddSchedule，
/// 截图存放在程序安装目录下的 screenshots\。
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "AutoAddSchedule";

    /// <summary>本地配置与数据目录：%AppData%\AutoAddSchedule</summary>
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string ConfigFilePath => Path.Combine(AppDataDirectory, "config.json");

    public static string SecretsFilePath => Path.Combine(AppDataDirectory, "secrets.dat");

    public static string SchedulesFilePath => Path.Combine(AppDataDirectory, "schedules.json");

    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");

    public static string LogFilePath => Path.Combine(LogDirectory, "app.log");

    /// <summary>程序安装目录（可执行文件所在目录）。</summary>
    public static string InstallDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>截图保存目录：程序安装目录下的 screenshots。</summary>
    public static string ScreenshotsDirectory => Path.Combine(InstallDirectory, "screenshots");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogDirectory);
        try
        {
            Directory.CreateDirectory(ScreenshotsDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Warn("创建截图目录失败：" + ex.Message);
        }
    }

    /// <summary>从程序目录开始逐级向上查找 .env 文件，最多向上 6 层。</summary>
    public static string? FindEnvFile()
    {
        try
        {
            var dir = new DirectoryInfo(InstallDirectory);
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // 查找失败不致命，忽略。
        }

        return null;
    }
}
