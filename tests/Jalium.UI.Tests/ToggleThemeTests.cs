using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Shapes;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Markup;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ToggleThemeTests
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
    public void CheckGlyphs_ShouldUseTextOnAccentThemeResource()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var accentText = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);

            var checkBox = new CheckBox { IsChecked = true };
            var radioButton = new RadioButton { IsChecked = true };
            checkBox.Style = Assert.IsType<Style>(app.Resources[typeof(CheckBox)]);
            radioButton.Style = Assert.IsType<Style>(app.Resources[typeof(RadioButton)]);
            var host = new StackPanel { Width = 320, Height = 120 };
            host.Children.Add(checkBox);
            host.Children.Add(radioButton);

            host.Measure(new Size(320, 120));
            host.Arrange(new Rect(0, 0, 320, 120));
            checkBox.ApplyTemplate();
            radioButton.ApplyTemplate();

            var checkMark = FindDescendant<ShapePath>(checkBox);
            var radioDot = FindNamedDescendant<Ellipse>(radioButton, "RadioDot");

            Assert.NotNull(checkMark);
            Assert.NotNull(radioDot);
            Assert.Same(accentText, checkMark.Fill);
            Assert.Same(accentText, radioDot.Fill);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CheckBox_IndeterminateState_ShouldShowDashGlyph()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var checkBox = new CheckBox
            {
                IsThreeState = true,
                IsChecked = null
            };
            checkBox.Style = Assert.IsType<Style>(app.Resources[typeof(CheckBox)]);
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(checkBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));
            checkBox.ApplyTemplate();

            var checkMark = FindDescendant<ShapePath>(checkBox);
            var indeterminateMark = FindNamedDescendant<Rectangle>(checkBox, "IndeterminateMark");
            var checkBoxBorder = FindNamedDescendant<Border>(checkBox, "CheckBoxBorder");
            var uncheckedBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleUncheckedBackground"]);
            var uncheckedBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleUncheckedBorder"]);
            var checkedBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleCheckedBackground"]);
            var checkedBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleCheckedBorder"]);

            Assert.NotNull(checkMark);
            Assert.NotNull(indeterminateMark);
            Assert.NotNull(checkBoxBorder);
            Assert.Equal(0.0, checkMark.Opacity);
            Assert.Equal(1.0, indeterminateMark.Opacity);
            Assert.Same(uncheckedBackground, checkBox.Background);
            Assert.Same(uncheckedBorder, checkBox.BorderBrush);
            Assert.Same(checkedBackground, checkBoxBorder!.Background);
            Assert.Same(checkedBorder, checkBoxBorder.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ToggleSwitch_VisualStates_ShouldBeOwnedByTemplateTriggers()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var uncheckedBorder = Assert.IsType<SolidColorBrush>(app.Resources["ToggleUncheckedBorder"]);
            var checkedBorder = Assert.IsType<LinearGradientBrush>(app.Resources["ToggleCheckedBorder"]);
            var checkedBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleCheckedBackground"]);
            var disabledBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleDisabledBackground"]);
            var disabledBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleDisabledBorder"]);

            var toggleSwitch = new ToggleSwitch();
            toggleSwitch.Style = Assert.IsType<Style>(app.Resources[typeof(ToggleSwitch)]);
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(toggleSwitch);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));
            toggleSwitch.ApplyTemplate();

            Assert.Equal(2, checkedBorder.GradientStops.Count);
            var track = Assert.IsType<Border>(toggleSwitch.FindName("PART_SwitchTrack"));
            Assert.IsType<Ellipse>(toggleSwitch.FindName("PART_SwitchThumb"));
            Assert.Same(toggleSwitch.OffBackground, track.Background);
            Assert.Same(uncheckedBorder, track.BorderBrush);

            toggleSwitch.IsOn = true;

            Assert.Same(checkedBackground, track.Background);
            Assert.Same(checkedBorder, track.BorderBrush);

            toggleSwitch.IsEnabled = false;

            Assert.Same(disabledBackground, track.Background);
            Assert.Same(disabledBorder, track.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ToggleButton_Default_ShouldRegisterImplicitStyleAndInstantiateTemplate()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            // The implicit style must exist — its absence is exactly the bug that made a
            // bare ToggleButton render invisible (no ControlTemplate at all).
            var style = Assert.IsType<Style>(app.Resources[typeof(ToggleButton)]);
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);

            var toggleButton = new ToggleButton { Content = "Toggle (off)" };
            toggleButton.Style = style;
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(toggleButton);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));
            toggleButton.ApplyTemplate();

            // The template actually instantiated a RootBorder + ContentPresenter, i.e. the
            // control now produces visible chrome instead of rendering empty.
            var rootBorder = FindNamedDescendant<Border>(toggleButton, "RootBorder");
            var contentPresenter = FindDescendant<ContentPresenter>(toggleButton);
            Assert.NotNull(rootBorder);
            Assert.NotNull(contentPresenter);

            // Unchecked uses the neutral control background (like a Button), flowed through
            // to the template root via TemplateBinding.
            Assert.Same(controlBackground, toggleButton.Background);
            Assert.Same(controlBackground, rootBorder!.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ToggleButton_Checked_ShouldUseAccentFillAndTextOnAccent()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var checkedBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleCheckedBackground"]);
            var checkedBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ToggleCheckedBorder"]);
            var accentText = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);
            var controlBackground = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);
            var controlBorder = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBorder"]);

            var toggleButton = new ToggleButton { Content = "Toggle (on)", IsChecked = true };
            toggleButton.Style = Assert.IsType<Style>(app.Resources[typeof(ToggleButton)]);
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(toggleButton);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));
            toggleButton.ApplyTemplate();

            var rootBorder = FindNamedDescendant<Border>(toggleButton, "RootBorder");
            Assert.NotNull(rootBorder);

            // The checked trigger paints the accent fill + accent border and switches the
            // foreground to the on-accent text brush (segmented-toggle look).
            Assert.Same(controlBackground, toggleButton.Background);
            Assert.Same(controlBorder, toggleButton.BorderBrush);
            Assert.Same(accentText, toggleButton.Foreground);
            Assert.Same(checkedBackground, rootBorder!.Background);
            Assert.Same(checkedBorder, rootBorder.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static T? FindDescendant<T>(Visual root) where T : class
    {
        if (root is T match)
        {
            return match;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                var result = FindDescendant<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static T? FindNamedDescendant<T>(Visual root, string name) where T : FrameworkElement
    {
        if (root is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
        {
            return match;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                var result = FindNamedDescendant<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }
}
