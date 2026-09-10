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
        ResolveBundledToolFile(
            "extract_spine_bundle.py",
            AppContext.BaseDirectory);

    public static string CharacterIconDownloaderScript { get; } =
        ResolveBundledToolFile(
            Path.Combine("icons-downloader", "Update-CharacterIcons.ps1"),
            AppContext.BaseDirectory);

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

    // 发行包把运行时工具放在包根 tools\（app\ 的上一级），源码与构建输出布局则放在应用同级的
    // Tools\。与 res、config.json 一致：先查就地目录，再查上一级（仅当自身目录名为 app），
    // 都找不到时回退到就地路径，交给调用方报错。
    // 目录名在方法内取局部变量：静态初始化按声明顺序执行，若写成静态字段会晚于上方属性的
    // 初始化器，导致属性构造时读到 null。
    internal static string ResolveBundledToolFile(
        string toolRelativePath,
        string baseDirectory)
    {
        // 源码与构建输出里是 Tools\，发行包根目录是 tools\；Windows 不区分大小写，
        // 两种写法都查一遍以免依赖文件系统行为。
        string[] toolDirectoryNames = ["Tools", "tools"];
        string resolvedBaseDirectory = Path.GetFullPath(baseDirectory);
        foreach (string candidate in GetPortableBaseDirectories(
                     resolvedBaseDirectory))
        {
            foreach (string toolDirectoryName in toolDirectoryNames)
            {
                string path = Path.Combine(
                    candidate,
                    toolDirectoryName,
                    toolRelativePath);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return Path.Combine(
            resolvedBaseDirectory,
            toolDirectoryNames[0],
            toolRelativePath);
    }
}
