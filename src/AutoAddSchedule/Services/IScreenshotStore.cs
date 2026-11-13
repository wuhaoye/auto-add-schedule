using System.Windows.Media.Imaging;

namespace AutoAddSchedule.Services;

public interface IScreenshotStore
{
    /// <summary>截图目录（程序安装目录下的 screenshots）。</summary>
    string Directory { get; }

    /// <summary>保存一张截图，返回完整路径。</summary>
    Task<string> SaveAsync(BitmapSource image, CancellationToken cancellationToken = default);

    /// <summary>按保留数量清理旧截图，返回删除的文件数。</summary>
    int Cleanup();
}
