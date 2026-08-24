using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Tests.TestDoubles;
using SpinePet.ViewModels;
using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SpinePet-MainViewModel-{Guid.NewGuid():N}");

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void InitialSettingsMirrorManagerConfiguration()
    {
        (MainViewModel viewModel, CharacterManager manager, _) =
            CreateViewModel();

        Assert.Equal(manager.AllowRenderDrag, viewModel.AllowRenderDrag);
        Assert.Equal(manager.TargetFrameRate, viewModel.TargetFrameRate);
        Assert.Equal(
            manager.LibraryThumbnailScalePercent,
            viewModel.ThumbnailScalePercent);
        Assert.Equal(
            CharacterDisplayModes.Normal,
            viewModel.SelectedDisplayMode);
        Assert.Equal(
            CharacterBattleStates.Cover,
            viewModel.SelectedBattleState);
        Assert.Empty(viewModel.Characters);
        Assert.False(viewModel.HasCharacters);
        Assert.False(viewModel.HasSelectedCharacter);
        Assert.False(viewModel.HasCharacterSearch);
    }

    [Fact]
    public void SearchTextRaisesFilterEventAndStatusProperties()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        CharacterViewModel? requested = null;
        int filterRequests = 0;
        viewModel.SearchFilterRequested += preferred =>
        {
            requested = preferred;
            filterRequests++;
        };

        viewModel.CharacterSearchText = "rapi";

        Assert.Equal(1, filterRequests);
        Assert.Null(requested);
        Assert.True(viewModel.HasCharacterSearch);
        Assert.True(viewModel.HasCharacterSearchInput);

        viewModel.CharacterSearchText = "rapi";
        Assert.Equal(1, filterRequests);
    }

    [Fact]
    public void LeavingSearchRestoresAnchoredSelection()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        CharacterViewModel first = CreateListCharacter("c1", "Rapi");
        CharacterViewModel second = CreateListCharacter("c2", "Neon");
        viewModel.Characters.Add(first);
        viewModel.Characters.Add(second);
        viewModel.SelectedCharacter = first;
        List<CharacterViewModel?> requestedSelections = [];
        viewModel.SearchFilterRequested +=
            preferred => requestedSelections.Add(preferred);

        viewModel.CharacterSearchText = "ne";
        viewModel.SelectedCharacter = second;
        viewModel.CharacterSearchText = string.Empty;

        // The search-entry event carries the current selection; leaving
        // search prefers the anchored selection from before the search.
        Assert.Equal(2, requestedSelections.Count);
        Assert.Equal(first, requestedSelections[0]);
        Assert.Equal(first, requestedSelections[1]);
        Assert.Equal(second, viewModel.SelectedCharacter);
        Assert.False(viewModel.HasCharacterSearch);
    }

    [Fact]
    public void ClearSearchResetsTextAndNotifiesFilter()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        int filterRequests = 0;
        viewModel.SearchFilterRequested += _ => filterRequests++;

        viewModel.CharacterSearchText = "abc";
        viewModel.ClearSearch();

        Assert.Equal(string.Empty, viewModel.CharacterSearchText);
        Assert.Equal(2, filterRequests);
    }

    [Fact]
    public void OnSelectionChangedByViewAnchorsSelectionWhileSearching()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        CharacterViewModel first = CreateListCharacter("c1", "Rapi");
        viewModel.Characters.Add(first);
        viewModel.CharacterSearchText = "rap";

        viewModel.OnSelectionChangedByView(first);

        Assert.Equal(first, viewModel.SelectedCharacter);
    }

    [Fact]
    public void DisplayModeSwitchRefreshesSelectionOptions()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        viewModel.SelectedCharacter =
            CreateListCharacter("c1", "Rapi");

        viewModel.SelectedDisplayMode = CharacterDisplayModes.Battle;
        Assert.Equal(
            viewModel.BattleStateOptions,
            viewModel.DisplaySelectionOptions);
        // Battle mode additionally requires the character to have battle
        // resources; without them the selector stays disabled.
        Assert.False(viewModel.IsDisplaySelectionEnabled);

        viewModel.SelectedAnimationNames.Add("idle");
        viewModel.SelectedAnimationNames.Add("wave");
        viewModel.SelectedDisplayMode = CharacterDisplayModes.Normal;
        Assert.Equal(
            ["idle", "wave"],
            viewModel.DisplaySelectionOptions);
        Assert.True(viewModel.IsDisplaySelectionEnabled);
    }

    [Fact]
    public void DisplayOptionsRebuildKeepsCurrentSelection()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        viewModel.SelectedCharacter =
            CreateListCharacter("c1", "Rapi");
        viewModel.SelectedAnimationNames.Add("idle");
        viewModel.SelectedAnimationNames.Add("wave");
        viewModel.SelectedAnimation = "idle";
        viewModel.SelectedDisplayMode =
            CharacterDisplayModes.Battle;

        viewModel.SelectedDisplayMode =
            CharacterDisplayModes.Normal;

        // The closed combo title must keep showing the effective value
        // after the option list is rebuilt for the same mode.
        Assert.Equal(
            ["idle", "wave"],
            viewModel.DisplaySelectionOptions);
        Assert.Equal("idle", viewModel.SelectedDisplaySelection);
    }

    [Fact]
    public void DisplayOptionsRebuildReplacesStaleEntries()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        viewModel.SelectedCharacter =
            CreateListCharacter("c1", "Rapi");
        viewModel.SelectedAnimationNames.Add("old1");
        viewModel.SelectedAnimationNames.Add("old2");
        viewModel.SelectedDisplayMode =
            CharacterDisplayModes.Battle;

        viewModel.SelectedAnimationNames.Clear();
        viewModel.SelectedAnimationNames.Add("a");
        viewModel.SelectedAnimationNames.Add("b");
        viewModel.SelectedDisplayMode =
            CharacterDisplayModes.Normal;

        Assert.Equal(
            ["a", "b"],
            viewModel.DisplaySelectionOptions);
    }

    [Fact]
    public void IsDisplaySelectionRequiresSelection()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();

        Assert.False(viewModel.IsDisplaySelectionEnabled);

        viewModel.SelectedCharacter = CreateListCharacter("c1", "Rapi");
        viewModel.SelectedDisplayMode = CharacterDisplayModes.Normal;
        Assert.False(viewModel.IsDisplaySelectionEnabled);

        viewModel.SelectedAnimationNames.Add("idle");
        viewModel.SelectedDisplayMode = CharacterDisplayModes.Battle;
        viewModel.SelectedDisplayMode = CharacterDisplayModes.Normal;
        Assert.True(viewModel.IsDisplaySelectionEnabled);

        viewModel.SelectedCharacter = null;
        Assert.False(viewModel.IsDisplaySelectionEnabled);
    }

    [Fact]
    public void SelectedDisplaySelectionRoutesByDisplayMode()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        viewModel.SelectedCharacter = CreateListCharacter("c1", "Rapi");

        viewModel.SelectedDisplayMode = CharacterDisplayModes.Battle;
        viewModel.SelectedDisplaySelection = CharacterBattleStates.Aim;
        Assert.Equal(
            CharacterBattleStates.Aim,
            viewModel.SelectedBattleState);
        Assert.Equal(
            CharacterBattleStates.Aim,
            viewModel.SelectedDisplaySelection);

        viewModel.SelectedAnimationNames.Add("idle");
        viewModel.SelectedDisplayMode = CharacterDisplayModes.Normal;
        viewModel.SelectedDisplaySelection = "idle";
        Assert.Equal("idle", viewModel.SelectedAnimation);
        Assert.Equal("idle", viewModel.SelectedDisplaySelection);

        viewModel.SelectedDisplaySelection = "missing";
        Assert.Equal("idle", viewModel.SelectedAnimation);
    }

    [Fact]
    public void ScaleComponentsClampAndRaiseCommitEvent()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        int commits = 0;
        viewModel.ScaleComponentsChanged += () => commits++;

        // The default base percent is already 100, so clamping 250 to 100
        // keeps the current value and must not raise a commit.
        viewModel.SelectedScaleBasePercent = 250;
        Assert.Equal(100, viewModel.SelectedScaleBasePercent);
        Assert.Equal("100%", viewModel.SelectedScaleBasePercentDisplay);
        Assert.Equal(0, commits);

        viewModel.SelectedScaleBasePercent = 50;
        Assert.Equal(1, commits);

        viewModel.SelectedScaleMultiplier = 9;
        Assert.Equal(5, viewModel.SelectedScaleMultiplier);
        Assert.Equal("x5.0", viewModel.SelectedScaleMultiplierDisplay);
        Assert.Equal(2, commits);

        viewModel.SelectedScaleMultiplier = 5;
        Assert.Equal(2, commits);
    }

    [Fact]
    public void ScaleAndSpeedClampToSupportedRanges()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();

        viewModel.SelectedScaleMax = 99;
        Assert.Equal(
            CharacterSettingsDefaults.DefaultMaxScale,
            viewModel.SelectedScaleMax);
        viewModel.SelectedScale = -1;
        Assert.Equal(-1, viewModel.SelectedScale);
        viewModel.SelectedSpeed = 5;
        Assert.Equal(10, viewModel.SelectedSpeed);
        viewModel.SelectedSpeed = 300;
        Assert.Equal(200, viewModel.SelectedSpeed);
        Assert.Equal("2.00x", viewModel.SelectedSpeedDisplay);
    }

    [Fact]
    public void AllowRenderDragWritesThroughAndPersists()
    {
        (MainViewModel viewModel, CharacterManager manager,
            ConfigService configService) = CreateViewModel();

        viewModel.AllowRenderDrag = !viewModel.AllowRenderDrag;

        Assert.Equal(
            viewModel.AllowRenderDrag,
            manager.AllowRenderDrag);
        Assert.Equal(
            viewModel.AllowRenderDrag,
            configService.Load().Global.AllowRenderDrag);
    }

    [Fact]
    public void TargetFrameRateNormalizesAndPersists()
    {
        (MainViewModel viewModel, CharacterManager manager,
            ConfigService configService) = CreateViewModel();

        viewModel.TargetFrameRate = GlobalConfig.HighRefreshTargetFrameRate;

        Assert.Equal(
            GlobalConfig.HighRefreshTargetFrameRate,
            manager.TargetFrameRate);
        Assert.Equal(
            GlobalConfig.HighRefreshTargetFrameRate,
            configService.Load().Global.TargetFrameRate);
        Assert.Contains(
            GlobalConfig.HighRefreshTargetFrameRate,
            viewModel.FrameRateOptions);
    }

    [Fact]
    public void ThumbnailScaleRaisesAppliedEventAndPersists()
    {
        (MainViewModel viewModel, CharacterManager manager,
            ConfigService configService) = CreateViewModel();
        int applied = 0;
        viewModel.ThumbnailScaleApplied += () => applied++;

        viewModel.ThumbnailScalePercent = 150;

        Assert.Equal(1, applied);
        Assert.Equal("150%", viewModel.ThumbnailScaleDisplay);
        Assert.Equal(
            150,
            manager.LibraryThumbnailScalePercent);
        Assert.Equal(
            150,
            configService.Load().Global.LibraryThumbnailScalePercent);
    }

    [Fact]
    public void SyncWithoutSelectionResetsDisplayState()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        int syncs = 0;
        viewModel.SettingsSyncRequested += () => syncs++;

        viewModel.SelectedDisplayMode = CharacterDisplayModes.Battle;
        viewModel.SyncSelectedCharacterSettings();

        Assert.Equal(1, syncs);
        Assert.Equal(
            CharacterDisplayModes.Normal,
            viewModel.SelectedDisplayMode);
        Assert.Equal(
            CharacterBattleStates.Cover,
            viewModel.SelectedBattleState);
        Assert.Empty(viewModel.DisplaySelectionOptions);
    }

    [Fact]
    public void SyncReadsRuntimeModeFromManagerForSelection()
    {
        CharacterConfig character =
            BattleInteractionControllerTests.CreateBattleCharacter(
                "vm-battle");
        (MainViewModel viewModel, CharacterManager manager, _) =
            CreateViewModel(configCharacters: character);
        character = Assert.Single(manager.Characters);
        CharacterViewModel selection = CreateListCharacter(
            character.Id,
            character.Name);
        viewModel.Characters.Add(selection);
        viewModel.SelectedCharacter = selection;

        // Battle resources are validated during configuration load, so the
        // view model must reflect whatever the manager actually holds.
        Assert.Equal(
            character.Battle != null,
            viewModel.HasSelectedBattle);

        viewModel.SyncSelectedCharacterSettings();

        Assert.Equal(
            CharacterDisplayModes.Normal,
            viewModel.SelectedDisplayMode);
        Assert.Empty(viewModel.DisplaySelectionOptions);
    }

    [Fact]
    public void ApplyRuntimeBattleStateOnlyUpdatesSelectedCharacter()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        CharacterViewModel selection = CreateListCharacter("c1", "Rapi");
        viewModel.SelectedCharacter = selection;

        viewModel.ApplyRuntimeBattleState(
            "other",
            CharacterDisplayModes.Battle,
            CharacterBattleStates.Aim);
        Assert.Equal(
            CharacterDisplayModes.Normal,
            viewModel.SelectedDisplayMode);

        viewModel.ApplyRuntimeBattleState(
            selection.Id,
            CharacterDisplayModes.Battle,
            CharacterBattleStates.Aim);
        Assert.Equal(
            CharacterDisplayModes.Battle,
            viewModel.SelectedDisplayMode);
        Assert.Equal(
            CharacterBattleStates.Aim,
            viewModel.SelectedBattleState);
        Assert.Equal(
            viewModel.BattleStateOptions,
            viewModel.DisplaySelectionOptions);
    }

    [Fact]
    public void FindSelectedCharacterConfigResolvesManagerConfig()
    {
        CharacterConfig character = new()
        {
            Id = "vm-find",
            Name = "Find me"
        };
        (MainViewModel viewModel, CharacterManager manager, _) =
            CreateViewModel(configCharacters: character);
        viewModel.SelectedCharacter =
            CreateListCharacter(character.Id, character.Name);

        CharacterConfig? resolved = viewModel.FindSelectedCharacterConfig();

        Assert.Same(manager.Characters.Single(), resolved);
    }

    [Fact]
    public void NotifySearchResultsChangedUpdatesCountsAndStatus()
    {
        (MainViewModel viewModel, _, _) = CreateViewModel();
        viewModel.Characters.Add(CreateListCharacter("c1", "Rapi"));
        viewModel.Characters.Add(CreateListCharacter("c2", "Neon"));

        viewModel.NotifySearchResultsChanged();
        Assert.Equal(2, viewModel.MatchingCharacterCount);
        Assert.Equal("2 loaded", viewModel.CharacterCountDisplay);

        viewModel.CharacterSearchText = "rap";
        // The library controller refreshes the view before the counts are
        // recalculated; mirror that order here.
        viewModel.CharacterView.Refresh();
        viewModel.NotifySearchResultsChanged();

        Assert.Equal(1, viewModel.MatchingCharacterCount);
        Assert.Equal("1 of 2", viewModel.CharacterCountDisplay);
        Assert.Equal(
            "1 matching characters out of 2 loaded",
            viewModel.CharacterSearchStatus);
    }

    [Fact]
    public void BindingSmoke()
    {
        RunOnSta(() =>
        {
            TextBox box = new();
            box.SetBinding(
                TextBox.TextProperty,
                new System.Windows.Data.Binding("SelectedAnimation"));
            box.DataContext = viewModelForSmoke();
            Window window = new()
            {
                Content = box,
                Width = 200,
                Height = 60,
                ShowInTaskbar = false
            };
            window.Show();
            PumpCallbacks!();
            Assert.Equal("smoke", box.Text);
        });

        static MainViewModel viewModelForSmoke()
        {
            CharacterConfig character = new()
            {
                Id = "smoke",
                Name = "Smoke"
            };
            ConfigService configService = new(Path.Combine(
                Path.GetTempPath(),
                $"spinepet-smoke-{Guid.NewGuid():N}.json"));
            CharacterManager manager = new(
                configService,
                new CharacterIdentityService(
                    new Dictionary<string, string>()),
                new FakeCharacterRenderHost());
            MainViewModel viewModel = new(manager);
            viewModel.SelectedAnimation = "smoke";
            return viewModel;
        }
    }

    [Fact]
    public void RealComboSelectsResourceDefaultAfterShowReplacesResetState()
    {
        RunOnSta(() =>
        {
            CharacterConfig character = new()
            {
                Id = "reset-combo",
                Name = "Reset Combo",
                ConfiguredAnimation = "wave",
                Visible = false
            };
            var (viewModel, _, _) =
                CreateViewModel(configCharacters: character);
            CharacterViewModel selection = CreateListCharacter(
                character.Id,
                character.Name);
            viewModel.Characters.Add(selection);
            viewModel.SelectedCharacter = selection;
            // Mirror what CharacterSettingsController does on settings
            // sync: copy the card's animation names and pick the
            // configured-or-idle animation.
            viewModel.SettingsSyncRequested += () =>
            {
                viewModel.SelectedAnimationNames.Clear();
                foreach (string name in selection.AnimationNames)
                {
                    viewModel.SelectedAnimationNames.Add(name);
                }

                viewModel.SelectedAnimation =
                    selection.AnimationNames.FirstOrDefault(name =>
                        name.Equals(
                            selection.ConfiguredAnimation,
                            StringComparison.OrdinalIgnoreCase)) ??
                    selection.AnimationNames.FirstOrDefault(name =>
                        name.StartsWith(
                            "idle",
                            StringComparison.OrdinalIgnoreCase)) ??
                    selection.AnimationNames.FirstOrDefault() ??
                    string.Empty;
            };

            ComboBox combo = new();
            ComboBox modeCombo = new();
            modeCombo.SetBinding(
                System.Windows.Controls.ItemsControl
                    .ItemsSourceProperty,
                new System.Windows.Data.Binding(
                    "DisplayModeOptions"));
            modeCombo.SetBinding(
                System.Windows.Controls.Primitives.Selector
                    .SelectedItemProperty,
                new System.Windows.Data.Binding(
                    "SelectedDisplayMode")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });
            combo.SetBinding(
                System.Windows.UIElement.IsEnabledProperty,
                new System.Windows.Data.Binding(
                    "IsDisplaySelectionEnabled"));
            StackPanel panel = new();
            panel.Children.Add(modeCombo);
            panel.Children.Add(combo);
            panel.DataContext = viewModel;
            using DisplaySelectionComboCoordinator coordinator =
                new(combo, viewModel);
            Window window = new()
            {
                Content = panel,
                Width = 200,
                Height = 100,
                ShowInTaskbar = false
            };
            window.Show();
            PumpCallbacks!();

            // The previously selected character also used idle, but from a
            // different option list.
            selection.UpdateAnimationNames(["idle", "wave"]);
            selection.ConfiguredAnimation = "idle";
            viewModel.SyncSelectedCharacterSettings();
            PumpCallbacks!();
            Assert.Equal("idle", combo.SelectedItem);

            // Selecting or showing another character replaces the items with
            // its real animations while the source value remains "idle".
            selection.UpdateAnimationNames(
                ["action", "delight", "expression_0", "idle"]);
            viewModel.SyncSelectedCharacterSettings();
            PumpCallbacks!();

            Assert.Equal("idle", viewModel.SelectedDisplaySelection);
            Assert.Equal("idle", (string?)combo.SelectedItem);
            combo.IsDropDownOpen = true;
            PumpCallbacks!();
            Assert.True(
                ((ComboBoxItem)combo.ItemContainerGenerator
                    .ContainerFromItem("idle")).IsSelected);
            combo.IsDropDownOpen = false;

            // Mode changes replace the animation items with a disjoint
            // Battle list. Returning to Normal must restore the real
            // configured animation as the closed combo title.
            modeCombo.SelectedItem = CharacterDisplayModes.Battle;
            PumpCallbacks!();
            Assert.Equal(
                CharacterBattleStates.Cover,
                (string?)combo.SelectedItem);
            modeCombo.SelectedItem = CharacterDisplayModes.Normal;
            PumpCallbacks!();
            Assert.Equal("idle", viewModel.SelectedDisplaySelection);
            Assert.Equal("idle", (string?)combo.SelectedItem);

            // Repeating the same refresh must preserve the same real item.
            viewModel.SyncSelectedCharacterSettings();
            PumpCallbacks!();
            Assert.Equal("idle", (string?)combo.SelectedItem);
            window.Close();
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                // Bindings only transfer values through dispatcher
                // operations; create the dispatcher up front and pump a
                // frame whenever the action needs bindings applied.
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                void pump()
                {
                    DispatcherFrame frame = new();
                    dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }

                PumpCallbacks = pump;
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                PumpCallbacks = null;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    private static Action? PumpCallbacks { get; set; }

    private (
        MainViewModel ViewModel,
        CharacterManager Manager,
        ConfigService ConfigService) CreateViewModel(
            params CharacterConfig[] configCharacters)
    {
        ConfigService configService = new(Path.Combine(
            _temporaryDirectory,
            $"config-{Guid.NewGuid():N}.json"));
        if (configCharacters.Length > 0)
        {
            configService.Save(new AppConfig
            {
                Characters = configCharacters.ToList()
            });
        }

        CharacterManager manager = new(
            configService,
            new CharacterIdentityService(
                new Dictionary<string, string>()),
            new FakeCharacterRenderHost());
        MainViewModel viewModel = new(manager);
        return (viewModel, manager, configService);
    }

    private static CharacterViewModel CreateListCharacter(
        string id,
        string name) => new()
    {
        Id = id,
        Name = name
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
