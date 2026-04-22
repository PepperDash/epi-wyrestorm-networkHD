using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;

namespace PepperDash.Essentials.Plugin
{
	public class Nhd120Tx : WyreStormNetworkHdBaseDevice
	{
		public Nhd120Tx(string key, string name, MakeModelConfig config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-120-TX")
		{
			AddInputPort("hdmi", 1);
			AddOutputPort("stream", 1);
		}
	}
}
