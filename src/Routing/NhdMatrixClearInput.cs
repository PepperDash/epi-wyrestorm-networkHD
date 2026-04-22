using System;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;

namespace PepperDash.Essentials.Plugin.Routing;

public class NhdMatrixClearInput : IRoutingInputSlot
{
    public string TxDeviceKey => string.Empty;

    public int SlotNumber => 0;

    public eRoutingSignalType SupportedSignalTypes => eRoutingSignalType.AudioVideo;

    public string Name => "None";

    public BoolFeedback IsOnline { get; private set; }

    public bool VideoSyncDetected => false;

    public string Key => "none";

    public event EventHandler VideoSyncChanged;

    public NhdMatrixClearInput()
    {
        IsOnline = new BoolFeedback("IsOnline", () => true);
        IsOnline.FireUpdate();
    }
}
