using SpinePet.Models;
using SpinePet.Rendering;

namespace SpinePet.Services;

/// <summary>
/// Coordinates asynchronous character show operations with single-flight
/// semantics per character and resource key. Hide-all style orchestration
/// and battle runtime resets stay with the manager.
/// </summary>
internal sealed class CharacterShowCoordinator
{
    private readonly ICharacterRenderHost _renderHost;
    private readonly Func<bool> _isClosed;
    private readonly Func<bool> _isConfigMode;
    private readonly Func<CharacterConfig, bool> _isTracked;
    private readonly Action _persist;
    private readonly object _showSync = new();
    private readonly Dictionary<string, ShowFlight> _showFlights =
        new(StringComparer.Ordinal);

    public CharacterShowCoordinator(
        ICharacterRenderHost renderHost,
        Func<bool> isClosed,
        Func<bool> isConfigMode,
        Func<CharacterConfig, bool> isTracked,
        Action persist)
    {
        _renderHost = renderHost;
        _isClosed = isClosed;
        _isConfigMode = isConfigMode;
        _isTracked = isTracked;
        _persist = persist;
    }

    public Task ShowAsync(CharacterConfig character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (IsShownAndSettled(character))
        {
            return Task.CompletedTask;
        }

        (Task<bool> task, bool ownsSideEffects) =
            GetOrStartShowFlight(character);
        return CompleteShowAsync(task, ownsSideEffects);
    }

    /// <summary>
    /// Shows a character without the public entry's settled-state guard.
    /// Used by resource switches that must reload even when marked visible.
    /// </summary>
    public Task<bool> EnsureShownAsync(CharacterConfig character) =>
        EnsureCharacterShownAsync(character);

    public async Task ShowAllAsync(
        IReadOnlyList<CharacterConfig> characters)
    {
        List<Task<bool>> tasks = [];
        foreach (CharacterConfig character in characters)
        {
            if (IsShownAndSettled(character))
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
        if (changes.Any(changed => changed) && !_isClosed())
        {
            _persist();
        }
    }

    private bool IsShownAndSettled(CharacterConfig character) =>
        _isClosed() ||
        (character.Visible &&
         _renderHost.IsCharacterVisible(character.Id) &&
         !_renderHost.IsCharacterLoading(character.Id));

    private async Task CompleteShowAsync(
        Task<bool> task,
        bool ownsSideEffects)
    {
        bool changed = await task;
        if (ownsSideEffects && changed && !_isClosed())
        {
            _persist();
        }
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
        if (_isClosed() ||
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
                _isConfigMode(),
                character.AnimationSpeed);
            if (_isClosed())
            {
                return false;
            }

            if (!_isTracked(character))
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
            if (!_isClosed() && character.Visible)
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
}
