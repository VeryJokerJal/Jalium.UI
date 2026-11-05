using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsPropertyRowTemplateTests
{
    public DevToolsPropertyRowTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    [Fact]
    public void TextEditorSupportsPendingValidationCancelAndLiveSourceChanges()
    {
        var target = new Border { Opacity = .5 }; using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        var row = new DevToolsPropertyRow { Model = model }; Layout(row);
        var editor = Editor(row); Assert.Equal(PropertyGridEditorKind.Text, editor.EditorKind); Assert.True(editor.CanEdit);
        var textBox = Part<TextBox>(editor, "PART_TextBox");
        Assert.Same(Assert.IsType<Style>(row.TryFindResource("DevTools.TextInput")), textBox.Style);
        Assert.Same(row.TryFindResource("DevTools.Control"), textBox.Background);
        Assert.Same(row.TryFindResource("DevTools.TextPrimary"), textBox.Foreground);
        Assert.Equal("Opacity", row.GetAutomationPeer()!.GetName()); Assert.Equal(AutomationControlType.Group, row.GetAutomationPeer()!.GetAutomationControlType());
        Assert.Equal("Local", Part<TextBlock>(row, "PART_Source").Text);
        Assert.True(editor.BeginEdit()); editor.EditText = "invalid";
        Assert.False(editor.CommitEdit()); Assert.True(editor.HasError); Assert.Equal(.5, target.Opacity);
        target.Opacity = .75; Assert.Equal("invalid", editor.EditText); editor.CancelEdit(); Assert.False(editor.HasError); Assert.Equal(Convert.ToString(.75), editor.EditText);
        Assert.True(editor.BeginEdit()); editor.EditText = Convert.ToString(.25)!; Assert.True(editor.CommitEdit()); Assert.Equal(.25, target.Opacity);
        target.Opacity = .9; Assert.Equal(Convert.ToString(.9), editor.ValueText);
        Assert.Equal(Convert.ToString(.9), editor.EditText);
        Assert.Equal(Convert.ToString(.9), Part<TextBox>(editor, "PART_TextBox").Text);
    }
    [Fact]
    public void BooleanAndEnumEditorsKeepStandardTypedContracts()
    {
        var target = new Border(); using var boolean = new DevToolsDependencyPropertyModel(target, UIElement.IsEnabledProperty);
        var row = new DevToolsPropertyRow { Model = boolean }; Layout(row);
        var editor = Editor(row); Assert.Equal(PropertyGridEditorKind.Boolean, editor.EditorKind); Assert.IsType<CheckBox>(editor.FindName("PART_ValueControl"));
        editor.BooleanValue = false; Assert.False(target.IsEnabled); target.IsEnabled = true; Assert.True(editor.BooleanValue);
        using var alignment = new DevToolsDependencyPropertyModel(target, FrameworkElement.HorizontalAlignmentProperty);
        row.Model = alignment; Layout(row); editor = Editor(row);
        Assert.Equal(PropertyGridEditorKind.Enum, editor.EditorKind); Assert.IsType<ComboBox>(editor.FindName("PART_ValueControl"));
        editor.SelectedEnumOption = editor.EnumOptions.Single(option => Equals(option.Value, HorizontalAlignment.Right)); Assert.Equal(HorizontalAlignment.Right, target.HorizontalAlignment);
        target.HorizontalAlignment = HorizontalAlignment.Center; Assert.Equal(HorizontalAlignment.Center, editor.SelectedEnumOption!.Value);
    }
    [Fact]
    public void OriginChangesAndReadonlyInputsRemainObservable()
    {
        var target = new Border(); using var origin = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        var row = new DevToolsPropertyRow { Model = origin }; Layout(row);
        Assert.Equal("DefaultSource", VisualStateManager.GetCurrentStateName(row, "SourceStates"));
        target.Opacity = 1; Assert.Equal("LocalSource", VisualStateManager.GetCurrentStateName(row, "SourceStates"));
        row.IsAlternate = true; Assert.Equal("Alternate", VisualStateManager.GetCurrentStateName(row, "RowStates"));
        using var readOnly = new DevToolsDependencyPropertyModel(target, FrameworkElement.ActualWidthProperty);
        row.Model = readOnly; Layout(row); Assert.False(Editor(row).CanEdit); Assert.False(Editor(row).BeginEdit());
        readOnly.Dispose(); Assert.Equal("UnavailableSource", VisualStateManager.GetCurrentStateName(row, "SourceStates"));
    }
    [Fact]
    public void ReplacingTheRowTemplateRetiresUnownedEditorsAndRestoresPendingText()
    {
        var target = new Border { Opacity = .5 }; using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        var row = new DevToolsPropertyRow { Model = model }; Layout(row); var original = row.Template; var retired = Editor(row);
        Assert.True(retired.BeginEdit()); retired.EditText = Convert.ToString(.25)!;
        var empty = new ControlTemplate(typeof(DevToolsPropertyRow)); empty.SetVisualTree(() => new Border()); row.Template = empty; row.ApplyTemplate();
        Assert.False(retired.BeginEdit()); Assert.False(retired.CommitEdit()); Assert.Equal(.5, target.Opacity);
        row.Template = original; Layout(row); var current = Editor(row); Assert.NotSame(retired, current);
        Assert.Equal(Convert.ToString(.25), current.EditText); Assert.True(current.CommitEdit()); Assert.Equal(.25, target.Opacity);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void RetargetingWithoutTemplatePartsDoesNotMoveAnOldDraftToTheNewProperty(bool restoreTemplate)
    {
        var first = new Border { Opacity = .5 }; var second = new Border { Opacity = .75 };
        using var oldModel = new DevToolsDependencyPropertyModel(first, UIElement.OpacityProperty);
        using var newModel = new DevToolsDependencyPropertyModel(second, UIElement.OpacityProperty);
        var row = new DevToolsPropertyRow { Model = oldModel }; Layout(row); var original = row.Template;
        var editor = Editor(row); Assert.True(editor.BeginEdit()); editor.EditText = Convert.ToString(.25)!;
        var empty = new ControlTemplate(typeof(DevToolsPropertyRow)); empty.SetVisualTree(() => new Border()); row.Template = empty; row.ApplyTemplate();
        row.Model = newModel;
        if (restoreTemplate)
        {
            row.Template = original; Layout(row); editor = Editor(row);
            Assert.False(editor.IsEditing); Assert.Equal(Convert.ToString(.75), editor.EditText);
        }
        Assert.Equal(.5, first.Opacity); Assert.Equal(.75, second.Opacity);
    }
    [Theory]
    [InlineData(220)] [InlineData(480)]
    public void ResponsiveRowsKeepLongNamesAndEditorWithinTheirBounds(double width)
    {
        var target = new Border { HorizontalAlignment = HorizontalAlignment.Stretch }; using var model = new DevToolsDependencyPropertyModel(target, FrameworkElement.HorizontalAlignmentProperty);
        var row = new DevToolsPropertyRow { Model = model }; Layout(row, width);
        Assert.InRange(row.DesiredSize.Width, 0, width + .5);
        var editor = Editor(row); Assert.True(editor.TranslatePoint(new Point(editor.ActualWidth, 0), row).X <= width + .5);
        Assert.Equal(width < row.NameColumnWidth + 160 ? "Compact" : "Wide", VisualStateManager.GetCurrentStateName(row, "LayoutStates"));
        row.Template = new ControlTemplate(typeof(DevToolsPropertyRow)); row.Template.SetVisualTree(() => new Border()); row.ApplyTemplate();
        Assert.Same(model, row.Model); Assert.Same(model.EditorProperty, row.Property);
    }
    private static PropertyGridEditor Editor(DevToolsPropertyRow row) => Part<PropertyGridEditor>(row, "PART_Editor");
    private static T Part<T>(Control control, string name) where T : class => Assert.IsAssignableFrom<T>(control.FindName(name));
    private static void Layout(UIElement element, double width = 480) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 600)); element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height)); element.UpdateLayout(); } }
}
