using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private (double Top, double Bottom, bool Active) InitialAvoidanceBounds(List<CssInlineFragment> fragments, double height)
        {
            var top = 0d; var bottom = height; var active = false;
            if (_initialLetter is not { } initial) return (top, bottom, false);
            foreach (var piece in FramePieces(fragments))
                if (piece.Initial is { } box && ReferenceEquals(box.Placement, initial))
                {
                    active = true;
                    top = Math.Min(top, box.Top - initial.Margin.Top);
                    bottom = Math.Max(bottom, box.Top + box.Height + initial.Margin.Bottom);
                }
            if (!initial.Inline && !initial.Placed && fragments.Any(fragment => ReferenceEquals(fragment.Source.Initial, initial)))
            {
                active = true;
                top = Math.Min(top, initial.RelativeTop - initial.Margin.Top);
                bottom = Math.Max(bottom, initial.RelativeTop + initial.Size.Height + initial.Margin.Bottom);
            }
            return (top, bottom, active);
        }

        private (double Left, double Right, bool Obstructed) FloatBandFor(List<CssInlineFragment> fragments,
            double height, int count = int.MaxValue)
        {
            var band = FloatBand(height, count);
            if (_floatSpace is not { } space) return band;
            var range = InitialAvoidanceBounds(fragments, height);
            if (!range.Active) return band;
            // Text follows shape-outside; the initial's whole margin box must
            // avoid float margin boxes, including floats below its first line.
            var initialBand = space.Band(_containing.Width, _y + range.Top,
                Math.Max(.000001, range.Bottom - range.Top), count, useShapes: false);
            return (Math.Max(band.Left, initialBand.Left), Math.Min(band.Right, initialBand.Right),
                band.Obstructed || initialBand.Obstructed);
        }
    }
}
