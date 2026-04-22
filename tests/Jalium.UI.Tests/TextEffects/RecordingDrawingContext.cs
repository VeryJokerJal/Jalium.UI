using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests.TextEffects;

/// <summary>
/// Minimal <see cref="DrawingContext"/> that records every public call so unit
/// tests can assert on ordering and arguments without a real GPU render target.
/// Only records events relevant to <see cref="Controls.TextEffects.TextEffectPresenter"/>
/// — DrawText, PushEffect/PopEffect, PushTransform/PushOpacity/Pop.
/// </summary>
internal sealed class RecordingDrawingContext : DrawingContext
{
    public List<string> Events { get; } = new();
    public List<IEffect> PushedEffects { get; } = new();
    public int PushEffectCount => PushedEffects.Count;
    public int PopEffectCount { get; private set; }
    public int DrawTextCount { get; private set; }

    public override void PushEffect(IEffect effect, Rect captureBounds)
    {
        PushedEffects.Add(effect);
        Events.Add($"PushEffect(type={effect.GetType().Name}, bounds={captureBounds})");
    }

    public override void PopEffect()
    {
        PopEffectCount++;
        Events.Add("PopEffect");
    }

    public override void DrawText(FormattedText formattedText, Point origin)
    {
        DrawTextCount++;
        Events.Add($"DrawText('{formattedText.Text}')");
    }

    public override void PushTransform(Transform transform)
    {
        Events.Add($"PushTransform({transform.GetType().Name})");
    }

    public override void PushClip(Geometry clipGeometry)
    {
        Events.Add($"PushClip({clipGeometry.GetType().Name})");
    }

    public override void PushOpacity(double opacity)
    {
        Events.Add($"PushOpacity({opacity:F2})");
    }

    public override void Pop()
    {
        Events.Add("Pop");
    }

    public override void Close() { }

    // All the draw-shape methods collapse to tagged event records — we only
    // care about order & types for the PushEffect tests, not pixel output.
    public override void DrawLine(Pen pen, Point p0, Point p1) => Events.Add("DrawLine");
    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) => Events.Add("DrawRectangle");
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double rx, double ry) => Events.Add("DrawRoundedRectangle");
    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double rx, double ry) => Events.Add("DrawEllipse");
    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Events.Add("DrawGeometry");
    public override void DrawImage(ImageSource imageSource, Rect rectangle) => Events.Add("DrawImage");
    public override void DrawBackdropEffect(Rect rectangle, Jalium.UI.IBackdropEffect effect, CornerRadius cornerRadius) => Events.Add("DrawBackdropEffect");
}
