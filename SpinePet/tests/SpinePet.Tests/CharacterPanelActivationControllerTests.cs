using SpinePet.ViewModels;
using SpinePet.Views;

namespace SpinePet.Tests;

public sealed class CharacterPanelActivationControllerTests
{
    [Fact]
    public void RightClickOpensPanelThenSelectsAndRevealsCharacter()
    {
        CharacterViewModel character = CreateCharacter("c001");
        List<string> calls = [];
        CharacterPanelActivationController controller = CreateController(
            character,
            isConfigMode: false,
            isInCurrentView: true,
            calls);

        controller.HandleRightClick("c001");

        Assert.Equal(
            ["switch", "select:c001"],
            calls);
    }

    [Fact]
    public void RightClickClearsSearchBeforeOpeningAndRevealingFilteredCharacter()
    {
        CharacterViewModel character = CreateCharacter("c002");
        List<string> calls = [];
        CharacterPanelActivationController controller = CreateController(
            character,
            isConfigMode: false,
            isInCurrentView: false,
            calls);

        controller.HandleRightClick("c002");

        Assert.Equal(
            ["clear-search", "switch", "select:c002"],
            calls);
    }

    [Fact]
    public void RightClickWhilePanelIsOpenClosesWithoutSelecting()
    {
        CharacterViewModel character = CreateCharacter("c003");
        List<string> calls = [];
        CharacterPanelActivationController controller = CreateController(
            character,
            isConfigMode: true,
            isInCurrentView: true,
            calls);

        controller.HandleRightClick("c003");

        Assert.Equal(["exit"], calls);
    }

    [Fact]
    public void RightClickForUnknownCharacterOnlyOpensPanel()
    {
        List<string> calls = [];
        CharacterPanelActivationController controller = CreateController(
            character: null,
            isConfigMode: false,
            isInCurrentView: false,
            calls);

        controller.HandleRightClick("missing");

        Assert.Equal(["switch"], calls);
    }

    private static CharacterPanelActivationController CreateController(
        CharacterViewModel? character,
        bool isConfigMode,
        bool isInCurrentView,
        List<string> calls)
    {
        return new CharacterPanelActivationController(
            isDisposed: () => false,
            isConfigMode: () => isConfigMode,
            exitConfiguration: () => calls.Add("exit"),
            findCharacter: _ => character,
            isInCurrentView: _ => isInCurrentView,
            clearSearch: () => calls.Add("clear-search"),
            switchToConfigMode: () => calls.Add("switch"),
            selectAndReveal: selected => calls.Add(
                $"select:{selected.Id}"));
    }

    private static CharacterViewModel CreateCharacter(string id) =>
        new()
        {
            Id = id,
            Name = id
        };
}
