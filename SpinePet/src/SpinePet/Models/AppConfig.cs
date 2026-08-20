using System.Text.Json.Serialization;

namespace SpinePet.Models;

public sealed class AppConfig
{
    public const string CurrentVersion = "1.5";

    public string Version { get; set; } = CurrentVersion;
    public GlobalConfig Global { get; set; } = new();
    public List<CharacterConfig> Characters { get; set; } = new();

    [JsonIgnore]
    internal bool RequiresRewrite { get; set; }
}
