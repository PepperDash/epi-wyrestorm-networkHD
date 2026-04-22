using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Plugin.Config;

namespace PepperDash.Essentials.Plugin
{
    public class NhdDeviceProperties
    {
        public static NhdDeviceProperties FromDeviceConfig(DeviceConfig config)
        {
            return config.Properties.ToObject<NhdDeviceProperties>();
        }

        public int DeviceId { get; set; }
        public string Alias { get; set; }
        public Nhd232Properties Rs232 { get; set; }
    }
}
