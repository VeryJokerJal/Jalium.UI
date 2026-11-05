using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSHexEditorRenderingTests : IDisposable
{
    public MacOSHexEditorRenderingTests() => AccessTextTemplateTestHelper.Register();
    public void Dispose() => AccessTextTemplateTestHelper.Register();

    [Fact]
    public void DefaultTypographyMeasuresAsMonospaceAndExplicitTypographySurvives()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        ThemeLoader.Initialize(); _ = new Application();
        Assert.Equal("Menlo", ThemeManager.CurrentMonospaceFontFamily);
        var narrow = new FormattedText("iiii", ThemeManager.CurrentMonospaceFontFamily, 13);
        var wide = new FormattedText("MMMM", ThemeManager.CurrentMonospaceFontFamily, 13);
        Assert.True(TextMeasurement.MeasureText(narrow)); Assert.True(TextMeasurement.MeasureText(wide));
        Assert.Equal(narrow.Width, wide.Width, 2);
        ThemeManager.ApplyTypography("Georgia", "Calibri", "Consolas");
        Assert.Equal("Consolas", ThemeManager.CurrentMonospaceFontFamily);
    }

    [Fact]
    public void MutablePaletteInvalidatesOnlyTheCurrentPrimitiveView()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0, 94, 166));
        var editor = new HexEditor { Data = new byte[2048], HexForeground = brush };
        editor.Resources.MergedDictionaries.Add(MenuFlyoutTemplateTestHelper.Themes.Value);
        Layout(editor);
        var old = Assert.IsType<HexEditorTextView>(editor.FindName("PART_TextView"));
        var dirty = typeof(Visual).GetField("_isRenderDirty", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dirty.SetValue(old, false);
        brush.Color = Color.FromRgb(143, 78, 50);
        Assert.True((bool)dirty.GetValue(old)!);
        var blank = new ControlTemplate(typeof(HexEditor)); blank.SetVisualTree(() => new Border());
        editor.Template = blank; Layout(editor);
        dirty.SetValue(old, false);
        brush.Color = Color.FromRgb(32, 32, 32);
        Assert.False((bool)dirty.GetValue(old)!);
        editor.ClearValue(Control.TemplateProperty); Layout(editor);
        var current = Assert.IsType<HexEditorTextView>(editor.FindName("PART_TextView"));
        dirty.SetValue(current, false);
        brush.Color = Color.FromRgb(0, 94, 166);
        Assert.True((bool)dirty.GetValue(current)!);
    }

    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(400, 200));
        element.Arrange(new Rect(0, 0, 400, 200)); element.UpdateLayout();
    }
}
