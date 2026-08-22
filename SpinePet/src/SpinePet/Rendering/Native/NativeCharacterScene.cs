using System.Diagnostics.CodeAnalysis;
using SpinePet.Models;

namespace SpinePet.Rendering.Native;

// Owns the live character set and its render order. Rendering policy and
// persistence stay outside this scene so state transitions have one owner.
internal sealed class NativeCharacterScene
{
    private readonly Dictionary<string, NativeCharacterState> _states =
        new(StringComparer.Ordinal);
    private readonly NativeCharacterZOrder _zOrder = new();

    public IEnumerable<NativeCharacterState> States => _states.Values;

    public IEnumerable<string> TopToBottom => _zOrder.TopToBottom;

    public bool TryGet(
        string characterId,
        [NotNullWhen(true)] out NativeCharacterState? state) =>
        _states.TryGetValue(characterId, out state);

    public NativeCharacterState GetOrCreate(
        CharacterConfig character,
        double defaultScale,
        double minimumScale,
        double maximumScale)
    {
        if (_states.TryGetValue(character.Id, out NativeCharacterState? existing))
            return existing;

        NativeCharacterState state = new()
        {
            Config = character,
            CurrentScale = Math.Clamp(
                character.Scale > 0 ? character.Scale : defaultScale,
                minimumScale,
                maximumScale),
            MaxScale = maximumScale
        };
        _states[character.Id] = state;
        _zOrder.MoveToTop(character.Id);
        return state;
    }

    public bool Remove(
        string characterId,
        [NotNullWhen(true)] out NativeCharacterState? state)
    {
        bool removed = _states.Remove(characterId, out state);
        if (removed)
            _zOrder.Remove(characterId);
        return removed;
    }

    public void MoveToTop(string characterId) =>
        _zOrder.MoveToTop(characterId);

    public void Clear()
    {
        foreach (NativeCharacterState state in _states.Values)
            state.Dispose();

        _states.Clear();
        _zOrder.Clear();
    }
}
