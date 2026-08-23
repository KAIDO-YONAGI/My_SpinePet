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

    // Published layouts keep resources next to the executable, so the
    // exe-adjacent folder wins over the repository lookup (which only
    // applies to development runs from bin\).
    public static string ResourceDirectory { get; } =
        ResolveResourceDirectory();

    public static string NikkeDbDirectory { get; } =
        ResolveNikkeDbDirectory();

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

        // Portable layout: a config.json next to the executable (or in the
        // parent folder when the payload lives in an app\ subfolder) keeps
        // all user state (config, logs) inside the application folder.
        foreach (string candidate in GetPortableBaseDirectories())
        {
            string portableConfigPath = Path.Combine(candidate, "config.json");
            if (File.Exists(portableConfigPath))
            {
                return candidate;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);
    }

    private static string ResolveResourceDirectory()
    {
        // Published layouts keep resources beside the executable or beside
        // the parent folder of an app\ payload; the repository lookup only
        // applies to development runs from bin\.
        foreach (string candidate in GetPortableBaseDirectories())
        {
            string candidateResources = Path.Combine(candidate, "res");
            if (Directory.Exists(candidateResources))
            {
                return candidateResources;
            }
        }

        return Path.Combine(ProjectRoot, "res");
    }

    private static string ResolveNikkeDbDirectory()
    {
        string[] candidates =
        [
            Path.Combine(ProjectRoot, "resources", "nikkedb"),
            Path.Combine(ProjectRoot, "..", "resources", "nikkedb")
        ];
        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static IEnumerable<string> GetPortableBaseDirectories()
    {
        yield return AppContext.BaseDirectory;

        // The packaged layout keeps the payload in an app\ subfolder next to
        // config.json/res. Only that exact folder name opts into the parent
        // lookup, so a stray config.json somewhere above a flat install can
        // never be mistaken for SpinePet's portable configuration.
        string baseDirectory = Path.TrimEndingDirectorySeparator(
            AppContext.BaseDirectory);
        if (!string.Equals(
                Path.GetFileName(baseDirectory),
                "app",
                StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        string? parentDirectory = Path.GetDirectoryName(baseDirectory);
        if (parentDirectory != null)
        {
            yield return parentDirectory;
        }
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
