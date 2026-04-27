using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Config
{
    /// <summary>
    /// Defines a named multiview preset that references either a built-in layout
    /// or a custom-config layout, plus optional window-to-TX routing targets.
    /// </summary>
    public class NhdMultiviewPresetProperties
    {
        /// <summary>
        /// Unique preset key selected by control logic.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Optional friendly label for UI/client display.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Defines how <see cref="Layout"/> is interpreted.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdMultiviewPresetLayoutSource LayoutSource { get; set; } = NhdMultiviewPresetLayoutSource.Config;

        /// <summary>
        /// Layout reference value.
        /// Controller: CTL preset scene name (for example "4-4" or other known scene name).
        /// Config: key from CustomMultiviewLayouts.
        /// </summary>
        public string Layout { get; set; }

        /// <summary>
        /// Per-window TX mapping for this preset.
        /// WindowReference is 1-based.
        /// </summary>
        public List<NhdMultiviewPresetWindowRouteProperties> WindowRoutes { get; set; } = new List<NhdMultiviewPresetWindowRouteProperties>();

        /// <summary>
        /// Optional audio behavior metadata for preset application.
        /// Defaults to NoChange when omitted.
        /// Leave Unknown or NoChange to keep current audio selection untouched.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdMultiviewAudioMode? AudioMode { get; set; } = NhdMultiviewAudioMode.NoChange;

        /// <summary>
        /// Optional audio window when AudioMode is Window.
        /// </summary>
        public int? AudioWindowReference { get; set; }

        /// <summary>
        /// Optional TX key used when AudioMode is Separate.
        /// </summary>
        public string AudioTxKey { get; set; }
    }

    /// <summary>
    /// Defines one window route target for a multiview preset.
    /// </summary>
    public class NhdMultiviewPresetWindowRouteProperties
    {
        /// <summary>
        /// 1-based multiview window/tile reference.
        /// </summary>
        public int WindowReference { get; set; }

        /// <summary>
        /// TX device key to route into WindowReference.
        /// </summary>
        public string TxKey { get; set; }
    }
}