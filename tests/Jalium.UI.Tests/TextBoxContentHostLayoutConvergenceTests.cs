using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// <see cref="TextBoxContentHost.ArrangeOverride"/> asks for a corrective measure pass when the
/// width it was arranged at differs from the width it was measured at. That request has to be
/// self-limiting: a TextBox template wraps PART_ContentHost in a ScrollViewer that measures with
/// infinite width and arranges at a finite one, so the mismatch is permanent. Requesting
/// unconditionally turned it into a layout loop — arrange invalidates measure, the frame
/// re-measures and re-arranges, arrange invalidates again — that never settles and pins the UI
/// thread at ~100% of a core for the lifetime of the window.
/// </summary>
public class TextBoxContentHostLayoutConvergenceTests
{
    [Fact]
    public void InfiniteMeasureWithFiniteArrange_StopsRequestingRemeasure()
    {
        var owner = new TextBox { Text = "a line of text long enough to have a real measured width" };
        var host = new TextBoxContentHost(owner);

        int requestsAcrossPasses = 0;
        for (int pass = 0; pass < 12; pass++)
        {
            host.Measure(new Size(double.PositiveInfinity, 100));
            Assert.True(host.IsMeasureValid);

            host.Arrange(new Rect(0, 0, 200, 100));
            if (!host.IsMeasureValid) requestsAcrossPasses++;
        }

        // A handful of corrective attempts is the intended behaviour; one per pass forever is the bug.
        Assert.InRange(requestsAcrossPasses, 0, 3);
    }

    [Fact]
    public void MatchingMeasureAndArrangeWidths_NeverRequestRemeasure()
    {
        var owner = new TextBox { Text = "short" };
        var host = new TextBoxContentHost(owner);

        for (int pass = 0; pass < 5; pass++)
        {
            host.Measure(new Size(200, 100));
            host.Arrange(new Rect(0, 0, 200, 100));
            Assert.True(host.IsMeasureValid);
        }
    }

    [Fact]
    public void ChangingArrangeWidth_StillGetsOneCorrectiveRequest()
    {
        var owner = new TextBox { Text = "a line of text long enough to have a real measured width" };
        var host = new TextBoxContentHost(owner);

        // Settle at a matching width first so the corrective budget is fresh.
        host.Measure(new Size(200, 100));
        host.Arrange(new Rect(0, 0, 200, 100));
        Assert.True(host.IsMeasureValid);

        // The parent now arranges wider than it measured — one corrective pass is expected.
        host.Arrange(new Rect(0, 0, 320, 100));
        Assert.False(host.IsMeasureValid);
    }
}
