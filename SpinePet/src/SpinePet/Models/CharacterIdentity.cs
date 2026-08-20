namespace SpinePet.Models;

public sealed record CharacterIdentity(
    string ResourceName,
    string CharacterCode,
    string SkinCode,
    string DisplayName)
{
    public string SkinLabel =>
        string.IsNullOrWhiteSpace(SkinCode) ? "Skin —" : $"Skin {SkinCode}";
}
