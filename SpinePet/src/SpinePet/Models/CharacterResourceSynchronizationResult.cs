namespace SpinePet.Models;

public sealed record CharacterResourceSynchronizationResult(
    int AddedCount,
    int UpdatedCount,
    int RemovedCount,
    int MergedCount)
{
    public bool HasChanges =>
        AddedCount > 0 ||
        UpdatedCount > 0 ||
        RemovedCount > 0 ||
        MergedCount > 0;
}
