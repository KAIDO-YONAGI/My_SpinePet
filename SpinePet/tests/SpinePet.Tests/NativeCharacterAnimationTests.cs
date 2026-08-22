using Spine;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCharacterAnimationTests
{
    [Fact]
    public void ClickAnimationPrefersConventionalActionName()
    {
        string? animation =
            NativeCharacterRenderHost.SelectClickAnimationName(
                ["idle", "skillcut_1", "action"]);

        Assert.Equal("action", animation);
    }

    [Fact]
    public void ClickAnimationSupportsBurstSkillcutNames()
    {
        string? animation =
            NativeCharacterRenderHost.SelectClickAnimationName(
                ["idle", "idle2", "skillcut_1", "skillcut_2"]);

        Assert.Equal("skillcut_1", animation);
    }

    [Fact]
    public void IdleAnimationUsesIdleVariantWhenExactIdleIsMissing()
    {
        string? animation =
            NativeCharacterRenderHost.SelectIdleAnimationName(
                ["walk", "idle2", "skillcut_1"]);

        Assert.Equal("idle2", animation);
    }

    [Fact]
    public void ConfiguredAnimationIsKeptWhenItExists()
    {
        string? animation =
            NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                "action",
                ["idle", "action"]);

        Assert.Equal("action", animation);
    }

    [Fact]
    public void MissingConfiguredAnimationFallsBackToIdle()
    {
        string? animation =
            NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                string.Empty,
                ["action", "idle"]);

        Assert.Equal("idle", animation);
    }

    [Fact]
    public void InvalidConfiguredAnimationFallsBackToIdleVariant()
    {
        string? animation =
            NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                "missing",
                ["action", "idle_loop"]);

        Assert.Equal("idle_loop", animation);
    }

    [Fact]
    public void TemporaryAnimationRestoresPreviousPlaybackState()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("custom", 2),
            ("action", 1));
        AnimationState animationState = fixture.AnimationState;
        TrackEntry persistent =
            animationState.SetAnimation(0, "custom", true);
        persistent.TrackTime = 0.75f;
        NativeTemporaryAnimationPlayback playback = new();

        playback.Play(animationState, "action");

        TrackEntry temporary = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        Assert.Equal("action", temporary.Animation.Name);
        Assert.False(temporary.Loop);
        TrackEntry restore = Assert.IsType<TrackEntry>(temporary.Next);
        Assert.Equal("custom", restore.Animation.Name);
        Assert.True(restore.Loop);
        Assert.Equal(0.75f, restore.TrackTime);

        AdvancePastCurrentAnimation(fixture, 1.1f);

        TrackEntry restored = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        Assert.Equal("custom", restored.Animation.Name);
        Assert.True(restored.Loop);
        Assert.False(playback.IsActive);
    }

    [Fact]
    public void RepeatedTemporaryAnimationKeepsOriginalRestoreState()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("custom", 2),
            ("action", 1));
        AnimationState animationState = fixture.AnimationState;
        animationState.SetAnimation(0, "custom", true);
        NativeTemporaryAnimationPlayback playback = new();

        playback.Play(animationState, "action");
        playback.Play(animationState, "action");

        TrackEntry temporary = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        TrackEntry restore = Assert.IsType<TrackEntry>(temporary.Next);
        Assert.Equal("custom", restore.Animation.Name);
        Assert.True(restore.Loop);

        AdvancePastCurrentAnimation(fixture, 1.1f);

        Assert.Equal(
            "custom",
            animationState.GetCurrent(0)?.Animation.Name);
    }

    [Fact]
    public void PersistentSelectionReplacesTemporaryAnimation()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("idle", 2),
            ("action", 1),
            ("selected", 2));
        AnimationState animationState = fixture.AnimationState;
        animationState.SetAnimation(0, "idle", true);
        NativeTemporaryAnimationPlayback playback = new();
        playback.Play(animationState, "action");

        playback.SetPersistent(
            animationState,
            "selected",
            repeat: true);
        AdvancePastCurrentAnimation(fixture, 2.1f);

        TrackEntry selected = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        Assert.Equal("selected", selected.Animation.Name);
        Assert.True(selected.Loop);
        Assert.Null(selected.Next);
        Assert.False(playback.IsActive);
    }

    private static AnimationFixture CreateAnimationFixture(
        params (string Name, float Duration)[] animations)
    {
        SkeletonData skeletonData = new();
        foreach ((string name, float duration) in animations)
        {
            skeletonData.Animations.Add(
                new Animation(
                    name,
                    new ExposedList<Timeline>(),
                    duration));
        }

        return new AnimationFixture(
            new AnimationState(new AnimationStateData(skeletonData)),
            new Skeleton(skeletonData));
    }

    private static void AdvancePastCurrentAnimation(
        AnimationFixture fixture,
        float elapsedSeconds)
    {
        fixture.AnimationState.Update(elapsedSeconds);
        fixture.AnimationState.Apply(fixture.Skeleton);
        fixture.AnimationState.Update(0);
        fixture.AnimationState.Apply(fixture.Skeleton);
    }

    private sealed record AnimationFixture(
        AnimationState AnimationState,
        Skeleton Skeleton);
}
