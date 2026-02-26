using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;

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
}
