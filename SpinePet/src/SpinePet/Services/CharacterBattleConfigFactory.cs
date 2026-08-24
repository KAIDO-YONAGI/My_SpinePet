using System.IO;
using Spine;
using SpinePet.Infrastructure;
using SpinePet.Models;

namespace SpinePet.Services;

internal static class CharacterBattleConfigFactory
{
    private static readonly Dictionary<
        string,
        CharacterBattleLayerConfig[]> AimFireEffectProfiles =
        new Dictionary<string, CharacterBattleLayerConfig[]>(
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
        animations.AimFireLayers = ResolveFireLayers(
            aim.SkeletonPath,
            aimAnimations,
            animations.AimIdle,
            animations.AimFire);
        animations.AimFire = null;

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

    internal static List<CharacterBattleLayerConfig>?
        ResolveLegacyFireLayers(
            CharacterBattleResourceConfig aim,
            CharacterBattleAnimationsConfig animations)
    {
        ArgumentNullException.ThrowIfNull(aim);
        ArgumentNullException.ThrowIfNull(animations);

        try
        {
            IReadOnlyList<BattleAnimationMetadata> metadata =
                ReadAnimations(aim.SkeletonPath, aim.AtlasPath);
            List<CharacterBattleLayerConfig>? resolved = ResolveFireLayers(
                aim.SkeletonPath,
                metadata,
                animations.AimIdle,
                animations.AimFire);
            if (resolved != null)
            {
                AddLegacyEffects(
                    resolved,
                    metadata,
                    animations.AimIdle,
                    animations.BattleEffects);
                return resolved;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterBattleConfigFactory),
                $"legacy-fire-layer-fallback path={aim.SkeletonPath} " +
                $"message={exception.Message}");
        }

        List<CharacterBattleLayerConfig> fallback = [];
        if (!string.IsNullOrWhiteSpace(animations.AimFire))
        {
            fallback.Add(CreateLayer(animations.AimFire));
        }
        if (animations.BattleEffects != null)
        {
            fallback.AddRange(animations.BattleEffects
                .Where(effect =>
                    effect != null &&
                    !string.IsNullOrWhiteSpace(effect.Animation))
                .Select(effect => CreateLayer(
                    effect.Animation,
                    effect.Blend,
                    effect.Alpha,
                    effect.Loop)));
        }
        return fallback.Count == 0 ? null : fallback;
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
        => ReadAnimations(resource.SkeletonPath, resource.AtlasPath);

    private static BattleAnimationMetadata[] ReadAnimations(
        string skeletonPath,
        string atlasPath)
    {
        Atlas atlas = new(atlasPath, new NoopTextureLoader());
        try
        {
            SkeletonData data = Path.GetExtension(skeletonPath)
                .Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? new SkeletonJson(atlas).ReadSkeletonData(skeletonPath)
                : new SkeletonBinary(atlas).ReadSkeletonData(skeletonPath);
            return data.Animations
                .Select(animation => new BattleAnimationMetadata(
                    animation.Name,
                    animation.Timelines.Count > 0,
                    animation.Duration,
                    animation.Timelines
                        .Select(timeline => new BattleTimelineMetadata(
                            SpineTimelineKey.Resolve(timeline, data),
                            timeline.FrameCount))
                        .ToArray()))
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
                out CharacterBattleLayerConfig[]? profile))
        {
            return null;
        }

        List<CharacterBattleEffectConfig> resolved = profile
            .Where(effect => isAvailable(effect.Animation))
            .Select(effect => new CharacterBattleEffectConfig
            {
                Animation = effect.Animation,
                Blend = effect.Blend,
                Alpha = effect.Alpha,
                Loop = effect.Loop
            })
            .ToList();
        return resolved.Count == 0 ? null : resolved;
    }

    private static List<CharacterBattleLayerConfig>? ResolveFireLayers(
        string skeletonPath,
        IReadOnlyList<BattleAnimationMetadata> animations,
        string? aimIdle,
        string? aimFire)
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
        if (available.Count == 0)
        {
            return null;
        }

        HashSet<string> idleDynamicTimelines = ResolveDynamicTimelines(
            available,
            aimIdle);
        List<CharacterBattleLayerConfig> layers = [];
        AddLayer(
            layers,
            available,
            aimFire,
            idleDynamicTimelines);

