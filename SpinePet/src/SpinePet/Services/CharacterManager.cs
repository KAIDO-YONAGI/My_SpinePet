using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using SpinePet.Infrastructure;
using SpinePet.Infrastructure.Import;
using SpinePet.Models;
using SpinePet.Rendering;
using SpinePet.Rendering.Native;

namespace SpinePet.Services;

public sealed class CharacterManager
{
    private readonly ConfigService _configService;
    private readonly CharacterIdentityService _identityService;
    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The renderer backend is intentionally isolated behind this contract.")]
    private readonly ICharacterRenderHost _renderHost;
    private readonly CharacterResourceCoordinator _resourceCoordinator;
    private readonly Rect? _workArea;
    private readonly AppConfig _config;
    private readonly object _showSync = new();
    private readonly Dictionary<string, ShowFlight> _showFlights =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CharacterBattleRuntimeState>
        _battleRuntime = new(StringComparer.Ordinal);
    private bool _isConfigMode;
    private int _configModeVersion;
    private bool _closed;

    public CharacterManager(
        ConfigService configService,
        CharacterIdentityService? identityService = null,
        ICharacterRenderHost? renderHost = null)
        : this(configService, identityService, renderHost, null)
    {
    }

    internal CharacterManager(
        ConfigService configService,
        CharacterIdentityService? identityService,
        ICharacterRenderHost? renderHost,
        Rect? workArea)
    {
        _configService = configService;
        _identityService = identityService ?? new CharacterIdentityService();
        _resourceCoordinator = new CharacterResourceCoordinator(
            _identityService);
        _workArea = workArea;
        _config = configService.Load();
        _renderHost = renderHost ?? new NativeCharacterRenderHost();
        _renderHost.SetRenderDragEnabled(_config.Global.AllowRenderDrag);
        _renderHost.SetTargetFrameRate(_config.Global.TargetFrameRate);
        _renderHost.CharacterScaleChanged += OnCharacterScaleChanged;
        _renderHost.CharacterAnimationsLoaded += OnCharacterAnimationsLoaded;
        _renderHost.CharacterLoadFailed += OnCharacterLoadFailed;
        _renderHost.CharacterStateChanged += OnCharacterStateChanged;
        _renderHost.CharacterPositionCommitted += OnCharacterPositionCommitted;
        _renderHost.CharacterRightPressed += OnCharacterRightPressed;
        _renderHost.CharacterRightReleased += OnCharacterRightReleased;
    }

    public IReadOnlyList<CharacterConfig> Characters => _config.Characters;

    public bool AllowRenderDrag => _config.Global.AllowRenderDrag;

    public int TargetFrameRate => _config.Global.TargetFrameRate;

    public int LibraryThumbnailScalePercent =>
        _config.Global.LibraryThumbnailScalePercent;

    public BattleRulesConfig BattleRules => _config.Global.BattleRules;

    public event Action? CharactersChanged;

    public event Action<string, double, double>? CharacterScaleChanged;

    public event Action<CharacterRenderSnapshot>? CharacterStateChanged;

    public event Action<string, double, double>? CharacterPositionChanged;

    public event Action<string>? CharacterRightClicked;

    public event Action<string, string, string>? CharacterBattleStateChanged;

    public ICharacterRenderHost RenderHost => _renderHost;

    public string GetCharacterDisplayMode(string characterId) =>
        GetBattleRuntime(characterId).Mode;

    public string GetCharacterBattleState(string characterId) =>
        GetBattleRuntime(characterId).BattleState;

    public async Task<bool> SetCharacterDisplayModeAsync(
        CharacterConfig character,
        string mode)
    {
        ArgumentNullException.ThrowIfNull(character);
        CharacterBattleRuntimeState runtime =
            GetBattleRuntime(character.Id);
        CancelBattleHold(runtime);
        int operationVersion = ++runtime.OperationVersion;
        if (string.Equals(
                mode,
                CharacterDisplayModes.Battle,
                StringComparison.OrdinalIgnoreCase))
        {
            if (character.Battle == null)
                return false;

            await EnsureBattleReadyAsync(character);
            if (operationVersion != runtime.OperationVersion)
                return false;

            if (!await _renderHost.SetCharacterResourceStateAsync(
                character.Id,
                CharacterBattleStates.Cover,
                character.Battle.Animations.CoverIdle))
            {
                NotifyBattleState(character.Id, runtime);
                return false;
            }

            runtime.Mode = CharacterDisplayModes.Battle;
            runtime.BattleState = CharacterBattleStates.Cover;
        }
        else
        {
            if (!await _renderHost.SetCharacterResourceStateAsync(
                character.Id,
                CharacterDisplayModes.Normal,
                character.ConfiguredAnimation))
            {
                NotifyBattleState(character.Id, runtime);
                return false;
            }

            runtime.Mode = CharacterDisplayModes.Normal;
            runtime.BattleState = CharacterBattleStates.Cover;
        }

        NotifyBattleState(character.Id, runtime);
        return true;
    }

