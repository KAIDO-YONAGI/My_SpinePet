namespace SpinePet.Rendering;

public sealed record CharacterRenderSnapshot(
    string CharacterId,
    bool IsLoading,
    bool IsVisible,
    IReadOnlyList<string> AnimationNames,
    double MaximumScale,
    double CurrentScale);
