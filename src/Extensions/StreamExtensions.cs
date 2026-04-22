using PepperDash.Core;

namespace PepperDash.Essentials.Plugin.Extensions;

public static class StreamExtensions
{
    public static void ClearStream(this NhdBaseDevice device)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Clearing stream", device);
        // TODO: send command over comms
    }

    public static void SetStreamUrl(this NhdBaseDevice device, string url)
    {
        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Setting stream: '{0}'", device, url);
        // TODO: send command over comms
    }

    public static void RouteStream(this NhdBaseDevice device, NhdBaseDevice tx)
    {
        if (tx == null)
        {
            device.ClearStream();
            return;
        }

        Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "Routing stream from '{txName}'", device, tx.Name);
        // TODO: get stream URL from tx and send command over comms
    }
}
