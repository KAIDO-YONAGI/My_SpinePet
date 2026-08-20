using System.IO;

namespace SpinePet.Infrastructure;

internal static class AppPaths
{
    private const string ApplicationDirectoryName = "SpinePet";
    internal const string DataDirectoryEnvironmentVariable =
        "SPINEPET_DATA_DIRECTORY";

    public static string LocalDataDirectory { get; } =
        ResolveLocalDataDirectory(Environment.GetEnvironmentVariable(
            DataDirectoryEnvironmentVariable));

    public static string ConfigFile { get; } = Path.Combine(
        LocalDataDirectory,
        "config.json");

    public static string LogDirectory { get; } = Path.Combine(
        LocalDataDirectory,
        "Logs");

    public static string ProjectRoot { get; } = FindProjectRoot();

    public static string ResourceDirectory { get; } = Path.Combine(
        ProjectRoot,
        "res");

    public static string BundleExtractorScript { get; } =
        ResolveBundledFile(
            Path.Combine(
                "src",
                "SpinePet",
                "Infrastructure",
                "Import",
                "Tools",
                "extract_spine_bundle.py"),
            Path.Combine("Tools", "extract_spine_bundle.py"));

    public static string CharacterIconDownloaderScript { get; } =
        ResolveBundledFile(
            Path.Combine(
                "tools",
                "icons-downloader",
                "Update-CharacterIcons.ps1"),
            Path.Combine(
                "Tools",
                "icons-downloader",
                "Update-CharacterIcons.ps1"));

    internal static string ResolveLocalDataDirectory(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string ResolveBundledFile(
        string sourceRelativePath,
        string outputRelativePath)
    {
        string sourcePath = Path.Combine(ProjectRoot, sourceRelativePath);
        return File.Exists(sourcePath)
            ? sourcePath
            : Path.Combine(AppContext.BaseDirectory, outputRelativePath);
    }
}
