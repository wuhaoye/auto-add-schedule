using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.UI.Capture;

/// <summary>
/// 单台显示器上的截图遮罩窗口：显示本显示器截图、暗色遮罩、选区与工具条。
/// 窗口按显示器物理矩形定位，因此截图与选区可以做到像素级对齐。
/// </summary>
public partial class CaptureOverlayWindow : Window
{
    private bool _allowClose;
    private bool _mouseCaptured;
    private IntPtr _handle;
    private OverlayViewState? _lastState;

    public CaptureOverlayWindow(MonitorSnapshot monitor, ScreenSnapshot snapshot)
    {
        Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        ArgumentNullException.ThrowIfNull(snapshot);

        InitializeComponent();
        ScreenImage.Source = snapshot.CreateMonitorView(monitor);
        OverlayCanvas.LostMouseCapture += OnLostMouseCapture;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
        Closing += OnClosing;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>本窗口对应的显示器。</summary>
    public MonitorSnapshot Monitor { get; }

    internal event Action<CaptureOverlayWindow, Point>? PointerPressed;

    internal event Action<CaptureOverlayWindow, Point>? PointerMoved;

    internal event Action<CaptureOverlayWindow, Point>? PointerReleased;

    internal event Action<CaptureOverlayWindow>? ConfirmRequested;

    internal event Action<CaptureOverlayWindow>? CancelRequested;

    internal event Action<CaptureOverlayWindow>? ResetRequested;

    internal event Action<CaptureOverlayWindow>? SupplementRequested;

    internal event Action<CaptureOverlayWindow>? KeyEnterPressed;

    internal event Action<CaptureOverlayWindow>? KeyEscapePressed;

    internal event Action<CaptureOverlayWindow>? CloseRequested;

    /// <summary>把窗口显示到对应显示器的物理矩形上，并延迟一帧确认 DPI 变化后的位置。</summary>
    internal void ShowOnMonitor()
    {
        if (IsVisible)
        {
            return;
        }

        // 先用透明状态显示，避免窗口在默认位置/尺寸上闪一帧。
        Opacity = 0;
        Left = Monitor.Bounds.Left;
        Top = Monitor.Bounds.Top;
        Width = Math.Max(1, Monitor.Bounds.Width);
        Height = Math.Max(1, Monitor.Bounds.Height);
        Show();
        PlaceOnMonitor();
        UpdateLayout();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            try
            {
                if (!IsVisible)
                {
                    return;
                }

                // SetWindowPos 期间可能触发 WM_DPICHANGED，WPF 会按建议矩形调整窗口，这里再校正一次。
                PlaceOnMonitor();
                UpdateLayout();
                Opacity = 1;
                if (_lastState is not null)
                {
                    Render(_lastState);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("显示截图遮罩窗口失败", ex);
            }
        }));
    }

    /// <summary>用物理像素把窗口贴到显示器矩形上（不激活窗口）。</summary>
    internal void PlaceOnMonitor()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        CaptureNativeMethods.SetWindowPos(
            _handle,
            CaptureNativeMethods.HwndTopmost,
            Monitor.Bounds.Left,
            Monitor.Bounds.Top,
            Monitor.Bounds.Width,
            Monitor.Bounds.Height,
            CaptureNativeMethods.SwpNoActivate | CaptureNativeMethods.SwpShowWindow);
    }

    /// <summary>激活窗口并把键盘焦点交给它，保证 Enter / Esc 能生效。</summary>
    internal void ActivateInput()
    {
        try
        {
            if (!IsActive)
            {
                Activate();
                if (_handle != IntPtr.Zero)
                {
                    CaptureNativeMethods.SetForegroundWindow(_handle);
                }
            }

            if (!IsKeyboardFocusWithin)
            {
                Focus();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("激活截图遮罩窗口失败：" + ex.Message);
        }
    }

    internal void CloseSafely()
    {
        _allowClose = true;
        _mouseCaptured = false;
        try
        {
            Mouse.Capture(null);
            if (IsVisible)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("关闭截图遮罩窗口失败：" + ex.Message);
        }
    }

    /// <summary>按当前选区状态刷新遮罩、边框、尺寸提示与工具条。</summary>
    internal void Render(OverlayViewState state)
    {
        _lastState = state;
        RenderCore(state);
    }

    private void RenderCore(OverlayViewState state)
    {
        var canvas = GetCanvasSize();
        var fullRect = new Rect(0, 0, canvas.Width, canvas.Height);

        Cursor = state.Selection is null || state.IsDragging ? Cursors.Cross : Cursors.Arrow;

        var selection = state.Selection;
        if (selection is null)
        {
            DimPath.Data = new RectangleGeometry(fullRect);
            SelectionBorderPath.Data = null;
            SelectionBorderPath.Visibility = Visibility.Collapsed;
            SizeTip.Visibility = Visibility.Collapsed;
            ToolBar.Visibility = Visibility.Collapsed;
            UpdateHint(state.ShowHint, canvas);
            return;
        }

        if (!TryScreenToLocal(selection.Value.Left, selection.Value.Top, out var topLeft) ||
            !TryScreenToLocal(selection.Value.Right, selection.Value.Bottom, out var bottomRight))
        {
            DimPath.Data = new RectangleGeometry(fullRect);
            SelectionBorderPath.Visibility = Visibility.Collapsed;
            SizeTip.Visibility = Visibility.Collapsed;
            ToolBar.Visibility = Visibility.Collapsed;
            return;
        }

        var localRect = new Rect(topLeft, bottomRight);
        var clipped = Rect.Intersect(localRect, fullRect);
        DimPath.Data = clipped.Width <= 0 || clipped.Height <= 0
            ? new RectangleGeometry(fullRect)
            : new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(fullRect),
                new RectangleGeometry(clipped));

        UpdateSelectionBorder(selection.Value, topLeft, bottomRight);
        UpdateSizeTip(state, selection.Value, canvas);
        UpdateToolBar(state, localRect, canvas);
        UpdateHint(false, canvas);
    }

    private void UpdateSelectionBorder(PhysicalRect selection, Point topLeft, Point bottomRight)
    {
        var geometry = new PathGeometry();
        var bounds = Monitor.Bounds;

        // 只绘制真正位于本显示器内部的边，避免跨显示器时在屏幕接缝处出现多余边框。
        if (selection.Left >= bounds.Left)
        {
            AddLine(geometry, topLeft, new Point(topLeft.X, bottomRight.Y));
        }

        if (selection.Right <= bounds.Right)
        {
            AddLine(geometry, new Point(bottomRight.X, topLeft.Y), bottomRight);
        }

        if (selection.Top >= bounds.Top)
        {
            AddLine(geometry, topLeft, new Point(bottomRight.X, topLeft.Y));
        }

        if (selection.Bottom <= bounds.Bottom)
        {
            AddLine(geometry, new Point(topLeft.X, bottomRight.Y), bottomRight);
        }

        SelectionBorderPath.Data = geometry;
        SelectionBorderPath.StrokeThickness = Math.Max(1.0, 2.0 / GetDpiScale());
        SelectionBorderPath.Visibility = geometry.Figures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSizeTip(OverlayViewState state, PhysicalRect selection, Size canvas)
    {
        var cursorX = (int)Math.Round(state.Cursor.X);
        var cursorY = (int)Math.Round(state.Cursor.Y);
        if (!state.ShowDimensionTip || !Monitor.Contains(cursorX, cursorY) ||
            !TryScreenToLocal(cursorX, cursorY, out var localCursor))
        {
            SizeTip.Visibility = Visibility.Collapsed;
            return;
        }

        SizeTipText.Text = selection.Width + " × " + selection.Height;
        SizeTip.Visibility = Visibility.Visible;
        SizeTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tipSize = SizeTip.DesiredSize;

        const double offset = 16;
        var left = localCursor.X + offset;
        var top = localCursor.Y + offset;
        if (left + tipSize.Width > canvas.Width - 4)
        {
            left = localCursor.X - tipSize.Width - offset;
        }

        if (top + tipSize.Height > canvas.Height - 4)
        {
            top = localCursor.Y - tipSize.Height - offset;
        }

        left = Math.Max(4, Math.Min(left, Math.Max(4, canvas.Width - tipSize.Width - 4)));
        top = Math.Max(4, Math.Min(top, Math.Max(4, canvas.Height - tipSize.Height - 4)));
        Canvas.SetLeft(SizeTip, left);
        Canvas.SetTop(SizeTip, top);
    }

    private void UpdateToolBar(OverlayViewState state, Rect localRect, Size canvas)
    {
        if (!state.ToolbarVisible || state.ToolbarOwnerHandle != Monitor.Handle)
        {
            ToolBar.Visibility = Visibility.Collapsed;
            return;
        }

        SupplementButton.Content = state.HasSupplement ? "补充信息（已填写）" : "补充信息";
        ToolBar.Visibility = Visibility.Visible;
        ToolBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = ToolBar.DesiredSize;

        const double gap = 10;
        const double margin = 12;
        var left = localRect.Left + (localRect.Width - size.Width) / 2;
        var top = localRect.Bottom + gap;

        // 下方空间不足时放到选区上方，再不足则贴着窗口边缘，保证工具条始终可见。
        if (top + size.Height > canvas.Height - margin)
        {
            top = localRect.Top - size.Height - gap;
        }

        if (top < margin)
        {
            top = margin;
        }

        if (top + size.Height > canvas.Height - margin)
        {
            top = Math.Max(0, canvas.Height - size.Height);
        }

        left = Math.Max(margin, Math.Min(left, Math.Max(margin, canvas.Width - size.Width - margin)));
        Canvas.SetLeft(ToolBar, left);
        Canvas.SetTop(ToolBar, top);
    }

    private void UpdateHint(bool show, Size canvas)
    {
        if (!show || !Monitor.IsPrimary)
        {
            HintPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HintPanel.Visibility = Visibility.Visible;
        HintPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HintPanel.DesiredSize;
        Canvas.SetLeft(HintPanel, Math.Max(0, (canvas.Width - size.Width) / 2));
        Canvas.SetTop(HintPanel, Math.Min(56, Math.Max(16, canvas.Height / 8)));
    }

    private Size GetCanvasSize()
    {
        var width = OverlayCanvas.ActualWidth;
        var height = OverlayCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            width = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : ActualWidth;
            height = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : ActualHeight;
        }

        if (width <= 0 || height <= 0)
        {
            width = Math.Max(1, Monitor.Bounds.Width);
            height = Math.Max(1, Monitor.Bounds.Height);
        }

        return new Size(width, height);
    }

    private bool TryScreenToLocal(int screenX, int screenY, out Point local)
    {
        try
        {
            local = OverlayCanvas.PointFromScreen(new Point(screenX, screenY));
            return true;
        }
        catch (Exception)
        {
            // 窗口尚未连接到 PresentationSource 时无法换算，忽略本次绘制即可。
            local = default;
            return false;
        }
    }

    private double GetDpiScale()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.DpiScaleX > 0)
            {
                return dpi.DpiScaleX;
            }
        }
        catch (Exception)
        {
            // 忽略，使用 1.0 兜底。
        }

        return 1.0;
    }

    private static void AddLine(PathGeometry geometry, Point start, Point end)
    {
        if (start == end)
        {
            return;
        }

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new LineSegment(end, true));
        geometry.Figures.Add(figure);
    }

    private static Point GetPointerPosition(MouseEventArgs e)
    {
        // 鼠标事件在跨显示器拖动时仍会送达捕获窗口；直接取系统光标物理坐标最可靠。
        return CaptureNativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        ApplyToolWindowStyle();
    }

    private void ApplyToolWindowStyle()
    {
        try
        {
            // 让遮罩窗口不进入 Alt+Tab，行为更接近系统截图。
            var style = CaptureNativeMethods.GetWindowLong(_handle, CaptureNativeMethods.GwlExStyle);
            if ((style & CaptureNativeMethods.WsExToolWindow) == 0)
            {
                CaptureNativeMethods.SetWindowLong(_handle, CaptureNativeMethods.GwlExStyle, style | CaptureNativeMethods.WsExToolWindow);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("设置截图遮罩窗口样式失败：" + ex.Message);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlaceOnMonitor();
        if (_lastState is not null)
        {
            Render(_lastState);
        }
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        try
        {
            PlaceOnMonitor();
            if (_lastState is not null)
            {
                Render(_lastState);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("截图遮罩窗口 DPI 变化处理失败：" + ex.Message);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_lastState is not null)
        {
            RenderCore(_lastState);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        // 用户按 Alt+F4 等关闭窗口时，取消本次关闭，并推迟到关闭流程结束后再通知会话放弃截图，
        // 避免在 Closing 事件里同步关闭窗口造成重入。
        e.Cancel = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => CloseRequested?.Invoke(this)));
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseCaptured = true;
        Mouse.Capture(OverlayCanvas);
        PointerPressed?.Invoke(this, GetPointerPosition(e));
        e.Handled = true;
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseCaptured)
        {
            return;
        }

        PointerMoved?.Invoke(this, GetPointerPosition(e));
        e.Handled = true;
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseCaptured)
        {
            return;
        }

        _mouseCaptured = false;
        Mouse.Capture(null);
        PointerReleased?.Invoke(this, GetPointerPosition(e));
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_mouseCaptured)
        {
            return;
        }

        // 捕获意外丢失（例如系统弹窗抢走鼠标）时按松开处理，避免选区卡在拖动状态。
        _mouseCaptured = false;
        PointerReleased?.Invoke(this, GetPointerPosition(e));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            KeyEscapePressed?.Invoke(this);
        }
        else if (e.Key == Key.Return)
        {
            e.Handled = true;
            KeyEnterPressed?.Invoke(this);
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => ConfirmRequested?.Invoke(this);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this);

    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke(this);

    private void SupplementButton_Click(object sender, RoutedEventArgs e) => SupplementRequested?.Invoke(this);
}