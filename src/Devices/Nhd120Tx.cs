namespace PepperDash.Essentials.Plugin
{
	public class Nhd120Tx : NhdBaseDevice
	{
		public Nhd120Tx(string key, string name, NhdDeviceProperties config)
			: base(key, name, config, "NHD-120-TX")
		{
			AddInputPort("hdmi", 1);
			AddOutputPort("stream", 1);
		}
	}
}
