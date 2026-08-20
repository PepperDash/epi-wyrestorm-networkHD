using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Enums;

/// <summary>
/// Reserved for future model-specific video-output mapping.
/// Intentionally retained even when not referenced by current device set.
/// </summary>
public class VideoOutputEnum : Enumeration<VideoOutputEnum>
{
    private VideoOutputEnum(int value, string name)
        : base(value, name)
    {
    }

    public static readonly VideoOutputEnum Stream = new(1, "Stream");
    public static readonly VideoOutputEnum Hdmi = new(2, "Hdmi");
}
