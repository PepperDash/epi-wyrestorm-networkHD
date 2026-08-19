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
        string scaleMode,
        int? zOrder = null)
    {
        TileNumber = tileNumber;
        SourceReference = sourceReference;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        ScaleMode = scaleMode;
        // Absent explicit stacking-order information from the protocol, tiles default to
        // stacking in tile-number order (e.g. tile 1 - typically the presentation/base tile - is
        // drawn first/lowest, later tiles are drawn on top).
        ZOrder = zOrder ?? tileNumber;
    }

    public int TileNumber { get; }
    public string SourceReference { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public string ScaleMode { get; }
    public int ZOrder { get; }

    public bool IsPlaceholder => string.IsNullOrWhiteSpace(SourceReference);

public static NhdMultiviewTileState CreatePlaceholder(int tileNumber)
{
    return new NhdMultiviewTileState(tileNumber, string.Empty, 0, 0, 0, 0, string.Empty);
}

    /// <summary>
    /// Returns a copy of this tile with <see cref="SourceReference"/> replaced by
    /// <paramref name="resolvedSourceReference"/>, leaving geometry/scale/z-order unchanged. Used
    /// to normalize a raw hardware-reported API reference (alias/hostname) into an Essentials
    /// device key once resolved (see <c>NhdCtlSessionManager.FinalizePendingMviewInformationEntry</c>).
    /// </summary>
    public NhdMultiviewTileState WithSourceReference(string resolvedSourceReference)
    {
        return new NhdMultiviewTileState(TileNumber, resolvedSourceReference, X, Y, Width, Height, ScaleMode, ZOrder);
    }
}
