using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;
using Jalium.UI.Markup;

namespace Jalium.UI.Controls;

/// <summary>
/// Renders the contained 3-D content within the 2-D layout bounds of this element.
/// </summary>
[ContentProperty(nameof(Children))]
public class Viewport3D : FrameworkElement, IAddChild
{
    private readonly Viewport3DVisual _viewport3DVisual;

    /// <summary>
    /// Initializes a new viewport and its 2-D/3-D bridge visual.
    /// </summary>
    public Viewport3D()
    {
        _viewport3DVisual = new Viewport3DVisual();
        AddVisualChild(_viewport3DVisual);
        SetValue(ChildrenPropertyKey, _viewport3DVisual.Children);
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.Viewport3DAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Camera dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CameraProperty =
        DependencyProperty.Register(nameof(Camera), typeof(Camera), typeof(Viewport3D),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    private static readonly DependencyPropertyKey ChildrenPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Children),
            typeof(Visual3DCollection),
            typeof(Viewport3D),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the read-only <see cref="Children"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ChildrenProperty = ChildrenPropertyKey.DependencyProperty;

    #endregion

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
    public Visual3DCollection Children =>
        (Visual3DCollection)GetValue(ChildrenProperty)!;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Viewport3D viewport)
        {
            if (e.Property == CameraProperty)
            {
                viewport._viewport3DVisual.Camera = (Camera?)e.NewValue;
            }

            viewport.InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport3DVisual.Viewport = new Rect(0, 0, finalSize.Width, finalSize.Height);
        return finalSize;
    }

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Visual3D child)
        {
            throw new ArgumentException(
                $"{nameof(Viewport3D)} children must derive from {nameof(Visual3D)}.",
                nameof(value));
        }

        Children.Add(child);
    }

    void IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(static character => !char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException($"{nameof(Viewport3D)} does not accept text content.");
        }
    }

}
