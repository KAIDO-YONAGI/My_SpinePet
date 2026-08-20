namespace SpinePet.ViewModels;

internal static class CharacterSearchMatcher
{
    public static bool Matches(
        CharacterViewModel character,
        string? searchText)
    {
        string[] terms = (searchText ?? string.Empty)
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return true;
        }

        return terms.All(term =>
            Contains(character.Name, term) ||
            Contains(character.SkinLabel, term) ||
            Contains(character.AvailableSkinSearchText, term));
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(
            term,
            StringComparison.CurrentCultureIgnoreCase) == true;
}
