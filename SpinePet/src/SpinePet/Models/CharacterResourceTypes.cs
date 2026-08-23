namespace SpinePet.Models;

public static class CharacterResourceTypes
{
    public const string Standing = "standing";
    public const string Aim = "aim";
    public const string Cover = "cover";
    public const string Icons = "icons";

    public static IReadOnlyList<string> Renderable { get; } =
        [Standing, Aim, Cover];

    public static IReadOnlyList<string> All { get; } =
        [Standing, Aim, Cover, Icons];

    public static bool IsRenderable(string? resourceType) =>
        Renderable.Contains(resourceType, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? resourceType) =>
        All.Contains(resourceType, StringComparer.OrdinalIgnoreCase);

}
