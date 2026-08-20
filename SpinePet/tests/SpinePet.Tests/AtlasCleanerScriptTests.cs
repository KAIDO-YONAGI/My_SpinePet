using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpinePet.Tests;

public sealed class AtlasCleanerScriptTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public AtlasCleanerScriptTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task CleanerPreservesReferencedNamesAndRemovesWholeUnusedRegion()
    {
        const string referencedNameWithSpaces = "eye highlight L";
        const string referencedSimpleName = "kept_region";
        const string unusedWatermarkName = "tmwallsSc8CDvu5x";
        const string unusedBounds = "bounds:30,30,30,30";

        string atlasPath = Path.Combine(_temporaryDirectory, "test.atlas");
        string skeletonPath = Path.Combine(_temporaryDirectory, "test.skel");
        string texturePath = Path.Combine(_temporaryDirectory, "test.png");

        await File.WriteAllTextAsync(
            atlasPath,
            $"""
            test.png
            size:64,64
            filter:Linear,Linear
            {referencedNameWithSpaces}
            bounds:1,1,10,10
            rotate:90
            {unusedWatermarkName}
            {unusedBounds}
            rotate:90
            {referencedSimpleName}
            bounds:40,40,10,10
            """);
        await File.WriteAllBytesAsync(
            skeletonPath,
            Encoding.UTF8.GetBytes(
                $"binary-prefix {referencedNameWithSpaces} {referencedSimpleName} binary-suffix"));
        await File.WriteAllBytesAsync(texturePath, [0]);

        string scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "atlas-cleaner",
            "Clean-Atlas.ps1");
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-Folder");
        startInfo.ArgumentList.Add(_temporaryDirectory);

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start PowerShell.");
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"Cleaner failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");

        string cleanedAtlas = await File.ReadAllTextAsync(atlasPath);
        Assert.Contains(referencedNameWithSpaces, cleanedAtlas);
        Assert.Contains(referencedSimpleName, cleanedAtlas);
        Assert.DoesNotContain(unusedWatermarkName, cleanedAtlas);
        Assert.DoesNotContain(unusedBounds, cleanedAtlas);
        Assert.True(File.Exists($"{atlasPath}.bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static string FindRepositoryRoot(
        [CallerFilePath] string sourceFilePath = "")
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(sourceFilePath)!);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the SpinePet repository root.");
    }
}
