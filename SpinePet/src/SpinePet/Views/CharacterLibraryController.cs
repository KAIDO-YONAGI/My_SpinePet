using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.ViewModels;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using ICollectionView = System.ComponentModel.ICollectionView;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;

namespace SpinePet.Views;

internal sealed class CharacterLibraryController
{
    private readonly CharacterManager _characterManager;
    private readonly CharacterResourceDiscoveryService _resourceDiscovery;
    private readonly UnityBundleImportService _bundleImporter;
    private readonly CharacterIconDownloadService _characterIconDownloader;
    private readonly ObservableCollection<CharacterViewModel> _characters;
    private readonly ICollectionView _characterView;
    private readonly ListBox _characterCards;
    private readonly CharacterPreviewNavigationController _previewNavigation;
    private readonly Dispatcher _dispatcher;
    private readonly Window _owner;
    private readonly CancellationToken _lifetimeToken;
    private readonly Func<CharacterViewModel?> _getSelectedCharacter;
    private readonly Action<CharacterViewModel?> _setSelectedCharacter;
    private readonly Action _syncSelectedCharacterSettings;
    private readonly Action _notifySearchResultsChanged;
    private readonly Action _announceSearchStatus;
    private readonly Func<bool> _isConfigMode;
    private readonly HashSet<string> _visibilityOperations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CharacterResourceFiles> _knownResources =
        new(StringComparer.OrdinalIgnoreCase);

    public CharacterLibraryController(
        CharacterManager characterManager,
        CharacterResourceDiscoveryService resourceDiscovery,
        UnityBundleImportService bundleImporter,
        CharacterIconDownloadService characterIconDownloader,
        ObservableCollection<CharacterViewModel> characters,
        ICollectionView characterView,
        ListBox characterCards,
        CharacterPreviewNavigationController previewNavigation,
        Dispatcher dispatcher,
        Window owner,
        Func<CharacterViewModel?> getSelectedCharacter,
        Action<CharacterViewModel?> setSelectedCharacter,
        Action syncSelectedCharacterSettings,
        Action notifySearchResultsChanged,
        Action announceSearchStatus,
        Func<bool> isConfigMode,
        CancellationToken lifetimeToken)
    {
        _characterManager = characterManager;
        _resourceDiscovery = resourceDiscovery;
        _bundleImporter = bundleImporter;
        _characterIconDownloader = characterIconDownloader;
        _characters = characters;
        _characterView = characterView;
        _characterCards = characterCards;
        _previewNavigation = previewNavigation;
        _dispatcher = dispatcher;
        _owner = owner;
        _lifetimeToken = lifetimeToken;
        _getSelectedCharacter = getSelectedCharacter;
        _setSelectedCharacter = setSelectedCharacter;
        _syncSelectedCharacterSettings = syncSelectedCharacterSettings;
        _notifySearchResultsChanged = notifySearchResultsChanged;
        _announceSearchStatus = announceSearchStatus;
        _isConfigMode = isConfigMode;
    }

    public bool IsUpdatingSelection { get; private set; }

    public IReadOnlyDictionary<string, CharacterResourceFiles> KnownResources =>
        _knownResources;

    public void RefreshKnownResources()
    {
        Dictionary<string, CharacterResourceFiles> discovered =
            _resourceDiscovery
                .DiscoverAll(AppPaths.ResourceDirectory)
                .GroupBy(
                    resource => resource.Identity.ResourceName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
        _knownResources.Clear();
        foreach (KeyValuePair<string, CharacterResourceFiles> resource in discovered)
        {
            _knownResources[resource.Key] = resource.Value;
        }
    }

    public void RefreshCharacterList()
    {
        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted)
            {
                _dispatcher.BeginInvoke(RefreshCharacterList);
            }

            return;
        }

