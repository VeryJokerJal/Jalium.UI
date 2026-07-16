using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for the template-lifecycle gap adjacent to the TabControl zombie-panel
/// crash (2026-07-15): Control.ClearTemplateContent discards the whole old template tree when
/// the Template property changes (runtime replacement, theme switch) or is cleared, but the
/// items-host panel living inside that tree was never retired — IsItemsHost, the shared
/// ItemContainerGenerator reference and ItemsOwner all survived on the detached panel, and
/// ItemsControl._itemsPresenter kept resolving ItemsHost to the dead panel.
///
/// Symptoms locked here:
///  - replacing Template leaves an armed zombie panel whose replayed measure steals containers
///    from the live panel (same mechanism as the VSPSample tab-switch crash);
///  - clearing Template leaves ItemsHost pointing at the detached panel, so the fallback host
///    is never built and items realize into an invisible tree (blank list, zero desired size).
/// </summary>
public sealed class ItemsControlTemplateLifecycleRegressionTests
{
    private static readonly Size ViewportSize = new(800, 600);
    private static readonly Rect ViewportRect = new(0, 0, 800, 600);

    private static void RunLayout(UIElement element)
    {
        element.Measure(ViewportSize);
        element.Arrange(ViewportRect);
    }

    private static ControlTemplate CreatePresenterTemplate()
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => new ItemsPresenter());
        return template;
    }

    private static ListBox CreateListBox(int itemCount = 3)
    {
        var listBox = new ListBox();
        for (int i = 0; i < itemCount; i++)
        {
            listBox.Items.Add($"item {i}");
        }

        return listBox;
    }

    [Fact]
    public void ReplacingTemplateAtRuntimeRetiresOldPanelAndRealizesIntoNewPanel()
    {
        var listBox = CreateListBox();

        listBox.Template = CreatePresenterTemplate();
        RunLayout(listBox);

        var oldPanel = listBox.ItemsHostInternal;
        Assert.NotNull(oldPanel);
        Assert.Equal(3, oldPanel!.Children.Count);

        // Swap in a new template at runtime — the exact shape of a theme switch pushing a new
        // ControlTemplate through the Template dependency property.
        listBox.Template = CreatePresenterTemplate();
        RunLayout(listBox);

        var newPanel = listBox.ItemsHostInternal;
        Assert.NotNull(newPanel);
        Assert.NotSame(oldPanel, newPanel);
        Assert.Equal(3, newPanel!.Children.Count);

        // The discarded panel must be fully decommissioned, not just detached.
        Assert.False(oldPanel.IsItemsHost);
        Assert.Equal(0, oldPanel.Children.Count);

        // Zombie replay: a stale queued measure on the discarded panel must be a harmless
        // no-op — it must neither throw nor pull containers out of the live panel through a
        // still-attached generator.
        var exception = Record.Exception(() =>
        {
            oldPanel.Measure(ViewportSize);
            oldPanel.Arrange(ViewportRect);
        });

        Assert.Null(exception);
        Assert.Equal(0, oldPanel.Children.Count);
        Assert.Equal(3, newPanel.Children.Count);
        for (int i = 0; i < newPanel.Children.Count; i++)
        {
            Assert.Same(newPanel, ((FrameworkElement)newPanel.Children[i]).Parent);
        }
    }

    [Fact]
    public void ClearingTemplateAtRuntimeFallsBackToDirectItemsHost()
    {
        var listBox = CreateListBox();

        listBox.Template = CreatePresenterTemplate();
        RunLayout(listBox);

        var templatePanel = listBox.ItemsHostInternal;
        Assert.NotNull(templatePanel);
        Assert.Equal(3, templatePanel!.Children.Count);

        listBox.Template = null;
        RunLayout(listBox);

        // ItemsHost must not keep resolving to the panel inside the discarded template tree;
        // the templateless fallback host takes over and the items stay visible.
        var fallbackPanel = listBox.ItemsHostInternal;
        Assert.NotNull(fallbackPanel);
        Assert.NotSame(templatePanel, fallbackPanel);
        Assert.Equal(3, fallbackPanel!.Children.Count);
        Assert.True(listBox.DesiredSize.Height > 0);

        // The discarded template panel is disarmed and empty.
        Assert.False(templatePanel.IsItemsHost);
        Assert.Equal(0, templatePanel.Children.Count);
    }

    [Fact]
    public void ReapplyingTemplateAfterClearKeepsItemsAlive()
    {
        var listBox = CreateListBox();

        listBox.Template = CreatePresenterTemplate();
        RunLayout(listBox);
        listBox.Template = null;
        RunLayout(listBox);

        listBox.Template = CreatePresenterTemplate();
        RunLayout(listBox);

        var panel = listBox.ItemsHostInternal;
        Assert.NotNull(panel);
        Assert.Equal(3, panel!.Children.Count);
        for (int i = 0; i < panel.Children.Count; i++)
        {
            Assert.Same(panel, ((FrameworkElement)panel.Children[i]).Parent);
        }
    }
}

/// <summary>
/// Same template-replacement scenario against the real theme templates (implicit style supplies
/// the initial ControlTemplate, ItemsPresenter sits behind ScrollViewer/ScrollContentPresenter
/// layers). Verifies the retire logic finds the presenter through nested template boundaries.
/// </summary>
[Collection("Application")]
public sealed class ItemsControlTemplateLifecycleThemedRegressionTests
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

    private static ControlTemplate CreatePresenterTemplate()
    {
        var template = new ControlTemplate(typeof(ListBox));
        template.SetVisualTree(() => new ItemsPresenter());
        return template;
    }

    [Fact]
    public void ThemedListBox_ReplacingTemplateAtRuntimeKeepsItemsAndRetiresOldPanel()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            var listBox = new ListBox();
            for (int i = 0; i < 3; i++)
            {
                listBox.Items.Add($"item {i}");
            }

            var window = new Window { Content = new DockPanel { Children = { listBox } } };
            RunLayout(window);

            var themedPanel = listBox.ItemsHostInternal;
            Assert.NotNull(themedPanel);
            Assert.Equal(3, themedPanel!.Children.Count);

            // Replace the theme-supplied template at runtime (theme-switch shape). The old
            // presenter lives several nested-template layers below the discarded root.
            listBox.Template = CreatePresenterTemplate();
            RunLayout(window);

            var newPanel = listBox.ItemsHostInternal;
            Assert.NotNull(newPanel);
            Assert.NotSame(themedPanel, newPanel);
            Assert.Equal(3, newPanel!.Children.Count);
            for (int i = 0; i < newPanel.Children.Count; i++)
            {
                Assert.Same(newPanel, ((FrameworkElement)newPanel.Children[i]).Parent);
            }

            // Old themed panel is disarmed; its zombie measure cannot steal containers back.
            Assert.False(themedPanel.IsItemsHost);
            Assert.Equal(0, themedPanel.Children.Count);

            var exception = Record.Exception(() =>
            {
                themedPanel.Measure(ViewportSize);
                themedPanel.Arrange(ViewportRect);
            });

            Assert.Null(exception);
            Assert.Equal(0, themedPanel.Children.Count);
            Assert.Equal(3, newPanel.Children.Count);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
