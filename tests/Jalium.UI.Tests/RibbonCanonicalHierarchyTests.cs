using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Ribbon;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class RibbonCanonicalHierarchyTests
{
    [Fact]
    public void DirectBasesMatchWpf10()
    {
        Assert.Equal(typeof(Selector), typeof(Ribbon).BaseType);
        Assert.Equal(typeof(RibbonMenuButton), typeof(RibbonApplicationMenu).BaseType);
        Assert.Equal(typeof(RibbonMenuItem), typeof(RibbonApplicationMenuItem).BaseType);
        Assert.Equal(typeof(RibbonSplitMenuItem), typeof(RibbonApplicationSplitMenuItem).BaseType);
        Assert.Equal(typeof(RibbonMenuButton), typeof(RibbonComboBox).BaseType);
        Assert.Equal(typeof(HeaderedItemsControl), typeof(RibbonGalleryCategory).BaseType);
        Assert.Equal(typeof(HeaderedItemsControl), typeof(RibbonGroup).BaseType);
        Assert.Equal(typeof(Menu), typeof(RibbonMenuButton).BaseType);
        Assert.Equal(typeof(Separator), typeof(RibbonSeparator).BaseType);
        Assert.Equal(typeof(RibbonMenuButton), typeof(RibbonSplitButton).BaseType);
        Assert.Equal(typeof(HeaderedItemsControl), typeof(RibbonTab).BaseType);
    }

    [Fact]
    public void MissingWpfIntermediateTypesAndEnumsAreExported()
    {
        Assert.Equal(typeof(MenuItem), typeof(RibbonMenuItem).BaseType);
        Assert.Equal(typeof(RibbonMenuItem), typeof(RibbonSplitMenuItem).BaseType);
        Assert.False(typeof(RibbonApplicationMenuItem).IsSealed);
        Assert.False(typeof(RibbonApplicationSplitMenuItem).IsSealed);
        Assert.False(typeof(RibbonComboBox).IsSealed);
        Assert.Equal([0, 1, 2], Enum.GetValues<RibbonApplicationMenuItemLevel>().Select(value => (int)value));
        Assert.Equal([0, 1], Enum.GetValues<RibbonSplitButtonLabelPosition>().Select(value => (int)value));

        Type[] forwarded = Assembly.Load("Jalium.UI.Controls").GetForwardedTypes();
        Assert.Contains(typeof(RibbonMenuItem), forwarded);
        Assert.Contains(typeof(RibbonSplitMenuItem), forwarded);
        Assert.Contains(typeof(RibbonApplicationMenuItemLevel), forwarded);
        Assert.Contains(typeof(RibbonSplitButtonLabelPosition), forwarded);
    }

    [Fact]
    public void DuplicateMembersAreInheritedFromCanonicalBases()
    {
        const BindingFlags DeclaredPublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags DeclaredPublicInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        Assert.Null(typeof(Ribbon).GetField(nameof(Selector.SelectedIndexProperty), DeclaredPublicStatic));
        Assert.Null(typeof(Ribbon).GetProperty(nameof(Selector.SelectedIndex), DeclaredPublicInstance));
        Assert.Null(typeof(RibbonTab).GetField(nameof(HeaderedItemsControl.HeaderProperty), DeclaredPublicStatic));
        Assert.Null(typeof(RibbonGroup).GetField(nameof(HeaderedItemsControl.HeaderProperty), DeclaredPublicStatic));
        Assert.Null(typeof(RibbonGalleryCategory).GetProperty(nameof(HeaderedItemsControl.Header), DeclaredPublicInstance));

        foreach (string member in new[] { "Label", "SmallImageSource", "LargeImageSource", "IsDropDownOpen", "KeyTip" })
        {
            Assert.Null(typeof(RibbonSplitButton).GetProperty(member, DeclaredPublicInstance));
        }

        Assert.Null(typeof(RibbonApplicationMenu).GetProperty(nameof(RibbonMenuButton.SmallImageSource), DeclaredPublicInstance));
        Assert.Null(typeof(RibbonApplicationMenu).GetProperty(nameof(RibbonMenuButton.KeyTip), DeclaredPublicInstance));
        Assert.Null(typeof(RibbonComboBox).GetProperty(nameof(RibbonMenuButton.Label), DeclaredPublicInstance));
        Assert.Null(typeof(RibbonComboBox).GetProperty(nameof(RibbonMenuButton.SmallImageSource), DeclaredPublicInstance));
    }

    [Fact]
    public void RibbonSelectionSynchronizesTabContainersInBothDirections()
    {
        var ribbon = new Ribbon();
        var first = new RibbonTab { Header = "First" };
        var second = new RibbonTab { Header = "Second" };
        ribbon.Items.Add(first);
        ribbon.Items.Add(second);
        ribbon.Measure(new Size(400, 200));
        ribbon.Arrange(new Rect(0, 0, 400, 200));

        ribbon.SelectedIndex = 1;

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Same(second, ribbon.SelectedItem);

        first.IsSelected = true;

        Assert.Equal(0, ribbon.SelectedIndex);
        Assert.Same(first, ribbon.SelectedItem);
        Assert.False(second.IsSelected);
    }

    [Fact]
    public void RibbonComboBoxTracksFirstGallerySelection()
    {
        var gallery = new RibbonGallery
        {
            ItemStringFormat = "Selected: {0}",
        };
        var comboBox = new RibbonComboBox();
        comboBox.Items.Add(gallery);

        gallery.SelectedItem = "Blue";

        Assert.Equal("Blue", comboBox.SelectionBoxItem);
        Assert.Equal("Selected: Blue", comboBox.Text);
        Assert.Equal(gallery.ItemStringFormat, comboBox.SelectionBoxItemStringFormat);
    }

    [Fact]
    public void MenuButtonRaisesDropDownEventsAndDetectsGalleries()
    {
        var menuButton = new RibbonMenuButton();
        var opened = 0;
        var closed = 0;
        menuButton.DropDownOpened += (_, _) => opened++;
        menuButton.DropDownClosed += (_, _) => closed++;

        menuButton.Items.Add(new RibbonGallery());
        menuButton.IsDropDownOpen = true;
        menuButton.IsDropDownOpen = false;

        Assert.True(menuButton.HasGallery);
        Assert.Equal(1, opened);
        Assert.Equal(1, closed);
        Assert.False(menuButton.IsMainMenu);
    }

    [Fact]
    public void SplitButtonCommandParticipatesInCanExecuteAndClick()
    {
        var canExecute = false;
        var executions = 0;
        var command = new ProbeCommand(() => canExecute, () => executions++);
        var splitButton = new RibbonSplitButton { Command = command, IsCheckable = true };
        var clicks = 0;
        splitButton.Click += (_, _) => clicks++;

        Assert.False(splitButton.IsEnabled);

        canExecute = true;
        command.RaiseCanExecuteChanged();
        Assert.True(splitButton.IsEnabled);

        splitButton.RaiseEvent(CreateMouseUp(splitButton));

        Assert.Equal(1, clicks);
        Assert.Equal(1, executions);
        Assert.True(splitButton.IsChecked);
    }

    private static MouseButtonEventArgs CreateMouseUp(UIElement source)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseLeftButtonUpEvent,
            new Point(5, 5),
            MouseButton.Left,
            MouseButtonState.Released,
            clickCount: 1,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0)
        {
            Source = source,
        };
    }

    private sealed class ProbeCommand(Func<bool> canExecute, Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
