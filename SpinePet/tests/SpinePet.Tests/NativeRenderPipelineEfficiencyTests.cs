using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Spine;
using SpinePet.Models;
using SpinePet.Rendering.Native;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class NativeRenderPipelineEfficiencyTests
{
    [Fact]
    public void BusyDispatchGateDropsAdditionalTicks()
    {
        NativeFrameDispatchGate gate = new();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.False(gate.TryEnter());

        gate.Exit();

        Assert.True(gate.TryEnter());
    }

    [Fact]
    public void DrawBatchCalculatesBoundsWhileAppendingVertices()
    {
        NativeSpineDrawBatch batch = new();
        batch.Reset(CreateOpaqueTexture(), BlendMode.Normal);

        batch.AppendVertices(
            [-4, 7, 12, -3, 5, 20],
            [0, 0, 1, 0, 0.5f, 1],
            positionsLength: 6,
            Vector4.One,
            Vector4.Zero,
            out NativeSpineBounds bounds);

        Assert.Equal(-4, bounds.Left);
        Assert.Equal(-3, bounds.Top);
        Assert.Equal(12.001f, bounds.Right, precision: 3);
        Assert.Equal(20.001f, bounds.Bottom, precision: 3);
        Assert.Equal(
            NativeSpineEnvelopeCalculator.GetBounds([batch]),
            bounds);
    }

    [Fact]
    public void GeometryBuildPublishesTheBoundsOfItsBatches()
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

        using NativeSpineResource resource = NativeSpineResource.Load(
            new CharacterConfig
            {
                AtlasPath = installed.AtlasPath,
                SkeletonPath = installed.SkeletonPath
            });
        resource.SetAnimation(
            resource.AnimationNames.Count > 0
                ? resource.AnimationNames[0]
                : null,
            true);
        resource.Update(1f / 60f);
        NativeSpineGeometry geometry = new();

        IReadOnlyList<NativeSpineDrawBatch> batches =
            geometry.Build(resource.Skeleton);

        Assert.Equal(
            NativeSpineEnvelopeCalculator.GetBounds(batches),
            geometry.Bounds);
    }

    [Fact]
    public void FramePlanPreservesDrawOrderAndCountsEveryBatch()
    {
        NativeTextureSource firstTexture =
            CreateOpaqueTexture("test://plan-first");
        NativeTextureSource secondTexture =
            CreateOpaqueTexture("test://plan-second");
        NativeSpineDrawBatch first = CreateTriangleBatch(
            firstTexture,
            BlendMode.Normal,
            xOffset: 0);
        NativeSpineDrawBatch second = CreateTriangleBatch(
            secondTexture,
            BlendMode.Screen,
            xOffset: 2);

        NativeFrameRenderPlan plan =
            NativeFrameRenderPlan.Create([first, second]);

        Assert.Collection(
            plan.Batches,
            batch => Assert.Same(first, batch),
            batch => Assert.Same(second, batch));
        Assert.Equal(6, plan.VertexCount);
        Assert.Equal(6, plan.IndexCount);
    }

    [Fact]
    public void NativeSubmissionUploadsGeometryOnceAndCachesPipelineResources()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);
        using NativeCompositionSurface surface = graphics.CreateSurface();
        surface.EnsureSize(
            new NativeSpineBounds(-2, -2, 2, 2),
            pivotX: 0,
            pivotY: 0,
            pixelScale: 1);
        NativeTextureSource firstTexture =
            CreateOpaqueTexture("test://pipeline-first");
        NativeTextureSource secondTexture =
            CreateOpaqueTexture("test://pipeline-second");
        NativeFrameRenderPlan plan = NativeFrameRenderPlan.Create(
        [
            CreateTriangleBatch(
                firstTexture,
                BlendMode.Normal,
                xOffset: -1),
            CreateTriangleBatch(
                firstTexture,
                BlendMode.Additive,
                xOffset: 0),
            CreateTriangleBatch(
                secondTexture,
                BlendMode.Additive,
                xOffset: 1)
        ]);

        graphics.Render(
            surface,
            plan,
            surface.GetTransform(pixelScale: 1));

        Assert.Equal(1, graphics.PipelineCreationCount);
        Assert.Equal(2, graphics.TextureUploadCount);
        Assert.Equal(2, graphics.TextureCacheCount);
        Assert.Equal(
            new NativeRenderSubmissionStatistics(
                GeometryUploadCount: 1,
                TextureBindCount: 2,
                BlendStateBindCount: 2,
                DrawCallCount: 3),
            graphics.LastSubmissionStatistics);

        graphics.Render(
            surface,
            plan,
            surface.GetTransform(pixelScale: 1));

        Assert.Equal(1, graphics.PipelineCreationCount);
        Assert.Equal(2, graphics.TextureUploadCount);
        Assert.Equal(2, graphics.TextureCacheCount);
        Assert.Equal(
            1,
            graphics.LastSubmissionStatistics.GeometryUploadCount);
    }

    [Fact]
    public void NativeReadbackKeepsTransparentBackgroundClear()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);
        using NativeCompositionSurface surface = CreateReadbackSurface(graphics);

        NativeRenderedFrame frame = graphics.RenderAndReadback(
            surface,
            NativeFrameRenderPlan.Create([]),
            new Vector4(1, 1, 0, 0));

        Assert.All(frame.BgraPixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void NativeReadbackAppliesAllBlendModesInDrawOrder()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);
        using NativeCompositionSurface surface = CreateReadbackSurface(graphics);
        (byte B, byte G, byte R, byte A)[] colors =
        [
            (64, 96, 128, 160),
            (20, 30, 40, 80),
            (32, 16, 48, 96),
            (8, 24, 40, 64)
        ];
        BlendMode[] blendModes =
        [
            BlendMode.Normal,
            BlendMode.Additive,
            BlendMode.Multiply,
            BlendMode.Screen
        ];
        NativeFrameRenderPlan plan = NativeFrameRenderPlan.Create(
            colors.Select(
                    (color, index) => CreateQuadBatch(
                        CreatePremultipliedTexture(
                            $"test://readback-{index}",
                            color),
                        blendModes[index]))
                .ToArray());

        NativeRenderedFrame frame = graphics.RenderAndReadback(
            surface,
            plan,
            new Vector4(1, 1, 0, 0));

        Vector4 expected = Vector4.Zero;
        for (int index = 0; index < colors.Length; index++)
        {
            (byte blue, byte green, byte red, byte alpha) = colors[index];
            Vector4 source = new(
                red / 255f,
                green / 255f,
                blue / 255f,
                alpha / 255f);
            expected = NativeBlendProfiles.CompositePremultiplied(
                blendModes[index],
                source,
                expected);
        }

        (byte actualBlue, byte actualGreen, byte actualRed, byte actualAlpha) =
            GetCenterPixel(frame);
        Assert.InRange(actualRed, ToByte(expected.X) - 2, ToByte(expected.X) + 2);
        Assert.InRange(actualGreen, ToByte(expected.Y) - 2, ToByte(expected.Y) + 2);
        Assert.InRange(actualBlue, ToByte(expected.Z) - 2, ToByte(expected.Z) + 2);
        Assert.InRange(actualAlpha, ToByte(expected.W) - 2, ToByte(expected.W) + 2);
    }

    [Fact]
    public void NativeAdditiveReadbackKeepsPremultipliedSurfaceCoverage()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);
        using NativeCompositionSurface surface = CreateReadbackSurface(graphics);
        NativeFrameRenderPlan plan = NativeFrameRenderPlan.Create(
        [
            CreateQuadBatch(
                CreatePremultipliedTexture(
                    "test://readback-base",
                    (96, 48, 24, 128)),
                BlendMode.Normal),
            CreateQuadBatch(
                CreatePremultipliedTexture(
                    "test://readback-additive",
                    (32, 48, 64, 96)),
                BlendMode.Additive)
        ]);

        NativeRenderedFrame frame = graphics.RenderAndReadback(
            surface,
            plan,
            new Vector4(1, 1, 0, 0));

        (byte blue, byte green, byte red, byte alpha) =
            GetCenterPixel(frame);
        Assert.Equal(224, alpha);
        Assert.True(red > 24);
        Assert.True(green > 48);
        Assert.True(blue > 96);
        Assert.True(red <= alpha);
        Assert.True(green <= alpha);
        Assert.True(blue <= alpha);
    }

    [Fact]
    public void NativeAdditiveReadbackOnTransparentPixelKeepsSourceCoverage()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);
        using NativeCompositionSurface surface = CreateReadbackSurface(graphics);
        NativeFrameRenderPlan plan = NativeFrameRenderPlan.Create(
        [
            CreateQuadBatch(
                CreatePremultipliedTexture(
                    "test://readback-transparent-additive",
                    (32, 48, 64, 96)),
                BlendMode.Additive)
        ]);

        NativeRenderedFrame frame = graphics.RenderAndReadback(
            surface,
            plan,
            new Vector4(1, 1, 0, 0));

        (byte blue, byte green, byte red, byte alpha) =
            GetCenterPixel(frame);
        Assert.Equal(96, alpha);
        Assert.True(red <= alpha);
        Assert.True(green <= alpha);
        Assert.True(blue <= alpha);
    }

    [Fact]
    public async Task InputRegionsOnlyRebuildWhenTheirInputsChange()
    {
        EnsureWindowsDirectoryEnvironment();
        using NativeRenderSession session = new();
        await session.Initialize((_, _, _) => { });
        NativeInputRegionCoordinator coordinator = new();
        NativeCharacterState state = CreateCachedVisibleState(session);

        coordinator.Update(session, [state], pointerDragging: false);
        int firstRebuildCount = coordinator.RegionRebuildCount;
        coordinator.Update(session, [state], pointerDragging: false);

        Assert.Equal(1, firstRebuildCount);
        Assert.Equal(firstRebuildCount, coordinator.RegionRebuildCount);

        state.CachedSilhouetteTimestamp++;
        coordinator.Update(session, [state], pointerDragging: false);

        Assert.Equal(
            firstRebuildCount + 1,
            coordinator.RegionRebuildCount);
    }

    [Fact]
    public async Task InputRegionRefreshIsDeferredUntilDraggingEnds()
    {
        EnsureWindowsDirectoryEnvironment();
        using NativeRenderSession session = new();
        await session.Initialize((_, _, _) => { });
        NativeInputRegionCoordinator coordinator = new();
        NativeCharacterState state = CreateCachedVisibleState(session);
        coordinator.Update(session, [state], pointerDragging: false);
        int firstRebuildCount = coordinator.RegionRebuildCount;

        state.CachedSilhouetteTimestamp++;
        coordinator.Update(session, [state], pointerDragging: true);

        Assert.Equal(firstRebuildCount, coordinator.RegionRebuildCount);

        coordinator.Update(session, [state], pointerDragging: false);

        Assert.Equal(
            firstRebuildCount + 1,
            coordinator.RegionRebuildCount);
    }

    [Fact]
    public void SurfaceAnchorPositionSkipsDuplicateCompositionWrites()
    {
        using NativeCompositionWindow window = new();
        using NativeGraphicsDevice graphics = new(window.Handle);
        using NativeCompositionSurface surface = graphics.CreateSurface();
        NativeSpineBounds envelope = new(-100, -200, 100, 0);
        surface.EnsureSize(
            envelope,
            pivotX: 0,
            pivotY: 0,
            pixelScale: 1);

        surface.SetAnchorPosition(400, 500);
        surface.SetAnchorPosition(400, 500);

        Assert.Equal(1, surface.AnchorPositionApplyCount);

        surface.EnsureSize(
            envelope,
            pivotX: 0,
            pivotY: 0,
            pixelScale: 2);
        surface.SetAnchorPosition(400, 500);

        Assert.Equal(2, surface.AnchorPositionApplyCount);
    }

    private static NativeCharacterState CreateCachedVisibleState(
        NativeRenderSession session)
    {
        NativeCompositionWindow window = session.Window!;
        CharacterConfig config = new()
        {
            PositionX = System.Windows.SystemParameters.VirtualScreenLeft +
                100 / window.DpiScale,
            PositionY = System.Windows.SystemParameters.VirtualScreenTop +
                100 / window.DpiScale
        };
        NativeCharacterState state = new()
        {
            Config = config,
            IsVisible = true,
            CurrentScale = 1,
            HasCachedSilhouette = true,
            CachedAnchorX = window.Left + 100,
            CachedAnchorY = window.Top + 100,
            CachedPixelScale = window.DpiScale,
            CachedSilhouetteTimestamp = Stopwatch.GetTimestamp()
        };
        state.CachedSilhouetteRuns.Add(
            new Rectangle(100, 100, 80, 120));
        return state;
    }

    private static NativeSpineDrawBatch CreateTriangleBatch(
        NativeTextureSource texture,
        BlendMode blendMode,
        float xOffset)
    {
        NativeSpineDrawBatch batch = new();
        batch.Reset(texture, blendMode);
        int vertexOffset = batch.AppendVertices(
            [
                xOffset - 0.5f, -0.5f,
                xOffset + 0.5f, -0.5f,
                xOffset, 0.5f
            ],
            [0, 0, 1, 0, 0.5f, 1],
            positionsLength: 6,
            Vector4.One,
            Vector4.Zero);
        batch.AppendIndices([0, 1, 2], 3, vertexOffset);
        return batch;
    }

    private static NativeSpineDrawBatch CreateQuadBatch(
        NativeTextureSource texture,
        BlendMode blendMode)
    {
        NativeSpineDrawBatch batch = new();
        batch.Reset(texture, blendMode);
        int vertexOffset = batch.AppendVertices(
            [
                -1, -1,
                -1, 1,
                1, 1,
                1, -1
            ],
            [
                0.5f, 0.5f,
                0.5f, 0.5f,
                0.5f, 0.5f,
                0.5f, 0.5f
            ],
            positionsLength: 8,
            Vector4.One,
            Vector4.Zero);
        batch.AppendIndices(
            [0, 1, 2, 0, 2, 3],
            6,
            vertexOffset);
        return batch;
    }

    private static NativeTextureSource CreateOpaqueTexture(
        string path = "test://bounds")
    {
        byte[] pixels = [0, 0, 0, 255];
        System.Windows.Media.Imaging.BitmapSource bitmap =
            System.Windows.Media.Imaging.BitmapSource.Create(
                1,
                1,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixels,
                4);
        return new NativeTextureSource(
            path,
            bitmap,
            sourcePremultipliedAlpha: false,
            pixels);
    }

    private static NativeTextureSource CreatePremultipliedTexture(
        string path,
        (byte B, byte G, byte R, byte A) color)
    {
        byte[] pixels = [color.B, color.G, color.R, color.A];
        System.Windows.Media.Imaging.BitmapSource bitmap =
            System.Windows.Media.Imaging.BitmapSource.Create(
                1,
                1,
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32,
                null,
                pixels,
                4);
        return new NativeTextureSource(
            path,
            bitmap,
            sourcePremultipliedAlpha: true,
            pixels);
    }

    private static NativeCompositionSurface CreateReadbackSurface(
        NativeGraphicsDevice graphics)
    {
        NativeCompositionSurface surface = graphics.CreateSurface();
        surface.EnsureSize(
            new NativeSpineBounds(-1, -1, 1, 1),
            pivotX: 0,
            pivotY: 0,
            pixelScale: 16);
        return surface;
    }

    private static (byte B, byte G, byte R, byte A) GetCenterPixel(
        NativeRenderedFrame frame)
    {
        int offset = checked(
            ((frame.Height / 2) * frame.Width + frame.Width / 2) * 4);
        return (
            frame.BgraPixels[offset],
            frame.BgraPixels[offset + 1],
            frame.BgraPixels[offset + 2],
            frame.BgraPixels[offset + 3]);
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp(
            (int)Math.Round(value * 255),
            byte.MinValue,
            byte.MaxValue);

    private static void EnsureWindowsDirectoryEnvironment()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("windir")))
        {
            Environment.SetEnvironmentVariable(
                "windir",
                Environment.GetEnvironmentVariable("SystemRoot"));
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(
                   directory.FullName,
                   "SpinePet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new DirectoryNotFoundException(
                   "Repository root not found.");
    }
}
