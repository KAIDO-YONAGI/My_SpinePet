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
}
