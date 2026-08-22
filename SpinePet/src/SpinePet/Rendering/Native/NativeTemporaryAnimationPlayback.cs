using Spine;

namespace SpinePet.Rendering.Native;

internal sealed class NativeTemporaryAnimationPlayback
{
    private string? _restoreAnimation;

    internal bool IsActive => _restoreAnimation != null;

    public void Play(
        AnimationState animationState,
        string temporaryAnimation,
        string restoreAnimation)
    {
        if (_restoreAnimation == null)
        {
            _restoreAnimation = restoreAnimation;
        }

        animationState.SetAnimation(0, temporaryAnimation, false);
        TrackEntry restoreEntry = animationState.AddAnimation(
            0,
            _restoreAnimation,
            loop: true,
            0);
        restoreEntry.Start += OnRestoreStarted;
    }

    public void SetPersistent(
        AnimationState animationState,
        string animation,
        bool repeat)
    {
        Clear();
        animationState.SetAnimation(0, animation, repeat);
    }

    public void Clear()
    {
        _restoreAnimation = null;
    }

    private void OnRestoreStarted(TrackEntry _)
    {
        Clear();
    }
}
