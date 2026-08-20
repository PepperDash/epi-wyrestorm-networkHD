using System;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Comms
{
    public class NhdEndpointCommunicationMonitor : StatusMonitorBase
    {
        private readonly NhdBaseDevice _endpoint;

        public NhdEndpointCommunicationMonitor(NhdBaseDevice endpoint, long warningTime, long errorTime)
            : base(endpoint, warningTime, errorTime)
        {
            _endpoint = endpoint;
        }

        public override void Start()
        {
            _endpoint.OnlineStateChanged -= HandleOnlineStateChanged;
            _endpoint.OnlineStateChanged += HandleOnlineStateChanged;

            UpdateStatus(_endpoint.OnlineState);
            IsOnlineFeedback.FireUpdate();
        }

        public override void Stop()
        {
            _endpoint.OnlineStateChanged -= HandleOnlineStateChanged;
            StopErrorTimers();
            Status = MonitorStatus.StatusUnknown;
        }

        private void HandleOnlineStateChanged(object sender, NhdDeviceBoolStateChangedEventArgs args)
        {
            UpdateStatus(args != null ? args.Value : _endpoint.OnlineState);
        }

        private void UpdateStatus(bool isOnline)
        {
            if (isOnline)
            {
                Status = MonitorStatus.IsOk;
                StopErrorTimers();
                return;
            }

            StartErrorTimers();
        }
    }
}
