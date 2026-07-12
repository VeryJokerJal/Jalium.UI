namespace Jalium.UI;

/// <summary>
/// Stable resource keys and resource-value routing for WPF system parameters.
/// </summary>
public static partial class SystemParameters
{
    private static readonly object s_resourceLock = new();
    private static readonly Dictionary<string, ResourceKey> s_resourceKeys = new(StringComparer.Ordinal);
    private static readonly Style s_focusVisualStyle = new();
    private static readonly Style s_navigationChromeDownLevelStyle = new();
    private static readonly Style s_navigationChromeStyle = new();

    public static ResourceKey BorderKey => GetResourceKey(nameof(Border));
    public static ResourceKey BorderWidthKey => GetResourceKey(nameof(BorderWidth));
    public static ResourceKey CaptionHeightKey => GetResourceKey(nameof(CaptionHeight));
    public static ResourceKey CaptionWidthKey => GetResourceKey(nameof(CaptionWidth));
    public static ResourceKey CaretWidthKey => GetResourceKey(nameof(CaretWidth));
    public static ResourceKey ClientAreaAnimationKey => GetResourceKey(nameof(ClientAreaAnimation));
    public static ResourceKey ComboBoxAnimationKey => GetResourceKey(nameof(ComboBoxAnimation));
    public static ResourceKey ComboBoxPopupAnimationKey => GetResourceKey(nameof(ComboBoxPopupAnimation));
    public static ResourceKey CursorHeightKey => GetResourceKey(nameof(CursorHeight));
    public static ResourceKey CursorShadowKey => GetResourceKey(nameof(CursorShadow));
    public static ResourceKey CursorWidthKey => GetResourceKey(nameof(CursorWidth));
    public static ResourceKey DragFullWindowsKey => GetResourceKey(nameof(DragFullWindows));
    public static ResourceKey DropShadowKey => GetResourceKey(nameof(DropShadow));
    public static ResourceKey FixedFrameHorizontalBorderHeightKey => GetResourceKey(nameof(FixedFrameHorizontalBorderHeight));
    public static ResourceKey FixedFrameVerticalBorderWidthKey => GetResourceKey(nameof(FixedFrameVerticalBorderWidth));
    public static ResourceKey FlatMenuKey => GetResourceKey(nameof(FlatMenu));
    public static ResourceKey FocusBorderHeightKey => GetResourceKey(nameof(FocusBorderHeight));
    public static ResourceKey FocusBorderWidthKey => GetResourceKey(nameof(FocusBorderWidth));
    public static ResourceKey FocusHorizontalBorderHeightKey => GetResourceKey(nameof(FocusHorizontalBorderHeight));
    public static ResourceKey FocusVerticalBorderWidthKey => GetResourceKey(nameof(FocusVerticalBorderWidth));
    public static ResourceKey FocusVisualStyleKey => GetResourceKey("FocusVisualStyle");
    public static ResourceKey ForegroundFlashCountKey => GetResourceKey(nameof(ForegroundFlashCount));
    public static ResourceKey FullPrimaryScreenHeightKey => GetResourceKey(nameof(FullPrimaryScreenHeight));
    public static ResourceKey FullPrimaryScreenWidthKey => GetResourceKey(nameof(FullPrimaryScreenWidth));
    public static ResourceKey GradientCaptionsKey => GetResourceKey(nameof(GradientCaptions));
    public static ResourceKey HighContrastKey => GetResourceKey(nameof(HighContrast));
    public static ResourceKey HorizontalScrollBarButtonWidthKey => GetResourceKey(nameof(HorizontalScrollBarButtonWidth));
    public static ResourceKey HorizontalScrollBarHeightKey => GetResourceKey(nameof(HorizontalScrollBarHeight));
    public static ResourceKey HorizontalScrollBarThumbWidthKey => GetResourceKey(nameof(HorizontalScrollBarThumbWidth));
    public static ResourceKey HotTrackingKey => GetResourceKey(nameof(HotTracking));
    public static ResourceKey IconGridHeightKey => GetResourceKey(nameof(IconGridHeight));
    public static ResourceKey IconGridWidthKey => GetResourceKey(nameof(IconGridWidth));
    public static ResourceKey IconHeightKey => GetResourceKey(nameof(IconHeight));
    public static ResourceKey IconHorizontalSpacingKey => GetResourceKey(nameof(IconHorizontalSpacing));
    public static ResourceKey IconTitleWrapKey => GetResourceKey(nameof(IconTitleWrap));
    public static ResourceKey IconVerticalSpacingKey => GetResourceKey(nameof(IconVerticalSpacing));
    public static ResourceKey IconWidthKey => GetResourceKey(nameof(IconWidth));
    public static ResourceKey IsImmEnabledKey => GetResourceKey(nameof(IsImmEnabled));
    public static ResourceKey IsMediaCenterKey => GetResourceKey(nameof(IsMediaCenter));
    public static ResourceKey IsMenuDropRightAlignedKey => GetResourceKey(nameof(IsMenuDropRightAligned));
    public static ResourceKey IsMiddleEastEnabledKey => GetResourceKey(nameof(IsMiddleEastEnabled));
    public static ResourceKey IsMousePresentKey => GetResourceKey(nameof(IsMousePresent));
    public static ResourceKey IsMouseWheelPresentKey => GetResourceKey(nameof(IsMouseWheelPresent));
    public static ResourceKey IsPenWindowsKey => GetResourceKey(nameof(IsPenWindows));
    public static ResourceKey IsRemoteSessionKey => GetResourceKey(nameof(IsRemoteSession));
    public static ResourceKey IsRemotelyControlledKey => GetResourceKey(nameof(IsRemotelyControlled));
    public static ResourceKey IsSlowMachineKey => GetResourceKey(nameof(IsSlowMachine));
    public static ResourceKey IsTabletPCKey => GetResourceKey(nameof(IsTabletPC));
    public static ResourceKey KanjiWindowHeightKey => GetResourceKey(nameof(KanjiWindowHeight));
    public static ResourceKey KeyboardCuesKey => GetResourceKey(nameof(KeyboardCues));
    public static ResourceKey KeyboardDelayKey => GetResourceKey(nameof(KeyboardDelay));
    public static ResourceKey KeyboardPreferenceKey => GetResourceKey(nameof(KeyboardPreference));
    public static ResourceKey KeyboardSpeedKey => GetResourceKey(nameof(KeyboardSpeed));
    public static ResourceKey ListBoxSmoothScrollingKey => GetResourceKey(nameof(ListBoxSmoothScrolling));
    public static ResourceKey MaximizedPrimaryScreenHeightKey => GetResourceKey(nameof(MaximizedPrimaryScreenHeight));
    public static ResourceKey MaximizedPrimaryScreenWidthKey => GetResourceKey(nameof(MaximizedPrimaryScreenWidth));
    public static ResourceKey MaximumWindowTrackHeightKey => GetResourceKey(nameof(MaximumWindowTrackHeight));
    public static ResourceKey MaximumWindowTrackWidthKey => GetResourceKey(nameof(MaximumWindowTrackWidth));
    public static ResourceKey MenuAnimationKey => GetResourceKey(nameof(MenuAnimation));
    public static ResourceKey MenuBarHeightKey => GetResourceKey(nameof(MenuBarHeight));
    public static ResourceKey MenuButtonHeightKey => GetResourceKey(nameof(MenuButtonHeight));
    public static ResourceKey MenuButtonWidthKey => GetResourceKey(nameof(MenuButtonWidth));
    public static ResourceKey MenuCheckmarkHeightKey => GetResourceKey(nameof(MenuCheckmarkHeight));
    public static ResourceKey MenuCheckmarkWidthKey => GetResourceKey(nameof(MenuCheckmarkWidth));
    public static ResourceKey MenuDropAlignmentKey => GetResourceKey(nameof(MenuDropAlignment));
    public static ResourceKey MenuFadeKey => GetResourceKey(nameof(MenuFade));
    public static ResourceKey MenuHeightKey => GetResourceKey(nameof(MenuHeight));
    public static ResourceKey MenuPopupAnimationKey => GetResourceKey(nameof(MenuPopupAnimation));
    public static ResourceKey MenuShowDelayKey => GetResourceKey(nameof(MenuShowDelay));
    public static ResourceKey MenuWidthKey => GetResourceKey(nameof(MenuWidth));
    public static ResourceKey MinimizeAnimationKey => GetResourceKey(nameof(MinimizeAnimation));
    public static ResourceKey MinimizedGridHeightKey => GetResourceKey(nameof(MinimizedGridHeight));
    public static ResourceKey MinimizedGridWidthKey => GetResourceKey(nameof(MinimizedGridWidth));
    public static ResourceKey MinimizedWindowHeightKey => GetResourceKey(nameof(MinimizedWindowHeight));
    public static ResourceKey MinimizedWindowWidthKey => GetResourceKey(nameof(MinimizedWindowWidth));
    public static ResourceKey MinimumWindowHeightKey => GetResourceKey(nameof(MinimumWindowHeight));
    public static ResourceKey MinimumWindowTrackHeightKey => GetResourceKey(nameof(MinimumWindowTrackHeight));
    public static ResourceKey MinimumWindowTrackWidthKey => GetResourceKey(nameof(MinimumWindowTrackWidth));
    public static ResourceKey MinimumWindowWidthKey => GetResourceKey(nameof(MinimumWindowWidth));
    public static ResourceKey MouseHoverHeightKey => GetResourceKey(nameof(MouseHoverHeight));
    public static ResourceKey MouseHoverTimeKey => GetResourceKey(nameof(MouseHoverTime));
    public static ResourceKey MouseHoverWidthKey => GetResourceKey(nameof(MouseHoverWidth));
    public static ResourceKey NavigationChromeDownLevelStyleKey => GetResourceKey("NavigationChromeDownLevelStyle");
    public static ResourceKey NavigationChromeStyleKey => GetResourceKey("NavigationChromeStyle");
    public static ResourceKey PowerLineStatusKey => GetResourceKey(nameof(PowerLineStatus));
    public static ResourceKey PrimaryScreenHeightKey => GetResourceKey(nameof(PrimaryScreenHeight));
    public static ResourceKey PrimaryScreenWidthKey => GetResourceKey(nameof(PrimaryScreenWidth));
    public static ResourceKey ResizeFrameHorizontalBorderHeightKey => GetResourceKey(nameof(ResizeFrameHorizontalBorderHeight));
    public static ResourceKey ResizeFrameVerticalBorderWidthKey => GetResourceKey(nameof(ResizeFrameVerticalBorderWidth));
    public static ResourceKey ScrollHeightKey => GetResourceKey(nameof(ScrollHeight));
    public static ResourceKey ScrollWidthKey => GetResourceKey(nameof(ScrollWidth));
    public static ResourceKey SelectionFadeKey => GetResourceKey(nameof(SelectionFade));
    public static ResourceKey ShowSoundsKey => GetResourceKey(nameof(ShowSounds));
    public static ResourceKey SmallCaptionHeightKey => GetResourceKey(nameof(SmallCaptionHeight));
    public static ResourceKey SmallCaptionWidthKey => GetResourceKey(nameof(SmallCaptionWidth));
    public static ResourceKey SmallIconHeightKey => GetResourceKey(nameof(SmallIconHeight));
    public static ResourceKey SmallIconWidthKey => GetResourceKey(nameof(SmallIconWidth));
    public static ResourceKey SmallWindowCaptionButtonHeightKey => GetResourceKey(nameof(SmallWindowCaptionButtonHeight));
    public static ResourceKey SmallWindowCaptionButtonWidthKey => GetResourceKey(nameof(SmallWindowCaptionButtonWidth));
    public static ResourceKey SnapToDefaultButtonKey => GetResourceKey(nameof(SnapToDefaultButton));
    public static ResourceKey StylusHotTrackingKey => GetResourceKey(nameof(StylusHotTracking));
    public static ResourceKey SwapButtonsKey => GetResourceKey(nameof(SwapButtons));
    public static ResourceKey ThickHorizontalBorderHeightKey => GetResourceKey(nameof(ThickHorizontalBorderHeight));
    public static ResourceKey ThickVerticalBorderWidthKey => GetResourceKey(nameof(ThickVerticalBorderWidth));
    public static ResourceKey ThinHorizontalBorderHeightKey => GetResourceKey(nameof(ThinHorizontalBorderHeight));
    public static ResourceKey ThinVerticalBorderWidthKey => GetResourceKey(nameof(ThinVerticalBorderWidth));
    public static ResourceKey ToolTipAnimationKey => GetResourceKey(nameof(ToolTipAnimation));
    public static ResourceKey ToolTipFadeKey => GetResourceKey(nameof(ToolTipFade));
    public static ResourceKey ToolTipPopupAnimationKey => GetResourceKey(nameof(ToolTipPopupAnimation));
    public static ResourceKey UIEffectsKey => GetResourceKey(nameof(UIEffects));
    public static ResourceKey VerticalScrollBarButtonHeightKey => GetResourceKey(nameof(VerticalScrollBarButtonHeight));
    public static ResourceKey VerticalScrollBarThumbHeightKey => GetResourceKey(nameof(VerticalScrollBarThumbHeight));
    public static ResourceKey VerticalScrollBarWidthKey => GetResourceKey(nameof(VerticalScrollBarWidth));
    public static ResourceKey VirtualScreenHeightKey => GetResourceKey(nameof(VirtualScreenHeight));
    public static ResourceKey VirtualScreenLeftKey => GetResourceKey(nameof(VirtualScreenLeft));
    public static ResourceKey VirtualScreenTopKey => GetResourceKey(nameof(VirtualScreenTop));
    public static ResourceKey VirtualScreenWidthKey => GetResourceKey(nameof(VirtualScreenWidth));
    public static ResourceKey WheelScrollLinesKey => GetResourceKey(nameof(WheelScrollLines));
    public static ResourceKey WindowCaptionButtonHeightKey => GetResourceKey(nameof(WindowCaptionButtonHeight));
    public static ResourceKey WindowCaptionButtonWidthKey => GetResourceKey(nameof(WindowCaptionButtonWidth));
    public static ResourceKey WindowCaptionHeightKey => GetResourceKey(nameof(WindowCaptionHeight));
    public static ResourceKey WorkAreaKey => GetResourceKey(nameof(WorkArea));

