using System.Collections.Generic;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugin
{
    public class NhdCtlDeviceFactory : NhdBaseDeviceFactory<NhdCtlPro>
    {
        public NhdCtlDeviceFactory()
        {
            MinimumEssentialsFrameworkVersion = MinimumEssentialsVersion;
            TypeNames = new List<string> { "nhd-ctl-pro", "nhdctlpro" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            var props = GetProperties(dc);
            if (props == null) return null;

            var comms = GetComms(dc);
            if (comms == null) return null;

            return new NhdCtlPro(dc.Key, dc.Name, props, comms);
        }
    }
}
