namespace AutoAddSchedule.Services;

/// <summary>截图选区界面。由截图模块实现。</summary>
public sealed class CaptureOverlayService : ICaptureOverlayService
{
    public bool IsActive { get; private set; }

    public CaptureResult? ShowCapture()
    {
        // 待截图模块实现；当前返回 null 表示用户放弃。
        return null;
    }
}
