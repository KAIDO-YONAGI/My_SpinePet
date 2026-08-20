using SpinePet.ViewModels;

namespace SpinePet.Tests;

public sealed class CharacterSearchMatcherTests
{
    private readonly CharacterViewModel _character = CreateCharacter();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptySearchMatchesCharacter(string? searchText)
    {
        Assert.True(CharacterSearchMatcher.Matches(_character, searchText));
    }

    [Theory]
    [InlineData("cinder")]
    [InlineData("CINDERELLA")]
    [InlineData("Skin 00")]
    [InlineData("Skin 01")]
    [InlineData("cinderella skin 01")]
    public void SearchMatchesNameAndMetadata(string searchText)
    {
        Assert.True(CharacterSearchMatcher.Matches(_character, searchText));
    }

    [Theory]
    [InlineData("anis")]
    [InlineData("skin 02")]
    [InlineData("cover")]
    public void SearchRejectsNonMatchingCharacter(string searchText)
    {
        Assert.False(CharacterSearchMatcher.Matches(_character, searchText));
    }

    [Fact]
    public void SearchHandlesMissingCharacterMetadata()
    {
        _character.Name = null!;
        _character.SkinLabel = null!;

        Assert.False(CharacterSearchMatcher.Matches(
            _character,
            "missing"));
        Assert.False(CharacterSearchMatcher.Matches(
            _character,
            "standing"));
    }

    private static CharacterViewModel CreateCharacter()
    {
        CharacterViewModel character = new()
        {
            Id = "c515_00",
            Name = "Cinderella",
            SkinLabel = "Skin 00"
        };
        character.UpdateSkins(
            "00",
            [
                new(
                    "c515_00",
                    "515",
                    "00",
                    "Cinderella"),
                new(
                    "c515_01",
                    "515",
                    "01",
                    "Cinderella")
            ]);
        return character;
    }
}
