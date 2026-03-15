using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ToolTipAndTreeThemeTests
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
    public void ToolTip_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var toolTip = new ToolTip { Content = "Hint" };
            var host = new StackPanel { Width = 240, Height = 80 };
            host.Children.Add(toolTip);

            host.Measure(new Size(240, 80));
            host.Arrange(new Rect(0, 0, 240, 80));

            Assert.False(toolTip.HasLocalValue(Control.BackgroundProperty));
            Assert.False(toolTip.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(toolTip.HasLocalValue(Control.PaddingProperty));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TreeViewItem_ImplicitThemeStyle_ShouldApplyWithoutLocalPaddingOverride()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var item = new TreeViewItem { Header = "Node" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(TreeViewItem)]);
            item.ApplyTemplate();

            Assert.False(item.HasLocalValue(Control.PaddingProperty));
            Assert.Equal(4, item.Padding.Left);
            Assert.Equal(2, item.Padding.Top);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ListBoxItem_LocalHoverAndSelectionState_ShouldUseThemeBrushes()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var item = new ListBoxItem { Content = "Node" };
            item.Style = Assert.IsType<Style>(app.Resources[typeof(ListBoxItem)]);
            item.ApplyTemplate();

            var backgroundBorder = GetPrivateField<Border>(item, "_backgroundBorder");
            var hoverBrush = Assert.IsAssignableFrom<Brush>(app.Resources["HighlightBackground"]);
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);

            Assert.NotNull(backgroundBorder);

            item.RaiseEvent(new RoutedEventArgs(UIElement.MouseEnterEvent, item));
            Assert.Same(hoverBrush, backgroundBorder.Background);

            item.IsSelected = true;
            Assert.Same(selectionBrush, backgroundBorder.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(instance) as T;
    }
}
