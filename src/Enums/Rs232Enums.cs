namespace PepperDash.Essentials.Plugin.Enums
{
    public enum BaudRate
    {
        Baud2400 = 2400,
        Baud4800 = 4800,
        Baud9600 = 9600,
        Baud19200 = 19200,
        Baud38400 = 38400,
        Baud57600 = 57600,
        Baud115200 = 115200,
    }

    public enum Parity
    {
        None,
        Odd,
        Even,
    }

    public enum StopBits
    {
        One = 1,
        Two = 2,
    }

    public enum DataBits
    {
        Six = 6,
        Seven = 7,
        Eight = 8,
    }

    /// <summary>
    /// Controls whether a COM port (IR or RS-232) participates in matrix routing.
    /// When null or <see cref="NotRoutable"/>, the port is used only via direct send methods.
    /// </summary>
    public enum NhdComPortRoutingMode
    {
        /// <summary>No routing ports registered. Use SendIrData/Send232Command directly.</summary>
        NotRoutable = 0,
        /// <summary>Control system side — connected to the Crestron COM port. Data enters the NHD network here (e.g. Crestron sends 232 commands, IR blaster driven from controller).</summary>
        ControlSystem = 1,
        /// <summary>Device side — connected to the end device (e.g. display). Data exits the NHD network here.</summary>
        Device = 2,
    }
}
