using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Comms;
using PepperDash.Essentials.Plugin.Enums;
using PepperDash.Essentials.Plugin.Routing;

namespace PepperDash.Essentials.Plugin
{
	public abstract class NhdBaseDevice : EssentialsDevice, IRoutingWithFeedback
	{
		private NhdMultiStreamMode _multiStreamMode = NhdMultiStreamMode.Tile;
		private bool _online;

		protected NhdBaseDevice(string key, string name, NhdDeviceProperties config, string modelName)
			: base(key, name)
		{
			Config = config ?? new NhdDeviceProperties();
			ModelName = modelName;
			DeviceId = Config.DeviceId;
			IsOnline = new BoolFeedback("IsOnline", () => _online);
		}

		protected NhdDeviceProperties Config { get; private set; }
		public string ModelName { get; private set; }
		public int DeviceId { get; private set; }
		public string ConfiguredAlias => string.IsNullOrWhiteSpace(Config.Alias) ? null : Config.Alias.Trim();
		public string Hostname { get; private set; }
		public BoolFeedback IsOnline { get; private set; }
		public string ApiEndpointReference =>
			ConfiguredAlias
			?? Hostname
			?? Key;
		public abstract bool IsTransmitter { get; }
		public abstract bool SupportsCec { get; }
		public abstract bool SupportsIr { get; }
		public abstract bool Supports232 { get; }

		/// <summary>
		/// Maximum number of simultaneous stream windows this device can decode. Defaults to 1.
		/// Override in subclasses that support multi-stream decoding.
		/// </summary>
		public virtual int MaxStreamCount => 1;

		/// <summary>
		/// Runtime multi-stream layout mode for devices that support more than one stream window.
		/// Setting this on single-stream devices throws an exception.
		/// </summary>
		public NhdMultiStreamMode MultiStreamMode
		{
			get => _multiStreamMode;
			set
			{
				if (MaxStreamCount <= 1)
					throw new InvalidOperationException($"{ModelName} does not support multi-stream mode changes");

				_multiStreamMode = value;
			}
		}

		public RoutingPortCollection<RoutingInputPort> InputPorts { get; } = new RoutingPortCollection<RoutingInputPort>();
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; } = new RoutingPortCollection<RoutingOutputPort>();
		public List<RouteSwitchDescriptor> CurrentRoutes { get; } = new List<RouteSwitchDescriptor>();
		public event RouteChangedEventHandler RouteChanged;

		public void SetResolvedHostname(string hostname)
		{
			if (string.IsNullOrWhiteSpace(hostname))
				return;

			var value = hostname.Trim();
			if (string.Equals(Hostname, value, StringComparison.OrdinalIgnoreCase))
				return;

			Hostname = value;
			Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Hostname resolved to '{1}' (alias='{2}')", this, Hostname, ConfiguredAlias ?? "null");
		}

		public void SetOnlineState(bool isOnline)
		{
			if (_online == isOnline)
				return;

			_online = isOnline;
			Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Online state -> {1} (endpointRef='{2}')", this, isOnline ? "ONLINE" : "OFFLINE", ApiEndpointReference);
			IsOnline.FireUpdate();
		}

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

