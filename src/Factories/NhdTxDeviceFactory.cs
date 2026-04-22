using System.Collections.Generic;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugin
{
    public class NhdTxDeviceFactory : NhdBaseDeviceFactory<Nhd120Tx>
    {
        public NhdTxDeviceFactory()
        {
            MinimumEssentialsFrameworkVersion = MinimumEssentialsVersion;
            TypeNames = new List<string> { "nhd-120-tx", "nhd120tx" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            var props = GetProperties(dc);
            if (props == null) return null;

            var comms = GetComms(dc);
            if (comms == null) return null;

            return new Nhd120Tx(dc.Key, dc.Name, props, comms);
        }
    }
}
