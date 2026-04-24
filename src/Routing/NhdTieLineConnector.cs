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
                tx.LogVerbose("Generating TX tie lines");

                foreach (var outputPort in tx.OutputPorts)
                {
                    if (outputPort == null || outputPort.Type == 0)
                        continue;

                    var routerPortKey = NhdGlobalRouter.GetRouterInputPortKeyForEndpointPort(tx.Key, outputPort.Key);
                    var routerInputPort = NhdGlobalRouter.Instance.InputPorts[routerPortKey];
                    if (routerInputPort == null)
                    {
                        tx.LogVerbose("Skipping TX tie line. Router input port missing for endpoint='{endpoint}', port='{port}'", tx.Key, outputPort.Key);
                        continue;
                    }

                    var tieLine = new TieLine(outputPort, routerInputPort, outputPort.Type);

                    tx.LogVerbose("Adding TX tie line {tieLine}", tieLine);

                    TieLineCollection.Default.Add(tieLine);
                }
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
                rx.LogVerbose("Generating RX tie lines");

                foreach (var inputPort in rx.InputPorts)
                {
                    if (inputPort == null || inputPort.Type == 0)
                        continue;

                    var routerPortKey = NhdGlobalRouter.GetRouterOutputPortKeyForEndpointPort(rx.Key, inputPort.Key);
                    var routerOutputPort = NhdGlobalRouter.Instance.OutputPorts[routerPortKey];
                    if (routerOutputPort == null)
                    {
                        rx.LogVerbose("Skipping RX tie line. Router output port missing for endpoint='{endpoint}', port='{port}'", rx.Key, inputPort.Key);
                        continue;
                    }

                    var tieLine = new TieLine(routerOutputPort, inputPort, inputPort.Type);

                    rx.LogVerbose("Adding RX tie line {tieLine}", tieLine);

                    TieLineCollection.Default.Add(tieLine);
                }
            }
            catch (Exception ex)
            {
                Debug.LogMessage(ex, "Exception adding RX tie line for '{key}'", null, rx.Key);
            }
        }
    }
}
