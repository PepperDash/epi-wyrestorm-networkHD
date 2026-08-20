using System.Collections.Generic;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugin.Mock
{
	/// <summary>
	/// Mock WyreStorm NHD-CTL controller for local development/testing without real hardware.
	/// This is purely a topology placeholder - unlike the real <see cref="NhdCtlPro"/>, it plays no
	/// functional part in dynamic-layout routing (<see cref="MockNhdRx.ApplyDynamicLayout"/> never
	/// looks for a controller at all - it applies computed tile state directly), since the Essentials
	/// routing/tie-line model already treats each transmitter as feeding a decoder's tile sinks
	/// directly, with the NHD-CTL controller acting purely as an out-of-band control-plane device
	/// (Telnet), not a routing hop. It's included only so a mock config's device list visually
	/// mirrors a real system's topology in the routing dev tools.
	/// </summary>
	/// <remarks>
	/// Does not implement <see cref="ICommunicationMonitor"/> - there's no real communication to
	/// monitor, and <see cref="StatusMonitorBase"/> requires either a real <see cref="IBasicCommunication"/>
	/// client or a custom subclass, which would be unwarranted complexity for a device that has no
	/// functional role in the mocked scenario. <see cref="IsOnline"/> is provided instead, as a simple
	/// always-true informational feedback.
	/// </remarks>
	public class MockNhdCtl : EssentialsDevice
	{
		/// <summary>
		/// Always-true feedback, standing in for a real connection/communication status.
		/// </summary>
		public BoolFeedback IsOnline { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdCtl"/> class.
		/// </summary>
		public MockNhdCtl(string key, string name)
			: base(key, name)
		{
			IsOnline = new BoolFeedback("IsOnline", () => true);
		}
	}

	/// <summary>
	/// Factory for building <see cref="MockNhdCtl"/> devices.
	/// </summary>
	public class MockNhdCtlFactory : EssentialsPluginDeviceFactory<MockNhdCtl>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MockNhdCtlFactory"/> class.
		/// </summary>
		public MockNhdCtlFactory()
		{
			TypeNames = new List<string> { "mocknhdctl", "mock-nhd-ctl" };
		}

		/// <inheritdoc />
		public override EssentialsDevice BuildDevice(DeviceConfig dc)
		{
			return new MockNhdCtl(dc.Key, dc.Name);
		}
	}
}
