namespace PepperDash.Essentials.Plugin.Enums
{
    /// <summary>
    /// Controls how a multi-stream decoder arranges simultaneous stream windows on its output.
    /// Set at runtime via <see cref="NhdBaseDevice.MultiStreamMode"/> and
    /// the device's <see cref="NhdBaseDevice.MaxStreamCount"/> is greater than 1.
    /// </summary>
    public enum NhdMultiStreamMode
    {
        /// <summary>
        /// Stream windows are arranged in a uniform tile grid.
        /// </summary>
        Tile = 0,
        /// <summary>
        /// Stream windows are arranged in an overlay/custom layout with independent positioning and sizing.
        /// </summary>
        Overlay = 1,
    }
}
