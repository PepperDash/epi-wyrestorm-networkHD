using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Routing;

/// <summary>
/// Plugin-local input-slot abstraction for the NHD matrix router.
/// Replaces the core <c>IRoutingInputSlot</c> interface that was removed in
/// PepperDashEssentials v3-routing. The NHD router only ever consumes these slots
/// internally (the slot dictionaries are not exposed to core routing), so this
/// stays a plugin-private contract. Implemented by <see cref="NhdMatrixInput"/>
/// (a transmitter) and <see cref="NhdMatrixClearInput"/> (the "route off" sentinel).
/// </summary>
public interface INhdInputSlot : IKeyName
{
    /// <summary>Matrix slot number (0 for the clear/none input).</summary>
    int SlotNumber { get; }

    /// <summary>Signal types this input can carry.</summary>
    eRoutingSignalType SupportedSignalTypes { get; }

    /// <summary>Online feedback for the input (always online for the clear/none sentinel).</summary>
    BoolFeedback IsOnline { get; }
}
