using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Config
{
    public class Nhd232Properties
    {
        public BaudRate BaudRate { get; set; } = BaudRate.Baud9600;
        [JsonConverter(typeof(StringEnumConverter))]
        public DataBits DataBits { get; set; } = DataBits.Eight;
        [JsonConverter(typeof(StringEnumConverter))]
        public Parity Parity { get; set; } = Parity.None;
        [JsonConverter(typeof(StringEnumConverter))]
        public StopBits StopBits { get; set; } = StopBits.One;
        public bool AppendCr { get; set; }
        public bool AppendLf { get; set; }
        public bool SendAsHex { get; set; }
    }
}
