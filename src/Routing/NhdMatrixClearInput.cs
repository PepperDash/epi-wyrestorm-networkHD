using System;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixClearInput : IRoutingInputSlot
{
    private readonly eRoutingSignalType _supportedSignalTypes;

    public string TxDeviceKey => string.Empty;

    public int SlotNumber => 0;

    public eRoutingSignalType SupportedSignalTypes => _supportedSignalTypes;

    public string Name => "None";

    public BoolFeedback IsOnline { get; private set; }

    public bool VideoSyncDetected => false;

    public string Key => "none";

    public event EventHandler VideoSyncChanged;

    public NhdMatrixClearInput(eRoutingSignalType supportedSignalTypes = eRoutingSignalType.AudioVideo)
    {
        _supportedSignalTypes = supportedSignalTypes == 0
            ? eRoutingSignalType.AudioVideo
            : supportedSignalTypes;

        IsOnline = new BoolFeedback("IsOnline", () => true);
        IsOnline.FireUpdate();
    }
}
