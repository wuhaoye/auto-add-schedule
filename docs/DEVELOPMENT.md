# 开发指南

## 环境

- Windows 10 / 11
- .NET SDK 10 或更高（本机为 SDK 11.0.100，目标框架 net10.0-windows）
- 无需 Visual Studio；命令行 dotnet 即可构建运行

## 构建

```powershell
pwsh tools/build.ps1
```

tools/build.ps1 内部使用全局互斥锁（Global\AutoAddScheduleBuild）串行化构建，
多 Agent/多进程同时工作时请统一走该脚本，避免 obj/、bin/ 文件占用冲突。

## 运行

```powershell
dotnet run --project src/AutoAddSchedule/AutoAddSchedule.csproj
```

首次运行若未配置密钥，会直接打开「设置」页。托盘图标出现后，主窗口可关闭，程序继续常驻。

## 调试与视觉检查

开发期可用的命令行参数：

- `--page calendar|list|settings`：启动后直接显示指定页面（用于界面视觉检查）。
- `--capture`：启动 1.2 秒后自动进入截图流程（用于验证截图界面）。

辅助脚本（仅开发使用，不参与程序运行）：

- `pwsh tools/screenshot.ps1 -OutPath .tmp/screen.png`：抓取当前桌面到 PNG。
- `pwsh tools/test.ps1`：与构建共用互斥锁运行测试，避免与构建并发冲突。
- `pwsh tools/make-icon.ps1`：重新生成 `src/AutoAddSchedule/Assets/app.ico`。

## 测试

```powershell
dotnet test tests/AutoAddSchedule.Tests/AutoAddSchedule.Tests.csproj
```

集成测试会读取仓库根目录的 .env 并真实调用 DeepSeek API；没有 Key 时集成测试标记为跳过。

## 代码约定

- 目标框架 net10.0-windows，启用 Nullable 与 ImplicitUsings。
- 服务一律面向接口；新服务通过 AppServices 暴露。
- 所有磁盘/网络操作异步，禁止在 UI 线程同步等待。
- 日志统一走 AppLog，不要用 Console.WriteLine。
- 时间在内存中统一用本地时间 DateTime；写 iCalendar 时转换为 UTC。