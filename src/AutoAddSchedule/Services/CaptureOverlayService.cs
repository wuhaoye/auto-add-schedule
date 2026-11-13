using System.Windows;
using AutoAddSchedule.Core;
using AutoAddSchedule.UI.Capture;

namespace AutoAddSchedule.Services;

/// <summary>
/// 截图选区界面：抓取整个虚拟屏幕 → 每台显示器一个遮罩窗口 → 拖选矩形 → 工具条/键盘操作
/// → 按物理像素裁剪出选区位图。实现类 Windows 截图的多显示器、高 DPI 体验。
/// </summary>
public sealed class CaptureOverlayService : ICaptureOverlayService
{
    public bool IsActive { get; private set; }

    public CaptureResult? ShowCapture()
    {
        if (IsActive)
        {
            AppLog.Warn("截图选区界面已经打开，忽略重复调用。");
            return null;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            AppLog.Warn("截图选区界面应在 UI 线程调用，已切换到 UI 线程。");
            try
            {
                return dispatcher.Invoke(ShowCapture);
            }
            catch (Exception ex)
            {
                AppLog.Error("切换到 UI 线程显示截图界面失败", ex);
                return null;
            }
        }

        IsActive = true;
        try
        {
            var session = new CaptureSession();
            return session.Run();
        }
        catch (Exception ex)
        {
            AppLog.Error("截图选区失败", ex);
            NotifyFailure(ex);
            return null;
        }
        finally
        {
            IsActive = false;
        }
    }

    private static void NotifyFailure(Exception ex)
    {
        try
        {
            var notifications = AppServices.Notifications;
            notifications?.Show("截图失败", ex.Message, NotificationKind.Error);
        }
        catch (Exception notifyError)
        {
            AppLog.Warn("显示截图失败通知时出错：" + notifyError.Message);
        }
    }
}
