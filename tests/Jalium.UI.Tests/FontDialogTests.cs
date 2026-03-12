using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class FontDialogTests
{
    [Fact]
    public void FontSize_ShouldClampToConfiguredBounds()
    {
        var dialog = new FontDialog
        {
            MinSize = 8,
            MaxSize = 24
        };

        dialog.FontSize = 6;
        Assert.Equal(8, dialog.FontSize);

        dialog.FontSize = 30;
        Assert.Equal(24, dialog.FontSize);
    }

    [Fact]
    public void MinSize_ShouldRaiseMaxSizeAndReclampCurrentSelection()
    {
        var dialog = new FontDialog
        {
            FontSize = 12,
            MaxSize = 18
        };

        dialog.MinSize = 20;

        Assert.Equal(20, dialog.MinSize);
        Assert.Equal(20, dialog.MaxSize);
        Assert.Equal(20, dialog.FontSize);
    }

    [Fact]
    public void UpdateDialogTextDecorations_ShouldPreserveOtherDecorations()
    {
        var existing = new TextDecorationCollection
        {
            new() { Location = TextDecorationLocation.OverLine },
            new() { Location = TextDecorationLocation.Underline }
        };

        var updated = FontDialog.UpdateDialogTextDecorations(existing, underline: false, strikeout: true);

        Assert.NotNull(updated);
        Assert.True(updated.HasDecoration(TextDecorationLocation.OverLine));
        Assert.False(updated.HasDecoration(TextDecorationLocation.Underline));
        Assert.True(updated.HasDecoration(TextDecorationLocation.Strikethrough));
    }

    [Fact]
    public void GetDialogEffects_ShouldReflectUnderlineAndStrikeout()
    {
        var decorations = new TextDecorationCollection
        {
            new() { Location = TextDecorationLocation.Underline },
            new() { Location = TextDecorationLocation.Strikethrough }
        };

        var effects = FontDialog.GetDialogEffects(decorations);

        Assert.True(effects.Underline);
        Assert.True(effects.Strikeout);
    }

    [Fact]
    public void GetFontFamilies_ShouldReturnDistinctNonEmptyNames()
    {
        var families = FontDialog.GetFontFamilies().ToArray();

        Assert.NotEmpty(families);
        Assert.All(families, family => Assert.False(string.IsNullOrWhiteSpace(family.Source)));
        Assert.Equal(
            families.Length,
            families.Select(family => family.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetStandardFontSizes_ShouldReturnExpectedSequence()
    {
        Assert.Equal(
            new double[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 },
            FontDialog.GetStandardFontSizes().ToArray());
    }
}
