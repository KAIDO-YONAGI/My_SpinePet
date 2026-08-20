namespace SpinePet.Models;

public sealed class GlobalConfig
{
    public const int DefaultTargetFrameRate = 60;
    public const int PowerSavingTargetFrameRate = 30;
    public const int HighRefreshTargetFrameRate = 120;

    public bool AllowRenderDrag { get; set; } = true;

    public int TargetFrameRate { get; set; } = DefaultTargetFrameRate;

    public static int NormalizeTargetFrameRate(int frameRate) =>
        frameRate switch
        {
            PowerSavingTargetFrameRate => PowerSavingTargetFrameRate,
            HighRefreshTargetFrameRate => HighRefreshTargetFrameRate,
            _ => DefaultTargetFrameRate
        };
}
