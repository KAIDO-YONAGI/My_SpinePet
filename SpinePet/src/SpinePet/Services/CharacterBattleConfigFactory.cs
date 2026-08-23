using System.IO;
using Spine;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

internal static class CharacterBattleConfigFactory
{
    public static CharacterBattleConfig? TryCreate(
        CharacterResourceFiles standing,
        IEnumerable<CharacterResourceFiles> resources)
    {
        string? standingDirectory = Path.GetDirectoryName(standing.SkeletonPath);
        string? skinDirectory = standingDirectory == null
            ? null
            : Path.GetDirectoryName(standingDirectory);
        if (skinDirectory == null)
        {
            return null;
        }

        CharacterResourceFiles? aim = FindState(
            resources,
            skinDirectory,
            CharacterResourceTypes.Aim);
        CharacterResourceFiles? cover = FindState(
            resources,
            skinDirectory,
            CharacterResourceTypes.Cover);
        if (aim == null || cover == null)
        {
            return null;
        }

        IReadOnlyList<BattleAnimationMetadata> aimAnimations;
        IReadOnlyList<BattleAnimationMetadata> coverAnimations;
        try
        {
            aimAnimations = ReadAnimations(aim);
            coverAnimations = ReadAnimations(cover);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterBattleConfigFactory),
                $"battle-profile-skipped path={skinDirectory} " +
                $"message={exception.Message}");
            return null;
        }

        return new CharacterBattleConfig
        {
            Aim = CreateResource(aim),
            Cover = CreateResource(cover),
            Animations = ResolveAnimations(aimAnimations, coverAnimations)
        };
    }

    internal static CharacterBattleAnimationsConfig ResolveAnimations(
        IReadOnlyList<string> aimAnimations,
        IReadOnlyList<string> coverAnimations) =>
        ResolveAnimations(
            aimAnimations
                .Select(name => new BattleAnimationMetadata(name, true))
                .ToArray(),
            coverAnimations
                .Select(name => new BattleAnimationMetadata(name, true))
                .ToArray());

    internal static CharacterBattleAnimationsConfig ResolveAnimations(
        IReadOnlyList<BattleAnimationMetadata> aimAnimations,
        IReadOnlyList<BattleAnimationMetadata> coverAnimations)
    {
        string[] usableAimAnimations = aimAnimations
            .Where(animation => animation.HasTimelines)
            .Select(animation => animation.Name)
            .ToArray();
        string[] usableCoverAnimations = coverAnimations
            .Where(animation => animation.HasTimelines)
            .Select(animation => animation.Name)
            .ToArray();
        List<string> aimFireEffects = aimAnimations
            .Where(animation =>
                animation.HasTimelines &&
                animation.Name.StartsWith(
                    "aim_fire_",
                    StringComparison.OrdinalIgnoreCase))
            .Select(animation => animation.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetAimFireEffectOrder)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CharacterBattleAnimationsConfig
        {
            AimIdle = FindPreferred(
                usableAimAnimations,
                "aim_idle",
                name => ContainsAll(name, "aim", "idle"),
                name => name.Contains(
                    "idle",
                    StringComparison.OrdinalIgnoreCase)),
            ToAim = FindPreferred(
                usableAimAnimations,
                "to_aim",
                name => ContainsAll(name, "to", "aim")),
            AimFire = FindPreferred(
                usableAimAnimations,
                "aim_fire",
                name =>
                    ContainsAll(name, "aim", "fire") &&
                    !name.StartsWith(
                        "aim_fire_",
                        StringComparison.OrdinalIgnoreCase),
                name =>
                    name.Contains(
                        "fire",
                        StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith(
                        "aim_fire_",
                        StringComparison.OrdinalIgnoreCase)),
            AimFireEffects = aimFireEffects.Count == 0
                ? null
                : aimFireEffects,
            CoverIdle = FindPreferred(
                usableCoverAnimations,
                "cover_idle",
                name => ContainsAll(name, "cover", "idle"),
                name => name.Contains(
                    "idle",
                    StringComparison.OrdinalIgnoreCase)),
            ToCover = FindPreferred(
                usableCoverAnimations,
                "to_cover",
                name => ContainsAll(name, "to", "cover")),
            ReloadSequence = FindReloadSequence(usableCoverAnimations)
        };
    }

    private static CharacterResourceFiles? FindState(
        IEnumerable<CharacterResourceFiles> resources,
        string skinDirectory,
        string state) =>
        resources.FirstOrDefault(resource =>
            string.Equals(
                resource.ResourceType,
                state,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetDirectoryName(
                    Path.GetDirectoryName(resource.SkeletonPath) ??
                    string.Empty),
                skinDirectory,
                StringComparison.OrdinalIgnoreCase));

    private static CharacterBattleResourceConfig CreateResource(
        CharacterResourceFiles resource) =>
        new()
        {
            SkeletonPath = resource.SkeletonPath,
            AtlasPath = resource.AtlasPath,
            TexturePath = resource.PrimaryTexturePath,
            ExtraTexturePaths = resource.AdditionalTexturePaths.ToList()
        };

    private static BattleAnimationMetadata[] ReadAnimations(
        CharacterResourceFiles resource)
    {
        Atlas atlas = new(resource.AtlasPath, new NoopTextureLoader());
        try
        {
            SkeletonData data = Path.GetExtension(resource.SkeletonPath)
                .Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? new SkeletonJson(atlas).ReadSkeletonData(resource.SkeletonPath)
                : new SkeletonBinary(atlas).ReadSkeletonData(resource.SkeletonPath);
            return data.Animations
                .Select(animation => new BattleAnimationMetadata(
                    animation.Name,
                    animation.Timelines.Count > 0))
                .ToArray();
        }
        finally
        {
            atlas.Dispose();
        }
    }

    private static string? FindPreferred(
        IReadOnlyList<string> names,
        string exact,
        params Func<string, bool>[] fallbacks) =>
        names.FirstOrDefault(name =>
            name.Equals(exact, StringComparison.OrdinalIgnoreCase)) ??
        fallbacks
            .Select(predicate => names.FirstOrDefault(predicate))
            .FirstOrDefault(name => name != null);

    private static List<string> FindReloadSequence(
        IReadOnlyList<string> names)
    {
        string? single = FindPreferred(
            names,
            "cover_reload",
            name => ContainsAll(name, "cover", "reload"),
            name => name.Equals("reload", StringComparison.OrdinalIgnoreCase));
        if (single != null)
        {
            return [single];
        }

        string? start = names.FirstOrDefault(name =>
            ContainsAll(name, "reload", "start"));
        string? loop = names.FirstOrDefault(name =>
            ContainsAll(name, "reload", "loop"));
        string? end = names.FirstOrDefault(name =>
            ContainsAll(name, "reload", "end"));
        return new[] { start, loop, end }
            .Where(name => name != null)
            .Cast<string>()
            .ToList();
    }

    private static bool ContainsAll(string value, params string[] parts) =>
        parts.All(part =>
            value.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static int GetAimFireEffectOrder(string name) =>
        name.Equals(
            "aim_fire_hair",
            StringComparison.OrdinalIgnoreCase)
                ? 0
                : name.Equals(
                    "aim_fire_hip",
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 2;

    private sealed class NoopTextureLoader : TextureLoader
    {
        public void Load(AtlasPage page, string path) { }
        public void Unload(object texture) { }
    }
}

internal readonly record struct BattleAnimationMetadata(
    string Name,
    bool HasTimelines);
