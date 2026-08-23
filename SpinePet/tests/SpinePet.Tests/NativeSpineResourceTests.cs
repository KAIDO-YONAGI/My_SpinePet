using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spine;
using SpinePet.Models;
using SpinePet.Rendering.Native;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class NativeSpineResourceTests
{
    private static readonly string[] OceanLamentComponentSlots =
    [
        "BG1",
        "BG2",
        "bg_ribbon1",
        "bg_ribbon2",
        "bg_ribbon3",
        "bg_ribbon4"
    ];

    private static readonly string[] ArcanaComponentSlots =
    [
        "BG_chair",
        "BG_desk",
        "BG_dream_catcher_1",
        "BG_bag_tarot_cards_1"
    ];

    [Fact]
    public void AttachmentExclusionRulesSupportPrefixesAndExactNames()
    {
        IReadOnlyList<NativeSpineAttachmentExclusionRule> rules =
            NativeSpineAttachmentExclusions.Parse(
            [
                "# Background attachments",
                "prefix: bg_",
                "PREFIX:water_",
                "name: O_tex",
                ""
            ]);

        Assert.Collection(
            rules,
            rule =>
            {
                Assert.True(rule.IsPrefix);
                Assert.True(rule.Matches("BG_JELLYFISH_1"));
                Assert.False(rule.Matches("hair_bg_detail"));
            },
            rule =>
            {
                Assert.True(rule.IsPrefix);
                Assert.True(rule.Matches("water_body_fx_1"));
            },
            rule =>
            {
                Assert.False(rule.IsPrefix);
                Assert.True(rule.Matches("o_TEX"));
                Assert.False(rule.Matches("O_tex_extra"));
            });
    }

    [Fact]
    public void StandingResourcesIncludeAllComponentSkins()
    {
        string repositoryRoot = FindRepositoryRoot();
        AssertComponentSkinIsVisible(
            repositoryRoot,
            Path.Combine(
                "Asuka WILLE - Ocean's Lament",
                "02",
                "standing"),
            "c83502_02",
            OceanLamentComponentSlots);
        AssertComponentSkinIsVisible(
            repositoryRoot,
            Path.Combine(
                "Arcana Fortune Mate",
                "00",
                "standing"),
            "c583_00",
            ArcanaComponentSlots);
    }

    [Fact]
    public void BurstResourceRemovesBackgroundAttachmentsBeforeCalculatingBounds()
    {
        string repositoryRoot = FindRepositoryRoot();
        string standingDirectory = Path.Combine(
            repositoryRoot,
            "res",
            "Burst - Liberalio - Dreaming Lake",
            "standing");
        string skeletonPath = Path.Combine(
            standingDirectory,
            "Burst_Liberalio_DL.skel");
        if (!File.Exists(skeletonPath))
            return;

        CharacterConfig config = new()
        {
            AtlasPath = Path.Combine(
                standingDirectory,
                "Burst_Liberalio_DL.atlas"),
            SkeletonPath = skeletonPath
        };

        using NativeSpineResource resource = NativeSpineResource.Load(config);

        Assert.Equal(136, resource.ExcludedAttachmentCount);
        Assert.DoesNotContain(
            resource.SkeletonData.Skins
                .SelectMany(skin => skin.Attachments),
            entry =>
            {
                string? regionName = entry.Attachment switch
                {
                    RegionAttachment region =>
                        (region.Region as AtlasRegion)?.name,
                    MeshAttachment mesh =>
                        (mesh.Region as AtlasRegion)?.name,
                    _ => null
                };
                return regionName != null &&
                    (regionName.StartsWith(
                         "bg_",
                         StringComparison.OrdinalIgnoreCase) ||
                     regionName.StartsWith(
                         "water_",
                         StringComparison.OrdinalIgnoreCase) ||
                     regionName.StartsWith(
                         "fx_",
                         StringComparison.OrdinalIgnoreCase) ||
                     regionName.Equals(
                         "O_tex",
                         StringComparison.OrdinalIgnoreCase));
            });
    }

    [Fact]
    public void PremultiplyBgraPixelsConvertsStraightAlphaBeforeFiltering()
    {
        byte[] pixels =
        [
            200, 100, 50, 128,
            25, 50, 75, 0,
            10, 20, 30, 255
        ];

        NativeTextureSource.PremultiplyBgraPixels(pixels);

        Assert.Equal(
            [
                100, 50, 25, 128,
                0, 0, 0, 0,
                10, 20, 30, 255
            ],
            pixels);
    }

    [Fact]
    public void TexturePixelsCanBeRehydratedAfterGpuCacheEviction()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"spinepet-alpha-{Guid.NewGuid():N}.png");
        try
        {
            BitmapSource bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 200, 100, 50, 128 },
                4);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);

            NativeTextureSource straight =
                NativeTextureSource.Load(path, premultipliedAlpha: false);
            NativeTextureSource premultiplied =
                NativeTextureSource.Load(path, premultipliedAlpha: true);

            Assert.Equal(
                new byte[] { 100, 50, 25, 128 },
                straight.CopyBgraPixels());
            Assert.Equal(
                new byte[] { 100, 50, 25, 128 },
                straight.CopyBgraPixels());
            Assert.Equal(
                new byte[] { 200, 100, 50, 128 },
                premultiplied.CopyBgraPixels());
            Assert.Equal(
                new byte[] { 200, 100, 50, 128 },
                premultiplied.CopyBgraPixels());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void TexturePurgeRetainsInactiveResourceSlots()
    {
        string repositoryRoot = FindRepositoryRoot();
        string resourceRoot = Path.Combine(repositoryRoot, "res");
        if (!Directory.Exists(resourceRoot))
            return;

        CharacterResourceFiles[] installedResources =
            new CharacterResourceDiscoveryService()
                .DiscoverAll(resourceRoot)
                .Take(2)
                .ToArray();
        if (installedResources.Length < 2)
            return;

        CharacterConfig activeConfig = new()
        {
            AtlasPath = installedResources[0].AtlasPath,
            SkeletonPath = installedResources[0].SkeletonPath
        };
        CharacterConfig retainedConfig = new()
        {
            AtlasPath = installedResources[1].AtlasPath,
            SkeletonPath = installedResources[1].SkeletonPath
        };
        NativeSpineResource active =
            NativeSpineResource.Load(activeConfig);
        NativeSpineResource retained =
            NativeSpineResource.Load(retainedConfig);
        using NativeCharacterState state = new()
        {
            Config = activeConfig,
            Resource = active
        };
        state.ResourceSlots[CharacterBattleStates.Cover] =
            new NativeCharacterLoadResult(
                retained,
                NativeSpineBounds.Empty,
                NativeSpineBounds.Empty);

        HashSet<string> paths =
            NativeFrameRenderer.CollectRetainedTexturePaths([state]);

        Assert.All(
            active.TextureLoader.Textures,
            texture => Assert.Contains(texture.Path, paths));
        Assert.All(
            retained.TextureLoader.Textures,
            texture => Assert.Contains(texture.Path, paths));
    }

    [Fact]
    public void LoadAllInstalledSpine41ResourcesProducesRenderableGeometry()
    {
        string repositoryRoot = FindRepositoryRoot();
        string resourceRoot = Path.Combine(repositoryRoot, "res");
        if (!Directory.Exists(resourceRoot))
            return;

        IReadOnlyList<CharacterResourceFiles> installedResources =
            new CharacterResourceDiscoveryService().DiscoverAll(resourceRoot);
        Assert.NotEmpty(installedResources);

        foreach (CharacterResourceFiles installed in installedResources)
        {
            CharacterConfig config = new()
            {
                AtlasPath = installed.AtlasPath,
                SkeletonPath = installed.SkeletonPath
            };

            using NativeSpineResource resource = NativeSpineResource.Load(config);
            resource.SetAnimation(
                resource.AnimationNames.Count > 0
                    ? resource.AnimationNames[0]
                    : null,
                true);
            resource.Update(1f / 60f);

            NativeSpineGeometry geometry = new();
            IReadOnlyList<NativeSpineDrawBatch> firstBuild =
                geometry.Build(resource.Skeleton);
            Assert.NotEmpty(firstBuild);
            NativeSpineVertex[][] vertexBuffers = firstBuild
                .Select(batch => batch.Vertices)
                .ToArray();
            int[][] indexBuffers = firstBuild
                .Select(batch => batch.Indices)
                .ToArray();

            IReadOnlyList<NativeSpineDrawBatch> secondBuild =
                geometry.Build(resource.Skeleton);

            Assert.Same(firstBuild, secondBuild);
            Assert.Equal(vertexBuffers.Length, secondBuild.Count);
            for (int batchIndex = 0;
                 batchIndex < secondBuild.Count;
                 batchIndex++)
            {
                NativeSpineDrawBatch batch = secondBuild[batchIndex];
                Assert.Same(vertexBuffers[batchIndex], batch.Vertices);
                Assert.Same(indexBuffers[batchIndex], batch.Indices);
                Assert.InRange(
                    batch.VertexCount,
                    1,
                    batch.Vertices.Length);
                Assert.InRange(
                    batch.IndexCount,
                    1,
                    batch.Indices.Length);
            }

            Assert.StartsWith("4.1", resource.SkeletonData.Version);
            Assert.NotEmpty(resource.TextureLoader.Textures);
        }
    }

    [Fact]
    public void InputAnimationTransitionsDoNotCrossFade()
    {
        string repositoryRoot = FindRepositoryRoot();
        string resourceRoot = Path.Combine(repositoryRoot, "res");
        if (!Directory.Exists(resourceRoot))
            return;

        IReadOnlyList<CharacterResourceFiles> installedResources =
            new CharacterResourceDiscoveryService()
                .DiscoverAll(resourceRoot);
        if (installedResources.Count == 0)
            return;
        CharacterResourceFiles installed = installedResources[0];

        CharacterConfig config = new()
        {
            AtlasPath = installed.AtlasPath,
            SkeletonPath = installed.SkeletonPath
        };

        using NativeSpineResource resource = NativeSpineResource.Load(config);

        Assert.Equal(0, resource.AnimationStateData.DefaultMix);
    }

    [Fact]
    public void CoverReloadSequenceRestoresLoopingCoverIdle()
    {
        string repositoryRoot = FindRepositoryRoot();
        string coverDirectory = Path.Combine(
            repositoryRoot,
            "res",
            "Anis Star",
            "00",
            CharacterResourceTypes.Cover);
        string skeletonPath = Directory.Exists(coverDirectory)
            ? Directory.EnumerateFiles(coverDirectory, "*.skel").FirstOrDefault()
                ?? string.Empty
            : string.Empty;
        string atlasPath = Directory.Exists(coverDirectory)
            ? Directory.EnumerateFiles(coverDirectory, "*.atlas").FirstOrDefault()
                ?? string.Empty
            : string.Empty;
        if (!File.Exists(skeletonPath) || !File.Exists(atlasPath))
            return;

        using NativeSpineResource resource = NativeSpineResource.Load(
            new CharacterConfig
            {
                AtlasPath = atlasPath,
                SkeletonPath = skeletonPath
            });
        resource.SetAnimationSequence(
            ["to_cover", "cover_reload"],
            "cover_idle",
            loopLast: false);

        float sequenceDuration = resource.SkeletonData
            .FindAnimation("to_cover")!.Duration +
            resource.SkeletonData.FindAnimation("cover_reload")!.Duration;
        for (float elapsed = 0;
             elapsed < sequenceDuration + 0.5f;
             elapsed += 1f / 60f)
        {
            resource.Update(1f / 60f);
        }

        TrackEntry current = Assert.IsType<TrackEntry>(
            resource.AnimationState.GetCurrent(0));
        Assert.Equal("cover_idle", current.Animation.Name);
        Assert.True(current.Loop);
        Assert.Null(current.Next);
    }

    [Fact]
    public void AimFireEffectsStartAfterTransitionAndClearWithStateChange()
    {
        string repositoryRoot = FindRepositoryRoot();
        string aimDirectory = Path.Combine(
            repositoryRoot,
            "res",
            "Laplace Neo",
            "00",
            CharacterResourceTypes.Aim);
        string skeletonPath = Path.Combine(
            aimDirectory,
            "c103_aim_00.skel");
        string atlasPath = Path.Combine(
            aimDirectory,
            "c103_aim_00.atlas");
        if (!File.Exists(skeletonPath) || !File.Exists(atlasPath))
            return;

        using NativeSpineResource resource = NativeSpineResource.Load(
            new CharacterConfig
            {
                AtlasPath = atlasPath,
                SkeletonPath = skeletonPath
            });
        resource.SetAnimationSequence(
            ["to_aim", "aim_fire"],
            restoreAnimation: null,
            loopLast: true,
            parallelAnimations: ["aim_fire_hair"]);

        TrackEntry baseTrack = Assert.IsType<TrackEntry>(
            resource.AnimationState.GetCurrent(0));
        TrackEntry effectTrack = Assert.IsType<TrackEntry>(
            resource.AnimationState.GetCurrent(1));
        Assert.Equal("to_aim", baseTrack.Animation.Name);
        Assert.Equal("aim_fire", baseTrack.Next?.Animation.Name);
        Assert.Equal("aim_fire_hair", effectTrack.Animation.Name);
        Assert.Equal(
            resource.SkeletonData.FindAnimation("to_aim")!.Duration,
            effectTrack.Delay,
            precision: 3);
        Assert.True(effectTrack.Loop);

        resource.SetAnimation("aim_idle", true);

        Assert.Equal(
            "aim_idle",
            resource.AnimationState.GetCurrent(0)?.Animation.Name);
        Assert.Null(resource.AnimationState.GetCurrent(1));
    }

    [Fact]
    public void ZeroDurationFireEffectRemainsOnLoopingOverlayTrack()
    {
        string repositoryRoot = FindRepositoryRoot();
        string aimDirectory = Path.Combine(
            repositoryRoot,
            "res",
            "Blanc - White Rabbit",
            CharacterResourceTypes.Aim);
        string skeletonPath = Path.Combine(
            aimDirectory,
            "c270_aim_01.skel");
        string atlasPath = Path.Combine(
            aimDirectory,
            "c270_aim_01.atlas");
        if (!File.Exists(skeletonPath) || !File.Exists(atlasPath))
            return;

        using NativeSpineResource resource = NativeSpineResource.Load(
            new CharacterConfig
            {
                AtlasPath = atlasPath,
                SkeletonPath = skeletonPath
            });
        Animation effect = Assert.IsType<Animation>(
            resource.SkeletonData.FindAnimation("aim_fire_hair"));
        Assert.Equal(0, effect.Duration);
        Assert.NotEmpty(effect.Timelines);

        resource.SetAnimationSequence(
            ["to_aim", "aim_fire"],
            restoreAnimation: null,
            loopLast: true,
            parallelAnimations: ["aim_fire_hair"]);
        float transitionDuration = resource.SkeletonData
            .FindAnimation("to_aim")!.Duration;
        resource.Update(transitionDuration + 0.5f);

        TrackEntry overlay = Assert.IsType<TrackEntry>(
            resource.AnimationState.GetCurrent(1));
        Assert.Equal("aim_fire_hair", overlay.Animation.Name);
        Assert.True(overlay.Loop);
        Assert.Equal(0, overlay.AnimationTime);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "SpinePet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void AssertComponentSkinIsVisible(
        string repositoryRoot,
        string relativeStandingDirectory,
        string resourceName,
        IReadOnlyList<string> componentSlots)
    {
        string standingDirectory = Path.Combine(
            repositoryRoot,
            "res",
            relativeStandingDirectory);
        string skeletonPath = Path.Combine(
            standingDirectory,
            resourceName + ".skel");
        if (!File.Exists(skeletonPath))
            return;

        CharacterConfig config = new()
        {
            AtlasPath = Path.Combine(
                standingDirectory,
                resourceName + ".atlas"),
            SkeletonPath = skeletonPath
        };

        using NativeSpineResource resource = NativeSpineResource.Load(config);
        resource.SetAnimation("idle", true);
        resource.Update(1f / 60f);

        Assert.Equal(1, resource.IncludedSkinCount);
        Assert.All(
            componentSlots,
            slotName =>
            {
                Slot? slot = resource.Skeleton.FindSlot(slotName);
                Assert.NotNull(slot);
                Assert.NotNull(slot.Attachment);
            });
    }
}
