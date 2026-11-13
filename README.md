# AutoAddSchedule（自动添加日程）

Windows 托盘小工具：框选截图 → 大模型提取日程 → 生成符合 CalDAV/iCalendar 规范的事件 → 同步到 iCloud 日历；
同时提供本地日历视图、日程列表、手动添加与 Windows 提醒通知。

## 功能

- 托盘常驻，不占任务栏；全局快捷键（默认 Ctrl+Alt+\）随时唤起截图。
- 类 Windows 截图的框选界面：拖选范围；选区下方工具条支持「确定 / 放弃 / 重选 / 补充信息」，Enter 确定、Esc 放弃。
- 截图与补充信息交给大模型（默认 DeepSeek deepseek-flash）提取日程；结构化结果生成为 iCalendar（VEVENT）后同步到 iCloud 日历（CalDAV）。
- 主界面：日历视图 + 日程列表 + 设置（大模型 / iCloud 日历 / 快捷键 / 通用）。
- 日程支持查看、手动添加、编辑、删除；联网时同步 iCloud，离线时本地可用。
- 日程到点前按「提前量」弹托盘通知提醒。
- 截图保存在程序目录 screenshots\，默认保留最近 60 张，超出自动清理。
- 除「同步 iCloud」与「调用大模型」外，其余功能完全可离线运行。

## 技术栈

- .NET 10（net10.0-windows）+ WPF + WinForms 托盘（NotifyIcon）
- 无第三方 UI 库；密钥使用 Windows DPAPI（当前用户）加密后保存在本地
- 测试：xUnit（单元测试 + 真实大模型集成测试）

## 环境要求

- Windows 10 / 11
- 构建：.NET SDK 10.0 或更高（本机使用 SDK 11 构建 net10.0 目标）
- 运行：自包含发布版无需安装 .NET；框架依赖运行需 .NET 10 Desktop Runtime

## 构建与运行

```powershell
# 构建（内部带构建锁，可多进程安全使用）
pwsh tools/build.ps1

# 运行
dotnet run --project src/AutoAddSchedule/AutoAddSchedule.csproj

# 测试
dotnet test tests/AutoAddSchedule.Tests/AutoAddSchedule.Tests.csproj

# 发布自包含版本到 publish/
pwsh tools/publish.ps1
```

## 配置

1. 复制 .env.example 为 .env，填入 DeepSeek Key、Apple ID、iCloud 应用专用密码。
2. 首次启动时程序自动导入到本地加密配置；也可在主界面「设置」中修改。
3. .env 已被 git 忽略，程序代码中不硬编码任何密钥。

快捷键、截图保留数量、开机自启等均在「设置」中配置。

## 目录结构

```text
AutoAddSchedule.slnx            解决方案
src/AutoAddSchedule/            WPF 主程序
  Core/                         配置、路径、数据模型、本地存储、日志
  Services/                     大模型、CalDAV、同步、提醒、快捷键、托盘等
  UI/                           主界面、日历、列表、设置、截图界面
  Assets/                       图标等静态资源
tests/AutoAddSchedule.Tests/    xUnit 测试（含 test/ 样本的真实调用集成测试）
test/                           用户提供的测试样本（截图 + 正确结果 + 补充信息）
docs/                           项目文档
tools/                          构建、发布脚本
```

## 文档

- [架构说明](docs/ARCHITECTURE.md)
- [开发指南](docs/DEVELOPMENT.md)
- [配置说明](docs/CONFIGURATION.md)
- [使用说明](docs/USAGE.md)
- [截图模块](docs/CAPTURE.md)
- [大模型提取](docs/LLM.md)
- [iCloud / CalDAV 同步](docs/CALDAV.md)
- [主界面](docs/UI.md)
- [测试](docs/TESTING.md)
- [发布](docs/RELEASE.md)

## 安全

- 所有密钥只保存在本地 .env（git 忽略）或 %AppData%\AutoAddSchedule\secrets.dat（DPAPI 加密，当前用户可解密）。
- 源码、日志、文档中不得出现明文密钥。
