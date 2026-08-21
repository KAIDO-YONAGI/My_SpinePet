using SpinePet.ViewModels;

namespace SpinePet.Views;

internal sealed class CharacterPanelActivationController
{
    private readonly Func<bool> _isDisposed;
    private readonly Func<bool> _isConfigMode;
    private readonly Action _exitConfiguration;
    private readonly Func<string, CharacterViewModel?> _findCharacter;
    private readonly Func<CharacterViewModel, bool> _isInCurrentView;
    private readonly Action _clearSearch;
    private readonly Action _switchToConfigMode;
    private readonly Action<CharacterViewModel> _selectAndReveal;

    public CharacterPanelActivationController(
        Func<bool> isDisposed,
        Func<bool> isConfigMode,
        Action exitConfiguration,
        Func<string, CharacterViewModel?> findCharacter,
        Func<CharacterViewModel, bool> isInCurrentView,
        Action clearSearch,
        Action switchToConfigMode,
        Action<CharacterViewModel> selectAndReveal)
    {
        _isDisposed = isDisposed;
        _isConfigMode = isConfigMode;
        _exitConfiguration = exitConfiguration;
        _findCharacter = findCharacter;
        _isInCurrentView = isInCurrentView;
        _clearSearch = clearSearch;
        _switchToConfigMode = switchToConfigMode;
        _selectAndReveal = selectAndReveal;
    }

    public void HandleRightClick(string characterId)
    {
        if (_isDisposed())
        {
            return;
        }

        if (_isConfigMode())
        {
            _exitConfiguration();
            return;
        }

        CharacterViewModel? character = _findCharacter(characterId);
        if (character == null)
        {
            _switchToConfigMode();
            return;
        }

        if (!_isInCurrentView(character))
        {
            _clearSearch();
        }

        _switchToConfigMode();
        _selectAndReveal(character);
    }
}
