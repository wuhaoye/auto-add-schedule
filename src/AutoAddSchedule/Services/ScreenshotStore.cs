using System.IO;
using System.Windows.Media.Imaging;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Services;

/// <summary>截图落盘与保留策略。由截图模块实现。</summary>
public sealed class ScreenshotStore : IScreenshotStore
{
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

    public string Directory => AppPaths.ScreenshotsDirectory;

    public Task<string> SaveAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                System.IO.Directory.CreateDirectory(Directory);
                var fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                var path = System.IO.Path.Combine(Directory, fileName);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(path))
                {
                    encoder.Save(stream);
                }

                AppLog.Info("截图已保存：" + path);
                return path;
            },
            cancellationToken);
    }

    public int Cleanup() => 0;
}
