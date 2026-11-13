using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;
using AutoAddSchedule.UI.Dialogs;

namespace AutoAddSchedule.UI.Capture;

/// <summary>
/// 一次截图会话：负责抓屏、创建各显示器遮罩窗口、维护选区状态机、处理工具条/键盘操作，
/// 并用嵌套 Dispatcher 消息循环同步阻塞到用户确定或放弃。
/// </summary>
internal sealed class CaptureSession
{
    /// <summary>小于该尺寸的拖动视为误触，不产生选区（物理像素）。</summary>
    private const int MinimumSelectionPixels = 4;

    private readonly List<CaptureOverlayWindow> _windows = new();
    private ScreenSnapshot? _snapshot;
    private DispatcherFrame? _frame;
    private CaptureResult? _result;
    private bool _finished;
    private bool _closing;
    private bool _dragging;
    private Point _anchor;
    private Point _current;
    private PhysicalRect? _selection;
    private string? _supplement;

    /// <summary>显示截图界面并阻塞等待用户操作；用户放弃时返回 null。</summary>
    public CaptureResult? Run()
    {
        _snapshot = ScreenCapture.CaptureAll();
        try
        {
            CreateWindows();
            var frame = new DispatcherFrame();
            _frame = frame;
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            CloseWindows();
            _snapshot = null;
            _frame = null;
        }

        return _result;
    }

    private void CreateWindows()
    {
        var snapshot = _snapshot ?? throw new InvalidOperationException("截图快照未初始化。");
        foreach (var monitor in snapshot.Monitors)
        {
            var window = new CaptureOverlayWindow(monitor, snapshot);
            window.PointerPressed += OnPointerPressed;
            window.PointerMoved += OnPointerMoved;
            window.PointerReleased += OnPointerReleased;
            window.ConfirmRequested += _ => Confirm();
            window.CancelRequested += _ => Cancel();
            window.ResetRequested += _ => ResetSelection();
            window.SupplementRequested += EditSupplement;
            window.KeyEnterPressed += _ => Confirm();
            window.KeyEscapePressed += _ => Cancel();
            window.CloseRequested += _ => Cancel();
            _windows.Add(window);
        }

        if (_windows.Count == 0)
        {
            throw new InvalidOperationException("没有可用的显示器，无法显示截图选区。");
        }

        foreach (var window in _windows)
        {
            window.ShowOnMonitor();
        }

        ReRender();

        var target = FindWindowUnderCursor() ?? _windows.FirstOrDefault(w => w.Monitor.IsPrimary) ?? _windows[0];
        target.ActivateInput();
    }

    private void OnPointerPressed(CaptureOverlayWindow window, Point physical)
    {
        if (_finished)
        {
            return;
        }

        // 任何一次按下都开始新的框选（工具条上的点击不会进入这里）。
        _dragging = true;
        _anchor = physical;
        _current = physical;
        _selection = null;
        window.ActivateInput();
        ReRender();
    }

    private void OnPointerMoved(CaptureOverlayWindow window, Point physical)
    {
        if (_finished || !_dragging)
        {
            return;
        }

        _current = physical;
        _selection = BuildRect(_anchor, _current);
        ReRender();
    }

    private void OnPointerReleased(CaptureOverlayWindow window, Point physical)
    {
        if (_finished || !_dragging)
        {
            return;
        }

        _dragging = false;
        _current = physical;
        var rect = BuildRect(_anchor, _current);
        if (rect.Width < MinimumSelectionPixels || rect.Height < MinimumSelectionPixels)
        {
            // 只是点了一下，不产生选区。
            _selection = null;
            ReRender();
            return;
        }

        _selection = ClampToVirtual(rect);
        ReRender();

        // 工具条出现后把键盘焦点交给它所在窗口，Enter / Esc 才能生效。
        FindToolbarWindow()?.ActivateInput();
    }

