using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Enums;

/// <summary>
/// Reserved for future model-specific audio-output mapping.
/// Intentionally retained even when not referenced by current device set.
/// </summary>
public class AudioOutputEnum : Enumeration<AudioOutputEnum>
{
    private AudioOutputEnum(int value, string name)
        : base(value, name)
    {
    }

    public static readonly AudioOutputEnum Stream = new(1, "Stream");
    public static readonly AudioOutputEnum Hdmi = new(2, "Hdmi");
    public static readonly AudioOutputEnum Analog = new(3, "Analog");
}
