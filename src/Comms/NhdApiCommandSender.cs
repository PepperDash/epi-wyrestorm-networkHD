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
                Debug.LogError("[{SourceKey}] Refusing to send empty NetworkHD command", source.Key);
                return false;
            }

            var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
            if (ctl == null)
            {
                Debug.LogError("[{SourceKey}] No NHD-CTL device found. Unable to send command: {Command}", source.Key, command);
                return false;
            }

            if (ctl.Comms == null)
            {
                Debug.LogError("[{SourceKey}] NHD-CTL comms is null. Unable to send command: {Command}", source.Key, command);
                return false;
            }

            if (ctl.SessionManager != null && !ctl.SessionManager.IsReadyForApiCommands)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Debug,
                    "Skipping send while CTL session is not ready: {Command}",
                    source,
                    command);
                return false;
            }

            if (!ctl.Comms.IsConnected)
            {
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Connecting NHD-CTL comms", source);
                ctl.Comms.Connect();
            }

            var commandWithDelimiter = command.EndsWith("\r\n", StringComparison.Ordinal)
                ? command
                : command + "\r\n";

            ctl.Comms.SendText(commandWithDelimiter);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ NHD API >> {Command}", source, command);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "NHD API >> {Command}", source, command);
            return true;
        }
    }
}