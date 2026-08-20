using System;
using System.Collections.Generic;
using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Routing;

/// <summary>
/// Represents a single tile/window of a multiview-capable NetworkHD decoder's active layout,
/// modeled as its own Essentials routing sink (<see cref="IRoutingSinkWithFeedback"/>).
/// Registered with <see cref="DeviceManager"/> (key: <c>"{parentKey}-tile{N}"</c>), so it can be
/// targeted directly by string-key routing APIs (tie lines / <c>IRunDirectRouteAction</c>), in
/// addition to being reachable programmatically via the parent decoder (which implements
/// <see cref="IRoutingSinkWithLayouts"/>) by indexing into its <c>WindowTileSinks</c> dictionary by
/// this tile's <see cref="TileNumber"/>. Also driven in bulk by the parent decoder's dynamic
/// multiview layout APIs. One instance is created per potential window slot (see
/// <see cref="NhdBaseDevice.ConfiguredMaxTileCount"/>) when the parent decoder is constructed.
/// </summary>
public class NhdMultiviewTileSink : EssentialsDevice, IRoutingSinkWithFeedback
{
	/// <summary>
	/// The multiview-capable decoder (e.g. Nhd150Rx) this tile belongs to.
	/// </summary>
	public NhdBaseDevice ParentDevice { get; }

	/// <summary>
	/// The 1-based tile/window number within the parent decoder's active layout.
	/// </summary>
	public int TileNumber { get; }

	/// <inheritdoc />
	public RoutingPortCollection<RoutingInputPort> InputPorts { get; } = new RoutingPortCollection<RoutingInputPort>();

	/// <inheritdoc />
	public Dictionary<eRoutingSignalType, IRoutingSource> CurrentSources { get; } = new Dictionary<eRoutingSignalType, IRoutingSource>
	{
		{ eRoutingSignalType.Audio, null },
		{ eRoutingSignalType.Video, null },
	};

	/// <inheritdoc />
	public Dictionary<eRoutingSignalType, string> CurrentSourceKeys { get; } = new Dictionary<eRoutingSignalType, string>
	{
		{ eRoutingSignalType.Audio, string.Empty },
		{ eRoutingSignalType.Video, string.Empty },
	};

	/// <inheritdoc />
	public event EventHandler<CurrentSourcesChangedEventArgs> CurrentSourcesChanged;

	/// <inheritdoc />
	public event InputChangedEventHandler InputChanged;

	/// <inheritdoc />
	public RoutingInputPort CurrentInputPort => InputPorts.Count > 0 ? InputPorts[0] : null;

	public NhdMultiviewTileSink(NhdBaseDevice parentDevice, int tileNumber)
		: base(BuildKey(parentDevice, tileNumber), BuildName(parentDevice, tileNumber))
	{
		ParentDevice = parentDevice ?? throw new ArgumentNullException(nameof(parentDevice));
		TileNumber = tileNumber;

		InputPorts.Add(new RoutingInputPort(
			"tileInput",
			eRoutingSignalType.AudioVideo,
			eRoutingPortConnectionType.None,
			"tileInput",
			this));
	}

	private static string BuildKey(NhdBaseDevice parentDevice, int tileNumber)
		=> $"{parentDevice?.Key}-tile{tileNumber}";

	private static string BuildName(NhdBaseDevice parentDevice, int tileNumber)
		=> $"{parentDevice?.Name} Tile {tileNumber}";

	/// <summary>
	/// This tile has only one logical input (whatever source is currently routed to it), so there's
	/// no local input-selection action to take here. The actual hardware routing happens in
	/// <see cref="SetCurrentSource"/>, once the resolved source device is known.
	/// </summary>
	public void ExecuteSwitch(object inputSelector)
	{
		Debug.LogMessage(Serilog.Events.LogEventLevel.Verbose, "[{DeviceKey}] Tile {TileNumber} on '{ParentKey}' switch executed", this, Key, TileNumber, ParentDevice.Key);
	}

	/// <inheritdoc />
	public void SetCurrentSource(eRoutingSignalType signalType, IRoutingSource sourceDevice)
	{
		UpdateCurrentSourceState(signalType, sourceDevice);

		if (sourceDevice != null
			&& (signalType.HasFlag(eRoutingSignalType.Video) || signalType.HasFlag(eRoutingSignalType.AudioVideo)))
		{
			RouteSourceIntoTile(sourceDevice);
		}
	}

	/// <summary>
	/// Updates CurrentSources/CurrentSourceKeys bookkeeping (and fires CurrentSourcesChanged) without
	/// sending any hardware command. Used by <see cref="Nhd150Rx.ApplyDynamicLayout"/> to keep this
	/// tile-sink's feedback in sync after a bulk layout apply, which already sent its own single
	/// combined hardware command - routing this tile individually as well would be redundant and
	/// could conflict with the guarded/queued single-tile route logic. <see cref="SetCurrentSource"/>
	/// (used by ad-hoc/manual single-tile routes) calls this internally in addition to actually
	/// routing the source into the tile.
	/// </summary>
	public void UpdateCurrentSourceState(eRoutingSignalType signalType, IRoutingSource sourceDevice)
	{
		foreach (eRoutingSignalType type in Enum.GetValues(typeof(eRoutingSignalType)))
		{
			var flagValue = Convert.ToInt32(type);
			// Skip 0 and non-power-of-two combined flags (e.g. AudioVideo) - only update the
			// individual Audio/Video entries.
			if (flagValue == 0 || (flagValue & (flagValue - 1)) != 0)
				continue;

			if (!signalType.HasFlag(type))
				continue;

			CurrentSources.TryGetValue(type, out var previousSource);
			UpdateCurrentSource(type, previousSource, sourceDevice);
		}
	}

	private void UpdateCurrentSource(eRoutingSignalType signalType, IRoutingSource previousSource, IRoutingSource sourceDevice)
	{
		CurrentSources[signalType] = sourceDevice;
		CurrentSourceKeys[signalType] = sourceDevice?.Key ?? string.Empty;

		CurrentSourcesChanged?.Invoke(this, new CurrentSourcesChangedEventArgs(signalType, previousSource, sourceDevice));
		InputChanged?.Invoke(this, CurrentInputPort);
	}

	/// <summary>
	/// Routes the resolved source device into this tile of the parent decoder's currently active
	/// layout, reusing the existing guarded/queued tile-route logic on the NHD-CTL session manager.
	/// </summary>
	private void RouteSourceIntoTile(IRoutingSource sourceDevice)
	{
		if (sourceDevice is not NhdBaseDevice txDevice)
		{
			Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "[{DeviceKey}] Source '{SourceKey}' is not an NhdBaseDevice, cannot route into tile {TileNumber}", this, Key, sourceDevice.Key, TileNumber);
			return;
		}

		var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
		if (ctl?.SessionManager == null)
		{
			Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "[{DeviceKey}] NHD-CTL session manager is not available for tile routing", this, Key);
			return;
		}

		ctl.SessionManager.RouteMVTileGuarded(this, txDevice, ParentDevice, null, TileNumber);
	}
}
