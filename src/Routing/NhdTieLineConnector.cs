using System;
using System.Collections.Generic;
using System.Linq;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Routing;

public static class NhdTieLineConnector
{
    public static void AddTieLinesForTransmitters(IEnumerable<NhdBaseDevice> transmitters) =>
        AddTieLinesForTransmitterPorts(transmitters);

    /// <summary>
    /// Same fixed 1:1 wiring as the real-device overload above (transmitter's own stream output
    /// port tied to its dedicated router input port), but for <see cref="Mock.MockNhdTx"/> devices.
    /// </summary>
    /// <remarks>
    /// NOT CURRENTLY CALLED (see <see cref="NhdGlobalRouter"/>'s <c>BuildTieLines</c>). Wiring this up
    /// together with <see cref="AddTieLinesForTiles"/> makes every mock Tx walkable as an
    /// intermediate <c>IRoutingMidpoint</c> from any tile's router tie line, which caused
    /// <c>Extensions.MapDestinationsToSources()</c> (run once at Essentials boot) to recursively
    /// fan out across every Tx for every tile x every decoder-as-source combination - an
    /// AccessViolationException-crashing combinatorial explosion in a real system with many tiles.
    /// Left here in case a future, more targeted fix (e.g. excluding tile sinks/mock Tx from
    /// <c>MapDestinationsToSources</c>'s enumeration) makes it safe to re-enable.
    /// </remarks>
    public static void AddTieLinesForTransmitters(IEnumerable<Mock.MockNhdTx> transmitters) =>
        AddTieLinesForTransmitterPorts(transmitters);

    private static void AddTieLinesForTransmitterPorts<T>(IEnumerable<T> transmitters)
        where T : class, IKeyed, IRoutingOutputs
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

    /// <summary>
    /// Ties each multiview decoder's per-tile router output port (see
    /// <see cref="NhdGlobalRouter"/>'s <c>IRoutingSinkWithLayouts</c> discovery) to that tile sink's
    /// own input port - the same fixed 1:1 relationship <see cref="AddTieLinesForReceivers"/> builds
    /// for whole-device receivers, just scoped to an individual tile instead. Covers real (e.g.
    /// Nhd150Rx) and mock (<see cref="Mock.MockNhdRx"/>) decoders alike, since both are discovered
    /// via the shared <see cref="IRoutingSinkWithLayouts"/> interface.
    /// </summary>
    /// <remarks>
    /// NOT CURRENTLY CALLED (see <see cref="NhdGlobalRouter"/>'s <c>BuildTieLines</c>). Every tile
    /// sink is a plain <c>IRoutingInputs</c> device, so <c>Extensions.MapDestinationsToSources()</c>
    /// treats it as a top-level routing sink at boot and tries every registered source against it.
    /// Once a tile has a tie line, that walk recurses into the router, and once the router also has
    /// tie lines from every Tx (see <see cref="AddTieLinesForTransmitters(IEnumerable{Mock.MockNhdTx})"/>),
    /// each of those per-tile checks fans out across every Tx too. With ~90 tiles x ~20 candidate
    /// sources x ~11 Tx this exploded into tens of thousands of recursive calls per boot and crashed
    /// Essentials (AccessViolationException / stack exhaustion). Do not call this without first
    /// addressing that - e.g. by excluding tile sinks and/or Tx midpoints from
    /// <c>MapDestinationsToSources</c>'s enumeration in Essentials Core.
    /// </remarks>
    public static void AddTieLinesForTiles(IEnumerable<IRoutingSinkWithLayouts> layoutParents)
    {
        foreach (var parent in layoutParents)
        {
            foreach (var tileSink in parent.WindowTileSinks.Values)
            {
                if (tileSink is not IKeyed keyedTile || string.IsNullOrEmpty(keyedTile.Key))
                    continue;

                try
                {
                    keyedTile.LogVerbose("Generating tile tie line");

                    var inputPort = tileSink.InputPorts.FirstOrDefault(p => p != null && p.Type != 0);
                    if (inputPort == null)
                        continue;

                    var routerOutputPort = NhdGlobalRouter.Instance.OutputPorts[keyedTile.Key];
                    if (routerOutputPort == null)
                    {
                        keyedTile.LogVerbose("Skipping tile tie line. Router output port missing for tile='{tile}'", keyedTile.Key);
                        continue;
                    }

                    var tieLine = new TieLine(routerOutputPort, inputPort, inputPort.Type);

                    keyedTile.LogVerbose("Adding tile tie line {tieLine}", tieLine);

                    TieLineCollection.Default.Add(tieLine);
                }
                catch (Exception ex)
                {
                    Debug.LogMessage(ex, "Exception adding tile tie line for '{key}'", null, keyedTile.Key);
                }
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
