namespace PepperDash.Essentials.Plugin.Enums
{
    /// <summary>
    /// Defines the signal-flow role of an NHD endpoint device.
    /// Used by devices that support multiple modes via configuration.
    /// Fixed-role devices (e.g. NHD-120-TX) hardcode this in their class instead.
    /// </summary>
    public enum NhdDeviceMode
    {
        /// <summary>Accepts local inputs and sends them to the NHD network as a stream.</summary>
        Transmitter = 0,
        /// <summary>Receives a stream from the NHD network and drives local outputs.</summary>
        Receiver = 1,
        /// <summary>Both transmits and receives simultaneously — has local inputs and outputs, and participates in the network in both directions.</summary>
        Transceiver = 2,
    }
}
