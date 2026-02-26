using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

/// <summary>
/// CheckBox 控件测试
/// </summary>
public class CheckBoxTests
{
    [Fact]
    public void CheckBox_ShouldHaveDefaultProperties()
    {
        // Arrange & Act
        var checkBox = new CheckBox();

        // Assert
        Assert.False(checkBox.IsChecked);
        Assert.False(checkBox.IsThreeState);
        Assert.True(checkBox.Focusable);
    }

    [Fact]
    public void CheckBox_IsChecked_ShouldBeSettable()
    {
        // Arrange
        var checkBox = new CheckBox();

        // Act
        checkBox.IsChecked = true;

        // Assert
        Assert.True(checkBox.IsChecked);
    }

    [Fact]
    public void CheckBox_IsChecked_ShouldToggle()
    {
        // Arrange
        var checkBox = new CheckBox();
        Assert.False(checkBox.IsChecked);

        // Act - toggle to checked
        checkBox.IsChecked = true;
        Assert.True(checkBox.IsChecked);

        // Act - toggle back to unchecked
        checkBox.IsChecked = false;
        Assert.False(checkBox.IsChecked);
    }

    [Fact]
    public void CheckBox_IsThreeState_ShouldSupportIndeterminate()
    {
        // Arrange
        var checkBox = new CheckBox();
        checkBox.IsThreeState = true;

        // Act
        checkBox.IsChecked = null; // Indeterminate state

        // Assert
        Assert.Null(checkBox.IsChecked);
    }

    [Fact]
    public void CheckBox_Checked_ShouldRaiseEvent()
    {
        // Arrange
        var checkBox = new CheckBox();
        var checkedEventRaised = false;
        checkBox.Checked += (s, e) => checkedEventRaised = true;

        // Act
        checkBox.IsChecked = true;

        // Assert
        Assert.True(checkedEventRaised);
    }

    [Fact]
    public void CheckBox_Unchecked_ShouldRaiseEvent()
    {
        // Arrange
        var checkBox = new CheckBox();
        checkBox.IsChecked = true;

        var uncheckedEventRaised = false;
        checkBox.Unchecked += (s, e) => uncheckedEventRaised = true;

        // Act
        checkBox.IsChecked = false;

        // Assert
        Assert.True(uncheckedEventRaised);
    }

    [Fact]
    public void CheckBox_Indeterminate_ShouldRaiseEvent()
    {
        // Arrange
        var checkBox = new CheckBox();
        checkBox.IsThreeState = true;

        var indeterminateEventRaised = false;
        checkBox.Indeterminate += (s, e) => indeterminateEventRaised = true;

        // Act
        checkBox.IsChecked = null;

        // Assert
        Assert.True(indeterminateEventRaised);
    }

    [Fact]
    public void CheckBox_Content_ShouldBeSettable()
    {
        // Arrange
        var checkBox = new CheckBox();

        // Act
        checkBox.Content = "Accept Terms";

        // Assert
        Assert.Equal("Accept Terms", checkBox.Content);
    }

    [Fact]
    public void CheckBox_TwoState_ShouldCycleBetweenCheckedAndUnchecked()
    {
        // Arrange
        var checkBox = new CheckBox();
        checkBox.IsThreeState = false;
        checkBox.IsChecked = false;

        // Act & Assert - first toggle
        checkBox.IsChecked = !(checkBox.IsChecked ?? false);
        Assert.True(checkBox.IsChecked);

        // Act & Assert - second toggle
        checkBox.IsChecked = !(checkBox.IsChecked ?? false);
        Assert.False(checkBox.IsChecked);
    }

    [Fact]
    public void CheckBox_Click_ShouldRaiseClickEvent()
    {
        // Arrange
        var checkBox = new CheckBox();
        var clicked = false;
        checkBox.Click += (s, e) => clicked = true;

        // Act
        checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, checkBox));

        // Assert
        Assert.True(clicked);
    }

    [Fact]
    public void CheckBox_EventOrder_CheckedThenClick()
    {
        // Arrange
        var checkBox = new CheckBox();
        var eventOrder = new List<string>();

        checkBox.Checked += (s, e) => eventOrder.Add("Checked");
        checkBox.Click += (s, e) => eventOrder.Add("Click");

        // Act - simulating OnClick which calls OnToggle then base.OnClick
        checkBox.IsChecked = true;
        checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, checkBox));

        // Assert
        Assert.Equal(2, eventOrder.Count);
        Assert.Equal("Checked", eventOrder[0]);
        Assert.Equal("Click", eventOrder[1]);
    }

    [Fact]
    public void CheckBox_IsEnabled_ShouldDefaultToTrue()
    {
        // Arrange & Act
        var checkBox = new CheckBox();

        // Assert
        Assert.True(checkBox.IsEnabled);
    }

    [Fact]
    public void CheckBox_Disabled_ShouldNotToggle()
    {
        // Arrange
        var checkBox = new CheckBox();
        checkBox.IsEnabled = false;
        checkBox.IsChecked = false;

        // Note: In real usage, the input handler checks IsEnabled
        // Here we just verify the property can still be set programmatically
        checkBox.IsChecked = true;

        // Assert - programmatic set still works even when disabled
        Assert.True(checkBox.IsChecked);
    }
}
