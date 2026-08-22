using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Rendering.Native;
using SpinePet.Services;
using SpinePet.ViewModels;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace SpinePet.Views;

internal interface ICharacterSettingsHost
{
    CharacterViewModel? SelectedCharacter { get; }
    bool IsRefreshingSelection { get; set; }
    string SelectedAnimation { get; set; }
    double SelectedScale { get; set; }
    double SelectedScaleMax { get; set; }
    double SelectedScaleBasePercent { get; set; }
    double SelectedScaleMultiplier { get; set; }
    double SelectedSpeed { get; set; }
    ObservableCollection<string> SelectedAnimationNames { get; }
}

internal sealed class CharacterSettingsController
{
    private readonly CharacterManager _characterManager;
    private readonly CharacterResourceStorageService _resourceStorage;
    private readonly ObservableCollection<CharacterViewModel> _characters;
    private readonly ICharacterSettingsHost _host;
    private readonly Window _owner;
    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyDictionary<string, CharacterResourceFiles>>
        _getKnownResources;
    private readonly Action _refreshKnownResources;
    private readonly Func<CharacterResourceSynchronizationResult>
        _synchronizeKnownResources;
    private readonly Action _refreshCharacterList;
    private bool _isDeletingSkin;

    public CharacterSettingsController(
        CharacterManager characterManager,
        CharacterResourceStorageService resourceStorage,
        ObservableCollection<CharacterViewModel> characters,
        ICharacterSettingsHost host,
        Window owner,
        Dispatcher dispatcher,
        Func<IReadOnlyDictionary<string, CharacterResourceFiles>> getKnownResources,
        Action refreshKnownResources,
        Func<CharacterResourceSynchronizationResult> synchronizeKnownResources,
        Action refreshCharacterList)
    {
        _characterManager = characterManager;
        _resourceStorage = resourceStorage;
        _characters = characters;
        _host = host;
        _owner = owner;
        _dispatcher = dispatcher;
        _getKnownResources = getKnownResources;
        _refreshKnownResources = refreshKnownResources;
        _synchronizeKnownResources = synchronizeKnownResources;
        _refreshCharacterList = refreshCharacterList;
    }

