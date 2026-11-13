# 项目协作约定（AI Agent 与开发者共同遵守）

1. 语言：界面文案、代码注释、项目文档一律使用中文。
2. 密钥：不得硬编码任何密钥/密码/Token。只能存在于两个位置：仓库根目录的 .env（已被 git 忽略）或
   %AppData%\AutoAddSchedule\secrets.dat（DPAPI 加密）。日志与异常信息中不得输出密钥。
3. 构建：统一使用 pwsh tools/build.ps1（内部使用全局互斥锁串行构建），不要并发直接跑 dotnet build。
   测试使用 dotnet test（先构建成功再跑）。
4. Git：Agent 不得自行执行任何 git 命令（含 status/log/diff）；由项目负责人批准后由 Lead 统一提交。
5. 文档：修改代码后必须同步更新 docs/ 中对应文档；文档里不要写「某年某月某日修改」一类时间信息，
   变更历史由 git 记录。
6. 离线：除「同步 iCloud」与「调用大模型」外，其余功能必须能断网运行；不得引入运行时必须联网的依赖。
7. 依赖：新增 NuGet 包需说明理由并保持最小化；界面优先使用 WPF 自带控件与自绘。
8. 测试：tests/AutoAddSchedule.Tests 覆盖单元测试与 test/ 样本的集成测试；改动后保持全部测试通过。
9. 界面：所有耗时操作异步执行，不得阻塞 UI 线程；跨线程更新界面统一走 Application.Current.Dispatcher。
10. 异常：面向用户的错误要有中文提示，同时写入 %AppData%\AutoAddSchedule\logs 日志便于排查。
