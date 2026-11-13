# 截图模块

## 目标

- 全局快捷键（Ctrl+Alt+\）或托盘菜单唤起后，在所有显示器上显示暗色遮罩，鼠标拖选矩形区域。
- 选区下方（空间不足时上方）显示工具条：确定 / 放弃 / 重选 / 补充信息；Enter 确定、Esc 放弃。
- 覆盖多显示器与高 DPI（程序清单为 PerMonitorV2），输出按物理像素裁剪的位图供保存与大模型识别。

## 文件结构

- `UI/Capture/ScreenCapture.cs`：Win32 GDI 抓取整个虚拟屏幕（物理像素），枚举显示器物理矩形，提供裁剪与显示器查找
- `UI/Capture/CaptureNativeMethods.cs`：截图相关 Win32 API 声明（GDI、显示器枚举、窗口定位、光标）
- `UI/Capture/PhysicalRect.cs`：物理像素矩形，支持负坐标与归一化
- `UI/Capture/CaptureOverlayWindow.xaml(.cs)`：单台显示器上的遮罩窗口：截图、遮罩、选区、尺寸提示、工具条
- `UI/Capture/CaptureSession.cs`：一次截图会话：创建各显示器窗口、选区状态机、键盘/工具条处理、嵌套消息循环
- `Services/CaptureOverlayService.cs`：`ICaptureOverlayService` 实现，对外提供同步阻塞的 `ShowCapture()`
- `Services/ScreenshotStore.cs`：截图保存为 PNG 与「保留最近 N 张」清理
- `UI/Dialogs/SupplementDialog.xaml(.cs)`：补充信息输入对话框


## 屏幕捕获

- 使用 GDI：`GetDC(NULL)` → `CreateCompatibleDC` / `CreateCompatibleBitmap` / `BitBlt`
  （`SRCCOPY | CAPTUREBLT`）抓取整个虚拟屏幕，再用 `GetDIBits` 取出 32 位像素。
- 虚拟屏幕范围来自 `GetSystemMetrics(SM_XVIRTUALSCREEN/SM_YVIRTUALSCREEN/SM_CXVIRTUALSCREEN/SM_CYVIRTUALSCREEN)`；
  每台显示器使用 `EnumDisplayMonitors` + `GetMonitorInfo` 的 `rcMonitor`（物理像素，含负坐标）。
- BitBlt 得到的 Alpha 通道为 0，读取后统一填为 255，避免 WPF 中显示为全透明；
  点位图冻结后创建为 `BitmapSource`（Bgra32）。
- 抓屏发生在遮罩窗口显示之前，因此截图内容不包含遮罩与鼠标指针。
- 锁屏 / 安全桌面 / 无法分配位图等情况会抛出带中文说明的异常，由 `CaptureOverlayService`
  记录日志并弹出「截图失败」通知，正常返回 `null`。

`ScreenSnapshot` 同时持有一份整屏位图与显示器列表：

- `CreateMonitorView(monitor)`：按显示器矩形创建 `CroppedBitmap` 视图，供遮罩窗口做 1:1 显示，不复制像素。
- `Crop(x, y, w, h)`：按物理像素坐标复制像素裁剪出独立 `BitmapSource`，用于最终结果；
  坐标超出虚拟屏幕时自动收敛。
- `FindMonitor` / `FindNearestMonitor`：根据物理坐标定位显示器（决定工具条显示在哪块屏幕）。

## 覆盖层交互

### 窗口与 DPI

- 每台显示器一个覆盖层窗口：`WindowStyle=None`、`AllowsTransparency=True`、`Topmost=True`、
  `ShowInTaskbar=False`、`ResizeMode=NoResize`。
- 窗口先以透明状态 `Show()`，再用 `SetWindowPos` 按显示器物理矩形定位（`SWP_NOACTIVATE`）。
  这样 WPF 的 DIP 布局由窗口自身 DPI 决定，不受 WPF `Left/Top` 的换算歧义影响。
- `SetWindowPos` 过程中可能触发 `WM_DPICHANGED`，窗口会在 `DpiChanged` 与延迟一帧的回调中
  重新贴到显示器物理矩形，确保不同缩放比例的显示器都精确覆盖。
- 屏幕坐标与窗口 DIP 的换算统一使用 `Visual.PointToScreen` / `PointFromScreen`，
  鼠标位置直接取 `GetCursorPos` 的物理坐标，因此跨显示器、跨缩放拖动都不会错位。
