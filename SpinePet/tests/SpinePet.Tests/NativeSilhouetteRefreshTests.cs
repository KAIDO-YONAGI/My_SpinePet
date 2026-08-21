using System.Diagnostics;
using SpinePet.Models;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeSilhouetteRefreshTests
{
    [Fact]
    public void MissingCacheForcesRefresh()
    {
        NativeCharacterState state = CreateState();
        long now = Stopwatch.GetTimestamp();

        Assert.True(NativeCharacterRenderHost.NeedsSilhouetteRefresh(
            state,
            anchorX: 100,
            anchorY: 100,
            pixelScale: 1,
            nowTimestamp: now));
    }

    [Fact]
    public void FreshCacheWithSubPixelDriftSkipsRefresh()
    {
        NativeCharacterState state = CreateState();
        long now = Stopwatch.GetTimestamp();
        SeedCache(state, anchorX: 100, anchorY: 100, pixelScale: 1, now);

        Assert.False(NativeCharacterRenderHost.NeedsSilhouetteRefresh(
            state,
            anchorX: 101.5f,
            anchorY: 100.5f,
            pixelScale: 1,
            nowTimestamp: now));
    }

    [Fact]
    public void AnchorMoveBeyondEpsilonForcesRefresh()
    {
        NativeCharacterState state = CreateState();
        long now = Stopwatch.GetTimestamp();
        SeedCache(state, anchorX: 100, anchorY: 100, pixelScale: 1, now);

        Assert.True(NativeCharacterRenderHost.NeedsSilhouetteRefresh(
            state,
            anchorX: 102,
            anchorY: 100,
            pixelScale: 1,
            nowTimestamp: now));
    }

    [Fact]
    public void ScaleChangeForcesRefresh()
    {
        NativeCharacterState state = CreateState();
        long now = Stopwatch.GetTimestamp();
        SeedCache(state, anchorX: 100, anchorY: 100, pixelScale: 1, now);

        Assert.True(NativeCharacterRenderHost.NeedsSilhouetteRefresh(
            state,
            anchorX: 100,
            anchorY: 100,
            pixelScale: 1.5f,
            nowTimestamp: now));
    }

    [Fact]
    public void ElapsedRefreshIntervalForcesRefresh()
    {
        NativeCharacterState state = CreateState();
        long now = Stopwatch.GetTimestamp();
        long staleTimestamp = now - Stopwatch.Frequency;
        SeedCache(
            state,
            anchorX: 100,
            anchorY: 100,
            pixelScale: 1,
            staleTimestamp);

        Assert.True(NativeCharacterRenderHost.NeedsSilhouetteRefresh(
            state,
            anchorX: 100,
            anchorY: 100,
            pixelScale: 1,
            nowTimestamp: now));
    }

    private static NativeCharacterState CreateState() => new()
    {
        Config = new CharacterConfig()
    };

    private static void SeedCache(
        NativeCharacterState state,
        float anchorX,
        float anchorY,
        float pixelScale,
        long timestamp)
    {
        state.HasCachedSilhouette = true;
        state.CachedAnchorX = anchorX;
        state.CachedAnchorY = anchorY;
        state.CachedPixelScale = pixelScale;
        state.CachedSilhouetteTimestamp = timestamp;
    }
}
