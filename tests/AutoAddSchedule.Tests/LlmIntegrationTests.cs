using System;
using System.Collections.Generic;
using System.IO;
using AutoAddSchedule.Core;

namespace AutoAddSchedule.Tests;

/// <summary>
/// 真实大模型集成测试：遍历仓库 test/ 下的样本目录，调用真实 DeepSeek 接口，
/// 比对 TimeFormat.FormatReminderTime(开始时间 - 提前分钟数) 与 result 文件内容。
/// 没有 MODELKEY 时自动跳过；每次只跑样本目录，避免产生多余费用。
/// </summary>
public class LlmIntegrationTests
{
    public static IEnumerable<object[]> SampleDirectories() => LlmTestEnvironment.SampleDirectories();

    [LlmIntegrationTheory]
    [MemberData(nameof(SampleDirectories))]
    public void Extract_Real_Api_Should_Match_Expected_Result(string sampleDirectory)
    {
        var imagePath = Path.Combine(sampleDirectory, "image.png");
        var addPath = Path.Combine(sampleDirectory, "add");
        var resultPath = Path.Combine(sampleDirectory, "result");

        var supplement = File.Exists(addPath) ? File.ReadAllText(addPath).Trim() : null;
        var expected = File.ReadAllText(resultPath).Trim();

        // 复制到临时目录再调用，避免模型/接口改动 test/ 下的原始样本。
        var tempDirectory = Path.Combine(Path.GetTempPath(), "AutoAddSchedule-LlmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var tempImage = Path.Combine(tempDirectory, "image.png");
        File.Copy(imagePath, tempImage, overwrite: true);

        try
        {
            var extractor = LlmTestEnvironment.CreateExtractor();
            var result = extractor.ExtractAsync(tempImage, supplement).GetAwaiter().GetResult();

            var actual = TimeFormat.FormatReminderTime(
                result.Draft.StartLocal.AddMinutes(-result.Draft.ReminderMinutesBefore));

            Assert.Equal(expected, actual);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响测试结果。
            }
        }
    }
}

/// <summary>没有 .env / MODELKEY 时自动跳过的 Theory 特性。</summary>
public sealed class LlmIntegrationTheoryAttribute : TheoryAttribute
{
    public LlmIntegrationTheoryAttribute()
    {
        if (!LlmTestEnvironment.HasApiKey)
        {
            Skip = "未在仓库根 .env 中找到 MODELKEY，已跳过真实大模型集成测试。";
        }
    }
}
