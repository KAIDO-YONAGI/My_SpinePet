using System.IO;
using SpinePet.Infrastructure;

namespace SpinePet.Tests;

public sealed class AppLoggerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public AppLoggerTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void PruneLogsKeepsActiveFileAndNewestArchives()
    {
        string activePath = CreateLog("spinepet-20260813.log", 80, 0);
        string newestArchive = CreateLog(
            "spinepet-20260812.log",
            80,
            -1);
        CreateLog("spinepet-20260811.log", 80, -2);
        CreateLog("spinepet-20260810.log", 80, -3);
        CreateLog("spinepet-20260809.log", 80, -4);

        AppLogger.PruneLogs(
            _temporaryDirectory,
            activePath,
            maximumFiles: 3,
            maximumTotalBytes: long.MaxValue);

        string[] remaining = Directory.GetFiles(
            _temporaryDirectory,
            "spinepet-*.log");
        Assert.Equal(3, remaining.Length);
        Assert.Contains(activePath, remaining);
        Assert.Contains(newestArchive, remaining);
    }

    [Fact]
    public void PruneLogsAppliesTotalSizeLimitWithoutDeletingActiveFile()
    {
        string activePath = CreateLog("spinepet-20260813.log", 90, 0);
        CreateLog("spinepet-20260812.log", 90, -1);
        CreateLog("spinepet-20260811.log", 90, -2);

        AppLogger.PruneLogs(
            _temporaryDirectory,
            activePath,
            maximumFiles: 10,
            maximumTotalBytes: 180);

        FileInfo[] remaining = new DirectoryInfo(_temporaryDirectory)
            .GetFiles("spinepet-*.log");
        Assert.True(File.Exists(activePath));
        Assert.True(remaining.Sum(file => file.Length) <= 180);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string CreateLog(
        string fileName,
        int byteCount,
        int ageInDays)
    {
        string path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllBytes(path, new byte[byteCount]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(ageInDays));
        return path;
    }
}
