namespace Jalium.UI.Controls.Primitives;

/// <summary>Provides logical or physical scrolling metrics and operations.</summary>
public interface IScrollInfo
{
    bool CanHorizontallyScroll { get; set; }
    bool CanVerticallyScroll { get; set; }
    double ExtentWidth { get; }
    double ExtentHeight { get; }
    double ViewportWidth { get; }
    double ViewportHeight { get; }
    double HorizontalOffset { get; }
    double VerticalOffset { get; }
    ScrollViewer? ScrollOwner { get; set; }
    void LineUp();
    void LineDown();
    void LineLeft();
    void LineRight();
    void PageUp();
    void PageDown();
    void PageLeft();
    void PageRight();
    void MouseWheelUp();
    void MouseWheelDown();
    void MouseWheelLeft();
    void MouseWheelRight();
    void SetHorizontalOffset(double offset);
    void SetVerticalOffset(double offset);
    Rect MakeVisible(Visual visual, Rect rectangle);
}
