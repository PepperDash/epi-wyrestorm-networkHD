using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	public class Nhd150Rx : NhdBaseDevice
	{
		public override bool IsTransmitter => false;
		public override bool SupportsCec => true;
		public override bool SupportsIr => false;
		public override bool Supports232 => true;
		public override int MaxStreamCount => 9;

		public Nhd150Rx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-150-RX")
		{
			AddStreamInputPort();
			AddHdmiOutputPort(NhdPortKeys.HdmiOutput1);
			AddAnalogAudioOutputPort();
			AddRs232Ports(config.Rs232RoutingMode);
		}
	}
}
