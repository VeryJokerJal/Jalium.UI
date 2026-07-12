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

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(AdornedElement.RenderSize);
        dc.DrawRectangle(null, _errorPen, rect);
    }
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
