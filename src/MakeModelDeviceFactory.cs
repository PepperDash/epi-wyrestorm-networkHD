using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin
{
  public class MakeModelDeviceFactory : EssentialsPluginDeviceFactory<WyreStormNetworkHdBaseDevice>
  {
    public MakeModelDeviceFactory()
    {
      MinimumEssentialsFrameworkVersion = "2.12.1";
      TypeNames = new List<string>() { "nhd-150-rs", "nhd150rs", "nhd-120-tx", "nhd120tx", "nhd-ctl-pro", "nhdctlpro" };
    }

    public override EssentialsDevice BuildDevice(PepperDash.Essentials.Core.Config.DeviceConfig dc)
    {
      Debug.LogVerbose("[{key}] Factory Attempting to create new device from type: {type}", dc.Key, dc.Type);

      var propertiesConfig = dc.Properties.ToObject<MakeModelConfig>();
      if (propertiesConfig == null)
      {
        Debug.LogError("[{key}] Factory: failed to read properties config for {name}", dc.Key, dc.Name);
        return null;
      }

      var comms = CommFactory.CreateCommForDevice(dc);
      if (comms == null)
      {
        Debug.LogError("[{key}] Factory Notice: No control object present for device {name}", dc.Key, dc.Name);
        return null;
      }

      var type = dc.Type.ToLower();
      if (type == "nhd-150-rs" || type == "nhd150rs")
      {
        return new Nhd150RsDecoderDevice(dc.Key, dc.Name, propertiesConfig, comms);
      }
      if (type == "nhd-120-tx" || type == "nhd120tx")
      {
        return new Nhd120TxEncoderDevice(dc.Key, dc.Name, propertiesConfig, comms);
      }
      if (type == "nhd-ctl-pro" || type == "nhdctlpro")
      {
        return new NhdCtlProControllerDevice(dc.Key, dc.Name, propertiesConfig, comms);
      }

      Debug.LogError("[{key}] Factory: unsupported device type '{type}' for {name}", dc.Key, dc.Type, dc.Name);
      return null;

    }

  }

}
