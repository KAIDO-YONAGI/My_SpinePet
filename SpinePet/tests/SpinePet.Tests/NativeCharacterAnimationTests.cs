using Spine;
using SpinePet.Models;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCharacterAnimationTests
{
    [Fact]
    public void ClickAnimationPrefersConventionalActionName()
    {
        string? animation =
            NativeAnimationController.SelectClickAnimationName(
                ["idle", "skillcut_1", "action"]);

        Assert.Equal("action", animation);
    }

    [Fact]
    public void ClickAnimationSupportsBurstSkillcutNames()
    {
        string? animation =
            NativeAnimationController.SelectClickAnimationName(
                ["idle", "idle2", "skillcut_1", "skillcut_2"]);

        Assert.Equal("skillcut_1", animation);
    }

    [Fact]
    public void ClickAnimationSupportsFavoriteExpressionFallback()
    {
        string? animation =
            NativeAnimationController.SelectClickAnimationName(
                ["bg_idle", "expression_merged", "idle"]);

        Assert.Equal("expression_merged", animation);
    }

    [Fact]
    public void ConventionalClickAnimationPrecedesFavoriteExpression()
    {
        string? animation =
            NativeAnimationController.SelectClickAnimationName(
                ["expression_merged", "touch"]);

        Assert.Equal("touch", animation);
    }

    [Fact]
    public void IdleAnimationUsesIdleVariantWhenExactIdleIsMissing()
    {
        string? animation =
            NativeAnimationController.SelectIdleAnimationName(
                ["walk", "idle2", "skillcut_1"]);

        Assert.Equal("idle2", animation);
    }

    [Fact]
    public void FavoriteDefaultAnimationPrefersMergedIdle()
    {
        string? animation =
            NativeAnimationController.SelectConfiguredOrIdleAnimationName(
                string.Empty,
                ["idle", "idle_merged", "expression_merged"],
                preferMergedIdle: true);

        Assert.Equal("idle_merged", animation);
    }

    [Fact]
    public void FavoriteConfiguredAnimationStillWinsOverMergedIdle()
    {
        string? animation =
            NativeAnimationController.SelectConfiguredOrIdleAnimationName(
                "idle",
                ["idle", "idle_merged", "expression_merged"],
                preferMergedIdle: true);

        Assert.Equal("idle", animation);
    }

    [Theory]
    [InlineData("Diesel Favorite", true)]
    [InlineData("diesel favorite", true)]
    [InlineData("Diesel", false)]
    public void FavoriteDisplayNameControlsMergedIdlePreference(
        string displayName,
        bool expected)
    {
        Assert.Equal(
            expected,
            NativeAnimationController.ShouldPreferMergedIdle(displayName));
    }

    [Fact]
    public void ConfiguredAnimationIsKeptWhenItExists()
    {
        string? animation =
            NativeAnimationController.SelectConfiguredOrIdleAnimationName(
                "action",
                ["idle", "action"]);

        Assert.Equal("action", animation);
    }

    [Fact]
    public void MissingConfiguredAnimationFallsBackToIdle()
    {
        string? animation =
            NativeAnimationController.SelectConfiguredOrIdleAnimationName(
                string.Empty,
                ["action", "idle"]);

        Assert.Equal("idle", animation);
    }

    [Theory]
    [InlineData(CharacterDisplayModes.Normal, true)]
    [InlineData(CharacterBattleStates.Cover, false)]
    [InlineData(CharacterBattleStates.Aim, false)]
    public void ConfiguredAnimationSelectionOnlyAppliesToNormalResources(
        string resourceState,
        bool expected)
    {
        Assert.Equal(
            expected,
            NativeAnimationController.ShouldSelectModeAnimation(
                resourceState));
    }

    [Fact]
    public void InvalidConfiguredAnimationFallsBackToIdleVariant()
    {
        string? animation =
            NativeAnimationController.SelectConfiguredOrIdleAnimationName(
                "missing",
                ["action", "idle_loop"]);

        Assert.Equal("idle_loop", animation);
    }

    [Fact]
    public void TemporaryAnimationRestartsConfiguredDefaultStateFromStart()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("custom", 2),
            ("action", 1));
        AnimationState animationState = fixture.AnimationState;
        TrackEntry persistent =
            animationState.SetAnimation(0, "custom", true);
        persistent.TrackTime = 0.75f;
        NativeTemporaryAnimationPlayback playback = new();

        playback.Play(animationState, "action", () => "custom");

        TrackEntry temporary = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        Assert.Equal("action", temporary.Animation.Name);
        Assert.False(temporary.Loop);
        Assert.Null(temporary.Next);

        AdvancePastCurrentAnimation(fixture, 1.1f);

        TrackEntry restored = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        Assert.Equal("custom", restored.Animation.Name);
        Assert.True(restored.Loop);
        Assert.Equal(0, restored.TrackTime);
        Assert.False(playback.IsActive);
        Assert.Equal(1, playback.RestoreCount);
    }

    [Fact]
    public void RepeatingPersistentSelectionKeepsCurrentTrackProgress()
    {
        AnimationFixture fixture = CreateAnimationFixture(("idle", 2));
        AnimationState animationState = fixture.AnimationState;
        TrackEntry current =
            animationState.SetAnimation(0, "idle", true);
        current.TrackTime = 0.75f;
        NativeTemporaryAnimationPlayback playback = new();

        playback.SetPersistent(animationState, "idle", repeat: true);

        Assert.Same(current, animationState.GetCurrent(0));
        Assert.Equal(0.75f, current.TrackTime);
    }

    [Fact]
    public void RepeatedTemporaryAnimationUsesLatestRestoreStateOnce()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("custom", 2),
            ("action", 1),
            ("selected", 2));
        AnimationState animationState = fixture.AnimationState;
        animationState.SetAnimation(0, "custom", true);
        NativeTemporaryAnimationPlayback playback = new();
        string restoreAnimation = "custom";

        playback.Play(
            animationState,
            "action",
            () => restoreAnimation);
        restoreAnimation = "selected";
        playback.Play(
            animationState,
            "action",
            () => restoreAnimation);

        TrackEntry temporary = Assert.IsType<TrackEntry>(
            animationState.GetCurrent(0));
        Assert.Equal("action", temporary.Animation.Name);
        Assert.Null(temporary.Next);

        AdvancePastCurrentAnimation(fixture, 1.1f);

        Assert.Equal(
            "selected",
            animationState.GetCurrent(0)?.Animation.Name);
        Assert.Equal(1, playback.RestoreCount);

        AdvancePastCurrentAnimation(fixture, 2.1f);

        Assert.Equal(1, playback.RestoreCount);
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
        playback.Play(animationState, "action", () => "idle");

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
        Assert.Equal(0, playback.RestoreCount);
    }

    [Fact]
    public void ClearedTemporaryAnimationCannotRestoreAfterCancellation()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("idle", 2),
            ("action", 1),
            ("selected", 2));
        AnimationState animationState = fixture.AnimationState;
        animationState.SetAnimation(0, "idle", true);
        NativeTemporaryAnimationPlayback playback = new();
        playback.Play(animationState, "action", () => "idle");

        playback.Clear();
        animationState.SetAnimation(0, "selected", true);
        AdvancePastCurrentAnimation(fixture, 2.1f);

        Assert.Equal(
            "selected",
            animationState.GetCurrent(0)?.Animation.Name);
        Assert.False(playback.IsActive);
        Assert.Equal(0, playback.RestoreCount);
    }

    [Fact]
    public void MissingRestoreAtCompletionDoesNotQueueAnOldAnimation()
    {
        AnimationFixture fixture = CreateAnimationFixture(
            ("idle", 2),
            ("action", 1));
        AnimationState animationState = fixture.AnimationState;
        animationState.SetAnimation(0, "idle", true);
        NativeTemporaryAnimationPlayback playback = new();
        playback.Play(animationState, "action", () => null);

        AdvancePastCurrentAnimation(fixture, 1.1f);

        Assert.Equal(
            "action",
            animationState.GetCurrent(0)?.Animation.Name);
        Assert.False(playback.IsActive);
        Assert.Equal(0, playback.RestoreCount);
    }

    [Theory]
    [InlineData(CharacterDisplayModes.Normal, "normal_idle")]
    [InlineData(CharacterBattleStates.Cover, "cover_idle")]
    [InlineData(CharacterBattleStates.Aim, "aim_idle")]
    public void CurrentDefaultAnimationFollowsActiveResourceState(
        string resourceState,
        string expected)
    {
        NativeCharacterState state = new()
        {
            Config = new CharacterConfig
            {
                ConfiguredAnimation = "normal_idle",
                Battle = new CharacterBattleConfig
                {
                    Animations = new CharacterBattleAnimationsConfig
                    {
                        CoverIdle = "cover_idle",
                        AimIdle = "aim_idle"
                    }
                }
            },
            ActiveResourceState = resourceState
        };

        string? selected =
            NativeAnimationController.SelectDefaultAnimationNameForState(
                state,
                ["normal_idle", "cover_idle", "aim_idle"]);

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void FavoriteNormalStateDefaultsToMergedIdle()
    {
        NativeCharacterState state = new()
        {
            Config = new CharacterConfig
            {
                Name = "Diesel Favorite"
            },
            ActiveResourceState = CharacterDisplayModes.Normal
        };

        string? selected =
            NativeAnimationController.SelectDefaultAnimationNameForState(
                state,
                ["idle", "idle_merged", "expression_merged"]);

        Assert.Equal("idle_merged", selected);
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
