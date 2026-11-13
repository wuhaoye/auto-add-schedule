# 测试

## 测试项目

tests/AutoAddSchedule.Tests（xUnit，目标 net10.0-windows，引用主程序）：

- 单元测试：时间格式化（2026年10月26日6：45）、iCalendar 生成、.env 解析、配置加解密、截图保留清理。
- 集成测试：读取 test/01、test/02、test/03 的 image.png 与可选 add 补充信息，
  真实调用 DeepSeek 提取，计算提醒触发时间并与 result 比对。

## 运行

```powershell
dotnet test tests/AutoAddSchedule.Tests/AutoAddSchedule.Tests.csproj
```

集成测试需要仓库根目录 .env 中的 MODELKEY；缺失时该组测试跳过（单元测试不受影响）。

## 样本语义

result 文件内容为「提醒触发时间」，格式：yyyy年M月d日H:mm（全角冒号、小时不补零）。
add 文件内容为发给大模型的补充信息（可选）。
