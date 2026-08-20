using SpinePet.Services;

namespace SpinePet.Tests;

public sealed class CharacterIdentityServiceTests
{
    [Fact]
    public void ResolveUsesMappedCharacterNameAndSkinCode()
    {
        CharacterIdentityService service = new();

        var identity = service.Resolve(
            @"E:\SpinePet\res\Anis\c017_00.skel",
            "Anis");

        Assert.Equal("c017_00", identity.ResourceName);
        Assert.Equal("017", identity.CharacterCode);
        Assert.Equal("00", identity.SkinCode);
        Assert.Equal("Anis Star", identity.DisplayName);
        Assert.Equal("Skin 00", identity.SkinLabel);
    }

    [Fact]
    public void ResolveUsesFolderNameForUnmappedCharacter()
    {
        CharacterIdentityService service = new();

        var identity = service.Resolve(
            @"E:\SpinePet\res\Cinderella_00\c515_00.skel",
            "Cinderella_00");

        Assert.Equal("515", identity.CharacterCode);
        Assert.Equal("00", identity.SkinCode);
        Assert.Equal("Cinderella Crystal Wave", identity.DisplayName);
    }
}