- 窗口设置 `WS_EX_TOOLWINDOW`，不进入 Alt+Tab；显示后 `Activate()` + `SetForegroundWindow`
  把键盘焦点交给遮罩窗口，避免 Enter / Esc 失效。

### 状态机

1. **空闲**：整屏暗色遮罩，主屏显示「拖动鼠标框选要识别的区域 / 按 Esc 取消」提示，十字光标。
2. **拖动中**：鼠标按下后捕获鼠标（`Mouse.Capture`），拖动时实时更新选区：
   选区内部保持原始亮度（遮罩镂空），四周绘制 1–2 物理像素高亮边框，
   光标旁显示「宽 × 高」（物理像素）。
3. **已选区**：松开鼠标后选区固定，工具条出现在选区下方（下方空间不足时自动放到上方，
   仍不足则贴窗口边缘）；单击任意遮罩区域可重新开始框选。
4. 拖动距离小于 4 物理像素视为误触，不产生选区。

选区用全局物理像素表示，跨显示器时每块屏幕的遮罩窗口各自绘制与本屏相交的部分；
选区边框只绘制真正落在该显示器内部的边，避免屏幕接缝处出现多余线条。

### 工具条

- 按钮：确定 / 放弃 / 重选 / 补充信息。
- 工具条显示在选区右下角所在的显示器上；该显示器窗口承担键盘焦点。
- 「重选」清除选区，回到空闲状态重新框选。
- 「补充信息」打开补充信息对话框，可多次打开修改，内容会保留；
  填写后按钮显示「补充信息（已填写）」，关闭对话框后仍可继续确定。

### 键盘

- Enter：有选区且工具条显示时 = 确定；拖动过程中按 Enter 无效。
- Esc：任何时候（包括拖动中、工具条显示时、补充信息对话框内）= 放弃。
- 补充信息对话框内普通 Enter 换行，Ctrl+Enter 提交，Esc 取消。

### 可靠清理

- 确定：用原始虚拟屏幕位图按物理像素裁剪出 `BitmapSource`，返回
  `CaptureResult { Image, Supplement }`。
- 放弃：返回 `null`。
- 无论确定、放弃、异常还是用户按 Alt+F4 关闭遮罩窗口，都会先释放鼠标捕获
  （`Mouse.Capture(null)`）再关闭全部覆盖层窗口，之后 `ShowCapture()` 的嵌套消息循环才退出，
  不会残留窗口；重复触发由 `IsActive` 拦截。

## 截图存储

`ScreenshotStore` 将截图保存到程序安装目录下的 `screenshots\`（`AppPaths.ScreenshotsDirectory`）：

- 文件名：`yyyyMMdd_HHmmss_fff.png`；同一毫秒内连续保存时等待时间戳变化，不覆盖已有文件。
- 目录不存在会自动创建；目录不可写时抛带中文说明的 `IOException` 并写日志。
- `Cleanup()` 按保留数量 `AppServices.Config.Config.General.ScreenshotRetention`（默认 60，下限 1）
  保留最新 N 张，按文件最后写入时间倒序删除更旧的 `.png/.jpg/.jpeg`，忽略其他文件；返回删除数量。
- 单个文件删除失败只记警告，不影响其余清理；清理本身不抛异常。
- 默认无参构造函数供 `AppServices` 使用；测试可通过
  `new ScreenshotStore(directory, retention)` 或 `new ScreenshotStore(directory, Func<int>)` 注入临时目录与保留数量。

## 测试

`tests/AutoAddSchedule.Tests/ScreenshotStoreTests.cs` 使用临时目录验证（不依赖真实屏幕）：

- 保存成功、文件名格式、目录自动创建、PNG 可解码且尺寸正确；
- 连续保存不覆盖、目录不可用时抛出中文异常；
- 只保留最新 N 张、保留数非法（0/负数）时至少保留 1 张、非图片文件不受影响；
- 清理数量返回值、目录不存在时返回 0、保留数量支持每次重新读取（`Func<int>` 注入）。

## 已知限制

- 覆盖层只在主显示器显示操作提示，其余显示器只显示遮罩。
- 选区不支持拖动调整大小（可点「重选」后重新框选）。
- 抓屏使用 GDI 位图，暂不支持受保护内容（如部分 DRM/硬件覆盖层视频）的截取。
- 若 Windows 拒绝把前台焦点交给本程序（极少数前台锁定场景），Enter / Esc 可能需要先点击一次遮罩窗口。