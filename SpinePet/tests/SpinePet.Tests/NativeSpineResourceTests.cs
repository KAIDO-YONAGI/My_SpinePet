using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spine;
using SpinePet.Models;
using SpinePet.Rendering.Native;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class NativeSpineResourceTests
{
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
    public void LoadStraightAlphaTexturePremultipliesGpuUploadOnlyOnce()
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
}