		// Video ports
		protected void AddHdmiInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddHdmiOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddUsbcInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddUsbcOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddStreamInputPort()
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.Stream, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Streaming, NhdPortKeys.Stream, this));

		protected void AddStreamOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.Stream, eRoutingSignalType.AudioVideo, eRoutingPortConnectionType.Streaming, NhdPortKeys.Stream, this));

		// Audio ports
		protected void AddHdmiAudioInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddHdmiAudioOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.Hdmi, key, this));

		protected void AddUsbcAudioInputPort(string key)
			=> InputPorts.Add(new RoutingInputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.UsbC, key, this));

		protected void AddUsbcAudioOutputPort(string key)
			=> OutputPorts.Add(new RoutingOutputPort(key, eRoutingSignalType.Audio, eRoutingPortConnectionType.UsbC, key, this));

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
			=> InputPorts.Add(new RoutingInputPort(NhdPortKeys.UsbInput, NhdRoutingSignalTypes.UsbInput, eRoutingPortConnectionType.UsbC, NhdPortKeys.UsbInput, this));

		protected void AddUsbOutputPort()
			=> OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.UsbOutput, NhdRoutingSignalTypes.UsbOutput, eRoutingPortConnectionType.UsbC, NhdPortKeys.UsbOutput, this));

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
					InputPorts.Add(new RoutingInputPort(NhdPortKeys.IrInput, NhdRoutingSignalTypes.Ir, eRoutingPortConnectionType.None, NhdPortKeys.IrInput, this));
					break;
				case NhdComPortRoutingMode.Device:
					OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.IrOutput, NhdRoutingSignalTypes.Ir, eRoutingPortConnectionType.None, NhdPortKeys.IrOutput, this));
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
					InputPorts.Add(new RoutingInputPort(NhdPortKeys.Rs232Input, NhdRoutingSignalTypes.Serial, eRoutingPortConnectionType.None, NhdPortKeys.Rs232Input, this));
					break;
				case NhdComPortRoutingMode.Device:
					OutputPorts.Add(new RoutingOutputPort(NhdPortKeys.Rs232Output, NhdRoutingSignalTypes.Serial, eRoutingPortConnectionType.None, NhdPortKeys.Rs232Output, this));
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

			if (string.IsNullOrWhiteSpace(state))
				throw new ArgumentException("state must be 'on' or 'off'", nameof(state));

			var normalized = state.Trim().ToLowerInvariant();
			if (normalized != "on" && normalized != "off")
				throw new ArgumentException("state must be 'on' or 'off'", nameof(state));

			NhdApiCommandSender.TrySend(this, $"config set device sinkpower {normalized} {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends a CEC command.
		/// </summary>
		/// <param name="command">"onetouchdisplay" or "standby"</param>
		public virtual void SendCecCommand(string command)
		{
			if (!SupportsCec)
				throw new NotSupportedException($"{ModelName} does not support CEC");

			if (string.IsNullOrWhiteSpace(command))
				throw new ArgumentException("command must be 'onetouchplay' or 'standby'", nameof(command));

			var normalized = command.Trim().ToLowerInvariant();
			if (normalized != "onetouchplay" && normalized != "standby")
				throw new ArgumentException("command must be 'onetouchplay' or 'standby'", nameof(command));

			NhdApiCommandSender.TrySend(this, $"config set device cec {normalized} {ApiEndpointReference}");
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
			if (string.IsNullOrWhiteSpace(data))
				throw new ArgumentException("CEC data cannot be empty", nameof(data));
			if (data.Contains("\""))
				throw new ArgumentException("CEC data cannot contain quote characters", nameof(data));

			NhdApiCommandSender.TrySend(this, $"cec \"{data.Trim()}\" {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends an IR command.
		/// </summary>
		/// <param name="data">IR data payload</param>
		public virtual void SendIrData(string data)
		{
			if (!SupportsIr)
				throw new NotSupportedException($"{ModelName} does not support IR");
			if (string.IsNullOrWhiteSpace(data))
				throw new ArgumentException("IR data cannot be empty", nameof(data));
			if (data.Contains("\""))
				throw new ArgumentException("IR data cannot contain quote characters", nameof(data));

			NhdApiCommandSender.TrySend(this, $"infrared \"{data.Trim()}\" {ApiEndpointReference}");
		}

		/// <summary>
		/// Sends an RS-232 command. Comm params are read from config.Rs232.
		/// </summary>
		/// <param name="data">Data string to send</param>
		public virtual void Send232Command(string data)
		{
			if (!Supports232)
				throw new NotSupportedException($"{ModelName} does not support RS-232");
			if (string.IsNullOrWhiteSpace(data))
				throw new ArgumentException("RS-232 data cannot be empty", nameof(data));
			if (data.Contains("\""))
				throw new ArgumentException("RS-232 data cannot contain quote characters", nameof(data));

			var serial = Config.Rs232 ?? new PepperDash.Essentials.Plugin.Config.Nhd232Properties();
			var parity = GetParityCode(serial.Parity);
			var baud = (int)serial.BaudRate;
			var bits = (int)serial.DataBits;
			var stop = (int)serial.StopBits;

			var command =
				$"serial -b {baud}-{bits}{parity}{stop} -r {(serial.AppendCr ? "on" : "off")} -n {(serial.AppendLf ? "on" : "off")} -h {(serial.SendAsHex ? "on" : "off")} \"{data.Trim()}\" {ApiEndpointReference}";

			NhdApiCommandSender.TrySend(this, command);
		}

		private static string GetParityCode(Parity parity)
		{
			switch (parity)
			{
				case Parity.Even:
					return "e";
				case Parity.Odd:
					return "o";
				case Parity.None:
				default:
					return "n";
			}
		}
	}
}
