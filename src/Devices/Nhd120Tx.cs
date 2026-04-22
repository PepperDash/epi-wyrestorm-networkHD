namespace PepperDash.Essentials.Plugin
{
	public class Nhd120Tx : NhdBaseDevice
	{
		public override bool IsTransmitter => true;
		public override bool SupportsCec => false;
		public override bool SupportsIr => false;
		public override bool Supports232 => false;

		public Nhd120Tx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-120-TX")
		{
			AddInputPort("hdmi", 1);
			AddOutputPort("stream", 1);
		}
	}
}
