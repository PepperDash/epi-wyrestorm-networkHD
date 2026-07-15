using System.Collections.Generic;
using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	/// <summary>
	/// NHD-150-RX multiview decoder. Also implements <see cref="IRoutingSource"/> since its
	/// HdmiOutput1 port is effectively the composited output of whatever layout is currently active
	/// - it may be routed on (via tie lines / further midpoints) to its eventual display
	/// destination, rather than assumed to be a fixed direct physical connection. Also implements
	/// <see cref="IHasDynamicMultiviewLayout"/> (defined in Essentials Core) so consumers like a room
	/// plugin can drive dynamic layouts without taking a compile-time dependency on this plugin.
	/// Also implements <see cref="IRoutingSinkWithLayouts"/> so its per-tile sinks can be reached
	/// programmatically via the parent device, in addition to being individually registered with
	/// <see cref="DeviceManager"/> - see <see cref="WindowTileSinks"/> for details.
	/// </summary>
	public class Nhd150Rx : NhdBaseDevice, IRoutingSource, IHasDynamicMultiviewLayout, IRoutingSinkWithLayouts
	{

		/// <summary>
		/// The multiview tile-sink child devices for this decoder, one per potential window slot
		/// (see <see cref="NhdBaseDevice.ConfiguredMaxTileCount"/>), keyed by 1-based tile number.
		/// Each is also registered with <see cref="DeviceManager"/>, so it can be targeted directly by
		/// string-key routing APIs (tie lines / <c>IRunDirectRouteAction.RunDirectRoute</c>) as well as
		/// programmatically via this dictionary (per <see cref="IRoutingSinkWithLayouts"/>).
		/// </summary>
		public Dictionary<int, IRoutingSinkWithFeedback> WindowTileSinks { get; } = new Dictionary<int, IRoutingSinkWithFeedback>();

		public override bool IsTransmitter => false;
		public override bool SupportsCec => true;
		public override bool SupportsIr => false;
		public override bool Supports232 => true;
		public override int MaxStreamCount => 9;

		/// <summary>
		/// The multiview tile-sink child devices for this decoder, one per potential window slot.
		/// Thin, concretely-typed view over <see cref="WindowTileSinks"/> for callers within this
		/// plugin that need <see cref="NhdMultiviewTileSink"/>-specific members.
		/// </summary>
		public IReadOnlyList<NhdMultiviewTileSink> TileSinks
			=> WindowTileSinks.Values.OfType<NhdMultiviewTileSink>().ToList();

		public Nhd150Rx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-150-RX")
		{
			AddStreamInputPort();
			AddHdmiOutputPort(NhdPortKeys.HdmiOutput1);
			AddAnalogAudioOutputPort();

			for (var tileNumber = 1; tileNumber <= ConfiguredMaxTileCount; tileNumber++)
			{
				var tileSink = new NhdMultiviewTileSink(this, tileNumber);
				WindowTileSinks.Add(tileNumber, tileSink);
				DeviceManager.AddDevice(tileSink);
			}
		}

		/// <summary>
		/// Gets the tile-sink for the given 1-based tile number, or null if out of range.
		/// </summary>
		public NhdMultiviewTileSink GetTileSink(int tileNumber)
			=> WindowTileSinks.TryGetValue(tileNumber, out var tileSink) ? tileSink as NhdMultiviewTileSink : null;

		/// <summary>
		/// Computes a multiview layout at runtime from a set of participant sources with priority
		/// values (see <see cref="NhdDynamicMultiviewLayoutCalculator"/>), sends it to the decoder,
		/// and syncs each tile-sink's feedback (<see cref="NhdMultiviewTileSink.UpdateCurrentSourceState"/>)
		/// to match. Additive to the existing named preset/custom layout APIs - does not affect them.
		/// </summary>
		/// <param name="participantSources">Sources to place in participant tiles, each with a priority (lower = higher priority).</param>
		/// <param name="presentationSourceKey">Essentials device key for the active presentation source, or null/empty if no presentation is active.</param>
		public bool ApplyDynamicLayout(
			IReadOnlyList<MultiviewParticipantSource> participantSources,
			string presentationSourceKey)
		{
			if (!SupportsMultiview)
			{
				Debug.LogError("[{0}] Endpoint does not support dynamic multiview layout", Key);
				return false;
			}

			var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
			if (ctl?.SessionManager == null)
			{
				Debug.LogError("[{0}] NHD-CTL session manager is not available for dynamic multiview layout", Key);
				return false;
			}

			TryGetHdmiOutResolutionDimensions(out var width, out var height);

			var tiles = NhdDynamicMultiviewLayoutCalculator.CalculateLayout(
				participantSources,
				presentationSourceKey,
				width,
				height,
				ConfiguredMaxTileCount);

			if (!ctl.SessionManager.TryApplyDynamicLayout(this, this, tiles))
				return false;

			SyncTileSinksToLayout(tiles);
			return true;
		}

		/// <summary>
		/// Devjson/console-friendly overload of
		/// <see cref="ApplyDynamicLayout(IReadOnlyList{MultiviewParticipantSource}, string)"/>.
		/// The reflection-based devjson dispatcher (<see cref="DeviceJsonApi"/>) can only construct
		/// true C# arrays of primitives/enums from JSON - it can't build an
		/// <see cref="IReadOnlyList{T}"/> of the <c>MultiviewParticipantSource</c> POCO. This takes
		/// parallel primitive arrays instead (<paramref name="sourceKeys"/>[i] paired with
		/// <paramref name="priorities"/>[i]) so the batch API is directly testable via devjson.
		/// </summary>
		public bool ApplyDynamicLayout(string[] sourceKeys, int[] priorities, string presentationSourceKey)
		{
			if (sourceKeys == null || priorities == null || sourceKeys.Length != priorities.Length)
			{
				Debug.LogError("[{0}] sourceKeys and priorities must both be provided and the same length", Key);
				return false;
			}

			var participantSources = sourceKeys
				.Select((key, i) => new MultiviewParticipantSource(key, priorities[i]))
				.ToList();

			return ApplyDynamicLayout(participantSources, presentationSourceKey);
		}

		private void SyncTileSinksToLayout(IReadOnlyList<NhdMultiviewTileState> tiles)
		{
			var tilesByNumber = (tiles ?? new List<NhdMultiviewTileState>())
				.Where(t => t != null)
				.ToDictionary(t => t.TileNumber);

			foreach (var tileSink in WindowTileSinks.Values.OfType<NhdMultiviewTileSink>())
			{
				tilesByNumber.TryGetValue(tileSink.TileNumber, out var tile);

				var sourceKey = tile?.SourceReference;
				var sourceDevice = string.IsNullOrWhiteSpace(sourceKey)
					? null
					: DeviceManager.GetDeviceForKey(sourceKey) as IRoutingSource;

				tileSink.UpdateCurrentSourceState(eRoutingSignalType.Video, sourceDevice);
			}
		}
	}
}
