using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoAddSchedule.Core;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>ScheduleExtractionService 的离线单元测试：注入假 ILlmClient，不联网。</summary>
public class ExtractionServiceTests
{
    private const string ValidJson =
        "{\"title\":\"升旗仪式\",\"start\":\"2026-10-26T06:45:00\"," +
        "\"location\":\"昌平校区风雨操场旗杆下\",\"reminder_minutes_before\":0,\"all_day\":false}";

    [Fact]
    public async Task ExtractAsync_Should_Parse_Fenced_Json_And_Keep_Raw()
    {
        var client = new FakeLlmClient { Response = "```json\n" + ValidJson + "\n```" };
        var service = new ScheduleExtractionService(
            client,
            () => new DateTime(2026, 11, 13, 20, 0, 0),
            () => "deepseek-flash");
        var imagePath = CreateTempImage();

        try
        {
            var result = await service.ExtractAsync(imagePath, null);

            Assert.Equal("升旗仪式", result.Draft.Title);
            Assert.Equal(new DateTime(2026, 10, 26, 6, 45, 0), result.Draft.StartLocal);
            Assert.Equal(client.Response, result.RawJson);
            Assert.Equal("deepseek-flash", result.Model);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_Should_Send_Image_Prompt_And_Supplement()
    {
        var client = new FakeLlmClient { Response = ValidJson };
        var service = new ScheduleExtractionService(client, () => new DateTime(2026, 11, 13, 20, 0, 0));
        var imagePath = CreateTempImage();

        try
        {
            await service.ExtractAsync(imagePath, "提前30分钟提醒我");

            Assert.NotNull(client.LastRequest);
            var request = client.LastRequest!;
            Assert.Single(request.ImagePaths);
            Assert.Equal(imagePath, request.ImagePaths[0]);
            Assert.True(request.JsonMode);
            Assert.Contains("2026-11-13", request.SystemPrompt);
            Assert.Contains("提前30分钟提醒我", request.UserPrompt);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_Should_Keep_Reminder_From_Json()
    {
        var client = new FakeLlmClient
        {
            Response = "{\"title\":\"社联培训会\",\"start\":\"2026-11-24T20:45:00\"," +
                       "\"reminder_minutes_before\":30,\"all_day\":false}"
        };
        var service = new ScheduleExtractionService(client);
        var imagePath = CreateTempImage();

        try
        {
            var result = await service.ExtractAsync(imagePath, "提前30提醒我");

            Assert.Equal(30, result.Draft.ReminderMinutesBefore);
            Assert.Equal(
                "2026年11月24日20：15",
                TimeFormat.FormatReminderTime(result.Draft.StartLocal.AddMinutes(-result.Draft.ReminderMinutesBefore)));
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_Should_Throw_Chinese_Error_When_Model_Returns_Garbage()
    {
        var client = new FakeLlmClient { Response = "抱歉，我无法识别这张图片。" };
        var service = new ScheduleExtractionService(client);
        var imagePath = CreateTempImage();

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExtractAsync(imagePath, null));

            Assert.Contains("日程解析失败", ex.Message);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task ExtractAsync_Should_Throw_When_Image_Missing()
    {
        var service = new ScheduleExtractionService(new FakeLlmClient { Response = ValidJson });
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ExtractAsync(missingPath, null));

        Assert.Contains("截图不存在", ex.Message);
    }

    [Fact]
    public async Task ExtractAsync_Should_Pass_Cancellation_Token()
    {
        var client = new FakeLlmClient { Response = ValidJson };
        var service = new ScheduleExtractionService(client);
        var imagePath = CreateTempImage();

        try
        {
            using var cts = new CancellationTokenSource();
            await service.ExtractAsync(imagePath, null, cts.Token);

            Assert.True(client.ReceivedToken.CanBeCanceled);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    private static string CreateTempImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AutoAddSchedule-LlmTests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        return path;
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        public string Response { get; init; } = string.Empty;

        public LlmRequest? LastRequest { get; private set; }

        public CancellationToken ReceivedToken { get; private set; }

        public Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            ReceivedToken = cancellationToken;
            return Task.FromResult(Response);
        }

        public Task<string> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("连接成功");
    }
}
