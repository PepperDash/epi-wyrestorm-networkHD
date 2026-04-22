using PepperDash.Core;

namespace PepperDash.Essentials.Plugin.Extensions;

public static class HdmiOutputExtensions
{
    public static void SetVideoAspectRatioMode(this NhdBaseDevice device, ushort mode)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Setting Video Aspect Ratio to '{0}'", device, mode);
        // TODO: send command over comms
    }

    public static void SetVideowallMode(this NhdBaseDevice device, ushort value)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Setting videowall mode to: '{0}'", device, value);
        // TODO: send command over comms
    }
}
