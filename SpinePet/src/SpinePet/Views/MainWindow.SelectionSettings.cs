using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.ViewModels;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace SpinePet.Views;

public partial class MainWindow
{
    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(
                    () => OnCharacterScaleChanged(
                        characterId,
                        maximumScale,
                        currentScale));
            }

            return;
        }

        var viewModel = Characters.FirstOrDefault(
            character => character.Id == characterId);
        if (viewModel != null)
        {
            viewModel.MaxScale = Math.Clamp(
                maximumScale,
                MinimumMaximumScale,
                DefaultMaxScale);
            viewModel.Scale = Math.Clamp(
                currentScale,
                MinimumScale,
                viewModel.MaxScale);
        }

        if (SelectedCharacter?.Id != characterId)
        {
            return;
        }

        _isRefreshingSelection = true;
        SelectedScaleMax = Math.Clamp(
            maximumScale,
            MinimumMaximumScale,
            DefaultMaxScale);
        SelectedScale = Math.Clamp(
            currentScale,
            MinimumScale,
            SelectedScaleMax);
        SetSelectedScaleFromValue(SelectedScale);
        SelectedCharacter.MaxScale = SelectedScaleMax;
        SelectedCharacter.Scale = SelectedScale;
        _isRefreshingSelection = false;
    }

    private void OnSpeedChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRefreshingSelection || SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        double speed = SelectedSpeed / 100.0;
        character.AnimationSpeed = speed;
        SelectedCharacter.AnimationSpeed = speed;
        _characterManager.RenderHost.SetCharacterSpeed(character.Id, speed);
    }

    private void OnResetScale(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter == null)
        {
            return;
        }

        SetSelectedScaleFromValue(DefaultScale);
        CommitSelectedScale();
    }

    private void OnResetSpeed(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter != null)
        {
            SelectedSpeed = 100;
        }
    }

    private void OnResetCharacterPosition(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
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

    private void OnAnimationChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isRefreshingSelection ||
            sender is not ComboBox ||
            string.IsNullOrEmpty(SelectedAnimation) ||
            SelectedCharacter == null)
        {
            return;
        }

        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        character.ConfiguredAnimation = SelectedAnimation;
        SelectedCharacter.ConfiguredAnimation = SelectedAnimation;
        _characterManager.RenderHost.PlayCharacterAnimation(
            character.Id,
            SelectedAnimation,
            repeat: true);
    }

    private async void OnDeleteSelectedSkin(object sender, RoutedEventArgs e)
    {
        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null)
        {
            return;
        }

        if (_isDeletingSkin)
        {
            return;
        }

        CharacterIdentity identity =
            _characterManager.GetCharacterIdentity(character);
        if (!_knownResources.TryGetValue(
                identity.ResourceName,
                out CharacterResourceFiles? resources))
        {
            MessageBox.Show(
                this,
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
                nameof(MainWindow),
                $"skin-delete-path-rejected id={character.Id} " +
                $"message={exception.Message}");
            MessageBox.Show(
                this,
                exception.Message,
                "Skin Cannot Be Deleted",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Delete {character.Name} · {identity.SkinLabel}?" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"The entire skin folder will be moved to the Recycle Bin:" +
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
            RefreshKnownResources();
            SynchronizeKnownResources();
            RefreshCharacterList();

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
                        nameof(MainWindow),
                        $"skin-delete-reload-failed id={remainingCharacter.Id} " +
                        $"message={exception.Message}");
                }
            }

            MessageBox.Show(
                this,
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
                nameof(MainWindow),
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
                        nameof(MainWindow),
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
                this,
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

    private void OnExitConfiguration(object sender, RoutedEventArgs e)
    {
        AppLogger.Write(
            nameof(MainWindow),
            "configuration-finished");
        _characterManager.SaveAllState();
        _isConfigMode = false;
        ApplyConfigMode();
    }

    private CharacterConfig? FindSelectedCharacterConfig()
    {
        string? characterId = SelectedCharacter?.Id;
        return characterId == null
            ? null
            : _characterManager.Characters.FirstOrDefault(
                character => character.Id == characterId);
    }

}
