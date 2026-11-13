using System.Windows.Media.Imaging;

namespace AutoAddSchedule.Services;

/// <summary>用户在截图上完成框选后的结果。</summary>
public sealed class CaptureResult
{
    /// <summary>裁剪后的选区图像（物理像素）。</summary>
    public required BitmapSource Image { get; init; }

    /// <summary>用户填写的补充信息（可为空）。</summary>
    public string? Supplement { get; init; }
}

public interface ICaptureOverlayService
{
    bool IsActive { get; }

    /// <summary>
    /// 在 UI 线程显示截图选区界面并阻塞等待用户操作（内部消息循环）。
    /// 用户放弃时返回 null。
    /// </summary>
    CaptureResult? ShowCapture();
}
