using PepperDash.Core;

namespace PepperDash.Essentials.Plugin.Extensions;

public static class HdmiInputExtensions
{
    public static void SetHdmi1HdcpCapability(this NhdBaseDevice device, ushort capability)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Setting Hdmi1 HDCP Capability to '{0}'", device, capability);
        // TODO: send command over comms
    }

    public static void SetHdmi2HdcpCapability(this NhdBaseDevice device, ushort capability)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Setting Hdmi2 HDCP Capability to '{0}'", device, capability);
        // TODO: send command over comms
    }
}
