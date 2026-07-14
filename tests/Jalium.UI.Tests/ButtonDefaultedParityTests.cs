using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection(nameof(ParityFoundationBehaviorCollection))]
public sealed class ButtonDefaultedParityTests
{
    [Fact]
    public void IsDefaultedIsReadOnlyAndTracksFocusEligibility()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        try
        {
            Assert.True(Button.IsDefaultedProperty.ReadOnly);
            Assert.Null(typeof(Button).GetProperty(nameof(Button.IsDefaulted))!.SetMethod);

            var root = new Grid();
            var button = new Button { IsDefault = true };
            var singleLine = new TextBox();
            var multiLine = new TextBox { AcceptsReturn = true };
            root.Children.Add(button);
            root.Children.Add(singleLine);
            root.Children.Add(multiLine);

            Assert.False(button.IsDefaulted);
            Assert.True(singleLine.Focus());
            Assert.True(button.IsDefaulted);

            Assert.True(multiLine.Focus());
            Assert.False(button.IsDefaulted);

            Assert.True(button.Focus());
            Assert.True(button.IsDefaulted);
            button.IsEnabled = false;
            Assert.False(button.IsDefaulted);
        }
        finally
        {
            Keyboard.ClearFocus();
        }
    }
}
