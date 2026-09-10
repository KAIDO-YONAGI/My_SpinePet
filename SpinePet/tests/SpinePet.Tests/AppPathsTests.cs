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

    [Fact]
    public void BundledToolFileResolvesFromPackageRootToolsDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "SpinePet.Tests",
            Guid.NewGuid().ToString("N"));
        string appDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        string toolPath = Path.Combine(root, "tools", "extract_spine_bundle.py");
        File.WriteAllText(toolPath, string.Empty);

        try
        {
            // 发行包布局：应用在 app\，工具在包根 tools\。
            string result = AppPaths.ResolveBundledToolFile(
                "extract_spine_bundle.py",
                appDirectory + Path.DirectorySeparatorChar);

            // 目录名候选是 "Tools" 与 "tools"，而 Windows 文件系统不区分大小写，
            // 因此返回的字符串可能保留另一种拼写；这里只校验指向同一个文件。
            Assert.True(File.Exists(result));
            Assert.Equal(toolPath, result, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BundledToolFilePrefersInPlaceToolsDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "SpinePet.Tests",
            Guid.NewGuid().ToString("N"));
        string appDirectory = Path.Combine(root, "app");
        string inPlaceDirectory = Path.Combine(appDirectory, "Tools");
        Directory.CreateDirectory(inPlaceDirectory);
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        string inPlacePath = Path.Combine(inPlaceDirectory, "extract_spine_bundle.py");
        File.WriteAllText(inPlacePath, string.Empty);
        File.WriteAllText(
            Path.Combine(root, "tools", "extract_spine_bundle.py"),
            string.Empty);

        try
        {
            string result = AppPaths.ResolveBundledToolFile(
                "extract_spine_bundle.py",
                appDirectory + Path.DirectorySeparatorChar);

            Assert.Equal(inPlacePath, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BundledToolFileFallsBackToInPlacePathWhenNothingExists()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "SpinePet.Tests",
            Guid.NewGuid().ToString("N"));
        string appDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(appDirectory);

        try
        {
            string result = AppPaths.ResolveBundledToolFile(
                "extract_spine_bundle.py",
                appDirectory + Path.DirectorySeparatorChar);

            Assert.Equal(
                Path.Combine(appDirectory, "Tools", "extract_spine_bundle.py"),
                result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
