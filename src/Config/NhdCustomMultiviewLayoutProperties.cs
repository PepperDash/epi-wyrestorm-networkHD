using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Config
{
    /// <summary>
    /// Defines a named custom multiview geometry profile with no source/content bindings.
    /// </summary>
    public class NhdCustomMultiviewLayoutProperties
    {
        /// <summary>
        /// Unique layout key used by client code to select this geometry profile.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Optional friendly name for UI display.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Tile or overlay mode to apply with this geometry profile.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdMultiStreamMode Mode { get; set; } = NhdMultiStreamMode.Tile;

        /// <summary>
        /// Reference output width used by this geometry profile.
        /// </summary>
        public int CanvasWidth { get; set; } = 1920;

        /// <summary>
        /// Reference output height used by this geometry profile.
        /// </summary>
        public int CanvasHeight { get; set; } = 1080;

        /// <summary>
        /// Optional audio mode metadata associated with this geometry profile.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdMultiviewAudioMode? AudioMode { get; set; }

        /// <summary>
        /// Optional audio window reference when <see cref="AudioMode"/> is window-based.
        /// </summary>
        public int? AudioWindowReference { get; set; }

        /// <summary>
        /// Window geometry definitions for this profile.
        /// </summary>
        public List<NhdCustomMultiviewWindowProperties> Windows { get; set; } = new List<NhdCustomMultiviewWindowProperties>();
    }

    /// <summary>
    /// Defines geometry and render metadata for one custom multiview window.
    /// </summary>
    public class NhdCustomMultiviewWindowProperties
    {
        /// <summary>
        /// 1-based window reference used to order or map this window in commands.
        /// </summary>
        public int WindowReference { get; set; }

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// Scale mode for content in this window.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public NhdMultiviewScaleMode Scale { get; set; } = NhdMultiviewScaleMode.Stretch;

        /// <summary>
        /// Optional rotation metadata (for example 0, 90, 180, 270) when supported by firmware.
        /// </summary>
        public int? Rotation { get; set; }
    }
}
