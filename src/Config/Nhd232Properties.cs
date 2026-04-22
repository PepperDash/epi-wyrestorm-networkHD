using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin.Config
{
    public class Nhd232Properties
    {
        public BaudRate BaudRate { get; set; } = BaudRate.Baud9600;
        public DataBits DataBits { get; set; } = DataBits.Eight;
        public Parity Parity { get; set; } = Parity.None;
        public StopBits StopBits { get; set; } = StopBits.One;
        public bool AppendCr { get; set; }
        public bool AppendLf { get; set; }
        public bool SendAsHex { get; set; }
    }
}
