using System;
using System.Collections.Generic;
using System.Linq;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixOutput : IRoutingOutputSlot
{
    private readonly NhdBaseDevice _device;
    private readonly eRoutingSignalType _supportedSignalTypes;
    private readonly Dictionary<eRoutingSignalType, IRoutingInputSlot> _currentRoutes;

    public NhdMatrixOutput(NhdBaseDevice device)
    {
        try
        {
            _device = device;
            _supportedSignalTypes = ResolveSupportedSignalTypes(device);
            _currentRoutes = BuildCurrentRoutes(_supportedSignalTypes);
            // TODO: subscribe to stream/route feedback when comms is implemented
        }
        catch (Exception ex)
        {
            Debug.LogMessage(ex, "Exception creating NhdMatrixOutput {ex}", this, ex.Message);
            _supportedSignalTypes = eRoutingSignalType.AudioVideo;
            _currentRoutes = BuildCurrentRoutes(_supportedSignalTypes);
        }
    }

    public bool SupportsMatrixSwitching => _device != null
        && !_device.SupportsMultiview
        && _device.InputPorts.Any(port => port != null);

    public string RxDeviceKey => _device.Key;

    public NhdBaseDevice Device => _device;

    public int SlotNumber => _device.DeviceId;

    public eRoutingSignalType SupportedSignalTypes => SupportsMatrixSwitching ? _supportedSignalTypes : 0;

    public string Name => _device.Name;

    public string Key => _device.Key;

    public Dictionary<eRoutingSignalType, IRoutingInputSlot> CurrentRoutes => _currentRoutes;

    public event EventHandler OutputSlotChanged;

    private static eRoutingSignalType ResolveSupportedSignalTypes(NhdBaseDevice device)
    {
        if (device == null)
            return eRoutingSignalType.AudioVideo;

        var signalTypes = device
            .InputPorts
            .Where(port => port != null)
            .Aggregate((eRoutingSignalType)0, (current, port) => current | port.Type);

        return signalTypes == 0 ? eRoutingSignalType.AudioVideo : signalTypes;
    }

    private static Dictionary<eRoutingSignalType, IRoutingInputSlot> BuildCurrentRoutes(eRoutingSignalType supportedSignalTypes)
    {
        var currentRoutes = new Dictionary<eRoutingSignalType, IRoutingInputSlot>();

        if (supportedSignalTypes.HasFlag(eRoutingSignalType.Audio))
            currentRoutes[eRoutingSignalType.Audio] = default;

        if (supportedSignalTypes.HasFlag(eRoutingSignalType.Video))
            currentRoutes[eRoutingSignalType.Video] = default;

        if (supportedSignalTypes.HasFlag(eRoutingSignalType.Usb) || supportedSignalTypes.HasFlag(NhdRoutingSignalTypes.UsbInput))
            currentRoutes[NhdRoutingSignalTypes.UsbInput] = default;

        if (supportedSignalTypes.HasFlag(eRoutingSignalType.Usb) || supportedSignalTypes.HasFlag(NhdRoutingSignalTypes.UsbOutput))
            currentRoutes[NhdRoutingSignalTypes.UsbOutput] = default;

        if (supportedSignalTypes.HasFlag(NhdRoutingSignalTypes.Ir))
            currentRoutes[NhdRoutingSignalTypes.Ir] = default;

        if (supportedSignalTypes.HasFlag(NhdRoutingSignalTypes.Serial))
            currentRoutes[NhdRoutingSignalTypes.Serial] = default;

        return currentRoutes;
    }

    public void SetInputRoute(eRoutingSignalType type, IRoutingInputSlot input)
    {
        if (_currentRoutes.ContainsKey(type))
            _currentRoutes[type] = input;
        else
            _currentRoutes.Add(type, input);

        OutputSlotChanged?.Invoke(this, new EventArgs());
    }
}
