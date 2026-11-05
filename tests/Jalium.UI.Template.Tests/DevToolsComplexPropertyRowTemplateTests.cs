using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsComplexPropertyRowTemplateTests
{
    public DevToolsComplexPropertyRowTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    [Fact]
    public void SerializedSpacingKeepsValidationCancelCommitAndLiveText()
    {
        var target = new Border { Margin = new Thickness(1.25) }; using var model = new DevToolsDependencyPropertyModel(target, FrameworkElement.MarginProperty);
        var row = new DevToolsPropertyRow { Model = model }; Layout(row); var editor = Editor(row);
        Assert.Equal(PropertyGridEditorKind.Text, editor.EditorKind); Assert.Equal("1.25,1.25,1.25,1.25", Text(editor).Text);
        Assert.True(editor.BeginEdit()); editor.EditText = "invalid"; Assert.False(editor.CommitEdit()); Assert.True(editor.HasError); Assert.Equal(new Thickness(1.25), target.Margin);
        editor.CancelEdit(); Assert.False(editor.HasError); Assert.True(editor.BeginEdit()); editor.EditText = "2.5,4.75"; Assert.True(editor.CommitEdit()); Assert.Equal(new Thickness(2.5,4.75), target.Margin);
        target.Margin = new Thickness(-3.25); Assert.Equal("-3.25,-3.25,-3.25,-3.25", Text(editor).Text);
    }
    [Theory]
    [InlineData(220)] [InlineData(480)]
    public void ExpandableColorPickerUsesTheTypedEditorAndFitsTheAvailableWidth(double width)
    {
        var target = new Border { Background = new SolidColorBrush(Color.FromRgb(10, 20, 30)) };
        using var model = new DevToolsDependencyPropertyModel(target, Border.BackgroundProperty);
        var row = new DevToolsPropertyRow { Model = model }; Layout(row, width); var editor = Editor(row);
        Assert.Equal(PropertyGridEditorKind.Color, editor.EditorKind);
        var expander = Assert.IsType<Expander>(editor.FindName("PART_Expander")); var picker = Assert.IsType<ColorPicker>(editor.FindName("PART_ValueControl"));
        Assert.Equal(editor.ValueText, Assert.IsType<TextBlock>(editor.FindName("PART_ColorValue")).Text);
        Assert.False(expander.IsExpanded); editor.BeginEdit(); Assert.True(expander.IsExpanded); Layout(row, width);
        Assert.True(picker.IsHexInputVisible); Assert.True(picker.IsAlphaEnabled);
        Assert.InRange(row.DesiredSize.Width, 0, width + .5);
        Assert.True(picker.TranslatePoint(new Point(picker.ActualWidth, 0), row).X <= width + .5);
        var edited = Color.FromArgb(128, 40, 50, 60); picker.Color = edited;
        Assert.Equal(edited, Assert.IsType<SolidColorBrush>(target.Background).Color); Assert.Equal(edited, Assert.IsType<SolidColorBrush>(row.PreviewBrush).Color);
        var original = row.Template; var blank = new ControlTemplate(typeof(DevToolsPropertyRow)); blank.SetVisualTree(() => new Border()); row.Template = blank; row.ApplyTemplate();
        picker.Color = Color.White; Assert.Equal(edited, Assert.IsType<SolidColorBrush>(target.Background).Color);
        row.Template = original; Layout(row, width); Assert.Equal(edited, Editor(row).ColorValue);
    }
    [Fact]
    public void ChangingBrushRepresentationDuringCommitRetiresThePreviousInputAndPicker()
    {
        var target = new Border(); using var model = new DevToolsDependencyPropertyModel(target, Border.BackgroundProperty);
        var row = new DevToolsPropertyRow { Model = model }; Layout(row); var editor = Editor(row); var input = Text(editor);
        Assert.True(editor.BeginEdit()); editor.EditText = "#FF102030"; Assert.True(editor.CommitEdit()); Layout(row);
        Assert.Same(model.EditorProperty, row.Property); Assert.Equal(PropertyGridEditorKind.Color, Editor(row).EditorKind);
        input.Text = "#FFFFFFFF"; Assert.Equal(Color.FromRgb(16,32,48), Assert.IsType<SolidColorBrush>(target.Background).Color);
        var picker = Assert.IsType<ColorPicker>(Editor(row).FindName("PART_ValueControl"));
        target.Background = new LinearGradientBrush(); Layout(row);
        Assert.Equal(PropertyGridEditorKind.Text, Editor(row).EditorKind); Assert.False(Editor(row).CanEdit); Assert.Null(row.PreviewBrush);
        Assert.Equal("LinearGradientBrush", Text(Editor(row)).Text);
        picker.Color = Color.White; Assert.IsType<LinearGradientBrush>(target.Background);
        target.Background = null; Layout(row); Assert.True(Editor(row).CanEdit); Assert.Equal("(null)", Editor(row).ValueText);
    }
    [Fact]
    public void FontValuesAndRuntimeEnumsKeepTheirFrameworkConversionContracts()
    {
        var target = new Button { FontFamily = new FontFamily("Menlo"), Tag = HorizontalAlignment.Center };
        using var font = new DevToolsDependencyPropertyModel(target, Control.FontFamilyProperty);
        var row = new DevToolsPropertyRow { Model = font }; Layout(row); var editor = Editor(row);
        Assert.Equal("Menlo", Text(editor).Text); Assert.True(editor.BeginEdit()); editor.EditText = "Arial"; Assert.True(editor.CommitEdit()); Assert.Equal("Arial", target.FontFamily.Source);
        using var runtime = new DevToolsDependencyPropertyModel(target, FrameworkElement.TagProperty); row.Model = runtime; Layout(row);
        Assert.Equal(PropertyGridEditorKind.Enum, Editor(row).EditorKind);
        Editor(row).SelectedEnumOption = Editor(row).EnumOptions.Single(option => Equals(option.Value, HorizontalAlignment.Right)); Assert.Equal(HorizontalAlignment.Right, target.Tag);
        target.Tag = Visibility.Collapsed; Layout(row); Assert.Equal(Visibility.Collapsed, Editor(row).SelectedEnumOption!.Value);
    }
    private static PropertyGridEditor Editor(DevToolsPropertyRow row) => Assert.IsType<PropertyGridEditor>(row.FindName("PART_Editor"));
    private static TextBox Text(PropertyGridEditor editor) => Assert.IsType<TextBox>(editor.FindName("PART_TextBox"));
    private static void Layout(UIElement element, double width = 480) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 1200)); element.Arrange(new Rect(0,0,width,element.DesiredSize.Height)); element.UpdateLayout(); } }
}
