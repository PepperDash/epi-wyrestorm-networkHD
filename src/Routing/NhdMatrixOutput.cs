using System;
using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixOutput : IRoutingOutputSlot
{
    private readonly NhdBaseDevice _device;

    private readonly Dictionary<eRoutingSignalType, IRoutingInputSlot> _currentRoutes = new()
    {
        { eRoutingSignalType.Audio, default },
        { eRoutingSignalType.Video, default },
    };

    public NhdMatrixOutput(NhdBaseDevice device)
    {
        try
        {
            _device = device;
            // TODO: subscribe to stream/route feedback when comms is implemented
        }
        catch (Exception ex)
        {
            Debug.LogMessage(ex, "Exception creating NhdMatrixOutput {ex}", this, ex.Message);
        }
    }

    public string RxDeviceKey => _device.Key;

    public NhdBaseDevice Device => _device;

    public int SlotNumber => _device.DeviceId;

    public eRoutingSignalType SupportedSignalTypes => eRoutingSignalType.AudioVideo;

    public string Name => _device.Name;

    public string Key => _device.Key;

    public Dictionary<eRoutingSignalType, IRoutingInputSlot> CurrentRoutes => _currentRoutes;

    public event EventHandler OutputSlotChanged;

    public void SetInputRoute(eRoutingSignalType type, IRoutingInputSlot input)
    {
        if (_currentRoutes.ContainsKey(type))
            _currentRoutes[type] = input;
        else
            _currentRoutes.Add(type, input);

        OutputSlotChanged?.Invoke(this, new EventArgs());
    }
}
