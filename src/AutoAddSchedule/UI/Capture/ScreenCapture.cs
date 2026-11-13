using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoAddSchedule.UI.Capture;

/// <summary>单台显示器的物理像素信息（来自 EnumDisplayMonitors / GetMonitorInfo）。</summary>
public sealed class MonitorSnapshot
{
    public required IntPtr Handle { get; init; }

    /// <summary>显示器在虚拟屏幕中的物理像素矩形。</summary>
    public required PhysicalRect Bounds { get; init; }

    public required bool IsPrimary { get; init; }

    public bool Contains(int x, int y) => Bounds.Contains(x, y);
}

/// <summary>
/// 一次整屏快照：虚拟屏幕位图（物理像素，Bgra32）+ 显示器列表。
/// 裁剪时物理坐标直接映射到位图内部坐标，保证多显示器/高 DPI 下不会错位。
/// </summary>
public sealed class ScreenSnapshot
{
    /// <summary>整个虚拟屏幕的位图，像素格式为 Bgra32，Alpha 已填充为 255。</summary>
    public required BitmapSource Image { get; init; }

    /// <summary>虚拟屏幕矩形（物理像素，Left/Top 可能为负）。</summary>
    public required PhysicalRect VirtualBounds { get; init; }

    /// <summary>全部显示器。</summary>
    public required IReadOnlyList<MonitorSnapshot> Monitors { get; init; }

    /// <summary>创建某台显示器的显示视图（不复制像素，供遮罩窗口使用）。</summary>
    public BitmapSource CreateMonitorView(MonitorSnapshot monitor)
    {
        var local = ToImageRect(monitor.Bounds).Intersect(new PhysicalRect(0, 0, Image.PixelWidth, Image.PixelHeight));
        if (local.IsEmpty)
        {
            return Image;
        }

        var view = new CroppedBitmap(Image, new Int32Rect(local.Left, local.Top, local.Width, local.Height));
        view.Freeze();
        return view;
    }

    /// <summary>
    /// 按物理像素矩形裁剪出独立位图（会复制像素，可安全地交给后台线程编码）。
    /// 坐标超出虚拟屏幕时自动收敛到有效范围内。
    /// </summary>
    public BitmapSource Crop(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("选区为空，无法裁剪截图。");
        }

        var local = ToImageRect(new PhysicalRect(left, top, width, height))
            .Intersect(new PhysicalRect(0, 0, Image.PixelWidth, Image.PixelHeight));
        if (local.IsEmpty)
        {
            throw new ArgumentException("选区超出了屏幕范围，无法裁剪截图。");
        }

        var view = new CroppedBitmap(Image, new Int32Rect(local.Left, local.Top, local.Width, local.Height));
        var stride = local.Width * 4;
        var buffer = new byte[stride * local.Height];
        view.CopyPixels(buffer, stride, 0);

        var copy = BitmapSource.Create(
            local.Width,
            local.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            buffer,
            stride);
        copy.Freeze();
        return copy;
    }

    /// <summary>查找包含指定物理坐标的显示器。</summary>
    public MonitorSnapshot? FindMonitor(int x, int y)
    {
        foreach (var monitor in Monitors)
        {
            if (monitor.Contains(x, y))
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>查找离指定物理坐标最近的显示器（用于坐标刚好落在屏幕边缘时）。</summary>
    public MonitorSnapshot? FindNearestMonitor(int x, int y)
    {
        MonitorSnapshot? best = null;
        var bestDistance = long.MaxValue;
        foreach (var monitor in Monitors)
        {
            var bounds = monitor.Bounds;
            var dx = x < bounds.Left ? bounds.Left - x : x > bounds.Right ? x - bounds.Right : 0;
            var dy = y < bounds.Top ? bounds.Top - y : y > bounds.Bottom ? y - bounds.Bottom : 0;
            var distance = (long)dx * dx + (long)dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = monitor;
            }
        }

        return best;
    }

    private PhysicalRect ToImageRect(PhysicalRect physical)
        => new(physical.Left - VirtualBounds.Left, physical.Top - VirtualBounds.Top, physical.Width, physical.Height);
}

