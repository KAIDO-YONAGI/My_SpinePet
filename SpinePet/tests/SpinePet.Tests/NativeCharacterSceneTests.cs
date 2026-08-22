using SpinePet.Models;
using SpinePet.Rendering.Native;

namespace SpinePet.Tests;

public sealed class NativeCharacterSceneTests
{
    [Fact]
    public void RepeatedGetOrCreateKeepsSingleStateAndZOrderEntry()
    {
        NativeCharacterScene scene = new();
        CharacterConfig character = new()
        {
            Id = "character-1",
            Scale = 0.4
        };

        NativeCharacterState first = scene.GetOrCreate(
            character,
            defaultScale: 0.2,
            minimumScale: 0.05,
            maximumScale: 2);
        NativeCharacterState second = scene.GetOrCreate(
            character,
            defaultScale: 0.2,
            minimumScale: 0.05,
            maximumScale: 2);

        Assert.Same(first, second);
        Assert.Single(scene.States);
        Assert.Equal(["character-1"], scene.TopToBottom);
    }

    [Fact]
    public void RepeatedRemoveHasNoAdditionalEffect()
    {
        NativeCharacterScene scene = new();
        CharacterConfig character = new()
        {
            Id = "character-1"
        };
        scene.GetOrCreate(
            character,
            defaultScale: 0.2,
            minimumScale: 0.05,
            maximumScale: 2);

        Assert.True(scene.Remove(character.Id, out _));
        Assert.False(scene.Remove(character.Id, out _));
        Assert.Empty(scene.States);
        Assert.Empty(scene.TopToBottom);
    }
}
