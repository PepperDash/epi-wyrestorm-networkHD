using System.Collections.Generic;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugin
{
    public class NhdRxDeviceFactory : NhdBaseDeviceFactory<Nhd150Rx>
    {
        public NhdRxDeviceFactory()
        {
            MinimumEssentialsFrameworkVersion = MinimumEssentialsVersion;
            TypeNames = new List<string> { "nhd-150-rs", "nhd150rx" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            var props = GetProperties(dc);
            if (props == null) return null;

            return new Nhd150Rx(dc.Key, dc.Name, props);
        }
    }
}
