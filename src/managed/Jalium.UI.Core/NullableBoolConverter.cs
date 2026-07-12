using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI;

/// <summary>
/// Converts values to and from nullable Boolean values and exposes the three
/// values supported by a nullable Boolean editor.
/// </summary>
public class NullableBoolConverter : NullableConverter
{
    [ThreadStatic]
    private static StandardValuesCollection? s_standardValues;

    /// <summary>
    /// Initializes a new instance of the <see cref="NullableBoolConverter"/> class.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "The nullable type passed to NullableConverter is statically fixed to Boolean.")]
    public NullableBoolConverter()
        : base(typeof(bool?))
    {
    }

    /// <inheritdoc />
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    /// <inheritdoc />
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    /// <inheritdoc />
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        return s_standardValues ??= new StandardValuesCollection(
            new bool?[] { true, false, null });
    }
}
