using System;
using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Comms
{
    public static class NhdApiCommandSender
    {
        public static bool TrySend(IKeyed source, string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                Debug.LogError("[{0}] Refusing to send empty NetworkHD command", source.Key);
                return false;
            }

            var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
            if (ctl == null)
            {
                Debug.LogError("[{0}] No NHD-CTL device found. Unable to send command: {1}", source.Key, command);
                return false;
            }

            if (ctl.Comms == null)
            {
                Debug.LogError("[{0}] NHD-CTL comms is null. Unable to send command: {1}", source.Key, command);
                return false;
            }

            if (!ctl.Comms.IsConnected)
            {
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Connecting NHD-CTL comms", source);
                ctl.Comms.Connect();
            }

            var commandWithDelimiter = command.EndsWith("\n", StringComparison.Ordinal)
                ? command
                : command + "\n";

            ctl.Comms.SendText(commandWithDelimiter);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] NHD API >> {1}", source, command);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "[{0}] NHD API >> {1}", source, command);
            return true;
        }
    }
}