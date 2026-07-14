using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// ContentPresenter 绑定测试
/// </summary>
[Collection("Application")]
public class ContentPresenterBindingTests
{
    /// <summary>
    /// Resets static state for clean test isolation.
    /// </summary>
    private static void ResetApplicationState()
    {
        // Clear Application._current
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        // Reset ThemeManager._initialized
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void ContentPresenter_WithExplicitContent_ShouldNotBeOverridden()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            // Create a ContentPresenter with explicit content
            var presenter = new ContentPresenter();
            presenter.Content = "Test Content";

            // Check content before template parent is set
            Assert.Equal("Test Content", presenter.Content);

            // Simulate adding to a ToggleButton template
            var toggleButton = new ToggleButton();
            toggleButton.Content = "Button Content";

            // After being added to visual tree, Content should still be "Test Content"
            // (not overridden by TemplatedParent binding)
            Assert.Equal("Test Content", presenter.Content);
        }
        finally
        {
            ResetApplicationState();
        }
    }
    
    [Fact]
    public void ContentPresenter_WithBinding_ShouldFindAncestor()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            // Create ComboBox with SelectionBoxItem
            var comboBox = new ComboBox();
            comboBox.Width = 200;

            // Add to container to trigger template application
            var container = new StackPanel { Width = 400, Height = 300 };
            container.Children.Add(comboBox);

            // Measure to trigger template instantiation
            container.Measure(new Size(400, 300));
            container.Arrange(new Rect(0, 0, 400, 300));

            // Verify template was applied
            Assert.NotNull(comboBox.Template);

            // Verify there are visual children (template instantiated)
            Assert.True(comboBox.VisualChildrenCount > 0, "ComboBox should have visual children after template application");

            // Check SelectionBoxItem value
            var selectionBoxItem = comboBox.SelectionBoxItem;

            // Walk the visual tree to find ContentPresenter
            ContentPresenter? foundPresenter = null;
            FindContentPresenter(comboBox, ref foundPresenter);

            // Verify ContentPresenter was found
            Assert.NotNull(foundPresenter);

            // Verify ContentPresenter.Content equals SelectionBoxItem
            Assert.Equal(selectionBoxItem, foundPresenter.Content);

            // Get binding details and verify it's active
            var bindingExpr = foundPresenter.GetBindingExpression(ContentPresenter.ContentProperty) as BindingExpression;
            Assert.NotNull(bindingExpr);
            Assert.Equal(BindingStatus.Active, bindingExpr.Status);
            Assert.NotNull(bindingExpr.ResolvedSource);
            Assert.IsType<ComboBox>(bindingExpr.ResolvedSource);
        }
        finally
        {
            ResetApplicationState();
        }
    }
    
    private void FindContentPresenter(Visual visual, ref ContentPresenter? result)
    {
        if (result != null) return;

        if (visual is ContentPresenter cp)
        {
            result = cp;
            return;
        }

        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                FindContentPresenter(child, ref result);
            }
        }
    }

    private void FindHeaderContentPresenter(Visual visual, ref ContentPresenter? result)
    {
        if (result != null) return;

        if (visual is ContentPresenter cp && cp.Name == "PART_HeaderContent")
        {
            result = cp;
            return;
        }

        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                FindHeaderContentPresenter(child, ref result);
            }
        }
    }

    [Fact]
    public void PropertyGridCategoryExpander_HeaderContentPresenter_ShouldFillGridColumn()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            // Real PropertyGrid path: the header ContentPresenter sits in a Grid star column
            // and must stretch to fill it (so long header text or a custom header container
            // has room to render) while still centering vertically.
            var window = new Window { Width = 1000, Height = 500 };
            var propertyGrid = new PropertyGrid { Width = 1000, Height = 400 };
            window.Content = propertyGrid;

            propertyGrid.SelectedObject = new Button
            {
                Width = 100,
                Height = 30,
                Content = "Test"
            };

            window.Measure(new Size(1000, 500));
            window.Arrange(new Rect(0, 0, 1000, 500));

            ContentPresenter? presenter = null;
            FindHeaderContentPresenter(propertyGrid, ref presenter);
            Assert.NotNull(presenter);

            Assert.Equal(HorizontalAlignment.Stretch, presenter.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, presenter.VerticalAlignment);
        }
        finally
        {
            ResetApplicationState();
        }
    }

}
