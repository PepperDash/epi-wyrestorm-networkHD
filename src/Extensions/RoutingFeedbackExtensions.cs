using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Extensions;

public static class RoutingFeedbackExtensions
{
    public static RouteSwitchDescriptor UpdateVideoRoute(this IRoutingWithFeedback parent, RoutingInputPort inputPort)
    {
        var outputPort = parent.OutputPorts.FirstOrDefault();

        if (outputPort == null)
        {
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "No output port found", (EssentialsDevice)parent);
            return null;
        }

        var existingRoute = parent.CurrentRoutes.FirstOrDefault(rd => rd.OutputPort.Key == outputPort.Key);

        if (existingRoute == null && inputPort != null)
        {
            var newRoute = new RouteSwitchDescriptor(outputPort, inputPort);
            parent.CurrentRoutes.Add(newRoute);
            return newRoute;
        }

        if (inputPort == null)
        {
            parent.CurrentRoutes.Remove(existingRoute);
            return null;
        }

        existingRoute.InputPort = inputPort;
        return existingRoute;
    }
}
