using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;

namespace PepperDash.Essentials.Plugin
{
	public class Nhd120Tx : NhdBaseDevice
	{
		public Nhd120Tx(string key, string name, NhdDeviceProperties config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-120-TX")
		{
			AddInputPort("hdmi", 1);
			AddOutputPort("stream", 1);
		}
	}
}
