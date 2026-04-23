using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Comms
{
    public class NhdCtlSessionManager
    {
        private static readonly Regex AliasLineRegex = new Regex(
            "^(?<hostname>\\S+?)(?:'s|’s)\\s+alias\\s+is\\s+(?<alias>\\S+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EndpointNotifyRegex = new Regex(
            "^notify\\s+endpoint\\s+(?<state>[+-])\\s+(?<reference>\\S+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly NhdCtlPro _ctl;
        private readonly CommunicationGather _gather;

        public NhdCtlSessionManager(NhdCtlPro ctl)
        {
            _ctl = ctl;
            _gather = new CommunicationGather(ctl.Comms, "\r\n");
            _gather.LineReceived += HandleLineReceived;
        }

        public void Start()
        {
            // Preferred endpoint references are aliases, but some replies still return hostnames.
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Starting NHD session manager; enabling alias mode and requesting identity list", _ctl);
            NhdApiCommandSender.TrySend(_ctl, "config set session alias on");
            NhdApiCommandSender.TrySend(_ctl, "config get name");
            NhdApiCommandSender.TrySend(_ctl, "config get devicelist");
        }

        private void HandleLineReceived(object sender, GenericCommMethodReceiveTextArgs args)
        {
            var line = (args.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line))
                return;

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] NHD API << {1}", _ctl, line);

            if (TryHandleAliasMappingLine(line))
                return;

            if (TryHandleEndpointNotifyLine(line))
                return;

            TryHandleDeviceListLine(line);
        }

        private bool TryHandleAliasMappingLine(string line)
        {
            var match = AliasLineRegex.Match(line);
            if (!match.Success)
                return false;

            var hostname = match.Groups["hostname"].Value.Trim();
            var aliasValue = match.Groups["alias"].Value.Trim();
            var alias = aliasValue.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : aliasValue;

            var endpoint = ResolveEndpoint(alias) ?? ResolveEndpoint(hostname);
            if (endpoint == null)
            {
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Alias mapping unresolved. Hostname='{1}', Alias='{2}'", _ctl, hostname, alias ?? "null");
                return true;
            }

            endpoint.SetResolvedHostname(hostname);
            endpoint.SetOnlineState(true);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Alias mapping resolved endpoint='{1}', Hostname='{2}', Alias='{3}'", _ctl, endpoint.Key, hostname, alias ?? "null");

            return true;
        }

        private bool TryHandleEndpointNotifyLine(string line)
        {
            var match = EndpointNotifyRegex.Match(line);
            if (!match.Success)
                return false;

            var isOnline = match.Groups["state"].Value == "+";
            var reference = match.Groups["reference"].Value.Trim();

            var endpoint = ResolveEndpoint(reference);
            endpoint?.SetOnlineState(isOnline);
            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Notify endpoint reference='{1}', state='{2}', resolvedEndpoint='{3}'", _ctl, reference, isOnline ? "online" : "offline", endpoint?.Key ?? "unresolved");

            return true;
        }

        private bool TryHandleDeviceListLine(string line)
        {
            const string prefix = "devicelist is";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var refs = line.Substring(prefix.Length).Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Processing devicelist with {1} references", _ctl, refs.Count);

            var listedEndpoints = new HashSet<NhdBaseDevice>();
            foreach (var reference in refs)
            {
                var endpoint = ResolveEndpoint(reference);
                if (endpoint == null)
                {
                    Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Devicelist reference unresolved: '{1}'", _ctl, reference);
                    continue;
                }

                listedEndpoints.Add(endpoint);
                endpoint.SetOnlineState(true);
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Devicelist marked endpoint online: '{1}' (ref='{2}')", _ctl, endpoint.Key, reference);
            }

            foreach (var endpoint in GetEndpoints().Where(e => !listedEndpoints.Contains(e)))
            {
                endpoint.SetOnlineState(false);
                Debug.LogMessage(Serilog.Events.LogEventLevel.Information, "$$$$$$$$$$ [{0}] Devicelist marked endpoint offline: '{1}'", _ctl, endpoint.Key);
            }

            return true;
        }

        private static IEnumerable<NhdBaseDevice> GetEndpoints()
        {
            return DeviceManager.AllDevices
                .OfType<NhdBaseDevice>()
                .Where(d => d is not NhdCtlPro);
        }

        private static NhdBaseDevice ResolveEndpoint(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            var value = reference.Trim();

            return GetEndpoints().FirstOrDefault(d =>
                string.Equals(d.ConfiguredAlias, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Hostname, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Key, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}