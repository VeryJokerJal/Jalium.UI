using Jalium.UI.Documents;
using Jalium.UI.Media;

using System.Runtime.CompilerServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Adorner that displays a red border around elements with validation errors.
/// </summary>
internal sealed class ValidationErrorAdorner : Adorner
{
    private const double ValidationErrorBorderWidth = 1.5;
    private static readonly Pen _errorPen = new(new SolidColorBrush(Color.FromRgb(255, 0, 0)), ValidationErrorBorderWidth);

    public ValidationErrorAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;

        var rect = new Rect(AdornedElement.RenderSize);
        dc.DrawRectangle(null, _errorPen, rect);
    }
}

/// <summary>
/// Represents the result of a hit test on an adorner layer.
/// Provides information about which adorner was hit in addition to the visual and point data.
/// </summary>
public sealed class AdornerHitTestResult : Jalium.UI.Media.PointHitTestResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdornerHitTestResult"/> class.
    /// </summary>
    /// <param name="visual">The visual that was hit.</param>
    /// <param name="pt">The point that was hit, in the visual's coordinate space.</param>
    /// <param name="adorner">The adorner that was hit.</param>
    internal AdornerHitTestResult(Visual visual, Point pt, Adorner adorner)
        : base(visual, pt)
    {
        Adorner = adorner;
    }

    /// <summary>
    /// Gets the adorner that was hit.
    /// </summary>
    public Adorner Adorner { get; }
}

/// <summary>
/// Registers the validation adorner handler. Called during application startup
/// to connect the base Validation class with the adorner infrastructure.
/// </summary>
internal static class ValidationAdornerIntegration
{
    private static readonly ConditionalWeakTable<DependencyObject, ValidationErrorAdorner> _adorners = new();

    internal static void Initialize()
    {
        Validation.AdornerHandler = HandleValidationAdorner;
    }

    private static void HandleValidationAdorner(DependencyObject element, bool hasError)
    {
        if (element is not UIElement uiElement) return;

        if (hasError)
        {
            if (_adorners.TryGetValue(element, out _)) return;

            var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
            if (adornerLayer != null)
            {
                var adorner = new ValidationErrorAdorner(uiElement);
                adornerLayer.Add(adorner);
                _adorners.Add(element, adorner);
            }
        }
        else
        {
            if (_adorners.TryGetValue(element, out var adorner))
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
                adornerLayer?.Remove(adorner);
                _adorners.Remove(element);
            }
        }
    }
}
