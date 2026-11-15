using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Tests for the WPF-aligned <see cref="TextOptions"/> attached-property facade and the
/// underlying <see cref="DependencyProperty"/> machinery it depends on
/// (FrameworkPropertyMetadataOptions.Inherits propagation + ValidateValueCallback gating).
/// </summary>
public class TextOptionsTests
{
    [Fact]
    public void ApplyTextOptions_PreservesInheritedParagraphDirection()
    {
        var parent = new Jalium.UI.Controls.StackPanel { FlowDirection = FlowDirection.RightToLeft };
        var child = new Jalium.UI.Controls.TextBlock(); parent.Children.Add(child);
        var text = new FormattedText("مرحبا CSS", "DejaVu Sans", 20).ApplyTextOptionsFrom(child);
        Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
        parent.FlowDirection = FlowDirection.LeftToRight;
        Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
        Assert.Equal(FlowDirection.LeftToRight, new FormattedText("مرحبا CSS", "DejaVu Sans", 20).ApplyTextOptionsFrom(child).FlowDirection);
    }

    [Fact]
    public void RecordedTextPool_KeepsOppositeParagraphDirectionsIndependent()
    {
        var left = new FormattedText("مرحبا CSS", "DejaVu Sans", 20) { FlowDirection = FlowDirection.LeftToRight };
        var right = new FormattedText("مرحبا CSS", "DejaVu Sans", 20) { FlowDirection = FlowDirection.RightToLeft };
        var a = Jalium.UI.Media.Rendering.DrawingObjectPool.CanonicalizeFormattedText(left);
        var b = Jalium.UI.Media.Rendering.DrawingObjectPool.CanonicalizeFormattedText(right);
        Assert.NotSame(a,b);
        Assert.Equal(FlowDirection.LeftToRight,a.FlowDirection);
        Assert.Equal(FlowDirection.RightToLeft,b.FlowDirection);
        right.FlowDirection = FlowDirection.LeftToRight;
        Assert.Equal(FlowDirection.RightToLeft,b.FlowDirection);
    }

    private sealed class Element : DependencyObject { }

    [Fact]
    public void TextFormattingMode_DefaultIsIdeal()
    {
        var e = new Element();
        Assert.Equal(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(e));
    }

    [Fact]
    public void TextRenderingMode_DefaultIsAuto()
    {
        var e = new Element();
        Assert.Equal(TextRenderingMode.Auto, TextOptions.GetTextRenderingMode(e));
    }

    [Fact]
    public void TextHintingMode_DefaultIsAuto()
    {
        var e = new Element();
        Assert.Equal(TextHintingMode.Auto, TextOptions.GetTextHintingMode(e));
    }

