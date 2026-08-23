using System.IO;
using Spine;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

internal static class CharacterBattleConfigFactory
{
    private static readonly Dictionary<
        string,
        CharacterBattleEffectConfig[]> AimFireEffectProfiles =
        new Dictionary<string, CharacterBattleEffectConfig[]>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["c515_aim_00"] =
            [
                CreateEffect("aim_fire_hair")
            ],
            ["c103_aim_00"] =
            [
                CreateEffect("aim_fire_hair")
            ],
            ["c10301_01_aim_00"] =
            [
                CreateEffect("aim_fire_hair")
            ],
            ["c14002_aim_02"] =
            [
                CreateEffect("aim_fire_hair"),
                CreateEffect("aim_fire_hip")
            ]
        };

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

        CharacterBattleAnimationsConfig animations = ResolveAnimations(
            aimAnimations,
            coverAnimations);
        animations.BattleEffects = ResolveBattleEffects(
            aim.SkeletonPath,
            aimAnimations);

        return new CharacterBattleConfig
        {
            Aim = CreateResource(aim),
            Cover = CreateResource(cover),
            Animations = animations
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

    internal static List<CharacterBattleEffectConfig>?
        ResolveLegacyBattleEffects(
            string skeletonPath,
            IReadOnlyList<string>? animationNames)
    {
        if (animationNames == null || animationNames.Count == 0)
        {
            return null;
        }

        HashSet<string> available = animationNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ResolveBattleEffects(
            skeletonPath,
            name => available.Contains(name));
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
                    animation.Timelines.Count > 0,
                    animation.Duration))
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

    private static List<CharacterBattleEffectConfig>? ResolveBattleEffects(
        string skeletonPath,
        IReadOnlyList<BattleAnimationMetadata> animations)
    {
        Dictionary<string, BattleAnimationMetadata> available = animations
            .Where(animation =>
                animation.HasTimelines &&
                animation.Duration > 0)
            .GroupBy(
                animation => animation.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        return ResolveBattleEffects(
            skeletonPath,
            name => available.ContainsKey(name));
    }

    private static List<CharacterBattleEffectConfig>? ResolveBattleEffects(
        string skeletonPath,
        Func<string, bool> isAvailable)
    {
        string skeletonName = Path.GetFileNameWithoutExtension(skeletonPath);
        if (!AimFireEffectProfiles.TryGetValue(
                skeletonName,
                out CharacterBattleEffectConfig[]? profile))
        {
            return null;
        }

        List<CharacterBattleEffectConfig> resolved = profile
            .Where(effect => isAvailable(effect.Animation))
            .Select(CloneEffect)
            .ToList();
        return resolved.Count == 0 ? null : resolved;
    }

    private static CharacterBattleEffectConfig CreateEffect(
        string animation,
        string blend = CharacterBattleEffectBlendModes.Replace,
        float alpha = 1,
        bool loop = true) =>
        new()
        {
            Animation = animation,
            Blend = blend,
            Alpha = alpha,
            Loop = loop
        };

    private static CharacterBattleEffectConfig CloneEffect(
        CharacterBattleEffectConfig source) =>
        CreateEffect(
            source.Animation,
            source.Blend,
            source.Alpha,
            source.Loop);

    private sealed class NoopTextureLoader : TextureLoader
    {
        public void Load(AtlasPage page, string path) { }
        public void Unload(object texture) { }
    }
}

internal readonly record struct BattleAnimationMetadata(
    string Name,
    bool HasTimelines,
    float Duration = 1);
