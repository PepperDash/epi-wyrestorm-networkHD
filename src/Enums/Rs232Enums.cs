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
}
