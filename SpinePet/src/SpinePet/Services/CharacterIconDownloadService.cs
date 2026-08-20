using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

public sealed class CharacterIconDownloadService
{
    private const int MaximumDiagnosticCharacters = 4096;

    private readonly string _downloaderScriptPath;
    private readonly string _powerShellCommand;
    private readonly string _resourceDirectory;

    public CharacterIconDownloadService(
        string? downloaderScriptPath = null,
        string powerShellCommand = "powershell.exe",
        string? resourceDirectory = null)
    {
        _downloaderScriptPath = downloaderScriptPath ??
            AppPaths.CharacterIconDownloaderScript;
        _powerShellCommand = powerShellCommand;
        _resourceDirectory = Path.GetFullPath(
            resourceDirectory ?? AppPaths.ResourceDirectory);
    }

    public async Task<CharacterIconDownloadResult> DownloadMissingAsync(
        CharacterResourceFiles standingResources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(standingResources);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(
                standingResources.ResourceType,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                standingResources,
                string.Empty,
                "Automatic icon download requires standing resources.");
        }

        CharacterIdentity identity = standingResources.Identity;
        string resourceName = identity.ResourceName.Trim();
        if (!IsValidResourceName(resourceName) ||
            string.IsNullOrWhiteSpace(identity.CharacterCode) ||
            string.IsNullOrWhiteSpace(identity.SkinCode) ||
            !string.Equals(
                resourceName,
                $"c{identity.CharacterCode}_{identity.SkinCode}",
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                standingResources,
                string.Empty,
                "The character identity is not safe for an icon download.");
        }

        string iconPath = string.Empty;
        string skinDirectory;
        try
        {
            string? standingDirectory = Path.GetDirectoryName(
                Path.GetFullPath(standingResources.SkeletonPath));
            if (string.IsNullOrWhiteSpace(standingDirectory) ||
                !string.Equals(
                    Path.GetFileName(
                        Path.TrimEndingDirectorySeparator(standingDirectory)),
                    CharacterResourceTypes.Standing,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    standingResources,
                    string.Empty,
                    "The skeleton is not inside a standing resource directory.");
            }

            skinDirectory = Path.GetDirectoryName(standingDirectory) ??
                throw new InvalidOperationException(
                    "The Skin directory for the standing resources could not be resolved.");
            skinDirectory = ValidateManagedSkinDirectory(
                skinDirectory,
                identity.SkinCode,
                identity.CharacterCode);
            iconPath = Path.Combine(
                skinDirectory,
                CharacterResourceTypes.Icons,
                $"{resourceName}_icon.png");
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or IOException or
            UnauthorizedAccessException or NotSupportedException)
        {
            return Failure(
                standingResources,
                iconPath,
                "Automatic icon download refused the Skin directory: " +
                exception.Message);
        }

        if (File.Exists(iconPath))
        {
            return new CharacterIconDownloadResult(
                CharacterIconDownloadStatus.AlreadyPresent,
                resourceName,
                iconPath);
        }

        if (!File.Exists(_downloaderScriptPath))
        {
            return Failure(
                standingResources,
                iconPath,
                $"The character icon downloader script was not found: " +
                _downloaderScriptPath);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = _powerShellCommand,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(_downloaderScriptPath);
        startInfo.ArgumentList.Add("-ResourceDirectory");
        startInfo.ArgumentList.Add(_resourceDirectory);
        startInfo.ArgumentList.Add("-ResourceId");
        startInfo.ArgumentList.Add(resourceName);
        startInfo.ArgumentList.Add("-TargetSkinDirectory");
        startInfo.ArgumentList.Add(skinDirectory);

        try
        {
            using Process process = Process.Start(startInfo) ??
                throw new InvalidOperationException(
                    "PowerShell did not return an icon downloader process.");
            Task<string> standardOutputTask =
                process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> standardErrorTask =
                process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryTerminateProcessTree(process);
                await ObserveProcessExitAsync(process).ConfigureAwait(false);
                await ObserveOutputDrainAsync(
                        standardOutputTask,
                        standardErrorTask)
                    .ConfigureAwait(false);
                throw;
            }

            string standardOutput = BoundDiagnostic(
                await standardOutputTask.ConfigureAwait(false));
            string standardError = BoundDiagnostic(
                await standardErrorTask.ConfigureAwait(false));

            if (process.ExitCode != 0)
            {
                string detail = FirstNonBlank(standardError, standardOutput);
                string message =
                    $"Icon download failed with exit code {process.ExitCode}.";
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message += $" {detail}";
                }

                return new CharacterIconDownloadResult(
                    CharacterIconDownloadStatus.Failed,
                    resourceName,
                    iconPath,
                    message,
                    process.ExitCode,
                    standardOutput,
                    standardError);
            }

