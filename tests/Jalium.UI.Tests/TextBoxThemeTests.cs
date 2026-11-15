using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("Application")]
public class TextBoxThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void TextBox_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var textBox = new TextBox();
            var host = new StackPanel
            {
                Width = 320,
                Height = 80
            };
            host.Children.Add(textBox);
            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));
            Assert.True(app.Resources.TryGetValue(typeof(TextBox), out var styleObj));
            Assert.IsType<Style>(styleObj);
            Assert.False(textBox.HasLocalValue(Control.BackgroundProperty));
            Assert.False(textBox.HasLocalValue(Control.ForegroundProperty));
            Assert.False(textBox.HasLocalValue(Control.BorderBrushProperty));
            Assert.NotNull(textBox.Background);
            Assert.NotNull(textBox.Foreground);
            Assert.NotNull(textBox.BorderBrush);
            Assert.NotNull(textBox.SelectionBrush);
            Assert.NotNull(textBox.CaretBrush);
            Assert.Equal(38, textBox.MinHeight);
            Assert.True(textBox.RenderSize.Height >= textBox.MinHeight);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TextBox_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var caretBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var textBox = new TextBox();
            var placeholderMethod = typeof(TextBox).GetMethod("ResolvePlaceholderBrush", BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(TextBox).GetMethod("ResolveFocusedBorderBrush", BindingFlags.Instance | BindingFlags.NonPublic);
            var selectionMethod = typeof(TextBoxBase).GetMethod("ResolveSelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic);
            var caretMethod = typeof(TextBoxBase).GetMethod("ResolveCaretBrush", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(placeholderMethod);
            Assert.NotNull(focusedMethod);
            Assert.NotNull(selectionMethod);
            Assert.NotNull(caretMethod);
            Assert.Same(placeholderObj, placeholderMethod!.Invoke(textBox, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(textBox, null));
            Assert.Same(selectionBrush, selectionMethod!.Invoke(textBox, null));
            Assert.Same(caretBrush, caretMethod!.Invoke(textBox, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TextBoxBase_EditingMenuUsesThePublicThemeTemplate()
    {
        ResetApplicationState();
        var app = new Application();
        var nativeMenu = Jalium.UI.Controls.Platform.PlatformTextContextMenus.Show;
        ContextMenu? menu = null;
        try
        {
            Jalium.UI.Controls.Platform.PlatformTextContextMenus.Show = null;
            var textBox = new TextBox
            {
                Text = "Theme"
            };
            typeof(TextBoxBase).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(textBox, new object[] { true });
            menu = Assert.IsType<ContextMenu>(typeof(TextBoxBase).GetField("_editingContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(textBox));
            menu.ApplyTemplate();
            var frame = Assert.IsType<Border>(menu.FindName("PART_PopupBorder"));
            Assert.Same(app.Resources["MenuFlyoutPresenterBackground"], frame.Background);
            Assert.Same(app.Resources["MenuFlyoutPresenterBorderBrush"], frame.BorderBrush);
            Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.IsType<string>(item.Header));
            Assert.False(menu.HasLocalValue(Control.BackgroundProperty));
        }
        finally
        {
            if (menu != null)
                menu.IsOpen = false;
            Jalium.UI.Controls.Platform.PlatformTextContextMenus.Show = nativeMenu;
            ResetApplicationState();
        }
    }
}
