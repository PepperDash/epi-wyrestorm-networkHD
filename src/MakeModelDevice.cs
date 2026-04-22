using System.Collections.Generic;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Essentials.Plugin
{
	public abstract class WyrestormNetworkHdBaseDevice : EssentialsBridgeableDevice, IRoutingWithFeedback
	{
		protected WyrestormNetworkHdBaseDevice(string key, string name, MakeModelConfig config, IBasicCommunication comms, string modelName)
			: base(key, name)
		{
			Config = config;
			Comms = comms;
			ModelName = modelName;
		}

		protected MakeModelConfig Config { get; private set; }
		protected IBasicCommunication Comms { get; private set; }
		public string ModelName { get; private set; }

		public RoutingPortCollection<RoutingInputPort> InputPorts { get; } = new RoutingPortCollection<RoutingInputPort>();
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; } = new RoutingPortCollection<RoutingOutputPort>();
		public List<RouteSwitchDescriptor> CurrentRoutes { get; } = new List<RouteSwitchDescriptor>();
		public event RouteChangedEventHandler RouteChanged;

		public void ExecuteSwitch(object inputSelector, object outputSelector, eRoutingSignalType signalType)
		{
			var inputPort = inputSelector as RoutingInputPort;
			if (inputPort == null)
			{
				return;
			}

			var outputPort = outputSelector as RoutingOutputPort;

			var route = outputPort == null
				? new RouteSwitchDescriptor(inputPort)
				: new RouteSwitchDescriptor(outputPort, inputPort);

			CurrentRoutes.Clear();
			CurrentRoutes.Add(route);

			var callback = RouteChanged;
			if (callback != null)
			{
				callback(this, route);
			}
		}

		protected void AddInputPort(string key, object selector)
		{
			InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, (eRoutingPortConnectionType)0, selector, this));
		}

		protected void AddOutputPort(string key, object selector)
		{
			OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, (eRoutingPortConnectionType)0, selector, this));
		}

		public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
		}
	}

	public class Nhd150RsDecoderDevice : WyrestormNetworkHdBaseDevice
	{
		public Nhd150RsDecoderDevice(string key, string name, MakeModelConfig config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-150-RS")
		{
			AddInputPort("stream", 1);
			AddOutputPort("hdmi", 1);
		}
	}

	public class Nhd120TxEncoderDevice : WyrestormNetworkHdBaseDevice
	{
		public Nhd120TxEncoderDevice(string key, string name, MakeModelConfig config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-120-TX")
		{
			AddInputPort("hdmi", 1);
			AddOutputPort("stream", 1);
		}
	}

	public class NhdCtlProControllerDevice : WyrestormNetworkHdBaseDevice
	{
		public NhdCtlProControllerDevice(string key, string name, MakeModelConfig config, IBasicCommunication comms)
			: base(key, name, config, comms, "NHD-CTL-PRO")
		{
			AddInputPort("network", 1);
			AddOutputPort("network", 1);
		}
	}
}
