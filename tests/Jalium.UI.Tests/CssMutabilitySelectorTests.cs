using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssMutabilitySelectorTests
{
    static CssMutabilitySelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":read-only", true)]
    [InlineData(":read-write", true)]
    [InlineData(":READ-WRITE", true)]
    [InlineData(":read-only()", false)]
    [InlineData(":read-write()", false)]
    public void SelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void ReadOnly_IsTheComplementOfEditableHostControls()
    {
        var readOnly = Assert.Single(CssSelectorParser.ParseGroup(":read-only", out _)!);
        var readWrite = Assert.Single(CssSelectorParser.ParseGroup(":read-write", out _)!);
        (FrameworkElement Element, bool Editable)[] controls =
        [
            (new TextBox(), true), (new RichTextBox(), true),
            (new AutoCompleteBox(), true), (new NumberBox(), true),
            (new PasswordBox(), true), (new EditControl(), true),
            (new HexEditor(), true), (new Terminal(), true),
            (new DataGrid(), true), (new DataGridCell(), true),
            (new ComboBox(), false), (new JsonTreeViewer(), false),
            (new Border(), false), (new Slider(), false),
            (new DiffViewer(), false), (new PropertyGrid(), false),
        ];

        foreach (var (element, editable) in controls)
        {
            Assert.Equal(editable, CssMatcher.Matches(element, readWrite, evaluateStates: true));
            Assert.Equal(!editable, CssMatcher.Matches(element, readOnly, evaluateStates: true));
        }
    }

    [Fact]
    public void NativeReadOnlyEditableAndEnabledChanges_UpdateStyles()
    {
        var text = new TextBox();
        var password = new PasswordBox();
        var combo = new ComboBox();
        var json = new JsonTreeViewer();
        var grid = new DataGrid();
        var border = new Border();
        var root = new StackPanel();
        FrameworkElement[] controls = [text, password, combo, json, grid, border];
        foreach (var control in controls) root.Children.Add(control);
        Css.SetStyleSheet(root, ":read-write { opacity:.4 } :read-only { opacity:.8 }");
        Flush(root);
        Assert.Equal(.4, text.Opacity);
        Assert.Equal(.4, password.Opacity);
        Assert.Equal(.4, grid.Opacity);
        Assert.Equal(.8, combo.Opacity);
        Assert.Equal(.8, json.Opacity);
        Assert.Equal(.8, border.Opacity);

        text.IsReadOnly = true;
        password.IsReadOnly = true;
        grid.IsReadOnly = true;
        combo.IsEditable = true;
        json.IsEditable = true;
        Flush(root);
        Assert.Equal(.8, text.Opacity);
        Assert.Equal(.8, password.Opacity);
        Assert.Equal(.8, grid.Opacity);
        Assert.Equal(.4, combo.Opacity);
        Assert.Equal(.4, json.Opacity);

        combo.IsReadOnly = true;
        root.IsEnabled = false;
        Flush(root);
        foreach (var control in controls) Assert.Equal(.8, control.Opacity);

        root.IsEnabled = true;
        text.IsReadOnly = false;
        password.IsReadOnly = false;
        grid.IsReadOnly = false;
        combo.IsReadOnly = false;
        Flush(root);
        Assert.Equal(.4, text.Opacity);
        Assert.Equal(.4, password.Opacity);
        Assert.Equal(.4, grid.Opacity);
        Assert.Equal(.4, combo.Opacity);
        Assert.Equal(.4, json.Opacity);
        Assert.Equal(.8, border.Opacity);
    }

    [Fact]
    public void AncestorReadWriteSelector_ReevaluatesItsDescendantRule()
    {
        var text = new TextBox();
        var border = new Border();
        var root = new StackPanel();
        root.Children.Add(text);
        root.Children.Add(border);
        Css.SetStyleSheet(root, "TextBox:read-write + Border { opacity:.4 }");
        Flush(root);
        Assert.Equal(.4, border.Opacity);

        text.IsReadOnly = true;
        Flush(root);
        Assert.Equal(1, border.Opacity);

        text.IsReadOnly = false;
        Flush(root);
        Assert.Equal(.4, border.Opacity);
    }

    [Fact]
    public void EditorReadOnlyProperties_UpdateTheirOwnSelectors()
    {
        FrameworkElement[] editors =
            [new RichTextBox(), new AutoCompleteBox(), new NumberBox(),
             new EditControl(), new HexEditor(), new Terminal()];
        var root = new StackPanel();
        foreach (var editor in editors) root.Children.Add(editor);
        Css.SetStyleSheet(root, ":read-write { opacity:.4 } :read-only { opacity:.8 }");
        Flush(root);
        foreach (var editor in editors) Assert.Equal(.4, editor.Opacity);

        foreach (var editor in editors)
        {
            var readOnly = CssDependencyPropertyLookup.Find(editor.GetType(), "IsReadOnly");
            Assert.NotNull(readOnly);
            editor.SetValue(readOnly!, true);
        }
        Flush(root);
        foreach (var editor in editors) Assert.Equal(.8, editor.Opacity);
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}
