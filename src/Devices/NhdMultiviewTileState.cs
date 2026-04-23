namespace PepperDash.Essentials.Plugin;

public sealed class NhdMultiviewTileState
{
    public NhdMultiviewTileState(
        int tileNumber,
        string sourceReference,
        int x,
        int y,
        int width,
        int height,
        string scaleMode)
    {
        TileNumber = tileNumber;
        SourceReference = sourceReference;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ScaleMode = scaleMode;
    }

    public int TileNumber { get; }
    public string SourceReference { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public string ScaleMode { get; }

    public bool IsPlaceholder => string.IsNullOrWhiteSpace(SourceReference);

    public static NhdMultiviewTileState CreatePlaceholder(int tileNumber)
    {
        return new NhdMultiviewTileState(tileNumber, null, 0, 0, 0, 0, null);
    }
}
