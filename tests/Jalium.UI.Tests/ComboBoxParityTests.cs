using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class ComboBoxParityTests
{
    [Fact]
    public void PublicSurface_DeclaresWpfReadOnlyStatePresentationAndVirtualHooks()
    {
        var type = typeof(ComboBox);
        foreach (string fieldName in new[]
                 {
                     nameof(ComboBox.IsReadOnlyProperty),
                     nameof(ComboBox.ShouldPreserveUserEnteredPrefixProperty),
                     nameof(ComboBox.SelectionBoxItemTemplateProperty),
                     nameof(ComboBox.SelectionBoxItemStringFormatProperty),
                 })
        {
            Assert.Equal(
                typeof(DependencyProperty),
                type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)?.FieldType);
        }

        Assert.Same(TextBoxBase.IsReadOnlyProperty, ComboBox.IsReadOnlyProperty);
        Assert.True(ComboBox.SelectionBoxItemProperty.ReadOnly);
        Assert.True(ComboBox.SelectionBoxItemTemplateProperty.ReadOnly);
        Assert.True(ComboBox.SelectionBoxItemStringFormatProperty.ReadOnly);

        Assert.False(type.GetProperty(nameof(ComboBox.SelectionBoxItemTemplate))!.SetMethod!.IsPublic);
        Assert.False(type.GetProperty(nameof(ComboBox.SelectionBoxItemStringFormat))!.SetMethod!.IsPublic);
        Assert.Null(type.GetProperty(nameof(ComboBox.IsSelectionBoxHighlighted))!.SetMethod);

        foreach (string methodName in new[] { "OnDropDownOpened", "OnDropDownClosed" })
        {
            var method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                binder: null,
                types: new[] { typeof(EventArgs) },
                modifiers: null);
            Assert.NotNull(method);
            Assert.True(method!.IsFamily);
            Assert.True(method.IsVirtual);
        }

        var comboBox = new ComboBox();
        Assert.False(comboBox.IsReadOnly);
        Assert.False(comboBox.ShouldPreserveUserEnteredPrefix);
        Assert.False(comboBox.IsSelectionBoxHighlighted);
        Assert.True(comboBox.IsTextSearchEnabled);
    }

    [Fact]
    public void SelectionBox_UsesLogicalItemTemplateAndStringFormat_AndTextUsesDisplayPath()
    {
        var item = new ProbeItem("Alpha");
        var template = new DataTemplate();
        var comboBox = new ComboBox
        {
            DisplayMemberPath = nameof(ProbeItem.Name),
            ItemTemplate = template,
            ItemStringFormat = "Value: {0}",
        };
        comboBox.Items.Add(item);

        comboBox.SelectedIndex = 0;

        Assert.Same(item, comboBox.SelectionBoxItem);
        Assert.Same(template, comboBox.SelectionBoxItemTemplate);
        Assert.Equal("Value: {0}", comboBox.SelectionBoxItemStringFormat);
        Assert.Equal("Alpha", comboBox.Text);

        var containerTemplate = new DataTemplate();
        var ownContainer = new ComboBoxItem
        {
            Content = item,
            ContentTemplate = containerTemplate,
            ContentStringFormat = "Container: {0}",
        };
        var ownContainerCombo = new ComboBox();
        ownContainerCombo.Items.Add(ownContainer);
        ownContainerCombo.SelectedIndex = 0;

        Assert.Same(item, ownContainerCombo.SelectionBoxItem);
        Assert.Same(containerTemplate, ownContainerCombo.SelectionBoxItemTemplate);
        Assert.Equal("Container: {0}", ownContainerCombo.SelectionBoxItemStringFormat);
    }

    [Fact]
    public void GeneratedContainer_PreservesLogicalItemAndPresentationContract()
    {
        var item = new ProbeItem("Alpha");
        var tag = new object();
        var selector = new DataTemplateSelector();
        var comboBox = new ProbeComboBox
        {
            ItemTemplateSelector = selector,
            ItemStringFormat = "Value: {0}",
        };
        var container = new ComboBoxItem { Tag = tag };

        comboBox.Prepare(container, item);

        Assert.Same(item, container.Content);
        Assert.Same(selector, container.ContentTemplateSelector);
        Assert.Equal("Value: {0}", container.ContentStringFormat);
        Assert.Same(tag, container.Tag);
        Assert.IsAssignableFrom<ListBoxItem>(container);
    }

    [Fact]
    public void DropDownState_RaisesClrEventsThroughVirtualHooksExactlyOnce()
    {
        var comboBox = new ProbeComboBox();
        int openedEvents = 0;
        int closedEvents = 0;
        comboBox.DropDownOpened += (_, _) => openedEvents++;
        comboBox.DropDownClosed += (_, _) => closedEvents++;

        comboBox.IsDropDownOpen = true;
        comboBox.IsDropDownOpen = false;

        Assert.Equal(1, comboBox.OpenedCalls);
        Assert.Equal(1, comboBox.ClosedCalls);
        Assert.Equal(1, openedEvents);
        Assert.Equal(1, closedEvents);
    }

    [Fact]
    public void NonEditableTextSearch_IsEnabledByDefaultAndHonorsCaseSensitivity()
    {
        var comboBox = new ComboBox();
        comboBox.Items.Add("Alpha");
        comboBox.Items.Add("Beta");
        var insensitiveInput = new TextCompositionEventArgs(UIElement.TextInputEvent, "b", 0);

        comboBox.RaiseEvent(insensitiveInput);

        Assert.True(insensitiveInput.Handled);
        Assert.Equal(1, comboBox.SelectedIndex);
        Assert.Equal("Beta", comboBox.Text);

        var caseSensitiveComboBox = new ComboBox { IsTextSearchCaseSensitive = true };
        caseSensitiveComboBox.Items.Add("Alpha");
        caseSensitiveComboBox.Items.Add("Beta");
        var wrongCaseInput = new TextCompositionEventArgs(UIElement.TextInputEvent, "b", 0);
        caseSensitiveComboBox.RaiseEvent(wrongCaseInput);
        Assert.False(wrongCaseInput.Handled);
        Assert.Equal(-1, caseSensitiveComboBox.SelectedIndex);

        var matchingCaseInput = new TextCompositionEventArgs(UIElement.TextInputEvent, "B", 1);
        caseSensitiveComboBox.RaiseEvent(matchingCaseInput);
        Assert.True(matchingCaseInput.Handled);
        Assert.Equal(1, caseSensitiveComboBox.SelectedIndex);
    }

    [Fact]
    public void EditableTextSearch_CompletesAndPreservesUserPrefix_WhileReadOnlyFlowsToEditor()
    {
        TextBox? editor = null;
        var template = new ControlTemplate(typeof(ComboBox));
        template.SetVisualTree(() => editor = new TextBox { Name = "PART_EditableTextBox" });
        var comboBox = new ComboBox
        {
            Template = template,
            IsEditable = true,
            IsReadOnly = true,
            ShouldPreserveUserEnteredPrefix = true,
        };
        comboBox.Items.Add("Alpha");
        comboBox.Items.Add("Beta");
        comboBox.Measure(new Size(200, 40));
        comboBox.Arrange(new Rect(0, 0, 200, 40));

        var editableTextBox = Assert.IsType<TextBox>(editor);
        Assert.True(editableTextBox.IsReadOnly);

        comboBox.IsReadOnly = false;
        editableTextBox.Text = "aL";

        Assert.False(editableTextBox.IsReadOnly);
        Assert.Equal(0, comboBox.SelectedIndex);
        Assert.Equal("aLpha", comboBox.Text);
        Assert.Equal("aLpha", editableTextBox.Text);
        Assert.Equal(2, editableTextBox.SelectionStart);
        Assert.Equal(3, editableTextBox.SelectionLength);
    }

    [Fact]
    public void ComboBoxItem_InheritsListBoxItemAndExposesProtectedReadOnlyHighlight()
    {
        Assert.Same(typeof(ListBoxItem), typeof(ComboBoxItem).BaseType);
        Assert.True(ComboBoxItem.IsHighlightedProperty.ReadOnly);

        var property = typeof(ComboBoxItem).GetProperty(nameof(ComboBoxItem.IsHighlighted));
        Assert.NotNull(property);
        Assert.True(property!.SetMethod!.IsFamily);

        var item = new ProbeComboBoxItem();
        item.SetHighlighted(true);
        Assert.True(item.IsHighlighted);
        Assert.Throws<InvalidOperationException>(() =>
            item.SetValue(ComboBoxItem.IsHighlightedProperty, false));
    }

    private sealed record ProbeItem(string Name);

    private sealed class ProbeComboBox : ComboBox
    {
        public int OpenedCalls { get; private set; }
        public int ClosedCalls { get; private set; }

        public void Prepare(ComboBoxItem container, object item) =>
            PrepareContainerForItem(container, item);

        protected override void OnDropDownOpened(EventArgs e)
        {
            OpenedCalls++;
            base.OnDropDownOpened(e);
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            ClosedCalls++;
            base.OnDropDownClosed(e);
        }
    }

    private sealed class ProbeComboBoxItem : ComboBoxItem
    {
        public void SetHighlighted(bool value) => IsHighlighted = value;
    }
}
