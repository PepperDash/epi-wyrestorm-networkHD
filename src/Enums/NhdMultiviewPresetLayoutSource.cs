namespace PepperDash.Essentials.Plugin.Enums
{
    /// <summary>
    /// Selects whether a multiview preset layout is sourced from the controller
    /// (built-in CTL scene name) or from this device's config-defined custom layouts.
    /// </summary>
    public enum NhdMultiviewPresetLayoutSource
    {
        /// <summary>
        /// Use a controller-known multiview scene name (mscene active/change flow).
        /// </summary>
        Controller = 0,

        /// <summary>
        /// Use a layout key from CustomMultiviewLayouts (mview set flow).
        /// </summary>
        Config = 1,
    }
}