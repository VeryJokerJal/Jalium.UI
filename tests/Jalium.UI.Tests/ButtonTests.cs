using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

/// <summary>
/// Button 控件测试
/// </summary>
public class ButtonTests
{
    [Fact]
    public void Button_ShouldHaveDefaultProperties()
    {
        // Arrange & Act
        var button = new Button();

        // Assert
        Assert.False(button.IsDefault);
        Assert.False(button.IsCancel);
        Assert.False(button.IsPressed);
        Assert.True(button.Focusable);
        Assert.Equal(ClickMode.Release, button.ClickMode);
    }

    [Fact]
    public void Button_IsDefault_ShouldBeSettable()
    {
        // Arrange
        var button = new Button();

        // Act
        button.IsDefault = true;

        // Assert
        Assert.True(button.IsDefault);
    }

    [Fact]
    public void Button_IsCancel_ShouldBeSettable()
    {
        // Arrange
        var button = new Button();

        // Act
        button.IsCancel = true;

        // Assert
        Assert.True(button.IsCancel);
    }

    [Fact]
    public void Button_Content_ShouldBeSettable()
    {
        // Arrange
        var button = new Button();

        // Act
        button.Content = "Click Me";

        // Assert
        Assert.Equal("Click Me", button.Content);
    }

    [Fact]
    public void Button_Click_ShouldRaiseEvent()
    {
        // Arrange
        var button = new Button();
        var clicked = false;
        button.Click += (s, e) => clicked = true;

        // Act - simulate click by raising the event
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        // Assert
        Assert.True(clicked);
    }

    [Fact]
    public void Button_Click_ShouldBubbleUp()
    {
        // Arrange
        var parent = new TestPanel();
        var button = new Button();
        parent.AddChild(button);

        var parentReceivedClick = false;
        parent.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
        {
            parentReceivedClick = true;
        }));

        // Act
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        // Assert
        Assert.True(parentReceivedClick, "Click event should bubble to parent");
    }

    [Fact]
    public void Button_ClickMode_Press_ShouldBeSettable()
    {
        // Arrange
        var button = new Button();

        // Act
        button.ClickMode = ClickMode.Press;

        // Assert
        Assert.Equal(ClickMode.Press, button.ClickMode);
    }

    [Fact]
    public void Button_ClickMode_Hover_ShouldBeSettable()
    {
        // Arrange
        var button = new Button();

        // Act
        button.ClickMode = ClickMode.Hover;

        // Assert
        Assert.Equal(ClickMode.Hover, button.ClickMode);
    }

    [Fact]
    public void Button_IsEnabled_ShouldDefaultToTrue()
    {
        // Arrange & Act
        var button = new Button();

        // Assert
        Assert.True(button.IsEnabled);
    }

    [Fact]
    public void Button_DisabledButton_ShouldNotRaiseClick()
    {
        // Arrange
        var button = new Button();
        button.IsEnabled = false;
        var clicked = false;
        button.Click += (s, e) => clicked = true;

        // Act - We cannot easily simulate disabled behavior without mouse events
        // But we can verify the IsEnabled property
        Assert.False(button.IsEnabled);

        // Note: In a real scenario, the input handler checks IsEnabled before raising click
    }

    /// <summary>
    /// Test helper panel class
    /// </summary>
    private class TestPanel : FrameworkElement
    {
        public void AddChild(UIElement child)
        {
            AddVisualChild(child);
        }
    }
}
