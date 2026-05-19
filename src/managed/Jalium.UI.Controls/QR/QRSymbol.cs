namespace Jalium.UI.Controls.QR;

/// <summary>
/// Result of encoding a string to a QR code symbol matrix.
/// </summary>
public sealed class QRSymbol
{
    /// <summary>Square matrix of dark (true) / light (false) modules, indexed [row, column].</summary>
    public bool[,] Modules { get; }
    public int ModuleCount { get; }
    public int Version { get; }
    public QRErrorCorrectionLevel ErrorCorrectionLevel { get; }
    public int Mask { get; }
    public QRMode Mode { get; }

    internal QRSymbol(bool[,] modules, int version, QRErrorCorrectionLevel ecc, int mask, QRMode mode)
    {
        Modules = modules;
        ModuleCount = modules.GetLength(0);
        Version = version;
        ErrorCorrectionLevel = ecc;
        Mask = mask;
        Mode = mode;
    }
}
