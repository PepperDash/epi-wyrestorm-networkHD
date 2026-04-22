using System;
using PepperDash.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugin
{
    public class NhdDeviceProperties
    {
        public static NhdDeviceProperties FromDeviceConfig(DeviceConfig config)
        {
            return config.Properties.ToObject<NhdDeviceProperties>();
        }

        public int DeviceId { get; set; }
        public ControlPropertiesConfig Control { get; set; }
        public string Mode { get; set; }
        public string StreamUrl { get; set; }
        public string MulticastVideoAddress { get; set; }
        public string MulticastAudioAddress { get; set; }
        public string ParentDeviceKey { get; set; }
        public string DefaultAudioInput { get; set; }
        public string DefaultVideoInput { get; set; }
        public bool EnableAutoRoute { get; set; }
        public string DefaultMulticastSource { get; set; }
    }

    internal static class NhdDevicePropertiesExt
    {
        public static bool DeviceIsTransmitter(this NhdDeviceProperties props)
        {
            return !string.IsNullOrEmpty(props.Mode) &&
                        props.Mode.Equals("tx", StringComparison.OrdinalIgnoreCase);
        }
    }
}
