# 配置说明

## 配置文件位置

- %AppData%\AutoAddSchedule\config.json：非敏感配置（大模型端点/模型名、iCloud 账号邮箱、日历地址、快捷键、通用项）
- %AppData%\AutoAddSchedule\secrets.dat：敏感数据（DeepSeek API Key、iCloud 应用专用密码），
  使用 Windows DPAPI（DataProtectionScope.CurrentUser）加密，只有当前 Windows 用户可解密。
- 仓库根目录 .env：首次运行的密钥来源，已被 .gitignore 忽略。程序代码不读取任何硬编码密钥。

## .env 格式

```text
MODELKEY=sk-xxxx                      # DeepSeek API Key
APPLEID=someone@example.com           # iCloud 账号（Apple ID 邮箱）
APPLEKEY=xxxx-xxxx-xxxx-xxxx          # iCloud 应用专用密码
```

启动时程序按以下顺序查找 .env：程序目录 → 逐级向上最多 6 层。找到后只会用其中的值填充「当前为空」的配置项，
不会覆盖用户在设置界面里已填的值。

## 设置项

### 大模型

- Base URL：默认 https://api.deepseek.com
- 模型：默认 deepseek-flash（支持图片输入）；deepseek-v4-pro 不支持图片，不能用于截图识别
- API Key：保存在 secrets.dat
- 「测试连接」会发送一次最小请求验证 Key 与端点

### iCloud 日历

- Apple ID 邮箱
- 应用专用密码（在 https://appleid.apple.com 生成，形如 xxxx-xxxx-xxxx-xxxx）
- 「读取日历列表」通过 CalDAV 发现账号下的日历，选择同步目标并记住

### 快捷键

- 默认 Ctrl+Alt+\；点击输入框后按下组合键即可修改
- 注册失败（被其他程序占用）时会提示并保留原快捷键

### 通用

- 开机自启：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run，默认关闭
- 截图保留数量：默认 60 张
- 启动后最小化到托盘：默认开启
