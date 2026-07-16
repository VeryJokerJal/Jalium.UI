using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for the "The logical child already has a parent." crash when switching
/// tabs whose TabItem.Content is a FrameworkElement (VSPSample repro, 2026-07-15).
///
/// Root scenario: each TabItem takes logical ownership of its Content at XAML-load time
/// (ContentControl.OnContentChanged -> AddLogicalChild). Selecting another tab routes that
/// same element into the shared selected-content ContentPresenter; any code path that then
/// re-parents it logically throws InvalidOperationException in FrameworkElement.AddLogicalChild.
/// </summary>
public sealed class TabControlLogicalTreeRegressionTests
{
    private static readonly Size ViewportSize = new(800, 600);
    private static readonly Rect ViewportRect = new(0, 0, 800, 600);

    private static TabControl CreateTabControl(out TabItem first, out TabItem second)
    {
        first = new TabItem { Header = "First", Content = new DockPanel() };
        second = new TabItem
        {
            Header = "Second",
            Content = new DockPanel
            {
                Children =
                {
                    new StackPanel { Children = { new TextBlock { Text = "description" } } },
                    new TextBlock { Text = "body" },
                },
            },
        };

        var tabControl = new TabControl();
        tabControl.Items.Add(first);
        tabControl.Items.Add(second);
        return tabControl;
    }

    private static void RunLayout(UIElement element)
    {
        element.Measure(ViewportSize);
        element.Arrange(ViewportRect);
    }

    [Fact]
    public void SwitchingToSecondTabWithElementContentDoesNotThrow()
    {
        var tabControl = CreateTabControl(out _, out var second);

        RunLayout(tabControl);

        tabControl.SelectedIndex = 1;
        RunLayout(tabControl);

        Assert.True(second.IsSelected);
        Assert.Same(second.Content, tabControl.SelectedContent);
    }

    [Fact]
    public void SwitchingTabsBackAndForthKeepsLogicalParentStable()
    {
        var tabControl = CreateTabControl(out var first, out var second);

        RunLayout(tabControl);

        for (int i = 0; i < 3; i++)
        {
            tabControl.SelectedIndex = 1;
            RunLayout(tabControl);
            tabControl.SelectedIndex = 0;
            RunLayout(tabControl);
        }

        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.Same(first, ((FrameworkElement)first.Content!).Parent);
        Assert.Same(second, ((FrameworkElement)second.Content!).Parent);
    }

    [Fact]
    public void SwitchingTabsInsideWindowDoesNotThrow()
    {
        var tabControl = CreateTabControl(out _, out var second);
        var window = new Window { Content = new DockPanel { Children = { tabControl } } };

        RunLayout(window);

        tabControl.SelectedIndex = 1;
        RunLayout(window);

        Assert.True(second.IsSelected);
    }

    /// <summary>
    /// Locks the logical half of UIElementCollection's automatic-reparent contract: handing a
    /// container from one panel to another must migrate the logical parent instead of throwing
    /// "The logical child already has a parent." This is the exact handoff a retired items-host
    /// panel performs against its replacement (VSPSample tab-switch crash).
    /// </summary>
    [Fact]
    public void ContainerHandoffBetweenPanelsMigratesLogicalParent()
    {
        var container = new ListBoxItem();
        var oldPanel = new StackPanel { Children = { container } };
        var newPanel = new StackPanel();

        Assert.Same(oldPanel, container.Parent);

        newPanel.Children.Add(container);

        Assert.Same(newPanel, container.Parent);
        Assert.True(newPanel.Children.Contains(container));
    }

    /// <summary>
    /// Guard lock: a panel that opts into PreserveExistingLogicalParents (MenuPopupScrollHost)
    /// must never migrate an incoming child's logical parent — menu items visually hosted in the
    /// popup scroll panel logically stay with their menu. Without this guard the logical-half
    /// migration in PrepareIncomingChild would strip the original owner first, defeating the
    /// SetLogicalParent guard that checks Parent != null.
    /// </summary>
    [Fact]
    public void PreserveExistingLogicalParentsPanelKeepsChildOwnership()
    {
        var child = new TextBlock();
        var owner = new StackPanel { Children = { child } };
        var host = new StackPanel { PreserveExistingLogicalParents = true };

        Assert.Same(owner, child.Parent);

        host.Children.Add(child);

        Assert.Same(owner, child.Parent);
        Assert.Same(host, child.VisualParent);
    }

