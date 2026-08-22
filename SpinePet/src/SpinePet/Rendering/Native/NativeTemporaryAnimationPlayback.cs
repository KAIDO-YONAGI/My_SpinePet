using Spine;

namespace SpinePet.Rendering.Native;

internal sealed class NativeTemporaryAnimationPlayback
{
    private string? _restoreAnimation;
    private bool _restoreRepeat;
    private float _restoreTrackTime;

    internal bool IsActive => _restoreAnimation != null;

    public void Play(
        AnimationState animationState,
        string temporaryAnimation)
    {
        TrackEntry? current = animationState.GetCurrent(0);
        if (_restoreAnimation == null && current?.Animation != null)
        {
            _restoreAnimation = current.Animation.Name;
            _restoreRepeat = current.Loop;
            _restoreTrackTime = current.TrackTime;
        }

        animationState.SetAnimation(0, temporaryAnimation, false);
        if (_restoreAnimation == null)
            return;

        TrackEntry restoreEntry = animationState.AddAnimation(
            0,
            _restoreAnimation,
            _restoreRepeat,
            0);
        restoreEntry.TrackTime = _restoreTrackTime;
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
        _restoreRepeat = false;
        _restoreTrackTime = 0;
    }

    private void OnRestoreStarted(TrackEntry _)
    {
        Clear();
    }
}
