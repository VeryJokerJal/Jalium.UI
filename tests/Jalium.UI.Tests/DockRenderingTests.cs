using System.Collections.Generic;
using System.Linq;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DockRenderingTests
{
    [Fact]
    public void DockItem_SelectedTab_ShouldUseRoundedTopCornersByDefault()
    {
        var item = new DockItem
        {
            IsSelected = true,
            CanClose = false,
            Width = 96,
            Height = 28
        };

        item.Measure(new Size(96, 28));
        item.Arrange(new Rect(0, 0, 96, 28));

        var drawingContext = new RecordingDrawingContext();
        item.Render(drawingContext);

        var fillGeometryCall = Assert.Single(drawingContext.GeometryCalls,
            call => call.Brush != null && call.Pen == null);
        var fillGeometry = Assert.IsType<PathGeometry>(fillGeometryCall.Geometry);
        var figure = Assert.Single(fillGeometry.Figures);

        Assert.InRange(figure.StartPoint.X, 7.9, 8.1);
        Assert.InRange(figure.StartPoint.Y, -0.1, 0.1);
    }

    [Fact]
    public void DockItem_FirstSelectedTab_ShouldUseSquareTopLeftCorner()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel();
        var first = new DockItem { CanClose = false };
        var second = new DockItem { CanClose = false };

        panel.Items.Add(first);
        panel.Items.Add(second);
        panel.SelectedIndex = 0;

        host.Children.Add(panel);
        host.UpdateLayoutPass(new Size(260, 180));

        var drawingContext = new RecordingDrawingContext();
        first.Render(drawingContext);

        var fillGeometryCall = Assert.Single(drawingContext.GeometryCalls,
            call => call.Brush != null && call.Pen == null);
        var fillGeometry = Assert.IsType<PathGeometry>(fillGeometryCall.Geometry);
        var figure = Assert.Single(fillGeometry.Figures);

        Assert.InRange(figure.StartPoint.X, -0.1, 0.1);
        Assert.InRange(figure.StartPoint.Y, -0.1, 0.1);
    }

    [Fact]
    public void DockTabPanel_SelectedTabBorder_ShouldUseSofterRoundedJoinCurves()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel();
        var first = new DockItem { CanClose = false };
        var second = new DockItem { CanClose = false };

        panel.Items.Add(first);
        panel.Items.Add(second);
        panel.SelectedIndex = 1;

        host.Children.Add(panel);
        host.UpdateLayoutPass(new Size(260, 180));

        var drawingContext = new RecordingDrawingContext();
        panel.Render(drawingContext);

        var contentBackground = Assert.Single(drawingContext.RoundedRectCalls,
            call => call.Brush != null && call.Pen == null);
        Assert.InRange(contentBackground.RadiusX, 7.9, 8.1);
        Assert.InRange(contentBackground.RadiusY, 7.9, 8.1);

        var outerBorder = Assert.Single(drawingContext.RoundedRectCalls,
            call => call.Brush == null && call.Pen != null);
        Assert.InRange(outerBorder.RadiusX, 7.9, 8.1);
        Assert.InRange(outerBorder.RadiusY, 7.9, 8.1);

        var borderGeometryCall = Assert.Single(drawingContext.GeometryCalls,
            call => call.Brush == null && call.Pen != null);
        var borderGeometry = Assert.IsType<PathGeometry>(borderGeometryCall.Geometry);
        var figure = Assert.Single(borderGeometry.Figures);
        var bezierSegments = figure.Segments.OfType<BezierSegment>().ToList();

        var selectedPosition = second.TransformToAncestor(panel);
        var topY = panel.TabStripHeight;
        var expectedLeftJoinEnd = new Point(selectedPosition.X, topY - 6);
        var expectedRightJoinEnd = new Point(selectedPosition.X + second.ActualWidth + 6, topY);

        Assert.Contains(bezierSegments, segment => PointsAreClose(segment.Point3, expectedLeftJoinEnd));
        Assert.Contains(bezierSegments, segment => PointsAreClose(segment.Point3, expectedRightJoinEnd));
    }

    [Fact]
    public void DockTabPanel_FirstSelectedTab_ShouldKeepTopLeftCornerSquare()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel();
        var first = new DockItem { CanClose = false };
        var second = new DockItem { CanClose = false };

        panel.Items.Add(first);
        panel.Items.Add(second);
        panel.SelectedIndex = 0;

        host.Children.Add(panel);
        host.UpdateLayoutPass(new Size(260, 180));

        var drawingContext = new RecordingDrawingContext();
        panel.Render(drawingContext);

        Assert.Contains(drawingContext.LineCalls, call =>
            PointsAreClose(call.Start, new Point(0.5, panel.TabStripHeight)) &&
            Math.Abs(call.End.X - 0.5) < 0.1);

        var borderGeometryCall = Assert.Single(drawingContext.GeometryCalls,
            call => call.Brush == null && call.Pen != null);
        var borderGeometry = Assert.IsType<PathGeometry>(borderGeometryCall.Geometry);
        var figure = Assert.Single(borderGeometry.Figures);
        Assert.True(figure.StartPoint.X < 0.6);
    }

    private static bool PointsAreClose(Point actual, Point expected)
    {
        return Math.Abs(actual.X - expected.X) < 0.1
            && Math.Abs(actual.Y - expected.Y) < 0.1;
    }

    private sealed class LayoutHostPanel : Panel, ILayoutManagerHost
    {
        private readonly LayoutManager _layoutManager = new();

        LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

        public void UpdateLayoutPass(Size availableSize)
        {
            _layoutManager.UpdateLayout(this, availableSize);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                    child.Measure(availableSize);
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                    child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            }

            return finalSize;
        }
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public List<RoundedRectCall> RoundedRectCalls { get; } = new();
        public List<GeometryCall> GeometryCalls { get; } = new();
        public List<LineCall> LineCalls { get; } = new();

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
            LineCalls.Add(new LineCall(pen, point0, point1));
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            RoundedRectCalls.Add(new RoundedRectCall(brush, pen, rectangle, radiusX, radiusY));
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            GeometryCalls.Add(new GeometryCall(brush, pen, geometry));
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }

    private sealed record RoundedRectCall(Brush? Brush, Pen? Pen, Rect Rectangle, double RadiusX, double RadiusY);

    private sealed record GeometryCall(Brush? Brush, Pen? Pen, Geometry Geometry);

    private sealed record LineCall(Pen Pen, Point Start, Point End);
}
