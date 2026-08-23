namespace SpinePet.Models;

public sealed class BattleRulesConfig
{
    public const int DefaultRightHoldThresholdMs = 300;

    public string StartupMode { get; set; } = CharacterDisplayModes.Normal;
    public string DefaultBattleState { get; set; } =
        CharacterBattleStates.Cover;
    public int RightHoldThresholdMs { get; set; } =
        DefaultRightHoldThresholdMs;
    public bool ContinuousFireWhileHeld { get; set; } = true;
    public bool ReloadOnRelease { get; set; } = true;
    public bool ShortRightClickOpensPanel { get; set; } = true;
}

public static class CharacterDisplayModes
{
    public const string Normal = "Normal";
    public const string Battle = "Battle";
}

public static class CharacterBattleStates
{
    public const string Cover = "Cover";
    public const string Aim = "Aim";
}
