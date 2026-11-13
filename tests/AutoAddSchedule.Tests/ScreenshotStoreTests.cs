using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoAddSchedule.Services;

namespace AutoAddSchedule.Tests;

/// <summary>
/// ScreenshotStore 单元测试：文件命名与落盘、目录自动创建、保留数量清理、异常处理。
/// 全部使用临时目录与内存构造的位图，不依赖真实屏幕与用户环境。
/// </summary>
public sealed class ScreenshotStoreTests : IDisposable
{
    private readonly string _directory;

    public ScreenshotStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AutoAddScheduleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    [Fact]
    public async Task SaveAsync_Should_Create_Png_File_With_Timestamp_Name()
    {
        var store = new ScreenshotStore(_directory, 60);

        var path = await store.SaveAsync(CreateImage(4, 3));

        Assert.True(File.Exists(path));
        Assert.Equal(Path.GetFullPath(_directory), Path.GetFullPath(Path.GetDirectoryName(path)!));
        Assert.Matches(@"^\d{8}_\d{6}_\d{3}\.png$", Path.GetFileName(path));

        // 落盘内容应是可解码的 PNG，且尺寸与传入位图一致。
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Single(decoder.Frames);
        Assert.Equal(4, decoder.Frames[0].PixelWidth);
        Assert.Equal(3, decoder.Frames[0].PixelHeight);
    }

    [Fact]
    public async Task SaveAsync_Should_Create_Directory_When_Missing()
    {
        var nested = Path.Combine(_directory, "nested", "screenshots");
        var store = new ScreenshotStore(nested, 60);

        var path = await store.SaveAsync(CreateImage());

        Assert.True(Directory.Exists(nested));
        Assert.True(File.Exists(path));
        Assert.StartsWith(nested, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_Should_Not_Overwrite_When_Called_Twice()
    {
        var store = new ScreenshotStore(_directory, 60);

        var first = await store.SaveAsync(CreateImage());
        var second = await store.SaveAsync(CreateImage());

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(2, Directory.GetFiles(_directory, "*.png").Length);
    }

    [Fact]
    public async Task SaveAsync_Should_Throw_Chinese_Exception_When_Directory_Is_Blocked()
    {
        // 用同名文件占位，模拟截图目录不可用（不需要修改文件系统权限）。
        var blockedPath = Path.Combine(_directory, "blocked");
        await File.WriteAllTextAsync(blockedPath, "occupied");
        var store = new ScreenshotStore(blockedPath, 60);

        var exception = await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(CreateImage()));

        Assert.Contains("截图", exception.Message);
    }

    [Fact]
    public void Cleanup_Should_Keep_Only_Latest_N()
    {
        var paths = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            // 时间从旧到新：删除后应保留 index 2、3、4。
            paths.Add(CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-10 + i), "shot_" + i + ".png"));
        }

        var store = new ScreenshotStore(_directory, 3);
        var deleted = store.Cleanup();

        Assert.Equal(2, deleted);
        var remaining = Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName).ToList();
        Assert.Equal(3, remaining.Count);
        Assert.Contains("shot_4.png", remaining);
        Assert.Contains("shot_3.png", remaining);
        Assert.Contains("shot_2.png", remaining);
        Assert.DoesNotContain("shot_0.png", remaining);
        Assert.DoesNotContain("shot_1.png", remaining);
    }

    [Fact]
    public void Cleanup_Should_Ignore_Non_Image_Files()
    {
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-3), "a.png");
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-2), "b.jpg");
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-1), "c.jpeg");
        File.WriteAllText(Path.Combine(_directory, "note.txt"), "保留说明文件");
        File.WriteAllText(Path.Combine(_directory, "data.json"), "{}");

        var store = new ScreenshotStore(_directory, 1);
        var deleted = store.Cleanup();

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(Path.Combine(_directory, "note.txt")));
        Assert.True(File.Exists(Path.Combine(_directory, "data.json")));
        Assert.Single(Directory.GetFiles(_directory, "*.jpeg"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Cleanup_Should_Keep_At_Least_One_When_Retention_Is_Not_Positive(int retention)
    {
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-3), "old_a.png");
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-2), "old_b.png");
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-1), "old_c.png");

        var store = new ScreenshotStore(_directory, retention);
        var deleted = store.Cleanup();

        Assert.Equal(2, deleted);
        Assert.Single(Directory.GetFiles(_directory, "*.png"));
    }

    [Fact]
    public void Cleanup_Should_Return_Zero_When_Nothing_To_Delete()
    {
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-3), "a.png");
        CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-2), "b.png");

        var store = new ScreenshotStore(_directory, 5);

        Assert.Equal(0, store.Cleanup());
        Assert.Equal(2, Directory.GetFiles(_directory, "*.png").Length);
    }

    [Fact]
    public void Cleanup_Should_Return_Zero_When_Directory_Does_Not_Exist()
    {
        var store = new ScreenshotStore(Path.Combine(_directory, "not_exists"), 5);

        Assert.Equal(0, store.Cleanup());
    }

    [Fact]
    public void Cleanup_Should_Read_Retention_From_Provider_Each_Time()
    {
        var paths = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            paths.Add(CreateDummyImageFile(DateTime.UtcNow.AddMinutes(-10 + i), "shot_" + i + ".png"));
        }

        var retention = 5;
        var store = new ScreenshotStore(_directory, () => retention);

        Assert.Equal(0, store.Cleanup());

        retention = 2;
        Assert.Equal(3, store.Cleanup());
        Assert.Equal(2, Directory.GetFiles(_directory, "*.png").Length);
    }

    [Fact]
    public void Directory_Should_Return_Injected_Path()
    {
        var store = new ScreenshotStore(_directory, 60);

        Assert.Equal(_directory, store.Directory);
    }

    private string CreateDummyImageFile(DateTime lastWriteUtc, string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static BitmapSource CreateImage(int width = 4, int height = 4)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x30;     // B
            pixels[i + 1] = 0x80; // G
            pixels[i + 2] = 0xE0; // R
            pixels[i + 3] = 0xFF; // A
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }
}