using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
    public abstract class NhdBaseDeviceFactory<T> : EssentialsPluginDeviceFactory<T> where T : NhdBaseDevice
    {
        public const string MinimumEssentialsVersion = "2.12.1";

        static NhdBaseDeviceFactory()
        {
            if (DeviceManager.GetDeviceForKey(NhdGlobalRouter.InstanceKey) == null)
                DeviceManager.AddDevice(NhdGlobalRouter.Instance);
        }

        protected static NhdDeviceProperties GetProperties(DeviceConfig dc)
        {
            var props = dc.Properties.ToObject<NhdDeviceProperties>();
            if (props == null)
            {
                Debug.LogError("[{key}] Factory: failed to read properties config for {name}", dc.Key, dc.Name);
                return null;
            }

            HydrateApiCredentialsFromControlConfig(dc, props);
            return props;
        }

        private static void HydrateApiCredentialsFromControlConfig(DeviceConfig dc, NhdDeviceProperties props)
        {
            if (props == null || dc == null)
                return;

            var controlConfig = CommFactory.GetControlPropertiesConfig(dc);
            var tcpSsh = controlConfig?.TcpSshProperties;
            if (tcpSsh == null)
                return;

            if (string.IsNullOrWhiteSpace(props.ApiUsername) && !string.IsNullOrWhiteSpace(tcpSsh.Username))
            {
                props.ApiUsername = tcpSsh.Username;
            }

            if (string.IsNullOrEmpty(props.ApiPassword) && !string.IsNullOrEmpty(tcpSsh.Password))
            {
                props.ApiPassword = tcpSsh.Password;
            }
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
