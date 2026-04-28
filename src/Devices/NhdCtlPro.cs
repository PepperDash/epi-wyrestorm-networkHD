using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Comms;

namespace PepperDash.Essentials.Plugin
{
	public class NhdCtlPro : NhdBaseDevice
	{
		public override bool IsTransmitter => false;
		public override bool SupportsCec => false;
		public override bool SupportsIr => false;
		public override bool Supports232 => false;

		public IBasicCommunication Comms { get; private set; }
		public NhdCtlSessionManager SessionManager { get; private set; }
		public string ApiUsername => string.IsNullOrWhiteSpace(Config?.ApiUsername) ? null : Config.ApiUsername.Trim();
		public string ApiPassword => Config?.ApiPassword ?? string.Empty;

		protected override bool AutoStartCommunicationMonitorInBase => false;

		protected override StatusMonitorBase BuildCommunicationMonitor()
		{
			return new NhdCtlCommunicationMonitor(this, 10000, 30000);
		}

		public NhdCtlPro(string key, string name, NhdDeviceProperties config, IBasicCommunication comms)
			: base(key, name, config, "NHD-CTL-PRO")
		{
			Comms = comms;
			AddStreamInputPort();
			AddStreamOutputPort();
		}

		protected override bool CustomActivate()
		{
			var result = base.CustomActivate();
			if (!result)
				return false;

			if (Comms != null)
			{
				SessionManager = new NhdCtlSessionManager(this);
				SessionManager.StartSessionLifecycle();
			}

			CommunicationMonitor.Start();

			return true;
		}

		public override bool Deactivate()
		{
			SessionManager?.StopSessionLifecycle();
			SessionManager = null;
			return base.Deactivate();
		}
	}
}
