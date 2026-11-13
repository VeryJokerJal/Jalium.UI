using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsInspectorFontTemplateTests
{
    public DevToolsInspectorFontTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Theory]
    [InlineData(200)] [InlineData(480)]
    public void FamilyEditingPreviewAndCommandUseTheLiveModel(double width)
    {
        var target = new TextBlock { FontFamily = new FontFamily("Menlo") };
        using var model = new DevToolsDependencyPropertyModel(target,TextBlock.FontFamilyProperty);
        int calls = 0;
        var row = new DevToolsPropertyRow { Model = model, FontDialogCommand = new RelayCommand(value => { Assert.Same(model,value); calls++; }) }; Layout(row,width);
        var editor = Editor(row); var text = Assert.IsType<TextBox>(editor.FindName("PART_TextBox"));
        var action = Assert.IsType<DevToolsActionButton>(editor.FindName("PART_ActionButton"));
        Assert.True(row.IsFontFamily); Assert.True(row.HasFontDialog); Assert.Equal("Menlo",text.FontFamily.Source); Assert.True(action.IsEnabled);
        action.PerformClick(); Assert.Equal(1,calls);
        target.FontFamily = new FontFamily("Cascadia Code"); Assert.Equal("Cascadia Code",row.PreviewFontFamily!.Source); Assert.Equal("Cascadia Code",text.FontFamily.Source);
        editor.BeginEdit(); editor.EditText = "Monaco"; Assert.True(editor.CommitEdit()); Assert.Equal("Monaco",target.FontFamily.Source); Assert.Equal("Monaco",text.FontFamily.Source);
        Layout(row,width); Assert.InRange(row.DesiredSize.Width,0,width + .5); Assert.True(action.TranslatePoint(new Point(action.ActualWidth,0),row).X <= row.ActualWidth + .5);
        row.FontDialogCommand = new RelayCommand(() => calls++,() => false); action.PerformClick(); Assert.Equal(1,calls);
        row.FontDialogCommand = null; Assert.False(row.HasFontDialog); Assert.Equal(Visibility.Collapsed,action.Visibility);
    }

    [Fact]
    public void WeightPreviewAndFamilyTemplateRetirementFollowPropertyChanges()
    {
        var target = new TextBlock { FontWeight = FontWeights.Bold };
        using var family = new DevToolsDependencyPropertyModel(target,TextBlock.FontFamilyProperty);
        using var weight = new DevToolsDependencyPropertyModel(target,TextBlock.FontWeightProperty);
        int calls = 0; var row = new DevToolsPropertyRow { Model = family,FontDialogCommand = new RelayCommand(() => calls++) }; Layout(row);
        var retired = Assert.IsType<DevToolsActionButton>(Editor(row).FindName("PART_ActionButton")); row.Model = weight; Layout(row);
        Assert.False(row.IsFontFamily); Assert.Null(Editor(row).FindName("PART_ActionButton")); retired.PerformClick(); Assert.Equal(0,calls);
        var text = Assert.IsType<TextBox>(Editor(row).FindName("PART_TextBox")); Assert.Equal(FontWeights.Bold,text.FontWeight);
        target.FontWeight = FontWeights.Light; Assert.Equal(FontWeights.Light,row.PreviewFontWeight); Assert.Equal(FontWeights.Light,text.FontWeight);
        row.Model = family; Layout(row); retired.PerformClick(); Assert.Equal(0,calls); Assert.IsType<DevToolsActionButton>(Editor(row).FindName("PART_ActionButton")).PerformClick(); Assert.Equal(1,calls);
        family.Dispose(); Assert.False(row.HasFontDialog);
    }

    [Fact]
    public void GroupsForwardAnOptionalFontCommandWithoutAddingDuplicateEditors()
    {
        var target = new Button(); using var model = new DevToolsDependencyPropertyModel(target,Control.FontFamilyProperty); int calls = 0;
        var group = new DevToolsPropertyGroup { ItemsSource = new[] { model },FontDialogCommand = new RelayCommand(value => { Assert.Same(model,value); calls++; }) }; Layout(group);
        var row = Assert.Single(Walk(group).OfType<DevToolsPropertyRow>()); Assert.Same(group.FontDialogCommand,row.FontDialogCommand);
        Assert.IsType<DevToolsActionButton>(Editor(row).FindName("PART_ActionButton")).PerformClick(); Assert.Equal(1,calls);
        group.FontDialogCommand = null; Assert.False(row.HasFontDialog);
    }

    [Fact]
    public void ReplacingTheWholeRowTemplateRetiresTheFontAction()
    {
        var target = new TextBlock(); using var model = new DevToolsDependencyPropertyModel(target,TextBlock.FontFamilyProperty); int calls = 0;
        var row = new DevToolsPropertyRow { Model = model,FontDialogCommand = new RelayCommand(() => calls++) }; Layout(row);
        var retired = Assert.IsType<DevToolsActionButton>(Editor(row).FindName("PART_ActionButton")); var original = row.Template;
        var blank = new ControlTemplate(typeof(DevToolsPropertyRow)); blank.SetVisualTree(() => new Border()); row.Template = blank; row.ApplyTemplate();
        retired.PerformClick(); Assert.Equal(0,calls); row.Template = original; Layout(row); retired.PerformClick(); Assert.Equal(0,calls);
        Assert.IsType<DevToolsActionButton>(Editor(row).FindName("PART_ActionButton")).PerformClick(); Assert.Equal(1,calls);
    }

    private static PropertyGridEditor Editor(DevToolsPropertyRow row) => Assert.IsType<PropertyGridEditor>(row.FindName("PART_Editor"));
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static void Layout(UIElement element,double width = 480) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width,1800)); element.Arrange(new Rect(0,0,width,element.DesiredSize.Height)); element.UpdateLayout(); } }
}
