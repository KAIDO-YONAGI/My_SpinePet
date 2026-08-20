using System.Diagnostics;
using System.IO;
using SpinePet.Models;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterIconDownloadServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public CharacterIconDownloadServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task DownloadMissingAsyncReturnsAlreadyPresentWithoutStartingTool()
    {
        CharacterResourceFiles resources = CreateStandingResources("c010_02");
        string iconPath = GetExpectedIconPath(resources);
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        await File.WriteAllBytesAsync(iconPath, [1, 2, 3]);
        CharacterIconDownloadService service = new(
            Path.Combine(_temporaryDirectory, "missing.ps1"),
            "missing-powershell.exe",
            _temporaryDirectory);

        CharacterIconDownloadResult result =
            await service.DownloadMissingAsync(resources);

        Assert.True(result.IsSuccess);
        Assert.False(result.WasDownloaded);
        Assert.Equal(
            CharacterIconDownloadStatus.AlreadyPresent,
            result.Status);
        Assert.Equal(iconPath, result.IconPath);
    }

    [Fact]
    public async Task DownloadMissingAsyncTargetsOnlyCurrentSkin()
    {
        CharacterResourceFiles resources = CreateStandingResources("c010_02");
        CharacterResourceFiles otherSkin = CreateStandingResources("c010_00");
        string scriptPath = Path.Combine(_temporaryDirectory, "fake-download.ps1");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            param(
                [string]$ResourceDirectory,
                [string[]]$ResourceId,
                [string]$TargetSkinDirectory
            )
            $skeleton = Get-ChildItem `
                -LiteralPath (Join-Path $TargetSkinDirectory 'standing') `
                -Filter "$($ResourceId[0]).skel" -File |
                Select-Object -First 1
            $skinDirectory = $skeleton.Directory.Parent.FullName
            $iconDirectory = Join-Path $skinDirectory 'icons'
            New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null
            $iconPath = Join-Path $iconDirectory ($skeleton.BaseName + '_icon.png')
            [System.IO.File]::WriteAllBytes($iconPath, [byte[]](1, 2, 3))
            Write-Output (
                "TARGET=$($ResourceId[0]) " +
                "SKIN=$TargetSkinDirectory ROOT=$ResourceDirectory"
            )
            """);
        CharacterIconDownloadService service = new(
            scriptPath,
            resourceDirectory: _temporaryDirectory);

        CharacterIconDownloadResult result =
            await service.DownloadMissingAsync(resources);

        Assert.True(result.IsSuccess);
        Assert.True(result.WasDownloaded);
        Assert.Equal(CharacterIconDownloadStatus.Downloaded, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(result.IconPath));
        Assert.Contains(
            resources.Identity.ResourceName,
            result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Path.GetDirectoryName(
                Path.GetDirectoryName(resources.SkeletonPath)!)!,
            result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(GetExpectedIconPath(otherSkin)));
    }

    [Fact]
    public async Task DownloadMissingAsyncReturnsToolFailureDetails()
    {
        CharacterResourceFiles resources = CreateStandingResources("c010_02");
        string scriptPath = Path.Combine(_temporaryDirectory, "failed-download.ps1");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            param(
                [string]$ResourceDirectory,
                [string[]]$ResourceId,
                [string]$TargetSkinDirectory
            )
            [Console]::Error.WriteLine('catalog entry missing')
            exit 7
            """);
        CharacterIconDownloadService service = new(
            scriptPath,
            resourceDirectory: _temporaryDirectory);

        CharacterIconDownloadResult result =
            await service.DownloadMissingAsync(resources);

        Assert.False(result.IsSuccess);
        Assert.Equal(CharacterIconDownloadStatus.Failed, result.Status);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("exit code 7", result.ErrorMessage);
        Assert.Contains("catalog entry missing", result.ErrorMessage);
        Assert.Contains("catalog entry missing", result.StandardError);
    }

    [Fact]
    public async Task DownloadMissingAsyncPropagatesCancellationAndStopsProcess()
    {
        CharacterResourceFiles resources = CreateStandingResources("c010_02");
        string scriptPath = Path.Combine(_temporaryDirectory, "slow-download.ps1");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            param(
                [string]$ResourceDirectory,
                [string[]]$ResourceId,
                [string]$TargetSkinDirectory
            )
            Start-Sleep -Seconds 60
            """);
        CharacterIconDownloadService service = new(
            scriptPath,
            resourceDirectory: _temporaryDirectory);
        using CancellationTokenSource cancellation =
            new(TimeSpan.FromMilliseconds(500));
        Stopwatch stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadMissingAsync(resources, cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(GetExpectedIconPath(resources)));
    }

    [Fact]
    public async Task DownloadMissingAsyncReportsMissingPackagedScript()
    {
        CharacterResourceFiles resources = CreateStandingResources("c010_02");
        string missingScript = Path.Combine(_temporaryDirectory, "missing.ps1");
        CharacterIconDownloadService service = new(
            missingScript,
            resourceDirectory: _temporaryDirectory);

        CharacterIconDownloadResult result =
            await service.DownloadMissingAsync(resources);

        Assert.Equal(CharacterIconDownloadStatus.Failed, result.Status);
        Assert.Null(result.ExitCode);
        Assert.Contains(missingScript, result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadMissingAsyncRejectsSkinOutsideManagedRootBeforeTool()
    {
        CharacterResourceFiles resources = CreateStandingResources("c010_02");
        string managedRoot = Path.Combine(_temporaryDirectory, "managed-res");
        Directory.CreateDirectory(managedRoot);
        CharacterIconDownloadService service = new(
            Path.Combine(_temporaryDirectory, "missing.ps1"),
            "missing-powershell.exe",
            managedRoot);

        CharacterIconDownloadResult result =
            await service.DownloadMissingAsync(resources);

        Assert.Equal(CharacterIconDownloadStatus.Failed, result.Status);
        Assert.Contains(
            "selected managed character Skin",
            result.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script was not found", result.ErrorMessage);
    }

    private CharacterResourceFiles CreateStandingResources(string resourceName)
    {
        string skinCode = resourceName[(resourceName.IndexOf('_') + 1)..];
        string standingDirectory = Path.Combine(
            _temporaryDirectory,
            "Rapi",
            skinCode,
            CharacterResourceTypes.Standing);
        Directory.CreateDirectory(standingDirectory);
        string skeletonPath = Path.Combine(
            standingDirectory,
            $"{resourceName}.skel");
        string atlasPath = Path.Combine(
            standingDirectory,
            $"{resourceName}.atlas");
        string texturePath = Path.Combine(
            standingDirectory,
            $"{resourceName}.png");
        File.WriteAllBytes(skeletonPath, []);
        File.WriteAllText(atlasPath, $"{resourceName}.png\nsize: 1,1\n");
        File.WriteAllBytes(texturePath, []);

        return new CharacterResourceFiles(
            skeletonPath,
            atlasPath,
            texturePath,
            [],
            CharacterResourceTypes.Standing,
            new CharacterIdentity(
                resourceName,
                "010",
                skinCode,
                "Rapi"));
    }

    private static string GetExpectedIconPath(
        CharacterResourceFiles resources)
    {
        string standingDirectory =
            Path.GetDirectoryName(resources.SkeletonPath)!;
        string skinDirectory = Path.GetDirectoryName(standingDirectory)!;
        return Path.Combine(
            skinDirectory,
            CharacterResourceTypes.Icons,
            $"{resources.Identity.ResourceName}_icon.png");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
