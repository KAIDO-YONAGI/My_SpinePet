using System.Diagnostics;
using System.Windows.Threading;
using Spine;
using SpinePet.Models;
using SpinePet.Rendering.Native;
using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class NativeCharacterRenderHostPerformanceTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void FrameLoopStartsIdleAndAcceptsSupportedRate(
        int targetFrameRate)
    {
        using NativeCharacterRenderHost renderHost = new();

        Assert.False(renderHost.IsFrameLoopRunning);

        renderHost.SetTargetFrameRate(targetFrameRate);

        Assert.Equal(targetFrameRate, renderHost.TargetFrameRate);
        Assert.Equal(
            TimeSpan.FromSeconds(1.0 / targetFrameRate),
            renderHost.FrameInterval);
        Assert.False(renderHost.IsFrameLoopRunning);
    }

    [Fact]
    public void ConfigModeToggleDoesNotRestartActiveAnimation()
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
            Id = "config-mode-animation",
            AtlasPath = installed.AtlasPath,
            SkeletonPath = installed.SkeletonPath
        };
        using NativeCharacterRenderEngine renderEngine =
            new(Dispatcher.CurrentDispatcher);
        NativeCharacterScene scene = renderEngine.Scene;
        NativeCharacterState state = scene.GetOrCreate(
            config,
            defaultScale: 0.2,
            minimumScale: 0.05,
            maximumScale: 2);
        state.Resource = NativeSpineResource.Load(config);
        state.IsVisible = true;
        Assert.NotEmpty(state.Resource.AnimationNames);
        string animation = state.Resource.AnimationNames[0];
        config.ConfiguredAnimation = animation;
        state.PersistentAnimationName = animation;
        state.TemporaryAnimationPlayback.Play(
            state.Resource.AnimationState,
            animation,
            () => animation);
        TrackEntry temporary = Assert.IsType<TrackEntry>(
            state.Resource.AnimationState.GetCurrent(0));

        renderEngine.SetConfigMode(true);
        renderEngine.SetConfigMode(false);

        Assert.Same(
            temporary,
            state.Resource.AnimationState.GetCurrent(0));
        Assert.False(temporary.Loop);
        Assert.True(state.TemporaryAnimationPlayback.IsActive);
        Assert.Equal(0, state.TemporaryAnimationPlayback.RestoreCount);
    }

    [Fact]
    public async Task GuiCommandsDoNotWaitForBusyRenderThread()
    {
        using NativeCharacterRenderHost renderHost = new();
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        Task blocked = renderHost.ExecuteOnRenderThreadAsync(() =>
        {
            entered.Set();
            release.Wait();
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        Stopwatch stopwatch = Stopwatch.StartNew();
        renderHost.SetCharacterScale("missing", 1);
        renderHost.MoveCharacter("missing", 10, 20);
        renderHost.SetTargetFrameRate(60);
        stopwatch.Stop();

        Assert.NotEqual(
            Environment.CurrentManagedThreadId,
            renderHost.RenderThreadId);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(100),
            $"GUI commands waited {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");

        release.Set();
        await blocked.WaitAsync(TimeSpan.FromSeconds(2));
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
               throw new DirectoryNotFoundException(
                   "Repository root not found.");
    }
}
