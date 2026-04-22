using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Routing;

public static class NhdTieLineConnector
{
    public static void AddTieLinesForTransmitters(IEnumerable<NhdBaseDevice> transmitters)
    {
        foreach (var tx in transmitters)
        {
            try
            {
                tx.LogVerbose("Generating TX tie line");

                var outputPort = tx.OutputPorts[NhdPortKeys.Stream]
                    ?? throw new NullReferenceException($"No '{NhdPortKeys.Stream}' output port on TX '{tx.Key}'");

                var routerInputPort = NhdGlobalRouter.Instance.InputPorts[tx.Key]
                    ?? throw new NullReferenceException($"No router input port for TX '{tx.Key}'");

                var tieLine = new TieLine(outputPort, routerInputPort, eRoutingSignalType.AudioVideo);

                tx.LogVerbose("Adding TX tie line {tieLine}", tieLine);

                TieLineCollection.Default.Add(tieLine);
            }
            catch (Exception ex)
            {
                Debug.LogMessage(ex, "Exception adding TX tie line for '{key}'", null, tx.Key);
            }
        }
    }

    public static void AddTieLinesForReceivers(IEnumerable<NhdBaseDevice> receivers)
    {
        foreach (var rx in receivers)
        {
            try
            {
                rx.LogVerbose("Generating RX tie line");

                var inputPort = rx.InputPorts[NhdPortKeys.Stream]
                    ?? throw new NullReferenceException($"No '{NhdPortKeys.Stream}' input port on RX '{rx.Key}'");

                var routerOutputPort = NhdGlobalRouter.Instance.OutputPorts[rx.Key]
                    ?? throw new NullReferenceException($"No router output port for RX '{rx.Key}'");

                var tieLine = new TieLine(routerOutputPort, inputPort, eRoutingSignalType.AudioVideo);

                rx.LogVerbose("Adding RX tie line {tieLine}", tieLine);

                TieLineCollection.Default.Add(tieLine);
            }
            catch (Exception ex)
            {
                Debug.LogMessage(ex, "Exception adding RX tie line for '{key}'", null, rx.Key);
            }
        }
    }
}