            if (!File.Exists(iconPath))
            {
                return new CharacterIconDownloadResult(
                    CharacterIconDownloadStatus.Failed,
                    resourceName,
                    iconPath,
                    "The icon downloader completed successfully but did not " +
                    $"create the expected file: {iconPath}",
                    process.ExitCode,
                    standardOutput,
                    standardError);
            }

            return new CharacterIconDownloadResult(
                CharacterIconDownloadStatus.Downloaded,
                resourceName,
                iconPath,
                ExitCode: process.ExitCode,
                StandardOutput: standardOutput,
                StandardError: standardError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or
            InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Failure(
                standingResources,
                iconPath,
                $"The character icon downloader could not be started or " +
                $"completed: {exception.Message}");
        }
    }

    private static CharacterIconDownloadResult Failure(
        CharacterResourceFiles resources,
        string iconPath,
        string message) =>
        new(
            CharacterIconDownloadStatus.Failed,
            resources.Identity.ResourceName,
            iconPath,
            message);

    private static bool IsValidResourceName(string resourceName)
    {
        return !string.IsNullOrWhiteSpace(resourceName) &&
            string.Equals(
                Path.GetFileName(resourceName),
                resourceName,
                StringComparison.Ordinal) &&
            resourceName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ??
        string.Empty;

    private static string BoundDiagnostic(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= MaximumDiagnosticCharacters
            ? trimmed
            : trimmed[..MaximumDiagnosticCharacters];
    }

    private string ValidateManagedSkinDirectory(
        string skinDirectory,
        string expectedSkinCode,
        string expectedCharacterCode)
    {
        string resolvedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(_resourceDirectory));
        string resolvedSkin = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(skinDirectory));
        string relativePath = Path.GetRelativePath(
            resolvedRoot,
            resolvedSkin);
        string[] segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(relativePath) ||
            segments.Length != 2 ||
            segments.Any(segment => segment is "." or "..") ||
            !string.Equals(
                segments[1],
                expectedSkinCode,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The path is not the selected managed character Skin.");
        }

        DirectoryInfo? current = new(resolvedSkin);
        while (current != null)
        {
            current.Refresh();
            if (current.Exists &&
                current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"The path contains a reparse point: {current.FullName}");
            }

            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(current.FullName),
                    resolvedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = current.Parent;
        }

        if (current == null)
        {
            throw new InvalidOperationException(
                "The Skin directory is outside the resource root.");
        }

        CharacterResourceDirectorySafety.EnsureTreeOwnedByCharacter(
            resolvedSkin,
            expectedCharacterCode,
            reason => new InvalidOperationException(
                $"The Skin directory {reason}."));
        return resolvedSkin;
    }

    private static void TryTerminateProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited while cancellation was being handled.
        }
        catch (NotSupportedException)
        {
            // Best-effort cancellation still propagates to the caller.
        }
        catch (Win32Exception)
        {
            // Best-effort cancellation still propagates to the caller.
        }
    }

    private static async Task ObserveProcessExitAsync(Process process)
    {
        try
        {
            using CancellationTokenSource timeout =
                new(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Do not indefinitely block application shutdown on a child process.
        }
        catch (InvalidOperationException)
        {
            // The process has already exited or has no associated handle.
        }
    }

    private static async Task ObserveOutputDrainAsync(params Task[] tasks)
    {
        Task drain = Task.WhenAll(tasks);
        try
        {
            await drain.WaitAsync(TimeSpan.FromSeconds(3))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = drain.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or ObjectDisposedException)
        {
            // Cancellation has priority over diagnostics from closing pipes.
        }
    }
}