    /// <summary>
    /// Guard lock: a UIElementCollection constructed WITHOUT a logical parent (ToolBarOverflowPanel
    /// custom-template scenario) must keep the incoming child's original logical parent — overflow
    /// items logically remain with the ToolBar. SetLogicalParent is a no-op for such collections,
    /// so the migration must not strip the old owner either.
    /// </summary>
    [Fact]
    public void CollectionWithoutLogicalParentKeepsChildOwnership()
    {
        var child = new TextBlock();
        var owner = new StackPanel { Children = { child } };
        var visualHost = new StackPanel();
        var collection = new UIElementCollection(visualHost, null);

        collection.Add(child);

        Assert.Same(owner, child.Parent);
    }

    /// <summary>
    /// Locks RetireItemsHostPanel's disarm semantics: after a fallback items host is superseded by
    /// a template panel, the retired panel must be fully decommissioned — no items-host flag, no
    /// children, and a replayed (zombie) measure must neither throw nor steal containers from the
    /// active panel. This is the deterministic replay of the VSPSample dirty-queue crash.
    /// </summary>
    [Fact]
    public void RetiredFallbackPanelIsDisarmedAndCannotStealContainers()
    {
        var listBox = new ListBox();
        for (int i = 0; i < 3; i++)
        {
            listBox.Items.Add($"item {i}");
        }

        // Templateless first measure: builds the fallback items host and realizes containers.
        listBox.Measure(ViewportSize);
        listBox.Arrange(ViewportRect);
        var fallback = listBox.ItemsHostInternal;
        Assert.NotNull(fallback);

        // Hand the control a template with an ItemsPresenter — the next measure swaps the items
        // host to the template panel and retires the fallback (the VSPSample tab-switch shape).
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => new ItemsPresenter());
        listBox.Template = template;
        listBox.Measure(ViewportSize);
        listBox.Arrange(ViewportRect);

        var templatePanel = listBox.ItemsHostInternal;
        Assert.NotNull(templatePanel);
        Assert.NotSame(fallback, templatePanel);
        Assert.Equal(3, templatePanel!.Children.Count);

        // Retired panel is disarmed and empty.
        Assert.False(fallback!.IsItemsHost);
        Assert.Equal(0, fallback.Children.Count);

        // Zombie replay: measuring the retired panel again must be a harmless no-op.
        var exception = Record.Exception(() =>
        {
            fallback.Measure(ViewportSize);
            fallback.Arrange(ViewportRect);
        });

        Assert.Null(exception);
        Assert.Equal(0, fallback.Children.Count);
        Assert.Equal(3, templatePanel.Children.Count);
        for (int i = 0; i < templatePanel.Children.Count; i++)
        {
            Assert.Same(templatePanel, ((FrameworkElement)templatePanel.Children[i]).Parent);
        }
    }
}

/// <summary>
/// Same regression scenarios but with the real theme templates applied (DefaultTabControlStyle
/// with PART_SelectedContentHost), matching what a real application exercises. The templateless
/// fallback path above never reproduced the crash; the themed path is the one VSPSample hits.
/// </summary>
[Collection("Application")]
public sealed class TabControlLogicalTreeThemedRegressionTests
{
    private static readonly Size ViewportSize = new(800, 600);
    private static readonly Rect ViewportRect = new(0, 0, 800, 600);

    private static void RunLayout(UIElement element)
    {
        element.Measure(ViewportSize);
        element.Arrange(ViewportRect);
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static TabControl CreateTabControl(out TabItem first, out TabItem second)
    {
        first = new TabItem { Header = "First", Content = new DockPanel() };
        second = new TabItem
        {
            Header = "Second",
            Content = new DockPanel
            {
                Children =
                {
                    new StackPanel { Children = { new TextBlock { Text = "description" } } },
                    new TextBlock { Text = "body" },
                },
            },
        };

        var tabControl = new TabControl();
        tabControl.Items.Add(first);
        tabControl.Items.Add(second);
        return tabControl;
    }

    [Fact]
    public void ThemedTabControl_SwitchingToSecondTabWithElementContentDoesNotThrow()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            var tabControl = CreateTabControl(out _, out var second);
            var window = new Window { Content = new DockPanel { Children = { tabControl } } };

            RunLayout(window);

            tabControl.SelectedIndex = 1;
            RunLayout(window);

            Assert.True(second.IsSelected);
            Assert.Same(second.Content, tabControl.SelectedContent);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ThemedTabControl_SwitchingTabsBackAndForthKeepsLogicalParentStable()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            var tabControl = CreateTabControl(out var first, out var second);
            var window = new Window { Content = new DockPanel { Children = { tabControl } } };

            RunLayout(window);

            for (int i = 0; i < 3; i++)
            {
                tabControl.SelectedIndex = 1;
                RunLayout(window);
                tabControl.SelectedIndex = 0;
                RunLayout(window);
            }

            Assert.True(first.IsSelected);
            Assert.Same(first, ((FrameworkElement)first.Content!).Parent);
            Assert.Same(second, ((FrameworkElement)second.Content!).Parent);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
