using PepperDash.Essentials.Core;
using PepperDash.Essentials.Plugin.Mock;

namespace PepperDash.Essentials.Plugin.Routing;

/// <summary>
/// <see cref="INhdInputSlot"/> adapter for a <see cref="MockNhdTx"/>, so mock transmitters can be
/// discovered by <see cref="NhdGlobalRouter"/> alongside real <see cref="NhdMatrixInput"/> (real
/// transmitter) slots. Always reports online/synced since there is no real hardware link to track.
/// </summary>
public class MockNhdMatrixInput : INhdInputSlot
{
    private readonly MockNhdTx _device;

    public MockNhdMatrixInput(MockNhdTx device)
    {
        _device = device;
        IsOnline = new BoolFeedback("IsOnline", () => true);
        IsOnline.FireUpdate();
    }

    public MockNhdTx Device => _device;

    /// <summary>Mock inputs are not part of a physical matrix slot numbering scheme.</summary>
    public int SlotNumber => 0;

    public eRoutingSignalType SupportedSignalTypes => eRoutingSignalType.AudioVideo;

    public BoolFeedback IsOnline { get; }

    public string Name => _device.Name;

    public string Key => _device.Key;
}
