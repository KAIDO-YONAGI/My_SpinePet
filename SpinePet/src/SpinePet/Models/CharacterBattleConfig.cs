using System.IO;
using System.Text.Json.Serialization;

namespace SpinePet.Models;

public sealed class CharacterBattleConfig
{
    public CharacterBattleResourceConfig Aim { get; set; } = new();
    public CharacterBattleResourceConfig Cover { get; set; } = new();
    public CharacterBattleAnimationsConfig Animations { get; set; } = new();
}

public sealed class CharacterBattleResourceConfig
{
    [JsonPropertyName("SkelPath")]
    public string SkeletonPath { get; set; } = string.Empty;
    public string AtlasPath { get; set; } = string.Empty;
    public string TexturePath { get; set; } = string.Empty;
    public List<string> ExtraTexturePaths { get; set; } = [];

    public bool Exists() =>
        File.Exists(SkeletonPath) &&
        File.Exists(AtlasPath) &&
        File.Exists(TexturePath) &&
        ExtraTexturePaths.All(File.Exists);

    public CharacterConfig CreateRenderConfig(CharacterConfig source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            SkeletonPath = SkeletonPath,
            AtlasPath = AtlasPath,
            TexturePath = TexturePath,
            AdditionalTexturePaths = ExtraTexturePaths.ToList(),
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            Scale = source.Scale,
            ScaleBasePercent = source.ScaleBasePercent,
            ScaleMultiplier = source.ScaleMultiplier,
            AnimationSpeed = source.AnimationSpeed,
            Visible = source.Visible
        };
}

public sealed class CharacterBattleAnimationsConfig
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AimIdle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToAim { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AimFire { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AimFireEffects { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CharacterBattleEffectConfig>? BattleEffects { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoverIdle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToCover { get; set; }
    public List<string> ReloadSequence { get; set; } = [];
}

public sealed class CharacterBattleEffectConfig
{
    public string Animation { get; set; } = string.Empty;
    public string Blend { get; set; } =
        CharacterBattleEffectBlendModes.Replace;
    public float Alpha { get; set; } = 1;
    public bool Loop { get; set; } = true;
}

public static class CharacterBattleEffectBlendModes
{
    public const string Replace = "Replace";
    public const string Add = "Add";

    public static string Normalize(string? value) =>
        string.Equals(value, Add, StringComparison.OrdinalIgnoreCase)
            ? Add
            : Replace;
}
