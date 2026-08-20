using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.ViewModels;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SpinePet.Views;

public partial class MainWindow
{
    private readonly HashSet<string> _visibilityOperations =
        new(StringComparer.Ordinal);

    private void RefreshKnownResources()
    {
        _knownResources = _resourceDiscovery
            .DiscoverAll(AppPaths.ResourceDirectory)
            .GroupBy(
                resource => resource.Identity.ResourceName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshCharacterList()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(RefreshCharacterList);
            }

            return;
        }

        bool wasUpdatingSelection = _isUpdatingCharacterSelection;
        _isUpdatingCharacterSelection = true;
        try
        {
            RefreshCharacterListCore();
        }
        finally
        {
            _isUpdatingCharacterSelection = wasUpdatingSelection;
        }
    }

    private void RefreshCharacterListCore()
    {
        string? selectedId = SelectedCharacter?.Id;
        Dictionary<string, CharacterViewModel> existing = Characters.ToDictionary(
            character => character.Id,
            StringComparer.Ordinal);
        HashSet<string> currentIds = new(StringComparer.Ordinal);

        foreach (CharacterConfig character in _characterManager.Characters
                     .OrderBy(character => character.Name, StringComparer.CurrentCulture))
        {
            currentIds.Add(character.Id);
            List<string> animationNames = _characterManager.RenderHost
                .GetAnimationNames(character.Id)
                .ToList();
            double maximumScale = Math.Clamp(
                _characterManager.RenderHost.GetMaxScale(character.Id),
                MinimumMaximumScale,
                DefaultMaxScale);
            double scale = Math.Clamp(
                character.Scale > 0 ? character.Scale : DefaultScale,
                MinimumScale,
                maximumScale);

            if (animationNames.Count == 0 &&
                !string.IsNullOrEmpty(character.ConfiguredAnimation))
            {
                animationNames.Add(character.ConfiguredAnimation);
            }

            if (existing.TryGetValue(
                character.Id,
                out CharacterViewModel? viewModel))
            {
                UpdateCharacterViewModel(
                    viewModel,
                    character,
                    animationNames,
                    maximumScale,
                    scale);
            }
            else
            {
                CharacterViewModel newViewModel = new()
                {
                    Id = character.Id
                };
                UpdateCharacterViewModel(
                    newViewModel,
                    character,
                    animationNames,
                    maximumScale,
                    scale);
                Characters.Add(newViewModel);
            }
        }

        for (int index = Characters.Count - 1; index >= 0; index--)
        {
            if (!currentIds.Contains(Characters[index].Id))
            {
                Characters.RemoveAt(index);
            }
        }

        List<CharacterViewModel> sorted = Characters
            .OrderBy(character => character.Name, StringComparer.CurrentCulture)
            .ToList();
        for (int index = 0; index < sorted.Count; index++)
        {
            int currentIndex = Characters.IndexOf(sorted[index]);
            if (currentIndex != index)
            {
                Characters.Move(currentIndex, index);
            }
        }

        OnPropertyChanged(nameof(HasCharacters));

        CharacterViewModel? preferredSelection =
            Characters.FirstOrDefault(character => character.Id == selectedId) ??
            Characters.FirstOrDefault();
        RefreshCharacterFilter(preferredSelection);
    }

    private void UpdateCharacterViewModel(
        CharacterViewModel viewModel,
        CharacterConfig character,
        IReadOnlyList<string> animationNames,
        double maximumScale,
        double scale)
    {
        CharacterIdentity identity =
            _characterManager.GetCharacterIdentity(character);
        CharacterIdentity[] availableSkins = _knownResources
            .Values
            .Select(available => available.Identity)
            .Where(availableIdentity => string.Equals(
                availableIdentity.CharacterCode,
                identity.CharacterCode,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (availableSkins.Length == 0)
        {
            availableSkins = [identity];
        }

        viewModel.Name = character.Name;
        viewModel.SkinLabel = identity.SkinLabel;
        viewModel.UpdateSkins(identity.SkinCode, availableSkins);
        viewModel.ThumbnailPath =
            _characterManager.GetCharacterThumbnailPath(character);
        viewModel.Scale = scale;
        viewModel.MaxScale = maximumScale;
        viewModel.PositionX = (int)character.PositionX;
        viewModel.PositionY = (int)character.PositionY;
        viewModel.IsVisible = character.Visible;
        viewModel.IsLoading = _characterManager.IsCharacterLoading(character.Id);
        viewModel.UpdateAnimationNames(animationNames);
        viewModel.ConfiguredAnimation = character.ConfiguredAnimation;
        viewModel.AnimationSpeed = character.AnimationSpeed;
    }

    private void RefreshCharacterFilter(
        CharacterViewModel? preferredSelection)
    {
        bool wasUpdatingSelection = _isUpdatingCharacterSelection;
        _isUpdatingCharacterSelection = true;
        try
        {
            CharacterView.Refresh();

            CharacterViewModel? nextSelection =
                preferredSelection != null &&
                CharacterView.Contains(preferredSelection)
                    ? preferredSelection
                    : CharacterView
                        .Cast<CharacterViewModel>()
                        .FirstOrDefault();

            SelectedCharacter = nextSelection;
            CharacterCards.SelectedItem = nextSelection;
        }
        finally
        {
            _isUpdatingCharacterSelection = wasUpdatingSelection;
        }

        OnPropertyChanged(nameof(MatchingCharacterCount));
        OnPropertyChanged(nameof(CharacterCountDisplay));
        OnPropertyChanged(nameof(CharacterSearchStatus));
        CommandManager.InvalidateRequerySuggested();
        SyncSelectedCharacterSettings();
        AnnounceCharacterSearchStatus();
    }

    private void OnFocusCharacterSearchExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        CharacterSearchBox.Focus();
        CharacterSearchBox.SelectAll();
        CharacterSearchBox.BringIntoView();
        e.Handled = true;
    }

    private void OnClearCharacterSearchExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        CharacterSearchText = string.Empty;
        CharacterSearchBox.Focus();
        e.Handled = true;
    }

    private void OnCanClearCharacterSearchExecuted(
        object sender,
        CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = HasCharacterSearchInput;
        e.Handled = true;
    }

    private void OnFocusCharacterResultsExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        CharacterViewModel? result =
            SelectedCharacter != null &&
            CharacterView.Contains(SelectedCharacter)
                ? SelectedCharacter
                : CharacterView
                    .Cast<CharacterViewModel>()
                    .FirstOrDefault();
        if (result == null)
        {
            return;
        }

        CharacterCards.SelectedItem = result;
        CharacterCards.ScrollIntoView(result);
        CharacterCards.UpdateLayout();
        if (CharacterCards.ItemContainerGenerator.ContainerFromItem(result)
            is ListBoxItem item)
        {
            item.Focus();
        }
        else
        {
            CharacterCards.Focus();
        }

        e.Handled = true;
    }

    private void OnCanFocusCharacterResultsExecuted(
        object sender,
        CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = MatchingCharacterCount > 0;
        e.Handled = true;
    }

    private void AnnounceCharacterSearchStatus()
    {
        if (!IsLoaded || !CharacterCountText.IsVisible)
        {
            return;
        }

        _searchAnnouncementTimer.Stop();
        _searchAnnouncementTimer.Start();
    }

    private void OnCharacterSearchAnnouncementTick(
        object? sender,
        EventArgs e)
    {
        _searchAnnouncementTimer.Stop();
        if (!CharacterCountText.IsVisible)
        {
            return;
        }

        AutomationPeer? peer =
            UIElementAutomationPeer.FromElement(
                CharacterCountText) ??
            UIElementAutomationPeer.CreatePeerForElement(
                CharacterCountText);
        if (peer == null)
        {
            return;
        }

        peer.RaiseAutomationEvent(
            AutomationEvents.LiveRegionChanged);
    }

    private async void OnAddCharacter(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select a Spine skeleton or UnityFS character bundle",
            Filter =
                "Spine Skeleton|*.skel|" +
                "UnityFS Character Bundle|*.bundle;*.*|" +
                "All Files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (string.Equals(
            Path.GetExtension(dialog.FileName),
            ".skel",
            StringComparison.OrdinalIgnoreCase))
        {
            ImportSkeleton(dialog.FileName);
            return;
        }

        Button? addButton = sender as Button;
        if (addButton != null)
        {
            addButton.IsEnabled = false;
        }

        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            CharacterBundleImportResult result =
                await _bundleImporter.ImportAsync(
                    dialog.FileName,
                    AppPaths.ResourceDirectory,
                    _lifetimeCancellation.Token);
            CharacterIconDownloadResult? iconDownload = null;
            if (result.Resources is { } standingResources)
            {
                iconDownload = await _characterIconDownloader
                    .DownloadMissingAsync(
                        standingResources,
                        _lifetimeCancellation.Token);
                if (!iconDownload.IsSuccess)
                {
                    AppLogger.Write(
                        nameof(MainWindow),
                        $"automatic-icon-download-failed " +
                        $"resource={standingResources.Identity.ResourceName} " +
                        $"message={iconDownload.ErrorMessage}");
                }
            }

            RefreshKnownResources();
            CharacterResourceSynchronizationResult synchronization =
                SynchronizeKnownResources();
            RefreshCharacterList();
            string message = result.IsIcon
                ? "The character icon was extracted for this skin."
                : synchronization.AddedCount > 0
                    ? "The character resources were extracted and added to the library."
                    : "The standing resources were extracted and refreshed.";
            MessageBoxImage messageImage = MessageBoxImage.Information;
            if (iconDownload?.WasDownloaded == true)
            {
                message += Environment.NewLine +
                    "The card icon was downloaded automatically.";
            }
            else if (iconDownload?.Status ==
                     CharacterIconDownloadStatus.Failed)
            {
                message += Environment.NewLine + Environment.NewLine +
                    "The standing resources were imported, but the card icon " +
                    "could not be downloaded automatically." +
                    Environment.NewLine + iconDownload.ErrorMessage;
                messageImage = MessageBoxImage.Warning;
            }

            Mouse.OverrideCursor = null;
            MessageBox.Show(
                this,
                $"{message}{Environment.NewLine}{result.DestinationDirectory}",
                "Character Imported",
                MessageBoxButton.OK,
                messageImage);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Write(
                nameof(MainWindow),
                "bundle-import-cancelled reason=application-shutdown");
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"bundle-import-failed message={exception.Message}");
            Mouse.OverrideCursor = null;
            MessageBox.Show(
                this,
                exception.Message,
                "Character Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (addButton != null)
            {
                addButton.IsEnabled = true;
            }
        }
    }

    private async void OnScanResources(object sender, RoutedEventArgs e)
    {
        Button? scanButton = sender as Button;
        if (scanButton != null)
        {
            scanButton.IsEnabled = false;
        }

        try
        {
            Dictionary<string, string> visibleResourcePaths =
                _characterManager.Characters
                    .Where(character => character.Visible)
                    .ToDictionary(
                        character => character.Id,
                        character => character.SkeletonPath,
                        StringComparer.Ordinal);
            RefreshKnownResources();
            SynchronizeKnownResources();
            int reloadFailureCount = 0;
            foreach (CharacterConfig character in _characterManager.Characters)
            {
                if (!character.Visible ||
                    !visibleResourcePaths.TryGetValue(
                        character.Id,
                        out string? previousSkeletonPath) ||
                    string.Equals(
                        previousSkeletonPath,
                        character.SkeletonPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _characterManager.UnloadCharacter(character);
                try
                {
                    await _characterManager.ShowCharacterAsync(character);
                }
                catch (Exception exception)
                {
                    reloadFailureCount++;
                    AppLogger.Write(
                        nameof(MainWindow),
                        $"scan-reload-failed id={character.Id} " +
                        $"message={exception.Message}");
                }
            }

            RefreshCharacterList();
            if (reloadFailureCount > 0)
            {
                MessageBox.Show(
                    this,
                    $"Scan updated the library, but {reloadFailureCount} " +
                    "visible character(s) could not be reloaded. Check their " +
                    "resources and the local log.",
                    "Resource Scan Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"resource-scan-failed message={exception.Message}");
            MessageBox.Show(
                this,
                "The resource folder could not be scanned. Check the local " +
                "log for details.",
                "Resource Scan Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (scanButton != null)
            {
                scanButton.IsEnabled = true;
            }
        }
    }

    private CharacterResourceSynchronizationResult SynchronizeKnownResources()
    {
        return _characterManager.SynchronizeResources(
            _knownResources.Values,
            AppPaths.ResourceDirectory);
    }

    private void OnOpenResourceFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            CharacterResourceStorageService.OpenResourceDirectory(
                AppPaths.ResourceDirectory);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"resource-folder-open-failed message={exception.Message}");
            MessageBox.Show(
                this,
                "The resource folder could not be opened. " +
                "Check the local log for details.",
                "Resource Folder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportSkeleton(string skeletonPath)
    {
        try
        {
            CharacterBundleImportResult result = _bundleImporter.ImportSkeleton(
                skeletonPath,
                AppPaths.ResourceDirectory,
                cancellationToken: _lifetimeCancellation.Token);
            RefreshKnownResources();
            CharacterResourceSynchronizationResult synchronization =
                SynchronizeKnownResources();
            RefreshCharacterList();
            string message = synchronization.AddedCount > 0
                ? "The Spine resources were copied and added to the library."
                : "The Spine resources were copied into the managed library.";
            MessageBox.Show(
                this,
                $"{message}{Environment.NewLine}{result.DestinationDirectory}",
                "Character Imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"skeleton-import-failed message={exception.Message}");
            MessageBox.Show(
                this,
                exception.Message,
                "Character Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCharacterSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingCharacterSelection)
        {
            return;
        }

        SelectedCharacter =
            CharacterCards.SelectedItem as CharacterViewModel;
        if (HasCharacterSearch)
        {
            _selectionBeforeSearchId =
                SelectedCharacter?.Id;
        }

        SyncSelectedCharacterSettings();
    }

    private void OnMoveSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateOverlayState();
    }

    private async void OnCharacterCardsPreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            e.IsRepeat ||
            Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        CharacterViewModel? focusedCharacter = null;
        if (e.OriginalSource is DependencyObject originalSource &&
            ItemsControl.ContainerFromElement(
                CharacterCards,
                originalSource) is ListBoxItem focusedItem)
        {
            focusedCharacter =
                focusedItem.DataContext as CharacterViewModel;
        }

        focusedCharacter ??=
            CharacterCards.SelectedItem as CharacterViewModel;
        if (focusedCharacter == null ||
            !focusedCharacter.CanToggleVisibility)
        {
            return;
        }

        e.Handled = true;
        await ToggleCharacterVisibilityAsync(focusedCharacter);
    }

    private async void OnCharacterSkinExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not CharacterSkinOptionViewModel option)
        {
            return;
        }

        CharacterConfig? character = _characterManager.Characters.FirstOrDefault(
            item => item.Id == option.CharacterId);
        if (character == null ||
            !_knownResources.TryGetValue(
                option.ResourceName,
                out CharacterResourceFiles? resources))
        {
            return;
        }

        CharacterIdentity currentIdentity =
            _characterManager.GetCharacterIdentity(character);
        if (!string.Equals(
                currentIdentity.CharacterCode,
                option.CharacterCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _characterManager.SwitchCharacterResourcesAsync(
                character,
                resources);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(MainWindow),
                $"skin-switch-failed id={character.Id} " +
                $"skin={option.SkinCode} message={exception.Message}");
            MessageBox.Show(
                this,
                "The character skin could not be loaded. " +
                "Check the local log for details.",
                "Character Skin Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnToggleCharacter(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not CharacterViewModel viewModel)
        {
            return;
        }

        await ToggleCharacterVisibilityAsync(viewModel);
    }

    private async Task ToggleCharacterVisibilityAsync(
        CharacterViewModel viewModel)
    {
        if (viewModel.IsLoading ||
            !_visibilityOperations.Add(viewModel.Id))
        {
            return;
        }

        try
        {
            CharacterConfig? character =
                _characterManager.Characters.FirstOrDefault(
                    item => item.Id == viewModel.Id);
            if (character == null)
            {
                return;
            }

            if (viewModel.IsVisible)
            {
                _characterManager.HideCharacter(character);
            }
            else
            {
                try
                {
                    await _characterManager.ShowCharacterAsync(character);
                }
                catch (Exception exception)
                {
                    AppLogger.Write(
                        nameof(MainWindow),
                        $"show-character-failed id={character.Id} message={exception.Message}");
                    MessageBox.Show(
                        this,
                        "The character could not be displayed. Check the local log for details.",
                        "Render Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            UpdateOverlayState();
            if (character.Visible && _isConfigMode)
            {
                ShowOverlay();
                Activate();
            }
        }
        finally
        {
            _visibilityOperations.Remove(viewModel.Id);
        }
    }
}
