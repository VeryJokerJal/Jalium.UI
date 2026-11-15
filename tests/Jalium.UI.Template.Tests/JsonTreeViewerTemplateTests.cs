using System.ComponentModel;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class JsonTreeViewerTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var defaults = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        // This fixture runs without Application. Register the container's default
        // theme layer so an appearance-only consumer style retains its template.
        ThemeManager.RegisterControlThemes(new ResourceDictionary { [typeof(TreeViewItem)] = defaults[typeof(TreeViewItem)] });
        return defaults;
    });

    [Fact]
    public void ParsingAndFilteringBeforeATemplateDoNotCreateAVisualTree()
    {
        var viewer = new JsonTreeViewer { JsonText = "{\"number\":7,\"other\":false}", SearchText = "number" };
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(viewer));
        Assert.NotNull(viewer.RootNode);
        Assert.True(viewer.RootNode.Children[0].IsVisible);
        Assert.False(viewer.RootNode.Children[1].IsVisible);
        Assert.Equal("Nodes: 3 | Showing: 2", viewer.StatusText);
        var minimal = new ControlTemplate(typeof(JsonTreeViewer));
        minimal.SetVisualTree(() => new Border());
        viewer.Template = minimal;
        Layout(viewer);
        viewer.ExpandAll();
        viewer.CollapseAll();
        viewer.JsonText = "invalid";
        Assert.Null(viewer.RootNode);
        Assert.NotEmpty(viewer.ParseError);
        Assert.Null(viewer.FindName("PART_TreeView"));
    }

    [Fact]
    public void DefaultTemplateDisplaysDataAndObservableNodeChanges()
    {
        var viewer = Create("{\"name\":\"first\",\"items\":[1,2]}");
        Assert.Equal("Ready", VisualStateManager.GetCurrentStateName(viewer, "DataStates"));
        var name = viewer.RootNode!.Children[0];
        var row = Row(viewer, name);
        Assert.Equal("name", row.KeyText);
        Assert.Equal("\"first\"", row.ValueText);
        name.Value = "updated";
        Assert.Equal("\"updated\"", row.ValueText);
        Assert.Equal(row.ValueText, Assert.IsType<TextBlock>(row.FindName("PART_Value")).Text);
        var array = viewer.RootNode.Children[1];
        var arrayRow = Row(viewer, array);
        array.Children.Add(new JsonTreeNode { NodeType = JsonNodeType.Null });
        Assert.Equal(" 3 items ", arrayRow.CountText);
        viewer.ShowItemCount = false;
        viewer.ShowTypeIndicators = false;
        Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(row.FindName("PART_Indicator")).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(arrayRow.FindName("PART_Count")).Visibility);
        var customBrush = new SolidColorBrush(Colors.Red);
        viewer.StringBrush = customBrush;
        Assert.Same(customBrush, row.TypeBrush);
        Assert.Same(row, Row(viewer, name)); // Appearance changes keep pending editors and containers.
    }

    [Fact]
    public void SearchAndSelectionUpdateTheirTwoWayBindingsAndSurviveRebuilding()
    {
        var model = new Model();
        var viewer = Create("{\"name\":\"first\",\"nested\":{\"target\":5}}");
        viewer.SetBinding(JsonTreeViewer.SearchTextProperty, new Binding(nameof(Model.Query)) { Source = model, Mode = BindingMode.TwoWay });
        viewer.SetBinding(JsonTreeViewer.SelectedNodeProperty, new Binding(nameof(Model.Selected)) { Source = model, Mode = BindingMode.TwoWay });
        var selected = viewer.RootNode!.Children[0];
        int notifications = 0;
        viewer.SelectedNodeChanged += (_, _) => notifications++;
        var selectedContainer = Container(viewer, selected);
        selectedContainer.IsSelected = true;
        Assert.Same(selected, model.Selected);
        Assert.Equal(1, notifications);
        Assert.True(viewer.HasSelection);
        var search = Assert.IsType<TextBox>(viewer.FindName("PART_SearchBox"));
        search.Text = "name";
        Layout(viewer);
        Assert.Equal("name", model.Query);
        Assert.Same(selected, model.Selected);
        Assert.True(Container(viewer, selected).IsSelected);
        Assert.Equal(1, notifications);
        Assert.True(Row(viewer, selected).IsMatched);
        model.Query = "target";
        Layout(viewer);
        Assert.Null(model.Selected);
        Assert.Equal(2, notifications);
        Assert.False(viewer.HasSelection);
        Assert.Equal("target", search.Text);
        Assert.NotNull(viewer.GetBindingExpression(JsonTreeViewer.SearchTextProperty));
        Assert.NotNull(viewer.GetBindingExpression(JsonTreeViewer.SelectedNodeProperty));
        viewer.JsonText = "{\"unrelated\":1}";
        Assert.False(viewer.RootNode!.IsVisible); // The active filter applies to newly parsed data.
        Assert.Equal("Nodes: 2 | Showing: 0", viewer.StatusText);
    }

    [Fact]
    public void TheToolbarExpandsCollapsesAndEnablesCopyForASelection()
    {
        var viewer = Create("{\"nested\":{\"value\":5}}");
        var node = viewer.RootNode!.Children[0];
        Click(viewer, "PART_CollapseAllButton");
        Assert.False(viewer.RootNode.IsExpanded);
        Assert.False(node.IsExpanded);
        Click(viewer, "PART_ExpandAllButton");
        Assert.True(viewer.RootNode.IsExpanded);
        Assert.True(node.IsExpanded);
        viewer.SelectedNode = node.Children[0];
        Assert.True(Assert.IsAssignableFrom<ButtonBase>(viewer.FindName("PART_CopyPathButton")).IsEnabled);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("1e999")]
    [InlineData("")]
    public void InvalidNumbersStayPendingAndNeverChangeTheModel(string text)
    {
        var viewer = Create("{\"number\":7}", editable: true);
        var node = viewer.RootNode!.Children[0];
        var row = Row(viewer, node);
        Assert.True(viewer.BeginEditNode(node));
        var editor = Assert.IsType<TextBox>(row.FindName("PART_ValueEditor"));
        editor.Text = text;
        Assert.False(row.CommitEdit());
        Assert.Equal(7L, node.Value);
        Assert.True(row.IsEditing);
        Assert.True(row.HasError);
        Assert.Equal("Invalid", VisualStateManager.GetCurrentStateName(row, "ValidationStates"));
        Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(row.FindName("PART_ValidationMessage")).Visibility);
        row.CancelEdit();
    }

    [Fact]
    public void TypedCommitBubblesOnceAndEscapeKeepsTheLastCommittedValue()
    {
        var viewer = Create("{\"number\":7,\"flag\":true,\"nothing\":null}", editable: true);
        var node = viewer.RootNode!.Children[0];
        var row = Row(viewer, node);
        int commits = 0;
        viewer.NodeValueEdited += (_, e) => { commits++; Assert.Same(node, e.Node); Assert.Equal(7L, e.OldValue); Assert.Equal(2.5d, e.NewValue); };
        Assert.True(viewer.BeginEditNode(node));
        var editor = Assert.IsType<TextBox>(row.FindName("PART_ValueEditor"));
        editor.Text = "2.5";
        Key(editor, Jalium.UI.Input.Key.Enter);
        Assert.Equal(2.5d, node.Value);
        Assert.Equal(1, commits);
        Assert.False(row.IsEditing);
        row.BeginEdit();
        editor.Text = "100";
        Key(editor, Jalium.UI.Input.Key.Escape);
        editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, editor));
        Assert.Equal(2.5d, node.Value);
        Assert.Equal(1, commits);
        var flag = Row(viewer, viewer.RootNode.Children[1]);
        flag.BeginEdit(); flag.EditText = "maybe";
        Assert.False(flag.CommitEdit());
        flag.CancelEdit();
        var nil = Row(viewer, viewer.RootNode.Children[2]);
        nil.BeginEdit(); nil.EditText = "anything";
        Assert.False(nil.CommitEdit());
        nil.CancelEdit();
    }

    [Fact]
    public void ReplacingTheViewerTemplateReleasesOldPartsAndRestoresPendingInput()
    {
        var viewer = Create("{\"number\":7}", editable: true);
        var node = viewer.RootNode!.Children[0];
        var oldRow = Row(viewer, node);
        var oldTree = Assert.IsType<TreeView>(viewer.FindName("PART_TreeView"));
        var oldSearch = Assert.IsType<TextBox>(viewer.FindName("PART_SearchBox"));
        var oldCollapse = Assert.IsAssignableFrom<ButtonBase>(viewer.FindName("PART_CollapseAllButton"));
        var oldEditor = Assert.IsType<TextBox>(oldRow.FindName("PART_ValueEditor"));
        viewer.BeginEditNode(node);
        oldEditor.Text = "42";
        var original = viewer.Template;
        viewer.Template = null;
        Assert.Empty(oldTree.Items);
        Assert.False(oldRow.IsEditing);
        Assert.Equal(7L, node.Value);
        oldSearch.Text = "retired";
        oldCollapse.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, oldCollapse));
        oldEditor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, oldEditor));
        Assert.Equal(string.Empty, viewer.SearchText);
        Assert.True(viewer.RootNode.IsExpanded);
        Assert.Equal(7L, node.Value);
        viewer.Template = original;
        Layout(viewer);
        var replacement = Row(viewer, node);
        Assert.NotSame(oldRow, replacement);
        Assert.True(replacement.IsEditing);
        Assert.Equal("42", replacement.EditText);
        Assert.True(replacement.CommitEdit());
        Assert.Equal(42L, node.Value);
    }

    [Fact]
    public void ReplacingAPresenterTemplatePreservesThePendingEditAndDetachesTheEditor()
    {
        var viewer = Create("{\"number\":7}", editable: true);
        var node = viewer.RootNode!.Children[0];
        var row = Row(viewer, node);
        row.BeginEdit();
        row.EditText = "9";
        var oldEditor = Assert.IsType<TextBox>(row.FindName("PART_ValueEditor"));
        var original = row.Template;
        row.Template = null;
        Assert.True(row.IsEditing);
        Assert.Equal("9", row.EditText);
        oldEditor.Text = "999";
        oldEditor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, oldEditor));
        Assert.Equal(7L, node.Value);
        Assert.Equal("9", row.EditText);
        row.Template = original;
        Layout(viewer);
        Assert.Equal("9", Assert.IsType<TextBox>(row.FindName("PART_ValueEditor")).Text);
        viewer.IsEditable = false;
        Assert.False(row.IsEditing);
    }

    [Fact]
    public void ConsumersCanReplaceNodeTemplatesAndMissingPartsRemainOptional()
    {
        var viewer = Create("{\"name\":\"first\"}");
        viewer.NodeTemplate = Assert.IsType<DataTemplate>(Jalium.UI.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.jalium.ui/2024">
                <TextBlock Text="{Binding Key}" />
            </DataTemplate>
            """));
        Layout(viewer);
        Assert.Empty(Descendants<JsonTreeNodePresenter>(viewer));
        Assert.Contains(Descendants<TextBlock>(viewer), text => text.Text == "name");
        Assert.False(viewer.BeginEditNode(viewer.RootNode!.Children[0]));
    }

    [Fact]
    public void IndentSizeChangesUpdateContainerTemplatesWithoutRebuildingRows()
    {
        var viewer = Create("{\"nested\":{\"value\":5}}");
        var leaf = viewer.RootNode!.Children[0].Children[0];
        var item = Container(viewer, leaf);
        var row = Row(viewer, leaf);
        Assert.Equal(40, item.IndentWidth);
        viewer.IndentSize = 30;
        Assert.Equal(60, item.IndentWidth);
        Assert.Equal(60, Assert.IsType<Grid>(item.FindName("PART_IndentSpacer")).Width);
        Assert.Same(row, Row(viewer, leaf));
        Assert.Throws<ArgumentException>(() => viewer.IndentSize = double.NaN);
        Assert.Throws<ArgumentException>(() => viewer.MaxRenderDepth = -1);
        viewer.MaxRenderDepth = 1;
        Assert.DoesNotContain(Descendants<TreeViewItem>(viewer), candidate => ReferenceEquals(candidate.Tag, leaf));
    }

    [Fact]
    public void EmptyErrorAndDisabledStatesRemainAvailableWithAccessibleText()
    {
        var viewer = Create(null);
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(viewer, "DataStates"));
        Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(viewer.FindName("PART_EmptyMessage")).Visibility);
        viewer.JsonText = "{";
        Assert.Equal("Error", VisualStateManager.GetCurrentStateName(viewer, "DataStates"));
        Assert.StartsWith("Parse error:", Assert.IsType<TextBlock>(viewer.FindName("PART_StatusText")).Text);
        Assert.Null(viewer.RootNode);
        viewer.JsonText = "1";
        viewer.IsEnabled = false;
        Assert.Equal("Ready", VisualStateManager.GetCurrentStateName(viewer, "DataStates"));
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(viewer, "CommonStates"));
    }

    [Fact]
    public void AutomationHonorsReadOnlyAndPreservesTheJsonBinding()
    {
        var model = new Model();
        var viewer = Create("1");
        viewer.SetBinding(JsonTreeViewer.JsonTextProperty, new Binding(nameof(Model.Json)) { Source = model, Mode = BindingMode.TwoWay });
        var peer = Assert.IsType<JsonTreeViewerAutomationPeer>(FrameworkElementAutomationPeer.CreatePeerForElement(viewer));
        var provider = (IValueProvider)peer;
        Assert.Throws<InvalidOperationException>(() => provider.SetValue("2"));
        viewer.IsEditable = true;
        provider.SetValue("2");
        Assert.Equal("2", model.Json);
        Assert.Equal(2L, viewer.RootNode!.Value);
        Assert.NotNull(viewer.GetBindingExpression(JsonTreeViewer.JsonTextProperty));
        viewer.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => provider.SetValue("3"));
    }

    [Fact]
    public void AStandalonePresenterSupportsOptionalPartsModelReplacementAndDisable()
    {
        var node = new JsonTreeNode { Key = "label", NodeType = JsonNodeType.String, Value = "first" };
        var template = new ControlTemplate(typeof(JsonTreeNodePresenter));
        template.SetVisualTree(() => new Border());
        var row = new JsonTreeNodePresenter { Node = node, IsEditable = true, Template = template };
        Layout(row);
        Assert.True(row.BeginEdit());
        row.EditText = "pending";
        var replacement = new JsonTreeNode { Key = "next", NodeType = JsonNodeType.Number, Value = 7L };
        row.Node = replacement;
        Assert.False(row.IsEditing);
        Assert.Equal("first", node.Value);
        node.Value = "retired";
        Assert.Equal("7", row.ValueText);
        Assert.True(row.BeginEdit());
        row.EditText = "9";
        row.IsEnabled = false;
        Assert.False(row.IsEditing);
        Assert.False(row.CommitEdit());
        Assert.Equal(7L, replacement.Value);
        row.IsEnabled = true;
        Assert.True(row.BeginEdit());
        row.EditText = "2.5";
        Assert.True(row.CommitEdit());
        Assert.Equal(2.5d, replacement.Value);
    }

    [Fact]
    public void RichHeadersKeepTheirSemanticColorsOnACustomizableSelectionSurface()
    {
        var viewer = Create("{\"number\":7}");
        var node = viewer.RootNode!.Children[0];
        var item = Container(viewer, node);
        var row = Row(viewer, node);
        var semanticBrush = row.TypeBrush;
        var selected = new SolidColorBrush(Colors.White);
        item.SelectionBrush = selected;
        item.IsSelected = true;
        var header = Assert.IsType<Border>(item.FindName("PART_HeaderBorder"));
        Assert.Same(selected, header.Background);
        Assert.Same(item.SelectionBorderBrush, header.BorderBrush);
        Assert.Equal(new Thickness(1), header.BorderThickness);
        Assert.Same(semanticBrush, Assert.IsType<TextBlock>(row.FindName("PART_Value")).Foreground);
        var replacement = new SolidColorBrush(Colors.LightGray);
        item.SelectionBrush = replacement;
        Assert.Same(replacement, header.Background);
        item.IsSelected = false;
        Assert.NotSame(replacement, header.Background);
    }

    [Fact]
    public void InlineEditsWriteBackJsonWithoutReplacingNodesSelectionOrBindings()
    {
        var model = new Model { Json = "{\"number\":7,\"text\":\"\u4e2d\u6587\",\"items\":[1,2,3]}" };
        var viewer = Create(model.Json, editable: true);
        viewer.SetBinding(JsonTreeViewer.JsonTextProperty, new Binding(nameof(Model.Json)) { Source = model });
        var root = viewer.RootNode!;
        var node = root.Children[0];
        var row = Row(viewer, node);
        viewer.SelectedNode = node;
        row.BeginEdit(); row.EditText = "9";
        Assert.True(row.CommitEdit());
        using (var document = System.Text.Json.JsonDocument.Parse(model.Json))
        {
            Assert.Equal(9, document.RootElement.GetProperty("number").GetInt32());
            Assert.Equal(3, document.RootElement.GetProperty("items")[2].GetInt32());
            Assert.Equal("\u4e2d\u6587", document.RootElement.GetProperty("text").GetString());
        }
        Assert.Contains("\u4e2d\u6587", model.Json);
        Assert.Same(root, viewer.RootNode);
        Assert.Same(node, viewer.SelectedNode);
        Assert.Same(row, Row(viewer, node));
        Assert.NotNull(viewer.GetBindingExpression(JsonTreeViewer.JsonTextProperty));
        var committedJson = model.Json;
        row.BeginEdit(); row.EditText = "100"; row.CancelEdit();
        Assert.Equal(committedJson, model.Json);
    }

    [Fact]
    public void ToolbarActionsWrapWithinANarrowViewer()
    {
        var viewer = Create("{\"number\":7}");
        viewer.Width = 240;
        Layout(viewer);
        var expand = Assert.IsAssignableFrom<ButtonBase>(viewer.FindName("PART_ExpandAllButton"));
        var copy = Assert.IsAssignableFrom<ButtonBase>(viewer.FindName("PART_CopyPathButton"));
        var first = expand.TranslatePoint(new Point(0, 0), viewer);
        var last = copy.TranslatePoint(new Point(0, 0), viewer);
        Assert.True(last.Y > first.Y);
        Assert.True(last.X + copy.ActualWidth <= viewer.ActualWidth);
        Assert.Equal(TextTrimming.CharacterEllipsis, Assert.IsType<TextBlock>(viewer.FindName("PART_StatusText")).TextTrimming);
    }

    private static JsonTreeViewer Create(string? json, bool editable = false)
    {
        var viewer = new JsonTreeViewer { JsonText = json, IsEditable = editable };
        viewer.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(viewer);
        Assert.NotNull(viewer.NodeTemplate);
        var rows = Descendants<JsonTreeNodePresenter>(viewer).ToArray();
        if (json != null && string.IsNullOrEmpty(viewer.ParseError))
            Assert.True(rows.Length > 0 && rows.Any(row => row.Node != null), "No data rows. Containers: " + string.Join(" | ", Descendants<TreeViewItem>(viewer).Select(item => $"{item.Header} template:{item.HeaderTemplate != null}")) + " Rows: " + string.Join(" | ", rows.Select(row => $"node:{row.Node} context:{row.DataContext}")));
        return viewer;
    }
    private static void Layout(FrameworkElement control)
    {
        control.Measure(new Size(600, 500));
        control.Arrange(new Rect(0, 0, 600, 500));
        control.UpdateLayout();
    }
    private static JsonTreeNodePresenter Row(JsonTreeViewer viewer, JsonTreeNode node) => Assert.Single(Descendants<JsonTreeNodePresenter>(viewer), row => ReferenceEquals(row.Node, node));
    private static TreeViewItem Container(JsonTreeViewer viewer, JsonTreeNode node) => Assert.Single(Descendants<TreeViewItem>(viewer), item => ReferenceEquals(item.Tag, node));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i)!;
            if (child is T target) yield return target;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Click(JsonTreeViewer viewer, string name)
    {
        var part = Assert.IsAssignableFrom<ButtonBase>(viewer.FindName(name));
        part.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, part));
    }
    private static void Key(UIElement target, Jalium.UI.Input.Key key) => target.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, new Source { RootVisual = target }, 1, key) { RoutedEvent = UIElement.KeyDownEvent });
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private sealed class Model : INotifyPropertyChanged
    {
        private string _query = string.Empty;
        private string _json = "1";
        private JsonTreeNode? _selected;
        public string Query { get => _query; set { _query = value; PropertyChanged?.Invoke(this, new(nameof(Query))); } }
        public string Json { get => _json; set { _json = value; PropertyChanged?.Invoke(this, new(nameof(Json))); } }
        public JsonTreeNode? Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
