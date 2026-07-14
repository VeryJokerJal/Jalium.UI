using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class AccessTextParityTests
{
    [Fact]
    public void AddedDependencyPropertiesShareTextBlockIdentifiersAndDefaults()
    {
        Assert.Same(TextBlock.BackgroundProperty, AccessText.BackgroundProperty);
        Assert.Same(TextBlock.BaselineOffsetProperty, AccessText.BaselineOffsetProperty);
        Assert.Same(TextBlock.FontStretchProperty, AccessText.FontStretchProperty);
        Assert.Same(TextBlock.LineHeightProperty, AccessText.LineHeightProperty);
        Assert.Same(TextBlock.LineStackingStrategyProperty, AccessText.LineStackingStrategyProperty);
        Assert.Same(TextBlock.TextAlignmentProperty, AccessText.TextAlignmentProperty);
        Assert.Same(TextBlock.TextDecorationsProperty, AccessText.TextDecorationsProperty);
        Assert.Same(TextBlock.TextEffectsProperty, AccessText.TextEffectsProperty);

        var accessText = new AccessText();
        Assert.Null(accessText.Background);
        Assert.True(double.IsNaN(accessText.BaselineOffset));
        Assert.Equal(FontStretches.Normal, accessText.FontStretch);
        Assert.True(double.IsNaN(accessText.LineHeight));
        Assert.Equal(LineStackingStrategy.MaxHeight, accessText.LineStackingStrategy);
        Assert.Equal(TextAlignment.Left, accessText.TextAlignment);
        Assert.Empty(accessText.TextDecorations);
        Assert.Empty(accessText.TextEffects);
    }

    [Fact]
    public void MarkupTextAndRepresentableInlinesAppendToTextAndReparseAccessKey()
    {
        var accessText = new AccessText();
        var addChild = Assert.IsAssignableFrom<IAddChild>(accessText);

        addChild.AddText("_File");
        addChild.AddChild(new Span(new Run(" __Now")));
        addChild.AddChild(new LineBreak());
        addChild.AddChild("_Open");

        Assert.Equal("_File __Now\n_Open", accessText.Text);
        Assert.Equal("File _Now\nOpen", accessText.DisplayText);
        Assert.Equal('F', accessText.AccessKey);
        Assert.Equal(accessText.DisplayText, Assert.IsType<TextBlock>(accessText.GetVisualChild(0)).Text);
    }

    [Fact]
    public void UnsupportedInlineContentIsRejectedWithoutLosingExistingText()
    {
        var accessText = new AccessText { Text = "_Keep" };
        var addChild = (IAddChild)accessText;

        Assert.Throws<ArgumentException>(() => addChild.AddChild(new InlineUIContainer()));
        Assert.Equal("_Keep", accessText.Text);
        Assert.Equal('K', accessText.AccessKey);
    }

    [Fact]
    public void FormattingPropertiesForwardToRenderedTextBlockAndMnemonicIsUnderlined()
    {
        var background = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        var foreground = new SolidColorBrush(Color.FromRgb(40, 50, 60));
        var decorations = new TextDecorationCollection(TextDecorations.Strikethrough);
        var effects = new TextEffectCollection { new() { PositionStart = 0, PositionCount = 1 } };
        var accessText = new AccessText
        {
            Text = "_Save",
            Background = background,
            Foreground = foreground,
            FontStretch = FontStretches.Expanded,
            BaselineOffset = 5,
            LineHeight = 28,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Right,
            TextDecorations = decorations,
            TextEffects = effects,
        };

        var child = Assert.IsType<TextBlock>(accessText.GetVisualChild(0));
        Assert.Same(background, child.Background);
        Assert.Same(foreground, child.Foreground);
        Assert.Equal(FontStretches.Expanded, child.FontStretch);
        Assert.Equal(5, child.BaselineOffset);
        Assert.Equal(28, child.LineHeight);
        Assert.Equal(LineStackingStrategy.BlockLineHeight, child.LineStackingStrategy);
        Assert.Equal(TextAlignment.Right, child.TextAlignment);
        Assert.Same(decorations, child.TextDecorations);
        Assert.Same(effects, child.TextEffects);

        accessText.Measure(new Size(160, 50));
        accessText.Arrange(new Rect(0, 0, 160, 50));
        var context = new RecordingDrawingContext();
        accessText.Render(context);

        Assert.Contains(context.Texts, text => text.Text == "Save");
        Assert.NotEmpty(context.Lines);
        Assert.Contains(context.Rectangles, item => ReferenceEquals(item.brush, background));
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        public List<FormattedText> Texts { get; } = [];
        public List<(Pen pen, Point start, Point end)> Lines { get; } = [];
        public List<(Brush? brush, Pen? pen, Rect rectangle)> Rectangles { get; } = [];

        public override void DrawLine(Pen pen, Point point0, Point point1) => Lines.Add((pen, point0, point1));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Rectangles.Add((brush, pen, rectangle));
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) => Texts.Add(formattedText);
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
