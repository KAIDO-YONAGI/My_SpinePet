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

    private static NativeTextureSource CreateOpaqueTexture()
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
            "test://bounds",
            bitmap,
            sourcePremultipliedAlpha: false,
            pixels);
    }

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
