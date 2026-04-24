using System;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixInput : IRoutingInputSlot
{
    private readonly NhdBaseDevice _device;

    public NhdMatrixInput(NhdBaseDevice device)
    {
        _device = device;
        _device.InputSyncStateChanged += HandleInputSyncStateChanged;
    }

    public string TxDeviceKey => _device.Key;

    public NhdBaseDevice Device => _device;

    public int SlotNumber => _device.DeviceId;

    public eRoutingSignalType SupportedSignalTypes => eRoutingSignalType.AudioVideo;

    public string Name => _device.Name;

    public BoolFeedback IsOnline => _device.IsOnline;

    public bool VideoSyncDetected => _device.InputSyncDetectedState;

    public string Key => _device.Key;

    public event EventHandler VideoSyncChanged;

    private void HandleInputSyncStateChanged(object sender, NhdDeviceBoolStateChangedEventArgs args)
    {
        VideoSyncChanged?.Invoke(this, EventArgs.Empty);
    }
}
