using PepperDash.Core;

namespace PepperDash.Essentials.Plugin.Extensions;

public static class VideoInputExtensions
{
    public static void SetVideoInput(this NhdBaseDevice device, ushort input)
    {
        switch (input)
        {
            case 1:
                device.SetVideoToHdmiInput1();
                break;
            case 2:
                device.SetVideoToHdmiInput2();
                break;
            case 3:
                device.SetVideoToStream();
                break;
            case 99:
                device.SetVideoToInputNone();
                break;
        }
    }

    public static void SetVideoToHdmiInput1(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Video Input to: 'Hdmi1'", device);
        // TODO: send command over comms
    }

    public static void SetVideoToHdmiInput2(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Video Input to: 'Hdmi2'", device);
        // TODO: send command over comms
    }

    public static void SetVideoToStream(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Video Input to: 'Stream'", device);
        // TODO: send command over comms
    }

    public static void SetVideoToInputNone(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Video Input to: 'None'", device);
        // TODO: send command over comms
    }
}
