using System;
using System.Threading;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Comms
{
    public class NhdCtlCommunicationMonitor : StatusMonitorBase
    {
        private static readonly TimeSpan SessionActivityTimeout = TimeSpan.FromSeconds(150);
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(60);
        private const int HealthTimerPeriodMs = 2000;

        private readonly NhdCtlPro _ctl;
        private readonly IBasicCommunication _comms;
        private Timer _healthTimer;
        private DateTime _lastActivityUtc;
        private DateTime _lastProbeUtc;
        private bool? _transportHealthyState;

        public NhdCtlCommunicationMonitor(NhdCtlPro ctl, long warningTime, long errorTime)
            : base(ctl, warningTime, errorTime)
        {
            _ctl = ctl;
            _comms = ctl != null ? ctl.Comms : null;
        }

        public override void Start()
        {
            _lastActivityUtc = DateTime.UtcNow;
            _lastProbeUtc = DateTime.MinValue;
            _transportHealthyState = null;

            if (_comms != null)
            {
                _comms.TextReceived -= HandleCommsTextReceived;
                _comms.TextReceived += HandleCommsTextReceived;

                if (_comms is ISocketStatus socket)
                {
                    socket.ConnectionChange -= HandleConnectionChange;
                    socket.ConnectionChange += HandleConnectionChange;
                }
            }

            if (_ctl.SessionManager != null)
            {
                _ctl.SessionManager.SessionReadyStateChanged -= HandleSessionReadyStateChanged;
                _ctl.SessionManager.SessionReadyStateChanged += HandleSessionReadyStateChanged;
            }

            _healthTimer?.Dispose();
            _healthTimer = new Timer(HandleHealthTimer, null, HealthTimerPeriodMs, HealthTimerPeriodMs);

            EvaluateStatus();
            IsOnlineFeedback.FireUpdate();
        }

        public override void Stop()
        {
            if (_comms != null)
            {
                _comms.TextReceived -= HandleCommsTextReceived;

                if (_comms is ISocketStatus socket)
                {
                    socket.ConnectionChange -= HandleConnectionChange;
                }
            }

            if (_ctl.SessionManager != null)
            {
                _ctl.SessionManager.SessionReadyStateChanged -= HandleSessionReadyStateChanged;
            }

            _healthTimer?.Dispose();
            _healthTimer = null;

            StopErrorTimers();
            Status = MonitorStatus.StatusUnknown;
            _ctl.SetOnlineState(false);
        }

        private void HandleConnectionChange(object sender, GenericSocketStatusChageEventArgs args)
        {
            var connected = args?.Client?.IsConnected ?? (_comms != null && _comms.IsConnected);
            if (connected)
            {
                _lastActivityUtc = DateTime.UtcNow;
            }

            _ctl.SessionManager?.HandleCtlTransportConnectionChanged(connected);
            EvaluateStatus();
        }

        private void HandleCommsTextReceived(object sender, GenericCommMethodReceiveTextArgs args)
        {
            _lastActivityUtc = DateTime.UtcNow;
            EvaluateStatus();
        }

        private void HandleSessionReadyStateChanged(object sender, NhdCtlSessionReadyStateChangedEventArgs args)
        {
            EvaluateStatus();
        }

        private void EvaluateStatus()
        {
            var now = DateTime.UtcNow;
            var isConnected = _comms != null && _comms.IsConnected;
            var hasRecentActivity = now - _lastActivityUtc <= SessionActivityTimeout;
            var transportHealthy = isConnected && hasRecentActivity;

            if (_transportHealthyState != transportHealthy)
            {
                _transportHealthyState = transportHealthy;
                _ctl.SessionManager?.HandleCtlTransportConnectionChanged(transportHealthy);
            }

            var isReady = transportHealthy
                && _ctl.SessionManager != null
                && _ctl.SessionManager.IsReadyForApiCommands;

            if (isReady)
            {
                Status = MonitorStatus.IsOk;
                StopErrorTimers();
            }
            else
            {
                StartErrorTimers();
            }

            _ctl.SetOnlineState(isReady);
        }

        private void HandleHealthTimer(object state)
        {
            try
            {
                var now = DateTime.UtcNow;

                if (_ctl.SessionManager != null
                    && _comms != null
                    && _comms.IsConnected
                    && _ctl.SessionManager.IsReadyForApiCommands
                    && now - _lastProbeUtc >= ProbeInterval)
                {
                    _lastProbeUtc = now;
                    _ctl.SessionManager.ProbeSessionHealth("comm monitor health");
                }

                EvaluateStatus();
            }
            catch
            {
                // Monitor exceptions should not crash runtime threads.
            }
        }
    }
}
