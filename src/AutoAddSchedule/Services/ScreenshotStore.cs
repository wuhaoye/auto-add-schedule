using System.IO;
using System.Windows.Media.Imaging;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>
/// 截图落盘与保留策略：图片保存为 PNG（文件名 yyyyMMdd_HHmmss_fff.png）到程序目录 screenshots\，
/// 并按配置的保留数量删除最旧的截图。默认构造函数读取 AppServices 中的配置，测试可注入目录与数量。
/// </summary>
public sealed class ScreenshotStore : IScreenshotStore
{
    /// <summary>默认保留数量（配置文件缺失或读取失败时使用）。</summary>
    private const int DefaultRetention = 60;

    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

    private readonly Func<int> _retentionProvider;

    public ScreenshotStore()
        : this(AppPaths.ScreenshotsDirectory, GetConfiguredRetention)
    {
    }

    public ScreenshotStore(string directory, int retention)
        : this(directory, () => retention)
    {
    }

    public ScreenshotStore(string directory, Func<int> retentionProvider)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("截图目录不能为空。", nameof(directory));
        }

        Directory = directory;
        _retentionProvider = retentionProvider ?? throw new ArgumentNullException(nameof(retentionProvider));
    }

    public string Directory { get; }

    public Task<string> SaveAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        // 冻结后位图可以安全地交给后台线程编码（截图界面返回的位图本身已冻结）。
        if (!image.IsFrozen && image.CanFreeze)
        {
            image.Freeze();
        }

        return Task.Run(() => Save(image, cancellationToken), cancellationToken);
    }

    public int Cleanup()
    {
        try
        {
            var directory = new DirectoryInfo(Directory);
            if (!directory.Exists)
            {
                return 0;
            }

            var files = directory.GetFiles()
                .Where(file => SupportedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();

            var keep = GetRetention();
            if (files.Count <= keep)
            {
                return 0;
            }

            var deleted = 0;
            foreach (var file in files.Skip(keep))
            {
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("删除旧截图失败：" + file.FullName + " | " + ex.Message);
                }
            }

            if (deleted > 0)
            {
                AppLog.Info("已清理 " + deleted + " 张旧截图，保留最近 " + keep + " 张。");
            }

            return deleted;
        }
        catch (Exception ex)
        {
            AppLog.Error("清理旧截图失败", ex);
            return 0;
        }
    }

    private string Save(BitmapSource image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception ex)
        {
            var message = "无法写入截图目录：" + Directory + "。请检查程序目录是否可写（可能需要管理员权限）。";
            AppLog.Error(message, ex);
            throw new IOException(message, ex);
        }

        var path = BuildUniquePath();
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(path);
            var message = "保存截图失败：" + path + "。" + ex.Message;
            AppLog.Error(message, ex);
            throw new IOException(message, ex);
        }

        AppLog.Info("截图已保存：" + path);
        return path;
    }

    /// <summary>生成不重名的文件路径，保持 yyyyMMdd_HHmmss_fff.png 命名。</summary>
    private string BuildUniquePath()
    {
        var path = BuildPath(DateTime.Now);
        for (var i = 0; i < 20 && File.Exists(path); i++)
        {
            // 同一毫秒内连续保存时，等到时间戳变化，避免出现不符合命名的后缀。
            Thread.Sleep(2);
            path = BuildPath(DateTime.Now);
        }

        if (!File.Exists(path))
        {
            return path;
        }

        // 极端情况（例如系统时间被回拨）下加序号兜底，绝不覆盖已有截图。
        return Path.Combine(
            Directory,
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6] + ".png");
    }

    private string BuildPath(DateTime timestamp)
        => Path.Combine(Directory, timestamp.ToString("yyyyMMdd_HHmmss_fff") + ".png");

    private int GetRetention()
    {
        int keep;
        try
        {
            keep = _retentionProvider();
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取截图保留数量失败，使用默认值 " + DefaultRetention + "：" + ex.Message);
            keep = DefaultRetention;
        }

        // 至少保留 1 张，避免把截图全部删光。
        return Math.Max(1, keep);
    }

    /// <summary>默认保留数量：来自配置文件，未初始化或读取失败时用 60。</summary>
    private static int GetConfiguredRetention()
    {
        try
        {
            var configured = AppServices.Config.Config.General.ScreenshotRetention;
            return configured > 0 ? configured : DefaultRetention;
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取截图保留数量失败，使用默认值 " + DefaultRetention + "：" + ex.Message);
            return DefaultRetention;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理残留文件失败不影响向用户报错。
        }
    }
}
