using Jalium.UI.Media;

namespace Jalium.UI.Tests.TestHelpers;

/// <summary>
/// Minimal <see cref="DrawingContext"/> stub used by tests that exercise
/// <see cref="Visual.Render"/> bookkeeping (tick stamps, observer hooks,
/// dirty-flag transitions, idle reclaimer tracking) without exercising any
/// real drawing. All draw / push / pop calls are no-ops; pattern matches
/// against extra interfaces (offset, clip bounds, etc.) miss by design,
/// which keeps the visual tree on the legacy immediate-mode dispatch path.
/// </summary>
internal sealed class StubDrawingContext : DrawingContextAdapter
{
    public override void DrawLine(Pen pen, Point point0, Point point1) { }
    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
    public override void DrawText(FormattedText formattedText, Point origin) { }
    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
    public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
    public override void PushTransform(Transform transform) { }
    public override void PushClip(Geometry clipGeometry) { }
    public override void PushOpacity(double opacity) { }
    public override void Pop() { }
    public override void Close() { }
}
