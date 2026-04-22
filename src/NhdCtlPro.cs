using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;

namespace PepperDash.Essentials.Plugin
{
	public class NhdCtlPro : WyreStormNetworkHdBaseDevice
	{
		public NhdCtlPro(string key, string name, MakeModelConfig config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-CTL-PRO")
		{
			AddInputPort("network", 1);
			AddOutputPort("network", 1);
		}
	}
}
