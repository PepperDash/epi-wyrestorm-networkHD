using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin.Mock
{
	/// <summary>
	/// Configuration properties for a <see cref="MockNhdRx"/> device.
	/// </summary>
	public class MockNhdRxPropertiesConfig
	{
		/// <summary>
		/// Number of multiview tile-sink child devices to create. Defaults to 4.
		/// </summary>
		[JsonProperty("tileCount")]
		public int TileCount { get; set; } = 4;
	}

	/// <summary>
	/// Mock WyreStorm NetworkHD multiview decoder (e.g. standing in for a real
	/// <see cref="Nhd150Rx"/>) for local development/testing of routing and dynamic multiview
	/// layouts without real hardware or a real NHD-CTL controller. Implements the same
	/// routing-relevant interfaces as a real decoder - <see cref="IRoutingSource"/>,
	/// <see cref="IHasDynamicMultiviewLayout"/>, and <see cref="IRoutingSinkWithLayouts"/> - but
	/// <see cref="ApplyDynamicLayout(IReadOnlyList{MultiviewParticipantSource}, string)"/> only
	/// computes a layout with the real <see cref="NhdDynamicMultiviewLayoutCalculator"/> and updates
	/// each mock tile-sink's own bookkeeping/feedback directly - it never talks to a controller or
	/// any real hardware. This is enough to exercise the full dynamic-layout algorithm and the
	/// routing dev tools' live feedback end to end.
	/// </summary>
	public class MockNhdRx : EssentialsDevice, IRoutingSource, IHasDynamicMultiviewLayout, IRoutingSinkWithLayouts
	{
		private readonly int _maxTileCount;

		/// <inheritdoc />
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; } = new RoutingPortCollection<RoutingOutputPort>();

		/// <summary>
		/// The multiview tile-sink child devices for this mock decoder, one per potential window
		/// slot, keyed by 1-based tile number. Each is also registered with <see cref="DeviceManager"/>,
		/// matching the real <see cref="Nhd150Rx"/>'s behavior.
		/// </summary>
		public Dictionary<int, IRoutingSinkWithFeedback> WindowTileSinks { get; } = new Dictionary<int, IRoutingSinkWithFeedback>();

		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdRx"/> class.
		/// </summary>
		public MockNhdRx(string key, string name, MockNhdRxPropertiesConfig config)
			: base(key, name)
		{
			config ??= new MockNhdRxPropertiesConfig();
			_maxTileCount = config.TileCount > 0 ? config.TileCount : 4;

			OutputPorts.Add(new RoutingOutputPort(
				NhdPortKeys.HdmiOutput1,
				eRoutingSignalType.AudioVideo,
				eRoutingPortConnectionType.Hdmi,
				NhdPortKeys.HdmiOutput1,
				this));

			for (var tileNumber = 1; tileNumber <= _maxTileCount; tileNumber++)
			{
				var tileSink = new MockNhdMultiviewTileSink(this, tileNumber);
				WindowTileSinks.Add(tileNumber, tileSink);
				DeviceManager.AddDevice(tileSink);
			}
		}

		/// <summary>
		/// Gets the tile-sink for the given 1-based tile number, or null if out of range.
		/// </summary>
		public MockNhdMultiviewTileSink GetTileSink(int tileNumber)
			=> WindowTileSinks.TryGetValue(tileNumber, out var tileSink) ? tileSink as MockNhdMultiviewTileSink : null;

		/// <inheritdoc />
		public bool ApplyDynamicLayout(
			IReadOnlyList<MultiviewParticipantSource> participantSources,
			string presentationSourceKey)
		{
			// Canvas dimensions <= 0 fall back to the calculator's own defaults (1920x1080) - there's
			// no real display resolution query to perform here.
			var tiles = NhdDynamicMultiviewLayoutCalculator.CalculateLayout(
				participantSources,
				presentationSourceKey,
				0,
				0,
				_maxTileCount);

			Debug.LogMessage(Serilog.Events.LogEventLevel.Information,
				"[{0}] Applied mock dynamic layout with {1} tile(s)", Key, tiles.Count);

			SyncTileSinksToLayout(tiles);
			return true;
		}

		/// <summary>
		/// Devjson/console-friendly overload, mirroring the real <see cref="Nhd150Rx"/>'s equivalent
		/// method - see there for why parallel primitive arrays are used instead of the POCO overload.
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

			foreach (var tileSink in WindowTileSinks.Values.OfType<MockNhdMultiviewTileSink>())
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

	/// <summary>
	/// Factory for building <see cref="MockNhdRx"/> devices.
	/// </summary>
	public class MockNhdRxFactory : EssentialsPluginDeviceFactory<MockNhdRx>
	{
		// Matches NhdBaseDeviceFactory<T>'s static constructor - ensures NhdGlobalRouter is
		// registered even in systems built purely from mock devices (no real NhdBaseDevice).
		static MockNhdRxFactory()
		{
			if (DeviceManager.GetDeviceForKey(NhdGlobalRouter.InstanceKey) == null)
				DeviceManager.AddDevice(NhdGlobalRouter.Instance);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdRxFactory"/> class.
		/// </summary>
		public MockNhdRxFactory()
		{
			TypeNames = new List<string> { "mocknhdrx", "mock-nhd-rx" };
		}

		/// <inheritdoc />
		public override EssentialsDevice BuildDevice(DeviceConfig dc)
		{
			MockNhdRxPropertiesConfig props = null;
			try
			{
				if (dc.Properties != null)
					props = dc.Properties.ToObject<MockNhdRxPropertiesConfig>();
			}
			catch (System.Exception ex)
			{
				Debug.LogError("[{key}] Factory: exception reading properties config for {name}: {message}", dc.Key, dc.Name, ex.Message);
			}

			return new MockNhdRx(dc.Key, dc.Name, props ?? new MockNhdRxPropertiesConfig());
		}
	}
}
