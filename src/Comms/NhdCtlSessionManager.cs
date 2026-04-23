using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Comms
{
    public class NhdCtlSessionManager
    {
        private static readonly Regex AliasLineRegex = new Regex(
            "^(?<hostname>\\S+?)(?:'s|’s)\\s+alias\\s+is\\s+(?<alias>\\S+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EndpointNotifyRegex = new Regex(
            "^notify\\s+endpoint\\s+(?<state>[+-])\\s+(?<reference>\\S+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MviewInformationLineRegex = new Regex(
            "^(?<reference>\\S+)\\s+(?<mode>tile|overlay)\\s*(?<tiles>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TileDescriptorRegex = new Regex(
            "^(?<source>[^:\\s]+):(?<x>\\d+)_(?<y>\\d+)_(?<w>\\d+)_(?<h>\\d+):(?<scale>fit|stretch)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MsceneActiveResponseRegex = new Regex(
            "^mscene\\s+active\\s+(?<reference>\\S+)\\s+(?<layout>\\S+)\\s+(?<result>success|failure)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly TimeSpan MultiviewStateFreshness = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MultiviewRefreshThrottle = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MsceneListRefreshThrottle = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PendingTileRouteExpiry = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FullscreenRouteClearBypassWindow = TimeSpan.FromSeconds(10);

        private sealed class PendingMultiviewTileRoute
        {
            public string TxEndpointKey { get; set; }
            public string LayoutName { get; set; }
            public int TileReference { get; set; }
            public string RequestedByKey { get; set; }
            public DateTime QueuedUtc { get; set; }
        }

        private sealed class PendingLayoutProbe
        {
            public string EndpointKey { get; set; }
            public Queue<string> RemainingLayouts { get; set; }
            public string ActiveLayout { get; set; }
            public int AttemptedCount { get; set; }
            public int LearnedCount { get; set; }
            public string RequestedByKey { get; set; }
        }

        private sealed class PendingMultiviewFullscreen
        {
            public string PreviousLayoutName { get; set; }
            public int SourceTileReference { get; set; }
            public string SourceReference { get; set; }
            public DateTime QueuedUtc { get; set; }
        }

        private sealed class MultiviewFullscreenReturnState
        {
            public string PreviousLayoutName { get; set; }
            public DateTime CapturedUtc { get; set; }
        }

        private sealed class RecentFullscreenRoute
        {
            public string TxReference { get; set; }
            public string LayoutName { get; set; }
            public int TileReference { get; set; }
            public DateTime RequestedUtc { get; set; }
        }

        private readonly NhdCtlPro _ctl;
        private readonly CommunicationGather _gather;
        private readonly Dictionary<string, PendingMultiviewTileRoute> _pendingTileRoutes = new Dictionary<string, PendingMultiviewTileRoute>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastMsceneListRequestUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pendingLayoutGeometryCapture = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingLayoutProbe> _pendingLayoutProbes = new Dictionary<string, PendingLayoutProbe>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _startupProbeCompleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingMultiviewFullscreen> _pendingFullscreen = new Dictionary<string, PendingMultiviewFullscreen>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pendingFullscreenReturns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MultiviewFullscreenReturnState> _fullscreenReturnStates = new Dictionary<string, MultiviewFullscreenReturnState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RecentFullscreenRoute> _recentFullscreenRoutes = new Dictionary<string, RecentFullscreenRoute>(StringComparer.OrdinalIgnoreCase);

        private bool _isParsingMviewInformation;
        private bool _isParsingMsceneList;
        private NhdBaseDevice _pendingMviewEndpoint;
        private NhdMultiStreamMode _pendingMviewMode;
        private readonly List<NhdMultiviewTileState> _pendingMviewTiles = new List<NhdMultiviewTileState>();

        public NhdCtlSessionManager(NhdCtlPro ctl)
        {
            _ctl = ctl;
            _gather = new CommunicationGather(ctl.Comms, "\r\n");
            _gather.LineReceived += HandleLineReceived;
        }

        public void Start()
        {
            // Preferred endpoint references are aliases, but some replies still return hostnames.
            _startupProbeCompleted.Clear();
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Starting NHD session manager; enabling alias mode and requesting identity list", _ctl);
            NhdApiCommandSender.TrySend(_ctl, "config set session alias on");
            NhdApiCommandSender.TrySend(_ctl, "config get name");
            NhdApiCommandSender.TrySend(_ctl, "config get devicelist");
            NhdApiCommandSender.TrySend(_ctl, "mscene get");
            NhdApiCommandSender.TrySend(_ctl, "mview get");
        }

        public bool TryRouteMultiviewTile(IKeyed requestedBy, NhdBaseDevice txEndpoint, NhdBaseDevice rxEndpoint, string layoutName, int tileReference)
        {
            var source = requestedBy ?? _ctl;

            if (txEndpoint == null || rxEndpoint == null)
            {
                Debug.LogError("[{0}] Unable to route multiview tile: TX or RX endpoint is null", source.Key);
                return false;
            }

            if (!rxEndpoint.SupportsMultiview)
            {
                Debug.LogError("[{0}] Endpoint '{1}' does not support multiview tile routing", source.Key, rxEndpoint.Key);
                return false;
            }

            var trimmedLayout = string.IsNullOrWhiteSpace(layoutName)
                ? rxEndpoint.ActivePresetMultiviewLayoutName
                : layoutName.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLayout))
            {
                Debug.LogError("[{0}] Multiview tile routing requires an active preset layout. Activate one first with 'mscene active'.", source.Key);
                return false;
            }

            if (tileReference <= 0)
            {
                Debug.LogError("[{0}] Multiview tile reference must be >= 1", source.Key);
                return false;
            }

            if (rxEndpoint.AvailablePresetMultiviewLayouts.Count > 0 && !rxEndpoint.IsKnownPresetMultiviewLayout(trimmedLayout))
            {
                Debug.LogError("[{0}] Multiview preset layout '{1}' is not available on endpoint '{2}'", source.Key, trimmedLayout, rxEndpoint.Key);
                return false;
            }

            if (NhdBaseDevice.TryInferPresetLayoutShape(trimmedLayout, out var inferredTileCount, out _) && tileReference > inferredTileCount)
            {
                Debug.LogError("[{0}] Multiview tile reference {1} exceeds inferred tile count {2} for layout '{3}'", source.Key, tileReference, inferredTileCount, trimmedLayout);
                return false;
            }

            if (CanRouteTileNow(rxEndpoint, tileReference))
            {
                var command = BuildPresetTileRouteCommand(
                    txEndpoint.ApiEndpointReference,
                    rxEndpoint.ApiEndpointReference,
                    trimmedLayout,
                    tileReference);

                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Sending multiview tile route immediately: layout='{1}', tile={2}, tx='{3}', rx='{4}', mode='{5}', activeTiles={6}",
                    source,
                    trimmedLayout,
                    tileReference,
                    txEndpoint.ApiEndpointReference,
                    rxEndpoint.ApiEndpointReference,
                    rxEndpoint.MultiStreamMode,
                    rxEndpoint.ActiveTileCount);

                var sent = NhdApiCommandSender.TrySend(source, command);
                if (sent)
                {
                    if (!ShouldBypassFullscreenReturnClearForRoute(rxEndpoint, txEndpoint.ApiEndpointReference, trimmedLayout, tileReference))
                    {
                        ClearFullscreenReturnState(rxEndpoint, "tile route changed");
                    }
                }

                return sent;
            }

            _pendingTileRoutes[rxEndpoint.Key] = new PendingMultiviewTileRoute
            {
                TxEndpointKey = txEndpoint.Key,
                LayoutName = trimmedLayout,
                TileReference = tileReference,
                RequestedByKey = source.Key,
                QueuedUtc = DateTime.UtcNow,
            };

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Queued multiview tile route pending verification: layout='{1}', tile={2}, tx='{3}', rx='{4}', knownActiveTiles={5}, hasFreshState={6}",
                source,
                trimmedLayout,
                tileReference,
                txEndpoint.ApiEndpointReference,
                rxEndpoint.ApiEndpointReference,
                rxEndpoint.ActiveTileCount,
                rxEndpoint.IsMultiviewStateFresh(MultiviewStateFreshness));

            RequestMultiviewState(rxEndpoint, source);
            return false;
        }

        public bool TryActivateMultiviewLayout(IKeyed requestedBy, NhdBaseDevice rxEndpoint, string layoutName)
        {
            var source = requestedBy ?? _ctl;

            if (rxEndpoint == null)
            {
                Debug.LogError("[{0}] Unable to activate multiview layout: RX endpoint is null", source.Key);
                return false;
            }

            if (!rxEndpoint.SupportsMultiview)
            {
                Debug.LogError("[{0}] Endpoint '{1}' does not support multiview preset layouts", source.Key, rxEndpoint.Key);
                return false;
            }

            if (string.IsNullOrWhiteSpace(layoutName))
            {
                Debug.LogError("[{0}] Multiview preset layout name cannot be empty", source.Key);
                return false;
            }

            var trimmedLayout = layoutName.Trim();

            if (rxEndpoint.AvailablePresetMultiviewLayouts.Count > 0 && !rxEndpoint.IsKnownPresetMultiviewLayout(trimmedLayout))
            {
                Debug.LogError("[{0}] Multiview preset layout '{1}' is not available on endpoint '{2}'", source.Key, trimmedLayout, rxEndpoint.Key);
                return false;
            }

            var command = $"mscene active {rxEndpoint.ApiEndpointReference} {trimmedLayout}";

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Activating multiview preset layout '{1}' on endpoint '{2}'",
                source,
                trimmedLayout,
                rxEndpoint.ApiEndpointReference);

            return NhdApiCommandSender.TrySend(source, command);
        }

        public bool TryFullscreenMultiviewTile(IKeyed requestedBy, NhdBaseDevice rxEndpoint, int sourceTileReference)
        {
            var source = requestedBy ?? _ctl;

            if (rxEndpoint == null)
            {
                Debug.LogError("[{0}] Unable to fullscreen multiview tile: RX endpoint is null", source.Key);
                return false;
            }

            if (!rxEndpoint.SupportsMultiview)
            {
                Debug.LogError("[{0}] Endpoint '{1}' does not support multiview", source.Key, rxEndpoint.Key);
                return false;
            }

            if (sourceTileReference <= 0)
            {
                Debug.LogError("[{0}] Source tile reference must be >= 1", source.Key);
                return false;
            }

            if (!rxEndpoint.IsMultiviewStateFresh(MultiviewStateFreshness))
            {
                RequestMultiviewState(rxEndpoint, source, force: true);
                Debug.LogError("[{0}] Multiview state is stale on endpoint '{1}'. Refresh requested; try fullscreen again.", source.Key, rxEndpoint.Key);
                return false;
            }

            if (rxEndpoint.ActiveTileCount <= 1)
            {
                Debug.LogError("[{0}] Fullscreen requires active multiview tile count > 1 on endpoint '{1}'", source.Key, rxEndpoint.Key);
                return false;
            }

            if (sourceTileReference > rxEndpoint.ActiveTileCount)
            {
                Debug.LogError("[{0}] Source tile {1} exceeds active tile count {2} on endpoint '{3}'", source.Key, sourceTileReference, rxEndpoint.ActiveTileCount, rxEndpoint.Key);
                return false;
            }

            var previousLayout = rxEndpoint.ActivePresetMultiviewLayoutName;
            if (string.IsNullOrWhiteSpace(previousLayout))
            {
                Debug.LogError("[{0}] Cannot fullscreen tile on endpoint '{1}' because active layout is unknown", source.Key, rxEndpoint.Key);
                return false;
            }

            if (!rxEndpoint.TryGetActiveMultiviewTile(sourceTileReference, out var sourceTile) || string.IsNullOrWhiteSpace(sourceTile.SourceReference))
            {
                Debug.LogError("[{0}] Cannot fullscreen tile {1} on endpoint '{2}' because source is unknown", source.Key, sourceTileReference, rxEndpoint.Key);
                return false;
            }

            const string fullscreenLayout = "1-1";
            if (rxEndpoint.AvailablePresetMultiviewLayouts.Count > 0 && !rxEndpoint.IsKnownPresetMultiviewLayout(fullscreenLayout))
            {
                Debug.LogError("[{0}] Fullscreen layout '{1}' is not available on endpoint '{2}'", source.Key, fullscreenLayout, rxEndpoint.Key);
                return false;
            }

            ClearFullscreenReturnState(rxEndpoint, "new fullscreen requested");

            _pendingFullscreen[rxEndpoint.Key] = new PendingMultiviewFullscreen
            {
                PreviousLayoutName = previousLayout,
                SourceTileReference = sourceTileReference,
                SourceReference = sourceTile.SourceReference,
                QueuedUtc = DateTime.UtcNow,
            };

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Starting fullscreen transition endpoint='{1}', fromLayout='{2}', sourceTile={3}, sourceRef='{4}'",
                source,
                rxEndpoint.Key,
                previousLayout,
                sourceTileReference,
                sourceTile.SourceReference);

            return NhdApiCommandSender.TrySend(source, $"mscene active {rxEndpoint.ApiEndpointReference} {fullscreenLayout}");
        }

        public bool TryReturnFromMultiviewFullscreen(IKeyed requestedBy, NhdBaseDevice rxEndpoint)
        {
            var source = requestedBy ?? _ctl;

            if (rxEndpoint == null)
            {
                Debug.LogError("[{0}] Unable to return from fullscreen: RX endpoint is null", source.Key);
                return false;
            }

            if (!TryGetMultiviewFullscreenReturnLayout(rxEndpoint, out var returnLayout))
            {
                Debug.LogError("[{0}] No fullscreen return layout is available for endpoint '{1}'", source.Key, rxEndpoint.Key);
                return false;
            }

            _pendingFullscreenReturns[rxEndpoint.Key] = returnLayout;

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Returning from fullscreen endpoint='{1}' to layout='{2}'",
                source,
                rxEndpoint.Key,
                returnLayout);

            var sent = NhdApiCommandSender.TrySend(source, $"mscene active {rxEndpoint.ApiEndpointReference} {returnLayout}");
            if (!sent)
            {
                _pendingFullscreenReturns.Remove(rxEndpoint.Key);
            }

            return sent;
        }

        public bool TryGetMultiviewFullscreenReturnLayout(NhdBaseDevice rxEndpoint, out string layoutName)
        {
            layoutName = null;

            if (rxEndpoint == null)
                return false;

            if (!_fullscreenReturnStates.TryGetValue(rxEndpoint.Key, out var state))
                return false;

            if (string.IsNullOrWhiteSpace(state.PreviousLayoutName))
                return false;

            layoutName = state.PreviousLayoutName;
            return true;
        }

        public bool TryProbeAndLearnMultiviewLayouts(IKeyed requestedBy, NhdBaseDevice rxEndpoint)
        {
            var source = requestedBy ?? _ctl;

            if (rxEndpoint == null)
            {
                Debug.LogError("[{0}] Unable to probe multiview layouts: RX endpoint is null", source.Key);
                return false;
            }

            if (!rxEndpoint.SupportsMultiview)
            {
                Debug.LogError("[{0}] Endpoint '{1}' does not support multiview preset layouts", source.Key, rxEndpoint.Key);
                return false;
            }

            if (_pendingLayoutProbes.ContainsKey(rxEndpoint.Key))
            {
                Debug.LogError("[{0}] Layout probe already running for endpoint '{1}'", source.Key, rxEndpoint.Key);
                return false;
            }

            if (rxEndpoint.AvailablePresetMultiviewLayouts.Count == 0)
            {
                Debug.LogError("[{0}] Cannot start layout probe for endpoint '{1}' because no preset layout list is known yet", source.Key, rxEndpoint.Key);
                RequestMultiviewPresetLayouts(rxEndpoint, source);
                return false;
            }

            var orderedLayouts = rxEndpoint.AvailablePresetMultiviewLayouts
                .OrderBy(layout => ParseLayoutTileCount(layout))
                .ThenBy(layout => layout, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var probe = new PendingLayoutProbe
            {
                EndpointKey = rxEndpoint.Key,
                RemainingLayouts = new Queue<string>(orderedLayouts),
                RequestedByKey = source.Key,
            };

            _pendingLayoutProbes[rxEndpoint.Key] = probe;

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Starting multiview layout probe endpoint='{1}', totalLayouts={2}",
                source,
                rxEndpoint.Key,
                orderedLayouts.Count);

            if (!TrySendNextProbeLayout(rxEndpoint))
            {
                _pendingLayoutProbes.Remove(rxEndpoint.Key);
                return false;
            }

            return true;
        }

        public bool TryReprobeAndLearnMultiviewLayouts(IKeyed requestedBy, NhdBaseDevice rxEndpoint)
        {
            var source = requestedBy ?? _ctl;

            if (rxEndpoint == null)
            {
                Debug.LogError("[{0}] Unable to reprobe multiview layouts: RX endpoint is null", source.Key);
                return false;
            }

            _pendingLayoutGeometryCapture.Remove(rxEndpoint.Key);
            _pendingLayoutProbes.Remove(rxEndpoint.Key);
            _startupProbeCompleted.Remove(rxEndpoint.Key);
            rxEndpoint.ClearLearnedPresetLayoutGeometrySignatures();

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Starting explicit multiview layout reprobe for endpoint '{1}'",
                source,
                rxEndpoint.Key);

            return TryProbeAndLearnMultiviewLayouts(source, rxEndpoint);
        }

        private void HandleLineReceived(object sender, GenericCommMethodReceiveTextArgs args)
        {
            ExpirePendingTileRoutes();

            var line = (args.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line))
                return;

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] NHD API << {1}", _ctl, line);

            if (TryHandleMultiviewInformationBlock(line))
                return;

            if (TryHandleMsceneListBlock(line))
                return;

            if (TryHandleMsceneActiveResponseLine(line))
                return;

            if (TryHandleAliasMappingLine(line))
                return;

            if (TryHandleEndpointNotifyLine(line))
                return;

            TryHandleDeviceListLine(line);
        }

        private bool TryHandleMsceneActiveResponseLine(string line)
        {
            var match = MsceneActiveResponseRegex.Match(line);
            if (!match.Success)
                return false;

            var reference = match.Groups["reference"].Value.Trim();
            var layout = match.Groups["layout"].Value.Trim();
            var success = match.Groups["result"].Value.Equals("success", StringComparison.OrdinalIgnoreCase);

            var endpoint = ResolveEndpoint(reference);
            if (endpoint == null)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] mscene active response unresolved endpoint='{1}', layout='{2}', result='{3}'",
                    _ctl,
                    reference,
                    layout,
                    success ? "success" : "failure");
                return true;
            }

            var handledFullscreenTransition = false;
            if (_pendingFullscreen.TryGetValue(endpoint.Key, out var pendingFullscreen)
                && string.Equals(layout, "1-1", StringComparison.OrdinalIgnoreCase))
            {
                _pendingFullscreen.Remove(endpoint.Key);

                if (success)
                {
                    var command = BuildPresetTileRouteCommand(
                        pendingFullscreen.SourceReference,
                        endpoint.ApiEndpointReference,
                        "1-1",
                        1);

                    var routeSent = NhdApiCommandSender.TrySend(_ctl, command);
                    if (routeSent)
                    {
                        _recentFullscreenRoutes[endpoint.Key] = new RecentFullscreenRoute
                        {
                            TxReference = pendingFullscreen.SourceReference,
                            LayoutName = "1-1",
                            TileReference = 1,
                            RequestedUtc = DateTime.UtcNow,
                        };

                        SetFullscreenReturnState(endpoint, pendingFullscreen.PreviousLayoutName);
                        Debug.LogMessage(
                            Serilog.Events.LogEventLevel.Information,
                            "$$$$$$$$$$ [{0}] Fullscreen transition routed tile source endpoint='{1}', sourceTile={2}, sourceRef='{3}'",
                            _ctl,
                            endpoint.Key,
                            pendingFullscreen.SourceTileReference,
                            pendingFullscreen.SourceReference);
                    }
                    else
                    {
                        var rollbackSent = NhdApiCommandSender.TrySend(
                            _ctl,
                            $"mscene active {endpoint.ApiEndpointReference} {pendingFullscreen.PreviousLayoutName}");

                        Debug.LogMessage(
                            Serilog.Events.LogEventLevel.Information,
                            "$$$$$$$$$$ [{0}] Fullscreen transition route failed; rollback to previous layout '{1}' on endpoint '{2}' was {3}",
                            _ctl,
                            pendingFullscreen.PreviousLayoutName,
                            endpoint.Key,
                            rollbackSent ? "sent" : "not sent");

                        ClearFullscreenReturnState(endpoint, "fullscreen route command failed");
                    }
                }

                handledFullscreenTransition = true;
            }

            var handledFullscreenReturn = false;
            if (_pendingFullscreenReturns.TryGetValue(endpoint.Key, out var returnLayout)
                && string.Equals(returnLayout, layout, StringComparison.OrdinalIgnoreCase))
            {
                _pendingFullscreenReturns.Remove(endpoint.Key);
                if (success)
                {
                    ClearFullscreenReturnState(endpoint, "returned from fullscreen");
                }

                handledFullscreenReturn = true;
            }

            if (success)
            {
                endpoint.SetActivePresetMultiviewLayout(layout, inferred: false);
                _pendingLayoutGeometryCapture[endpoint.Key] = layout;

                if (NhdBaseDevice.TryInferPresetLayoutShape(layout, out var inferredTileCount, out var inferredMode))
                {
                    endpoint.SetMultiviewRuntimeState(inferredMode, inferredTileCount);
                    Debug.LogMessage(
                        Serilog.Events.LogEventLevel.Information,
                        "$$$$$$$$$$ [{0}] Inferred multiview layout shape endpoint='{1}', layout='{2}', mode='{3}', tiles={4}",
                        _ctl,
                        endpoint.Key,
                        layout,
                        inferredMode,
                        inferredTileCount);
                }

                RequestMultiviewState(endpoint, force: true);
            }

            if (success && !handledFullscreenTransition && !handledFullscreenReturn)
            {
                ClearFullscreenReturnState(endpoint, "layout recalled");
            }

            if (_pendingLayoutProbes.TryGetValue(endpoint.Key, out var probe)
                && string.Equals(probe.ActiveLayout, layout, StringComparison.OrdinalIgnoreCase)
                && !success)
            {
                probe.AttemptedCount++;
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Layout probe activation failed endpoint='{1}', layout='{2}', attempted={3}",
                    _ctl,
                    endpoint.Key,
                    layout,
                    probe.AttemptedCount);

                TrySendNextProbeLayout(endpoint);
            }

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] mscene active endpoint='{1}', layout='{2}', result='{3}'",
                _ctl,
                endpoint.Key,
                layout,
                success ? "success" : "failure");

            return true;
        }

        private bool TryHandleAliasMappingLine(string line)
        {
            var match = AliasLineRegex.Match(line);
            if (!match.Success)
                return false;

            var hostname = match.Groups["hostname"].Value.Trim();
            var aliasValue = match.Groups["alias"].Value.Trim();
            var alias = aliasValue.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : aliasValue;

            var endpoint = ResolveEndpoint(alias) ?? ResolveEndpoint(hostname);
            if (endpoint == null)
            {
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Alias mapping unresolved. Hostname='{1}', Alias='{2}'", _ctl, hostname, alias ?? "null");
                return true;
            }

            endpoint.SetResolvedHostname(hostname);
            endpoint.SetOnlineState(true);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Alias mapping resolved endpoint='{1}', Hostname='{2}', Alias='{3}'", _ctl, endpoint.Key, hostname, alias ?? "null");

            RequestMultiviewState(endpoint);
            RequestMultiviewPresetLayouts(endpoint);
            TryStartStartupProbeIfReady(endpoint);

            return true;
        }

        private bool TryHandleEndpointNotifyLine(string line)
        {
            var match = EndpointNotifyRegex.Match(line);
            if (!match.Success)
                return false;

            var isOnline = match.Groups["state"].Value == "+";
            var reference = match.Groups["reference"].Value.Trim();

            var endpoint = ResolveEndpoint(reference);
            endpoint?.SetOnlineState(isOnline);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Notify endpoint reference='{1}', state='{2}', resolvedEndpoint='{3}'", _ctl, reference, isOnline ? "online" : "offline", endpoint?.Key ?? "unresolved");

            if (isOnline && endpoint != null)
            {
                RequestMultiviewState(endpoint);
                RequestMultiviewPresetLayouts(endpoint);
                TryStartStartupProbeIfReady(endpoint);
            }

            return true;
        }

        private bool TryHandleDeviceListLine(string line)
        {
            const string prefix = "devicelist is";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var refs = line.Substring(prefix.Length).Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Processing devicelist with {1} references", _ctl, refs.Count);

            var listedEndpoints = new HashSet<NhdBaseDevice>();
            foreach (var reference in refs)
            {
                var endpoint = ResolveEndpoint(reference);
                if (endpoint == null)
                {
                    Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Devicelist reference unresolved: '{1}'", _ctl, reference);
                    continue;
                }

                listedEndpoints.Add(endpoint);
                endpoint.SetOnlineState(true);
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Devicelist marked endpoint online: '{1}' (ref='{2}')", _ctl, endpoint.Key, reference);
                RequestMultiviewState(endpoint);
                RequestMultiviewPresetLayouts(endpoint);
                TryStartStartupProbeIfReady(endpoint);
            }

            foreach (var endpoint in GetEndpoints().Where(e => !listedEndpoints.Contains(e)))
            {
                endpoint.SetOnlineState(false);
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Devicelist marked endpoint offline: '{1}'", _ctl, endpoint.Key);
            }

            return true;
        }

        private bool TryHandleMultiviewInformationBlock(string line)
        {
            if (_isParsingMviewInformation)
            {
                if (TryConsumeMviewInformationLine(line))
                    return true;

                FinalizePendingMviewInformationEntry();
                _isParsingMviewInformation = false;
            }

            if (line.StartsWith("mview information:", StringComparison.OrdinalIgnoreCase))
            {
                _isParsingMviewInformation = true;
                FinalizePendingMviewInformationEntry();
                return true;
            }

            return false;
        }

        private bool TryHandleMsceneListBlock(string line)
        {
            if (_isParsingMsceneList)
            {
                if (TryConsumeMsceneListLine(line))
                    return true;

                _isParsingMsceneList = false;
            }

            if (line.StartsWith("mscene list:", StringComparison.OrdinalIgnoreCase))
            {
                _isParsingMsceneList = true;
                return true;
            }

            return false;
        }

        private bool TryConsumeMsceneListLine(string line)
        {
            var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return false;

            var layoutTokens = tokens.Skip(1).ToList();
            if (!layoutTokens.Any(t => t.Contains("-")))
                return false;

            var endpoint = ResolveEndpoint(tokens[0]);
            if (endpoint == null || !endpoint.SupportsMultiview)
                return true;

            var layouts = layoutTokens
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            endpoint.SetAvailablePresetMultiviewLayouts(layouts);
            TryStartStartupProbeIfReady(endpoint);

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Parsed {1} preset layouts for endpoint '{2}'",
                _ctl,
                layouts.Count,
                endpoint.Key);

            return true;
        }

        private bool TryConsumeMviewInformationLine(string line)
        {
            var match = MviewInformationLineRegex.Match(line);
            if (match.Success)
            {
                FinalizePendingMviewInformationEntry();

                var reference = match.Groups["reference"].Value.Trim();
                var endpoint = ResolveEndpoint(reference);
                if (endpoint == null || !endpoint.SupportsMultiview)
                    return true;

                if (!TryParseMode(match.Groups["mode"].Value, out var mode))
                    return true;

                _pendingMviewEndpoint = endpoint;
                _pendingMviewMode = mode;
                _pendingMviewTiles.Clear();
                AppendTileDescriptors(match.Groups["tiles"].Value);
                return true;
            }

            if (_pendingMviewEndpoint != null && AppendTileDescriptors(line))
                return true;

            return false;
        }

        private static bool TryParseMode(string modeText, out NhdMultiStreamMode mode)
        {
            if (modeText.Equals("overlay", StringComparison.OrdinalIgnoreCase))
            {
                mode = NhdMultiStreamMode.Overlay;
                return true;
            }

            mode = NhdMultiStreamMode.Tile;
            return modeText.Equals("tile", StringComparison.OrdinalIgnoreCase);
        }

        private bool AppendTileDescriptors(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var appended = false;

            foreach (var token in tokens)
            {
                if (!TryParseTileDescriptor(token, _pendingMviewTiles.Count + 1, out var tile))
                    continue;

                _pendingMviewTiles.Add(tile);
                appended = true;
            }

            return appended;
        }

        private void FinalizePendingMviewInformationEntry()
        {
            if (_pendingMviewEndpoint == null)
                return;

            _pendingMviewEndpoint.SetMultiviewRuntimeState(_pendingMviewMode, _pendingMviewTiles);
            CaptureOrInferActiveLayout(_pendingMviewEndpoint);
            TryDispatchPendingTileRouteForEndpoint(_pendingMviewEndpoint);

            _pendingMviewEndpoint = null;
            _pendingMviewTiles.Clear();
        }

        private void CaptureOrInferActiveLayout(NhdBaseDevice endpoint)
        {
            if (endpoint == null || !endpoint.SupportsMultiview)
                return;

            if (_pendingLayoutGeometryCapture.TryGetValue(endpoint.Key, out var recalledLayout))
            {
                _pendingLayoutGeometryCapture.Remove(endpoint.Key);

                var captured = false;

                if (endpoint.TryCaptureActiveLayoutGeometry(recalledLayout))
                {
                    captured = true;
                    Debug.LogMessage(
                        Serilog.Events.LogEventLevel.Information,
                        "$$$$$$$$$$ [{0}] Learned layout geometry endpoint='{1}', layout='{2}'",
                        _ctl,
                        endpoint.Key,
                        recalledLayout);
                }

                if (_pendingLayoutProbes.TryGetValue(endpoint.Key, out var probe)
                    && string.Equals(probe.ActiveLayout, recalledLayout, StringComparison.OrdinalIgnoreCase))
                {
                    probe.AttemptedCount++;
                    if (captured)
                        probe.LearnedCount++;

                    TrySendNextProbeLayout(endpoint);
                }

                return;
            }

            if (!endpoint.TryIdentifyPresetLayoutByActiveGeometry(out var inferredLayout))
                return;

            var changed = !string.Equals(endpoint.ActivePresetMultiviewLayoutName, inferredLayout, StringComparison.OrdinalIgnoreCase)
                || !endpoint.ActivePresetMultiviewLayoutInferred;

            endpoint.SetActivePresetMultiviewLayout(inferredLayout, inferred: true);

            if (changed)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Inferred active layout from multiview state endpoint='{1}', layout='{2}', mode='{3}', tiles={4}",
                    _ctl,
                    endpoint.Key,
                    inferredLayout,
                    endpoint.MultiStreamMode,
                    endpoint.ActiveTileCount);
            }
        }

        private static bool TryParseTileDescriptor(string token, int tileNumber, out NhdMultiviewTileState tile)
        {
            tile = null;

            var match = TileDescriptorRegex.Match(token);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["x"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
                return false;

            if (!int.TryParse(match.Groups["y"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                return false;

            if (!int.TryParse(match.Groups["w"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
                return false;

            if (!int.TryParse(match.Groups["h"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
                return false;

            tile = new NhdMultiviewTileState(
                tileNumber,
                match.Groups["source"].Value,
                x,
                y,
                w,
                h,
                match.Groups["scale"].Value);

            return true;
        }

        private static bool CanRouteTileNow(NhdBaseDevice rxEndpoint, int tileReference)
        {
            if (!rxEndpoint.IsMultiviewStateFresh(MultiviewStateFreshness))
                return false;

            if (rxEndpoint.ActiveTileCount <= 0)
                return false;

            return tileReference <= rxEndpoint.ActiveTileCount;
        }

        private void RequestMultiviewState(NhdBaseDevice endpoint, IKeyed source = null, bool force = false)
        {
            if (endpoint == null || !endpoint.SupportsMultiview)
                return;

            if (!force && endpoint.IsMultiviewStateFresh(MultiviewRefreshThrottle))
                return;

            var sender = source ?? _ctl;
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Requesting multiview state for endpoint '{1}'", sender, endpoint.Key);
            NhdApiCommandSender.TrySend(sender, $"mview get {endpoint.ApiEndpointReference}");
        }

        private bool TrySendNextProbeLayout(NhdBaseDevice endpoint)
        {
            if (endpoint == null)
                return false;

            if (!_pendingLayoutProbes.TryGetValue(endpoint.Key, out var probe))
                return false;

            if (probe.RemainingLayouts.Count == 0)
            {
                _pendingLayoutProbes.Remove(endpoint.Key);
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Multiview layout probe complete endpoint='{1}', attempted={2}, learned={3}",
                    _ctl,
                    endpoint.Key,
                    probe.AttemptedCount,
                    probe.LearnedCount);
                return true;
            }

            var nextLayout = probe.RemainingLayouts.Dequeue();
            probe.ActiveLayout = nextLayout;

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Probing layout endpoint='{1}', layout='{2}', remaining={3}",
                _ctl,
                endpoint.Key,
                nextLayout,
                probe.RemainingLayouts.Count);

            return NhdApiCommandSender.TrySend(_ctl, $"mscene active {endpoint.ApiEndpointReference} {nextLayout}");
        }

        private void SetFullscreenReturnState(NhdBaseDevice endpoint, string previousLayoutName)
        {
            if (endpoint == null || string.IsNullOrWhiteSpace(previousLayoutName))
                return;

            _fullscreenReturnStates[endpoint.Key] = new MultiviewFullscreenReturnState
            {
                PreviousLayoutName = previousLayoutName,
                CapturedUtc = DateTime.UtcNow,
            };

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Fullscreen return available endpoint='{1}', returnLayout='{2}'",
                _ctl,
                endpoint.Key,
                previousLayoutName);
        }

        private void ClearFullscreenReturnState(NhdBaseDevice endpoint, string reason)
        {
            if (endpoint == null)
                return;

            _pendingFullscreen.Remove(endpoint.Key);
            _pendingFullscreenReturns.Remove(endpoint.Key);

            if (!_fullscreenReturnStates.Remove(endpoint.Key))
                return;

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Fullscreen return cleared endpoint='{1}', reason='{2}'",
                _ctl,
                endpoint.Key,
                reason ?? "unspecified");
        }

        private void TryStartStartupProbeIfReady(NhdBaseDevice endpoint)
        {
            if (endpoint == null || !endpoint.SupportsMultiview || endpoint.IsTransmitter)
                return;

            if (_startupProbeCompleted.Contains(endpoint.Key))
                return;

            if (_pendingLayoutProbes.ContainsKey(endpoint.Key))
                return;

            if (!endpoint.OnlineState)
                return;

            if (endpoint.AvailablePresetMultiviewLayouts.Count == 0)
                return;

            if (TryProbeAndLearnMultiviewLayouts(_ctl, endpoint))
            {
                _startupProbeCompleted.Add(endpoint.Key);
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Startup multiview layout probe started for endpoint '{1}'",
                    _ctl,
                    endpoint.Key);
            }
        }

        private static int ParseLayoutTileCount(string layoutName)
        {
            return NhdBaseDevice.TryInferPresetLayoutShape(layoutName, out var tileCount, out _)
                ? tileCount
                : int.MaxValue;
        }

        private void RequestMultiviewPresetLayouts(NhdBaseDevice endpoint, IKeyed source = null)
        {
            if (endpoint == null || !endpoint.SupportsMultiview)
                return;

            if (_lastMsceneListRequestUtc.TryGetValue(endpoint.Key, out var lastRequestUtc) && DateTime.UtcNow - lastRequestUtc < MsceneListRefreshThrottle)
                return;

            _lastMsceneListRequestUtc[endpoint.Key] = DateTime.UtcNow;

            var sender = source ?? _ctl;
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Requesting preset layout list for endpoint '{1}'", sender, endpoint.Key);
            NhdApiCommandSender.TrySend(sender, $"mscene get {endpoint.ApiEndpointReference}");
        }

        private static string BuildPresetTileRouteCommand(string txReference, string rxReference, string layoutName, int tileReference)
        {
            return $"mscene change {rxReference} {layoutName} {tileReference} {txReference}";
        }

        private void TryDispatchPendingTileRouteForEndpoint(NhdBaseDevice rxEndpoint)
        {
            if (rxEndpoint == null)
                return;

            if (!_pendingTileRoutes.TryGetValue(rxEndpoint.Key, out var pending))
                return;

            if (DateTime.UtcNow - pending.QueuedUtc > PendingTileRouteExpiry)
            {
                _pendingTileRoutes.Remove(rxEndpoint.Key);
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Dropping stale pending multiview tile route for endpoint '{1}'", _ctl, rxEndpoint.Key);
                return;
            }

            if (!rxEndpoint.IsMultiviewStateFresh(MultiviewStateFreshness))
                return;

            if (pending.TileReference > rxEndpoint.ActiveTileCount)
            {
                _pendingTileRoutes.Remove(rxEndpoint.Key);
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Rejected queued multiview tile route for endpoint '{1}': requested tile={2}, activeTiles={3}, mode='{4}'",
                    _ctl,
                    rxEndpoint.Key,
                    pending.TileReference,
                    rxEndpoint.ActiveTileCount,
                    rxEndpoint.MultiStreamMode);
                return;
            }

            var existingSource = rxEndpoint.TryGetActiveMultiviewTile(pending.TileReference, out var existingTile)
                ? existingTile.SourceReference
                : "unknown";

            var txEndpoint = DeviceManager.GetDeviceForKey(pending.TxEndpointKey) as NhdBaseDevice;
            if (txEndpoint == null)
            {
                _pendingTileRoutes.Remove(rxEndpoint.Key);
                Debug.LogError("[{0}] Queued multiview tile route dropped: TX endpoint '{1}' not found", _ctl.Key, pending.TxEndpointKey);
                return;
            }

            _pendingTileRoutes.Remove(rxEndpoint.Key);

            var command = BuildPresetTileRouteCommand(
                txEndpoint.ApiEndpointReference,
                rxEndpoint.ApiEndpointReference,
                pending.LayoutName,
                pending.TileReference);

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Verified queued multiview tile route; sending command layout='{1}', tile={2}, tx='{3}', rx='{4}', mode='{5}', activeTiles={6}",
                _ctl,
                pending.LayoutName,
                pending.TileReference,
                txEndpoint.ApiEndpointReference,
                rxEndpoint.ApiEndpointReference,
                rxEndpoint.MultiStreamMode,
                rxEndpoint.ActiveTileCount);

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Multiview tile {1} currently mapped to '{2}' on endpoint '{3}'",
                _ctl,
                pending.TileReference,
                existingSource ?? "null",
                rxEndpoint.Key);

            var sent = NhdApiCommandSender.TrySend(_ctl, command);
            if (sent)
            {
                if (!ShouldBypassFullscreenReturnClearForRoute(rxEndpoint, txEndpoint.ApiEndpointReference, pending.LayoutName, pending.TileReference))
                {
                    ClearFullscreenReturnState(rxEndpoint, "tile route changed");
                }
            }
        }

        private bool ShouldBypassFullscreenReturnClearForRoute(NhdBaseDevice rxEndpoint, string txReference, string layoutName, int tileReference)
        {
            if (rxEndpoint == null)
                return false;

            if (!_recentFullscreenRoutes.TryGetValue(rxEndpoint.Key, out var recent))
                return false;

            if (DateTime.UtcNow - recent.RequestedUtc > FullscreenRouteClearBypassWindow)
            {
                _recentFullscreenRoutes.Remove(rxEndpoint.Key);
                return false;
            }

            var isMatch = tileReference == recent.TileReference
                && string.Equals(layoutName?.Trim(), recent.LayoutName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(txReference?.Trim(), recent.TxReference, StringComparison.OrdinalIgnoreCase);

            if (!isMatch)
                return false;

            _recentFullscreenRoutes.Remove(rxEndpoint.Key);

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Keeping fullscreen return available for endpoint '{1}' because route matches recent fullscreen transition",
                _ctl,
                rxEndpoint.Key);

            return true;
        }

        private void ExpirePendingTileRoutes()
        {
            var expiredKeys = _pendingTileRoutes
                .Where(kvp => DateTime.UtcNow - kvp.Value.QueuedUtc > PendingTileRouteExpiry)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _pendingTileRoutes.Remove(key);
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Expired pending multiview tile route for endpoint '{1}'", _ctl, key);
            }
        }

        private static IEnumerable<NhdBaseDevice> GetEndpoints()
        {
            return DeviceManager.AllDevices
                .OfType<NhdBaseDevice>()
                .Where(d => d is not NhdCtlPro);
        }

        private static NhdBaseDevice ResolveEndpoint(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            var value = reference.Trim();

            return GetEndpoints().FirstOrDefault(d =>
                string.Equals(d.ConfiguredAlias, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Hostname, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Key, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}