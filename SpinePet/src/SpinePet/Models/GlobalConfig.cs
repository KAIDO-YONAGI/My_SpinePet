namespace SpinePet.Models;

public sealed class GlobalConfig
{
    public const int DefaultTargetFrameRate = 60;
    public const int PowerSavingTargetFrameRate = 30;
    public const int HighRefreshTargetFrameRate = 120;
    public const double DefaultConfigPanelWidth = 1020;

    public bool AllowRenderDrag { get; set; } = true;

    public int TargetFrameRate { get; set; } = DefaultTargetFrameRate;

    public int LibraryThumbnailScalePercent { get; set; } = 100;

    public static int NormalizeTargetFrameRate(int frameRate) =>
        frameRate switch
        {
            PowerSavingTargetFrameRate => PowerSavingTargetFrameRate,
            HighRefreshTargetFrameRate => HighRefreshTargetFrameRate,
            _ => DefaultTargetFrameRate
        };

    public static int NormalizeLibraryThumbnailScale(int percent) =>
        Math.Clamp(percent, 50, 150);
}