        bool wasUpdatingSelection = IsUpdatingSelection;
        IsUpdatingSelection = true;
        try
        {
            RefreshCharacterListCore();
        }
        finally
        {
            IsUpdatingSelection = wasUpdatingSelection;
        }
    }

    public void RefreshCharacterFilter(CharacterViewModel? preferredSelection)
    {
        _previewNavigation.ClearReveal();
        bool wasUpdatingSelection = IsUpdatingSelection;
        CharacterViewModel? nextSelection;
        IsUpdatingSelection = true;
        try
        {
            _characterView.Refresh();
            _notifySearchResultsChanged();

            nextSelection =
                preferredSelection != null &&
                _characterView.Contains(preferredSelection)
                    ? preferredSelection
                    : _characterView
                        .Cast<CharacterViewModel>()
                        .FirstOrDefault();

            _setSelectedCharacter(nextSelection);
            _characterCards.SelectedItem = nextSelection;
        }
        finally
        {
            IsUpdatingSelection = wasUpdatingSelection;
        }

        _syncSelectedCharacterSettings();
        if (_isConfigMode() && nextSelection != null)
        {
            _previewNavigation.Reveal(nextSelection);
        }

        _announceSearchStatus();
    }

    public CharacterViewModel? FindCharacter(string characterId) =>
        _characters.FirstOrDefault(character => character.Id == characterId);

    public void FocusCharacterResults()
    {
        CharacterViewModel? result =
            _getSelectedCharacter() != null &&
            _characterView.Contains(_getSelectedCharacter())
                ? _getSelectedCharacter()
                : _characterView
                    .Cast<CharacterViewModel>()
                    .FirstOrDefault();
        if (result == null)
        {
            return;
        }

        _previewNavigation.SelectAndReveal(result);
        if (_characterCards.ItemContainerGenerator.ContainerFromItem(result)
            is ListBoxItem item)
        {
            item.Focus();
        }
        else
        {
            _characterCards.Focus();
        }
    }

    public async Task AddCharacterAsync(Button? addButton)
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

        if (dialog.ShowDialog(_owner) != true)
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
                    _lifetimeToken);
            CharacterIconDownloadResult? iconDownload = null;
            if (result.Resources is { } standingResources)
            {
                iconDownload = await _characterIconDownloader
                    .DownloadMissingAsync(
                        standingResources,
                        _lifetimeToken);
                if (!iconDownload.IsSuccess)
                {
                    AppLogger.Write(
                        nameof(CharacterLibraryController),
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

            MessageBox.Show(
                _owner,
                $"{message}{Environment.NewLine}{result.DestinationDirectory}",
                "Character Imported",
                MessageBoxButton.OK,
                messageImage);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Write(
                nameof(CharacterLibraryController),
                "bundle-import-cancelled reason=application-shutdown");
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterLibraryController),
                $"bundle-import-failed message={exception.Message}");
            MessageBox.Show(
                _owner,
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

    public async Task ScanResourcesAsync(Button? scanButton)
    {
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
                        nameof(CharacterLibraryController),
                        $"scan-reload-failed id={character.Id} " +
                        $"message={exception.Message}");
                }
            }

            RefreshCharacterList();
            if (reloadFailureCount > 0)
            {
                MessageBox.Show(
                    _owner,
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
                nameof(CharacterLibraryController),
                $"resource-scan-failed message={exception.Message}");
            MessageBox.Show(
                _owner,
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

    public void OpenResourceFolder()
    {
        try
        {
            CharacterResourceStorageService.OpenResourceDirectory(
                AppPaths.ResourceDirectory);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterLibraryController),
                $"resource-folder-open-failed message={exception.Message}");
            MessageBox.Show(
                _owner,
                "The resource folder could not be opened. " +
                "Check the local log for details.",
                "Resource Folder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public CharacterResourceSynchronizationResult SynchronizeKnownResources() =>
        _characterManager.SynchronizeResources(
            _knownResources.Values,
            AppPaths.ResourceDirectory);

    public void ImportSkeleton(string skeletonPath)
    {
        try
        {
            CharacterBundleImportResult result = _bundleImporter.ImportSkeleton(
                skeletonPath,
                AppPaths.ResourceDirectory,
                cancellationToken: _lifetimeToken);
            RefreshKnownResources();
            CharacterResourceSynchronizationResult synchronization =
                SynchronizeKnownResources();
            RefreshCharacterList();
            string message = synchronization.AddedCount > 0
                ? "The Spine resources were copied and added to the library."
                : "The Spine resources were copied into the managed library.";
            MessageBox.Show(
                _owner,
                $"{message}{Environment.NewLine}{result.DestinationDirectory}",
                "Character Imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterLibraryController),
                $"skeleton-import-failed message={exception.Message}");
            MessageBox.Show(
                _owner,
                exception.Message,
                "Character Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async Task HandleCharacterSkinAsync(
        CharacterSkinOptionViewModel? option)
    {
        if (option == null)
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
                nameof(CharacterLibraryController),
                $"skin-switch-failed id={character.Id} " +
                $"skin={option.SkinCode} message={exception.Message}");
            MessageBox.Show(
                _owner,
                "The character skin could not be loaded. " +
                "Check the local log for details.",
                "Character Skin Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public async Task<bool> HandlePreviewKeyDownAsync(KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            e.IsRepeat ||
            Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        CharacterViewModel? focusedCharacter = null;
        if (e.OriginalSource is DependencyObject originalSource &&
            ItemsControl.ContainerFromElement(
                _characterCards,
                originalSource) is ListBoxItem focusedItem)
        {
            focusedCharacter =
                focusedItem.DataContext as CharacterViewModel;
        }

        focusedCharacter ??=
            _characterCards.SelectedItem as CharacterViewModel;
        if (focusedCharacter == null ||
            !focusedCharacter.CanToggleVisibility)
        {
            return false;
        }

        await ToggleCharacterVisibilityAsync(focusedCharacter);
        return true;
    }

    public async Task ToggleCharacterAsync(CharacterViewModel? viewModel)
    {
        if (viewModel != null)
        {
            await ToggleCharacterVisibilityAsync(viewModel);
        }
    }

    private void RefreshCharacterListCore()
    {
        string? selectedId = _getSelectedCharacter()?.Id;
        Dictionary<string, CharacterViewModel> existing = _characters.ToDictionary(
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
                CharacterSettingsDefaults.MinimumMaximumScale,
                CharacterSettingsDefaults.DefaultMaxScale);
            double scale = Math.Clamp(
                character.Scale > 0
                    ? character.Scale
                    : CharacterSettingsDefaults.DefaultScale,
                CharacterSettingsDefaults.MinimumScale,
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
                _characters.Add(newViewModel);
            }
        }

        for (int index = _characters.Count - 1; index >= 0; index--)
        {
            if (!currentIds.Contains(_characters[index].Id))
            {
                _characters.RemoveAt(index);
            }
        }

        List<CharacterViewModel> sorted = _characters
            .OrderBy(character => character.Name, StringComparer.CurrentCulture)
            .ToList();
        for (int index = 0; index < sorted.Count; index++)
        {
            int currentIndex = _characters.IndexOf(sorted[index]);
            if (currentIndex != index)
            {
                _characters.Move(currentIndex, index);
            }
        }

        CharacterViewModel? preferredSelection =
            _characters.FirstOrDefault(character => character.Id == selectedId) ??
            _characters.FirstOrDefault();
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
        viewModel.IsLoading =
            _characterManager.IsCharacterLoading(character.Id);
        viewModel.UpdateAnimationNames(animationNames);
        viewModel.ConfiguredAnimation = character.ConfiguredAnimation;
        viewModel.AnimationSpeed = character.AnimationSpeed;
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
                        nameof(CharacterLibraryController),
                        $"show-character-failed id={character.Id} " +
                        $"message={exception.Message}");
                    MessageBox.Show(
                        _owner,
                        "The character could not be displayed. Check the local " +
                        "log for details.",
                        "Render Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            if (character.Visible && _isConfigMode())
            {
                _owner.Activate();
            }
        }
        finally
        {
            _visibilityOperations.Remove(viewModel.Id);
        }
    }
}
