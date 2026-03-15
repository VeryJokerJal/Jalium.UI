using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Controls;

/// <summary>
/// Renders the contained 3-D content within the 2-D layout bounds of this element.
/// </summary>
public class Viewport3D : FrameworkElement
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.Viewport3DAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Camera dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CameraProperty =
        DependencyProperty.Register(nameof(Camera), typeof(Camera), typeof(Viewport3D),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    private readonly List<Visual3D> _children = new();

    /// <summary>
    /// Gets or sets the Camera used to view the 3-D content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Camera? Camera
    {
        get => (Camera?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>
    /// Gets the collection of Visual3D children.
    /// </summary>
    public IList<Visual3D> Children => _children;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Viewport3D viewport)
            viewport.InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

}
