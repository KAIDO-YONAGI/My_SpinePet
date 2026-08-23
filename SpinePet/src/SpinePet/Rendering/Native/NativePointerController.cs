using System.Windows;

namespace SpinePet.Rendering.Native;

internal sealed class NativePointerController
{
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonDown = 0x0204;
    private const int WmRightButtonUp = 0x0205;
    private const int WmCaptureChanged = 0x0215;

    private readonly NativeCharacterScene _scene;
    private readonly Func<NativeCompositionWindow?> _getWindow;
    private readonly Action<string, double, double> _moveCharacter;
    private readonly Action _beginMove;
    private readonly Action _endMove;
    private readonly Action<NativeCharacterState> _playClickAnimation;
    private readonly Action<string, double, double> _positionCommitted;
    private readonly Action<string> _rightPressed;
    private readonly Action<string> _rightReleased;
    private string? _rightCharacterId;
    private string? _characterId;
    private NativePoint _start;
    private NativePoint _latest;
    private double _startX;
    private double _startY;
    private bool _dragEnabled = true;
    private bool _dragging;
    private bool _movePending;

    public NativePointerController(
        NativeCharacterScene scene,
        Func<NativeCompositionWindow?> getWindow,
        Action<string, double, double> moveCharacter,
        Action beginMove,
        Action endMove,
        Action<NativeCharacterState> playClickAnimation,
        Action<string, double, double> positionCommitted,
        Action<string> rightPressed,
        Action<string> rightReleased)
    {
        _scene = scene;
        _getWindow = getWindow;
        _moveCharacter = moveCharacter;
        _beginMove = beginMove;
        _endMove = endMove;
        _playClickAnimation = playClickAnimation;
        _positionCommitted = positionCommitted;
        _rightPressed = rightPressed;
        _rightReleased = rightReleased;
    }

    public bool IsDragging => _dragging;

    public void SetDragEnabled(bool enabled)
    {
        _dragEnabled = enabled;
        if (!enabled && _dragging)
            Cancel(commitPosition: true);
    }

    public void Handle(uint nativeMessage, int x, int y)
    {
        int message = checked((int)nativeMessage);
        if (message != WmLeftButtonDown &&
            message != WmRightButtonDown &&
            message != WmRightButtonUp &&
            message != WmMouseMove &&
            message != WmLeftButtonUp &&
            message != WmCaptureChanged)
        {
            return;
        }

        NativePoint point = new(x, y);
        if (message == WmRightButtonDown)
        {
            Cancel(commitPosition: false);
            if (NativeCharacterHitTester.TryHit(
                    _scene,
                    _getWindow(),
                    point.X,
                    point.Y,
                out NativeCharacterState? rightClickedState))
            {
                _rightCharacterId = rightClickedState.Config.Id;
                _rightPressed(_rightCharacterId);
            }
            return;
        }

        if (message == WmRightButtonUp)
        {
            ReleaseRight();
            return;
        }

        if (message == WmLeftButtonDown &&
            NativeCharacterHitTester.TryHit(
                _scene,
                _getWindow(),
                point.X,
                point.Y,
                out NativeCharacterState? state))
        {
            _characterId = state.Config.Id;
            _start = point;
            _latest = point;
            _startX = state.Config.PositionX;
            _startY = state.Config.PositionY;
            _dragging = false;
            _movePending = false;
            return;
        }

        if (_characterId != null && message == WmMouseMove)
        {
            _latest = point;
            int deltaX = point.X - _start.X;
            int deltaY = point.Y - _start.Y;
            float dpiScale = _getWindow()?.DpiScale ?? 1;
            if (!_dragging &&
                _dragEnabled &&
                (Math.Abs(deltaX) >=
                     SystemParameters.MinimumHorizontalDragDistance *
                     dpiScale ||
                 Math.Abs(deltaY) >=
                     SystemParameters.MinimumVerticalDragDistance *
                     dpiScale))
            {
                _dragging = true;
                _beginMove();
            }

            if (_dragging)
                _movePending = true;
            return;
        }

        if (_characterId != null && message == WmLeftButtonUp)
        {
            _latest = point;
            _movePending = _dragging;
            string characterId = _characterId;
            FlushPendingMove();
            if (_scene.TryGet(
                    characterId,
                    out NativeCharacterState? releasedState))
            {
                if (_dragging)
                {
                    NativeInputRegionCoordinator.AlignCacheToCurrentPosition(
                        _getWindow(),
                        releasedState);
                    _endMove();
                    _positionCommitted(
                        characterId,
                        releasedState.Config.PositionX,
                        releasedState.Config.PositionY);
                }
                else
                {
                    _playClickAnimation(releasedState);
                }
            }

            Reset();
            return;
        }

        if (message == WmCaptureChanged)
        {
            if (_characterId != null)
                Cancel(commitPosition: true);
            ReleaseRight();
        }
    }

    public void FlushPendingMove()
    {
        NativeCompositionWindow? window = _getWindow();
        if (!_movePending ||
            !_dragging ||
            _characterId == null ||
            window == null)
        {
            return;
        }

        _movePending = false;
        _moveCharacter(
            _characterId,
            _startX + (_latest.X - _start.X) / window.DpiScale,
            _startY + (_latest.Y - _start.Y) / window.DpiScale);
    }

    public void CancelIfCharacter(string characterId)
    {
        if (string.Equals(
                _characterId,
                characterId,
                StringComparison.Ordinal))
        {
            Cancel(commitPosition: false);
        }
        if (string.Equals(
                _rightCharacterId,
                characterId,
                StringComparison.Ordinal))
        {
            ReleaseRight();
        }
    }

    public void Cancel(bool commitPosition)
    {
        if (_characterId != null &&
            _scene.TryGet(
                _characterId,
                out NativeCharacterState? state))
        {
            FlushPendingMove();
            if (_dragging)
            {
                NativeInputRegionCoordinator.AlignCacheToCurrentPosition(
                    _getWindow(),
                    state);
                _endMove();
                if (commitPosition)
                {
                    _positionCommitted(
                        state.Config.Id,
                        state.Config.PositionX,
                        state.Config.PositionY);
                }
            }
        }

        Reset();
        ReleaseRight();
    }

    private void Reset()
    {
        _characterId = null;
        _dragging = false;
        _movePending = false;
    }

    private void ReleaseRight()
    {
        string? characterId = _rightCharacterId;
        _rightCharacterId = null;
        if (characterId != null)
        {
            _rightReleased(characterId);
        }
    }

    private readonly record struct NativePoint(int X, int Y);
}
