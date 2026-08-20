namespace SpinePet.ViewModels;

public sealed record CharacterSkinOptionViewModel(
    string CharacterId,
    string CharacterCode,
    string ResourceName,
    string SkinCode,
    bool IsSelected)
{
    public string Label => string.IsNullOrWhiteSpace(SkinCode)
        ? "Skin —"
        : $"Skin {SkinCode}";
}