/// <summary>
/// 屏幕捕获：使用 GDI BitBlt 抓取整个虚拟屏幕（物理像素），并枚举每台显示器的物理矩形。
/// 截图内容只包含屏幕像素，不包含鼠标指针与遮罩窗口。
/// </summary>
public static class ScreenCapture
{
    /// <summary>抓取当前整个虚拟屏幕。失败时抛出带中文说明的异常。</summary>
    public static ScreenSnapshot CaptureAll()
    {
        var left = CaptureNativeMethods.GetSystemMetrics(CaptureNativeMethods.SmXVirtualScreen);
        var top = CaptureNativeMethods.GetSystemMetrics(CaptureNativeMethods.SmYVirtualScreen);
        var width = CaptureNativeMethods.GetSystemMetrics(CaptureNativeMethods.SmCxVirtualScreen);
        var height = CaptureNativeMethods.GetSystemMetrics(CaptureNativeMethods.SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("无法获取屏幕尺寸（可能处于锁屏或没有可用的桌面）。");
        }

        var virtualBounds = new PhysicalRect(left, top, width, height);
        var monitors = EnumerateMonitors();
        if (monitors.Count == 0)
        {
            // 极端情况（例如远程会话枚举失败）下，把整个虚拟屏幕当成一台显示器，保证截图流程可用。
            monitors.Add(new MonitorSnapshot
            {
                Handle = IntPtr.Zero,
                Bounds = virtualBounds,
                IsPrimary = true
            });
        }

        var image = CaptureVirtualScreen(virtualBounds);
        return new ScreenSnapshot
        {
            Image = image,
            VirtualBounds = virtualBounds,
            Monitors = monitors
        };
    }

    private static List<MonitorSnapshot> EnumerateMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        CaptureNativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref CaptureNativeMethods.Rect rect, IntPtr data) =>
        {
            var info = new CaptureNativeMethods.MonitorInfo
            {
                Size = Marshal.SizeOf<CaptureNativeMethods.MonitorInfo>()
            };
            if (CaptureNativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new MonitorSnapshot
                {
                    Handle = hMonitor,
                    Bounds = new PhysicalRect(
                        info.Monitor.Left,
                        info.Monitor.Top,
                        info.Monitor.Right - info.Monitor.Left,
                        info.Monitor.Bottom - info.Monitor.Top),
                    IsPrimary = (info.Flags & CaptureNativeMethods.MonitorInfoPrimary) != 0
                });
            }

            return true;
        };

        if (!CaptureNativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "枚举显示器失败，无法确定截图区域。");
        }

        return monitors;
    }

    private static BitmapSource CaptureVirtualScreen(PhysicalRect bounds)
    {
        var screenDc = CaptureNativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "获取屏幕设备上下文失败，无法截图。");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CaptureNativeMethods.CreateCompatibleDC(screenDc);
            bitmap = CaptureNativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建截图位图失败。");
            }

            oldBitmap = CaptureNativeMethods.SelectObject(memoryDc, bitmap);
            var copied = CaptureNativeMethods.BitBlt(
                memoryDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                screenDc,
                bounds.Left,
                bounds.Top,
                CaptureNativeMethods.SrcCopy | CaptureNativeMethods.CaptureBlt);
            if (!copied)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "抓取屏幕像素失败（可能处于锁屏或安全桌面）。");
            }

            // GetDIBits 要求位图未选入设备上下文，先恢复原对象再读取像素。
            CaptureNativeMethods.SelectObject(memoryDc, oldBitmap);
            oldBitmap = IntPtr.Zero;

            var bufferSize = (long)bounds.Width * bounds.Height * 4;
            if (bufferSize > int.MaxValue)
            {
                throw new InvalidOperationException("屏幕尺寸过大，无法分配截图缓冲区。");
            }

            var info = new CaptureNativeMethods.BitmapInfo
            {
                Header = new CaptureNativeMethods.BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<CaptureNativeMethods.BitmapInfoHeader>(),
                    Width = bounds.Width,
                    Height = -bounds.Height, // 负数表示自上而下的像素行
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0 // BI_RGB
                }
            };

            var stride = bounds.Width * 4;
            var buffer = new byte[(int)bufferSize];
            if (CaptureNativeMethods.GetDIBits(memoryDc, bitmap, 0, (uint)bounds.Height, buffer, ref info, CaptureNativeMethods.DibRgbColors) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取屏幕像素失败。");
            }

            // BitBlt 产生的 Alpha 通道为 0，WPF 中会显示为全透明，这里统一填充为不透明。
            for (var i = 3; i < buffer.Length; i += 4)
            {
                buffer[i] = 255;
            }

            var source = BitmapSource.Create(
                bounds.Width,
                bounds.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                buffer,
                stride);
            source.Freeze();
            return source;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                CaptureNativeMethods.SelectObject(memoryDc, oldBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                CaptureNativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                CaptureNativeMethods.DeleteDC(memoryDc);
            }

            CaptureNativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
