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

    public static string ResourceDirectory { get; } =
        ResolveResourceDirectory(AppContext.BaseDirectory);

    public static string BundleExtractorScript { get; } =
        ResolveBundledFile(Path.Combine("Tools", "extract_spine_bundle.py"));

    public static string CharacterIconDownloaderScript { get; } =
        ResolveBundledFile(Path.Combine(
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
        foreach (string candidate in GetPortableBaseDirectories(
                     AppContext.BaseDirectory))
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

    internal static string ResolveResourceDirectory(string baseDirectory)
    {
        string resolvedBaseDirectory = Path.GetFullPath(baseDirectory);
        foreach (string candidate in GetPortableBaseDirectories(
                     resolvedBaseDirectory))
        {
            string candidateResources = Path.Combine(candidate, "res");
            if (Directory.Exists(candidateResources))
            {
                return candidateResources;
            }
        }

        string? projectDirectory = FindApplicationProjectDirectory(
            resolvedBaseDirectory);
        return Path.Combine(
            projectDirectory ?? resolvedBaseDirectory,
            "res");
    }

    private static IEnumerable<string> GetPortableBaseDirectories(
        string baseDirectory)
    {
        yield return baseDirectory;

        // The packaged layout keeps the payload in an app\ subfolder next to
        // config.json/res. Only that exact folder name opts into the parent
        // lookup, so a stray config.json somewhere above a flat install can
        // never be mistaken for SpinePet's portable configuration.
        string trimmedBaseDirectory = Path.TrimEndingDirectorySeparator(
            baseDirectory);
        if (!string.Equals(
                Path.GetFileName(trimmedBaseDirectory),
                "app",
                StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        string? parentDirectory = Path.GetDirectoryName(trimmedBaseDirectory);
        if (parentDirectory != null)
        {
            yield return parentDirectory;
        }
    }

    private static string? FindApplicationProjectDirectory(
        string baseDirectory)
    {
        DirectoryInfo? directory = new(baseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")) &&
                File.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "SpinePet",
                    "SpinePet.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ResolveBundledFile(string outputRelativePath) =>
        Path.Combine(AppContext.BaseDirectory, outputRelativePath);
}
