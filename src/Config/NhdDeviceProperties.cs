using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Plugin.Config;
using PepperDash.Essentials.Plugin.Enums;

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
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdDeviceMode? Mode { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdComPortRoutingMode? IrRoutingMode { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdComPortRoutingMode? Rs232RoutingMode { get; set; }
    }
}
