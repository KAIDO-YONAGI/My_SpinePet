using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SpinePet.Tests;

public sealed class ResourceLayoutMigrationTests : IDisposable
{
    private static readonly TimeSpan ProcessTimeout =
        TimeSpan.FromSeconds(20);

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpinePet.Tests",
        Guid.NewGuid().ToString("N"));

    public ResourceLayoutMigrationTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task MigrationMovesEveryAtlasReferencedTexturePage()
    {
        string resourceRoot = Path.Combine(_temporaryDirectory, "res-pages");
        string legacyDirectory = Path.Combine(
            resourceRoot,
            "Legacy Rapi",
            "standing");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllBytes(
            Path.Combine(legacyDirectory, "c010_00.skel"),
            [1]);
        File.WriteAllText(
            Path.Combine(legacyDirectory, "c010_00.atlas"),
            "page-one.png\nsize: 1,1\npage-two.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(legacyDirectory, "page-one.png"),
            [2]);
        File.WriteAllBytes(
            Path.Combine(legacyDirectory, "page-two.png"),
            [3]);
        string characterNamesPath = WriteCharacterNames(
            "page-names.json",
            new Dictionary<string, string>
            {
                ["010"] = "Rapi"
            });

        ProcessResult result = await RunMigrationAsync(
            resourceRoot,
            characterNamesPath);

        Assert.True(
            result.ExitCode == 0,
            $"stdout: {result.StandardOutput}\nstderr: {result.StandardError}");
        string destination = Path.Combine(
            resourceRoot,
            "Rapi",
            "00",
            "standing");
        Assert.True(File.Exists(Path.Combine(destination, "c010_00.skel")));
        Assert.True(File.Exists(Path.Combine(destination, "c010_00.atlas")));
        Assert.True(File.Exists(Path.Combine(destination, "page-one.png")));
        Assert.True(File.Exists(Path.Combine(destination, "page-two.png")));
        Assert.False(Directory.Exists(legacyDirectory));
    }

    [Fact]
    public async Task MigrationRejectsTwoCharacterCodesSharingOneSkinDirectory()
    {
        string resourceRoot = Path.Combine(
            _temporaryDirectory,
            "res-collision");
        string firstSkeleton = WriteResourceSet(
            resourceRoot,
            "Legacy 511",
            "c511_00");
        string secondSkeleton = WriteResourceSet(
            resourceRoot,
            "Legacy 515",
            "c515_00");
        string characterNamesPath = WriteCharacterNames(
            "collision-names.json",
            new Dictionary<string, string>
            {
                ["511"] = "Cinderella",
                ["515"] = "Cinderella"
            });

        ProcessResult result = await RunMigrationAsync(
            resourceRoot,
            characterNamesPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "unique display names",
            result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(firstSkeleton));
        Assert.True(File.Exists(secondSkeleton));
        Assert.False(Directory.Exists(Path.Combine(
            resourceRoot,
            "Cinderella")));
    }

    [Fact]
    public async Task MigrationRejectsLegacyStateDirectoryReparsePoint()
    {
        string resourceRoot = Path.Combine(
            _temporaryDirectory,
            "res-reparse");
        string externalDirectory = Path.Combine(
            _temporaryDirectory,
            "outside-reparse");
        Directory.CreateDirectory(resourceRoot);
        Directory.CreateDirectory(externalDirectory);
        string characterDirectory = Path.Combine(resourceRoot, "Legacy Rapi");
        Directory.CreateDirectory(characterDirectory);
        string linkPath = Path.Combine(characterDirectory, "standing");
        try
        {
            Directory.CreateSymbolicLink(linkPath, externalDirectory);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
                IOException or
                PlatformNotSupportedException)
        {
            return;
        }

        string characterNamesPath = WriteCharacterNames(
            "reparse-names.json",
            new Dictionary<string, string>
            {
                ["010"] = "Rapi"
            });

        ProcessResult result = await RunMigrationAsync(
            resourceRoot,
            characterNamesPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "reparse point",
            result.StandardError + result.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(resourceRoot, "Rapi")));
    }

    private string WriteCharacterNames(
        string fileName,
        IReadOnlyDictionary<string, string> names)
    {
        string path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(names));
        return path;
    }

    private static string WriteResourceSet(
        string resourceRoot,
        string legacyCharacterName,
        string resourceName)
    {
        string directory = Path.Combine(
            resourceRoot,
            legacyCharacterName,
            "standing");
        Directory.CreateDirectory(directory);
        string skeletonPath = Path.Combine(
            directory,
            $"{resourceName}.skel");
        File.WriteAllBytes(skeletonPath, [1]);
        File.WriteAllText(
            Path.Combine(directory, $"{resourceName}.atlas"),
            $"{resourceName}.png\nsize: 1,1\n");
        File.WriteAllBytes(
            Path.Combine(directory, $"{resourceName}.png"),
            [2]);
        return skeletonPath;
    }

    private static async Task<ProcessResult> RunMigrationAsync(
        string resourceRoot,
        string characterNamesPath)
    {
        string scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "resource-layout",
            "Migrate-CharacterResources.ps1");
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
        startInfo.ArgumentList.Add("-CharacterNamesPath");
        startInfo.ArgumentList.Add(characterNamesPath);

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "PowerShell could not be started for migration testing.");
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