        string skeletonName = Path.GetFileNameWithoutExtension(skeletonPath);
        if (AimFireEffectProfiles.TryGetValue(
                skeletonName,
                out CharacterBattleLayerConfig[]? profile))
        {
            foreach (CharacterBattleLayerConfig effect in profile)
            {
                AddLayer(
                    layers,
                    available,
                    effect.Animation,
                    idleDynamicTimelines,
                    effect);
            }
        }

        return layers.Count == 0 ? null : layers;
    }

    private static void AddLegacyEffects(
        List<CharacterBattleLayerConfig> layers,
        IReadOnlyList<BattleAnimationMetadata> animations,
        string? aimIdle,
        List<CharacterBattleEffectConfig>? effects)
    {
        if (effects == null || effects.Count == 0)
        {
            return;
        }

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
        HashSet<string> idleDynamicTimelines = ResolveDynamicTimelines(
            available,
            aimIdle);
        HashSet<string> existing = layers
            .Select(layer => layer.Animation)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (CharacterBattleEffectConfig effect in effects.Where(
                     effect =>
                         effect != null &&
                         !string.IsNullOrWhiteSpace(effect.Animation) &&
                         !existing.Contains(effect.Animation)))
        {
            AddLayer(
                layers,
                available,
                effect.Animation,
                idleDynamicTimelines,
                CreateLayer(
                    effect.Animation,
                    effect.Blend,
                    effect.Alpha,
                    effect.Loop));
        }
    }

    private static HashSet<string> ResolveDynamicTimelines(
        Dictionary<string, BattleAnimationMetadata> animations,
        string? animationName)
    {
        if (string.IsNullOrWhiteSpace(animationName) ||
            !animations.TryGetValue(
                animationName,
                out BattleAnimationMetadata animation) ||
            animation.Timelines == null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return animation.Timelines
            .Where(timeline => timeline.FrameCount > 1)
            .Select(timeline => timeline.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void AddLayer(
        List<CharacterBattleLayerConfig> layers,
        Dictionary<string, BattleAnimationMetadata> animations,
        string? animationName,
        HashSet<string> idleDynamicTimelines,
        CharacterBattleLayerConfig? template = null)
    {
        if (string.IsNullOrWhiteSpace(animationName) ||
            !animations.TryGetValue(
                animationName,
                out BattleAnimationMetadata animation))
        {
            return;
        }

        IReadOnlyList<BattleTimelineMetadata> timelines =
            animation.Timelines ?? [];
        List<string> exclusions = timelines
            .Where(timeline =>
                timeline.FrameCount <= 1 &&
                idleDynamicTimelines.Contains(timeline.Key) &&
                !timeline.Key.StartsWith(
                    "AttachmentTimeline@",
                    StringComparison.Ordinal) &&
                !timeline.Key.StartsWith(
                    "DrawOrderTimeline@",
                    StringComparison.Ordinal))
            .Select(timeline => timeline.Key)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        int remainingTimelineCount = timelines.Count == 0
            ? 1
            : timelines.Count(timeline =>
                !exclusions.Contains(timeline.Key, StringComparer.Ordinal));
        if (remainingTimelineCount == 0)
        {
            return;
        }

        CharacterBattleLayerConfig layer = template == null
            ? CreateLayer(animationName)
            : CloneLayer(template);
        layer.ExcludeTimelines = exclusions.Count == 0
            ? null
            : exclusions;
        layers.Add(layer);
    }

    private static CharacterBattleLayerConfig CreateEffect(
        string animation,
        string blend = CharacterBattleEffectBlendModes.Replace,
        float alpha = 1,
        bool loop = true) =>
        CreateLayer(animation, blend, alpha, loop);

    private static CharacterBattleLayerConfig CreateLayer(
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

    private static CharacterBattleLayerConfig CloneLayer(
        CharacterBattleLayerConfig source) =>
        CreateLayer(
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
    float Duration = 1,
    IReadOnlyList<BattleTimelineMetadata>? Timelines = null);

internal readonly record struct BattleTimelineMetadata(
    string Key,
    int FrameCount);
