using PepperDash.Core;

namespace PepperDash.Essentials.Plugin
{
	public class NhdCtlPro : NhdBaseDevice
	{
		public override bool IsTransmitter => false;
		public override bool SupportsCec => false;
		public override bool SupportsIr => false;
		public override bool Supports232 => false;

		protected IBasicCommunication Comms { get; private set; }

		public NhdCtlPro(string key, string name, NhdDeviceProperties config, IBasicCommunication comms)
			: base(key, name, config, "NHD-CTL-PRO")
		{
			Comms = comms;
			AddInputPort("network", 1);
			AddOutputPort("network", 1);
		}
	}
}
