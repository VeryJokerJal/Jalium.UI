using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class LiquidGlassInteractionTests
{
    [Fact]
    public void LiquidGlassBorder_ShouldStartPress_WhenChildHandlesMouseDown()
    {
        UIElement.ForceReleaseMouseCapture();

        try
        {
            var glass = CreateInteractiveLiquidGlassHost(out var child);
            child.AddHandler(UIElement.MouseDownEvent, new RoutedEventHandler((_, e) => e.Handled = true));

            WireLiquidGlassTracking(glass);

            child.RaiseEvent(CreateMouseDown(new Point(12, 10)));

            Assert.True(glass.IsMouseCaptured);
            Assert.True(GetPrivateField<bool>(glass, "_lgPressed"));
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
        }
    }

    [Fact]
    public void LiquidGlassBorder_ShouldTrackDrag_WhenChildHandlesMouseMove()
    {
        UIElement.ForceReleaseMouseCapture();

        try
        {
            var glass = CreateInteractiveLiquidGlassHost(out var child);
            child.AddHandler(UIElement.MouseDownEvent, new RoutedEventHandler((_, e) => e.Handled = true));
            child.AddHandler(UIElement.MouseMoveEvent, new RoutedEventHandler((_, e) => e.Handled = true));

            WireLiquidGlassTracking(glass);

            child.RaiseEvent(CreateMouseDown(new Point(10, 10)));
            child.RaiseEvent(CreateMouseMove(new Point(34, 18), MouseButtonState.Pressed));

            var light = GetPrivateField<Point>(glass, "_lgLightLocal");
            Assert.Equal(34, light.X);
            Assert.Equal(18, light.Y);
        }
        finally
        {
            UIElement.ForceReleaseMouseCapture();
        }
    }

    private static Border CreateInteractiveLiquidGlassHost(out Border child)
    {
        child = new Border
        {
            Width = 160,
            Height = 56
        };

        var glass = new Border
        {
            Width = 160,
            Height = 56,
            LiquidGlass = true,
            LiquidGlassInteractive = true,
            Child = child
        };

        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 160,
            Height = 56,
            Content = glass
        };

        window.Measure(new Size(160, 56));
        window.Arrange(new Rect(0, 0, 160, 56));
        return glass;
    }

    private static void WireLiquidGlassTracking(Border glass)
    {
        var method = typeof(Border).GetMethod("TryWireLgWindowTracking", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(glass, [])!);
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1);
    }

    private static MouseEventArgs CreateMouseMove(Point position, MouseButtonState leftButton)
    {
        return new MouseEventArgs(
            UIElement.MouseMoveEvent,
            position,
            leftButton,
            MouseButtonState.Released,
            MouseButtonState.Released,
            MouseButtonState.Released,
            MouseButtonState.Released,
            ModifierKeys.None,
            timestamp: 2);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }
}
