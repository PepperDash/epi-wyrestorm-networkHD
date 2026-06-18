using System;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixClearInput : INhdInputSlot
{
    private readonly eRoutingSignalType _supportedSignalTypes;

    public string TxDeviceKey => string.Empty;

    public int SlotNumber => 0;

    public eRoutingSignalType SupportedSignalTypes => _supportedSignalTypes;

    public string Name => "None";

    public BoolFeedback IsOnline { get; private set; }

    public bool VideoSyncDetected => false;

    public string Key => "none";

    // The "route off" sentinel has no backing endpoint, so sync state never changes.
    public event EventHandler VideoSyncChanged { add { } remove { } }

    public NhdMatrixClearInput(eRoutingSignalType supportedSignalTypes = eRoutingSignalType.AudioVideo)
    {
        _supportedSignalTypes = supportedSignalTypes == 0
            ? eRoutingSignalType.AudioVideo
            : supportedSignalTypes;

        IsOnline = new BoolFeedback("IsOnline", () => true);
        IsOnline.FireUpdate();
    }
}
