using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

public partial class TextBox
{
    private CssInputTextEmphasis InputEmphasis => new(this);

    private double InputBodyY(double nativeY, double nativeHeight) =>
        InputEmphasis.BodyY(nativeY, nativeHeight);

    private double InputRowY(double nativeY, double nativeHeight) =>
        InputEmphasis.RowY(nativeY, nativeHeight);

    private double InputNativeY(string text, double width, double annotatedY)
    {
        var emphasis = InputEmphasis;
        if (!emphasis.Active) return annotatedY;
        var aligned = GetMacOSAlignedLayout(text, width);
        var height = aligned is not null && aligned.Row(0, false, out var row)
            ? row.Height : emphasis.NaturalHeight;
        return emphasis.NativeY(annotatedY, Math.Max(1, height));
    }

    private Rect InputCompositionRect(Rect rectangle, Rect anchor)
    {
        var emphasis = InputEmphasis;
        return new Rect(rectangle.X, anchor.Y +
            emphasis.BodyY(rectangle.Y - anchor.Y, rectangle.Height) - emphasis.Above,
            rectangle.Width, rectangle.Height);
    }
}
