using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugin.Routing;

/// <summary>
/// Plugin-specific routing signal flags used to preserve IR/serial/USB intent
/// when the referenced Essentials enum surface does not expose those names.
/// </summary>
public static class NhdRoutingSignalTypes
{
    public const eRoutingSignalType Ir = (eRoutingSignalType)(1 << 10);
    public const eRoutingSignalType Serial = (eRoutingSignalType)(1 << 11);
    public const eRoutingSignalType UsbInput = (eRoutingSignalType)(1 << 12);
    public const eRoutingSignalType UsbOutput = (eRoutingSignalType)(1 << 13);
}
