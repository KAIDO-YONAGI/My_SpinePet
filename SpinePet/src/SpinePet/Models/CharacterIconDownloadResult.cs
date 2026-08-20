namespace SpinePet.Models;

public enum CharacterIconDownloadStatus
{
    Downloaded,
    AlreadyPresent,
    Failed
}

public sealed record CharacterIconDownloadResult(
    CharacterIconDownloadStatus Status,
    string ResourceName,
    string IconPath,
    string? ErrorMessage = null,
    int? ExitCode = null,
    string StandardOutput = "",
    string StandardError = "")
{
    public bool IsSuccess =>
        Status is CharacterIconDownloadStatus.Downloaded or
            CharacterIconDownloadStatus.AlreadyPresent;

    public bool WasDownloaded =>
        Status == CharacterIconDownloadStatus.Downloaded;
}
