using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SpinePet.Tests;

public sealed class IconDownloaderTests : IDisposable
{
    private static readonly TimeSpan ProcessTimeout =
        TimeSpan.FromSeconds(20);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctionPaths = [];

    public IconDownloaderTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task ResourceIdArrayLimitsListOnlyPlanToRequestedSkins()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-filter");
        WriteStandingSkeleton(resourceRoot, "Rapi", "01", "c010_01");
        WriteStandingSkeleton(resourceRoot, "Rapi", "02", "c010_02");
        WriteStandingSkeleton(resourceRoot, "Anis", "00", "c017_00");
        string catalogPath = WriteCatalog(
            "filter-catalog.json",
            "c010_01",
            "c010_02",
            "c017_00");

        ProcessResult result = await RunIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            "C010_01",
            "c010_01",
            "c010_02");

        Assert.True(
            result.ExitCode == 0,
            $"stdout: {result.StandardOutput}\nstderr: {result.StandardError}");
        Assert.Contains("c010_01", result.StandardOutput);
        Assert.Contains("c010_02", result.StandardOutput);
        Assert.DoesNotContain("c017_00", result.StandardOutput);
        Assert.Contains("matched 2 icon(s)", result.StandardOutput);
    }

    [Fact]
    public async Task OmittingResourceIdKeepsTheFullLocalPlan()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-all");
        WriteStandingSkeleton(resourceRoot, "Rapi", "02", "c010_02");
        WriteStandingSkeleton(resourceRoot, "Anis", "00", "c017_00");
        string catalogPath = WriteCatalog(
            "all-catalog.json",
            "c010_02",
            "c017_00");

        ProcessResult result = await RunIconDownloaderAsync(
            resourceRoot,
            catalogPath);

        Assert.True(
            result.ExitCode == 0,
            $"stdout: {result.StandardOutput}\nstderr: {result.StandardError}");
        Assert.Contains("c010_02", result.StandardOutput);
        Assert.Contains("c017_00", result.StandardOutput);
        Assert.Contains("matched 2 icon(s)", result.StandardOutput);
    }

    [Fact]
    public async Task UnknownRequestedResourceIdFailsClearly()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-unknown");
        WriteStandingSkeleton(resourceRoot, "Rapi", "00", "c010_00");
        string catalogPath = WriteCatalog(
            "unknown-catalog.json",
            "c010_00");

        ProcessResult result = await RunIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            "c999_00");

        Assert.NotEqual(0, result.ExitCode);
        string output = result.StandardError + result.StandardOutput;
        Assert.Contains("c999_00", output);
        Assert.Contains(
            "not found in a local standing directory",
            output,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestedResourceWithoutCatalogBundleFailsClearly()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-catalog");
        WriteStandingSkeleton(resourceRoot, "Rapi", "02", "c010_02");
        string catalogPath = WriteCatalog(
            "missing-bundle-catalog.json",
            "c010_00");

        ProcessResult result = await RunIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            "c010_02");

        Assert.NotEqual(0, result.ExitCode);
        string output = result.StandardError + result.StandardOutput;
        Assert.Contains("c010_02", output);
        Assert.Contains(
            "No HD icon bundle was found",
            output,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetSkinDirectorySelectsOnlyThatDuplicateResourceId()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-target");
        WriteStandingSkeleton(resourceRoot, "First", "02", "c010_02");
        WriteStandingSkeleton(resourceRoot, "Duplicate", "02", "c010_02");
        string targetSkinDirectory = Path.Combine(
            resourceRoot,
            "First",
            "02");
        string catalogPath = WriteCatalog(
            "target-catalog.json",
            "c010_02");

        ProcessResult result = await RunTargetIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            targetSkinDirectory,
            "c010_02");

        Assert.True(
            result.ExitCode == 0,
            $"stdout: {result.StandardOutput}\nstderr: {result.StandardError}");
        Assert.Contains(
            $"Target Skin directory: {targetSkinDirectory}",
            result.StandardOutput);
        Assert.Contains("matched 1 icon(s)", result.StandardOutput);
        Assert.DoesNotContain(
            Path.Combine(resourceRoot, "Duplicate", "02"),
            result.StandardOutput);
    }

    [Fact]
    public async Task TargetSkinDirectoryOutsideResourceRootIsRejected()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-boundary");
        Directory.CreateDirectory(resourceRoot);
        string outsideSkinDirectory = Path.Combine(
            _temporaryDirectory,
            "outside-skin");
        WriteSkinStandingSkeleton(outsideSkinDirectory, "c010_02");
        string catalogPath = WriteCatalog(
            "boundary-catalog.json",
            "c010_02");

        ProcessResult result = await RunTargetIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            outsideSkinDirectory,
            "c010_02");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "strictly inside the resource directory",
            result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResourceRootCannotBeUsedAsTargetSkinDirectory()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-as-target");
        WriteSkinStandingSkeleton(resourceRoot, "c010_02");
        string catalogPath = WriteCatalog(
            "root-target-catalog.json",
            "c010_02");

        ProcessResult result = await RunTargetIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            resourceRoot,
            "c010_02");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "strictly inside the resource directory",
            result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetSkinDirectoryRequiresAnExactSkeletonFileName()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-exact");
        string targetSkinDirectory = Path.Combine(resourceRoot, "Rapi", "02");
        WriteSkinStandingSkeleton(targetSkinDirectory, "c010_02_extra");
        string catalogPath = WriteCatalog(
            "exact-catalog.json",
            "c010_02");

        ProcessResult result = await RunTargetIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            targetSkinDirectory,
            "c010_02");

        Assert.NotEqual(0, result.ExitCode);
        string output = result.StandardError + result.StandardOutput;
        Assert.Contains("c010_02.skel", output);
        Assert.Contains("found 0", output);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("standing")]
    [InlineData("icons")]
    public async Task TargetSkinDirectoryRejectsReparsePointInManagedPaths(
        string linkedPath)
    {
        string resourceRoot = Path.Combine(
            _temporaryDirectory,
            $"res-reparse-{linkedPath}");
        string characterDirectory = Path.Combine(resourceRoot, "Rapi");
        string targetSkinDirectory = Path.Combine(characterDirectory, "02");
        Directory.CreateDirectory(characterDirectory);

        switch (linkedPath)
        {
            case "target":
                string externalSkin = Path.Combine(
                    _temporaryDirectory,
                    "external-target");
                WriteSkinStandingSkeleton(externalSkin, "c010_02");
                CreateJunction(targetSkinDirectory, externalSkin);
                break;
            case "standing":
                Directory.CreateDirectory(targetSkinDirectory);
                string externalStanding = Path.Combine(
                    _temporaryDirectory,
                    "external-standing");
                Directory.CreateDirectory(externalStanding);
                File.WriteAllBytes(
                    Path.Combine(externalStanding, "c010_02.skel"),
                    [1]);
                CreateJunction(
                    Path.Combine(targetSkinDirectory, "standing"),
                    externalStanding);
                break;
            case "icons":
                WriteSkinStandingSkeleton(targetSkinDirectory, "c010_02");
                string externalIcons = Path.Combine(
                    _temporaryDirectory,
                    "external-icons");
                Directory.CreateDirectory(externalIcons);
                CreateJunction(
                    Path.Combine(targetSkinDirectory, "icons"),
                    externalIcons);
                break;
        }

        string catalogPath = WriteCatalog(
            $"reparse-{linkedPath}-catalog.json",
            "c010_02");

        ProcessResult result = await RunTargetIconDownloaderAsync(
            resourceRoot,
            catalogPath,
            targetSkinDirectory,
            "c010_02");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "reparse point",
            result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteStandingSkeleton(
        string resourceRoot,
        string characterName,
        string skinCode,
        string resourceId)
    {
        string standingDirectory = Path.Combine(
            resourceRoot,
            characterName,
            skinCode,
            "standing");
        Directory.CreateDirectory(standingDirectory);
        File.WriteAllBytes(
            Path.Combine(standingDirectory, $"{resourceId}.skel"),
            [1]);
    }

    private static void WriteSkinStandingSkeleton(
        string skinDirectory,
        string resourceId)
    {
        string standingDirectory = Path.Combine(skinDirectory, "standing");
        Directory.CreateDirectory(standingDirectory);
        File.WriteAllBytes(
            Path.Combine(standingDirectory, $"{resourceId}.skel"),
            [1]);
    }

    private void CreateJunction(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        Directory.CreateDirectory(targetPath);
        ProcessStartInfo startInfo = new()
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Command Prompt could not be started for junction testing.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"stdout: {standardOutput}\nstderr: {standardError}");
        _junctionPaths.Add(linkPath);
    }

    private string WriteCatalog(string fileName, params string[] resourceIds)
    {
        string path = Path.Combine(_temporaryDirectory, fileName);
        object[] records = resourceIds
            .Select(resourceId => new
            {
                internal_id =
                    "{NK.Addressable.HostConst.HostDatapackLoadURL}\\" +
                    $"icons-char-mi(hd)_assets_mi_{resourceId}_s_" +
                    "0123456789abcdef.bundle"
            })
            .Cast<object>()
            .ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(records));
        return path;
    }

    private static async Task<ProcessResult> RunIconDownloaderAsync(
        string resourceRoot,
        string catalogPath,
        params string[] resourceIds)
    {
        return await RunIconDownloaderCoreAsync(
            resourceRoot,
            catalogPath,
            targetSkinDirectory: null,
            resourceIds);
    }

    private static Task<ProcessResult> RunTargetIconDownloaderAsync(
        string resourceRoot,
        string catalogPath,
        string targetSkinDirectory,
        string resourceId) =>
        RunIconDownloaderCoreAsync(
            resourceRoot,
            catalogPath,
            targetSkinDirectory,
            [resourceId]);

    private static async Task<ProcessResult> RunIconDownloaderCoreAsync(
        string resourceRoot,
        string catalogPath,
        string? targetSkinDirectory,
        string[] resourceIds)
    {
        string repositoryRoot = FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "tools",
            "icons-downloader",
            "Update-CharacterIcons.ps1");
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ResourceDirectory");
        startInfo.ArgumentList.Add(resourceRoot);
        startInfo.ArgumentList.Add("-CatalogPath");
        startInfo.ArgumentList.Add(catalogPath);
        startInfo.ArgumentList.Add("-BaseUri");
        startInfo.ArgumentList.Add("https://example.invalid/dp/");
        startInfo.ArgumentList.Add("-ListOnly");
        if (!string.IsNullOrWhiteSpace(targetSkinDirectory))
        {
            startInfo.ArgumentList.Add("-TargetSkinDirectory");
            startInfo.ArgumentList.Add(targetSkinDirectory);
        }
        if (resourceIds.Length > 0)
        {
            startInfo.ArgumentList.Add("-ResourceId");
            startInfo.ArgumentList.Add(string.Join(',', resourceIds));
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "PowerShell could not be started for icon downloader testing.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(ProcessTimeout);
        await process.WaitForExitAsync(timeout.Token);
        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Repository root not found.");
    }

    public void Dispose()
    {
        foreach (string junctionPath in _junctionPaths
                     .OrderByDescending(path => path.Length))
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }
        }

        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
