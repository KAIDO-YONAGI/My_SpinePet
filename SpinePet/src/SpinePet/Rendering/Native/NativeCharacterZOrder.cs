namespace SpinePet.Rendering.Native;

internal sealed class NativeCharacterZOrder
{
    private readonly List<string> _bottomToTop = [];

    public IEnumerable<string> TopToBottom
    {
        get
        {
            for (int index = _bottomToTop.Count - 1;
                 index >= 0;
                 index--)
            {
                yield return _bottomToTop[index];
            }
        }
    }

    public void MoveToTop(string characterId)
    {
        _bottomToTop.Remove(characterId);
        _bottomToTop.Add(characterId);
    }

    public void Remove(string characterId)
    {
        _bottomToTop.Remove(characterId);
    }

    public void Clear()
    {
        _bottomToTop.Clear();
    }
}
