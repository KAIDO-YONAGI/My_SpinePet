using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpinePet.Models;

namespace SpinePet.ViewModels;

public sealed class CharacterViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _skinLabel = string.Empty;
    private string _thumbnailPath = string.Empty;
    private double _scale = 0.2;
    private double _maxScale = 1.35;
    private int _positionX = 200;
    private int _positionY = 200;
    private bool _isVisible;
    private bool _isLoading;
    private string _configAnimation = string.Empty;
    private double _animationSpeed = 1.0;

    public required string Id { get; init; }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value))
            {
                return;
            }

            OnPropertyChanged(nameof(AccessibilitySummary));
            OnPropertyChanged(nameof(VisibilityActionAutomationLabel));
        }
    }

    public string SkinLabel
    {
        get => _skinLabel;
        set
        {
            if (SetProperty(ref _skinLabel, value))
            {
                OnPropertyChanged(nameof(AccessibilitySummary));
            }
        }
    }

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetProperty(ref _thumbnailPath, value);
    }

    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public double MaxScale
    {
        get => _maxScale;
        set => SetProperty(ref _maxScale, value);
    }

    public int PositionX
    {
        get => _positionX;
        set => SetProperty(ref _positionX, value);
    }

    public int PositionY
    {
        get => _positionY;
        set => SetProperty(ref _positionY, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                OnPropertyChanged(nameof(VisibilityActionLabel));
                OnPropertyChanged(nameof(VisibilityActionAutomationLabel));
                OnPropertyChanged(nameof(VisibilityStateLabel));
                OnPropertyChanged(nameof(AccessibilitySummary));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(VisibilityActionLabel));
                OnPropertyChanged(nameof(VisibilityActionAutomationLabel));
                OnPropertyChanged(nameof(VisibilityStateLabel));
                OnPropertyChanged(nameof(AccessibilitySummary));
                OnPropertyChanged(nameof(CanToggleVisibility));
            }
        }
    }

    public bool CanToggleVisibility => !IsLoading;

    public string VisibilityActionLabel =>
        IsLoading ? "Loading…" : IsVisible ? "Hide" : "Show";

    public string VisibilityActionAutomationLabel =>
        IsLoading
            ? $"Loading {Name}"
            : IsVisible
                ? $"Hide {Name}"
                : $"Show {Name}";

    public string VisibilityStateLabel =>
        IsLoading
            ? "Loading"
            : IsVisible
                ? "Visible on desktop"
                : "Hidden on desktop";

    public string AccessibilitySummary =>
        $"{Name}, {SkinLabel}, {VisibilityStateLabel}";

    public string ConfiguredAnimation
    {
        get => _configAnimation;
        set => SetProperty(ref _configAnimation, value);
    }

    public double AnimationSpeed
    {
        get => _animationSpeed;
        set => SetProperty(ref _animationSpeed, value);
    }

    public ObservableCollection<string> AnimationNames { get; } = new();

    public ObservableCollection<CharacterSkinOptionViewModel> AvailableSkins
    {
        get;
    } = new();

    public string AvailableSkinSearchText => string.Join(
        ' ',
        AvailableSkins.SelectMany(skin => new[]
        {
            skin.Label,
            skin.SkinCode,
            skin.ResourceName
        }));

    public void UpdateAnimationNames(IEnumerable<string> animationNames)
    {
        AnimationNames.Clear();
        foreach (string animationName in animationNames)
        {
            AnimationNames.Add(animationName);
        }
    }

    public void UpdateSkins(
        string currentSkinCode,
        IEnumerable<CharacterIdentity> identities)
    {
        CharacterSkinOptionViewModel[] options = identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity.SkinCode))
            .GroupBy(
                identity => identity.ResourceName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(identity => identity.SkinCode, StringComparer.OrdinalIgnoreCase)
            .Select(identity => new CharacterSkinOptionViewModel(
                Id,
                identity.CharacterCode,
                identity.ResourceName,
                identity.SkinCode,
                string.Equals(
                    identity.SkinCode,
                    currentSkinCode,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (AvailableSkins.SequenceEqual(options))
        {
            return;
        }

        AvailableSkins.Clear();
        foreach (CharacterSkinOptionViewModel option in options)
        {
            AvailableSkins.Add(option);
        }

        OnPropertyChanged(nameof(AvailableSkinSearchText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
