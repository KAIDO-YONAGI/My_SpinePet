using Spine;
using SpinePet.Models;

namespace SpinePet.Rendering.Native;

// Centralizes persistent animation policy and temporary click playback.
internal static class NativeAnimationController
{
    public static void SelectModeAnimation(NativeCharacterState state)
    {
        if (!ShouldSelectModeAnimation(state.ActiveResourceState))
            return;

        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        string? animation = SelectConfiguredOrIdleAnimationName(
            state.Config.ConfiguredAnimation,
            resource.AnimationNames);
        state.PersistentAnimationName = animation;
        TrackEntry? current = resource.AnimationState.GetCurrent(0);
        if (current?.Animation?.Name.Equals(
                animation,
                StringComparison.OrdinalIgnoreCase) == true &&
            current.Loop)
        {
            return;
        }

        if (animation != null)
        {
            state.TemporaryAnimationPlayback.SetPersistent(
                resource.AnimationState,
                animation,
                repeat: true);
        }
    }

    internal static bool ShouldSelectModeAnimation(string resourceState) =>
        resourceState.Equals(
            CharacterDisplayModes.Normal,
            StringComparison.OrdinalIgnoreCase);

    public static void PlayClickAnimation(NativeCharacterState state)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null)
            return;

        string? clickAnimation =
            SelectClickAnimationName(resource.AnimationNames);
        if (clickAnimation == null)
            return;

        AnimationState animationState = resource.AnimationState;
        state.TemporaryAnimationPlayback.Play(
            animationState,
            clickAnimation,
            () => SelectCurrentDefaultAnimationName(
                state,
                animationState));
    }

    public static void SetPersistent(
        NativeCharacterState state,
        string animation,
        bool repeat)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null ||
            resource.SkeletonData.FindAnimation(animation) == null)
        {
            return;
        }

        state.TemporaryAnimationPlayback.SetPersistent(
            resource.AnimationState,
            animation,
            repeat);
        state.PersistentAnimationName = animation;
    }

    internal static string? SelectClickAnimationName(
        IReadOnlyList<string> animationNames)
    {
        string[] preferredNames =
        [
            "action",
            "click",
            "touch",
            "tap",
            "reaction",
            "interact",
            "skillcut"
        ];
        foreach (string preferredName in preferredNames)
        {
            string? exact = animationNames.FirstOrDefault(name =>
                name.Equals(
                    preferredName,
                    StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            string? prefixed = animationNames.FirstOrDefault(name =>
                name.StartsWith(
                    $"{preferredName}_",
                    StringComparison.OrdinalIgnoreCase));
            if (prefixed != null)
                return prefixed;
        }

        return null;
    }

    internal static string? SelectIdleAnimationName(
        IReadOnlyList<string> animationNames)
    {
        string? idle = animationNames.FirstOrDefault(name =>
            name.Equals("idle", StringComparison.OrdinalIgnoreCase));
        return idle ??
               animationNames.FirstOrDefault(name =>
                   name.StartsWith(
                       "idle",
                       StringComparison.OrdinalIgnoreCase)) ??
               (animationNames.Count > 0 ? animationNames[0] : null);
    }

    internal static string? SelectConfiguredOrIdleAnimationName(
        string? configuredAnimation,
        IReadOnlyList<string> animationNames)
    {
        string? configured = animationNames.FirstOrDefault(name =>
            name.Equals(
                configuredAnimation,
                StringComparison.OrdinalIgnoreCase));
        return configured ?? SelectIdleAnimationName(animationNames);
    }

    internal static string? SelectCurrentDefaultAnimationName(
        NativeCharacterState state,
        AnimationState expectedAnimationState)
    {
        NativeSpineResource? resource = state.Resource;
        if (resource == null ||
            !ReferenceEquals(
                resource.AnimationState,
                expectedAnimationState))
        {
            return null;
        }

        string? selected = SelectDefaultAnimationNameForState(
            state,
            resource.AnimationNames);
        state.PersistentAnimationName = selected;
        return selected;
    }

    internal static string? SelectDefaultAnimationNameForState(
        NativeCharacterState state,
        IReadOnlyList<string> animationNames)
    {
        string? configuredAnimation;
        if (state.ActiveResourceState.Equals(
                CharacterBattleStates.Aim,
                StringComparison.OrdinalIgnoreCase))
        {
            configuredAnimation =
                state.Config.Battle?.Animations.AimIdle;
        }
        else if (state.ActiveResourceState.Equals(
                     CharacterBattleStates.Cover,
                     StringComparison.OrdinalIgnoreCase))
        {
            configuredAnimation =
                state.Config.Battle?.Animations.CoverIdle;
        }
        else
        {
            configuredAnimation = state.Config.ConfiguredAnimation;
        }

        return SelectConfiguredOrIdleAnimationName(
            configuredAnimation,
            animationNames);
    }
}
