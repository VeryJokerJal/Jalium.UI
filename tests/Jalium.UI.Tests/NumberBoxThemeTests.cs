using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class NumberBoxThemeTests
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
    public void NumberBox_ImplicitThemeStyle_ShouldApplyWithoutLocalHeightOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var numberBox = new NumberBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(numberBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(NumberBox), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(numberBox.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.NotNull(numberBox.SelectionBrush);
            Assert.NotNull(numberBox.CaretBrush);
            Assert.Equal(32, numberBox.MinHeight);
            Assert.True(numberBox.RenderSize.Height >= 32);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NumberBox_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("TextSecondary", out var secondaryObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var caretBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var numberBox = new NumberBox();

            var placeholderMethod = typeof(NumberBox).GetMethod("ResolvePlaceholderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var secondaryMethod = typeof(NumberBox).GetMethod("ResolveSecondaryTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(NumberBox).GetMethod("ResolveFocusedBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var selectionMethod = typeof(TextBoxBase).GetMethod("ResolveSelectionBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var caretMethod = typeof(TextBoxBase).GetMethod("ResolveCaretBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(placeholderMethod);
            Assert.NotNull(secondaryMethod);
            Assert.NotNull(focusedMethod);
            Assert.NotNull(selectionMethod);
            Assert.NotNull(caretMethod);

            Assert.Same(placeholderObj, placeholderMethod!.Invoke(numberBox, null));
            Assert.Same(secondaryObj, secondaryMethod!.Invoke(numberBox, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(numberBox, null));
            Assert.Same(selectionBrush, selectionMethod!.Invoke(numberBox, null));
            Assert.Same(caretBrush, caretMethod!.Invoke(numberBox, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void NumberBox_SpinButtons_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);
            var secondaryText = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);

            var numberBox = new NumberBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(numberBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            var upSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_UpSpinButton"));
            var downSpinButton = Assert.IsType<RepeatButton>(numberBox.FindName("PART_DownSpinButton"));

            Assert.Same(controlBackground, upSpinButton.Background);
            Assert.Same(controlBackground, downSpinButton.Background);

            var upPath = Assert.IsType<ShapePath>(upSpinButton.Content);
            var downPath = Assert.IsType<ShapePath>(downSpinButton.Content);

            Assert.Same(secondaryText, upPath.Stroke);
            Assert.Same(secondaryText, downPath.Stroke);
            Assert.Same(Jalium.UI.Cursors.Arrow, upSpinButton.Cursor);
            Assert.Same(Jalium.UI.Cursors.Arrow, downSpinButton.Cursor);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
