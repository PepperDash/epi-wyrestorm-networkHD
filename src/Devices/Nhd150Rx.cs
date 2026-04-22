namespace PepperDash.Essentials.Plugin
{
	public class Nhd150Rx : NhdBaseDevice
	{
		public override bool IsTransmitter => false;
		public override bool SupportsCec => false;
		public override bool SupportsIr => false;
		public override bool Supports232 => false;

		public Nhd150Rx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-150-RS")
		{
			AddInputPort("stream", 1);
			AddOutputPort("hdmi", 1);
		}
	}
}
