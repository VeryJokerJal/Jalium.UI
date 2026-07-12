using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class TextBlockWpfParityTests
{
    [Fact]
    public void DefaultsMatchWpfSurface()
    {
        var textBlock = new TextBlock();

        Assert.Null(textBlock.Background);
        Assert.True(double.IsNaN(textBlock.BaselineOffset));
        Assert.Equal(FontStretches.Normal, textBlock.FontStretch);
        Assert.True(double.IsNaN(textBlock.LineHeight));
        Assert.Equal(LineStackingStrategy.MaxHeight, textBlock.LineStackingStrategy);
        Assert.Equal(new Thickness(0), textBlock.Padding);
        Assert.Empty(textBlock.TextDecorations);
        Assert.Empty(textBlock.TextEffects);
        Assert.False(textBlock.IsHyphenationEnabled);
        Assert.Equal(LineBreakCondition.BreakDesired, textBlock.BreakBefore);
        Assert.Equal(LineBreakCondition.BreakDesired, textBlock.BreakAfter);
        Assert.Empty(textBlock.Inlines);

        var second = new TextBlock();
        Assert.NotSame(textBlock.TextDecorations, second.TextDecorations);
        Assert.NotSame(textBlock.TextEffects, second.TextEffects);
    }

    [Fact]
    public void InlineConstructorRendersRunAndReflectsComplexContentThroughTextProperty()
    {
        var run = new Run("from inline");
        var textBlock = new TextBlock(run);

        var context = Render(textBlock);

        Assert.Equal("from inline", textBlock.Text);
        Assert.Same(run, Assert.Single(textBlock.Inlines));
        Assert.Equal("from inline", Assert.Single(context.Texts).text.Text);
    }

    [Fact]
    public void InlineAndNestedRunMutationsInvalidateRenderedContent()
    {
        var first = new Run("A");
        var nested = new Run("B");
        var span = new Span(nested);
        var textBlock = new TextBlock(first);
        textBlock.Inlines.Add(span);
        textBlock.Inlines.Add(new LineBreak());
        textBlock.Inlines.Add("C");

        var initial = Render(textBlock);
        Assert.Equal(new[] { "AB", "C" }, initial.Texts.Select(item => item.text.Text));

        nested.Text = " changed";
        first.Text = "First";
        var changed = Render(textBlock);

        Assert.Equal(new[] { "First changed", "C" }, changed.Texts.Select(item => item.text.Text));
    }

    [Fact]
    public void AllCollectionMutationPathsMaintainOwnershipAndSiblingLinks()
    {
        var textBlock = new TextBlock();
        var first = new Run("first");
        var middle = new Run("middle");
        var last = new Run("last");
        textBlock.Inlines.Add(first);
        textBlock.Inlines.Add(last);
        textBlock.Inlines.Insert(1, middle);

        Assert.Same(middle, first.NextInline);
        Assert.Same(first, middle.PreviousInline);
        Assert.Same(last, middle.NextInline);
        Assert.Same(middle, last.PreviousInline);

        var replacement = new Run("replacement");
        textBlock.Inlines[1] = replacement;
        Assert.Null(middle.PreviousInline);
        Assert.Null(middle.NextInline);
        Assert.Same(replacement, first.NextInline);
        Assert.Same(first, replacement.PreviousInline);

        textBlock.Inlines.RemoveAt(0);
        Assert.Null(replacement.PreviousInline);
        Assert.Same(last, replacement.NextInline);
        Assert.Throws<InvalidOperationException>(() => new Span().Inlines.Add(replacement));
    }

    [Fact]
    public void TextAndInlineViewsStayConsistentWithoutChangingWpfTextSemantics()
    {
        var textBlock = new TextBlock { Text = "property text" };
        var run = Assert.IsType<Run>(Assert.Single(textBlock.Inlines));

        Assert.Equal("property text", run.Text);
        Assert.True(textBlock.ShouldSerializeText());

        run.Text = "inline text";
        var context = Render(textBlock);

        Assert.Equal("inline text", textBlock.Text);
        Assert.Equal("inline text", Assert.Single(context.Texts).text.Text);
        Assert.False(textBlock.ShouldSerializeText());

        textBlock.Text = "property text";
        Assert.Equal("property text", Assert.IsType<Run>(Assert.Single(textBlock.Inlines)).Text);
    }

    [Fact]
    public void LayoutAndDrawingPropertiesDriveTheExistingRenderer()
    {
        var background = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        var textBlock = new TextBlock
        {
            Text = "layout",
            Background = background,
            Padding = new Thickness(11, 7, 13, 5),
            FontStretch = FontStretches.Expanded,
            LineHeight = 30,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextDecorations = new TextDecorationCollection
            {
                new TextDecoration(TextDecorationLocation.Underline, null, 2, 0, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel),
            },
        };

        textBlock.Measure(new Size(300, 200));
        Assert.True(textBlock.DesiredSize.Width >= 24);
        Assert.True(textBlock.DesiredSize.Height >= 42);

        var context = Render(textBlock, new Size(300, 80));
        var drawnText = Assert.Single(context.Texts);
        Assert.True(drawnText.origin.X >= textBlock.Padding.Left);
        Assert.Equal(FontStretches.Expanded.ToOpenTypeStretch(), drawnText.text.FontStretch);
        Assert.Contains(context.Rectangles, rectangle => ReferenceEquals(rectangle.brush, background));
        Assert.Single(context.Lines);
    }

    [Fact]
    public void AttachedPropertyAccessorsRoundTripOnAnyDependencyObject()
    {
        var target = new Border();
        var family = new FontFamily("Test Family");
        var foreground = new SolidColorBrush(Color.FromRgb(1, 2, 3));

        TextBlock.SetBaselineOffset(target, 4);
        TextBlock.SetFontFamily(target, family);
        TextBlock.SetFontSize(target, 18);
        TextBlock.SetFontStretch(target, FontStretches.Condensed);
        TextBlock.SetFontStyle(target, FontStyles.Italic);
        TextBlock.SetFontWeight(target, FontWeights.Bold);
        TextBlock.SetForeground(target, foreground);
        TextBlock.SetLineHeight(target, 24);
        TextBlock.SetLineStackingStrategy(target, LineStackingStrategy.BlockLineHeight);
        TextBlock.SetTextAlignment(target, TextAlignment.Right);

        Assert.Equal(4, TextBlock.GetBaselineOffset(target));
        Assert.Equal(family, TextBlock.GetFontFamily(target));
        Assert.Equal(18, TextBlock.GetFontSize(target));
        Assert.Equal(FontStretches.Condensed, TextBlock.GetFontStretch(target));
        Assert.Equal(FontStyles.Italic, TextBlock.GetFontStyle(target));
        Assert.Equal(FontWeights.Bold, TextBlock.GetFontWeight(target));
        Assert.Same(foreground, TextBlock.GetForeground(target));
        Assert.Equal(24, TextBlock.GetLineHeight(target));
        Assert.Equal(LineStackingStrategy.BlockLineHeight, TextBlock.GetLineStackingStrategy(target));
        Assert.Equal(TextAlignment.Right, TextBlock.GetTextAlignment(target));
    }

    [Fact]
    public void TypographyWrapperDelegatesToAttachedProperties()
    {
        var textBlock = new TextBlock();
        var first = textBlock.Typography;
        var second = textBlock.Typography;

        Assert.NotSame(first, second);
        first.StandardLigatures = false;
        first.Kerning = false;
        first.SlashedZero = true;

        Assert.False(Jalium.UI.Documents.Typography.GetStandardLigatures(textBlock));
        Assert.False(Jalium.UI.Documents.Typography.GetKerning(textBlock));
        Assert.True(Jalium.UI.Documents.Typography.GetSlashedZero(textBlock));
        Assert.False(second.StandardLigatures);
    }

    [Fact]
    public void SerializationAndMarkupContractsHaveObservableBehavior()
    {
        var textBlock = new TextBlock();
        Assert.False(textBlock.ShouldSerializeBaselineOffset());
        textBlock.BaselineOffset = 3;
        Assert.True(textBlock.ShouldSerializeBaselineOffset());
        textBlock.BaselineOffset = double.NaN;
        Assert.False(textBlock.ShouldSerializeBaselineOffset());

        var addChild = Assert.IsAssignableFrom<IAddChild>(textBlock);
        addChild.AddText("literal");
        addChild.AddChild(new Run(" run"));
        Assert.Equal(new[] { "literal", " run" }, textBlock.Inlines.Cast<Run>().Select(run => run.Text));

        var services = Assert.IsAssignableFrom<IServiceProvider>(textBlock);
        Assert.Null(services.GetService(typeof(TextBlockWpfParityTests)));
        Assert.Throws<ArgumentNullException>(() => services.GetService(null!));
    }

    private static RecordingDrawingContext Render(TextBlock textBlock, Size? size = null)
    {
        var finalSize = size ?? new Size(300, 80);
        textBlock.Measure(finalSize);
        textBlock.Arrange(new Rect(finalSize));
        var context = new RecordingDrawingContext();
        textBlock.Render(context);
        return context;
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText text, Point origin)> Texts { get; } = [];
        public List<(Pen pen, Point start, Point end)> Lines { get; } = [];
        public List<(Brush? brush, Pen? pen, Rect rectangle)> Rectangles { get; } = [];

        public override void DrawLine(Pen pen, Point point0, Point point1) => Lines.Add((pen, point0, point1));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Rectangles.Add((brush, pen, rectangle));
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) => Texts.Add((formattedText, origin));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
