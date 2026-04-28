using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Comms
{
    public static class NhdApiCommandSender
    {
        private static readonly object QueueSync = new object();
        private static readonly Queue<QueuedCommand> PendingCommands = new Queue<QueuedCommand>();
        private static readonly TimeSpan CommandResponseTimeout = TimeSpan.FromSeconds(2);

        private sealed class QueuedCommand
        {
            public IKeyed Source { get; set; }
            public string Command { get; set; }
        }

        private static NhdCtlPro _boundCtl;
        private static string _inFlightCommand;
        private static DateTime _inFlightSentUtc;
        private static long _inFlightToken;
        private static Timer _inFlightTimer;
        private static readonly StringBuilder ReceiveBuffer = new StringBuilder();

        public static bool TrySend(IKeyed source, string command)
        {
            var sourceKey = source?.Key ?? "unknown";

            if (string.IsNullOrWhiteSpace(command))
            {
                Debug.LogError("[{SourceKey}] Refusing to send empty NetworkHD command", sourceKey);
                return false;
            }

            var ctl = DeviceManager.AllDevices.OfType<NhdCtlPro>().FirstOrDefault();
            if (ctl == null)
            {
                Debug.LogError("[{SourceKey}] No NHD-CTL device found. Unable to send command: {Command}", sourceKey, command);
                return false;
            }

            if (ctl.Comms == null)
            {
                Debug.LogError("[{SourceKey}] NHD-CTL comms is null. Unable to send command: {Command}", sourceKey, command);
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

            var normalizedCommand = command.Trim();

            lock (QueueSync)
            {
                BindCtlLocked(ctl);

                if (IsDuplicateCommandLocked(normalizedCommand))
                {
                    Debug.LogMessage(
                        Serilog.Events.LogEventLevel.Debug,
                        "Skipping duplicate queued/in-flight command: {Command}",
                        source ?? ctl,
                        normalizedCommand);
                    return true;
                }

                PendingCommands.Enqueue(new QueuedCommand
                {
                    Source = source ?? ctl,
                    Command = normalizedCommand,
                });

                PumpQueueLocked();
            }

            return true;
        }

        private static void BindCtlLocked(NhdCtlPro ctl)
        {
            if (ReferenceEquals(_boundCtl, ctl))
                return;

            if (_boundCtl?.Comms != null)
            {
                _boundCtl.Comms.TextReceived -= HandleCommsTextReceived;
            }

            _boundCtl = ctl;
            if (_boundCtl?.Comms != null)
            {
                _boundCtl.Comms.TextReceived += HandleCommsTextReceived;
            }

            ReceiveBuffer.Clear();
            ClearInFlightLocked();
        }

        private static void HandleCommsTextReceived(object sender, GenericCommMethodReceiveTextArgs args)
        {
            lock (QueueSync)
            {
                if (string.IsNullOrWhiteSpace(_inFlightCommand))
                    return;

                if (args == null || string.IsNullOrEmpty(args.Text))
                    return;

                ReceiveBuffer.Append(args.Text);

                var matched = false;
                var raw = ReceiveBuffer.ToString();
                var start = 0;
                for (var i = 0; i < raw.Length; i++)
                {
                    if (raw[i] != '\n')
                        continue;

                    var line = raw.Substring(start, i - start).Trim();
                    if (IsResponseLineForCommand(line, _inFlightCommand))
                    {
                        matched = true;
                    }

                    start = i + 1;
                }

                ReceiveBuffer.Clear();
                if (start < raw.Length)
                {
                    // Keep trailing partial line for the next receive event.
                    ReceiveBuffer.Append(raw.Substring(start));
                }

                if (!matched)
                    return;

                ClearInFlightLocked();
                PumpQueueLocked();
            }
        }

        private static void PumpQueueLocked()
        {
            if (!string.IsNullOrWhiteSpace(_inFlightCommand))
                return;

            if (_boundCtl?.Comms == null)
                return;

            if (PendingCommands.Count <= 0)
                return;

            if (!_boundCtl.Comms.IsConnected)
            {
                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Debug,
                    "Connecting NHD-CTL comms for queued command dispatch",
                    _boundCtl);
                _boundCtl.Comms.Connect();
            }

            if (!_boundCtl.Comms.IsConnected)
                return;

            var queued = PendingCommands.Dequeue();
            var commandWithDelimiter = queued.Command.EndsWith("\r\n", StringComparison.Ordinal)
                ? queued.Command
                : queued.Command + "\r\n";

            _boundCtl.Comms.SendText(commandWithDelimiter);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Debug, "NHD API >> {Command}", queued.Source, queued.Command);

            _inFlightCommand = queued.Command;
            _inFlightSentUtc = DateTime.UtcNow;
            _inFlightToken++;
            var token = _inFlightToken;

            _inFlightTimer?.Dispose();
            _inFlightTimer = new Timer(_ => HandleInFlightTimeout(token), null, CommandResponseTimeout, Timeout.InfiniteTimeSpan);
        }

        private static void HandleInFlightTimeout(long token)
        {
            lock (QueueSync)
            {
                if (token != _inFlightToken || string.IsNullOrWhiteSpace(_inFlightCommand))
                    return;

                Debug.LogMessage(
                    Serilog.Events.LogEventLevel.Warning,
                    "NHD API command timeout after {ElapsedMs}ms; releasing queue: {Command}",
                    _boundCtl,
                    (int)(DateTime.UtcNow - _inFlightSentUtc).TotalMilliseconds,
                    _inFlightCommand);

                ClearInFlightLocked();
                PumpQueueLocked();
            }
        }

        private static void ClearInFlightLocked()
        {
            _inFlightCommand = null;
            _inFlightSentUtc = default(DateTime);
            _inFlightTimer?.Dispose();
            _inFlightTimer = null;
        }

        private static bool IsDuplicateCommandLocked(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            if (!string.IsNullOrWhiteSpace(_inFlightCommand)
                && string.Equals(_inFlightCommand, command, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return PendingCommands.Any(x => string.Equals(x.Command, command, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsResponseLineForCommand(string line, string command)
        {
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(command))
                return false;

            var normalizedLine = NormalizeForMatch(line);
            var normalizedCommand = NormalizeForMatch(command);

            if (normalizedCommand == "config get devicelist")
                return normalizedLine.StartsWith("devicelist is", StringComparison.Ordinal);

            if (normalizedCommand == "config get name")
                return normalizedLine.Contains(" alias is ");

            if (normalizedCommand == "config get device status")
                return normalizedLine.StartsWith("devices status info:", StringComparison.Ordinal);

            if (normalizedCommand == "matrix get")
                return normalizedLine.StartsWith("matrix information:", StringComparison.Ordinal);

            if (normalizedCommand == "matrix video get")
                return normalizedLine.StartsWith("matrix video information:", StringComparison.Ordinal);

            if (normalizedCommand == "matrix audio get")
                return normalizedLine.StartsWith("matrix audio information:", StringComparison.Ordinal);

            if (normalizedCommand == "matrix usb get")
                return normalizedLine.StartsWith("matrix usb information:", StringComparison.Ordinal);

            if (normalizedCommand == "matrix serial get")
                return normalizedLine.StartsWith("matrix serial information:", StringComparison.Ordinal);

            if (normalizedCommand == "matrix infrared get")
                return normalizedLine.StartsWith("matrix infrared information:", StringComparison.Ordinal);

            if (normalizedCommand.StartsWith("mview get", StringComparison.Ordinal))
                return normalizedLine.StartsWith("mview information:", StringComparison.Ordinal);

            if (normalizedCommand.StartsWith("mscene get", StringComparison.Ordinal))
                return normalizedLine.StartsWith("mscene list:", StringComparison.Ordinal);

            if (normalizedCommand.StartsWith("mscene active ", StringComparison.Ordinal))
            {
                return normalizedLine.StartsWith(normalizedCommand, StringComparison.Ordinal)
                    && (normalizedLine.EndsWith(" success", StringComparison.Ordinal)
                        || normalizedLine.EndsWith(" failure", StringComparison.Ordinal));
            }

            return normalizedLine.StartsWith(normalizedCommand, StringComparison.Ordinal);
        }

        private static string NormalizeForMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            var pendingSpace = false;

            for (var i = 0; i < value.Length; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}