    internal static bool TryGetResource(object resourceKey, out object? resource)
    {
        if (resourceKey is not ComponentResourceKey
            {
                TypeInTargetAssembly: { } ownerType,
                ResourceId: string resourceId,
            }
            || ownerType != typeof(SystemParameters))
        {
            resource = null;
            return false;
        }

        resource = resourceId switch
        {
            nameof(Border) => Border,
            nameof(BorderWidth) => BorderWidth,
            nameof(CaptionHeight) => CaptionHeight,
            nameof(CaptionWidth) => CaptionWidth,
            nameof(CaretWidth) => CaretWidth,
            nameof(ClientAreaAnimation) => ClientAreaAnimation,
            nameof(ComboBoxAnimation) => ComboBoxAnimation,
            nameof(ComboBoxPopupAnimation) => ComboBoxPopupAnimation,
            nameof(CursorHeight) => CursorHeight,
            nameof(CursorShadow) => CursorShadow,
            nameof(CursorWidth) => CursorWidth,
            nameof(DragFullWindows) => DragFullWindows,
            nameof(DropShadow) => DropShadow,
            nameof(FixedFrameHorizontalBorderHeight) => FixedFrameHorizontalBorderHeight,
            nameof(FixedFrameVerticalBorderWidth) => FixedFrameVerticalBorderWidth,
            nameof(FlatMenu) => FlatMenu,
            nameof(FocusBorderHeight) => FocusBorderHeight,
            nameof(FocusBorderWidth) => FocusBorderWidth,
            nameof(FocusHorizontalBorderHeight) => FocusHorizontalBorderHeight,
            nameof(FocusVerticalBorderWidth) => FocusVerticalBorderWidth,
            "FocusVisualStyle" => s_focusVisualStyle,
            nameof(ForegroundFlashCount) => ForegroundFlashCount,
            nameof(FullPrimaryScreenHeight) => FullPrimaryScreenHeight,
            nameof(FullPrimaryScreenWidth) => FullPrimaryScreenWidth,
            nameof(GradientCaptions) => GradientCaptions,
            nameof(HighContrast) => HighContrast,
            nameof(HorizontalScrollBarButtonWidth) => HorizontalScrollBarButtonWidth,
            nameof(HorizontalScrollBarHeight) => HorizontalScrollBarHeight,
            nameof(HorizontalScrollBarThumbWidth) => HorizontalScrollBarThumbWidth,
            nameof(HotTracking) => HotTracking,
            nameof(IconGridHeight) => IconGridHeight,
            nameof(IconGridWidth) => IconGridWidth,
            nameof(IconHeight) => IconHeight,
            nameof(IconHorizontalSpacing) => IconHorizontalSpacing,
            nameof(IconTitleWrap) => IconTitleWrap,
            nameof(IconVerticalSpacing) => IconVerticalSpacing,
            nameof(IconWidth) => IconWidth,
            nameof(IsImmEnabled) => IsImmEnabled,
            nameof(IsMediaCenter) => IsMediaCenter,
            nameof(IsMenuDropRightAligned) => IsMenuDropRightAligned,
            nameof(IsMiddleEastEnabled) => IsMiddleEastEnabled,
            nameof(IsMousePresent) => IsMousePresent,
            nameof(IsMouseWheelPresent) => IsMouseWheelPresent,
            nameof(IsPenWindows) => IsPenWindows,
            nameof(IsRemoteSession) => IsRemoteSession,
            nameof(IsRemotelyControlled) => IsRemotelyControlled,
            nameof(IsSlowMachine) => IsSlowMachine,
            nameof(IsTabletPC) => IsTabletPC,
            nameof(KanjiWindowHeight) => KanjiWindowHeight,
            nameof(KeyboardCues) => KeyboardCues,
            nameof(KeyboardDelay) => KeyboardDelay,
            nameof(KeyboardPreference) => KeyboardPreference,
            nameof(KeyboardSpeed) => KeyboardSpeed,
            nameof(ListBoxSmoothScrolling) => ListBoxSmoothScrolling,
            nameof(MaximizedPrimaryScreenHeight) => MaximizedPrimaryScreenHeight,
            nameof(MaximizedPrimaryScreenWidth) => MaximizedPrimaryScreenWidth,
            nameof(MaximumWindowTrackHeight) => MaximumWindowTrackHeight,
            nameof(MaximumWindowTrackWidth) => MaximumWindowTrackWidth,
            nameof(MenuAnimation) => MenuAnimation,
            nameof(MenuBarHeight) => MenuBarHeight,
            nameof(MenuButtonHeight) => MenuButtonHeight,
            nameof(MenuButtonWidth) => MenuButtonWidth,
            nameof(MenuCheckmarkHeight) => MenuCheckmarkHeight,
            nameof(MenuCheckmarkWidth) => MenuCheckmarkWidth,
            nameof(MenuDropAlignment) => MenuDropAlignment,
            nameof(MenuFade) => MenuFade,
            nameof(MenuHeight) => MenuHeight,
            nameof(MenuPopupAnimation) => MenuPopupAnimation,
            nameof(MenuShowDelay) => MenuShowDelay,
            nameof(MenuWidth) => MenuWidth,
            nameof(MinimizeAnimation) => MinimizeAnimation,
            nameof(MinimizedGridHeight) => MinimizedGridHeight,
            nameof(MinimizedGridWidth) => MinimizedGridWidth,
            nameof(MinimizedWindowHeight) => MinimizedWindowHeight,
            nameof(MinimizedWindowWidth) => MinimizedWindowWidth,
            nameof(MinimumWindowHeight) => MinimumWindowHeight,
            nameof(MinimumWindowTrackHeight) => MinimumWindowTrackHeight,
            nameof(MinimumWindowTrackWidth) => MinimumWindowTrackWidth,
            nameof(MinimumWindowWidth) => MinimumWindowWidth,
            nameof(MouseHoverHeight) => MouseHoverHeight,
            nameof(MouseHoverTime) => MouseHoverTime,
            nameof(MouseHoverWidth) => MouseHoverWidth,
            "NavigationChromeDownLevelStyle" => s_navigationChromeDownLevelStyle,
            "NavigationChromeStyle" => s_navigationChromeStyle,
            nameof(PowerLineStatus) => PowerLineStatus,
            nameof(PrimaryScreenHeight) => PrimaryScreenHeight,
            nameof(PrimaryScreenWidth) => PrimaryScreenWidth,
            nameof(ResizeFrameHorizontalBorderHeight) => ResizeFrameHorizontalBorderHeight,
            nameof(ResizeFrameVerticalBorderWidth) => ResizeFrameVerticalBorderWidth,
            nameof(ScrollHeight) => ScrollHeight,
            nameof(ScrollWidth) => ScrollWidth,
            nameof(SelectionFade) => SelectionFade,
            nameof(ShowSounds) => ShowSounds,
            nameof(SmallCaptionHeight) => SmallCaptionHeight,
            nameof(SmallCaptionWidth) => SmallCaptionWidth,
            nameof(SmallIconHeight) => SmallIconHeight,
            nameof(SmallIconWidth) => SmallIconWidth,
            nameof(SmallWindowCaptionButtonHeight) => SmallWindowCaptionButtonHeight,
            nameof(SmallWindowCaptionButtonWidth) => SmallWindowCaptionButtonWidth,
            nameof(SnapToDefaultButton) => SnapToDefaultButton,
            nameof(StylusHotTracking) => StylusHotTracking,
            nameof(SwapButtons) => SwapButtons,
            nameof(ThickHorizontalBorderHeight) => ThickHorizontalBorderHeight,
            nameof(ThickVerticalBorderWidth) => ThickVerticalBorderWidth,
            nameof(ThinHorizontalBorderHeight) => ThinHorizontalBorderHeight,
            nameof(ThinVerticalBorderWidth) => ThinVerticalBorderWidth,
            nameof(ToolTipAnimation) => ToolTipAnimation,
            nameof(ToolTipFade) => ToolTipFade,
            nameof(ToolTipPopupAnimation) => ToolTipPopupAnimation,
            nameof(UIEffects) => UIEffects,
            nameof(VerticalScrollBarButtonHeight) => VerticalScrollBarButtonHeight,
            nameof(VerticalScrollBarThumbHeight) => VerticalScrollBarThumbHeight,
            nameof(VerticalScrollBarWidth) => VerticalScrollBarWidth,
            nameof(VirtualScreenHeight) => VirtualScreenHeight,
            nameof(VirtualScreenLeft) => VirtualScreenLeft,
            nameof(VirtualScreenTop) => VirtualScreenTop,
            nameof(VirtualScreenWidth) => VirtualScreenWidth,
            nameof(WheelScrollLines) => WheelScrollLines,
            nameof(WindowCaptionButtonHeight) => WindowCaptionButtonHeight,
            nameof(WindowCaptionButtonWidth) => WindowCaptionButtonWidth,
            nameof(WindowCaptionHeight) => WindowCaptionHeight,
            nameof(WorkArea) => WorkArea,
            _ => DependencyProperty.UnsetValue,
        };

        if (ReferenceEquals(resource, DependencyProperty.UnsetValue))
        {
            resource = null;
            return false;
        }

        return true;
    }

    private static ResourceKey GetResourceKey(string resourceId)
    {
        lock (s_resourceLock)
        {
            if (!s_resourceKeys.TryGetValue(resourceId, out var key))
            {
                key = new ComponentResourceKey(typeof(SystemParameters), resourceId);
                s_resourceKeys.Add(resourceId, key);
            }

            return key;
        }
    }
}
