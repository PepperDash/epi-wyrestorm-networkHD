using System.Collections.Generic;
using Newtonsoft.Json;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Plugin.Config;
using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin
{
    public class NhdDeviceProperties
    {
        public static NhdDeviceProperties FromDeviceConfig(DeviceConfig config)
        {
            if (config == null || config.Properties == null)
                return new NhdDeviceProperties();

            try
            {
                return config.Properties.ToObject<NhdDeviceProperties>() ?? new NhdDeviceProperties();
            }
            catch
            {
                return new NhdDeviceProperties();
            }
        }

        public int MatrixInputSlot { get; set; }
        public int MatrixOutputSlot { get; set; }
        public string Alias { get; set; }
        public Nhd232Properties Rs232 { get; set; }
        public string ApiUsername { get; set; }
        public string ApiPassword { get; set; }
        [JsonConverter(typeof(TolerantStringEnumConverter))]
        public NhdDeviceMode? Mode { get; set; }
        public List<NhdCustomMultiviewLayoutProperties> CustomMultiviewLayouts { get; set; } = new List<NhdCustomMultiviewLayoutProperties>();
        public List<NhdMultiviewPresetProperties> MultiviewPresets { get; set; } = new List<NhdMultiviewPresetProperties>();

        /// <summary>
        /// Maximum number of multiview tile-sink child devices to create for this decoder.
        /// Defaults to the device's <see cref="NhdBaseDevice.MaxStreamCount"/> (e.g. 9 for
        /// NHD-150-RX) when not set. Values greater than MaxStreamCount are clamped down to it.
        /// </summary>
        public int? MaxTileCount { get; set; }
    }
}
