using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssObjectFit { Unspecified, Fill, Contain, Cover, None, ScaleDown }

/// <summary>CSS sizing and positioning of replaced image and video content.</summary>
internal static class CssImageObjectProperties
{
    internal static readonly DependencyProperty FitProperty = DependencyProperty.RegisterAttached(
        "CssObjectFit", typeof(CssObjectFit), typeof(CssImageObjectProperties),
        new PropertyMetadata(CssObjectFit.Unspecified, OnChanged));

    internal static readonly DependencyProperty PositionProperty = DependencyProperty.RegisterAttached(
        "CssObjectPosition", typeof(CssBackgroundPosition), typeof(CssImageObjectProperties),
        new PropertyMetadata(new CssBackgroundPosition(
            new(new CssLength(50, CssUnit.Percent)),
            new(new CssLength(50, CssUnit.Percent)), CssLengthContext.Default), OnChanged));

    internal static CssObjectFit Fit(DependencyObject element)
        => element.GetValue(FitProperty) is CssObjectFit value ? value : CssObjectFit.Unspecified;

    internal static CssBackgroundPosition Position(DependencyObject element)
        => (CssBackgroundPosition)element.GetValue(PositionProperty)!;

    internal static bool HasPosition(DependencyObject element)
        => element.GetEffectiveValueLayer(PositionProperty) is not null;

    internal static Size ConcreteSize(Size natural, Size box, CssObjectFit fit)
    {
        return box.Width <= 0 || box.Height <= 0 ? default
            : CssImageIntrinsicSize.FromSize(natural).ObjectSize(box, fit);
    }

    internal static Rect PositionedRect(DependencyObject element, Rect content, Size concrete)
    {
        var position = Position(element);
        var x = content.X + position.X.Resolve(content.Width - concrete.Width, position.Lengths);
        var y = content.Y + position.Y.Resolve(content.Height - concrete.Height, position.Lengths);
        return double.IsFinite(x) && double.IsFinite(y)
            ? new Rect(x, y, concrete.Width, concrete.Height) : Rect.Empty;
    }

    internal static bool NeedsContentClip(DependencyObject element, CssObjectFit fit, Rect content, Size outer)
        => fit != CssObjectFit.Unspecified || HasPosition(element) ||
           content.X > 0 || content.Y > 0 || content.Right < outer.Width || content.Bottom < outer.Height;

    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        if (target is not Image && target is not MediaElement && !CssReplacedContent.IsActive(target) && target is not CssGeneratedText { IsImageReplacement: true }) return;
        var element = (FrameworkElement)target;
        if (change.Property == FitProperty) element.InvalidateMeasure();
        else if (CssReplacedContent.IsActive(element) || element is CssGeneratedText { IsImageReplacement: true }) element.InvalidateArrange();
        element.InvalidateVisual();
    }
}
