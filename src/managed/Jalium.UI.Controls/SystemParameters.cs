namespace Jalium.UI;

/// <summary>
/// Contains properties that you can use to query system settings.
/// </summary>
public static class SystemParameters
{
    // Window metrics
    public static double BorderWidth => 1.0;
    public static double CaptionHeight => 22.0;
    public static double CaptionWidth => 22.0;
    public static Thickness WindowResizeBorderThickness => new Thickness(4);
    public static Thickness WindowNonClientFrameThickness => new Thickness(8, 30, 8, 8);

    // UI element sizes
    public static double SmallIconWidth => 16.0;
    public static double SmallIconHeight => 16.0;
    public static double IconWidth => 32.0;
    public static double IconHeight => 32.0;
    public static double MenuBarHeight => 20.0;
    public static double ScrollWidth => 17.0;
    public static double ScrollHeight => 17.0;
    public static double HorizontalScrollBarButtonWidth => 17.0;
    public static double VerticalScrollBarButtonHeight => 17.0;
    public static double HorizontalScrollBarHeight => 17.0;
    public static double VerticalScrollBarWidth => 17.0;
    public static double HorizontalScrollBarThumbWidth => 8.0;
    public static double VerticalScrollBarThumbHeight => 8.0;

    // Cursor sizes
    public static double CursorWidth => 32.0;
    public static double CursorHeight => 32.0;

    // Mouse settings
    public static int DoubleClickTime => 500;
    public static int MouseHoverTime => 400;
    public static double MouseHoverWidth => 4.0;
    public static double MouseHoverHeight => 4.0;

    // Drag settings
    public static double MinimumHorizontalDragDistance => 4.0;
    public static double MinimumVerticalDragDistance => 4.0;

    // Screen
    public static double PrimaryScreenWidth => 1920.0;
    public static double PrimaryScreenHeight => 1080.0;
    public static double VirtualScreenWidth => 1920.0;
    public static double VirtualScreenHeight => 1080.0;
    public static double VirtualScreenLeft => 0.0;
    public static double VirtualScreenTop => 0.0;
    public static Rect WorkArea => new Rect(0, 0, 1920, 1040);
    public static bool IsTabletPC => false;

    // Visual effects
    public static bool ClientAreaAnimation => true;
    public static bool DropShadow => true;
    public static bool FlatMenu => true;
    public static int ForegroundFlashCount => 3;
    public static bool GradientCaptions => true;
    public static bool HighContrast => false;
    public static bool MenuAnimation => true;
    public static bool MenuDropAlignment => false;
    public static bool SelectionFade => true;
    public static bool StylusHotTracking => true;
    public static bool ToolTipAnimation => true;
    public static bool UIEffects => true;

    // Focus
    public static int CaretWidth => 1;

    // Theme info
    public static bool IsGlassEnabled => true;

    // Caret blink rate
    public static int CaretBlinkTime => 530;

    // Wheel scroll lines
    public static int WheelScrollLines => 3;

    // Power
    public static bool PowerLineStatus => true; // AC power
}
