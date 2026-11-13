using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>
/// 真实大模型集成测试的环境：定位仓库根目录、读取 .env、构造使用真实 API 的提取服务。
/// .env 中没有 MODELKEY 时由 LlmIntegrationTheoryAttribute 跳过测试。
/// </summary>
public static class LlmTestEnvironment
{
    private static readonly Lazy<EnvFile> Env = new(EnvFile.LoadDefault);
    private static readonly Lazy<string> Root = new(LocateRepositoryRoot);

    public static string? ApiKey => Env.Value.Get("MODELKEY");

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public static string RepositoryRoot => Root.Value;

    /// <summary>遍历仓库 test/ 下包含 image.png 与 result 的样本目录。</summary>
    public static IEnumerable<object[]> SampleDirectories()
    {
        var testRoot = Path.Combine(RepositoryRoot, "test");
        if (!Directory.Exists(testRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.GetDirectories(testRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(directory, "image.png")) && File.Exists(Path.Combine(directory, "result")))
            {
                yield return new object[] { directory };
            }
        }
    }

    /// <summary>基于 .env 构造临时配置，不依赖 AppServices 初始化。</summary>
    public static LlmConfig CreateConfig()
    {
        var config = new AppConfig();
        var env = Env.Value;

        config.Llm.ApiKey = env.Get("MODELKEY") ?? string.Empty;

        var baseUrl = env.Get("MODELBASE") ?? env.Get("MODEL_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            config.Llm.BaseUrl = baseUrl;
        }

        var model = env.Get("MODEL") ?? env.Get("MODEL_NAME");
        if (!string.IsNullOrWhiteSpace(model))
        {
            config.Llm.Model = model;
        }

        return config.Llm;
    }

    public static ScheduleExtractionService CreateExtractor()
    {
        var config = CreateConfig();
        var client = new LlmClient(() => config);
        return new ScheduleExtractionService(client, () => DateTime.Now, () => config.Model);
    }

    private static string LocateRepositoryRoot()
    {
        var envPath = Env.Value.SourcePath;
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            var directory = Path.GetDirectoryName(envPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "test")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
