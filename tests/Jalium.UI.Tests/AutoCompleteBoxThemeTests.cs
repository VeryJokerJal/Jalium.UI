using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class AutoCompleteBoxThemeTests
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
    public void AutoCompleteBox_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var autoCompleteBox = new AutoCompleteBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(autoCompleteBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(AutoCompleteBox), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(autoCompleteBox.HasLocalValue(Control.BackgroundProperty));
            Assert.False(autoCompleteBox.HasLocalValue(Control.ForegroundProperty));
            Assert.False(autoCompleteBox.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(autoCompleteBox.HasLocalValue(FrameworkElement.HeightProperty));

            Assert.NotNull(autoCompleteBox.Background);
            Assert.NotNull(autoCompleteBox.Foreground);
            Assert.NotNull(autoCompleteBox.BorderBrush);
            Assert.NotNull(autoCompleteBox.SelectionBrush);
            Assert.NotNull(autoCompleteBox.CaretBrush);
            Assert.Equal(38, autoCompleteBox.MinHeight);
            Assert.True(autoCompleteBox.RenderSize.Height >= 38);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void AutoCompleteBox_TextEngineAndSuggestionTemplate_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var caretBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var autoCompleteBox = new AutoCompleteBox { ItemsSource = new[] { "apple", "apricot" }, Text = "ap" };
            autoCompleteBox.Measure(new Size(320, 80));
            autoCompleteBox.Arrange(new Rect(0, 0, 320, 80));
            var list = Assert.IsType<ListBox>(autoCompleteBox.FindName("PART_SuggestionsList"));
            list.Measure(new Size(300, 200));
            list.Arrange(new Rect(0, 0, 300, 200));
            var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            var surface = Assert.IsType<Border>(row.FindName("SuggestionSurface"));
            Assert.Same(selectionBrush, surface.Background);
            Assert.False(row.Focusable);

            var placeholderMethod = typeof(TextBox).GetMethod("ResolvePlaceholderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(TextBox).GetMethod("ResolveFocusedBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var selectionMethod = typeof(TextBoxBase).GetMethod("ResolveSelectionBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var caretMethod = typeof(TextBoxBase).GetMethod("ResolveCaretBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(placeholderMethod);
            Assert.NotNull(focusedMethod);
            Assert.NotNull(selectionMethod);
            Assert.NotNull(caretMethod);

            Assert.Same(placeholderObj, placeholderMethod!.Invoke(autoCompleteBox, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(autoCompleteBox, null));
            Assert.Same(selectionBrush, selectionMethod!.Invoke(autoCompleteBox, null));
            Assert.Same(caretBrush, caretMethod!.Invoke(autoCompleteBox, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
