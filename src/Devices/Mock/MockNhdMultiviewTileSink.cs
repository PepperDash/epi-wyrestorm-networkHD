using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Mock
{
	/// <summary>
	/// Mock per-tile sink for <see cref="MockNhdRx"/>. Mirrors the routing-relevant surface of the
	/// real <see cref="Routing.NhdMultiviewTileSink"/> - same <see cref="IRoutingSinkWithFeedback"/>
	/// contract, same <c>"{parentKey}-tile{N}"</c> key convention - but never issues any hardware
	/// command. Both <see cref="SetCurrentSource"/> and <see cref="UpdateCurrentSourceState"/> only
	/// update bookkeeping and fire <see cref="CurrentSourcesChanged"/>/<see cref="InputChanged"/>,
	/// which is all that's needed to exercise the routing dev tools' live feedback end to end.
	/// </summary>
	public class MockNhdMultiviewTileSink : EssentialsDevice, IRoutingSinkWithFeedback
	{
		/// <summary>
		/// The mock decoder (e.g. <see cref="MockNhdRx"/>) this tile belongs to.
		/// </summary>
		public IKeyed ParentDevice { get; }

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

		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdMultiviewTileSink"/> class.
		/// </summary>
		public MockNhdMultiviewTileSink(IKeyed parentDevice, int tileNumber)
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

		private static string BuildKey(IKeyed parentDevice, int tileNumber)
			=> $"{parentDevice?.Key}-tile{tileNumber}";

		private static string BuildName(IKeyed parentDevice, int tileNumber)
			=> $"{(parentDevice as IKeyName)?.Name ?? parentDevice?.Key} Tile {tileNumber}";

		/// <summary>
		/// No hardware to drive here - a tile's actual source assignment is applied via
		/// <see cref="SetCurrentSource"/> or <see cref="UpdateCurrentSourceState"/>.
		/// </summary>
		public void ExecuteSwitch(object inputSelector)
		{
			Debug.LogMessage(Serilog.Events.LogEventLevel.Verbose, "[{DeviceKey}] Tile {TileNumber} switch executed (no-op, mock device)", this, Key, TileNumber);
		}

		/// <inheritdoc />
		public void SetCurrentSource(eRoutingSignalType signalType, IRoutingSource sourceDevice)
			=> UpdateCurrentSourceState(signalType, sourceDevice);

		/// <summary>
		/// Updates CurrentSources/CurrentSourceKeys bookkeeping and fires
		/// CurrentSourcesChanged/InputChanged. Used by <see cref="MockNhdRx.ApplyDynamicLayout"/> to
		/// keep this tile-sink's feedback in sync after a bulk layout apply.
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
	}
}
