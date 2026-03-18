using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class AutoCompleteBoxTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void AutoCompleteBox_TextTrimming_DefaultsToCharacterEllipsis()
    {
        // Arrange & Act
        var autoComplete = new AutoCompleteBox();

        // Assert
        Assert.Equal(TextTrimming.CharacterEllipsis, autoComplete.TextTrimming);
    }

    [Fact]
    public void AutoCompleteBox_TabAccept_ShouldNotInsertTabCharacter()
    {
        // Arrange
        var autoComplete = new AutoCompleteBox
        {
            ItemsSource = new[] { "apple", "apricot", "banana" },
            MinimumPrefixLength = 1
        };

        autoComplete.Text = "ap";
        Assert.True(autoComplete.IsDropDownOpen);

        // Act: Tab accepts the current suggestion.
        autoComplete.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Tab, ModifierKeys.None, true, false, 0));
        // Simulate follow-up text input event for the same key stroke.
        autoComplete.RaiseEvent(new TextCompositionEventArgs(UIElement.TextInputEvent, "\t", 1));

        // Assert
        Assert.Equal("apple", autoComplete.Text);
        Assert.DoesNotContain('\t', autoComplete.Text);
    }

    [Fact]
    public void AutoCompleteBox_LostFocusWhilePopupHovered_ShouldKeepDropDownOpen()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var autoComplete = new AutoCompleteBox
            {
                Width = 240,
                ItemsSource = new[] { "apple", "apricot", "banana" },
                MinimumPrefixLength = 1
            };

            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 320,
                Height = 120,
                Content = autoComplete
            };
            app.MainWindow = window;

            window.Measure(new Size(320, 120));
            window.Arrange(new Rect(0, 0, 320, 120));

            autoComplete.Text = "ap";
            window.Measure(new Size(320, 120));
            window.Arrange(new Rect(0, 0, 320, 120));

            Assert.True(autoComplete.IsDropDownOpen);

            var popup = GetPrivateField<Popup>(autoComplete, "_popup");
            Assert.NotNull(popup);

            var popupRoot = GetPrivateField<PopupRoot>(popup, "_popupRoot");
            Assert.NotNull(popupRoot);

            popupRoot.RaiseEvent(new MouseEventArgs(UIElement.MouseEnterEvent) { Source = popupRoot });
            Assert.True(popup.IsMouseOver);

            var lostFocusHandler = typeof(AutoCompleteBox).GetMethod("OnLostFocusHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(lostFocusHandler);
            lostFocusHandler!.Invoke(autoComplete, new object[] { autoComplete, new KeyboardFocusChangedEventArgs(UIElement.LostKeyboardFocusEvent) });

            Assert.True(autoComplete.IsDropDownOpen);
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
}
