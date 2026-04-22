using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;

namespace PepperDash.Essentials.Plugin
{
	public class Nhd150Rx : NhdBaseDevice
	{
		public Nhd150Rx(string key, string name, NhdDeviceProperties config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-150-RS")
		{
			AddInputPort("stream", 1);
			AddOutputPort("hdmi", 1);
		}
	}
}
