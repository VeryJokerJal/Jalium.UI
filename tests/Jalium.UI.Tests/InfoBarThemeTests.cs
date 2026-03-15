using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class InfoBarThemeTests
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
    public void InfoBar_ImplicitThemeStyle_ShouldApplyWithoutLocalLayoutOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var infoBar = new InfoBar
            {
                Title = "Heads up",
                Message = "Theme-driven layout"
            };
            var host = new StackPanel { Width = 360, Height = 120 };
            host.Children.Add(infoBar);

            host.Measure(new Size(360, 120));
            host.Arrange(new Rect(0, 0, 360, 120));

            Assert.True(app.Resources.TryGetValue(typeof(InfoBar), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(infoBar.HasLocalValue(Control.PaddingProperty));
            Assert.False(infoBar.HasLocalValue(Control.CornerRadiusProperty));
            Assert.Equal(12, infoBar.Padding.Left);
            Assert.Equal(8, infoBar.Padding.Top);
            Assert.Equal(4, infoBar.CornerRadius.TopLeft);
            Assert.True(infoBar.RenderSize.Height >= 48);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void InfoBar_SeverityBrushes_ShouldResolveFromThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var infoBar = new InfoBar
            {
                Severity = InfoBarSeverity.Warning
            };

            Assert.True(app.Resources.TryGetValue("InfoBarWarningBackground", out var bgObj));
            Assert.True(app.Resources.TryGetValue("InfoBarWarningBrush", out var iconObj));

            var getSeverityBrushes = typeof(InfoBar).GetMethod("GetSeverityBrushes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(getSeverityBrushes);

            var tuple = getSeverityBrushes!.Invoke(infoBar, null);
            Assert.NotNull(tuple);

            var backgroundField = tuple!.GetType().GetField("Item1");
            var iconField = tuple.GetType().GetField("Item2");
            Assert.NotNull(backgroundField);
            Assert.NotNull(iconField);

            Assert.Same(bgObj, backgroundField!.GetValue(tuple));
            Assert.Same(iconObj, iconField!.GetValue(tuple));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void InfoBar_MouseWheel_ShouldUseAncestorScrollViewerSmoothScrolling()
    {
        ResetApplicationState();

        try
        {
            var infoBar = new InfoBar
            {
                Title = "Scrollable",
                Message = "Wheel should scroll page."
            };

            var content = new Grid { Height = 600 };
            content.Children.Add(new Border { Height = 260 });
            content.Children.Add(infoBar);
            Grid.SetRow(infoBar, 1);
            content.Children.Add(new Border { Height = 260 });
            Grid.SetRow(content.Children[2], 2);

            var viewer = new ScrollViewer
            {
                Width = 360,
                Height = 200,
                Content = content,
                IsScrollInertiaEnabled = true,
                ScrollInertiaDurationMs = 3000
            };

            viewer.Measure(new Size(360, 200));
            viewer.Arrange(new Rect(0, 0, 360, 200));

            SetPrivateField(viewer, "_viewportHeight", 200.0);
            SetPrivateField(viewer, "_extentHeight", 600.0);
            SetPrivateField(viewer, "_verticalOffset", 0.0);
            SetPrivateField(viewer, "_smoothTargetY", 0.0);

            var wheel = CreateMouseWheel(new Point(10, 10), -120, timestamp: 1);
            infoBar.RaiseEvent(wheel);

            Assert.True(wheel.Handled);
            Assert.True(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));
            Assert.True(GetPrivateField<double>(viewer, "_smoothTargetY") > viewer.VerticalOffset);
            Assert.Equal(0.0, viewer.VerticalOffset, precision: 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static MouseWheelEventArgs CreateMouseWheel(Point position, int delta, int timestamp)
    {
        return new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            position,
            delta,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: timestamp);
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return (T)value!;
    }
}
