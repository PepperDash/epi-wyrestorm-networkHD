using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin
{
	public abstract class NhdBaseDevice : EssentialsDevice, IRoutingWithFeedback
	{
		private const eRoutingPortConnectionType DefaultPortConnectionType = eRoutingPortConnectionType.None;

		protected NhdBaseDevice(string key, string name, NhdDeviceProperties config, string modelName)
			: base(key, name)
		{
			Config = config;
			ModelName = modelName;
			DeviceId = config.DeviceId;
		}

		protected NhdDeviceProperties Config { get; private set; }
		public string ModelName { get; private set; }
		public int DeviceId { get; private set; }
		public abstract bool IsTransmitter { get; }
		public abstract bool SupportsCec { get; }
		public abstract bool SupportsIr { get; }
		public abstract bool Supports232 { get; }

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

		/// <summary>
		/// Sends a power proxy command. Supported if device supports CEC or RS-232.
		/// </summary>
		/// <param name="state">"on" or "off"</param>
		public virtual void SendPowerProxyCommand(string state)
		{
			if (!SupportsCec && !Supports232)
				throw new NotSupportedException($"{ModelName} does not support CEC or RS-232 power proxy");
			// TODO: implement
		}

		/// <summary>
		/// Sends a CEC command.
		/// </summary>
		/// <param name="command">"onetouchdisplay" or "standby"</param>
		public virtual void SendCecCommand(string command)
		{
			if (!SupportsCec)
				throw new NotSupportedException($"{ModelName} does not support CEC");
			// TODO: implement
		}

		/// <summary>
		/// Sends a custom CEC command with raw data.
		/// </summary>
		/// <param name="command">Must be "custom"</param>
		/// <param name="data">Raw CEC data</param>
		public virtual void SendCecCommand(string command, string data)
		{
			if (!SupportsCec)
				throw new NotSupportedException($"{ModelName} does not support CEC");
			if (!command.Equals("custom", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Only 'custom' is valid for the overload with data", nameof(command));
			// TODO: implement
		}

		/// <summary>
		/// Sends an IR command.
		/// </summary>
		/// <param name="data">IR data payload</param>
		public virtual void SendIrData(string data)
		{
			if (!SupportsIr)
				throw new NotSupportedException($"{ModelName} does not support IR");
			// TODO: implement
		}

		/// <summary>
		/// Sends an RS-232 command. Comm params are read from config.Rs232.
		/// </summary>
		/// <param name="data">Data string to send</param>
		public virtual void Send232Command(string data)
		{
			if (!Supports232)
				throw new NotSupportedException($"{ModelName} does not support RS-232");
			// TODO: implement using Config.Rs232 comm params
		}
	}
}
