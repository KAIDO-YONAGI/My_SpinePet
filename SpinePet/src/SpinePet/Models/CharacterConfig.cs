using System.Text.Json.Serialization;

namespace SpinePet.Models;

public sealed class CharacterConfig
{
    public const double DefaultScale = 0.2;
    public const double DefaultAnimationSpeed = 1.0;
    public const double DefaultScaleBasePercent = 100;
    public const double DefaultScaleMultiplier = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("SkelPath")]
    public string SkeletonPath { get; set; } = string.Empty;
    public string AtlasPath { get; set; } = string.Empty;
    public string TexturePath { get; set; } = string.Empty;

    // Multi-page atlas characters can reference additional PNG files.
    [JsonPropertyName("ExtraTexturePaths")]
    public List<string> AdditionalTexturePaths { get; set; } = new();

    // Read old state-based configurations once, then omit this retired key
    // from every newly saved configuration.
    [JsonPropertyName("ResourceType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyResourceType { get; set; }

    [JsonIgnore]
    public bool RequiresStandingMigration { get; set; }

    public double PositionX { get; set; } = 200;
    public double PositionY { get; set; } = 200;
    public double Scale { get; set; } = DefaultScale;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ScaleBasePercent { get; set; } = DefaultScaleBasePercent;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ScaleMultiplier { get; set; } = DefaultScaleMultiplier;

    // Keep the existing JSON key so installed configurations migrate without data loss.
    [JsonPropertyName("CurrentAnimation")]
    public string ConfiguredAnimation { get; set; } = string.Empty;

    public double AnimationSpeed { get; set; } = DefaultAnimationSpeed;
    public bool Visible { get; set; }
}
