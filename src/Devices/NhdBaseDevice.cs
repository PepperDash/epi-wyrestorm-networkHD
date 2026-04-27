using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Comms;
using PepperDash.Essentials.Plugin.Config;
using PepperDash.Essentials.Plugin.Enums;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	public sealed class NhdDeviceBoolStateChangedEventArgs : EventArgs
	{
		public NhdDeviceBoolStateChangedEventArgs(bool value)
		{
			Value = value;
		}

		public bool Value { get; }
	}

	public abstract class NhdBaseDevice : EssentialsDevice, IRoutingWithFeedback, ICommunicationMonitor
	{
		private const long DefaultCommunicationWarningTimeMs = 10000;
		private const long DefaultCommunicationErrorTimeMs = 30000;

		private NhdMultiStreamMode _multiStreamMode = NhdMultiStreamMode.Tile;
		private bool _online;
		private bool _inputSyncDetected;
		private readonly Dictionary<int, NhdMultiviewTileState> _activeMultiviewTiles = new Dictionary<int, NhdMultiviewTileState>();
		private readonly HashSet<string> _availablePresetMultiviewLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _presetLayoutGeometrySignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, PresetMultiviewAudioSetting> _presetLayoutAudioSettings = new Dictionary<string, PresetMultiviewAudioSetting>(StringComparer.OrdinalIgnoreCase);
		private string _activeMultiviewGeometrySignature;
		private NhdMultiviewAudioMode _activeMultiviewAudioMode = NhdMultiviewAudioMode.Unknown;
		private int? _activeMultiviewAudioWindow;
		private string _activeMultiviewAudioSeparateSourceReference;
		private string _activeMultiviewAudioSourceReference;
		private string _hdmiOutResolution;
		private int? _hdmiOutResolutionWidth;
		private int? _hdmiOutResolutionHeight;

		private sealed class PresetMultiviewAudioSetting
		{
			public NhdMultiviewAudioMode Mode { get; set; }
			public int? WindowReference { get; set; }
			public string SeparateSourceReference { get; set; }
		}

		protected NhdBaseDevice(string key, string name, NhdDeviceProperties config, string modelName)
			: base(key, name)
		{
			Config = config ?? new NhdDeviceProperties();
			ModelName = modelName;
			DeviceId = Config.DeviceId;
			IsOnline = new BoolFeedback("IsOnline", () => _online);
			InputSyncDetected = new BoolFeedback("InputSyncDetected", () => _inputSyncDetected);
		}

		protected NhdDeviceProperties Config { get; private set; }
		public StatusMonitorBase CommunicationMonitor { get; protected set; }
		public string ModelName { get; private set; }
		public int DeviceId { get; private set; }
		public string ConfiguredAlias => string.IsNullOrWhiteSpace(Config.Alias) ? null : Config.Alias.Trim();
		public string Hostname { get; private set; }
		public bool OnlineState => _online;
		public BoolFeedback IsOnline { get; private set; }
		public bool InputSyncDetectedState => _inputSyncDetected;
		public BoolFeedback InputSyncDetected { get; private set; }
		public int ActiveTileCount { get; private set; }
		public DateTime? MultiviewStateLastRefreshUtc { get; private set; }
		public string ActivePresetMultiviewLayoutName { get; private set; }
		public bool ActivePresetMultiviewLayoutInferred { get; private set; }
		public DateTime? ActivePresetMultiviewLayoutLastUpdateUtc { get; private set; }
		public string ActiveCustomMultiviewLayoutKey { get; private set; }
		public bool ActiveCustomMultiviewLayoutInferred { get; private set; }
		public DateTime? ActiveCustomMultiviewLayoutLastUpdateUtc { get; private set; }
		public NhdMultiviewAudioMode ActiveMultiviewAudioMode => _activeMultiviewAudioMode;
		public int? ActiveMultiviewAudioWindow => _activeMultiviewAudioWindow;
		public string ActiveMultiviewAudioSeparateSourceReference => _activeMultiviewAudioSeparateSourceReference;
		public string ActiveMultiviewAudioSourceReference => _activeMultiviewAudioSourceReference;
		public IReadOnlyList<NhdCustomMultiviewLayoutProperties> CustomMultiviewLayouts => Config.CustomMultiviewLayouts ?? (IReadOnlyList<NhdCustomMultiviewLayoutProperties>)Array.Empty<NhdCustomMultiviewLayoutProperties>();
		public IReadOnlyList<NhdMultiviewPresetProperties> MultiviewPresets => Config.MultiviewPresets ?? (IReadOnlyList<NhdMultiviewPresetProperties>)Array.Empty<NhdMultiviewPresetProperties>();
		public string HdmiOutResolution => _hdmiOutResolution;
		public int? HdmiOutResolutionWidth => _hdmiOutResolutionWidth;
		public int? HdmiOutResolutionHeight => _hdmiOutResolutionHeight;
		public IReadOnlyDictionary<int, NhdMultiviewTileState> ActiveMultiviewTiles => _activeMultiviewTiles;
		public IReadOnlyCollection<string> AvailablePresetMultiviewLayouts => _availablePresetMultiviewLayouts;
		public IReadOnlyDictionary<string, string> LearnedPresetLayoutGeometrySignatures => _presetLayoutGeometrySignatures;
		public string ApiEndpointReference =>
			ConfiguredAlias
			?? Hostname
			?? Key;
		public bool SupportsMultiview => MaxStreamCount > 1;
		public abstract bool IsTransmitter { get; }
		public abstract bool SupportsCec { get; }
		public abstract bool SupportsIr { get; }
		public abstract bool Supports232 { get; }

		/// <summary>
		/// Maximum number of simultaneous stream windows this device can decode. Defaults to 1.
		/// Override in subclasses that support multi-stream decoding.
		/// </summary>
		public virtual int MaxStreamCount => 1;

		/// <summary>
		/// Runtime multi-stream layout mode for devices that support more than one stream window.
		/// Setting this on single-stream devices throws an exception.
		/// </summary>
		public NhdMultiStreamMode MultiStreamMode
		{
			get => _multiStreamMode;
			set
			{
				if (MaxStreamCount <= 1)
					throw new InvalidOperationException($"{ModelName} does not support multi-stream mode changes");

				_multiStreamMode = value;
			}
		}

		public RoutingPortCollection<RoutingInputPort> InputPorts { get; } = new RoutingPortCollection<RoutingInputPort>();
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; } = new RoutingPortCollection<RoutingOutputPort>();
		public List<RouteSwitchDescriptor> CurrentRoutes { get; } = new List<RouteSwitchDescriptor>();
		public event RouteChangedEventHandler RouteChanged;
		public event EventHandler<NhdDeviceBoolStateChangedEventArgs> OnlineStateChanged;
		public event EventHandler<NhdDeviceBoolStateChangedEventArgs> InputSyncStateChanged;

		protected virtual bool AutoStartCommunicationMonitorInBase => true;

		protected virtual StatusMonitorBase BuildCommunicationMonitor()
		{
			return new NhdEndpointCommunicationMonitor(this, DefaultCommunicationWarningTimeMs, DefaultCommunicationErrorTimeMs);
		}

		protected void EnsureCommunicationMonitor()
		{
			if (CommunicationMonitor != null)
				return;

			CommunicationMonitor = BuildCommunicationMonitor();
		}

		protected override bool CustomActivate()
		{
			var result = base.CustomActivate();
			if (!result)
				return false;

			EnsureCommunicationMonitor();
			if (AutoStartCommunicationMonitorInBase)
			{
				CommunicationMonitor?.Start();
			}

			return true;
		}

		public override bool Deactivate()
		{
			CommunicationMonitor?.Stop();
			return base.Deactivate();
		}

		public void SetResolvedHostname(string hostname)
		{
			if (string.IsNullOrWhiteSpace(hostname))
				return;

			var value = hostname.Trim();
			if (string.Equals(Hostname, value, StringComparison.OrdinalIgnoreCase))
				return;

			Hostname = value;
			Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{DeviceKey}] Hostname resolved to '{1}' (alias='{2}')", this, Key, Hostname, ConfiguredAlias ?? "null");
		}

		public void SetOnlineState(bool isOnline)
		{
			if (_online == isOnline)
				return;

			_online = isOnline;
			if (!isOnline && SupportsMultiview)
			{
				ActiveTileCount = 0;
				MultiviewStateLastRefreshUtc = null;
				_activeMultiviewGeometrySignature = null;
				_activeMultiviewTiles.Clear();
				ActivePresetMultiviewLayoutName = null;
				ActivePresetMultiviewLayoutInferred = false;
				ActivePresetMultiviewLayoutLastUpdateUtc = null;
				ActiveCustomMultiviewLayoutKey = null;
				ActiveCustomMultiviewLayoutInferred = false;
				ActiveCustomMultiviewLayoutLastUpdateUtc = null;
				_activeMultiviewAudioMode = NhdMultiviewAudioMode.Unknown;
				_activeMultiviewAudioWindow = null;
				_activeMultiviewAudioSeparateSourceReference = null;
				_activeMultiviewAudioSourceReference = null;
			}

			if (!isOnline)
			{
				SetInputSyncState(false);
			}

			Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{DeviceKey}] Online state -> {1} (endpointRef='{2}')", this, Key, isOnline ? "ONLINE" : "OFFLINE", ApiEndpointReference);
			IsOnline.FireUpdate();
			OnlineStateChanged?.Invoke(this, new NhdDeviceBoolStateChangedEventArgs(isOnline));
		}

		public void SetInputSyncState(bool hasSync)
		{
			if (_inputSyncDetected == hasSync)
				return;

			_inputSyncDetected = hasSync;

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Input sync -> {1} (endpointRef='{2}')",
				this,
				Key,
				hasSync ? "DETECTED" : "LOST",
				ApiEndpointReference);

			InputSyncDetected.FireUpdate();
			InputSyncStateChanged?.Invoke(this, new NhdDeviceBoolStateChangedEventArgs(hasSync));
		}

		public bool TryGetHdmiOutResolutionDimensions(out int width, out int height)
		{
			width = 0;
			height = 0;

			if (!_hdmiOutResolutionWidth.HasValue || !_hdmiOutResolutionHeight.HasValue)
				return false;

			width = _hdmiOutResolutionWidth.Value;
			height = _hdmiOutResolutionHeight.Value;
			return true;
		}

		public void SetHdmiOutResolution(string resolution)
		{
			var normalized = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();

			int? parsedWidth = null;
			int? parsedHeight = null;

			if (!string.IsNullOrWhiteSpace(normalized))
			{
				var tokens = normalized.ToLowerInvariant().Split('x');
				if (tokens.Length == 2
					&& int.TryParse(tokens[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
					&& int.TryParse(tokens[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
					&& width > 0
					&& height > 0)
				{
					parsedWidth = width;
					parsedHeight = height;
					normalized = string.Format(CultureInfo.InvariantCulture, "{0}x{1}", width, height);
				}
			}

			if (string.Equals(_hdmiOutResolution, normalized, StringComparison.OrdinalIgnoreCase)
				&& _hdmiOutResolutionWidth == parsedWidth
				&& _hdmiOutResolutionHeight == parsedHeight)
			{
				return;
			}

			_hdmiOutResolution = normalized;
			_hdmiOutResolutionWidth = parsedWidth;
			_hdmiOutResolutionHeight = parsedHeight;

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] HDMI out resolution -> '{1}' (endpointRef='{2}')",
				this,
				Key,
				_hdmiOutResolution ?? "unknown",
				ApiEndpointReference);
		}

		public bool TryGetCustomMultiviewLayout(string layoutKey, out NhdCustomMultiviewLayoutProperties layout)
		{
			layout = null;

			if (string.IsNullOrWhiteSpace(layoutKey))
				return false;

			var normalizedKey = layoutKey.Trim();
			var layouts = Config.CustomMultiviewLayouts;
			if (layouts != null && layouts.Count > 0)
			{
				layout = layouts.FirstOrDefault(l =>
					l != null
					&& !string.IsNullOrWhiteSpace(l.Key)
					&& string.Equals(l.Key.Trim(), normalizedKey, StringComparison.OrdinalIgnoreCase));

				if (layout != null)
					return true;
			}

			var sharedLayouts = GetControllerCustomMultiviewLayouts();
			layout = sharedLayouts.FirstOrDefault(l =>
				l != null
				&& !string.IsNullOrWhiteSpace(l.Key)
				&& string.Equals(l.Key.Trim(), normalizedKey, StringComparison.OrdinalIgnoreCase));

			return layout != null;
		}

		public bool TryGetMultiviewPreset(string presetKey, out NhdMultiviewPresetProperties preset)
		{
			preset = null;

			if (string.IsNullOrWhiteSpace(presetKey))
				return false;

			var normalizedKey = presetKey.Trim();
			var presets = Config.MultiviewPresets;
			if (presets != null && presets.Count > 0)
			{
				preset = presets.FirstOrDefault(p =>
					p != null
					&& !string.IsNullOrWhiteSpace(p.Key)
					&& string.Equals(p.Key.Trim(), normalizedKey, StringComparison.OrdinalIgnoreCase));

				if (preset != null)
					return true;
			}

			var sharedPresets = GetControllerMultiviewPresets();
			preset = sharedPresets.FirstOrDefault(p =>
				p != null
				&& !string.IsNullOrWhiteSpace(p.Key)
				&& string.Equals(p.Key.Trim(), normalizedKey, StringComparison.OrdinalIgnoreCase));

			return preset != null;
		}

		private static IReadOnlyList<NhdCustomMultiviewLayoutProperties> GetControllerCustomMultiviewLayouts()
		{
			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.Config?.CustomMultiviewLayouts == null)
				return Array.Empty<NhdCustomMultiviewLayoutProperties>();

			return ctl.Config.CustomMultiviewLayouts;
		}

		private static IReadOnlyList<NhdMultiviewPresetProperties> GetControllerMultiviewPresets()
		{
			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.Config?.MultiviewPresets == null)
				return Array.Empty<NhdMultiviewPresetProperties>();

			return ctl.Config.MultiviewPresets;
		}

		public bool IsMultiviewStateFresh(TimeSpan maxAge)
		{
			if (!SupportsMultiview || !MultiviewStateLastRefreshUtc.HasValue)
				return false;

			return DateTime.UtcNow - MultiviewStateLastRefreshUtc.Value <= maxAge;
		}

		public void SetMultiviewRuntimeState(NhdMultiStreamMode mode, int activeTileCount)
		{
			var normalizedCount = Math.Max(0, Math.Min(activeTileCount, MaxStreamCount));
			var placeholderTiles = new List<NhdMultiviewTileState>(normalizedCount);

			for (var i = 1; i <= normalizedCount; i++)
			{
				placeholderTiles.Add(NhdMultiviewTileState.CreatePlaceholder(i));
			}

			SetMultiviewRuntimeState(mode, placeholderTiles);
		}

		public void SetMultiviewRuntimeState(NhdMultiStreamMode mode, IReadOnlyList<NhdMultiviewTileState> tiles)
		{
			if (!SupportsMultiview)
				return;

			var normalizedTiles = (tiles ?? Array.Empty<NhdMultiviewTileState>())
				.Where(t => t != null && t.TileNumber > 0 && t.TileNumber <= MaxStreamCount)
				.GroupBy(t => t.TileNumber)
				.Select(g => g.Last())
				.OrderBy(t => t.TileNumber)
				.ToList();

			var normalizedTileCount = normalizedTiles.Count;
			var changed = _multiStreamMode != mode || ActiveTileCount != normalizedTileCount;

			_multiStreamMode = mode;
			ActiveTileCount = normalizedTileCount;
			_activeMultiviewTiles.Clear();
			foreach (var tile in normalizedTiles)
			{
				_activeMultiviewTiles[tile.TileNumber] = tile;
			}

			_activeMultiviewGeometrySignature = BuildGeometrySignature(normalizedTiles);
			RefreshActiveMultiviewAudioSourceReference();

			MultiviewStateLastRefreshUtc = DateTime.UtcNow;

			if (changed)
			{
				Debug.LogMessage(
					Serilog.Events.LogEventLevel.Information,
					"$$$$$$$$$$ [{DeviceKey}] Multiview state updated: mode='{Mode}', activeTiles={ActiveTiles}, maxTiles={MaxTiles}",
					this,
					Key,
					mode,
					normalizedTileCount,
					MaxStreamCount);
			}
		}

		public bool TryGetActiveMultiviewTile(int tileReference, out NhdMultiviewTileState tile)
		{
			return _activeMultiviewTiles.TryGetValue(tileReference, out tile);
		}

		public void SetActiveMultiviewAudioWindow(int? windowReference)
		{
			SetActiveMultiviewAudioSelection(NhdMultiviewAudioMode.Window, windowReference, null);
		}

		public void SetActiveMultiviewAudioSeparateSource(string sourceReference)
		{
			SetActiveMultiviewAudioSelection(NhdMultiviewAudioMode.Separate, null, sourceReference);
		}

		public void SetPresetLayoutAudioWindow(string layoutName, int? windowReference)
		{
			if (!SupportsMultiview || string.IsNullOrWhiteSpace(layoutName))
				return;

			var normalizedLayout = layoutName.Trim();
			var normalizedWindow = NormalizeAudioWindow(windowReference);

			_presetLayoutAudioSettings[normalizedLayout] = new PresetMultiviewAudioSetting
			{
				Mode = NhdMultiviewAudioMode.Window,
				WindowReference = normalizedWindow,
				SeparateSourceReference = null,
			};

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Saved preset audio setting layout='{1}', mode='window', target='{2}'",
				this,
				Key,
				normalizedLayout,
				normalizedWindow.HasValue ? normalizedWindow.Value.ToString() : "null");
		}

		public void SetPresetLayoutAudioSeparateSource(string layoutName, string sourceReference)
		{
			if (!SupportsMultiview || string.IsNullOrWhiteSpace(layoutName))
				return;

			var normalizedLayout = layoutName.Trim();
			var normalizedSource = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();

			_presetLayoutAudioSettings[normalizedLayout] = new PresetMultiviewAudioSetting
			{
				Mode = NhdMultiviewAudioMode.Separate,
				WindowReference = null,
				SeparateSourceReference = normalizedSource,
			};

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Saved preset audio setting layout='{1}', mode='separate', target='{2}'",
				this,
				Key,
				normalizedLayout,
				normalizedSource ?? "null");
		}

		public void ApplyPresetLayoutAudioSetting(string layoutName)
		{
			if (!SupportsMultiview || string.IsNullOrWhiteSpace(layoutName))
				return;

			var normalizedLayout = layoutName.Trim();
			if (!_presetLayoutAudioSettings.TryGetValue(normalizedLayout, out var setting) || setting == null)
				return;

			SetActiveMultiviewAudioSelection(setting.Mode, setting.WindowReference, setting.SeparateSourceReference);
		}

		private void SetActiveMultiviewAudioSelection(NhdMultiviewAudioMode mode, int? windowReference, string sourceReference)
		{
			if (!SupportsMultiview)
				return;

			var normalizedWindow = windowReference;
			var normalizedSource = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();

			if (!normalizedWindow.HasValue || normalizedWindow.Value <= 0)
				normalizedWindow = null;
			else if (SupportsMultiview && normalizedWindow.Value > MaxStreamCount)
				normalizedWindow = null;

			if (_activeMultiviewAudioMode == mode
				&& _activeMultiviewAudioWindow == normalizedWindow
				&& string.Equals(_activeMultiviewAudioSeparateSourceReference, normalizedSource, StringComparison.OrdinalIgnoreCase))
				return;

			_activeMultiviewAudioMode = mode;
			_activeMultiviewAudioWindow = normalizedWindow;
			_activeMultiviewAudioSeparateSourceReference = mode == NhdMultiviewAudioMode.Separate ? normalizedSource : null;
			RefreshActiveMultiviewAudioSourceReference();

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Active multiview audio setting mode='{1}', window='{2}', separateSource='{3}'",
				this,
				Key,
				_activeMultiviewAudioMode,
				_activeMultiviewAudioWindow.HasValue ? _activeMultiviewAudioWindow.Value.ToString() : "null",
				_activeMultiviewAudioSeparateSourceReference ?? "null");
		}

		private int? NormalizeAudioWindow(int? windowReference)
		{
			var normalizedWindow = windowReference;

			if (!normalizedWindow.HasValue || normalizedWindow.Value <= 0)
				return null;

			if (SupportsMultiview && normalizedWindow.Value > MaxStreamCount)
				return null;

			return normalizedWindow;
		}

		private void RefreshActiveMultiviewAudioSourceReference()
		{
			var newSourceReference = default(string);

			if (_activeMultiviewAudioMode == NhdMultiviewAudioMode.Separate)
			{
				newSourceReference = _activeMultiviewAudioSeparateSourceReference;
			}
			else if (_activeMultiviewAudioMode == NhdMultiviewAudioMode.Window
				&& _activeMultiviewAudioWindow.HasValue
				&& _activeMultiviewTiles.TryGetValue(_activeMultiviewAudioWindow.Value, out var tile)
				&& tile != null
				&& !string.IsNullOrWhiteSpace(tile.SourceReference))
			{
				newSourceReference = tile.SourceReference;
			}

			if (string.Equals(_activeMultiviewAudioSourceReference, newSourceReference, StringComparison.OrdinalIgnoreCase))
				return;

			_activeMultiviewAudioSourceReference = newSourceReference;

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Active multiview audio source set to '{1}' (mode='{2}', window='{3}', separateSource='{4}')",
				this,
				Key,
				_activeMultiviewAudioSourceReference ?? "null",
				_activeMultiviewAudioMode,
				_activeMultiviewAudioWindow.HasValue ? _activeMultiviewAudioWindow.Value.ToString() : "null",
				_activeMultiviewAudioSeparateSourceReference ?? "null");
		}

		public void SetAvailablePresetMultiviewLayouts(IEnumerable<string> layoutNames)
		{
			if (!SupportsMultiview)
				return;

			_availablePresetMultiviewLayouts.Clear();

			foreach (var layout in layoutNames ?? Array.Empty<string>())
			{
				if (string.IsNullOrWhiteSpace(layout))
					continue;

				_availablePresetMultiviewLayouts.Add(layout.Trim());
			}

			var staleSignatures = _presetLayoutGeometrySignatures
				.Keys
				.Where(layout => !_availablePresetMultiviewLayouts.Contains(layout))
				.ToList();

			foreach (var staleLayout in staleSignatures)
			{
				_presetLayoutGeometrySignatures.Remove(staleLayout);
				_presetLayoutAudioSettings.Remove(staleLayout);
			}

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Available multiview preset layouts updated: count={1}",
				this,
				Key,
				_availablePresetMultiviewLayouts.Count);
		}

		public bool IsKnownPresetMultiviewLayout(string layoutName)
		{
			if (string.IsNullOrWhiteSpace(layoutName))
				return false;

			return _availablePresetMultiviewLayouts.Contains(layoutName.Trim());
		}

		public bool TryCaptureActiveLayoutGeometry(string layoutName)
		{
			if (!SupportsMultiview)
				return false;

			if (string.IsNullOrWhiteSpace(layoutName))
				return false;

			if (string.IsNullOrWhiteSpace(_activeMultiviewGeometrySignature))
				return false;

			var normalized = layoutName.Trim();
			_presetLayoutGeometrySignatures[normalized] = _activeMultiviewGeometrySignature;

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Captured geometry signature for layout '{1}'",
				this,
				Key,
				normalized);

			return true;
		}

		public void ClearLearnedPresetLayoutGeometrySignatures()
		{
			if (!SupportsMultiview)
				return;

			_presetLayoutGeometrySignatures.Clear();

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Cleared learned multiview preset layout geometry signatures",
				this,
				Key);
		}

		public bool TryIdentifyPresetLayoutByActiveGeometry(out string layoutName)
		{
			layoutName = null;

			if (!SupportsMultiview)
				return false;

			if (string.IsNullOrWhiteSpace(_activeMultiviewGeometrySignature))
				return false;

			var exactMatches = _presetLayoutGeometrySignatures
				.Where(kvp => string.Equals(kvp.Value, _activeMultiviewGeometrySignature, StringComparison.Ordinal))
				.Select(kvp => kvp.Key)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (exactMatches.Count == 1)
			{
				layoutName = exactMatches[0];
				return true;
			}

			if (_availablePresetMultiviewLayouts.Count == 0)
				return false;

			var shapeMatches = _availablePresetMultiviewLayouts
				.Where(layout => TryInferPresetLayoutShape(layout, out var tiles, out var mode)
					&& tiles == ActiveTileCount
					&& mode == MultiStreamMode)
				.ToList();

			if (shapeMatches.Count == 1)
			{
				layoutName = shapeMatches[0];
				return true;
			}

			return false;
		}

		public bool TryIdentifyCustomLayoutByActiveGeometry(out string layoutKey)
		{
			layoutKey = null;

			if (!SupportsMultiview)
				return false;

			if (string.IsNullOrWhiteSpace(_activeMultiviewGeometrySignature))
				return false;

			var outputWidth = default(int);
			var outputHeight = default(int);
			var hasOutputDimensions = TryGetHdmiOutResolutionDimensions(out outputWidth, out outputHeight);

			var matches = GetAllCustomMultiviewLayoutsByPrecedence()
				.Where(layout => layout != null
					&& !string.IsNullOrWhiteSpace(layout.Key)
					&& layout.Mode == MultiStreamMode)
				.Where(layout =>
				{
					var signature = BuildCustomLayoutGeometrySignature(
						layout,
						hasOutputDimensions ? outputWidth : 0,
						hasOutputDimensions ? outputHeight : 0);
					return !string.IsNullOrWhiteSpace(signature)
						&& string.Equals(signature, _activeMultiviewGeometrySignature, StringComparison.Ordinal);
				})
				.Select(layout => layout.Key.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (matches.Count != 1)
				return false;

			layoutKey = matches[0];
			return true;
		}

		public static bool TryInferPresetLayoutShape(string layoutName, out int tileCount, out NhdMultiStreamMode mode)
		{
			tileCount = 0;
			mode = NhdMultiStreamMode.Tile;

			if (string.IsNullOrWhiteSpace(layoutName))
				return false;

			var normalized = layoutName.Trim();
			var dashIndex = normalized.IndexOf('-');
			if (dashIndex <= 0)
				return false;

			if (!int.TryParse(normalized.Substring(0, dashIndex), out tileCount))
				return false;

			if (tileCount <= 0)
				return false;

			// NHD built-ins use 2-2 as the overlay preset. Other presets are tile mode.
			mode = normalized.Equals("2-2", StringComparison.OrdinalIgnoreCase)
				? NhdMultiStreamMode.Overlay
				: NhdMultiStreamMode.Tile;

			return true;
		}

		public void SetActivePresetMultiviewLayout(string layoutName, bool inferred = false)
		{
			if (!SupportsMultiview)
				return;

			var normalized = string.IsNullOrWhiteSpace(layoutName) ? null : layoutName.Trim();
			if (
				string.Equals(ActivePresetMultiviewLayoutName, normalized, StringComparison.OrdinalIgnoreCase)
				&& ActivePresetMultiviewLayoutInferred == inferred)
				return;

			ActivePresetMultiviewLayoutName = normalized;
			ActivePresetMultiviewLayoutInferred = inferred;
			ActivePresetMultiviewLayoutLastUpdateUtc = DateTime.UtcNow;
			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Active multiview preset layout set to '{Layout}' (inferred={Inferred})",
				this,
				Key,
				ActivePresetMultiviewLayoutName ?? "null",
				ActivePresetMultiviewLayoutInferred);
		}

		public void SetActiveCustomMultiviewLayout(string layoutKey, bool inferred = false)
		{
			if (!SupportsMultiview)
				return;

			var normalized = string.IsNullOrWhiteSpace(layoutKey) ? null : layoutKey.Trim();
			if (string.Equals(ActiveCustomMultiviewLayoutKey, normalized, StringComparison.OrdinalIgnoreCase)
				&& ActiveCustomMultiviewLayoutInferred == inferred)
			{
				return;
			}

			ActiveCustomMultiviewLayoutKey = normalized;
			ActiveCustomMultiviewLayoutInferred = inferred;
			ActiveCustomMultiviewLayoutLastUpdateUtc = DateTime.UtcNow;

			Debug.LogMessage(
				Serilog.Events.LogEventLevel.Information,
				"$$$$$$$$$$ [{DeviceKey}] Active multiview custom layout set to '{LayoutKey}' (inferred={Inferred})",
				this,
				Key,
				ActiveCustomMultiviewLayoutKey ?? "null",
				ActiveCustomMultiviewLayoutInferred);
		}

		private IEnumerable<NhdCustomMultiviewLayoutProperties> GetAllCustomMultiviewLayoutsByPrecedence()
		{
			var yieldedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var localLayout in Config.CustomMultiviewLayouts ?? new List<NhdCustomMultiviewLayoutProperties>())
			{
				if (localLayout == null || string.IsNullOrWhiteSpace(localLayout.Key))
					continue;

				var normalizedKey = localLayout.Key.Trim();
				if (!yieldedKeys.Add(normalizedKey))
					continue;

				yield return localLayout;
			}

			foreach (var sharedLayout in GetControllerCustomMultiviewLayouts())
			{
				if (sharedLayout == null || string.IsNullOrWhiteSpace(sharedLayout.Key))
					continue;

				var normalizedKey = sharedLayout.Key.Trim();
				if (!yieldedKeys.Add(normalizedKey))
					continue;

				yield return sharedLayout;
			}
		}

		private static string BuildCustomLayoutGeometrySignature(NhdCustomMultiviewLayoutProperties layout, int outputWidth, int outputHeight)
		{
			if (layout == null)
				return null;

			var windows = (layout.Windows ?? new List<NhdCustomMultiviewWindowProperties>())
				.Where(window => window != null && window.WindowReference > 0)
				.OrderBy(window => window.WindowReference)
				.ToList();

			if (windows.Count == 0)
				return null;

			var canvasWidth = layout.CanvasWidth > 0 ? layout.CanvasWidth : 1920;
			var canvasHeight = layout.CanvasHeight > 0 ? layout.CanvasHeight : 1080;

			if (outputWidth <= 0)
				outputWidth = canvasWidth;

			if (outputHeight <= 0)
				outputHeight = canvasHeight;

			var geometryTiles = new List<NhdMultiviewTileState>();
			var tileNumber = 1;
			foreach (var window in windows)
			{
				if (window.Width <= 0 || window.Height <= 0)
					return null;

				var scaledX = ScaleCoordinate(window.X, canvasWidth, outputWidth);
				var scaledY = ScaleCoordinate(window.Y, canvasHeight, outputHeight);
				var scaledWidth = ScaleLength(window.Width, canvasWidth, outputWidth);
				var scaledHeight = ScaleLength(window.Height, canvasHeight, outputHeight);

				if (scaledX >= outputWidth)
					scaledX = Math.Max(0, outputWidth - 1);

				if (scaledY >= outputHeight)
					scaledY = Math.Max(0, outputHeight - 1);

				if (scaledX + scaledWidth > outputWidth)
					scaledWidth = Math.Max(1, outputWidth - scaledX);

				if (scaledY + scaledHeight > outputHeight)
					scaledHeight = Math.Max(1, outputHeight - scaledY);

				geometryTiles.Add(new NhdMultiviewTileState(tileNumber, null, scaledX, scaledY, scaledWidth, scaledHeight, null));
				tileNumber++;
			}

			return BuildGeometrySignature(geometryTiles);
		}

		private static int ScaleCoordinate(int value, int sourceSpan, int targetSpan)
		{
			if (sourceSpan <= 0 || targetSpan <= 0)
				return 0;

			if (value <= 0)
				return 0;

			var scaled = (double)value * targetSpan / sourceSpan;
			return Math.Max(0, (int)Math.Round(scaled, MidpointRounding.AwayFromZero));
		}

		private static int ScaleLength(int value, int sourceSpan, int targetSpan)
		{
			if (sourceSpan <= 0 || targetSpan <= 0)
				return 1;

			if (value <= 0)
				return 1;

			var scaled = (double)value * targetSpan / sourceSpan;
			return Math.Max(1, (int)Math.Round(scaled, MidpointRounding.AwayFromZero));
		}

		public bool ReprobeMultiviewLayouts()
		{
			if (!SupportsMultiview || IsTransmitter)
			{
				Debug.LogError("[{0}] Endpoint does not support multiview preset reprobe", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for multiview preset reprobe", Key);
				return false;
			}

			return ctl.SessionManager.TryReprobeAndLearnMultiviewLayouts(this, this);
		}

		public bool ApplyCustomMVLayout(string layoutKey)
		{
			if (!SupportsMultiview || IsTransmitter)
			{
				Debug.LogError("[{0}] Endpoint does not support multiview custom layout geometry", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for custom multiview geometry apply", Key);
				return false;
			}

			return ctl.SessionManager.TryApplyCustomMVLayout(this, this, layoutKey);
		}

		public bool ApplyCustomMVLayoutWithSources(string layoutKey, IDictionary<int, string> sourceReferencesByWindow)
		{
			if (!SupportsMultiview || IsTransmitter)
			{
				Debug.LogError("[{0}] Endpoint does not support multiview custom layout content apply", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for custom multiview content apply", Key);
				return false;
			}

			return ctl.SessionManager.TryApplyCustomMVLayoutWithSources(this, this, layoutKey, sourceReferencesByWindow);
		}

		public bool ApplyMVPreset(string presetKey)
		{
			if (!SupportsMultiview || IsTransmitter)
			{
				Debug.LogError("[{0}] Endpoint does not support multiview preset apply", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for multiview preset apply", Key);
				return false;
			}

			return ctl.SessionManager.TryApplyMVPreset(this, this, presetKey);
		}

		public bool FullscreenMultiviewTile(int sourceTileReference)
		{
			if (!SupportsMultiview || IsTransmitter)
			{
				Debug.LogError("[{0}] Endpoint does not support multiview fullscreen", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for multiview fullscreen", Key);
				return false;
			}

			return ctl.SessionManager.TryFullscreenMultiviewTile(this, this, sourceTileReference);
		}

		public bool ReturnFromMultiviewFullscreen()
		{
			if (!SupportsMultiview || IsTransmitter)
			{
				Debug.LogError("[{0}] Endpoint does not support multiview fullscreen return", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for multiview fullscreen return", Key);
				return false;
			}

			return ctl.SessionManager.TryReturnFromMultiviewFullscreen(this, this);
		}

		public bool TryGetMultiviewFullscreenReturnLayout(out string layoutName)
		{
			layoutName = null;

			if (!SupportsMultiview || IsTransmitter)
				return false;

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			return ctl?.SessionManager != null
				&& ctl.SessionManager.TryGetMultiviewFullscreenReturnLayout(this, out layoutName);
		}

		private static string BuildGeometrySignature(IEnumerable<NhdMultiviewTileState> tiles)
		{
			var normalizedTiles = (tiles ?? Array.Empty<NhdMultiviewTileState>())
				.Where(t => t != null)
				.OrderBy(t => t.TileNumber)
				.Select(t => string.Format("{0}:{1}_{2}_{3}_{4}", t.TileNumber, t.X, t.Y, t.Width, t.Height))
				.ToList();

			return normalizedTiles.Count == 0
				? null
				: string.Join("|", normalizedTiles);
		}

		public void ExecuteSwitch(object inputSelector, object outputSelector, eRoutingSignalType signalType)
		{
			var inputPort = inputSelector as RoutingInputPort;
			if (inputPort == null)
			{
				return;
			}

			var outputPort = outputSelector as RoutingOutputPort;

			var route = outputPort == null
				? new RouteSwitchDescriptor(inputPort)
				: new RouteSwitchDescriptor(outputPort, inputPort);

			CurrentRoutes.Clear();
			CurrentRoutes.Add(route);

			var callback = RouteChanged;
			if (callback != null)
			{
				callback(this, route);
			}
		}

		// Video ports
		protected void AddHdmiInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddHdmiOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddUsbcInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddUsbcOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddStreamInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.Stream, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Streaming, NhdPortKeys.Stream, this));

		protected void AddStreamOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.Stream, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Streaming, NhdPortKeys.Stream, this));

		// Audio ports
		protected void AddHdmiAudioInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddHdmiAudioOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddUsbcAudioInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddUsbcAudioOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddAnalogAudioInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.AnalogAudioInput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.AnalogAudioInput, this));

		protected void AddAnalogAudioOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.AnalogAudioOutput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.AnalogAudioOutput, this));

		protected void AddDanteInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.DanteInput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.DanteInput, this));

		protected void AddDanteOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.DanteOutput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.DanteOutput, this));

		// Control ports
		protected void AddUsbInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.UsbInput, NhdRoutingSignalTypes.UsbInput, eRoutingPortConnectionType.UsbC, NhdPortKeys.UsbInput, this));

		protected void AddUsbOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.UsbOutput, NhdRoutingSignalTypes.UsbOutput, eRoutingPortConnectionType.UsbC, NhdPortKeys.UsbOutput, this));

		/// <summary>
		/// Adds IR routing port(s) based on the configured routing mode.
		/// ControlSystem: adds irIn — Crestron side, data enters NHD here.
		/// Device: adds irOut — end-device side, data exits NHD here.
		/// NotRoutable or null: no routing ports; use SendIrData directly.
		/// </summary>
		protected void AddIrPorts(NhdComPortRoutingMode? mode)
		{
			switch (mode ?? NhdComPortRoutingMode.NotRoutable)
			{
				case NhdComPortRoutingMode.ControlSystem:
					InputPorts.Add(new RoutingInputPort(NhdPortKeys.IrInput, NhdRoutingSignalTypes.Ir, eRoutingPortConnectionType.None, NhdPortKeys.IrInput, this));
					break;
				case NhdComPortRoutingMode.Device:
					OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.IrOutput, NhdRoutingSignalTypes.Ir, eRoutingPortConnectionType.None, NhdPortKeys.IrOutput, this));
					break;
				case NhdComPortRoutingMode.NotRoutable:
				default:
					break;
			}
		}

		/// <summary>
		/// Adds RS-232 routing port(s) based on the configured routing mode.
		/// ControlSystem: adds rs232In — Crestron side, data enters NHD here.
		/// Device: adds rs232Out — end-device side, data exits NHD here.
		/// NotRoutable or null: no routing ports; use Send232Command directly.
		/// </summary>
		protected void AddRs232Ports(NhdComPortRoutingMode? mode)
		{
			switch (mode ?? NhdComPortRoutingMode.NotRoutable)
			{
				case NhdComPortRoutingMode.ControlSystem:
					InputPorts.Add(new RoutingInputPort(NhdPortKeys.Rs232Input, NhdRoutingSignalTypes.Serial, eRoutingPortConnectionType.None, NhdPortKeys.Rs232Input, this));
					break;
				case NhdComPortRoutingMode.Device:
					OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.Rs232Output, NhdRoutingSignalTypes.Serial, eRoutingPortConnectionType.None, NhdPortKeys.Rs232Output, this));
					break;
				case NhdComPortRoutingMode.NotRoutable:
				default:
					break;
			}
		}

		/// <summary>
		/// Sends a power proxy command. Supported if device supports CEC or RS-232.
		/// </summary>
		/// <param name="state">"on" or "off"</param>
		public virtual void SendPowerProxyCommand(string state)
		{
			if (!SupportsCec && !Supports232)
				throw new NotSupportedException($"{ModelName} does not support CEC or RS-232 power proxy");

			if (string.IsNullOrWhiteSpace(state))
				throw new ArgumentException("state must be 'on' or 'off'", nameof(state));

			var normalized = state.Trim().ToLowerInvariant();
			if (normalized != "on" && normalized != "off")
				throw new ArgumentException("state must be 'on' or 'off'", nameof(state));

			NhdApiCommandSender.TrySend(this, $"config set device sinkpower {normalized} {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends a CEC command.
		/// </summary>
		/// <param name="command">"onetouchdisplay" or "standby"</param>
		public virtual void SendCecCommand(string command)
		{
			if (!SupportsCec)
				throw new NotSupportedException($"{ModelName} does not support CEC");

			if (string.IsNullOrWhiteSpace(command))
				throw new ArgumentException("command must be 'onetouchplay' or 'standby'", nameof(command));

			var normalized = command.Trim().ToLowerInvariant();
			if (normalized != "onetouchplay" && normalized != "standby")
				throw new ArgumentException("command must be 'onetouchplay' or 'standby'", nameof(command));

			NhdApiCommandSender.TrySend(this, $"config set device cec {normalized} {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends a custom CEC command with raw data.
		/// </summary>
		/// <param name="command">Must be "custom"</param>
		/// <param name="data">Raw CEC data</param>
		public virtual void SendCecCommand(string command, string data)
		{
			if (!SupportsCec)
				throw new NotSupportedException($"{ModelName} does not support CEC");
			if (!command.Equals("custom", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Only 'custom' is valid for the overload with data", nameof(command));
			if (string.IsNullOrWhiteSpace(data))
				throw new ArgumentException("CEC data cannot be empty", nameof(data));
			if (data.Contains("\""))
				throw new ArgumentException("CEC data cannot contain quote characters", nameof(data));

			NhdApiCommandSender.TrySend(this, $"cec \"{data.Trim()}\" {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends an IR command.
		/// </summary>
		/// <param name="data">IR data payload</param>
		public virtual void SendIrData(string data)
		{
			if (!SupportsIr)
				throw new NotSupportedException($"{ModelName} does not support IR");
			if (string.IsNullOrWhiteSpace(data))
				throw new ArgumentException("IR data cannot be empty", nameof(data));
			if (data.Contains("\""))
				throw new ArgumentException("IR data cannot contain quote characters", nameof(data));

			NhdApiCommandSender.TrySend(this, $"infrared \"{data.Trim()}\" {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends an RS-232 command. Comm params are read from config.Rs232.
		/// </summary>
		/// <param name="data">Data string to send</param>
		public virtual void Send232Command(string data)
		{
			if (!Supports232)
				throw new NotSupportedException($"{ModelName} does not support RS-232");
			if (string.IsNullOrWhiteSpace(data))
				throw new ArgumentException("RS-232 data cannot be empty", nameof(data));
			if (data.Contains("\""))
				throw new ArgumentException("RS-232 data cannot contain quote characters", nameof(data));

			var serial = Config.Rs232 ?? new PepperDash.Essentials.Plugin.Config.Nhd232Properties();
			var parity = GetParityCode(serial.Parity);
			var baud = (int)serial.BaudRate;
			var bits = (int)serial.DataBits;
			var stop = (int)serial.StopBits;

			var command =
				$"serial -b {baud}-{bits}{parity}{stop} -r {(serial.AppendCr ? "on" : "off")} -n {(serial.AppendLf ? "on" : "off")} -h {(serial.SendAsHex ? "on" : "off")} \"{data.Trim()}\" {ApiEndpointReference}";

			NhdApiCommandSender.TrySend(this, command);
		}

		private static string GetParityCode(Parity parity)
		{
			switch (parity)
			{
				case Parity.Even:
					return "e";
				case Parity.Odd:
					return "o";
				case Parity.None:
				default:
					return "n";
			}
		}
	}
}
