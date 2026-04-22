using PepperDash.Core;

namespace PepperDash.Essentials.Plugin.Extensions;

public static class AudioInputExtensions
{
    /// <summary>
    /// Sets the audio input based on a ushort value
    /// </summary>
    /// <param name="device">The audio input device</param>
    /// <param name="input">The input number</param>
    public static void SetAudioInput(this NhdBaseDevice device, ushort input)
    {
        switch (input)
        {
            case 1:
                device.SetAudioToHdmiInput1();
                break;
            case 2:
                device.SetAudioToHdmiInput2();
                break;
            case 3:
                device.SetAudioToInputAnalog();
                break;
            case 4:
                device.SetAudioToPrimaryStreamAudio();
                break;
            case 99:
                device.SetAudioToInputAutomatic();
                break;
        }
    }

    public static void SetAudioToHdmiInput1(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Audio Input to: 'Hdmi1'", device);
        // TODO: send command over comms
    }

    public static void SetAudioToHdmiInput2(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Audio Input to: 'Hdmi2'", device);
        // TODO: send command over comms
    }

    public static void SetAudioToInputAnalog(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Audio Input to: 'Analog'", device);
        // TODO: send command over comms
    }

    public static void SetAudioToPrimaryStreamAudio(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Audio Input to: 'PrimaryStream'", device);
        // TODO: send command over comms
    }

    public static void SetAudioToInputAutomatic(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Switching Audio Input to: 'Automatic'", device);
        // TODO: send command over comms
    }
}
