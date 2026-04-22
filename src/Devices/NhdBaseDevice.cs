using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Enums;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	public abstract class NhdBaseDevice : EssentialsDevice, IRoutingWithFeedback
	{
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

		// Video ports
		protected void AddHdmiInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddHdmiOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddStreamInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.Stream, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Streaming, NhdPortKeys.Stream, this));

		protected void AddStreamOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.Stream, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Streaming, NhdPortKeys.Stream, this));

		// Audio ports
		protected void AddHdmiAudioInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddHdmiAudioOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddAnalogAudioInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.AnalogAudioInput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.AnalogAudioInput, this));

		protected void AddAnalogAudioOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.AnalogAudioOutput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.AnalogAudioOutput, this));

		protected void AddDanteInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.DanteInput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.DanteInput, this));

		protected void AddDanteOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.DanteOutput, eRoutingSignalType.Audio, eRoutingPortConnectionType.LineAudio, NhdPortKeys.DanteOutput, this));

		// Control ports
		protected void AddUsbInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.UsbInput, eRoutingSignalType.UsbInput, eRoutingPortConnectionType.Usb, NhdPortKeys.UsbInput, this));

		protected void AddUsbOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.UsbOutput, eRoutingSignalType.UsbOutput, eRoutingPortConnectionType.Usb, NhdPortKeys.UsbOutput, this));

		/// <summary>
		/// Adds IR routing port(s) based on the configured routing mode.
		/// ControlSystem: adds irIn — Crestron side, data enters NHD here.
		/// Device: adds irOut — end-device side, data exits NHD here.
		/// NotRoutable or null: no routing ports; use SendIrData directly.
		/// </summary>
		protected void AddIrPorts(NhdComPortRoutingMode? mode)
		{
			switch (mode ?? NhdComPortRoutingMode.NotRoutable)
			{
				case NhdComPortRoutingMode.ControlSystem:
					InputPorts.Add(new RoutingInputPort(NhdPortKeys.IrInput, eRoutingSignalType.IR, eRoutingPortConnectionType.Ir, NhdPortKeys.IrInput, this));
					break;
				case NhdComPortRoutingMode.Device:
					OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.IrOutput, eRoutingSignalType.IR, eRoutingPortConnectionType.Ir, NhdPortKeys.IrOutput, this));
					break;
				case NhdComPortRoutingMode.NotRoutable:
				default:
					break;
			}
		}

		/// <summary>
		/// Adds RS-232 routing port(s) based on the configured routing mode.
		/// ControlSystem: adds rs232In — Crestron side, data enters NHD here.
		/// Device: adds rs232Out — end-device side, data exits NHD here.
		/// NotRoutable or null: no routing ports; use Send232Command directly.
		/// </summary>
		protected void AddRs232Ports(NhdComPortRoutingMode? mode)
		{
			switch (mode ?? NhdComPortRoutingMode.NotRoutable)
			{
				case NhdComPortRoutingMode.ControlSystem:
					InputPorts.Add(new RoutingInputPort(NhdPortKeys.Rs232Input, eRoutingSignalType.None, eRoutingPortConnectionType.Com, NhdPortKeys.Rs232Input, this));
					break;
				case NhdComPortRoutingMode.Device:
					OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.Rs232Output, eRoutingSignalType.None, eRoutingPortConnectionType.Com, NhdPortKeys.Rs232Output, this));
					break;
				case NhdComPortRoutingMode.NotRoutable:
				default:
					break;
			}
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
