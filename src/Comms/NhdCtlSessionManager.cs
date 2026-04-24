using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Enums;
using PepperDash.Essentials.Plugin.Routing;

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

        private static readonly Regex SinkNotifyRegex = new Regex(
            "^notify\\s+sink\\s+(?<state>[+-]|found|lost|on|off|sync|nosync|present|absent|online|offline|1|0)\\s+(?<reference>\\S+)(?:\\s+\\(.*\\))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VideoNotifyRegex = new Regex(
            "^notify\\s+video\\s+(?:(?<state1>[+-]|found|lost|on|off|sync|nosync|present|absent|online|offline|1|0)\\s+)?(?<reference>\\S+?)(?:\\s+(?<state2>[+-]|found|lost|on|off|sync|nosync|present|absent|online|offline|1|0))?(?:\\s+\\(.*\\))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MviewInformationLineRegex = new Regex(
            "^(?<reference>\\S+)\\s+(?<mode>tile|overlay)\\s*(?<tiles>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TileDescriptorRegex = new Regex(
            "^(?<source>[^:\\s]+):(?<x>\\d+)_(?<y>\\d+)_(?<w>\\d+)_(?<h>\\d+):(?<scale>fit|stretch)(?::\\d+)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MsceneActiveResponseRegex = new Regex(
            "^mscene\\s+active\\s+(?<reference>\\S+)\\s+(?<layout>\\S+)\\s+(?<result>success|failure)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MsceneChangeResponseRegex = new Regex(
            "^mscene\\s+change\\s+(?<reference>\\S+)\\s+(?<layout>\\S+)\\s+(?<tile>\\d+)\\s+(?<source>\\S+)\\s+(?<result>success|failure)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MsceneSetAudioResponseRegex = new Regex(
            "^mscene\\s+set\\s+audio\\s+(?<reference>\\S+)\\s+(?<layout>\\S+)\\s+(?<mode>window|separate)\\s+(?<target>\\S+)\\s+(?<result>success|failure)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MviewSetAudioResponseRegex = new Regex(
            "^mview\\s+set\\s+audio\\s+(?<reference>\\S+)\\s+separate\\s+(?<source>\\S+)\\s+(?<result>success|failure)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MatrixInformationHeaderRegex = new Regex(
            "^matrix(?:\\s+(?<domain>video|audio|usb|serial|infrared))?\\s+information:$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MatrixSetResponseRegex = new Regex(
            "^matrix(?:\\s+(?<domain>video|audio|usb|serial|infrared))?\\s+set\\s+(?<tx>\\S+)\\s+(?<rx>\\S+)\\s+(?<result>success|failure)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TelnetNegotiationRegex = new Regex(
            "\\[(?:[0-9A-F]{2}h)\\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TelnetHexTokenRegex = new Regex(
            "\\[(?<hex>[0-9A-F]{2})h\\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const byte TelnetIac = 0xFF;
        private const byte TelnetWill = 0xFB;
        private const byte TelnetWont = 0xFC;
        private const byte TelnetDo = 0xFD;
        private const byte TelnetDont = 0xFE;

        private const byte TelnetOptionEcho = 0x01;
        private const byte TelnetOptionSuppressGoAhead = 0x03;

        private static readonly TimeSpan MultiviewStateFreshness = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MultiviewRefreshThrottle = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MatrixRefreshThrottle = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MsceneListRefreshThrottle = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PendingTileRouteExpiry = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FullscreenRouteClearBypassWindow = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan SessionProbeThrottle = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan CredentialPromptThrottle = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LoginFailureThrottle = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan PromptDedupWindow = TimeSpan.FromMilliseconds(10);

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

        private sealed class StartupProbeRestoreState
        {
            public string OriginalLayoutName { get; set; }
            public string OriginalGeometrySignature { get; set; }
            public string MatchedOriginalLayoutName { get; set; }
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
        private readonly Dictionary<string, StartupProbeRestoreState> _startupProbeRestoreStates = new Dictionary<string, StartupProbeRestoreState>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _startupProbeCompleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingMultiviewFullscreen> _pendingFullscreen = new Dictionary<string, PendingMultiviewFullscreen>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pendingFullscreenReturns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MultiviewFullscreenReturnState> _fullscreenReturnStates = new Dictionary<string, MultiviewFullscreenReturnState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RecentFullscreenRoute> _recentFullscreenRoutes = new Dictionary<string, RecentFullscreenRoute>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _subscribedNotificationReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime? _lastMatrixRefreshUtc;
        private DateTime? _lastSessionProbeUtc;
        private DateTime? _lastUserPromptHandledUtc;
        private DateTime? _lastPasswordPromptHandledUtc;
        private DateTime? _lastLoginFailureHandledUtc;
        private DateTime? _lastStandaloneUserPromptSeenUtc;
        private DateTime? _lastStandalonePasswordPromptSeenUtc;
        private bool _loginAutomationDisabledNoticeLogged;
        private bool _isSessionReady;
        private bool _bootstrapPending;
        private bool _telnetAwaitingCommand;
        private bool _telnetAwaitingOption;
        private byte _telnetPendingCommand;

        private bool _isParsingMviewInformation;
        private bool _isParsingMatrixInformation;
        private bool _isParsingMsceneList;
        private NhdBaseDevice _pendingMviewEndpoint;
        private NhdMultiStreamMode _pendingMviewMode;
        private readonly List<NhdMultiviewTileState> _pendingMviewTiles = new List<NhdMultiviewTileState>();
        private eRoutingSignalType _pendingMatrixSignalType = eRoutingSignalType.AudioVideo;

        public NhdCtlSessionManager(NhdCtlPro ctl)
        {
            _ctl = ctl;
            // Accept both LF and CRLF responses from the CTL CLI.
            _gather = new CommunicationGather(ctl.Comms, "\n");
            _gather.LineReceived += HandleLineReceived;
            _ctl.Comms.TextReceived += HandleRawTextReceived;
        }

        public bool IsReadyForApiCommands => _isSessionReady;

        public void Start()
        {
            ArmBootstrap("startup");
            SendSessionProbe("startup");
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

            CaptureStartupProbeRestoreState(rxEndpoint);
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
                _startupProbeRestoreStates.Remove(rxEndpoint.Key);
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
            _startupProbeRestoreStates.Remove(rxEndpoint.Key);
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

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ NHD API << {Line}", _ctl, line);

            if (TryHandleSessionLifecycleLine(line))
                return;

            if (TryHandleMultiviewInformationBlock(line))
                return;

            if (TryHandleMatrixInformationBlock(line))
                return;

            if (TryHandleMatrixSetResponseLine(line))
                return;

            if (TryHandleMsceneListBlock(line))
                return;

            if (TryHandleMsceneActiveResponseLine(line))
                return;

            if (TryHandleMsceneChangeResponseLine(line))
                return;

            if (TryHandleMsceneSetAudioResponseLine(line))
                return;

            if (TryHandleMviewSetAudioResponseLine(line))
                return;

            if (TryHandleAliasMappingLine(line))
                return;

            if (TryHandleEndpointNotifyLine(line))
                return;

            if (TryHandleSinkNotifyLine(line))
                return;

            if (TryHandleVideoNotifyLine(line))
                return;

            TryHandleDeviceListLine(line);
        }

        private void HandleRawTextReceived(object sender, GenericCommMethodReceiveTextArgs args)
        {
            var chunk = args?.Text;
            if (string.IsNullOrEmpty(chunk))
                return;

            TryHandleTelnetNegotiationBytes(chunk);
            TryHandleSessionLifecycleChunk(chunk);
        }

        private void TryHandleSessionLifecycleChunk(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk))
                return;

            if (chunk.IndexOf("*** IDLE TIMEOUT ***", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ArmBootstrap("idle timeout");
                return;
            }

            if (chunk.IndexOf("Unable to Login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                HandleLoginFailure("login failed");
                return;
            }

            if (TryHandleCredentialPrompts(chunk))
            {
                return;
            }

            if (!_isSessionReady && chunk.IndexOf("Welcome to NetworkHD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                MarkSessionReady("welcome banner");
            }
        }

        private void TryHandleTelnetNegotiationBytes(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
                return;

            var bytes = Encoding.GetEncoding(28591).GetBytes(chunk);
            if (bytes.Length == 0)
                return;

            var response = new List<byte>();
            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];

                if (_telnetAwaitingOption)
                {
                    AppendTelnetNegotiationReply(_telnetPendingCommand, value, response);
                    _telnetAwaitingOption = false;
                    continue;
                }

                if (_telnetAwaitingCommand)
                {
                    if (value == TelnetWill || value == TelnetWont || value == TelnetDo || value == TelnetDont)
                    {
                        _telnetPendingCommand = value;
                        _telnetAwaitingOption = true;
                    }

                    _telnetAwaitingCommand = false;
                    continue;
                }

                if (value == TelnetIac)
                {
                    _telnetAwaitingCommand = true;
                }
            }

            if (response.Count <= 0)
                return;

            _ctl.Comms.SendBytes(response.ToArray());
            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ Sent Telnet negotiation reply bytes: {0}",
                _ctl,
                FormatByteSequence(response));
        }

        private static void AppendTelnetNegotiationReply(byte command, byte option, List<byte> response)
        {
            if (response == null)
                return;

            switch (command)
            {
                case TelnetWill:
                    response.Add(TelnetIac);
                    response.Add(SupportsRemoteWillOption(option) ? TelnetDo : TelnetDont);
                    response.Add(option);
                    break;

                case TelnetWont:
                    response.Add(TelnetIac);
                    response.Add(TelnetDont);
                    response.Add(option);
                    break;

                case TelnetDo:
                    response.Add(TelnetIac);
                    response.Add(SupportsLocalDoOption(option) ? TelnetWill : TelnetWont);
                    response.Add(option);
                    break;

                case TelnetDont:
                    response.Add(TelnetIac);
                    response.Add(TelnetWont);
                    response.Add(option);
                    break;
            }
        }

        private static string FormatByteSequence(IEnumerable<byte> bytes)
        {
            if (bytes == null)
                return string.Empty;

            return string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }

        private bool TryHandleSessionLifecycleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (line.IndexOf("*** IDLE TIMEOUT ***", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ArmBootstrap("idle timeout");
                return true;
            }

            if (line.StartsWith("Unable to Login", StringComparison.OrdinalIgnoreCase))
            {
                HandleLoginFailure("login failed");
                return true;
            }

            if (line.IndexOf("Welcome to NetworkHD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                MarkSessionReady("welcome banner");
                return true;
            }

            if (!_isSessionReady && IsLikelyApiResponseLine(line))
            {
                MarkSessionReady("api response");
                return false;
            }

            if (TryHandleTelnetNegotiationLine(line) && !_isSessionReady)
            {
                return true;
            }

            return false;
        }

        private bool TryHandleCredentialPrompts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var sawUserPrompt = ContainsStandalonePrompt(text, "User:");
            var sawPasswordPrompt = ContainsStandalonePrompt(text, "Password:");
            if (!sawUserPrompt && !sawPasswordPrompt)
                return false;

            if (!_ctl.EnableTelnetLoginAutomation)
            {
                if (!_loginAutomationDisabledNoticeLogged)
                {
                    _loginAutomationDisabledNoticeLogged = true;
                    Debug.LogMessage(
                        Serilog.Events.LogEventLevel.Information,
                        "$$$$$$$$$$ Telnet login automation disabled; ignoring credential prompts",
                        _ctl);
                }

                return true;
            }

            var now = DateTime.UtcNow;
            var username = _ctl.ApiUsername ?? string.Empty;
            var password = _ctl.ApiPassword ?? string.Empty;

            if (sawUserPrompt
                && !IsDuplicatePrompt(ref _lastStandaloneUserPromptSeenUtc, now)
                && (!_lastUserPromptHandledUtc.HasValue || now - _lastUserPromptHandledUtc.Value >= CredentialPromptThrottle))
            {
                _lastUserPromptHandledUtc = now;
                ArmBootstrap("login prompt detected");
                SendUsernameCredential(username);
            }

            if (sawPasswordPrompt
                && !IsDuplicatePrompt(ref _lastStandalonePasswordPromptSeenUtc, now)
                && (!_lastPasswordPromptHandledUtc.HasValue || now - _lastPasswordPromptHandledUtc.Value >= CredentialPromptThrottle))
            {
                _lastPasswordPromptHandledUtc = now;
                ArmBootstrap("password prompt detected");
                SendPasswordCredential(password);
            }

            return true;
        }

        private static bool IsDuplicatePrompt(ref DateTime? lastSeenUtc, DateTime now)
        {
            if (lastSeenUtc.HasValue && now - lastSeenUtc.Value < PromptDedupWindow)
                return true;

            lastSeenUtc = now;
            return false;
        }

        private void SendUsernameCredential(string username)
        {
            _ctl.Comms.SendText((username ?? string.Empty) + "\r\n");

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ Login prompt 'User:' detected; sending configured username",
                _ctl);
        }

        private void SendPasswordCredential(string password)
        {
            _ctl.Comms.SendText((password ?? string.Empty) + "\r\n");

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ Login prompt 'Password:' detected; sending configured password",
                _ctl);
        }

        private void HandleLoginFailure(string reason)
        {
            var now = DateTime.UtcNow;
            if (_lastLoginFailureHandledUtc.HasValue && now - _lastLoginFailureHandledUtc.Value < LoginFailureThrottle)
                return;

            _lastLoginFailureHandledUtc = now;
            ArmBootstrap(reason);

            if (_ctl.EnableTelnetLoginAutomation)
            {
                Debug.LogError(
                    "[{0}] CTL login failed. Verify configured API username/password.",
                    _ctl.Key);
            }
        }

        private static bool ContainsStandalonePrompt(string text, string promptToken)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(promptToken))
                return false;

            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var index = text.IndexOf(promptToken, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return false;

                var nextIndex = index + promptToken.Length;
                if (nextIndex >= text.Length)
                    return true;

                var next = text[nextIndex];
                if (next == '\r' || next == '\n' || char.IsWhiteSpace(next))
                    return true;

                searchIndex = nextIndex;
            }

            return false;
        }

        private bool HasRecentCredentialPrompt()
        {
            var now = DateTime.UtcNow;

            if (_lastUserPromptHandledUtc.HasValue && now - _lastUserPromptHandledUtc.Value < SessionProbeThrottle)
                return true;

            if (_lastPasswordPromptHandledUtc.HasValue && now - _lastPasswordPromptHandledUtc.Value < SessionProbeThrottle)
                return true;

            return false;
        }

        private static bool IsLikelyApiResponseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (TelnetNegotiationRegex.IsMatch(line))
                return false;

            if (line.StartsWith("User:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Password:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Unable to Login", StringComparison.OrdinalIgnoreCase)
                || line.IndexOf("*** IDLE TIMEOUT ***", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return line.StartsWith("config ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("notify ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("matrix ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("mview ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("mscene ", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("+OK", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("-ERR", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryHandleTelnetNegotiationLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !TelnetNegotiationRegex.IsMatch(line))
                return false;

            var tokens = ParseTelnetHexTokens(line);
            if (tokens.Count < 3)
                return true;

            var response = new List<byte>();
            for (var i = 0; i + 2 < tokens.Count; i += 3)
            {
                if (tokens[i] != TelnetIac)
                    continue;

                var command = tokens[i + 1];
                var option = tokens[i + 2];

                AppendTelnetNegotiationReply(command, option, response);
            }

            if (response.Count > 0)
            {
                _ctl.Comms.SendBytes(response.ToArray());
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ Sent Telnet negotiation reply bytes: {0}",
                    _ctl,
                    FormatByteSequence(response));
            }

            return true;
        }

        private static List<byte> ParseTelnetHexTokens(string line)
        {
            var values = new List<byte>();
            if (string.IsNullOrWhiteSpace(line))
                return values;

            var matches = TelnetHexTokenRegex.Matches(line);
            foreach (Match match in matches)
            {
                if (!match.Success)
                    continue;

                var hex = match.Groups["hex"].Value;
                if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    values.Add(value);
            }

            return values;
        }

        private static bool SupportsRemoteWillOption(byte option)
        {
            return option == TelnetOptionEcho || option == TelnetOptionSuppressGoAhead;
        }

        private static bool SupportsLocalDoOption(byte option)
        {
            // This client only agrees to suppress-go-ahead locally.
            return option == TelnetOptionSuppressGoAhead;
        }

        private void ArmBootstrap(string reason)
        {
            var wasReady = _isSessionReady;

            _isSessionReady = false;
            _bootstrapPending = true;
            _isParsingMviewInformation = false;
            _isParsingMatrixInformation = false;
            _isParsingMsceneList = false;
            _pendingMviewEndpoint = null;
            _pendingMviewTiles.Clear();
            _subscribedNotificationReferences.Clear();
            _lastMatrixRefreshUtc = null;
            _startupProbeRestoreStates.Clear();

            if (wasReady)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ Session marked not-ready; reason='{Reason}'",
                    _ctl,
                    reason ?? "unspecified");
            }
        }

        private void MarkSessionReady(string reason)
        {
            var wasReady = _isSessionReady;
            _isSessionReady = true;

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ Session ready; reason='{Reason}', bootstrapPending={BootstrapPending}",
                _ctl,
                reason ?? "unspecified",
                _bootstrapPending);

            if (_bootstrapPending)
            {
                _bootstrapPending = false;
                RunBootstrapQueries();
            }
            else if (!wasReady)
            {
                RequestMatrixState(_ctl, force: true);
            }
        }

        private void RunBootstrapQueries()
        {
            // Preferred endpoint references are aliases, but some replies still return hostnames.
            _startupProbeCompleted.Clear();
            _lastMsceneListRequestUtc.Clear();

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ Running CTL bootstrap queries",
                _ctl);

            NhdApiCommandSender.TrySend(_ctl, "config set session alias on");
            NhdApiCommandSender.TrySend(_ctl, "config get name");
            NhdApiCommandSender.TrySend(_ctl, "config get devicelist");

            foreach (var endpoint in GetEndpoints())
            {
                EnsureNotificationsSubscribed(endpoint.ConfiguredAlias, _ctl);
            }

            RequestMatrixState(_ctl, force: true);
        }

        private void EnsureConnected()
        {
            if (_ctl.Comms == null)
                return;

            if (_ctl.Comms.IsConnected)
                return;

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ Connecting NHD-CTL session transport", _ctl);
            _ctl.Comms.Connect();
        }

        private void SendSessionProbe(string reason)
        {
            if (_ctl.Comms == null)
                return;

            if (HasRecentCredentialPrompt())
                return;

            if (_lastSessionProbeUtc.HasValue && DateTime.UtcNow - _lastSessionProbeUtc.Value < SessionProbeThrottle)
                return;

            EnsureConnected();
            if (!_ctl.Comms.IsConnected)
                return;

            _lastSessionProbeUtc = DateTime.UtcNow;

            if (_ctl.Comms is GenericSshClient)
            {
                // SSH sessions can come up without a banner/prompt; send a safe command probe
                // so first response can mark session ready and release bootstrap.
                SendPreReadyApiCommand("config get name");
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Debug,
                    "Sent session command probe; reason='{Reason}'",
                    _ctl,
                    reason ?? "unspecified");
                return;
            }

            _ctl.Comms.SendText("\r\n");

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Debug,
                "Sent session probe line; reason='{Reason}'",
                _ctl,
                reason ?? "unspecified");
        }

        private void SendPreReadyApiCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || _ctl.Comms == null)
                return;

            var normalized = command.Trim();
            _ctl.Comms.SendText(normalized + "\r\n");

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ NHD API >> {Command}",
                _ctl,
                normalized);

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Debug,
                "NHD API >> {Command}",
                _ctl,
                normalized);
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
                endpoint.ApplyPresetLayoutAudioSetting(layout);
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

        private bool TryHandleMsceneChangeResponseLine(string line)
        {
            var match = MsceneChangeResponseRegex.Match(line);
            if (!match.Success)
                return false;

            var reference = match.Groups["reference"].Value.Trim();
            var layout = match.Groups["layout"].Value.Trim();
            var tile = match.Groups["tile"].Value.Trim();
            var source = match.Groups["source"].Value.Trim();
            var success = match.Groups["result"].Value.Equals("success", StringComparison.OrdinalIgnoreCase);

            var endpoint = ResolveEndpoint(reference);
            if (endpoint == null)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] mscene change unresolved endpoint='{1}', layout='{2}', tile='{3}', source='{4}', result='{5}'",
                    _ctl,
                    reference,
                    layout,
                    tile,
                    source,
                    success ? "success" : "failure");
                return true;
            }

            if (success)
            {
                endpoint.SetActivePresetMultiviewLayout(layout, inferred: false);
                endpoint.ApplyPresetLayoutAudioSetting(layout);
                _pendingLayoutGeometryCapture[endpoint.Key] = layout;

                if (NhdBaseDevice.TryInferPresetLayoutShape(layout, out var inferredTileCount, out var inferredMode))
                {
                    endpoint.SetMultiviewRuntimeState(inferredMode, inferredTileCount);
                }

                RequestMultiviewState(endpoint, force: true);
                ClearFullscreenReturnState(endpoint, "layout tile changed");
            }

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] mscene change endpoint='{1}', layout='{2}', tile='{3}', source='{4}', result='{5}'",
                _ctl,
                endpoint.Key,
                layout,
                tile,
                source,
                success ? "success" : "failure");

            return true;
        }

        private bool TryHandleMsceneSetAudioResponseLine(string line)
        {
            var match = MsceneSetAudioResponseRegex.Match(line);
            if (!match.Success)
                return false;

            var reference = match.Groups["reference"].Value.Trim();
            var layout = match.Groups["layout"].Value.Trim();
            var mode = match.Groups["mode"].Value.Trim();
            var target = match.Groups["target"].Value.Trim();
            var success = match.Groups["result"].Value.Equals("success", StringComparison.OrdinalIgnoreCase);

            var endpoint = ResolveEndpoint(reference);
            if (success && endpoint != null)
            {
                if (mode.Equals("window", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var audioWindow))
                        endpoint.SetPresetLayoutAudioWindow(layout, audioWindow);
                }
                else if (mode.Equals("separate", StringComparison.OrdinalIgnoreCase))
                {
                    endpoint.SetPresetLayoutAudioSeparateSource(layout, target);
                }

                // mscene set audio writes saved preset configuration and is not live until layout activation.
                if (string.Equals(endpoint.ActivePresetMultiviewLayoutName, layout, StringComparison.OrdinalIgnoreCase))
                {
                    endpoint.ApplyPresetLayoutAudioSetting(layout);
                    RequestMultiviewState(endpoint, force: true);
                }
            }

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] mscene set audio reference='{1}', resolvedEndpoint='{2}', layout='{3}', mode='{4}', target='{5}', result='{6}'",
                _ctl,
                reference,
                endpoint?.Key ?? "unresolved",
                layout,
                mode,
                target,
                success ? "success" : "failure");

            return true;
        }

        private bool TryHandleMviewSetAudioResponseLine(string line)
        {
            var match = MviewSetAudioResponseRegex.Match(line);
            if (!match.Success)
                return false;

            var reference = match.Groups["reference"].Value.Trim();
            var source = match.Groups["source"].Value.Trim();
            var success = match.Groups["result"].Value.Equals("success", StringComparison.OrdinalIgnoreCase);

            var endpoint = ResolveEndpoint(reference);
            if (success && endpoint != null)
            {
                endpoint.SetActiveMultiviewAudioSeparateSource(source);
                RequestMultiviewState(endpoint, force: true);
            }

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] mview set audio reference='{1}', resolvedEndpoint='{2}', source='{3}', result='{4}'",
                _ctl,
                reference,
                endpoint?.Key ?? "unresolved",
                source,
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

            EnsureNotificationsSubscribed(hostname);
            EnsureNotificationsSubscribed(alias);

            RequestMultiviewState(endpoint);
            RequestMultiviewPresetLayouts(endpoint);
            RequestMatrixState();
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

            EnsureNotificationsSubscribed(reference);

            if (isOnline && endpoint != null)
            {
                RequestMultiviewState(endpoint);
                RequestMultiviewPresetLayouts(endpoint);
                RequestMatrixState();
                TryStartStartupProbeIfReady(endpoint);
            }

            return true;
        }

        private bool TryHandleVideoNotifyLine(string line)
        {
            var match = VideoNotifyRegex.Match(line);
            if (!match.Success)
                return false;

            var reference = match.Groups["reference"].Value.Trim();
            var stateToken = !string.IsNullOrWhiteSpace(match.Groups["state1"].Value)
                ? match.Groups["state1"].Value.Trim()
                : match.Groups["state2"].Value.Trim();

            EnsureNotificationsSubscribed(reference);

            if (!TryParseVideoNotifyState(stateToken, out var syncDetected))
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Notify video state could not be parsed: reference='{1}', token='{2}'",
                    _ctl,
                    reference,
                    stateToken);
                return true;
            }

            var endpoint = ResolveEndpoint(reference);
            endpoint?.SetInputSyncState(syncDetected);

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Notify video reference='{1}', sync='{2}', resolvedEndpoint='{3}'",
                _ctl,
                reference,
                syncDetected ? "detected" : "lost",
                endpoint?.Key ?? "unresolved");

            return true;
        }

        private bool TryHandleSinkNotifyLine(string line)
        {
            var match = SinkNotifyRegex.Match(line);
            if (!match.Success)
                return false;

            var reference = match.Groups["reference"].Value.Trim();
            var stateToken = match.Groups["state"].Value.Trim();

            EnsureNotificationsSubscribed(reference);

            if (!TryParseVideoNotifyState(stateToken, out var sinkDetected))
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Notify sink state could not be parsed: reference='{1}', token='{2}'",
                    _ctl,
                    reference,
                    stateToken);
                return true;
            }

            var endpoint = ResolveEndpoint(reference);
            if (endpoint?.IsTransmitter == true)
            {
                endpoint.SetInputSyncState(sinkDetected);
            }

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Notify sink reference='{1}', state='{2}', resolvedEndpoint='{3}', appliedToSync='{4}'",
                _ctl,
                reference,
                sinkDetected ? "found" : "lost",
                endpoint?.Key ?? "unresolved",
                endpoint?.IsTransmitter == true ? "yes" : "no");

            return true;
        }

        private static bool TryParseVideoNotifyState(string token, out bool syncDetected)
        {
            syncDetected = false;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var value = token.Trim();
            if (value == "+"
                || value.Equals("found", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase)
                || value.Equals("sync", StringComparison.OrdinalIgnoreCase)
                || value.Equals("present", StringComparison.OrdinalIgnoreCase)
                || value.Equals("online", StringComparison.OrdinalIgnoreCase)
                || value == "1")
            {
                syncDetected = true;
                return true;
            }

            if (value == "-"
                || value.Equals("lost", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase)
                || value.Equals("nosync", StringComparison.OrdinalIgnoreCase)
                || value.Equals("absent", StringComparison.OrdinalIgnoreCase)
                || value.Equals("offline", StringComparison.OrdinalIgnoreCase)
                || value == "0")
            {
                syncDetected = false;
                return true;
            }

            return false;
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
                EnsureNotificationsSubscribed(reference);

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
                RequestMatrixState();
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

        private bool TryHandleMatrixInformationBlock(string line)
        {
            if (_isParsingMatrixInformation)
            {
                if (TryConsumeMatrixInformationLine(line))
                    return true;

                _isParsingMatrixInformation = false;
            }

            var headerMatch = MatrixInformationHeaderRegex.Match(line);
            if (!headerMatch.Success)
                return false;

            _pendingMatrixSignalType = GetMatrixSignalType(headerMatch.Groups["domain"].Value);
            _isParsingMatrixInformation = true;
            return true;
        }

        private bool TryConsumeMatrixInformationLine(string line)
        {
            var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // Matrix information entries are strict "<txRef> <rxRef>" pairs.
            // Requiring exactly two tokens prevents this parser from swallowing
            // unrelated lines while a matrix block is open.
            if (tokens.Length != 2)
                return false;

            var txReference = tokens[0].Trim();
            var rxReference = tokens[1].Trim();

            TryApplyMatrixRoute(txReference, rxReference, _pendingMatrixSignalType);
            return true;
        }

        private bool TryHandleMatrixSetResponseLine(string line)
        {
            var match = MatrixSetResponseRegex.Match(line);
            if (!match.Success)
                return false;

            if (!match.Groups["result"].Value.Equals("success", StringComparison.OrdinalIgnoreCase))
                return true;

            var txReference = match.Groups["tx"].Value.Trim();
            var rxReference = match.Groups["rx"].Value.Trim();
            var signalType = GetMatrixSignalType(match.Groups["domain"].Value);

            TryApplyMatrixRoute(txReference, rxReference, signalType);
            return true;
        }

        private void TryApplyMatrixRoute(string txReference, string rxReference, eRoutingSignalType signalType)
        {
            var rxEndpoint = ResolveEndpoint(rxReference);
            if (rxEndpoint == null)
                return;

            var txEndpointKey = IsNullRouteReference(txReference)
                ? null
                : ResolveEndpoint(txReference)?.Key;

            if (!string.IsNullOrWhiteSpace(txReference) && !IsNullRouteReference(txReference) && string.IsNullOrWhiteSpace(txEndpointKey))
                return;

            if (!NhdGlobalRouter.Instance.TrySetTrackedMatrixRoute(txEndpointKey, rxEndpoint.Key, signalType))
                return;

            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Applied tracked matrix route from CTL feedback: tx='{1}', rx='{2}', signal='{3}'",
                _ctl,
                txEndpointKey ?? "null",
                rxEndpoint.Key,
                signalType);
        }

        private static eRoutingSignalType GetMatrixSignalType(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return eRoutingSignalType.AudioVideo;

            var normalized = domain.Trim();
            if (normalized.Equals("video", StringComparison.OrdinalIgnoreCase))
                return eRoutingSignalType.Video;

            if (normalized.Equals("audio", StringComparison.OrdinalIgnoreCase))
                return eRoutingSignalType.Audio;

            if (normalized.Equals("usb", StringComparison.OrdinalIgnoreCase))
                return NhdRoutingSignalTypes.UsbInput | NhdRoutingSignalTypes.UsbOutput | eRoutingSignalType.Usb;

            if (normalized.Equals("serial", StringComparison.OrdinalIgnoreCase))
                return NhdRoutingSignalTypes.Serial;

            if (normalized.Equals("infrared", StringComparison.OrdinalIgnoreCase))
                return NhdRoutingSignalTypes.Ir;

            return eRoutingSignalType.AudioVideo;
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

            // Stop list parsing when a command/response line starts (for example: "mscene active ...").
            if (tokens[0].Equals("mscene", StringComparison.OrdinalIgnoreCase))
                return false;

            var layoutTokens = tokens.Skip(1).ToList();
            if (!layoutTokens.Any(t => t.Contains("-")))
                return false;

            var endpoint = ResolveEndpoint(tokens[0]);
            if (endpoint == null || !endpoint.SupportsMultiview)
                return true;

            // If the CTL is returning a preset list for this endpoint, it is online for API purposes.
            endpoint.SetOnlineState(true);

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

                // Endpoint-scoped "mview get <rx>" responses are typically a single entry.
                // Finalize as soon as enough tile descriptors are parsed for the active probe layout.
                if (ShouldFinalizePendingMviewEntry())
                {
                    FinalizePendingMviewInformationEntry();
                    _isParsingMviewInformation = false;
                }

                return true;
            }

            if (_pendingMviewEndpoint != null && AppendTileDescriptors(line))
            {
                if (ShouldFinalizePendingMviewEntry())
                {
                    FinalizePendingMviewInformationEntry();
                    _isParsingMviewInformation = false;
                }

                return true;
            }

            return false;
        }

        private bool ShouldFinalizePendingMviewEntry()
        {
            if (_pendingMviewEndpoint == null)
                return false;

            if (_pendingMviewTiles.Count == 0)
                return false;

            if (_pendingLayoutGeometryCapture.TryGetValue(_pendingMviewEndpoint.Key, out var recalledLayout)
                && NhdBaseDevice.TryInferPresetLayoutShape(recalledLayout, out var expectedTileCount, out _))
            {
                return _pendingMviewTiles.Count >= expectedTileCount;
            }

            return true;
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
            TryStartStartupProbeIfReady(_pendingMviewEndpoint);

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
                    TryMatchStartupProbeOriginalLayout(endpoint, recalledLayout);
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
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ Requesting multiview state for endpoint '{EndpointKey}'", sender, endpoint.Key);
            NhdApiCommandSender.TrySend(sender, $"mview get {endpoint.ApiEndpointReference}");
        }

        private void RequestMatrixState(IKeyed source = null, bool force = false)
        {
            if (!force && _lastMatrixRefreshUtc.HasValue && DateTime.UtcNow - _lastMatrixRefreshUtc.Value < MatrixRefreshThrottle)
                return;

            _lastMatrixRefreshUtc = DateTime.UtcNow;

            var sender = source ?? _ctl;

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ Requesting matrix route state", sender);

            NhdApiCommandSender.TrySend(sender, "matrix video get");
            NhdApiCommandSender.TrySend(sender, "matrix audio get");
            NhdApiCommandSender.TrySend(sender, "matrix usb get");
            NhdApiCommandSender.TrySend(sender, "matrix serial get");
            NhdApiCommandSender.TrySend(sender, "matrix infrared get");
        }

        private void EnsureNotificationsSubscribed(string endpointReference, IKeyed source = null)
        {
            if (string.IsNullOrWhiteSpace(endpointReference))
                return;

            var reference = endpointReference.Trim();
            if (_subscribedNotificationReferences.Contains(reference))
                return;

            var sender = source ?? _ctl;
            var endpointSubscribed = NhdApiCommandSender.TrySend(sender, $"notify endpoint {reference}");

            if (!endpointSubscribed)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ Failed to subscribe endpoint notifications for endpoint reference '{EndpointRef}' (endpoint={EndpointResult})",
                    sender,
                    reference,
                    endpointSubscribed ? "ok" : "failed");
                return;
            }

            _subscribedNotificationReferences.Add(reference);
            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ Queued notification subscriptions for endpoint reference '{EndpointRef}'",
                sender,
                reference);
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
                TryRestoreLayoutAfterProbe(endpoint);
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

            if (!endpoint.IsMultiviewStateFresh(MultiviewStateFreshness))
            {
                RequestMultiviewState(endpoint, force: true);
                return;
            }

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

        private void CaptureStartupProbeRestoreState(NhdBaseDevice endpoint)
        {
            if (endpoint == null)
                return;

            _startupProbeRestoreStates[endpoint.Key] = new StartupProbeRestoreState
            {
                OriginalLayoutName = string.IsNullOrWhiteSpace(endpoint.ActivePresetMultiviewLayoutName)
                    ? null
                    : endpoint.ActivePresetMultiviewLayoutName.Trim(),
                OriginalGeometrySignature = BuildActiveGeometrySignature(endpoint),
            };
        }

        private void TryMatchStartupProbeOriginalLayout(NhdBaseDevice endpoint, string capturedLayout)
        {
            if (endpoint == null || string.IsNullOrWhiteSpace(capturedLayout))
                return;

            if (!_startupProbeRestoreStates.TryGetValue(endpoint.Key, out var state))
                return;

            if (!string.IsNullOrWhiteSpace(state.MatchedOriginalLayoutName))
                return;

            if (string.IsNullOrWhiteSpace(state.OriginalGeometrySignature))
                return;

            var activeSignature = BuildActiveGeometrySignature(endpoint);
            if (string.IsNullOrWhiteSpace(activeSignature))
                return;

            if (!string.Equals(activeSignature, state.OriginalGeometrySignature, StringComparison.Ordinal))
                return;

            state.MatchedOriginalLayoutName = capturedLayout;
            Debug.LogMessage(
                Serilog.Events.LogEventLevel.Information,
                "$$$$$$$$$$ [{0}] Matched pre-probe receiver state endpoint='{1}' to layout='{2}'",
                _ctl,
                endpoint.Key,
                capturedLayout);
        }

        private void TryRestoreLayoutAfterProbe(NhdBaseDevice endpoint)
        {
            if (endpoint == null)
                return;

            if (!_startupProbeRestoreStates.TryGetValue(endpoint.Key, out var state))
                return;

            _startupProbeRestoreStates.Remove(endpoint.Key);

            var restoreLayout = !string.IsNullOrWhiteSpace(state.MatchedOriginalLayoutName)
                ? state.MatchedOriginalLayoutName
                : state.OriginalLayoutName;

            if (string.IsNullOrWhiteSpace(restoreLayout))
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Probe complete for endpoint='{1}' with no known pre-probe layout to restore",
                    _ctl,
                    endpoint.Key);
                return;
            }

            if (string.Equals(endpoint.ActivePresetMultiviewLayoutName, restoreLayout, StringComparison.OrdinalIgnoreCase))
                return;

            if (NhdApiCommandSender.TrySend(_ctl, $"mscene active {endpoint.ApiEndpointReference} {restoreLayout}"))
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Information,
                    "$$$$$$$$$$ [{0}] Restoring pre-probe layout endpoint='{1}', layout='{2}'",
                    _ctl,
                    endpoint.Key,
                    restoreLayout);
            }
        }

        private static string BuildActiveGeometrySignature(NhdBaseDevice endpoint)
        {
            if (endpoint == null || endpoint.ActiveTileCount <= 0)
                return null;

            var parts = new List<string>(endpoint.ActiveTileCount);
            for (var i = 1; i <= endpoint.ActiveTileCount; i++)
            {
                if (!endpoint.TryGetActiveMultiviewTile(i, out var tile) || tile == null)
                    return null;

                parts.Add(string.Format("{0}:{1}_{2}_{3}_{4}", tile.TileNumber, tile.X, tile.Y, tile.Width, tile.Height));
            }

            return parts.Count == 0
                ? null
                : string.Join("|", parts);
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
                || string.Equals(d.ApiEndpointReference, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Key, value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNullRouteReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return true;

            var value = reference.Trim();

            return value.Equals("null", StringComparison.OrdinalIgnoreCase)
                || value.Equals(NhdGlobalRouter.RouteOff, StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase)
                || value.Equals("none", StringComparison.OrdinalIgnoreCase);
        }
    }
}