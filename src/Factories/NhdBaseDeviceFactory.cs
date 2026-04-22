using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugin
{
    public abstract class NhdBaseDeviceFactory<T> : EssentialsPluginDeviceFactory<T> where T : NhdBaseDevice
    {
        public const string MinimumEssentialsVersion = "2.12.1";

        protected static NhdDeviceProperties GetProperties(DeviceConfig dc)
        {
            var props = dc.Properties.ToObject<NhdDeviceProperties>();
            if (props == null)
                Debug.LogError("[{key}] Factory: failed to read properties config for {name}", dc.Key, dc.Name);
            return props;
        }

        protected static IBasicCommunication GetComms(DeviceConfig dc)
        {
            var comms = CommFactory.CreateCommForDevice(dc);
            if (comms == null)
                Debug.LogError("[{key}] Factory: no control object present for {name}", dc.Key, dc.Name);
            return comms;
        }
    }
}
