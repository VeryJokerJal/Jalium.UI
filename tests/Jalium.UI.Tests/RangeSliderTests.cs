using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class RangeSliderTests
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
    public void RangeSlider_DefaultBounds_AreFullRange()
    {
        var slider = new RangeSlider();

        Assert.Equal(0.0, slider.Minimum);
        Assert.Equal(100.0, slider.Maximum);
        Assert.Equal(0.0, slider.RangeStart);
        Assert.Equal(100.0, slider.RangeEnd);
    }

    [Fact]
    public void RangeSlider_RangeStart_CoercedToMinimum_WhenBelowBounds()
    {
        var slider = new RangeSlider { Minimum = 10, Maximum = 50 };

        slider.RangeStart = -5;

        Assert.Equal(10, slider.RangeStart);
    }

    [Fact]
    public void RangeSlider_RangeEnd_CoercedToMaximum_WhenAboveBounds()
    {
        var slider = new RangeSlider { Minimum = 0, Maximum = 50 };

        slider.RangeEnd = 999;

        Assert.Equal(50, slider.RangeEnd);
    }

    [Fact]
    public void RangeSlider_RangeStart_CannotExceedRangeEndMinusMinimumRange()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            RangeStart = 10,
            RangeEnd = 30,
            MinimumRange = 5
        };

        slider.RangeStart = 40;

        Assert.Equal(25, slider.RangeStart); // RangeEnd 30 - MinimumRange 5
    }

    [Fact]
    public void RangeSlider_RangeEnd_CannotFallBelowRangeStartPlusMinimumRange()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            RangeStart = 40,
            RangeEnd = 80,
            MinimumRange = 5
        };

        slider.RangeEnd = 10;

        Assert.Equal(45, slider.RangeEnd); // RangeStart 40 + MinimumRange 5
    }

    [Fact]
    public void RangeSlider_ChangingMaximum_RecoercesRangeEnd()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            RangeStart = 20,
            RangeEnd = 80
        };

        slider.Maximum = 50;

        Assert.Equal(50, slider.RangeEnd);
        Assert.Equal(20, slider.RangeStart);
    }

    [Fact]
    public void RangeSlider_RangeStartChangedEvent_FiresWithOldAndNewValues()
    {
        var slider = new RangeSlider { RangeStart = 10 };
        double? oldValue = null, newValue = null;
        slider.RangeStartChanged += (_, e) =>
        {
            oldValue = e.OldValue;
            newValue = e.NewValue;
        };

        slider.RangeStart = 25;

        Assert.Equal(10, oldValue);
        Assert.Equal(25, newValue);
    }

    [Fact]
    public void RangeSlider_RangeEndChangedEvent_FiresWithOldAndNewValues()
    {
        var slider = new RangeSlider { RangeEnd = 80 };
        double? oldValue = null, newValue = null;
        slider.RangeEndChanged += (_, e) =>
        {
            oldValue = e.OldValue;
            newValue = e.NewValue;
        };

        slider.RangeEnd = 60;

        Assert.Equal(80, oldValue);
        Assert.Equal(60, newValue);
    }

    [Fact]
    public void RangeSlider_RegisteredInXamlTypeRegistry_AndDefaultStyleProperties()
    {
        // The XamlTypeRegistry registration in Jalium.UI.Xaml.XamlReader.cs is what lets
        // <Style TargetType="RangeSlider"> resolve when jalxaml is parsed at runtime. If
        // someone forgets to add the Register<RangeSlider> call this assertion catches it
        // without depending on the (separately fragile) end-to-end theme-loading flow.
        var resolved = Jalium.UI.Markup.XamlTypeRegistry.GetType("RangeSlider");
        Assert.Equal(typeof(RangeSlider), resolved);

        var slider = new RangeSlider();
        // RangeBase-style invariants the default style relies on.
        Assert.Equal(0.0, slider.Minimum);
        Assert.Equal(100.0, slider.Maximum);
        Assert.Equal(0.0, slider.RangeStart);
        Assert.Equal(100.0, slider.RangeEnd);
    }

    [Fact]
    public void RangeSlider_ValueFromPosition_ProjectsBackToValueRange()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 216, // ThumbSize 16 + track 200
            Height = 24
        };
        slider.Measure(new Size(216, 24));
        slider.Arrange(new Rect(0, 0, 216, 24));

        var method = typeof(RangeSlider).GetMethod("ValueFromPosition", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        // Click in the middle of the track should map to ~50.
        var middleValue = (double)method!.Invoke(slider, new object[] { new Point(108, 12) })!;
        Assert.InRange(middleValue, 49.0, 51.0);

        // Far-left → Minimum.
        var leftValue = (double)method.Invoke(slider, new object[] { new Point(0, 12) })!;
        Assert.Equal(0, leftValue);

        // Far-right → Maximum.
        var rightValue = (double)method.Invoke(slider, new object[] { new Point(216, 12) })!;
        Assert.Equal(100, rightValue);
    }

    [Fact]
    public void RangeSlider_SnapToTick_RoundsToTickFrequency()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 216,
            Height = 24,
            TickFrequency = 10,
            IsSnapToTickEnabled = true
        };
        slider.Measure(new Size(216, 24));
        slider.Arrange(new Rect(0, 0, 216, 24));

        var method = typeof(RangeSlider).GetMethod("ValueFromPosition", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        // 53% of the track → raw 53 → snap to 50.
        var snappedValue = (double)method!.Invoke(slider, new object[] { new Point(8 + 200 * 0.53, 12) })!;
        Assert.Equal(50, snappedValue);
    }

    [Fact]
    public void RangeSlider_AutomationPeer_ValueRoundTrip()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            RangeStart = 20,
            RangeEnd = 80
        };
        var peer = new Jalium.UI.Automation.Peers.RangeSliderAutomationPeer(slider);

        Assert.Equal("20..80", peer.Value);

        peer.SetValue("30..70");
        Assert.Equal(30, slider.RangeStart);
        Assert.Equal(70, slider.RangeEnd);
    }

    [Fact]
    public void RangeSlider_AutomationPeer_SwapsStartAndEnd_WhenInverted()
    {
        var slider = new RangeSlider
        {
            Minimum = 0,
            Maximum = 100,
            RangeStart = 20,
            RangeEnd = 80
        };
        var peer = new Jalium.UI.Automation.Peers.RangeSliderAutomationPeer(slider);

        peer.SetValue("90..10");

        Assert.Equal(10, slider.RangeStart);
        Assert.Equal(90, slider.RangeEnd);
    }

    private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(instance) as T;
    }
}
