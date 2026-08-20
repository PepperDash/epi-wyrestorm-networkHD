using System;
using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixInput : INhdInputSlot
{
    private readonly NhdBaseDevice _device;
    private readonly eRoutingSignalType _supportedSignalTypes;

    public NhdMatrixInput(NhdBaseDevice device)
    {
        _device = device;
        _supportedSignalTypes = ResolveSupportedSignalTypes(device);
        _device.InputSyncStateChanged += HandleInputSyncStateChanged;
    }

    public string TxDeviceKey => _device.Key;

    public NhdBaseDevice Device => _device;

    public int SlotNumber => _device.MatrixInputSlot;

    public eRoutingSignalType SupportedSignalTypes => _supportedSignalTypes;

    public string Name => _device.Name;

    public BoolFeedback IsOnline => _device.IsOnline;

    public bool VideoSyncDetected => _device.InputSyncDetectedState;

    public string Key => _device.Key;

    public event EventHandler VideoSyncChanged;

    private static eRoutingSignalType ResolveSupportedSignalTypes(NhdBaseDevice device)
    {
        if (device == null)
            return eRoutingSignalType.AudioVideo;

        var signalTypes = device
            .OutputPorts
            .Where(port => port != null)
            .Aggregate((eRoutingSignalType)0, (current, port) => current | port.Type);

        return signalTypes == 0 ? eRoutingSignalType.AudioVideo : signalTypes;
    }

    private void HandleInputSyncStateChanged(object sender, NhdDeviceBoolStateChangedEventArgs args)
    {
        VideoSyncChanged?.Invoke(this, EventArgs.Empty);
    }
}
