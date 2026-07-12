using System.ComponentModel;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class SystemParametersWpfParityTests
{
    private const string ExpectedKeyNames = """
        BorderKey BorderWidthKey CaptionHeightKey CaptionWidthKey CaretWidthKey ClientAreaAnimationKey
        ComboBoxAnimationKey ComboBoxPopupAnimationKey CursorHeightKey CursorShadowKey CursorWidthKey
        DragFullWindowsKey DropShadowKey FixedFrameHorizontalBorderHeightKey FixedFrameVerticalBorderWidthKey
        FlatMenuKey FocusBorderHeightKey FocusBorderWidthKey FocusHorizontalBorderHeightKey FocusVerticalBorderWidthKey
        FocusVisualStyleKey ForegroundFlashCountKey FullPrimaryScreenHeightKey FullPrimaryScreenWidthKey
        GradientCaptionsKey HighContrastKey HorizontalScrollBarButtonWidthKey HorizontalScrollBarHeightKey
        HorizontalScrollBarThumbWidthKey HotTrackingKey IconGridHeightKey IconGridWidthKey IconHeightKey
        IconHorizontalSpacingKey IconTitleWrapKey IconVerticalSpacingKey IconWidthKey IsImmEnabledKey IsMediaCenterKey
        IsMenuDropRightAlignedKey IsMiddleEastEnabledKey IsMousePresentKey IsMouseWheelPresentKey IsPenWindowsKey
        IsRemoteSessionKey IsRemotelyControlledKey IsSlowMachineKey IsTabletPCKey KanjiWindowHeightKey KeyboardCuesKey
        KeyboardDelayKey KeyboardPreferenceKey KeyboardSpeedKey ListBoxSmoothScrollingKey MaximizedPrimaryScreenHeightKey
        MaximizedPrimaryScreenWidthKey MaximumWindowTrackHeightKey MaximumWindowTrackWidthKey MenuAnimationKey
        MenuBarHeightKey MenuButtonHeightKey MenuButtonWidthKey MenuCheckmarkHeightKey MenuCheckmarkWidthKey
        MenuDropAlignmentKey MenuFadeKey MenuHeightKey MenuPopupAnimationKey MenuShowDelayKey MenuWidthKey
        MinimizeAnimationKey MinimizedGridHeightKey MinimizedGridWidthKey MinimizedWindowHeightKey MinimizedWindowWidthKey
        MinimumWindowHeightKey MinimumWindowTrackHeightKey MinimumWindowTrackWidthKey MinimumWindowWidthKey
        MouseHoverHeightKey MouseHoverTimeKey MouseHoverWidthKey NavigationChromeDownLevelStyleKey NavigationChromeStyleKey
        PowerLineStatusKey PrimaryScreenHeightKey PrimaryScreenWidthKey ResizeFrameHorizontalBorderHeightKey
        ResizeFrameVerticalBorderWidthKey ScrollHeightKey ScrollWidthKey SelectionFadeKey ShowSoundsKey SmallCaptionHeightKey
        SmallCaptionWidthKey SmallIconHeightKey SmallIconWidthKey SmallWindowCaptionButtonHeightKey
        SmallWindowCaptionButtonWidthKey SnapToDefaultButtonKey StylusHotTrackingKey SwapButtonsKey
        ThickHorizontalBorderHeightKey ThickVerticalBorderWidthKey ThinHorizontalBorderHeightKey ThinVerticalBorderWidthKey
        ToolTipAnimationKey ToolTipFadeKey ToolTipPopupAnimationKey UIEffectsKey VerticalScrollBarButtonHeightKey
        VerticalScrollBarThumbHeightKey VerticalScrollBarWidthKey VirtualScreenHeightKey VirtualScreenLeftKey
        VirtualScreenTopKey VirtualScreenWidthKey WheelScrollLinesKey WindowCaptionButtonHeightKey
        WindowCaptionButtonWidthKey WindowCaptionHeightKey WorkAreaKey
        """;

    [Fact]
    public void FreshVerifierTierOneSurface_HasExactPropertyAndEventTypes()
    {
        AssertProperties<int>("Border KeyboardDelay KeyboardSpeed MenuShowDelay");
        AssertProperties<bool>("""
            ComboBoxAnimation CursorShadow DragFullWindows HotTracking IconTitleWrap IsImmEnabled IsMediaCenter
            IsMenuDropRightAligned IsMiddleEastEnabled IsMousePresent IsMouseWheelPresent IsPenWindows IsRemoteSession
            IsRemotelyControlled IsSlowMachine KeyboardCues KeyboardPreference ListBoxSmoothScrolling MenuFade
            MinimizeAnimation ShowSounds SnapToDefaultButton SwapButtons ToolTipFade
            """);
        AssertProperties<double>("""
            FixedFrameHorizontalBorderHeight FixedFrameVerticalBorderWidth FocusBorderHeight FocusBorderWidth
            FocusHorizontalBorderHeight FocusVerticalBorderWidth FullPrimaryScreenHeight FullPrimaryScreenWidth
            IconGridHeight IconGridWidth IconHorizontalSpacing IconVerticalSpacing KanjiWindowHeight
            MaximizedPrimaryScreenHeight MaximizedPrimaryScreenWidth MaximumWindowTrackHeight MaximumWindowTrackWidth
            MenuButtonHeight MenuButtonWidth MenuCheckmarkHeight MenuCheckmarkWidth MenuHeight MenuWidth
            MinimizedGridHeight MinimizedGridWidth MinimizedWindowHeight MinimizedWindowWidth MinimumWindowHeight
            MinimumWindowTrackHeight MinimumWindowTrackWidth MinimumWindowWidth ResizeFrameHorizontalBorderHeight
            ResizeFrameVerticalBorderWidth SmallCaptionHeight SmallCaptionWidth SmallWindowCaptionButtonHeight
            SmallWindowCaptionButtonWidth ThickHorizontalBorderHeight ThickVerticalBorderWidth ThinHorizontalBorderHeight
            ThinVerticalBorderWidth WindowCaptionButtonHeight WindowCaptionButtonWidth WindowCaptionHeight CaretWidth
            """);
        AssertProperties<PopupAnimation>("ComboBoxPopupAnimation MenuPopupAnimation ToolTipPopupAnimation");
        AssertProperties<string>("UxThemeColor UxThemeName");
        AssertProperties<CornerRadius>("WindowCornerRadius");
        AssertProperties<Brush>("WindowGlassBrush");
        AssertProperties<Color>("WindowGlassColor");
        AssertProperties<TimeSpan>("MouseHoverTime");
        AssertProperties<PowerLineStatus>("PowerLineStatus");

        var eventInfo = typeof(SystemParameters).GetEvent(
            nameof(SystemParameters.StaticPropertyChanged),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(PropertyChangedEventHandler), eventInfo!.EventHandlerType);

        var expectedKeys = Names(ExpectedKeyNames);
        var actualKeys = typeof(SystemParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(static property => property.Name.EndsWith("Key", StringComparison.Ordinal)
                && property.PropertyType == typeof(ResourceKey))
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedKeys.OrderBy(static name => name, StringComparer.Ordinal), actualKeys);
    }

    [Fact]
    public void EveryResourceKey_IsStableAndResolvesToAUsableResource()
    {
        foreach (var keyName in Names(ExpectedKeyNames))
        {
            var keyProperty = typeof(SystemParameters).GetProperty(keyName, BindingFlags.Public | BindingFlags.Static)!;
            var first = Assert.IsAssignableFrom<ResourceKey>(keyProperty.GetValue(null));
            var second = Assert.IsAssignableFrom<ResourceKey>(keyProperty.GetValue(null));
            Assert.Same(first, second);

            var componentKey = Assert.IsType<ComponentResourceKey>(first);
            Assert.Equal(typeof(SystemParameters), componentKey.TypeInTargetAssembly);
            var resourceId = Assert.IsType<string>(componentKey.ResourceId);

            Assert.True(SystemParameters.TryGetResource(first, out var actual));
            Assert.NotNull(actual);

            var valueProperty = typeof(SystemParameters).GetProperty(resourceId, BindingFlags.Public | BindingFlags.Static);
            if (valueProperty != null)
            {
                var expected = valueProperty.GetValue(null);
                if (expected is Brush)
                {
                    Assert.Same(expected, actual);
                }
                else
                {
                    Assert.Equal(expected, actual);
                }
            }
            else
            {
                Assert.IsType<Style>(actual);
            }
        }

        var unknown = new ComponentResourceKey(typeof(SystemParameters), "UnknownSystemParameter");
        Assert.False(SystemParameters.TryGetResource(unknown, out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void MetricsAndThemeValues_AreInUsefulRangesAndStableWhereRequired()
    {
        Assert.True(SystemParameters.PrimaryScreenWidth > 0);
        Assert.True(SystemParameters.PrimaryScreenHeight > 0);
        Assert.True(SystemParameters.VirtualScreenWidth > 0);
        Assert.True(SystemParameters.VirtualScreenHeight > 0);
        Assert.True(SystemParameters.WorkArea.Width > 0);
        Assert.True(SystemParameters.WorkArea.Height > 0);
        Assert.True(SystemParameters.MouseHoverTime > TimeSpan.Zero);
        Assert.True(SystemParameters.CaretWidth > 0);
        Assert.InRange(SystemParameters.KeyboardDelay, 0, 3);
        Assert.InRange(SystemParameters.KeyboardSpeed, 0, 31);
        Assert.True(Enum.IsDefined(SystemParameters.PowerLineStatus));
        Assert.False(string.IsNullOrWhiteSpace(SystemParameters.UxThemeName));

        Assert.Equal(
            SystemParameters.ComboBoxAnimation ? PopupAnimation.Slide : PopupAnimation.None,
            SystemParameters.ComboBoxPopupAnimation);
        Assert.Equal(
            !SystemParameters.MenuAnimation
                ? PopupAnimation.None
                : SystemParameters.MenuFade ? PopupAnimation.Fade : PopupAnimation.Scroll,
            SystemParameters.MenuPopupAnimation);
        Assert.Equal(
            SystemParameters.ToolTipAnimation && SystemParameters.ToolTipFade
                ? PopupAnimation.Fade
                : PopupAnimation.None,
            SystemParameters.ToolTipPopupAnimation);

        var brush = Assert.IsType<SolidColorBrush>(SystemParameters.WindowGlassBrush);
        Assert.Same(brush, SystemParameters.WindowGlassBrush);
        Assert.Equal(SystemParameters.WindowGlassColor, brush.Color);
    }

    [Fact]
    public void NotifyStaticPropertyChanged_RaisesThePublicStaticEvent()
    {
        object? sender = new object();
        string? propertyName = null;
        PropertyChangedEventHandler handler = (actualSender, args) =>
        {
            sender = actualSender;
            propertyName = args.PropertyName;
        };

        SystemParameters.StaticPropertyChanged += handler;
        try
        {
            SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.HighContrast));
        }
        finally
        {
            SystemParameters.StaticPropertyChanged -= handler;
        }

        Assert.Null(sender);
        Assert.Equal(nameof(SystemParameters.HighContrast), propertyName);
    }

    private static void AssertProperties<T>(string names)
    {
        foreach (var name in Names(names))
        {
            var property = typeof(SystemParameters).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(property);
            Assert.Equal(typeof(T), property!.PropertyType);
            Assert.NotNull(property.GetMethod);
        }
    }

    private static string[] Names(string names)
        => names.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
