using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace SpinePet.Views;

public partial class MainWindow
{
    private void OnCharacterCardsPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        ScrollViewer? characterScrollViewer =
            FindVisualChild<ScrollViewer>(CharacterCards);
        if (characterScrollViewer == null)
        {
            return;
        }

        if (CanScroll(characterScrollViewer, e.Delta))
        {
            ScrollByDelta(characterScrollViewer, e.Delta);
            e.Handled = true;
            return;
        }

        ScrollByDelta(MainScrollViewer, e.Delta);
        e.Handled = true;
    }

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
        SelectedScalePercent = ConvertScaleToPercent(
            SelectedScale,
            SelectedScaleMax);
        SelectedCharacter.MaxScale = SelectedScaleMax;
        SelectedCharacter.Scale = SelectedScale;
        _isRefreshingSelection = false;
    }

    private void OnScaleChanged(
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

        double effectiveMaximumScale =
            _characterManager.RenderHost.IsCharacterVisible(character.Id)
                ? _characterManager.RenderHost.GetMaxScale(character.Id)
                : SelectedScaleMax;

        if (!NearlyEquals(SelectedScaleMax, effectiveMaximumScale))
        {
            _isRefreshingSelection = true;
            SelectedScaleMax = effectiveMaximumScale;
            double rendererScale =
                _characterManager.RenderHost.GetCurrentScale(character.Id);
            double currentScale = Math.Clamp(
                rendererScale > 0 ? rendererScale : SelectedScale,
                MinimumScale,
                effectiveMaximumScale);
            SelectedScale = currentScale;
            SelectedScalePercent = ConvertScaleToPercent(
                currentScale,
                effectiveMaximumScale);
            SelectedCharacter.Scale = currentScale;
            character.Scale = currentScale;
            _isRefreshingSelection = false;
            return;
        }

        double scale = Math.Clamp(
            ConvertPercentToScale(
                SelectedScalePercent,
                SelectedScaleMax),
            MinimumScale,
            effectiveMaximumScale);
        character.Scale = scale;
        SelectedCharacter.Scale = scale;
        SelectedScale = scale;
        _characterManager.RenderHost.SetCharacterScale(character.Id, scale);
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
        if (SelectedCharacter != null)
        {
            SelectedScalePercent = ConvertScaleToPercent(
                DefaultScale,
                SelectedScaleMax);
        }
    }

    private void OnResetSpeed(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter != null)
        {
            SelectedSpeed = 100;
        }
    }

    private void OnResetSelectedPosition(object sender, RoutedEventArgs e)
    {
        CharacterConfig? character = FindSelectedCharacterConfig();
        if (character == null || SelectedCharacter == null)
        {
            return;
        }

        _characterManager.RenderHost.ResetCharacterPosition(character.Id);
        SelectedCharacter.PositionX = (int)character.PositionX;
        SelectedCharacter.PositionY = (int)character.PositionY;
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

    private void OnOverlayCharacterMoved(
        string characterId,
        double left,
        double top)
    {
        CharacterConfig? character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;

        var viewModel = Characters.FirstOrDefault(
            item => item.Id == characterId);
        if (viewModel != null)
        {
            viewModel.PositionX = (int)left;
            viewModel.PositionY = (int)top;
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

    private static double ConvertScaleToPercent(
        double scale,
        double maximumScale)
    {
        if (maximumScale <= MinimumScale)
        {
            return 100;
        }

        double normalized =
            (scale - MinimumScale) / (maximumScale - MinimumScale);
        return Math.Clamp(normalized * 100, 0, 100);
    }

    private static double ConvertPercentToScale(
        double percent,
        double maximumScale)
    {
        double normalized = Math.Clamp(percent, 0, 100) / 100.0;
        return MinimumScale +
            ((maximumScale - MinimumScale) * normalized);
    }

    private static bool CanScroll(
        ScrollViewer scrollViewer,
        int wheelDelta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return wheelDelta < 0
            ? scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight
            : scrollViewer.VerticalOffset > 0;
    }

    private static void ScrollByDelta(
        ScrollViewer scrollViewer,
        int wheelDelta)
    {
        int wheelLines = Math.Clamp(
            SystemParameters.WheelScrollLines,
            1,
            6);
        double step = Math.Max(
            48,
            wheelLines * 16);
        double nextOffset =
            scrollViewer.VerticalOffset - (wheelDelta / 120.0 * step);
        scrollViewer.ScrollToVerticalOffset(
            Math.Clamp(nextOffset, 0, scrollViewer.ScrollableHeight));
    }

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent == null)
        {
            return null;
        }

        for (int index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
