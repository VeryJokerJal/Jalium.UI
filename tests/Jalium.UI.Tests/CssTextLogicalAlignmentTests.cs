using Jalium.UI.Controls;
using Jalium.UI.Controls.TextEffects;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextLogicalAlignmentTests
{
    [Theory]
    [InlineData("direction:ltr; text-align:start", TextAlignment.Left)]
    [InlineData("direction:ltr; text-align:end", TextAlignment.Right)]
    [InlineData("text-align:start; direction:rtl", TextAlignment.Right)]
    [InlineData("direction:rtl; text-align:end", TextAlignment.Left)]
    [InlineData("direction:rtl; text-align:left", TextAlignment.Left)]
    [InlineData("direction:rtl; text-align:right", TextAlignment.Right)]
    public void LogicalAlignmentUsesComputedDirectionForTextBlock(string style, TextAlignment expected)
    {
        var block = CreateBlock();
        Css.SetStyle(block, style);

        Assert.Equal(expected, block.TextAlignment);
        AssertOrigin(block, expected);
    }

    [Fact]
    public void LogicalAlignmentTracksInheritedDirectionChanges()
    {
        var host = new StackPanel();
        var block = CreateBlock();
        host.Children.Add(block);
        Css.SetStyle(block, "text-align:start");

        Css.SetStyle(host, "direction:rtl");
        Assert.Equal(FlowDirection.RightToLeft, block.FlowDirection);
        AssertOrigin(block, TextAlignment.Right);
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal(TextAlignment.Right, block.TextAlignment);

        Css.SetStyle(host, "direction:ltr");
        Assert.Equal(FlowDirection.LeftToRight, block.FlowDirection);
        AssertOrigin(block, TextAlignment.Left);
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        Assert.Equal(TextAlignment.Left, block.TextAlignment);

        block.TextAlignment = TextAlignment.Center;
        Css.SetStyle(host, "direction:rtl");
        AssertOrigin(block, TextAlignment.Center);
    }

    [Fact]
    public void InheritedLogicalAlignmentUsesTheChildDirection()
    {
        var host = new StackPanel();
        var block = CreateBlock();
        host.Children.Add(block);
        Css.SetStyle(host, "direction:ltr; text-align:start");
        Css.SetStyle(block, "direction:rtl");

        AssertOrigin(block, TextAlignment.Right);
    }

    [Fact]
    public void InheritedAlignmentChangesRepaintTheChild()
    {
        var host = new StackPanel();
        var block = CreateBlock();
        host.Children.Add(block);
        Css.SetStyle(host, "text-align:left");
        AssertOrigin(block, TextAlignment.Left);

        Css.SetStyle(host, "text-align:right");
        AssertOrigin(block, TextAlignment.Right);
    }

    [Fact]
    public void ExplicitInheritResolvesStartInTheChildDirection()
    {
        var host = new StackPanel();
        var block = CreateBlock();
        host.Children.Add(block);
        Css.SetStyle(host, "direction:ltr; text-align:start");
        Css.SetStyle(block, "direction:rtl; text-align:inherit");

        Assert.Equal(TextAlignment.Right, block.TextAlignment);
        AssertOrigin(block, TextAlignment.Right);
    }

    [Fact]
    public void NativeTextAlignmentRemainsPhysicalWithoutCssAlignment()
    {
        var block = CreateBlock();
        block.FlowDirection = FlowDirection.RightToLeft;

        AssertOrigin(block, TextAlignment.Left);
    }

    [Theory]
    [InlineData("direction:rtl; text-align:start", TextAlignment.Right)]
    [InlineData("direction:rtl; text-align:end", TextAlignment.Left)]
    public void OtherDirectTextControlsReceivePhysicalAlignment(string style, TextAlignment expected)
    {
        var textBox = new TextBox { Text = "abc" };
        var accessText = new AccessText { Text = "abc" };
        Css.SetStyle(textBox, style);
        Css.SetStyle(accessText, style);

        Assert.Equal(expected, textBox.TextAlignment);
        Assert.Equal(expected, accessText.TextAlignment);
    }

    [Fact]
    public void TextBoxDrawingTracksInheritedLogicalAlignment()
    {
        var host = new StackPanel();
        var textBox = new TextBox { Text = "abc", FontSize = 20, Foreground = Brushes.Black };
        host.Children.Add(textBox);
        Css.SetStyle(host, "direction:rtl; text-align:start");

        Assert.InRange(TextBoxOrigin(textBox), 100, 180);

        Css.SetStyle(host, "direction:ltr; text-align:start");
        Assert.InRange(TextBoxOrigin(textBox), 0, 20);
    }

    [Fact]
    public void AccessTextUnderlineTracksInheritedLogicalAlignment()
    {
        var host = new StackPanel();
        var accessText = new AccessText
        {
            Text = "_abc", FontSize = 20, Foreground = Brushes.Black,
        };
        host.Children.Add(accessText);
        Css.SetStyle(host, "direction:rtl; text-align:start");

        var rtl = AccessTextPlacement(accessText);
        Assert.InRange(rtl.Underline, 100, 180);
        Assert.InRange(rtl.Text, 100, 180);

        Css.SetStyle(host, "direction:ltr; text-align:start");
        var ltr = AccessTextPlacement(accessText);
        Assert.InRange(ltr.Underline, 0, 20);
        Assert.InRange(ltr.Text, 0, 20);
    }

    [Fact]
    public void TextEffectPresenterTracksInheritedLogicalAlignment()
    {
        var host = new StackPanel();
        var presenter = new TextEffectPresenter
        {
            Text = "abc", FontSize = 20, Foreground = Brushes.Black,
        };
        host.Children.Add(presenter);
        Css.SetStyle(host, "direction:rtl; text-align:start");

        Assert.Contains(PresenterTransforms(presenter), x => x > 100);

        Css.SetStyle(host, "direction:ltr; text-align:start");
        Assert.DoesNotContain(PresenterTransforms(presenter), x => x > 1);
    }

    private static TextBlock CreateBlock() => new()
    {
        Text = "abc", FontSize = 20, Foreground = Brushes.Black,
    };

    private static void AssertOrigin(TextBlock block, TextAlignment alignment)
    {
        block.Measure(new Size(180, 50));
        block.Arrange(new Rect(0, 0, 180, 50));
        var context = new TextDrawingContext();
        block.Render(context);
        var (text, x) = Assert.Single(context.Texts);
        Assert.Equal("abc", text.Text);
        switch (alignment)
        {
            case TextAlignment.Left:
                Assert.InRange(x, 0, 1);
                break;
            case TextAlignment.Center:
                Assert.InRange(x, 40, 110);
                break;
            case TextAlignment.Right:
                Assert.InRange(x, 110, 180);
                break;
        }
    }

    private static double TextBoxOrigin(TextBox textBox)
    {
        textBox.Measure(new Size(180, 50));
        textBox.Arrange(new Rect(0, 0, 180, 50));
        var context = new TextDrawingContext();
        textBox.Render(context);
        return Assert.Single(context.Texts, item => item.Text.Text == "abc").X;
    }

    private static (double Underline, double Text) AccessTextPlacement(AccessText accessText)
    {
        accessText.Measure(new Size(180, 50));
        accessText.Arrange(new Rect(0, 0, 180, 50));
        var context = new TextDrawingContext();
        accessText.Render(context);
        return (Assert.Single(context.Lines),
            Assert.Single(context.Texts, item => item.Text.Text == "abc").X);
    }

    private static IReadOnlyList<double> PresenterTransforms(TextEffectPresenter presenter)
    {
        presenter.Measure(new Size(180, 50));
        presenter.Arrange(new Rect(0, 0, 180, 50));
        var context = new TextDrawingContext();
        presenter.Render(context);
        return context.TransformXs;
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText Text, double X)> Texts { get; } = [];
        public List<double> Lines { get; } = [];
        public List<double> TransformXs { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X));
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add(point0.X);
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform)
        {
            if (transform is TranslateTransform translate) TransformXs.Add(translate.X);
        }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
