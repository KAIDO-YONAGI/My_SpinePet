namespace SpinePet.Models;

public sealed record CharacterBundleImportResult(
    CharacterIdentity Identity,
    string ResourceType,
    string DestinationDirectory,
    CharacterResourceFiles? Resources = null,
    string? IconPath = null)
{
    public bool IsRenderable => Resources != null;

    public bool IsIcon => string.Equals(
        ResourceType,
        CharacterResourceTypes.Icons,
        StringComparison.OrdinalIgnoreCase);
}
