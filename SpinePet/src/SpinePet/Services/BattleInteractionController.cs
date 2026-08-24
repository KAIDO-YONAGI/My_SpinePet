using SpinePet.Infrastructure;
using SpinePet.Models;
using SpinePet.Rendering;

namespace SpinePet.Services;

/// <summary>
/// Owns per-character Normal/Battle runtime state and the right-button
/// hold/release interaction. The manager forwards renderer events and
/// public calls into this controller; persistence stays outside.
/// </summary>
internal sealed class BattleInteractionController
{
    private readonly ICharacterRenderHost _renderHost;
    private readonly Func<CharacterConfig, Task> _ensureVisible;
    private readonly Func<BattleRulesConfig> _getBattleRules;
    private readonly Func<string, CharacterConfig?> _findCharacter;
    private readonly Dictionary<string, CharacterBattleRuntimeState>
        _battleRuntime = new(StringComparer.Ordinal);

    public BattleInteractionController(
        ICharacterRenderHost renderHost,
        Func<CharacterConfig, Task> ensureVisible,
        Func<BattleRulesConfig> getBattleRules,
        Func<string, CharacterConfig?> findCharacter)
    {
        _renderHost = renderHost;
        _ensureVisible = ensureVisible;
        _getBattleRules = getBattleRules;
        _findCharacter = findCharacter;
    }

    public event Action<string, string, string>? CharacterBattleStateChanged;

    public event Action<string>? CharacterRightClicked;

    public string GetDisplayMode(string characterId) =>
        GetBattleRuntime(characterId).Mode;

    public string GetBattleState(string characterId) =>
        GetBattleRuntime(characterId).BattleState;

    public async Task<bool> SetDisplayModeAsync(
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

    public async Task<bool> SetBattleStateAsync(
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

    public void HandleRightPressed(string characterId)
    {
        CharacterConfig? character = _findCharacter(characterId);
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

    public void HandleRightReleased(string characterId)
    {
        CharacterConfig? character = _findCharacter(characterId);
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

        if (_getBattleRules().ShortRightClickOpensPanel)
        {
            CharacterRightClicked?.Invoke(characterId);
        }
    }

    public void ResetRuntime(string characterId)
    {
        if (_battleRuntime.Remove(
                characterId,
                out CharacterBattleRuntimeState? runtime))
        {
            CancelBattleHold(runtime);
        }
    }

    public void Close()
    {
        foreach (CharacterBattleRuntimeState runtime in _battleRuntime.Values)
        {
            CancelBattleHold(runtime);
        }
        _battleRuntime.Clear();
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
                _getBattleRules().RightHoldThresholdMs,
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
                    _getBattleRules().ContinuousFireWhileHeld,
                battleLayers: animations.AimFireLayers);
            NotifyBattleState(character.Id, runtime);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Write(
                nameof(BattleInteractionController),
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
            if (_getBattleRules().ReloadOnRelease)
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
                nameof(BattleInteractionController),
                $"battle-cover-failed id={character.Id} " +
                $"message={exception.Message}");
        }
    }

    private async Task EnsureBattleReadyAsync(CharacterConfig character)
    {
        if (!character.Visible)
        {
            await _ensureVisible(character);
        }
        await _renderHost.PreloadBattleResourcesAsync(character);
    }

    private CharacterBattleRuntimeState GetBattleRuntime(
        string characterId)
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

    private static void CancelBattleHold(
        CharacterBattleRuntimeState runtime)
    {
        runtime.HoldCancellation?.Cancel();
        runtime.HoldCancellation?.Dispose();
        runtime.HoldCancellation = null;
        runtime.LongHoldTriggered = false;
    }

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
