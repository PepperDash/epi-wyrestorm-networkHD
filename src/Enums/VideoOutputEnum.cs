using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Enums;

public class VideoOutputEnum : Enumeration<VideoOutputEnum>
{
    private VideoOutputEnum(int value, string name)
        : base(value, name)
    {
    }

    public static readonly VideoOutputEnum Stream = new(1, "Stream");
    public static readonly VideoOutputEnum Hdmi = new(2, "Hdmi");
}
