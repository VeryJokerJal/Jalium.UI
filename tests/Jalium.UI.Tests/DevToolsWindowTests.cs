using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DevToolsWindowTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void DevToolsWindow_ShouldBuildFlatVisibleRowListSynchronously()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var host = new Window
            {
                Title = "Host",
                Content = new StackPanel
                {
                    Children =
                    {
                        new Button { Content = "One" },
                        new Border
                        {
                            Child = new TextBlock { Text = "Two" }
                        }
                    }
                }
            };

            var devTools = new DevToolsWindow(host);
            try
            {
                // The inspector now builds a flat list of visible rows synchronously in the
                // ctor (no incremental TreeView build). The first row is the host window and
                // is expanded, so its children are emitted; deeper collapsed nodes are not.
                var rows = GetRows(devTools);
                Assert.NotEmpty(rows);
                Assert.Same(host, RowVisual(rows[0]!));
                Assert.True(RowExpanded(rows[0]!));
                Assert.True(rows.Count > 1, "the expanded root's children should be emitted");
            }
            finally
            {
                devTools.CloseDevTools();
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DevToolsWindow_FlatRowList_IsLazyAndExpandable()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var largePanel = new StackPanel();
            for (int i = 0; i < 160; i++)
            {
                largePanel.Children.Add(new Border
                {
                    Child = new TextBlock { Text = $"Item {i}" }
                });
            }

            var host = new Window
            {
                Title = "Host",
                Content = largePanel
            };

            var devTools = new DevToolsWindow(host);
            try
            {
                int totalVisuals = CountVisuals(host);
                Assert.True(totalVisuals > 300, $"expected a large visual tree, got {totalVisuals}");

                // Only visible rows are materialized — collapsed descendants are NOT emitted,
                // so the row count is bounded by what is expanded (root + its children), not by
                // the total visual count. This is the data-model half of virtualization.
                var rows = GetRows(devTools);
                Assert.True(rows.Count < 50, $"expected a small visible-row set, got {rows.Count}");

                // Expanding a collapsed row with children splices its children into the list…
                object? collapsible = null;
                foreach (var row in rows)
                {
                    if (RowHasChildren(row!) && !RowExpanded(row!)) { collapsible = row; break; }
                }
                Assert.NotNull(collapsible);

                int before = rows.Count;
                InvokePrivate(devTools, "ToggleExpand", collapsible);
                Assert.True(rows.Count > before, "expanding a node should add its children to the visible-row list");

                // …and collapsing it again removes them.
                InvokePrivate(devTools, "ToggleExpand", collapsible);
                Assert.Equal(before, rows.Count);
            }
            finally
            {
                devTools.CloseDevTools();
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DevToolsWindow_ShouldGroupDependencyPropertiesByCategory()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var button = new Button
            {
                Name = "CategorizedButton",
                Content = "Inspect me"
            };

            var host = new Window
            {
                Title = "Host",
                Content = button
            };

            var devTools = new DevToolsWindow(host);
            try
            {
                InvokePrivate(devTools, "UpdatePropertiesPanel", button);

                var propertiesPanel = GetPrivateField<StackPanel>(devTools, "_propertiesPanel");
                var headerTexts = propertiesPanel.Children
                    .OfType<TextBlock>()
                    .Select(textBlock => textBlock.Text ?? string.Empty)
                    .ToList();

                Assert.Contains(headerTexts, text => text.Contains("Properties by Category", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("Framework", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("Layout", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("Appearance", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("Other", StringComparison.Ordinal));
            }
            finally
            {
                devTools.CloseDevTools();
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CloseDevTools_ShouldStopOverlayHighlightAnimation()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var button = new Button { Content = "Inspect me" };
            var host = new Window
            {
                Title = "Host",
                Content = button
            };

            var devTools = new DevToolsWindow(host);

            // The overlay is created by the DevToolsWindow ctor and published on the
            // target window. Capture it now — CloseDevTools nulls the field.
            var overlay = host.DevToolsOverlay;
            Assert.NotNull(overlay);

            // Highlighting starts the per-frame highlight animation. Its 1ms
            // DispatcherTimer piggybacks on the static CompositionTarget.Rendering
            // event — the root that otherwise keeps an orphaned overlay (and the
            // target window + element subtree) alive and repainting every frame.
            overlay!.HighlightElement(button);

            var animationTimer = GetPrivateField<DispatcherTimer>(overlay, "_animationTimer");
            Assert.True(animationTimer.IsEnabled);
            Assert.True(
                RenderingHasSubscriberTarget(animationTimer),
                "Highlight animation timer should be subscribed to CompositionTarget.Rendering while highlighting.");

            // Act: close DevTools via the public API. Before the fix this nulled the
            // overlay references without stopping the animation, leaking the timer.
            devTools.CloseDevTools();

            // The animation must be fully torn down: timer unsubscribed from the
            // static frame event (GC root released, repaints stop) and overlay
            // state cleared.
            Assert.False(
                RenderingHasSubscriberTarget(animationTimer),
                "CloseDevTools must unsubscribe the overlay animation timer from CompositionTarget.Rendering.");
            Assert.False(animationTimer.IsEnabled);
            Assert.Null(GetPrivateFieldOrNull(overlay, "_animationTimer"));
            Assert.Null(GetPrivateFieldOrNull(overlay, "_highlightedElement"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void OnDevToolsClosing_ShouldStopOverlayHighlightAnimation()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var button = new Button { Content = "Inspect me" };
            var host = new Window
            {
                Title = "Host",
                Content = button
            };

            var devTools = new DevToolsWindow(host);
            var overlay = host.DevToolsOverlay;
            Assert.NotNull(overlay);

            overlay!.HighlightElement(button);
            var animationTimer = GetPrivateField<DispatcherTimer>(overlay, "_animationTimer");
            Assert.True(RenderingHasSubscriberTarget(animationTimer));

            // Act: drive the Closing handler directly. This is the close-via-OS
            // (window X button) route, which fires Closing without ever going
            // through CloseDevTools, so it must independently stop the animation.
            InvokePrivate(devTools, "OnDevToolsClosing", null, EventArgs.Empty);

            Assert.False(
                RenderingHasSubscriberTarget(animationTimer),
                "OnDevToolsClosing must unsubscribe the overlay animation timer from CompositionTarget.Rendering.");
            Assert.False(animationTimer.IsEnabled);
            Assert.Null(GetPrivateFieldOrNull(overlay, "_animationTimer"));
            Assert.Null(GetPrivateFieldOrNull(overlay, "_highlightedElement"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static object GetPrivateFieldObject(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return value!;
    }

    // ── Flat-list inspector helpers (InspectorRow is internal, so reflect on it) ──
    private static System.Collections.IList GetRows(object devTools)
    {
        var field = devTools.GetType().GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (System.Collections.IList)field!.GetValue(devTools)!;
    }

    private static Visual RowVisual(object row)
        => (Visual)row.GetType().GetProperty("Visual")!.GetValue(row)!;

    private static bool RowExpanded(object row)
        => (bool)row.GetType().GetProperty("IsExpanded")!.GetValue(row)!;

    private static bool RowHasChildren(object row)
        => (bool)row.GetType().GetProperty("HasChildren")!.GetValue(row)!;

    private static int CountVisuals(Visual visual)
    {
        int count = 1;
        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null) count += CountVisuals(child);
        }
        return count;
    }

    private static object? GetPrivateFieldOrNull(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(instance);
    }

    private static void InvokePrivate(object instance, string methodName, params object?[]? args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, args);
    }

    /// <summary>
    /// Returns true if any handler currently subscribed to the static
    /// <see cref="CompositionTarget.Rendering"/> event is bound to
    /// <paramref name="target"/>. Used to assert that the overlay's animation
    /// <see cref="DispatcherTimer"/> is (or, after teardown, is no longer) rooted
    /// by the centralized frame event.
    /// </summary>
    private static bool RenderingHasSubscriberTarget(object target)
    {
        var field = typeof(CompositionTarget).GetField("Rendering",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        if (field!.GetValue(null) is not Delegate handlers)
        {
            return false;
        }

        return handlers.GetInvocationList().Any(d => ReferenceEquals(d.Target, target));
    }
}
