namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how a <see cref="DataGridLength"/> value is interpreted.
/// </summary>
public enum DataGridLengthUnitType
{
    /// <summary>
    /// The size is calculated from the unconstrained sizes of the cells and header.
    /// </summary>
    Auto,

    /// <summary>
    /// The value is expressed in device-independent pixels.
    /// </summary>
    Pixel,

    /// <summary>
    /// The size is calculated from the unconstrained sizes of the cells.
    /// </summary>
    SizeToCells,

    /// <summary>
    /// The size is calculated from the unconstrained size of the header.
    /// </summary>
    SizeToHeader,

    /// <summary>
    /// The value is a weighted proportion of the available space.
    /// </summary>
    Star,
}
