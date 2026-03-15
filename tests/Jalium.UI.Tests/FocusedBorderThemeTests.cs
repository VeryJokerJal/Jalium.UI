using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class FocusedBorderThemeTests
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
    public void TextInputControls_ShouldResolveFocusedBorderBrushFromTheme()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedBrushObj));
            var focusedBrush = Assert.IsAssignableFrom<Brush>(focusedBrushObj);
            Assert.True(app.Resources.TryGetValue("FocusStrokeColorOuterBrush", out var focusOuterObj));
            Assert.True(app.Resources.TryGetValue("FocusStrokeColorInnerBrush", out var focusInnerObj));
            Assert.IsAssignableFrom<Brush>(focusOuterObj);
            Assert.IsAssignableFrom<Brush>(focusInnerObj);

            Assert.Same(focusedBrush, InvokeFocusedBrushResolver(new AutoCompleteBox()));
            Assert.Same(focusedBrush, InvokeFocusedBrushResolver(new DatePicker()));
            Assert.Same(focusedBrush, InvokeFocusedBrushResolver(new NumberBox()));
            Assert.Same(focusedBrush, InvokeFocusedBrushResolver(new PasswordBox()));
            Assert.Same(focusedBrush, InvokeFocusedBrushResolver(new TextBox()));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokeFocusedBrushResolver(Control control)
    {
        var method = control.GetType().GetMethod("ResolveFocusedBorderBrush",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, null));
    }
}