    public async Task<bool> SetCharacterBattleStateAsync(
        CharacterConfig character,
        string battleState)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.Battle == null)
            return false;

        CharacterBattleRuntimeState runtime =
            GetBattleRuntime(character.Id);
        CancelBattleHold(runtime);
        int operationVersion = ++runtime.OperationVersion;
        await EnsureBattleReadyAsync(character);
        if (operationVersion != runtime.OperationVersion)
            return false;

        string targetState = battleState.Equals(
            CharacterBattleStates.Aim,
            StringComparison.OrdinalIgnoreCase)
                ? CharacterBattleStates.Aim
                : CharacterBattleStates.Cover;
        string? idle = targetState == CharacterBattleStates.Aim
            ? character.Battle.Animations.AimIdle
            : character.Battle.Animations.CoverIdle;
        if (!await _renderHost.SetCharacterResourceStateAsync(
            character.Id,
            targetState,
            idle))
        {
            NotifyBattleState(character.Id, runtime);
            return false;
        }

        runtime.Mode = CharacterDisplayModes.Battle;
        runtime.BattleState = targetState;
        NotifyBattleState(character.Id, runtime);
        return true;
    }

    public void SetConfigMode(bool configMode)
    {
        if (_closed || _isConfigMode == configMode)
        {
            return;
        }

        _isConfigMode = configMode;
        _configModeVersion++;
        _renderHost.SetConfigMode(configMode);
    }

    public void SetAllowRenderDrag(bool allow)
    {
        if (_config.Global.AllowRenderDrag == allow)
        {
            return;
        }

        _config.Global.AllowRenderDrag = allow;
        _renderHost.SetRenderDragEnabled(allow);
        _configService.Save(_config);
    }

    public void SetTargetFrameRate(int frameRate)
    {
        int normalized = GlobalConfig.NormalizeTargetFrameRate(frameRate);
        if (_config.Global.TargetFrameRate == normalized)
        {
            return;
        }

        _config.Global.TargetFrameRate = normalized;
        _renderHost.SetTargetFrameRate(normalized);
        _configService.Save(_config);
    }

    public void SetLibraryThumbnailScale(int percent)
    {
        int normalized =
            GlobalConfig.NormalizeLibraryThumbnailScale(percent);
        if (_config.Global.LibraryThumbnailScalePercent == normalized)
        {
            return;
        }

        _config.Global.LibraryThumbnailScalePercent = normalized;
        _configService.Save(_config);
    }

    public bool AddCharacter(CharacterResourceFiles resources)
    {
        EnsureStandingResources(resources);

        CharacterConfig? character = _config.Characters.FirstOrDefault(character =>
            string.Equals(
                character.SkeletonPath,
                resources.SkeletonPath,
                StringComparison.OrdinalIgnoreCase));
        character ??= _resourceCoordinator.FindPreferredCharacter(
            _config.Characters,
            resources);

        if (character != null)
        {
            CharacterIdentity currentIdentity = GetCharacterIdentity(character);
            bool shouldUpdatePaths =
                !CharacterResourceCoordinator.ResourcesExist(character) ||
                IsSameSkin(
                    currentIdentity,
                    resources.Identity);
            bool changed = SetIfDifferent(
                character.Name,
                resources.Identity.DisplayName,
                value => character.Name = value);
            if (shouldUpdatePaths)
            {
                changed |= UpdateCharacterResources(character, resources);
            }

            if (changed)
            {
                _configService.Save(_config);
                CharactersChanged?.Invoke();
            }

            return false;
        }

        character = CreateCharacter(resources);

        _config.Characters.Add(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
        return true;
    }

    public CharacterIdentity GetCharacterIdentity(CharacterConfig character)
    {
        return _identityService.Resolve(
            character.SkeletonPath,
            character.Name);
    }

    public string GetCharacterThumbnailPath(CharacterConfig character)
    {
        CharacterIdentity identity = GetCharacterIdentity(character);
        return CharacterIconService.GetThumbnailPath(character, identity);
    }

    public Task ShowCharacterAsync(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed ||
            (character.Visible &&
             _renderHost.IsCharacterVisible(character.Id) &&
             !_renderHost.IsCharacterLoading(character.Id)))
        {
            return Task.CompletedTask;
        }

        (Task<bool> task, bool ownsSideEffects) =
            GetOrStartShowFlight(character);
        return CompleteShowAsync(task, ownsSideEffects);
    }

    private async Task CompleteShowAsync(
        Task<bool> task,
        bool ownsSideEffects)
    {
        bool changed = await task;
        if (ownsSideEffects && changed && !_closed)
        {
            _configService.Save(_config);
        }
    }

    public async Task SwitchCharacterResourcesAsync(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        EnsureStandingResources(resources);
        SpineSkeletonCompatibility.EnsureSupported(
            resources.SkeletonPath);

        if (!string.Equals(
            _resourceCoordinator.GetGroupKey(character),
            _resourceCoordinator.GetGroupKey(resources),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected resources belong to a different character.");
        }

        if (CharacterResourceCoordinator.ResourcesMatch(
                character,
                resources))
        {
            if (SetIfDifferent(
                    character.Name,
                    resources.Identity.DisplayName,
                    value => character.Name = value))
            {
                _configService.Save(_config);
                CharactersChanged?.Invoke();
            }

            return;
        }

        bool wasVisible = character.Visible;
        string previousName = character.Name;
        string previousSkeletonPath = character.SkeletonPath;
        string previousAtlasPath = character.AtlasPath;
        string previousTexturePath = character.TexturePath;
        List<string> previousAdditionalTexturePaths =
            character.AdditionalTexturePaths.ToList();
        string previousAnimation = character.ConfiguredAnimation;
        bool previousRequiresStandingMigration =
            character.RequiresStandingMigration;
        if (wasVisible)
        {
            _renderHost.RemoveCharacter(character.Id);
        }

        character.ConfiguredAnimation = string.Empty;
        UpdateCharacterResources(character, resources);

        try
        {
            if (wasVisible)
            {
                await EnsureCharacterShownAsync(character);
            }
        }
        catch
        {
            _renderHost.RemoveCharacter(character.Id);
            character.Name = previousName;
            character.SkeletonPath = previousSkeletonPath;
            character.AtlasPath = previousAtlasPath;
            character.TexturePath = previousTexturePath;
            character.AdditionalTexturePaths =
                previousAdditionalTexturePaths;
            character.ConfiguredAnimation = previousAnimation;
            character.RequiresStandingMigration =
                previousRequiresStandingMigration;
            character.Visible = wasVisible;

            if (wasVisible)
            {
                try
                {
                    await EnsureCharacterShownAsync(character);
                }
                catch (Exception rollbackException)
                {
                    AppLogger.Write(
                        nameof(CharacterManager),
                        $"resource-switch-rollback-failed " +
                        $"id={character.Id} " +
                        $"message={rollbackException.Message}");
                }
            }

            throw;
        }
        finally
        {
            _configService.Save(_config);
            CharactersChanged?.Invoke();
        }
    }

    public CharacterResourceSynchronizationResult SynchronizeResources(
        IEnumerable<CharacterResourceFiles> resources,
        string managedRoot)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        CharacterResourceFiles[] resourceCatalog = resources.ToArray();

        CharacterResourceSynchronizationPlan plan =
            _resourceCoordinator.CalculateSynchronization(
                _config.Characters,
                resourceCatalog,
                managedRoot);
        int addedCount = 0;
        int updatedCount = 0;
        int removedCount = 0;
        int mergedCount = 0;

        foreach (CharacterRemovalPlan removal in plan.Removals)
        {
            RemoveCharacterFromConfiguration(removal.Character);
            if (removal.IsMerge)
            {
                mergedCount++;
            }
            else
            {
                removedCount++;
            }
        }

        foreach (CharacterUpdatePlan update in plan.Updates)
        {
            UpdateCharacterResources(update.Character, update.Resources);
            if (update.ClearConfiguredAnimation)
            {
                update.Character.ConfiguredAnimation = string.Empty;
            }
            updatedCount++;
        }

        foreach (CharacterResourceFiles addition in plan.Additions)
        {
            _config.Characters.Add(CreateCharacter(addition));
            addedCount++;
        }

        foreach (CharacterConfig character in _config.Characters)
        {
            CharacterResourceFiles? standing = resourceCatalog
                .FirstOrDefault(resource =>
                    string.Equals(
                        resource.ResourceType,
                        CharacterResourceTypes.Standing,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        resource.SkeletonPath,
                        character.SkeletonPath,
                        StringComparison.OrdinalIgnoreCase));
            CharacterBattleConfig? battle = standing == null
                ? null
                : CharacterBattleConfigFactory.TryCreate(
                    standing,
                    resourceCatalog);
            if (!BattleProfilesMatch(character.Battle, battle))
            {
                character.Battle = battle;
                updatedCount++;
            }
        }

        CharacterResourceSynchronizationResult result = new(
            addedCount,
            updatedCount,
            removedCount,
            mergedCount);
        if (result.HasChanges || _config.RequiresRewrite)
        {
            _configService.Save(_config);
        }

        if (result.HasChanges)
        {
            CharactersChanged?.Invoke();
        }

        return result;
    }

    private (Task<bool> Task, bool Owner) GetOrStartShowFlight(
        CharacterConfig character)
    {
        string resourceKey = GetResourceKey(character);
        lock (_showSync)
        {
            if (_showFlights.TryGetValue(
                    character.Id,
                    out ShowFlight? existing) &&
                string.Equals(
                    existing.ResourceKey,
                    resourceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (existing.Task, false);
            }

            Task<bool> task = existing == null
                ? EnsureCharacterShownAsync(character)
                : ShowAfterAsync(existing.Task, character);
            _showFlights[character.Id] = new ShowFlight(resourceKey, task);
            _ = task.ContinueWith(
                _ => RemoveShowFlight(character.Id, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return (task, true);
        }
    }

    private async Task<bool> ShowAfterAsync(
        Task<bool> previous,
        CharacterConfig character)
    {
        try
        {
            await previous;
        }
        catch
        {
            // A newer resource request is independent of the previous load.
        }

        return await EnsureCharacterShownAsync(character);
    }

    private async Task<bool> EnsureCharacterShownAsync(
        CharacterConfig character)
    {
        if (_closed ||
            (character.Visible &&
             _renderHost.IsCharacterVisible(character.Id) &&
             !_renderHost.IsCharacterLoading(character.Id)))
        {
            return false;
        }

        bool wasVisible = character.Visible;
        character.Visible = true;
        try
        {
            await _renderHost.ShowCharacterAsync(
                character,
                _isConfigMode,
                character.AnimationSpeed);
            if (_closed)
            {
                return false;
            }

            if (!_config.Characters.Contains(character))
            {
                _renderHost.RemoveCharacter(character.Id);
                return false;
            }

            if (!character.Visible)
            {
                if (_renderHost.IsCharacterVisible(character.Id) ||
                    _renderHost.IsCharacterLoading(character.Id))
                {
                    _renderHost.HideCharacter(character.Id);
                }

                return false;
            }

            return true;
        }
        catch
        {
            if (!_closed && character.Visible)
            {
                character.Visible = wasVisible;
                _renderHost.HideCharacter(character.Id);
            }

            throw;
        }
    }

    private void RemoveShowFlight(string characterId, Task<bool> task)
    {
        lock (_showSync)
        {
            if (_showFlights.TryGetValue(
                    characterId,
                    out ShowFlight? current) &&
                ReferenceEquals(current.Task, task))
            {
                _showFlights.Remove(characterId);
            }
        }
    }

    public void HideCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed ||
            (!character.Visible &&
             !_renderHost.IsCharacterVisible(character.Id) &&
             !_renderHost.IsCharacterLoading(character.Id)))
        {
            return;
        }

        character.Visible = false;
        ResetBattleRuntime(character.Id);
        _renderHost.HideCharacter(character.Id);
        _configService.Save(_config);
    }

    public void UnloadCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed)
        {
            return;
        }
        ResetBattleRuntime(character.Id);
        _renderHost.RemoveCharacter(character.Id);
    }

    public void RemoveCharacter(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (_closed || !_config.Characters.Contains(character))
        {
            return;
        }

        character.Visible = false;
        ResetBattleRuntime(character.Id);
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void HideAll()
    {
        if (_closed ||
            !_config.Characters.Any(character =>
                character.Visible ||
                _renderHost.IsCharacterVisible(character.Id) ||
                _renderHost.IsCharacterLoading(character.Id)))
        {
            return;
        }

        foreach (CharacterConfig character in _config.Characters)
        {
            character.Visible = false;
            ResetBattleRuntime(character.Id);
        }

        _renderHost.HideAll();
        _configService.Save(_config);
    }

    public async Task ShowAllAsync()
    {
        if (_closed)
        {
            return;
        }

        List<Task<bool>> tasks = [];
        foreach (CharacterConfig character in _config.Characters)
        {
            if (character.Visible &&
                _renderHost.IsCharacterVisible(character.Id) &&
                !_renderHost.IsCharacterLoading(character.Id))
            {
                continue;
            }

            (Task<bool> task, bool ownsSideEffects) =
                GetOrStartShowFlight(character);
            if (ownsSideEffects)
            {
                tasks.Add(task);
            }
        }

        if (tasks.Count == 0)
        {
            return;
        }

        bool[] changes = await Task.WhenAll(tasks);
        if (changes.Any(changed => changed) && !_closed)
        {
            _configService.Save(_config);
        }
    }

    public void ResetAllSettings()
    {
        Rect workArea = _workArea ?? SystemParameters.WorkArea;
        double defaultPositionX = workArea.Left + workArea.Width / 2;
        double defaultPositionY = workArea.Bottom - 24;

        foreach (CharacterConfig character in _config.Characters)
        {
            IReadOnlyList<string> animationNames =
                _renderHost.GetAnimationNames(character.Id);
            string? idleAnimation =
                NativeAnimationController.SelectIdleAnimationName(
                    animationNames);

            character.Scale = CharacterConfig.DefaultScale;
            character.ScaleBasePercent =
                CharacterConfig.DefaultScaleBasePercent;
            character.ScaleMultiplier =
                CharacterConfig.DefaultScaleMultiplier;
            character.AnimationSpeed =
                CharacterConfig.DefaultAnimationSpeed;
            character.ConfiguredAnimation = idleAnimation ?? string.Empty;
            character.PositionX = defaultPositionX;
            character.PositionY = defaultPositionY;

            _renderHost.SetCharacterScale(
                character.Id,
                CharacterConfig.DefaultScale);
            _renderHost.SetCharacterSpeed(
                character.Id,
                CharacterConfig.DefaultAnimationSpeed);
            if (idleAnimation != null)
            {
                _renderHost.PlayCharacterAnimation(
                    character.Id,
                    idleAnimation,
                    repeat: true);
            }

            _renderHost.ResetCharacterPosition(character.Id);
        }

        _configService.Save(_config);
        CharactersChanged?.Invoke();
    }

    public void SaveAllState()
    {
        _configService.Save(_config);
    }

    public async Task RestoreAllAsync(bool configMode)
    {
        int modeVersion = _configModeVersion;
        if (modeVersion == 0)
        {
            _isConfigMode = configMode;
        }

        await _renderHost.RestoreVisibleCharactersAsync(_config.Characters, configMode);
        _renderHost.SetConfigMode(
            modeVersion == _configModeVersion ? configMode : _isConfigMode);
    }

    public bool IsCharacterLoading(string characterId) =>
        _renderHost.IsCharacterLoading(characterId);

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _renderHost.CharacterScaleChanged -= OnCharacterScaleChanged;
        _renderHost.CharacterAnimationsLoaded -= OnCharacterAnimationsLoaded;
        _renderHost.CharacterLoadFailed -= OnCharacterLoadFailed;
        _renderHost.CharacterStateChanged -= OnCharacterStateChanged;
        _renderHost.CharacterPositionCommitted -= OnCharacterPositionCommitted;
        _renderHost.CharacterRightPressed -= OnCharacterRightPressed;
        _renderHost.CharacterRightReleased -= OnCharacterRightReleased;
        foreach (CharacterBattleRuntimeState runtime in _battleRuntime.Values)
        {
            CancelBattleHold(runtime);
        }
        _battleRuntime.Clear();
        _renderHost.Close();
    }

    private void OnCharacterScaleChanged(
        string characterId,
        double maximumScale,
        double currentScale)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null)
        {
            character.Scale = currentScale;
        }

        CharacterScaleChanged?.Invoke(characterId, maximumScale, currentScale);
    }

    private void OnCharacterAnimationsLoaded(
        string characterId,
        IReadOnlyList<string> animations)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null &&
            string.IsNullOrWhiteSpace(character.ConfiguredAnimation) &&
            animations.Count > 0)
        {
            character.ConfiguredAnimation =
                NativeAnimationController.SelectIdleAnimationName(animations)
                ?? animations[0];
        }

    }

    private void OnCharacterLoadFailed(string characterId)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character != null)
        {
            character.Visible = false;
        }

    }

    private void OnCharacterStateChanged(
        CharacterRenderSnapshot snapshot)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(
                item => item.Id == snapshot.CharacterId);
        if (character != null)
        {
            character.Visible = snapshot.IsVisible;
            character.Scale = snapshot.CurrentScale;
        }

        CharacterStateChanged?.Invoke(snapshot);
    }

    private void OnCharacterPositionCommitted(
        string characterId,
        double left,
        double top)
    {
        if (_closed)
        {
            return;
        }

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character == null)
        {
            return;
        }

        character.PositionX = left;
        character.PositionY = top;
        _ = _configService.SaveAsync(_config);
        CharacterPositionChanged?.Invoke(characterId, left, top);
    }

    private void OnCharacterRightPressed(string characterId)
    {
        if (_closed)
            return;

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character == null)
            return;

        CharacterBattleRuntimeState runtime =
            GetBattleRuntime(characterId);
        CancelBattleHold(runtime);
        int operationVersion = ++runtime.OperationVersion;
        runtime.LongHoldTriggered = false;
        runtime.HoldCancellation = new CancellationTokenSource();
        if (character.Battle != null &&
            runtime.Mode == CharacterDisplayModes.Battle)
        {
            _ = TriggerBattleHoldAsync(
                character,
                runtime,
                operationVersion,
                runtime.HoldCancellation.Token);
        }
    }

    private void OnCharacterRightReleased(string characterId)
    {
        if (_closed)
            return;

        CharacterConfig? character =
            _config.Characters.FirstOrDefault(item => item.Id == characterId);
        if (character == null)
            return;

        CharacterBattleRuntimeState runtime =
            GetBattleRuntime(characterId);
        bool wasLongHold = runtime.LongHoldTriggered;
        CancelBattleHold(runtime);
        int operationVersion = ++runtime.OperationVersion;
        if (wasLongHold &&
            character.Battle != null &&
            runtime.Mode == CharacterDisplayModes.Battle)
        {
            _ = ReturnToCoverAsync(
                character,
                runtime,
                operationVersion);
            return;
        }

        if (_config.Global.BattleRules.ShortRightClickOpensPanel)
        {
            CharacterRightClicked?.Invoke(characterId);
        }
    }

    private async Task TriggerBattleHoldAsync(
        CharacterConfig character,
        CharacterBattleRuntimeState runtime,
        int operationVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                _config.Global.BattleRules.RightHoldThresholdMs,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureBattleReadyAsync(character);
            cancellationToken.ThrowIfCancellationRequested();
            if (operationVersion != runtime.OperationVersion ||
                runtime.Mode != CharacterDisplayModes.Battle)
            {
                return;
            }

            CharacterBattleAnimationsConfig animations =
                character.Battle!.Animations;
            if (!await _renderHost.SetCharacterResourceStateAsync(
                character.Id,
                CharacterBattleStates.Aim,
                animations.AimIdle))
            {
                NotifyBattleState(character.Id, runtime);
                return;
            }

            runtime.LongHoldTriggered = true;
            runtime.BattleState = CharacterBattleStates.Aim;
            List<string> sequence = [];
            if (animations.ToAim != null)
                sequence.Add(animations.ToAim);
            _renderHost.PlayCharacterAnimationSequence(
                character.Id,
                sequence,
                animations.AimIdle,
                loopLast:
                    animations.AimFireLayers?.Count > 0 &&
                    _config.Global.BattleRules.ContinuousFireWhileHeld,
                battleLayers: animations.AimFireLayers);
            NotifyBattleState(character.Id, runtime);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterManager),
                $"battle-hold-failed id={character.Id} " +
                $"message={exception.Message}");
        }
    }

    private async Task ReturnToCoverAsync(
        CharacterConfig character,
        CharacterBattleRuntimeState runtime,
        int operationVersion)
    {
        try
        {
            await EnsureBattleReadyAsync(character);
            if (operationVersion != runtime.OperationVersion ||
                runtime.Mode != CharacterDisplayModes.Battle)
            {
                return;
            }

            CharacterBattleAnimationsConfig animations =
                character.Battle!.Animations;
            if (!await _renderHost.SetCharacterResourceStateAsync(
                character.Id,
                CharacterBattleStates.Cover,
                animations.CoverIdle))
            {
                NotifyBattleState(character.Id, runtime);
                return;
            }

            runtime.BattleState = CharacterBattleStates.Cover;
            List<string> sequence = [];
            if (animations.ToCover != null)
                sequence.Add(animations.ToCover);
            if (_config.Global.BattleRules.ReloadOnRelease)
                sequence.AddRange(animations.ReloadSequence);
            _renderHost.PlayCharacterAnimationSequence(
                character.Id,
                sequence,
                animations.CoverIdle,
                loopLast: false);
            NotifyBattleState(character.Id, runtime);
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(CharacterManager),
                $"battle-cover-failed id={character.Id} " +
                $"message={exception.Message}");
        }
    }

    private async Task EnsureBattleReadyAsync(CharacterConfig character)
    {
        if (!character.Visible)
        {
            await ShowCharacterAsync(character);
        }
        await _renderHost.PreloadBattleResourcesAsync(character);
    }

    private CharacterBattleRuntimeState GetBattleRuntime(string characterId)
    {
        if (!_battleRuntime.TryGetValue(
                characterId,
                out CharacterBattleRuntimeState? runtime))
        {
            runtime = new CharacterBattleRuntimeState();
            _battleRuntime[characterId] = runtime;
        }
        return runtime;
    }

    private void NotifyBattleState(
        string characterId,
        CharacterBattleRuntimeState runtime) =>
        CharacterBattleStateChanged?.Invoke(
            characterId,
            runtime.Mode,
            runtime.BattleState);

    private void ResetBattleRuntime(string characterId)
    {
        if (_battleRuntime.Remove(
                characterId,
                out CharacterBattleRuntimeState? runtime))
        {
            CancelBattleHold(runtime);
        }
    }

    private static void CancelBattleHold(
        CharacterBattleRuntimeState runtime)
    {
        runtime.HoldCancellation?.Cancel();
        runtime.HoldCancellation?.Dispose();
        runtime.HoldCancellation = null;
        runtime.LongHoldTriggered = false;
    }

    private void RemoveCharacterFromConfiguration(CharacterConfig character)
    {
        _renderHost.RemoveCharacter(character.Id);
        _config.Characters.Remove(character);
    }

    private CharacterConfig CreateCharacter(
        CharacterResourceFiles resources)
    {
        Rect workArea = _workArea ?? SystemParameters.WorkArea;
        return new CharacterConfig
        {
            Name = resources.Identity.DisplayName,
            SkeletonPath = resources.SkeletonPath,
            AtlasPath = resources.AtlasPath,
            TexturePath = resources.PrimaryTexturePath,
            AdditionalTexturePaths = resources.AdditionalTexturePaths.ToList(),
            PositionX = workArea.Left + workArea.Width / 2,
            PositionY = workArea.Bottom - 24
        };
    }

    private static bool IsSameSkin(
        CharacterIdentity leftIdentity,
        CharacterIdentity rightIdentity)
    {
        return string.Equals(
            leftIdentity.SkinCode,
            rightIdentity.SkinCode,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool UpdateCharacterResources(
        CharacterConfig character,
        CharacterResourceFiles resources)
    {
        bool changed = false;
        changed |= SetIfDifferent(
            character.Name,
            resources.Identity.DisplayName,
            value => character.Name = value);
        changed |= SetIfDifferent(
            character.SkeletonPath,
            resources.SkeletonPath,
            value => character.SkeletonPath = value);
        changed |= SetIfDifferent(
            character.AtlasPath,
            resources.AtlasPath,
            value => character.AtlasPath = value);
        changed |= SetIfDifferent(
            character.TexturePath,
            resources.PrimaryTexturePath,
            value => character.TexturePath = value);
        if (!character.AdditionalTexturePaths.SequenceEqual(
            resources.AdditionalTexturePaths,
            StringComparer.OrdinalIgnoreCase))
        {
            character.AdditionalTexturePaths =
                resources.AdditionalTexturePaths.ToList();
            changed = true;
        }

        if (character.RequiresStandingMigration)
        {
            character.RequiresStandingMigration = false;
            changed = true;
        }

        return changed;
    }

    private static bool BattleProfilesMatch(
        CharacterBattleConfig? left,
        CharacterBattleConfig? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return ResourceProfilesMatch(left.Aim, right.Aim) &&
            ResourceProfilesMatch(left.Cover, right.Cover) &&
            string.Equals(
                left.Animations.AimIdle,
                right.Animations.AimIdle,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                left.Animations.ToAim,
                right.Animations.ToAim,
                StringComparison.OrdinalIgnoreCase) &&
            BattleLayersMatch(
                left.Animations.AimFireLayers,
                right.Animations.AimFireLayers) &&
            string.Equals(
                left.Animations.CoverIdle,
                right.Animations.CoverIdle,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                left.Animations.ToCover,
                right.Animations.ToCover,
                StringComparison.OrdinalIgnoreCase) &&
            left.Animations.ReloadSequence.SequenceEqual(
                right.Animations.ReloadSequence,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool BattleLayersMatch(
        IReadOnlyList<CharacterBattleLayerConfig>? left,
        IReadOnlyList<CharacterBattleLayerConfig>? right)
    {
        IReadOnlyList<CharacterBattleLayerConfig> normalizedLeft =
            left ?? [];
        IReadOnlyList<CharacterBattleLayerConfig> normalizedRight =
            right ?? [];
        if (normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        for (int index = 0; index < normalizedLeft.Count; index++)
        {
            CharacterBattleLayerConfig leftEffect = normalizedLeft[index];
            CharacterBattleLayerConfig rightEffect =
                normalizedRight[index];
            if (!string.Equals(
                    leftEffect.Animation,
                    rightEffect.Animation,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    CharacterBattleEffectBlendModes.Normalize(
                        leftEffect.Blend),
                    CharacterBattleEffectBlendModes.Normalize(
                        rightEffect.Blend),
                    StringComparison.Ordinal) ||
                leftEffect.Alpha != rightEffect.Alpha ||
                leftEffect.Loop != rightEffect.Loop ||
                !TimelineKeysMatch(
                    leftEffect.IncludeTimelines,
                    rightEffect.IncludeTimelines) ||
                !TimelineKeysMatch(
                    leftEffect.ExcludeTimelines,
                    rightEffect.ExcludeTimelines))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TimelineKeysMatch(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right) =>
        (left ?? []).SequenceEqual(
            right ?? [],
            StringComparer.Ordinal);

    private static bool ResourceProfilesMatch(
        CharacterBattleResourceConfig left,
        CharacterBattleResourceConfig right) =>
        string.Equals(
            left.SkeletonPath,
            right.SkeletonPath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            left.AtlasPath,
            right.AtlasPath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            left.TexturePath,
            right.TexturePath,
            StringComparison.OrdinalIgnoreCase) &&
        left.ExtraTexturePaths.SequenceEqual(
            right.ExtraTexturePaths,
            StringComparer.OrdinalIgnoreCase);

    private static void EnsureStandingResources(
        CharacterResourceFiles resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!string.Equals(
                resources.ResourceType,
                CharacterResourceTypes.Standing,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only standing character resources are supported.");
        }
    }

    private static bool SetIfDifferent(
        string currentValue,
        string newValue,
        Action<string> setter)
    {
        if (string.Equals(
            currentValue,
            newValue,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        setter(newValue);
        return true;
    }

    private static string GetResourceKey(CharacterConfig character) =>
        string.Join(
            "\n",
            new[]
            {
                character.SkeletonPath,
                character.AtlasPath,
                character.TexturePath
            }.Concat(character.AdditionalTexturePaths));

    private sealed record ShowFlight(string ResourceKey, Task<bool> Task);

    private sealed class CharacterBattleRuntimeState
    {
        public string Mode { get; set; } = CharacterDisplayModes.Normal;
        public string BattleState { get; set; } =
            CharacterBattleStates.Cover;
        public bool LongHoldTriggered { get; set; }
        public CancellationTokenSource? HoldCancellation { get; set; }
        public int OperationVersion { get; set; }
    }
}
