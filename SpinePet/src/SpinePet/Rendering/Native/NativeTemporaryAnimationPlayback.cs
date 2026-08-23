using Spine;

namespace SpinePet.Rendering.Native;

internal sealed class NativeTemporaryAnimationPlayback
{
    private AnimationState? _animationState;
    private TrackEntry? _temporaryEntry;
    private Func<string?>? _resolveRestoreAnimation;
    private int _generation;
    private int _activeGeneration;

    internal bool IsActive => _temporaryEntry != null;
    internal int RestoreCount { get; private set; }

    public void Play(
        AnimationState animationState,
        string temporaryAnimation,
        Func<string?> resolveRestoreAnimation)
    {
        ArgumentNullException.ThrowIfNull(animationState);
        ArgumentNullException.ThrowIfNull(resolveRestoreAnimation);

        Clear();
        _animationState = animationState;
        _resolveRestoreAnimation = resolveRestoreAnimation;
        _activeGeneration = ++_generation;
        _temporaryEntry =
            animationState.SetAnimation(0, temporaryAnimation, false);
        _temporaryEntry.Complete += OnTemporaryCompleted;
        _temporaryEntry.Dispose += OnTemporaryDisposed;
    }

    public void SetPersistent(
        AnimationState animationState,
        string animation,
        bool repeat)
    {
        TrackEntry? current = animationState.GetCurrent(0);
        if (!IsActive &&
            current?.Animation?.Name.Equals(
                animation,
                StringComparison.OrdinalIgnoreCase) == true &&
            current.Loop == repeat)
        {
            return;
        }

        Clear();
        animationState.SetAnimation(0, animation, repeat);
    }

    public void Clear()
    {
        _generation++;
        DetachTemporaryEntry();
        _animationState = null;
        _resolveRestoreAnimation = null;
        _activeGeneration = 0;
    }

    private void OnTemporaryCompleted(TrackEntry completedEntry)
    {
        if (!ReferenceEquals(completedEntry, _temporaryEntry) ||
            _activeGeneration == 0 ||
            _activeGeneration != _generation)
        {
            return;
        }

        AnimationState? animationState = _animationState;
        Func<string?>? resolveRestoreAnimation =
            _resolveRestoreAnimation;
        DetachTemporaryEntry();
        _animationState = null;
        _resolveRestoreAnimation = null;
        _activeGeneration = 0;
        _generation++;

        string? restoreAnimation = resolveRestoreAnimation?.Invoke();
        Animation? restore = string.IsNullOrWhiteSpace(restoreAnimation)
            ? null
            : animationState?.Data.SkeletonData.FindAnimation(
                restoreAnimation);
        if (animationState == null || restore == null)
            return;

        animationState.SetAnimation(0, restore, true);
        RestoreCount++;
    }

    private void OnTemporaryDisposed(TrackEntry disposedEntry)
    {
        if (!ReferenceEquals(disposedEntry, _temporaryEntry))
            return;

        Clear();
    }

    private void DetachTemporaryEntry()
    {
        if (_temporaryEntry != null)
        {
            _temporaryEntry.Complete -= OnTemporaryCompleted;
            _temporaryEntry.Dispose -= OnTemporaryDisposed;
            _temporaryEntry = null;
        }
    }
}