    [Fact]
    public void SetTextFormattingMode_RoundTrips()
    {
        var e = new Element();
        TextOptions.SetTextFormattingMode(e, TextFormattingMode.Display);
        Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(e));
    }

    [Fact]
    public void SetTextRenderingMode_RoundTrips()
    {
        var e = new Element();
        TextOptions.SetTextRenderingMode(e, TextRenderingMode.ClearType);
        Assert.Equal(TextRenderingMode.ClearType, TextOptions.GetTextRenderingMode(e));
    }

    [Fact]
    public void SetTextHintingMode_RoundTrips()
    {
        var e = new Element();
        TextOptions.SetTextHintingMode(e, TextHintingMode.Fixed);
        Assert.Equal(TextHintingMode.Fixed, TextOptions.GetTextHintingMode(e));
    }

    [Fact]
    public void GetTextFormattingMode_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextOptions.GetTextFormattingMode(null!));
    }

    [Fact]
    public void SetTextFormattingMode_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextOptions.SetTextFormattingMode(null!, TextFormattingMode.Ideal));
    }

    [Fact]
    public void GetTextRenderingMode_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextOptions.GetTextRenderingMode(null!));
    }

    [Fact]
    public void SetTextRenderingMode_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextOptions.SetTextRenderingMode(null!, TextRenderingMode.Auto));
    }

    [Fact]
    public void GetTextHintingMode_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextOptions.GetTextHintingMode(null!));
    }

    [Fact]
    public void SetTextHintingMode_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextOptions.SetTextHintingMode(null!, TextHintingMode.Auto));
    }

    [Fact]
    public void SetTextFormattingMode_OutOfRangeEnum_RejectedByValidator()
    {
        var e = new Element();
        // Cast bypasses C# enum range check; ValidateValueCallback must catch it instead.
        Assert.Throws<ArgumentException>(() =>
            TextOptions.SetTextFormattingMode(e, (TextFormattingMode)42));
    }

    [Fact]
    public void SetTextRenderingMode_OutOfRangeEnum_RejectedByValidator()
    {
        var e = new Element();
        Assert.Throws<ArgumentException>(() =>
            TextOptions.SetTextRenderingMode(e, (TextRenderingMode)99));
    }

    [Fact]
    public void SetTextHintingMode_OutOfRangeEnum_RejectedByValidator()
    {
        var e = new Element();
        Assert.Throws<ArgumentException>(() =>
            TextOptions.SetTextHintingMode(e, (TextHintingMode)123));
    }

    [Fact]
    public void TextHintingModeProperty_IsAliasedFromInternal()
    {
        // AddOwner() shares the backing DependencyProperty instance, so the value
        // written via the TextOptions facade must be visible through the internal
        // alias and vice versa — the WPF TextOptionsInternal.AddOwner pattern.
        var e = new Element();
        TextOptions.SetTextHintingMode(e, TextHintingMode.Animated);
        var viaInternalAlias = (TextHintingMode)(e.GetValue(TextOptions.TextHintingModeProperty) ?? TextHintingMode.Auto);
        Assert.Equal(TextHintingMode.Animated, viaInternalAlias);
    }

    [Fact]
    public void FrameworkPropertyMetadata_InheritsFlag_PropagatesToMetadata()
    {
        // FrameworkPropertyMetadata previously dropped the Inherits flag because
        // SetFlags didn't translate it into PropertyMetadata.Inherits — without
        // this regression test, the bug returns silently and inheritance breaks
        // for every TextOptions attached property.
        var metadata = new FrameworkPropertyMetadata(
            defaultValue: 0,
            flags: FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender);
        Assert.True(metadata.Inherits);
        Assert.True(metadata.AffectsRender);
        Assert.False(metadata.AffectsMeasure);
    }

    [Fact]
    public void TextFormattingModeProperty_DefaultMetadata_HasInheritsAndAffectsMeasureAndRender()
    {
        var m = (FrameworkPropertyMetadata)TextOptions.TextFormattingModeProperty.DefaultMetadata;
        Assert.True(m.Inherits, "TextFormattingMode must inherit down the visual tree.");
        Assert.True(m.AffectsMeasure, "Switching Ideal<->Display changes glyph metrics, so measure must re-run.");
        Assert.True(m.AffectsRender, "Switching Ideal<->Display changes rasterization, so render must re-run.");
    }

    [Fact]
    public void TextRenderingModeProperty_DefaultMetadata_HasInheritsAndAffectsRender()
    {
        var m = (FrameworkPropertyMetadata)TextOptions.TextRenderingModeProperty.DefaultMetadata;
        Assert.True(m.Inherits);
        Assert.True(m.AffectsRender);
        Assert.False(m.AffectsMeasure);
    }

    [Fact]
    public void TextHintingModeProperty_DefaultMetadata_HasInheritsAndAffectsRender()
    {
        var m = (FrameworkPropertyMetadata)TextOptions.TextHintingModeProperty.DefaultMetadata;
        Assert.True(m.Inherits);
        Assert.True(m.AffectsRender);
    }

    [Fact]
    public void ValidateValueCallback_RegisterAttached_DefaultValueMustPass()
    {
        // The registration-time guard rejects a default that fails its own validator;
        // proves the validator hook actually runs at Register-time, not lazily on first set.
        Assert.Throws<ArgumentException>(() =>
            DependencyProperty.RegisterAttached(
                "BadDefault",
                typeof(int),
                typeof(TextOptionsTests),
                new PropertyMetadata(-1),
                v => v is int i && i >= 0));
    }

    [Fact]
    public void ApplyTextOptionsFrom_CopiesAllThreeModes_FromElementToFormattedText()
    {
        // The managed-side bridge that Controls call (formattedText.ApplyTextOptionsFrom(this))
        // must read all three attached-property values off the element and stamp them onto
        // the FormattedText so RenderTargetDrawingContext can forward them to the native
        // text format on DrawText. Without this regression test the extension method could
        // silently start copying only one mode and the per-element override would partially
        // leak the process-wide default.
        var e = new Element();
        TextOptions.SetTextRenderingMode(e, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(e, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(e, TextHintingMode.Animated);

        var ft = new FormattedText("hi", "Segoe UI", 12);
        ft.ApplyTextOptionsFrom(e);

        Assert.Equal((int)TextRenderingMode.ClearType, ft.TextRenderingMode);
        Assert.Equal((int)TextFormattingMode.Display, ft.TextFormattingMode);
        Assert.Equal((int)TextHintingMode.Animated,   ft.TextHintingMode);
    }

    [Fact]
    public void ApplyTextOptionsFrom_DefaultElement_LeavesAllModesZero()
    {
        // Auto/Ideal/Auto map to 0/0/0, which is the native side's signal to fall
        // back to the process-wide value. Controls that hand un-set elements through
        // ApplyTextOptionsFrom must not accidentally pin them to an explicit mode.
        var e = new Element();
        var ft = new FormattedText("hi", "Segoe UI", 12).ApplyTextOptionsFrom(e);
        Assert.Equal(0, ft.TextRenderingMode);
        Assert.Equal(0, ft.TextFormattingMode);
        Assert.Equal(0, ft.TextHintingMode);
    }

    [Fact]
    public void ApplyTextOptionsFrom_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((FormattedText)null!).ApplyTextOptionsFrom(new Element()));
        Assert.Throws<ArgumentNullException>(() =>
            new FormattedText("x", "Segoe UI", 12).ApplyTextOptionsFrom(null!));
    }
}
