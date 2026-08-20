using System.IO;

namespace SpinePet.Infrastructure;

internal static class AppLogger
{
    internal const int MaximumRetainedFiles = 14;
    internal const long MaximumTotalBytes = 5 * 1024 * 1024;
    internal const long MaximumActiveFileBytes = 1024 * 1024;
    private const int MaximumMessageCharacters = 4096;

    private static readonly Lock SyncRoot = new();
    private static DateTime _lastPrunedDate = DateTime.MinValue;

    public static void Write(string source, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            DateTime now = DateTime.Now;
            string logPath = Path.Combine(
                AppPaths.LogDirectory,
                $"spinepet-{now:yyyyMMdd}.log");
            string boundedMessage = message.Length <= MaximumMessageCharacters
                ? message
                : $"{message[..MaximumMessageCharacters]}…";
            string line =
                $"{now:yyyy-MM-dd HH:mm:ss.fff} [{source}] {boundedMessage}{Environment.NewLine}";

            lock (SyncRoot)
            {
                bool rolled = RollIfNeeded(logPath, now);
                if (rolled || _lastPrunedDate.Date != now.Date)
                {
                    PruneLogs(
                        AppPaths.LogDirectory,
                        logPath,
                        MaximumRetainedFiles,
                        MaximumTotalBytes);
                    _lastPrunedDate = now.Date;
                }

                File.AppendAllText(logPath, line);
            }
        }
        catch
        {
            // Diagnostics must never interrupt rendering or input handling.
        }
    }

    internal static void PruneLogs(
        string logDirectory,
        string activeLogPath,
        int maximumFiles,
        long maximumTotalBytes)
    {
        if (!Directory.Exists(logDirectory) ||
            maximumFiles < 1 ||
            maximumTotalBytes < 1)
        {
            return;
        }

        string protectedPath = Path.GetFullPath(activeLogPath);
        List<LogFile> files = [];
        foreach (string path in Directory.EnumerateFiles(
                     logDirectory,
                     "spinepet-*.log",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                FileInfo info = new(path);
                files.Add(new LogFile(
                    info.FullName,
                    info.Length,
                    info.LastWriteTimeUtc));
            }
            catch (IOException)
            {
                // A concurrently rotated or locked file can be retried next time.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging cleanup is best effort.
            }
        }

        files.Sort(static (left, right) =>
        {
            int timeComparison = right.LastWriteTimeUtc.CompareTo(
                left.LastWriteTimeUtc);
            return timeComparison != 0
                ? timeComparison
                : StringComparer.OrdinalIgnoreCase.Compare(
                    right.FullPath,
                    left.FullPath);
        });

        int existingFileCount = files.Count;
        foreach (LogFile file in files.AsEnumerable().Reverse())
        {
            if (existingFileCount <= maximumFiles)
            {
                break;
            }

            if (IsProtected(file, protectedPath) || !TryDelete(file))
            {
                continue;
            }

            file.Deleted = true;
            existingFileCount--;
        }

        long totalBytes = files
            .Where(file => !file.Deleted)
            .Sum(file => file.Length);
        foreach (LogFile file in files.AsEnumerable().Reverse())
        {
            if (totalBytes <= maximumTotalBytes)
            {
                break;
            }

            if (file.Deleted ||
                IsProtected(file, protectedPath) ||
                !TryDelete(file))
            {
                continue;
            }

            file.Deleted = true;
            totalBytes -= file.Length;
        }
    }

    private static bool RollIfNeeded(string logPath, DateTime now)
    {
        if (!File.Exists(logPath) ||
            new FileInfo(logPath).Length < MaximumActiveFileBytes)
        {
            return false;
        }

        string directory = Path.GetDirectoryName(logPath)!;
        string stem = Path.GetFileNameWithoutExtension(logPath);
        string archivePath = Path.Combine(
            directory,
            $"{stem}-{now:HHmmssfff}.log");
        for (int suffix = 1; File.Exists(archivePath); suffix++)
        {
            archivePath = Path.Combine(
                directory,
                $"{stem}-{now:HHmmssfff}-{suffix}.log");
        }

        File.Move(logPath, archivePath);
        return true;
    }

    private static bool IsProtected(LogFile file, string protectedPath) =>
        string.Equals(
            file.FullPath,
            protectedPath,
            StringComparison.OrdinalIgnoreCase);

    private static bool TryDelete(LogFile file)
    {
        try
        {
            File.Delete(file.FullPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class LogFile(
        string fullPath,
        long length,
        DateTime lastWriteTimeUtc)
    {
        public string FullPath { get; } = fullPath;
        public long Length { get; } = length;
        public DateTime LastWriteTimeUtc { get; } = lastWriteTimeUtc;
        public bool Deleted { get; set; }
    }
}
