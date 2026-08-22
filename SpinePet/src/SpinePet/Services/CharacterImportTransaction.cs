using System.Collections.Concurrent;
using System.IO;
using SpinePet.Infrastructure;

namespace SpinePet.Services;

internal sealed class CharacterImportTransaction(
    string resourceDirectory) : IDisposable
{
    private readonly string _resourceRoot =
        Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(resourceDirectory));
    private readonly List<string> _createdFiles = [];
    private readonly List<string> _createdDirectories = [];
    private bool _completed;

    public void CreateDirectory(string path)
    {
        string fullPath = EnsureWithinRoot(path);
        if (Directory.Exists(fullPath))
        {
            return;
        }

        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent) &&
            !Directory.Exists(parent))
        {
            CreateDirectory(parent);
        }

        Directory.CreateDirectory(fullPath);
        _createdDirectories.Add(fullPath);
    }

    public void CopyFile(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullDestination = EnsureWithinRoot(destinationPath);
        File.Copy(sourcePath, fullDestination, overwrite: false);
        _createdFiles.Add(fullDestination);
    }

    public void MoveFile(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullDestination = EnsureWithinRoot(destinationPath);
        File.Move(sourcePath, fullDestination);
        _createdFiles.Add(fullDestination);
    }

    public void Complete() => _completed = true;

    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        foreach (string file in _createdFiles.AsEnumerable().Reverse())
        {
            TryDeleteFile(file);
        }
        foreach (string directory in
                 _createdDirectories.AsEnumerable().Reverse())
        {
            TryDeleteEmptyDirectory(directory);
        }
    }

    private string EnsureWithinRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(_resourceRoot, fullPath);
        if (Path.IsPathRooted(relativePath) ||
            string.Equals(relativePath, "..", StringComparison.Ordinal) ||
            relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to modify an import path outside res.");
        }

        return fullPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"import-file-rollback-failed path={path} " +
                $"message={exception.Message}");
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(UnityBundleImportService),
                $"import-directory-rollback-failed path={path} " +
                $"message={exception.Message}");
        }
    }
}

internal static class CharacterImportTargetLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable Acquire(
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = GetGate(targetDirectory);
        gate.Wait(cancellationToken);
        return new Releaser(gate);
    }

    public static async Task<IDisposable> AcquireAsync(
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = GetGate(targetDirectory);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private static SemaphoreSlim GetGate(string targetDirectory)
    {
        string normalized = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(targetDirectory));
        return Gates.GetOrAdd(normalized, _ => new SemaphoreSlim(1, 1));
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