    private void Confirm()
    {
        if (_finished || _dragging)
        {
            return;
        }

        var selection = _selection;
        var snapshot = _snapshot;
        if (selection is null || snapshot is null)
        {
            return;
        }

        try
        {
            var rect = ClampToVirtual(selection.Value);
            var image = snapshot.Crop(rect.Left, rect.Top, rect.Width, rect.Height);
            var supplement = string.IsNullOrWhiteSpace(_supplement) ? null : _supplement!.Trim();
            Finish(new CaptureResult
            {
                Image = image,
                Supplement = supplement
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("裁剪截图失败", ex);
            Finish(null);
        }
    }

    private void Cancel()
    {
        if (_finished)
        {
            return;
        }

        Finish(null);
    }

    private void ResetSelection()
    {
        if (_finished || _dragging)
        {
            return;
        }

        _selection = null;
        ReRender();
        (FindWindowUnderCursor() ?? _windows.FirstOrDefault())?.ActivateInput();
    }

    private void EditSupplement(CaptureOverlayWindow window)
    {
        if (_finished || _dragging || _selection is null)
        {
            return;
        }

        var accepted = false;
        try
        {
            var dialog = new SupplementDialog(_supplement);
            dialog.Owner = window;
            accepted = dialog.ShowDialog() == true;
            if (accepted)
            {
                _supplement = dialog.SupplementText;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("打开补充信息对话框失败", ex);
        }
        finally
        {
            if (!_finished)
            {
                window.ActivateInput();
                ReRender();
            }
        }
    }

    private void Finish(CaptureResult? result)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _result = result;
        CloseWindows();

        var frame = _frame;
        if (frame is { Continue: true })
        {
            frame.Continue = false;
        }
    }

    private void CloseWindows()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            Mouse.Capture(null);
            foreach (var window in _windows)
            {
                window.CloseSafely();
            }

            _windows.Clear();
        }
        catch (Exception ex)
        {
            AppLog.Error("关闭截图遮罩窗口时出错", ex);
        }
        finally
        {
            _closing = false;
        }
    }

    private void ReRender()
    {
        if (_snapshot is null || _windows.Count == 0)
        {
            return;
        }

        var state = BuildViewState();
        foreach (var window in _windows)
        {
            window.Render(state);
        }
    }

    private OverlayViewState BuildViewState()
    {
        var toolbarWindow = _selection is null ? null : FindToolbarWindow();
        return new OverlayViewState
        {
            Selection = _selection,
            IsDragging = _dragging,
            Cursor = _dragging ? _current : GetCursorPosition(),
            ShowHint = _selection is null && !_dragging,
            ShowDimensionTip = _dragging && _selection is not null,
            ToolbarVisible = _selection is not null && !_dragging,
            ToolbarOwnerHandle = toolbarWindow is null ? IntPtr.Zero : toolbarWindow.Monitor.Handle,
            HasSupplement = !string.IsNullOrWhiteSpace(_supplement)
        };
    }

    private CaptureOverlayWindow? FindWindowUnderCursor()
    {
        if (!CaptureNativeMethods.GetCursorPos(out var point) || _snapshot is null)
        {
            return null;
        }

        var monitor = _snapshot.FindMonitor(point.X, point.Y) ?? _snapshot.FindNearestMonitor(point.X, point.Y);
        if (monitor is null)
        {
            return null;
        }

        return _windows.FirstOrDefault(w => ReferenceEquals(w.Monitor, monitor));
    }

    private CaptureOverlayWindow? FindToolbarWindow()
    {
        var selection = _selection;
        var snapshot = _snapshot;
        if (selection is null || snapshot is null)
        {
            return null;
        }

        var rect = selection.Value;
        var monitor =
            snapshot.FindMonitor(rect.Right - 1, rect.Bottom - 1)
            ?? snapshot.FindMonitor(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2)
            ?? snapshot.FindNearestMonitor(rect.Left, rect.Top)
            ?? snapshot.Monitors.FirstOrDefault();

        if (monitor is null)
        {
            return _windows.FirstOrDefault();
        }

        return _windows.FirstOrDefault(w => ReferenceEquals(w.Monitor, monitor)) ?? _windows.FirstOrDefault();
    }

    private PhysicalRect ClampToVirtual(PhysicalRect rect)
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return rect;
        }

        var clamped = rect.Intersect(snapshot.VirtualBounds);
        return clamped.IsEmpty ? rect : clamped;
    }

    private static PhysicalRect BuildRect(Point start, Point end)
    {
        return PhysicalRect.FromCorners(
            (int)Math.Round(start.X),
            (int)Math.Round(start.Y),
            (int)Math.Round(end.X),
            (int)Math.Round(end.Y));
    }

    private static Point GetCursorPosition()
    {
        return CaptureNativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point();
    }
}

/// <summary>遮罩窗口绘制所需的一次性状态快照。</summary>
internal sealed class OverlayViewState
{
    /// <summary>当前选区（物理像素，屏幕坐标），为空表示还没有选区。</summary>
    public PhysicalRect? Selection { get; init; }

    public bool IsDragging { get; init; }

    /// <summary>鼠标位置（物理像素），拖动时用于显示尺寸提示。</summary>
    public Point Cursor { get; init; }

    /// <summary>是否显示「拖动鼠标框选」提示（仅无选区且未拖动时）。</summary>
    public bool ShowHint { get; init; }

    public bool ShowDimensionTip { get; init; }

    public bool ToolbarVisible { get; init; }

    /// <summary>应该显示工具条的显示器句柄。</summary>
    public IntPtr ToolbarOwnerHandle { get; init; }

    public bool HasSupplement { get; init; }
}
