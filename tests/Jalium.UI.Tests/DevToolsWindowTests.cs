using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DevToolsWindowTests
{
    [Fact]
    public void BgraPixelSampling_UsesPhysicalDpiCoordinatesAndChannelOrder()
    {
        byte[] pixels =
        [
            // 2x2, top-down BGRA
            1, 2, 3, 4,       5, 6, 7, 8,
            9, 10, 11, 12,    13, 14, 15, 16,
        ];

        Assert.True(DevToolsWindow.TrySampleBgraPixel(
            pixels, 2, 2, dpiScale: 2, new Point(0.75, 0.75), out Color color));
        Assert.Equal(Color.FromArgb(16, 15, 14, 13), color);
    }

    [Theory]
    [InlineData(-0.1, 0, 1, 16)]
    [InlineData(0, -0.1, 1, 16)]
    [InlineData(2, 0, 1, 16)]
    [InlineData(0, 2, 1, 16)]
    [InlineData(0, 0, 0, 16)]
    [InlineData(0, 0, 1, 15)]
    public void BgraPixelSampling_RejectsInvalidCoordinatesScaleOrBuffer(
        double x,
        double y,
        double scale,
        int byteCount)
    {
        Assert.False(DevToolsWindow.TrySampleBgraPixel(
            new byte[byteCount], 2, 2, scale, new Point(x, y), out _));
    }

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
    public void UiaTreeKeepsReadableDevToolsInkInALightApplication()
    {
        ResetApplicationState();
        _ = new Application();
        ThemeManager.ApplyTheme(ThemeVariant.Light);
        var host = new Window { Title = "Host", Content = new Button { Content = "Target action" } };
        host.Measure(new Size(420, 400)); host.Arrange(new Rect(0, 0, 420, 400));
        var devTools = new DevToolsWindow(host);
        try
        {
            var workspace = Assert.IsType<DevToolsWorkspace>(devTools.Content);
            workspace.SelectedPage = DevToolsPage.Automation;
            var view = Assert.IsType<DevToolsAutomationView>(workspace.AutomationContent);
            for (int pass = 0; view.IsBuilding && pass < 1000; pass++) InvokePrivate(devTools, "OnUiaBuildTimerTick", null, EventArgs.Empty);
            Assert.False(view.IsBuilding);
            view.Measure(new Size(420, 400)); view.Arrange(new Rect(0, 0, 420, 400)); view.UpdateLayout();
            var tree = Assert.IsType<TreeView>(view.FindName("PART_PeerTree"));
            var root = Assert.IsAssignableFrom<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(Color.FromRgb(0xDD, 0xE2, 0xE9), Assert.IsType<SolidColorBrush>(root.Foreground).Color);
            Assert.NotEmpty(root.Items);
            Assert.All(root.Items.OfType<TreeViewItem>(), child =>
                Assert.Equal(Color.FromRgb(0xDD, 0xE2, 0xE9), Assert.IsType<SolidColorBrush>(child.Foreground).Color));
        }
        finally { devTools.CloseDevTools(); ResetApplicationState(); }
    }

    [Fact]
    public void ReplUsesNativeTextEditorAndReadOnlyTerminalThroughItsDefaultTemplate()
    {
        ResetApplicationState();
        _ = new Application();
        var devTools = new DevToolsWindow(new Window { Title = "Host" });
        try
        {
            var workspace = Assert.IsType<DevToolsWorkspace>(devTools.Content);
            workspace.SelectedPage = DevToolsPage.Repl;
            devTools.Measure(new Size(1040, 860)); devTools.Arrange(new Rect(0, 0, 1040, 860)); devTools.UpdateLayout();
            var view = Assert.IsType<DevToolsReplView>(workspace.ReplContent);
            var terminal = Assert.IsType<Terminal>(view.FindName("PART_Terminal"));
            var editor = Assert.IsType<TextBox>(view.FindName("PART_InputTextBox"));
            Assert.True(terminal.Focusable);
            Assert.True(terminal.IsReadOnly);
            Assert.False(terminal.AutoStartShell);
            Assert.False(editor.IsReadOnly);
            var input = new TextCompositionEventArgs(UIElement.TextInputEvent, "1", 0);
            editor.RaiseEvent(input);
            Assert.True(input.Handled);
            Assert.Equal("1", view.InputText);
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, new ReplKeySource { RootVisual = editor }, 1, Key.Enter)
                { RoutedEvent = UIElement.PreviewKeyDownEvent, Source = editor };
            editor.RaiseEvent(enter);
            Assert.True(enter.Handled);
            var text = Assert.IsAssignableFrom<ITextProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Text));
            Assert.Contains("=> 1", text.DocumentRange.GetText(-1));
            Assert.Equal(new[] { "1" }, view.History);
        }
        finally { Keyboard.ClearFocus(); devTools.CloseDevTools(); ResetApplicationState(); }
    }

    private sealed class ReplKeySource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }

    [Fact]
    public void ScreenshotCrop_UsesPhysicalDpiAndClipsToBackBuffer()
    {
        Assert.True(DevToolsWindow.TryCalculateScreenshotCrop(
            new Rect(-2.25, 3.25, 12.5, 8.5), 2.0, 18, 20, out Int32Rect crop));

        Assert.Equal(new Int32Rect(0, 6, 18, 14), crop);
    }

    [Fact]
    public void ScreenshotCrop_RejectsOffscreenAndInvalidBounds()
    {
        Assert.False(DevToolsWindow.TryCalculateScreenshotCrop(
            new Rect(100, 100, 10, 10), 1.0, 80, 60, out _));
        Assert.False(DevToolsWindow.TryCalculateScreenshotCrop(
            new Rect(0, 0, 10, 10), 0.0, 80, 60, out _));
    }

    [Fact]
    public void InspectorAutomationNamesAndExpansionFollowTheFlattenedModels()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var host = new Window { Title = "Inspector host", Content = new StackPanel { Children = { new Button { Content = "Child" } } } };
            var devTools = new DevToolsWindow(host);
            try
            {
                var list = Assert.IsAssignableFrom<ListBox>(GetPrivateFieldOrNull(devTools, "_visualTreeView"));
                var peer = UIElementAutomationPeer.CreatePeerForElement(list)!;
                Assert.Equal(AutomationControlType.Tree, peer.GetAutomationControlType());
                var root = peer.GetChildren()[0];
                Assert.Contains("Inspector host", root.GetName());
                Assert.Equal(AutomationControlType.TreeItem, root.GetAutomationControlType());
                var expansion = Assert.IsAssignableFrom<IExpandCollapseProvider>(root.GetPattern(PatternInterface.ExpandCollapse));
                Assert.Equal(ExpandCollapseState.Expanded, expansion.ExpandCollapseState);
                expansion.Collapse();
                Assert.Single(GetRows(devTools));
                var replacementRoot = peer.GetChildren()[0];
                var replacementExpansion = Assert.IsAssignableFrom<IExpandCollapseProvider>(replacementRoot.GetPattern(PatternInterface.ExpandCollapse));
                replacementExpansion.Expand();
                Assert.True(GetRows(devTools).Count > 1);
                Assert.Throws<InvalidOperationException>(expansion.Expand);
            }
            finally { devTools.CloseDevTools(); }
        }
        finally { ResetApplicationState(); }
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
                var inspector = Assert.IsType<DevToolsInspector>(GetPrivateFieldOrNull(devTools, "_inspector"));
                Assert.Equal(DevToolsInspectorView.Visual, inspector.SelectedView);
                Assert.False(inspector.CanDelete);
                Assert.False(inspector.CanUndo);
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
    public void DeleteElement_UndoDelete_RestoresPanelChildAtOriginalIndex()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var first = new Button { Content = "First" };
            var deleted = new Button { Content = "Deleted" };
            var last = new Button { Content = "Last" };
            var panel = new StackPanel { Children = { first, deleted, last } };
            var host = new Window { Title = "Host", Content = panel };
            var devTools = new DevToolsWindow(host);
            try
            {
                InvokePrivate(devTools, "DeleteElement", deleted);
                Assert.Equal([first, last], panel.Children.Cast<UIElement>().ToArray());

                InvokePrivate(devTools, "UndoDelete");

                Assert.Equal([first, deleted, last], panel.Children.Cast<UIElement>().ToArray());
                Assert.Same(panel, deleted.VisualParent);
                Assert.Null(GetPrivateFieldOrNull(devTools, "_deleteRecord"));
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

                var propertiesPanel = GetPrivateField<DevToolsDetailsList>(devTools, "_propertiesPanel");
                var headerTexts = EnumerateVisuals(propertiesPanel)
                    .OfType<TextBlock>()
                    .Select(textBlock => NormalizeHeaderText(textBlock.Text))
                    .ToList();

                Assert.Contains(headerTexts, text => text.Contains("PROPERTIESBYCATEGORY", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("FRAMEWORK", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("LAYOUT", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("APPEARANCE", StringComparison.Ordinal));
                Assert.Contains(headerTexts, text => text.Contains("OTHER", StringComparison.Ordinal));
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
    public void ClosingViaWindowStopsOverlayHighlightAnimation()
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

            // Use the same accepted close lifecycle as the window close button,
            // without going through the DevTools command helper.
            devTools.Close();

            Assert.False(
                RenderingHasSubscriberTarget(animationTimer),
                "An accepted close must unsubscribe the overlay animation timer from CompositionTarget.Rendering.");
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

    private static IEnumerable<Visual> EnumerateVisuals(Visual root)
    {
        yield return root;

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is not Visual child)
            {
                continue;
            }

            foreach (var descendant in EnumerateVisuals(child))
            {
                yield return descendant;
            }
        }
    }

    private static string NormalizeHeaderText(string? text)
    {
        return string.Concat((text ?? string.Empty)
            .Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
    }

    /// <summary>
    /// The Perf tab used to turn on process-wide draw-API instrumentation and a 500 ms
    /// rebuild timer, and undo neither when the user moved to another tab or closed the
    /// window. The result: every window in the process kept paying per-draw-call timing
    /// plus a per-frame native GPU-timing readback for the rest of the session, and the
    /// Perf timer kept rebuilding both panels and re-uploading the graph bitmap twice a
    /// second — forcing a full DevTools repaint while the user was on the Inspector tab.
    /// </summary>
    [Fact]
    public void PerfTab_LeavingOrClosing_StopsInstrumentationAndItsRefreshTimer()
    {
        ResetApplicationState();
        _ = new Application();

        bool previousEnabled = RenderDiagnostics.ApiStatsEnabled;
        object? previousOwner = RenderDiagnostics.StatsOwner;
        try
        {
            var host = new Window { Title = "Host", Content = new Button { Content = "One" } };
            var devTools = new DevToolsWindow(host);
            try
            {
                var workspace = Assert.IsType<DevToolsWorkspace>(devTools.Content);

                workspace.SelectedPage = DevToolsPage.Performance;
                Assert.True(RenderDiagnostics.ApiStatsEnabled);
                Assert.Same(host, RenderDiagnostics.StatsOwner);
                var timer = (DispatcherTimer?)GetPrivateFieldOrNull(devTools, "_perfRefreshTimer");
                Assert.NotNull(timer);
                Assert.True(timer!.IsEnabled);

                workspace.SelectedPage = DevToolsPage.Inspector;
                Assert.False(RenderDiagnostics.ApiStatsEnabled);
                Assert.Null(RenderDiagnostics.StatsOwner);
                Assert.False(timer.IsEnabled);

                // Re-entering must arm it again — stopping on leave must not be one-way.
                workspace.SelectedPage = DevToolsPage.Performance;
                Assert.True(RenderDiagnostics.ApiStatsEnabled);
                Assert.True(timer.IsEnabled);

                devTools.Close();
                Assert.False(RenderDiagnostics.ApiStatsEnabled);
                Assert.Null(RenderDiagnostics.StatsOwner);
                Assert.False(timer.IsEnabled);
            }
            finally
            {
                devTools.Close();
            }
        }
        finally
        {
            RenderDiagnostics.ApiStatsEnabled = previousEnabled;
            RenderDiagnostics.StatsOwner = previousOwner;
        }
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

        return handlers.GetInvocationList().Any(d =>
        {
            if (ReferenceEquals(d.Target, target))
                return true;

            if (d.Target?.GetType().DeclaringType != typeof(DispatcherTimer))
                return false;

            // Frame-rate timers bind the handler to their active generation so
            // a callback already snapshotted by Rendering cannot tick a restart.
            var owner = d.Target.GetType().GetProperty("Owner",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return ReferenceEquals(owner?.GetValue(d.Target), target);
        });
    }
}
