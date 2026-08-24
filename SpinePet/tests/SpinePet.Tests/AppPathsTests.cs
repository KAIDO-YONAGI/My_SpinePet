using System.IO;
using SpinePet.Infrastructure;

namespace SpinePet.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void CharacterIconDownloaderResolvesForSourceAndBuildOutput()
    {
        string packagedPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "icons-downloader",
            "Update-CharacterIcons.ps1");

        Assert.True(File.Exists(AppPaths.CharacterIconDownloaderScript));
        Assert.True(File.Exists(packagedPath));
    }

    [Fact]
    public void ResolveLocalDataDirectoryUsesAbsoluteOverride()
    {
        string overridePath = Path.Combine(
            Path.GetTempPath(),
            "SpinePet.Performance",
            Guid.NewGuid().ToString("N"));

        string result = AppPaths.ResolveLocalDataDirectory(overridePath);

        Assert.Equal(Path.GetFullPath(overridePath), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveLocalDataDirectoryUsesDefaultForBlankOverride(
        string? overridePath)
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "SpinePet");

        string result = AppPaths.ResolveLocalDataDirectory(overridePath);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResourceDirectoryDoesNotTreatGitAncestorAsProjectRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "SpinePet.Tests",
            Guid.NewGuid().ToString("N"));
        string baseDirectory = Path.Combine(root, "workspace", "build", "bin");
        Directory.CreateDirectory(Path.Combine(root, "workspace", ".git"));
        Directory.CreateDirectory(Path.Combine(root, "workspace", "res"));
        Directory.CreateDirectory(baseDirectory);

        try
        {
            string result = AppPaths.ResolveResourceDirectory(baseDirectory);

            Assert.Equal(
                Path.Combine(baseDirectory, "res"),
                result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResourceDirectoryUsesOnlyValidatedSpinePetProject()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "SpinePet.Tests",
            Guid.NewGuid().ToString("N"));
        string projectDirectory = Path.Combine(root, "SpinePet");
        string baseDirectory = Path.Combine(
            projectDirectory,
            "src",
            "SpinePet",
            "bin",
            "Debug");
        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(Path.Combine(projectDirectory, "res"));
        Directory.CreateDirectory(Path.Combine(
            projectDirectory,
            "src",
            "SpinePet"));
        File.WriteAllText(
            Path.Combine(projectDirectory, "SpinePet.sln"),
            string.Empty);
        File.WriteAllText(
            Path.Combine(
                projectDirectory,
                "src",
                "SpinePet",
                "SpinePet.csproj"),
            string.Empty);

        try
        {
            string result = AppPaths.ResolveResourceDirectory(baseDirectory);

            Assert.Equal(
                Path.Combine(projectDirectory, "res"),
                result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
