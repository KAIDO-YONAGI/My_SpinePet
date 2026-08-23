using System.Reflection;
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
        using NativeCharacterRenderHost renderHost = new();
        NativeCharacterScene scene = GetScene(renderHost);
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

        renderHost.SetConfigMode(true);
        renderHost.SetConfigMode(false);

        Assert.Same(
            temporary,
            state.Resource.AnimationState.GetCurrent(0));
        Assert.False(temporary.Loop);
        Assert.True(state.TemporaryAnimationPlayback.IsActive);
        Assert.Equal(0, state.TemporaryAnimationPlayback.RestoreCount);
    }

    private static NativeCharacterScene GetScene(
        NativeCharacterRenderHost renderHost)
    {
        FieldInfo field = typeof(NativeCharacterRenderHost).GetField(
            "_scene",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(
                typeof(NativeCharacterRenderHost).FullName,
                "_scene");
        return Assert.IsType<NativeCharacterScene>(
            field.GetValue(renderHost));
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
