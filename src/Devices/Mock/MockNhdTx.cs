using System.Collections.Generic;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin.Mock
{
	/// <summary>
	/// Mock WyreStorm NetworkHD transmitter (encoder) for local development/testing of routing and
	/// dynamic multiview layouts without real hardware. A Tx is a midpoint, not a source in its own
	/// right - it's what a real source device (e.g. a camera or generic HDMI source) plugs into to
	/// be converted from HDMI to a NetworkHD/VoIP stream. Implements <see cref="IRoutingMidpoint"/>
	/// (a single HDMI input + stream output) so the original source device can be tied directly into
	/// it, representing that physical HDMI cable, without modeling any of a real Tx's other
	/// encoder-specific behavior (EDID, HDCP, etc.). The original source device (not this Tx) is what
	/// gets referenced wherever a participant/dynamic-layout source is needed.
	/// </summary>
	public class MockNhdTx : EssentialsDevice, IRoutingMidpoint
	{
		/// <inheritdoc />
		public RoutingPortCollection<RoutingInputPort> InputPorts { get; } = new RoutingPortCollection<RoutingInputPort>();

		/// <inheritdoc />
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; } = new RoutingPortCollection<RoutingOutputPort>();

		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdTx"/> class.
		/// </summary>
		public MockNhdTx(string key, string name)
			: base(key, name)
		{
			InputPorts.Add(new RoutingInputPort(
				NhdPortKeys.HdmiInput1,
				eRoutingSignalType.AudioVideo,
				eRoutingPortConnectionType.Hdmi,
				NhdPortKeys.HdmiInput1,
				this));

			OutputPorts.Add(new RoutingOutputPort(
				NhdPortKeys.Stream,
				eRoutingSignalType.AudioVideo,
				eRoutingPortConnectionType.Streaming,
				NhdPortKeys.Stream,
				this));
		}
	}

	/// <summary>
	/// Factory for building <see cref="MockNhdTx"/> devices.
	/// </summary>
	public class MockNhdTxFactory : EssentialsPluginDeviceFactory<MockNhdTx>
	{
		// Matches NhdBaseDeviceFactory<T>'s static constructor - ensures NhdGlobalRouter is
		// registered even in systems built purely from mock devices (no real NhdBaseDevice).
		static MockNhdTxFactory()
		{
			if (DeviceManager.GetDeviceForKey(NhdGlobalRouter.InstanceKey) == null)
				DeviceManager.AddDevice(NhdGlobalRouter.Instance);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdTxFactory"/> class.
		/// </summary>
		public MockNhdTxFactory()
		{
			TypeNames = new List<string> { "mocknhdtx", "mock-nhd-tx" };
		}

		/// <inheritdoc />
		public override EssentialsDevice BuildDevice(DeviceConfig dc)
		{
			return new MockNhdTx(dc.Key, dc.Name);
		}
	}
}
