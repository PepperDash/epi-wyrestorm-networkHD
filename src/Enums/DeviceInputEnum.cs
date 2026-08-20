using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Enums;

/// <summary>
/// Reserved for future model-specific generic input mapping.
/// Intentionally retained even when not referenced by current device set.
/// </summary>
public class DeviceInputEnum : Enumeration<DeviceInputEnum>
{
    private DeviceInputEnum(int value, string name) : base(value, name)
    {
    }

    public static readonly DeviceInputEnum Stream = new(0, "Stream");
    public static readonly DeviceInputEnum Hdmi1 = new(1, "Hdmi1");
    public static readonly DeviceInputEnum Hdmi2 = new(2, "Hdmi2");
    public static readonly DeviceInputEnum AnalogAudio = new(3, "AnalogAudio");
    public static readonly DeviceInputEnum PrimaryAudio = new(4, "PrimaryAudio");
    public static readonly DeviceInputEnum Automatic = new(98, "Automatic");
    public static readonly DeviceInputEnum NoSwitch = new(99, "NoSwitch");
}
