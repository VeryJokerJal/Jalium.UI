using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Input;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed unsafe class MacOSItemContentAccessibilityTests
{
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public void ExplicitAndTemplatedItemControlsRemainReachableAndOperable(bool outline, int mode)
    {
        using var f = new Fixture(outline, mode);
        ulong row = f.RowId; ulong check = Assert.Single(f.Children(row), f.IsCheck);
        Assert.Equal(row, f.Info(check).ParentId);
        Assert.Equal("项目开关 中文🙂", f.Text(check));
        Assert.True(f.Action(check, MacOSAXAction.SetFocus));
        Assert.Equal(check, f.Request(1, MacOSAXOperation.Focus).ResultId);
        Assert.True(f.Action(check, MacOSAXAction.Press)); Assert.True(f.Check!.IsChecked);
        Assert.Equal(1, f.Info(check).Value); Assert.False(f.IsSelected);
        Assert.True(f.Action(check, MacOSAXAction.Press)); Assert.False(f.Check.IsChecked);
        Assert.Equal(0, f.Info(check).Value); Assert.Equal(check, Assert.Single(f.Children(row), f.IsCheck));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReplacingContentRetiresItsOldActionAndPublishesTheReplacement(bool outline)
    {
        using var f = new Fixture(outline, 0); ulong row = f.RowId;
        ulong old = Assert.Single(f.Children(row), f.IsCheck); var previous = f.Check!;
        f.ReplaceContent(); ulong current = Assert.Single(f.Children(row), f.IsCheck);
        Assert.NotEqual(old, current); Assert.False(f.TryInfo(old)); Assert.False(f.Action(old, MacOSAXAction.Press));
        Assert.False(previous.IsChecked); Assert.Equal(row, f.Info(current).ParentId);
        Assert.True(f.Action(current, MacOSAXAction.Press)); Assert.True(f.Check!.IsChecked); Assert.False(f.IsSelected);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReplacingAnItemTemplateDoesNotKeepCachedOldControls(bool outline)
    {
        using var f = new Fixture(outline, 1); ulong row = f.RowId;
        ulong old = Assert.Single(f.Children(row), f.IsCheck); var previous = f.Check!;
        f.ReplaceTemplate(); ulong current = Assert.Single(f.Children(f.RowId), f.IsCheck);
        Assert.NotEqual(old, current); Assert.False(f.TryInfo(old)); Assert.False(f.Action(old, MacOSAXAction.Press));
        Assert.False(previous.IsChecked); Assert.True(f.Action(current, MacOSAXAction.Press)); Assert.True(f.Check!.IsChecked);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void CollapsingAGeneratedHeaderHidesChildRowsAndRetainsHeaderActions(int mode)
    {
        using var f = new Fixture(true, mode); var row = (TreeViewItem)f.Row;
        row.Items.Add(new TreeViewItem { Header = "嵌套项目", Template = RowTemplate(true) });
        row.IsExpanded = true; f.Layout(); ulong id = f.RowId;
        var expanded = f.Children(id); ulong header = Assert.Single(expanded, f.IsCheck);
        Assert.Single(expanded, x => f.Info(x).Role == (int)AutomationControlType.TreeItem);
        Assert.True(f.Action(id, MacOSAXAction.Collapse)); f.Layout();
        Assert.Equal(header, Assert.Single(f.Children(id))); Assert.True(f.Action(header, MacOSAXAction.Press));
        Assert.True(f.Check!.IsChecked); Assert.False(f.IsSelected);
        Assert.True(f.Action(id, MacOSAXAction.Expand)); f.Layout();
        Assert.Contains(header, f.Children(id)); Assert.Equal(2, f.Children(id).Count);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void ACustomRowTemplateCanExposeItsActionWithoutACustomAutomationPeer(bool outline, bool unusedContent)
    {
        using var f = new Fixture(outline, unusedContent ? 5 : 3); ulong id = Assert.Single(f.Children(f.RowId), f.IsCheck);
        Assert.Equal(f.RowId, f.Info(id).ParentId); Assert.True(f.Action(id, MacOSAXAction.Press));
        Assert.True(f.Check!.IsChecked); Assert.False(f.IsSelected);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PlainStringRowsKeepTheirSingleTextRepresentation(bool outline)
    {
        using var f = new Fixture(outline, 4);
        Assert.Empty(f.Children(f.RowId)); Assert.Equal("纯文字项目", f.Text(f.RowId));
    }

    private sealed class ChooseTemplate(DataTemplate template) : DataTemplateSelector
    { public override DataTemplate? SelectTemplate(object? item, DependencyObject? container) => template; }
    private sealed class RowList : ListBox
    {
        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        { base.PrepareContainerForItem(element, item); ((Control)element).Template = RowTemplate(false); element.Height = 40; }
    }
    private sealed class RowTree : TreeView
    {
        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        { base.PrepareContainerForItem(element, item); ((Control)element).Template = RowTemplate(true); element.MinHeight = 40; }
    }
    private static ControlTemplate RowTemplate(bool outline)
    {
        var template = new ControlTemplate(outline ? typeof(TreeViewItem) : typeof(ListBoxItem));
        template.SetVisualTree(() =>
        {
            var content = new ContentPresenter();
            content.SetTemplateBinding(ContentPresenter.ContentProperty, outline ? HeaderedItemsControl.HeaderProperty : ContentControl.ContentProperty);
            content.SetTemplateBinding(ContentPresenter.ContentTemplateProperty, outline ? HeaderedItemsControl.HeaderTemplateProperty : ContentControl.ContentTemplateProperty);
            content.SetTemplateBinding(ContentPresenter.ContentTemplateSelectorProperty, outline ? HeaderedItemsControl.HeaderTemplateSelectorProperty : ContentControl.ContentTemplateSelectorProperty);
            return outline ? new StackPanel { Children =
            {
                new Border { Name = "PART_HeaderBorder", Child = content }, new ItemsPresenter { Name = "PART_ItemsHost" }
            } } : content;
        });
        return template;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly bool _outline;
        private readonly DisplayedTestWindow _window = new() { TitleBarStyle = WindowTitleBarStyle.Native };
        private readonly MacOSAccessibilityTree _tree;
        private readonly ItemsControl _items;
        internal CheckBox? Check;
        internal FrameworkElement Row => _items.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement
            ?? throw new InvalidOperationException("The row was not realized.");
        internal ulong RowId { get { Assert.True(_tree.TryGetId(Row.GetAutomationPeer()!, out var id)); return id; } }
        internal bool IsSelected => _outline ? ((TreeViewItem)Row).IsSelected : ((ListBoxItem)Row).IsSelected;
        internal Fixture(bool outline, int mode)
        {
            _outline = outline; _items = outline ? new RowTree() : new RowList();
            var template = new ControlTemplate(_items.GetType()); template.SetVisualTree(() => new ItemsPresenter());
            _items.Template = template;
            if (mode is 1 or 2)
            {
                var data = Template();
                if (mode == 1) _items.ItemTemplate = data; else _items.ItemTemplateSelector = new ChooseTemplate(data);
                _items.Items.Add(new object());
            }
            else if (mode is 0 or 5)
            {
                object content = new Border { Child = NewCheck() };
                _items.Items.Add(outline ? new TreeViewItem { Header = content } : new ListBoxItem { Content = content });
            }
            else _items.Items.Add("纯文字项目");
            _window.Content = _items; Layout();
            if (mode is 3 or 5)
            {
                var custom = new ControlTemplate(Row.GetType()); custom.SetVisualTree(() => NewCheck());
                ((Control)Row).Template = custom; Layout();
            }
            if (mode != 4) Assert.NotNull(Check);
            _tree = new(_window);
        }
        private CheckBox NewCheck()
        {
            var check = new CheckBox { Height = 32, Content = "项目开关 中文🙂" };
            AutomationProperties.SetName(check, "项目开关 中文🙂"); Check = check; return check;
        }
        private DataTemplate Template()
        { var template = new DataTemplate(); template.SetVisualTree(() => new Border { Child = NewCheck() }); return template; }
        internal void ReplaceContent()
        { if (_outline) ((TreeViewItem)Row).Header = new Border { Child = NewCheck() }; else ((ListBoxItem)Row).Content = new Border { Child = NewCheck() }; Layout(); }
        internal void ReplaceTemplate() { _items.ItemTemplate = Template(); Layout(); }
        internal void Layout() { _window.Measure(new Size(320, 280)); _window.Arrange(new Rect(0, 0, 320, 280)); }
        internal bool IsCheck(ulong id) => Info(id).Role == (int)AutomationControlType.CheckBox;
        internal MacOSAXRequest Request(ulong id, MacOSAXOperation op, int index = 0)
        { var r = new MacOSAXRequest { NodeId = id, Operation = op, Index = index }; Assert.True(_tree.Handle(ref r)); return r; }
        internal MacOSAXRequest Info(ulong id) => Request(id, MacOSAXOperation.Info);
        internal bool TryInfo(ulong id) { var r = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info }; return _tree.Handle(ref r); }
        internal List<ulong> Children(ulong id) => Enumerable.Range(0, Info(id).ChildCount).Select(i => Request(id, MacOSAXOperation.Child, i).ResultId).ToList();
        internal bool Action(ulong id, MacOSAXAction action) { var r = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Action, Index = (int)action }; return _tree.Handle(ref r); }
        internal string Text(ulong id)
        { char* buffer = stackalloc char[256]; var r = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.String, Index = (int)MacOSAXString.Name, Text = buffer, TextCapacity = 256 }; Assert.True(_tree.Handle(ref r)); return new string(buffer, 0, r.TextCount); }
        public void Dispose() { Keyboard.ClearFocus(); _tree.Dispose(); _window.Close(); }
    }
}
