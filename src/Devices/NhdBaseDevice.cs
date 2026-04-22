using System.Collections.Generic;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Essentials.Plugin
{
	public abstract class NhdBaseDevice : EssentialsBridgeableDevice, IRoutingWithFeedback
	{
		private const eRoutingPortConnectionType DefaultPortConnectionType = eRoutingPortConnectionType.None;

		protected NhdBaseDevice(string key, string name, NhdDeviceProperties config, IBasicCommunication comms, string modelName)
			: base(key, name)
		{
			Config = config;
			Comms = comms;
			ModelName = modelName;
		}

		protected NhdDeviceProperties Config { get; private set; }
		protected IBasicCommunication Comms { get; private set; }
		public string ModelName { get; private set; }

		public RoutingPortCollection<RoutingInputPort> InputPorts { get; } = new RoutingPortCollection<RoutingInputPort>();
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; } = new RoutingPortCollection<RoutingOutputPort>();
		public List<RouteSwitchDescriptor> CurrentRoutes { get; } = new List<RouteSwitchDescriptor>();
		public event RouteChangedEventHandler RouteChanged;

		public void ExecuteSwitch(object inputSelector, object outputSelector, eRoutingSignalType signalType)
		{
			if (signalType != eRoutingSignalType.AudioVideo)
			{
				Debug.LogError("[{0}] Unsupported signal type '{1}' for switch operation", Key, signalType);
				return;
			}

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
			InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, DefaultPortConnectionType, selector, this));
		}

		protected void AddOutputPort(string key, object selector)
		{
			OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, DefaultPortConnectionType, selector, this));
		}

		public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
		}
	}
}
