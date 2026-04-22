using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	public class Nhd120Tx : NhdBaseDevice
	{
		public override bool IsTransmitter => true;
		public override bool SupportsCec => true;
		public override bool SupportsIr => true;
		public override bool Supports232 => true;

		public Nhd120Tx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-120-TX")
		{
			AddHdmiInputPort(NhdPortKeys.HdmiInput1);
			AddHdmiAudioInputPort(NhdPortKeys.HdmiAudioInput1);
			AddAnalogAudioInputPort();
			AddStreamOutputPort();
			AddIrPorts(config.IrRoutingMode);
			AddRs232Ports(config.Rs232RoutingMode);
		}
	}
}
