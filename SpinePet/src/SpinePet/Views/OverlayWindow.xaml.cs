using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpinePet.Services;
using Cursors = System.Windows.Input.Cursors;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace SpinePet.Views;

public partial class OverlayWindow : Window
{
    private readonly CharacterManager _characterManager;
    private bool _isDragArmed;
    private bool _isDragging;
    private Point _dragStartScreen;
    private Matrix _dragScreenToDipTransform = Matrix.Identity;
    private Point _windowStartPosition;

    public string? SelectedCharacterId { get; set; }
    public bool MoveSelectedCharacter { get; set; }

    public event Action<string, double, double>? CharacterMoved;

    public OverlayWindow(CharacterManager characterManager)
    {
        InitializeComponent();
        _characterManager = characterManager;

        Deactivated += (_, _) => StopDragging();
    }

    public void SetPreviewBounds(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void ShowOverlay()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }

        StopDragging();
    }

    public void CancelDrag()
    {
        StopDragging();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!MoveSelectedCharacter || string.IsNullOrEmpty(SelectedCharacterId))
        {
            return;
        }

        var character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == SelectedCharacterId);
        if (character == null ||
            !_characterManager.RenderHost.IsCharacterVisible(SelectedCharacterId))
        {
            return;
        }

        _isDragArmed = true;
        _isDragging = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragScreenToDipTransform =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
        _windowStartPosition = new Point(character.PositionX, character.PositionY);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if ((!_isDragArmed && !_isDragging) || string.IsNullOrEmpty(SelectedCharacterId))
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDragging();
            return;
        }

        var character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == SelectedCharacterId);
        if (character == null)
        {
            StopDragging();
            return;
        }

        Point currentScreen = PointToScreen(e.GetPosition(this));
        Vector delta =
            _dragScreenToDipTransform.Transform(currentScreen) -
            _dragScreenToDipTransform.Transform(_dragStartScreen);

        if (!_isDragging)
        {
            bool horizontalMoved =
                Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance;
            bool verticalMoved =
                Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (!horizontalMoved && !verticalMoved)
            {
                return;
            }

            _isDragging = true;
            _characterManager.RenderHost.BeginCharacterMove();
            Cursor = Cursors.SizeAll;
        }

        double nextLeft = _windowStartPosition.X + delta.X;
        double nextTop = _windowStartPosition.Y + delta.Y;
        _characterManager.RenderHost.MoveCharacter(character.Id, nextLeft, nextTop);
        CharacterMoved?.Invoke(character.Id, nextLeft, nextTop);
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopDragging();
    }

    private void OnPreviewMouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDragging();
        }
    }

    private void StopDragging()
    {
        if (_isDragging)
            _characterManager.RenderHost.EndCharacterMove();

        _isDragArmed = false;
        _isDragging = false;
        Cursor = Cursors.Arrow;
    }
}
