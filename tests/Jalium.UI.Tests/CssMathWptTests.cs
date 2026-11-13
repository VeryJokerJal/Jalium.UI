using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssMathWptTests
{
    [Fact]
    [Trait("WPT", "css/css-values/round-mod-rem-computed.html")]
    public void SteppedFunctions_MixedPercentageCasesMatchNativeReferencePixels()
    {
        Compare(75,
        [
            ("round(10%,1px)", 8), ("round(10%,5px)", 10),
            ("mod(10%,1px)", .5), ("mod(10%,5px)", 2.5),
            ("mod(18px,100% / 15)", 3), ("mod(-18px,100% / 10)", 4.5),
            ("rem(10%,1px)", .5), ("rem(10%,5px)", 2.5),
            ("rem(18px,100% / 15)", 3), ("calc(round(1px + 0%,1px + 0%))", 1),
            ("calc(mod(3px + 0%,2px + 0%))", 1), ("calc(rem(3px + 0%,2px + 0%))", 1)
        ]);
    }

    [Fact]
    [Trait("WPT", "css/css-values/sin-cos-tan-computed.html")]
    public void TrigonometricFunctions_NumberCasesDriveRealBoxWidthsAndPixels()
    {
        // The WPT numeric results are mapped onto 100px + result * 10px so
        // negative results remain observable as nonnegative painted widths.
        (string Expression, double Expected)[] cases =
        [
            ("cos(0)", 1), ("sin(0)", 0), ("tan(0)", 0),
            ("tan(315deg)", -1), ("tan(360deg)", 0), ("tan(405deg)", 1),
            ("calc(sin(pi/2 - pi/2))", 0), ("calc(cos(pi - 3.14159265358979323846))", 1),
            ("calc(cos(e - 2.7182818284590452354))", 1),
            ("calc(sin(100grad))", 1), ("calc(sin(0.25turn))", 1),
            ("calc(cos(sin(cos(pi) + 1)))", 1), ("calc(sin(tan(pi/4)*pi/2))", 1)
        ];
        Compare(150, cases.Select(c => ($"calc(100px + {c.Expression} * 10px)", 100 + c.Expected * 10)).ToArray());
    }

    private static void Compare(int width, (string Expression, double Expected)[] cases)
    {
        var source = new StackPanel(); var reference = new StackPanel();
        foreach (var item in cases)
        {
            var target = new Border { Height = 8, HorizontalAlignment = HorizontalAlignment.Left };
            source.Children.Add(target);
            Css.SetStyle(target, "background-color:lime; width:" + item.Expression);
            reference.Children.Add(new Border { Width = item.Expected, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Colors.Lime) });
        }
        var height = cases.Length * 8;
        foreach (var root in new[] { source, reference })
        {
            CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
        }
        for (var i = 0; i < cases.Length; i++)
            Assert.Equal(cases[i].Expected, ((Border)source.Children[i]).ActualWidth, 8);
        Assert.Equal(Pixels(reference, width, height), Pixels(source, width, height));
    }

    private static byte[] Pixels(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        bitmap.Clear(Colors.White); bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return pixels;
    }
}
