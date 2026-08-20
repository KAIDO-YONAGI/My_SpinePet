namespace SpinePet.Models;

public sealed record CharacterBundleDescriptor(
    string ResourceName,
    string CharacterCode,
    string SkinCode,
    string ResourceType);
