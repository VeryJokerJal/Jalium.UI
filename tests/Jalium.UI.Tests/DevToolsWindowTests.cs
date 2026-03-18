using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Themes;

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
    public void DevToolsWindow_ShouldBuildVisualTreeIncrementally()
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
                var treeView = GetPrivateField<TreeView>(devTools, "_visualTreeView");
                var rootItem = Assert.IsAssignableFrom<TreeViewItem>(Assert.Single(treeView.Items));

                Assert.Empty(rootItem.Items);

                InvokePrivate(devTools, "OnTreeBuildTimerTick", null, EventArgs.Empty);

                Assert.NotEmpty(rootItem.Items);
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
    public void DevToolsWindow_ShouldChunkLargeVisualNodesAcrossTicks()
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
                var treeView = GetPrivateField<TreeView>(devTools, "_visualTreeView");
                var rootItem = Assert.IsAssignableFrom<TreeViewItem>(Assert.Single(treeView.Items));

                InvokePrivate(devTools, "OnTreeBuildTimerTick", null, EventArgs.Empty);
                InvokePrivate(devTools, "OnTreeBuildTimerTick", null, EventArgs.Empty);

                var panelItem = rootItem.Items
                    .OfType<TreeViewItem>()
                    .FirstOrDefault(item => item.Header?.ToString()?.Contains("StackPanel", StringComparison.Ordinal) == true);

                Assert.NotNull(panelItem);
                Assert.True(panelItem!.Items.Count > 0);
                Assert.True(panelItem.Items.Count < largePanel.Children.Count);

                var pendingTreeBuild = GetPrivateFieldObject(devTools, "_pendingTreeBuild");
                var countProperty = pendingTreeBuild.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(countProperty);
                var pendingCount = Assert.IsType<int>(countProperty!.GetValue(pendingTreeBuild));
                Assert.True(pendingCount > 0);
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

    private static void InvokePrivate(object instance, string methodName, params object?[]? args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, args);
    }
}