    public void HandleCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted)
            {
                _dispatcher.BeginInvoke(
                    () => HandleCharacterScaleChanged(
                        characterId,
                        maximumScale,
                        currentScale));
            }

            return;
        }

        CharacterViewModel? viewModel = _characters.FirstOrDefault(
            character => character.Id == characterId);
        if (viewModel != null)
        {
            viewModel.MaxScale = Math.Clamp(
                maximumScale,
                CharacterSettingsDefaults.MinimumMaximumScale,
                CharacterSettingsDefaults.DefaultMaxScale);
            viewModel.Scale = Math.Clamp(
                currentScale,
                CharacterSettingsDefaults.MinimumScale,
                viewModel.MaxScale);
        }

        if (_host.SelectedCharacter?.Id != characterId)
        {
            return;
        }

        _host.IsRefreshingSelection = true;
        try
        {
            _host.SelectedScaleMax = Math.Clamp(
                maximumScale,
                CharacterSettingsDefaults.MinimumMaximumScale,
                CharacterSettingsDefaults.DefaultMaxScale);
            _host.SelectedScale = Math.Clamp(
                currentScale,
                CharacterSettingsDefaults.MinimumScale,
                _host.SelectedScaleMax);
            _host.SelectedCharacter.MaxScale = _host.SelectedScaleMax;
            _host.SelectedCharacter.Scale = _host.SelectedScale;
        }
        finally
        {
            _host.IsRefreshingSelection = false;
        }
    }

    public void HandleSpeedChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_host.IsRefreshingSelection ||
            _host.SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        double speed = _host.SelectedSpeed / 100.0;
        character.AnimationSpeed = speed;
        _host.SelectedCharacter.AnimationSpeed = speed;
        _characterManager.RenderHost.SetCharacterSpeed(character.Id, speed);
    }

    public void ResetScale()
    {
        if (_host.SelectedCharacter == null)
        {
            return;
        }

        SetSelectedScaleFromValue(CharacterSettingsDefaults.DefaultScale);
        CommitSelectedScale();
    }

    public void ResetSpeed()
    {
        if (_host.SelectedCharacter != null)
        {
            _host.SelectedSpeed = 100;
        }
    }

    public void ResetCharacterPosition(object sender)
    {
        if (sender is not Button
            {
                Tag: CharacterViewModel characterViewModel
            })
        {
            return;
        }

        CharacterConfig? character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == characterViewModel.Id);
        if (character == null)
        {
            return;
        }

        _characterManager.RenderHost.ResetCharacterPosition(character.Id);
        characterViewModel.PositionX = (int)character.PositionX;
        characterViewModel.PositionY = (int)character.PositionY;
    }

    public void HandleAnimationChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_host.IsRefreshingSelection ||
            sender is not ComboBox ||
            string.IsNullOrEmpty(_host.SelectedAnimation) ||
            _host.SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        character.ConfiguredAnimation = _host.SelectedAnimation;
        _host.SelectedCharacter.ConfiguredAnimation = _host.SelectedAnimation;
        _characterManager.RenderHost.PlayCharacterAnimation(
            character.Id,
            _host.SelectedAnimation,
            repeat: true);
    }

    public async Task DeleteSelectedSkinAsync()
    {
        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null || _isDeletingSkin)
        {
            return;
        }

        CharacterIdentity identity =
            _characterManager.GetCharacterIdentity(character);
        IReadOnlyDictionary<string, CharacterResourceFiles> knownResources =
            _getKnownResources();
        if (!knownResources.TryGetValue(
                identity.ResourceName,
                out CharacterResourceFiles? resources))
        {
            MessageBox.Show(
                _owner,
                "The selected skin resources could not be found. Run Scan " +
                "to remove stale library entries.",
                "Skin Resources Missing",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string skinDirectory;
        try
        {
            skinDirectory = CharacterResourceStorageService.GetSkinDirectory(
                resources,
                AppPaths.ResourceDirectory);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterSettingsController),
                $"skin-delete-path-rejected id={character.Id} " +
                $"message={exception.Message}");
            MessageBox.Show(
                _owner,
                exception.Message,
                "Skin Cannot Be Deleted",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            _owner,
            $"Delete {character.Name} · {identity.SkinLabel}?" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "The entire skin folder will be moved to the Recycle Bin:" +
            $"{Environment.NewLine}{skinDirectory}" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "Other skins for this character will not be changed.",
            "Delete Current Skin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        bool wasVisible = character.Visible;
        bool recycled = false;
        _isDeletingSkin = true;

        try
        {
            _characterManager.UnloadCharacter(character);
            _resourceStorage.RecycleSkinDirectory(
                skinDirectory,
                AppPaths.ResourceDirectory,
                identity.SkinCode,
                identity.CharacterCode);
            recycled = true;
            _refreshKnownResources();
            _synchronizeKnownResources();
            _refreshCharacterList();

            CharacterConfig? remainingCharacter =
                _characterManager.Characters.FirstOrDefault(candidate =>
                    string.Equals(
                        _characterManager
                            .GetCharacterIdentity(candidate)
                            .CharacterCode,
                        identity.CharacterCode,
                        StringComparison.OrdinalIgnoreCase));
            string? reloadWarning = null;
            if (wasVisible && remainingCharacter != null)
            {
                try
                {
                    await _characterManager.ShowCharacterAsync(
                        remainingCharacter);
                }
                catch (Exception exception)
                {
                    reloadWarning =
                        " The replacement skin could not be displayed; " +
                        "use the card to try again after checking its resources.";
                    AppLogger.Write(
                        nameof(CharacterSettingsController),
                        $"skin-delete-reload-failed id={remainingCharacter.Id} " +
                        $"message={exception.Message}");
                }
            }

            MessageBox.Show(
                _owner,
                $"{character.Name} · {identity.SkinLabel} was moved to " +
                $"the Recycle Bin.{reloadWarning}",
                "Skin Deleted",
                MessageBoxButton.OK,
                reloadWarning == null
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterSettingsController),
                $"skin-delete-failed id={character.Id} " +
                $"path={skinDirectory} message={exception.Message}");
            if (!recycled && wasVisible)
            {
                try
                {
                    await _characterManager.ShowCharacterAsync(character);
                }
                catch (Exception restoreException)
                {
                    AppLogger.Write(
                        nameof(CharacterSettingsController),
                        $"skin-delete-restore-failed id={character.Id} " +
                        $"message={restoreException.Message}");
                }
            }

            string message = recycled
                ? "The skin was moved to the Recycle Bin, but the library " +
                  "could not be refreshed. Run Scan and check the local log."
                : "The skin could not be moved to the Recycle Bin. " +
                  "No library entry was removed. Check the local log for details.";
            MessageBox.Show(
                _owner,
                message,
                "Skin Delete Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isDeletingSkin = false;
        }
    }

    public void SyncSelectedCharacterSettings()
    {
        _host.IsRefreshingSelection = true;
        try
        {
            _host.SelectedAnimationNames.Clear();

            if (_host.SelectedCharacter == null)
            {
                _host.SelectedAnimation = string.Empty;
                _host.SelectedScale = CharacterSettingsDefaults.DefaultScale;
                _host.SelectedScaleMax =
                    CharacterSettingsDefaults.DefaultMaxScale;
                SetSelectedScaleFromValue(
                    CharacterSettingsDefaults.DefaultScale);
                _host.SelectedSpeed = 100;
                return;
            }

            foreach (string animation in _host.SelectedCharacter.AnimationNames)
            {
                _host.SelectedAnimationNames.Add(animation);
            }

            _host.SelectedScaleMax = Math.Clamp(
                _host.SelectedCharacter.MaxScale > 0
                    ? _host.SelectedCharacter.MaxScale
                    : CharacterSettingsDefaults.DefaultMaxScale,
                CharacterSettingsDefaults.MinimumMaximumScale,
                CharacterSettingsDefaults.DefaultMaxScale);
            CharacterConfig? selectedConfig = FindSelectedCharacterConfig();
            if (selectedConfig == null ||
                !TrySetSelectedScaleFromComponents(selectedConfig))
            {
                SetSelectedScaleFromValue(
                    _host.SelectedCharacter.Scale > 0
                        ? _host.SelectedCharacter.Scale
                        : CharacterSettingsDefaults.DefaultScale);
                if (selectedConfig != null)
                {
                    selectedConfig.ScaleBasePercent =
                        _host.SelectedScaleBasePercent;
                    selectedConfig.ScaleMultiplier =
                        _host.SelectedScaleMultiplier;
                }
            }
            _host.SelectedAnimation =
                NativeCharacterRenderHost.SelectConfiguredOrIdleAnimationName(
                    _host.SelectedCharacter.ConfiguredAnimation,
                    _host.SelectedAnimationNames) ??
                string.Empty;
            _host.SelectedSpeed = Math.Clamp(
                _host.SelectedCharacter.AnimationSpeed * 100.0,
                10,
                200);
        }
        finally
        {
            _host.IsRefreshingSelection = false;
        }
    }

    public void CommitSelectedScale()
    {
        if (_host.IsRefreshingSelection ||
            _host.SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        double effectiveMaximumScale =
            _characterManager.RenderHost.IsCharacterVisible(character.Id)
                ? _characterManager.RenderHost.GetMaxScale(character.Id)
                : _host.SelectedScaleMax;
        _host.SelectedScaleMax = effectiveMaximumScale;

        double scale = Math.Clamp(
            (_host.SelectedScaleBasePercent / 100.0) *
                CharacterSettingsDefaults.MaximumBaseScale *
                _host.SelectedScaleMultiplier,
            CharacterSettingsDefaults.MinimumScale,
            effectiveMaximumScale);
        _host.SelectedScale = scale;
        character.Scale = scale;
        character.ScaleBasePercent = _host.SelectedScaleBasePercent;
        character.ScaleMultiplier = _host.SelectedScaleMultiplier;
        _host.SelectedCharacter.Scale = scale;
        _characterManager.RenderHost.SetCharacterScale(
            character.Id,
            scale);
    }

    private bool TrySetSelectedScaleFromComponents(CharacterConfig character)
    {
        if (character.ScaleBasePercent is not double basePercent ||
            character.ScaleMultiplier is not double multiplier ||
            !double.IsFinite(basePercent) ||
            !double.IsFinite(multiplier) ||
            basePercent <
                CharacterSettingsDefaults.MinimumScaleBasePercent ||
            basePercent >
                CharacterSettingsDefaults.MaximumScaleBasePercent ||
            multiplier <
                CharacterSettingsDefaults.MinimumScaleMultiplier ||
            multiplier >
                CharacterSettingsDefaults.MaximumScaleMultiplier)
        {
            return false;
        }

        SetSelectedScaleComponents(basePercent, multiplier);
        return true;
    }

    private void SetSelectedScaleFromValue(double scale)
    {
        double clamped = Math.Clamp(
            scale,
            CharacterSettingsDefaults.MinimumScale,
            CharacterSettingsDefaults.MaximumBaseScale *
                CharacterSettingsDefaults.MaximumScaleMultiplier);
        double basePercent;
        double multiplier;
        if (clamped <= CharacterSettingsDefaults.MaximumBaseScale)
        {
            basePercent = Math.Clamp(
                (clamped / CharacterSettingsDefaults.MaximumBaseScale) * 100,
                CharacterSettingsDefaults.MinimumScaleBasePercent,
                CharacterSettingsDefaults.MaximumScaleBasePercent);
            multiplier = 1;
        }
        else
        {
            basePercent = CharacterSettingsDefaults.MaximumScaleBasePercent;
            multiplier = Math.Clamp(
                clamped / CharacterSettingsDefaults.MaximumBaseScale,
                CharacterSettingsDefaults.MinimumScaleMultiplier,
                CharacterSettingsDefaults.MaximumScaleMultiplier);
        }

        SetSelectedScaleComponents(basePercent, multiplier);
    }

    private void SetSelectedScaleComponents(
        double basePercent,
        double multiplier)
    {
        bool wasRefreshing = _host.IsRefreshingSelection;
        _host.IsRefreshingSelection = true;
        try
        {
            _host.SelectedScaleBasePercent = basePercent;
            _host.SelectedScaleMultiplier = multiplier;
            _host.SelectedScale =
                (basePercent / 100.0) *
                CharacterSettingsDefaults.MaximumBaseScale *
                multiplier;
        }
        finally
        {
            _host.IsRefreshingSelection = wasRefreshing;
        }
    }

    private CharacterConfig? FindSelectedCharacterConfig()
    {
        string? characterId = _host.SelectedCharacter?.Id;
        return characterId == null
            ? null
            : _characterManager.Characters.FirstOrDefault(
                character => character.Id == characterId);
    }
}
