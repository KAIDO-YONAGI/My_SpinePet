using SpinePet.ViewModels;

namespace SpinePet.Tests;

public sealed class CharacterViewModelAccessibilityTests
{
    [Fact]
    public void AccessibilitySummaryDescribesCharacterAndVisibility()
    {
        CharacterViewModel character = CreateCharacter();

        Assert.Equal(
            "Cinderella, Skin 00, Hidden on desktop",
            character.AccessibilitySummary);

        character.IsVisible = true;

        Assert.Equal(
            "Cinderella, Skin 00, Visible on desktop",
            character.AccessibilitySummary);
        Assert.Equal(
            "Hide Cinderella",
            character.VisibilityActionAutomationLabel);
    }

    [Fact]
    public void LoadingStateUpdatesAccessibilityProperties()
    {
        CharacterViewModel character = CreateCharacter();
        List<string?> changedProperties = [];
        character.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);

        character.IsLoading = true;

        Assert.Equal("Loading", character.VisibilityStateLabel);
        Assert.Equal(
            "Loading Cinderella",
            character.VisibilityActionAutomationLabel);
        Assert.Contains(
            nameof(CharacterViewModel.AccessibilitySummary),
            changedProperties);
        Assert.Contains(
            nameof(CharacterViewModel.VisibilityActionAutomationLabel),
            changedProperties);
    }

    [Fact]
    public void SkinOptionsIdentifyCurrentSkinAndRemainSorted()
    {
        CharacterViewModel character = CreateCharacter();

        character.UpdateSkins(
            "01",
            [
                new("c515_02", "515", "02", "Cinderella"),
                new("c515_01", "515", "01", "Cinderella")
            ]);

        Assert.Equal(["01", "02"], character.AvailableSkins
            .Select(option => option.SkinCode));
        Assert.True(character.AvailableSkins[0].IsSelected);
        Assert.False(character.AvailableSkins[1].IsSelected);
    }

    private static CharacterViewModel CreateCharacter()
    {
        CharacterViewModel character = new()
        {
            Id = "c515_00",
            Name = "Cinderella",
            SkinLabel = "Skin 00"
        };
        return character;
    }
}
