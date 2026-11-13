# 发布

## 自包含发布

```powershell
pwsh tools/publish.ps1
```

产物输出到 publish/：目标机器无需安装 .NET 运行时，双击 AutoAddSchedule.exe 即用。
首次发布需要从 NuGet 下载 Windows x64 运行时包（约 150–250 MB，缓存于 %USERPROFILE%\.nuget\packages）。
实测 win-x64 自包含发布目录约 154 MB（251 个文件），已在本机 Windows 11 上启动验证。

## 目录约定

- publish/screenshots/：运行时自动创建，保存截图，保留最近 60 张。
- .env 不随发布目录携带；目标机通过「设置」界面填写密钥（DPAPI 加密保存）。

## 检查清单

1. dotnet test 全部通过
2. tools/publish.ps1 成功
3. 发布目录运行：托盘图标出现、快捷键可用、截图→提取→同步链路可用
4. 设置中「测试连接」「读取日历列表」正常