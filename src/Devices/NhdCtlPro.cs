using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;

namespace PepperDash.Essentials.Plugin
{
	public class NhdCtlPro : NhdBaseDevice
	{
		public NhdCtlPro(string key, string name, NhdDeviceProperties config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-CTL-PRO")
		{
			AddInputPort("network", 1);
			AddOutputPort("network", 1);
		}
	}
}
