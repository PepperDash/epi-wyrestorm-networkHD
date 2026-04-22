using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	public class Nhd150Rx : NhdBaseDevice
	{
		public override bool IsTransmitter => false;
		public override bool SupportsCec => true;
		public override bool SupportsIr => false;
		public override bool Supports232 => true;

		public Nhd150Rx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-150-RS")
		{
			AddStreamInputPort();
			AddHdmiOutputPort(NhdPortKeys.HdmiOutput1);
			AddHdmiAudioOutputPort(NhdPortKeys.HdmiAudioOutput1);
			AddAnalogAudioOutputPort();
			AddRs232Ports(config.Rs232RoutingMode);
		}
	}
}
