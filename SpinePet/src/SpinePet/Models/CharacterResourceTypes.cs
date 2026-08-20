namespace SpinePet.Models;

public static class CharacterResourceTypes
{
    public const string Standing = "standing";
    public const string Icons = "icons";

    public static IReadOnlyList<string> Renderable { get; } =
        [Standing];

    public static IReadOnlyList<string> All { get; } =
        [Standing, Icons];

    public static bool IsRenderable(string? resourceType) =>
        Renderable.Contains(resourceType, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? resourceType) =>
        All.Contains(resourceType, StringComparer.OrdinalIgnoreCase);

}
