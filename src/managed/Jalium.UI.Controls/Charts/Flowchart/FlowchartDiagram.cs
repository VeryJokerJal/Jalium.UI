using System.Collections.ObjectModel;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Renders a directed flowchart: a set of <see cref="FlowchartNode"/> boxes connected by
/// directed <see cref="FlowchartEdge"/> arrows, laid out automatically using a layered
/// (Sugiyama-style) algorithm. The control understands the same node shapes and edge
/// styles as mermaid flowcharts and can be populated directly or via
/// <see cref="MermaidDiagram"/>.
/// </summary>
public class FlowchartDiagram : ChartBase
{
    #region Defaults

    private static readonly SolidColorBrush s_defaultNodeFill = new(Color.FromRgb(0xEC, 0xEC, 0xFF));
    private static readonly SolidColorBrush s_defaultNodeBorder = new(Color.FromRgb(0x93, 0x70, 0xDB));
    private static readonly SolidColorBrush s_defaultNodeForeground = new(Color.FromRgb(0x1F, 0x23, 0x30));
    private static readonly SolidColorBrush s_defaultEdgeBrush = new(Color.FromRgb(0x55, 0x5A, 0x66));
    private static readonly SolidColorBrush s_defaultEdgeLabelBackground = new(Color.FromArgb(235, 0xFF, 0xFF, 0xFF));

    #endregion

    #region Private State

    private bool _layoutDirty = true;
    private double _contentWidth;
    private double _contentHeight;

    #endregion

    #region Dependency Properties

    /// <summary>Identifies the <see cref="Nodes"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(nameof(Nodes), typeof(ObservableCollection<FlowchartNode>), typeof(FlowchartDiagram),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="Edges"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty EdgesProperty =
        DependencyProperty.Register(nameof(Edges), typeof(ObservableCollection<FlowchartEdge>), typeof(FlowchartDiagram),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="Direction"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(nameof(Direction), typeof(FlowchartDirection), typeof(FlowchartDiagram),
            new PropertyMetadata(FlowchartDirection.TopToBottom, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="NodeFill"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeFillProperty =
        DependencyProperty.Register(nameof(NodeFill), typeof(Brush), typeof(FlowchartDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="NodeBorderBrush"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeBorderBrushProperty =
        DependencyProperty.Register(nameof(NodeBorderBrush), typeof(Brush), typeof(FlowchartDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="NodeForeground"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeForegroundProperty =
        DependencyProperty.Register(nameof(NodeForeground), typeof(Brush), typeof(FlowchartDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="EdgeBrush"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty EdgeBrushProperty =
        DependencyProperty.Register(nameof(EdgeBrush), typeof(Brush), typeof(FlowchartDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="NodeFontSize"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty NodeFontSizeProperty =
        DependencyProperty.Register(nameof(NodeFontSize), typeof(double), typeof(FlowchartDiagram),
            new PropertyMetadata(13.0, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="RankSpacing"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RankSpacingProperty =
        DependencyProperty.Register(nameof(RankSpacing), typeof(double), typeof(FlowchartDiagram),
            new PropertyMetadata(50.0, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="NodeSpacing"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty NodeSpacingProperty =
        DependencyProperty.Register(nameof(NodeSpacing), typeof(double), typeof(FlowchartDiagram),
            new PropertyMetadata(28.0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>Gets or sets the nodes of the flowchart.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<FlowchartNode> Nodes
    {
        get
        {
            var n = (ObservableCollection<FlowchartNode>?)GetValue(NodesProperty);
            if (n == null)
            {
                n = new ObservableCollection<FlowchartNode>();
                SetValue(NodesProperty, n);
            }
            return n;
        }
        set => SetValue(NodesProperty, value);
    }

    /// <summary>Gets or sets the edges of the flowchart.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<FlowchartEdge> Edges
    {
        get
        {
            var e = (ObservableCollection<FlowchartEdge>?)GetValue(EdgesProperty);
            if (e == null)
            {
                e = new ObservableCollection<FlowchartEdge>();
                SetValue(EdgesProperty, e);
            }
            return e;
        }
        set => SetValue(EdgesProperty, value);
    }

    /// <summary>Gets or sets the primary layout direction.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FlowchartDirection Direction
    {
        get => (FlowchartDirection)GetValue(DirectionProperty)!;
        set => SetValue(DirectionProperty, value);
    }

    /// <summary>Gets or sets the default fill brush for nodes.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NodeFill
    {
        get => (Brush?)GetValue(NodeFillProperty);
        set => SetValue(NodeFillProperty, value);
    }

    /// <summary>Gets or sets the default outline brush for nodes.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NodeBorderBrush
    {
        get => (Brush?)GetValue(NodeBorderBrushProperty);
        set => SetValue(NodeBorderBrushProperty, value);
    }

    /// <summary>Gets or sets the brush used for node label text.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NodeForeground
    {
        get => (Brush?)GetValue(NodeForegroundProperty);
        set => SetValue(NodeForegroundProperty, value);
    }

    /// <summary>Gets or sets the default brush used for edges and arrow heads.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? EdgeBrush
    {
        get => (Brush?)GetValue(EdgeBrushProperty);
        set => SetValue(EdgeBrushProperty, value);
    }

    /// <summary>Gets or sets the font size used for node labels.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double NodeFontSize
    {
        get => (double)GetValue(NodeFontSizeProperty)!;
        set => SetValue(NodeFontSizeProperty, value);
    }

    /// <summary>Gets or sets the spacing between adjacent ranks (layers).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double RankSpacing
    {
        get => (double)GetValue(RankSpacingProperty)!;
        set => SetValue(RankSpacingProperty, value);
    }

    /// <summary>Gets or sets the spacing between nodes within the same rank.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double NodeSpacing
    {
        get => (double)GetValue(NodeSpacingProperty)!;
        set => SetValue(NodeSpacingProperty, value);
    }

    #endregion

    #region Construction

    /// <summary>Initializes a new instance of the <see cref="FlowchartDiagram"/> class.</summary>
    public FlowchartDiagram()
    {
        IsLegendVisible = false;
        IsTooltipEnabled = false;
        PlotAreaMargin = new Thickness(10);
    }

    #endregion

    #region Property changed

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowchartDiagram f)
        {
            f._layoutDirty = true;
            f.InvalidateMeasure();
            f.InvalidateVisual();
        }
    }

    #endregion

    #region Layout

    private void EnsureLayout()
    {
        if (!_layoutDirty)
        {
            return;
        }

        _layoutDirty = false;
        _contentWidth = 0;
        _contentHeight = 0;

        var nodes = (ObservableCollection<FlowchartNode>?)GetValue(NodesProperty);
        if (nodes == null || nodes.Count == 0)
        {
            return;
        }

        var edges = (ObservableCollection<FlowchartEdge>?)GetValue(EdgesProperty);
        var fontFamily = ResolveFontFamily();

        // 1. Measure every node.
        foreach (var node in nodes)
        {
            MeasureNode(node, fontFamily);
        }

        // 2. Assign ranks (longest-path layering with a cycle guard).
        var rankOf = ComputeRanks(nodes, edges);

        // 3. Group nodes by rank preserving first-seen order.
        var maxRank = 0;
        foreach (var r in rankOf.Values)
        {
            if (r > maxRank) maxRank = r;
        }

        var ranks = new List<List<FlowchartNode>>(maxRank + 1);
        for (var i = 0; i <= maxRank; i++)
        {
            ranks.Add(new List<FlowchartNode>());
        }
        foreach (var node in nodes)
        {
            ranks[rankOf[node.Id]].Add(node);
        }

        // 4. Position nodes along major (rank) and minor (within rank) axes.
        var vertical = Direction is FlowchartDirection.TopToBottom or FlowchartDirection.BottomToTop;
        var rankSpacing = RankSpacing;
        var nodeSpacing = NodeSpacing;

        // Major extent per rank = max of the node's major-axis size.
        var bandMajor = new double[ranks.Count];
        var rankMinorTotal = new double[ranks.Count];
        double contentMinor = 0;
        for (var i = 0; i < ranks.Count; i++)
        {
            double band = 0;
            double minorTotal = 0;
            var list = ranks[i];
            for (var j = 0; j < list.Count; j++)
            {
                var node = list[j];
                var major = vertical ? node.Height : node.Width;
                var minor = vertical ? node.Width : node.Height;
                band = Math.Max(band, major);
                minorTotal += minor;
                if (j < list.Count - 1) minorTotal += nodeSpacing;
            }
            bandMajor[i] = band;
            rankMinorTotal[i] = minorTotal;
            contentMinor = Math.Max(contentMinor, minorTotal);
        }

        double contentMajor = 0;
        for (var i = 0; i < ranks.Count; i++)
        {
            contentMajor += bandMajor[i];
            if (i < ranks.Count - 1) contentMajor += rankSpacing;
        }

        // Walk the ranks assigning centers.
        double majorCursor = 0;
        for (var i = 0; i < ranks.Count; i++)
        {
            var list = ranks[i];
            var majorCenter = majorCursor + bandMajor[i] / 2.0;
            var start = (contentMinor - rankMinorTotal[i]) / 2.0;
            double minorCursor = start;

            foreach (var node in list)
            {
                var minorSize = vertical ? node.Width : node.Height;
                var minorCenter = minorCursor + minorSize / 2.0;
                minorCursor += minorSize + nodeSpacing;

                var mjr = majorCenter;
                // Reverse the major axis for bottom-to-top / right-to-left.
                if (Direction is FlowchartDirection.BottomToTop or FlowchartDirection.RightToLeft)
                {
                    mjr = contentMajor - majorCenter;
                }

                double cx, cy;
                if (vertical)
                {
                    cx = minorCenter;
                    cy = mjr;
                }
                else
                {
                    cx = mjr;
                    cy = minorCenter;
                }

                node.X = cx - node.Width / 2.0;
                node.Y = cy - node.Height / 2.0;
                node.Rank = i;
            }

            majorCursor += bandMajor[i] + rankSpacing;
        }

        _contentWidth = vertical ? contentMinor : contentMajor;
        _contentHeight = vertical ? contentMajor : contentMinor;
    }

    private Dictionary<string, int> ComputeRanks(
        ObservableCollection<FlowchartNode> nodes,
        ObservableCollection<FlowchartEdge>? edges)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            inDegree[node.Id] = 0;
            children[node.Id] = new List<string>();
        }

        if (edges != null)
        {
            foreach (var edge in edges)
            {
                if (!children.ContainsKey(edge.SourceId) || !inDegree.ContainsKey(edge.TargetId))
                {
                    continue;
                }
                if (edge.SourceId == edge.TargetId)
                {
                    continue; // self-loop does not affect ranking
                }
                children[edge.SourceId].Add(edge.TargetId);
                inDegree[edge.TargetId]++;
            }
        }

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var node in nodes)
        {
            if (inDegree[node.Id] == 0)
            {
                rank[node.Id] = 0;
                queue.Enqueue(node.Id);
            }
        }

        // No source (every node is part of a cycle): seed with the first node.
        if (queue.Count == 0 && nodes.Count > 0)
        {
            rank[nodes[0].Id] = 0;
            queue.Enqueue(nodes[0].Id);
        }

        var rankCap = nodes.Count; // hard bound so cycles cannot loop forever
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var current = rank[id];
            foreach (var childId in children[id])
            {
                var candidate = current + 1;
                if (candidate > rankCap)
                {
                    continue;
                }
                if (!rank.TryGetValue(childId, out var existing) || existing < candidate)
                {
                    rank[childId] = candidate;
                    queue.Enqueue(childId);
                }
            }
        }

        // Any node not reached (disconnected / cycle remnant) lands on rank 0.
        foreach (var node in nodes)
        {
            if (!rank.ContainsKey(node.Id))
            {
                rank[node.Id] = 0;
            }
        }

        return rank;
    }

    private void MeasureNode(FlowchartNode node, string fontFamily)
    {
        var text = node.DisplayText;
        var ft = new FormattedText(string.IsNullOrEmpty(text) ? " " : text, fontFamily, NodeFontSize);
        TextMeasurement.MeasureText(ft);

        double textW = ft.Width;
        double textH = ft.Height;

        double padX = 24;
        double padY = 14;
        double w = textW + padX;
        double h = textH + padY;

        switch (node.Shape)
        {
            case FlowchartNodeShape.Circle:
                {
                    var d = Math.Max(textW, textH) + 28;
                    w = d;
                    h = d;
                    break;
                }
            case FlowchartNodeShape.Rhombus:
                w = textW + 44;
                h = textH + 36;
                break;
            case FlowchartNodeShape.Hexagon:
                w = textW + 40;
                h = textH + 16;
                break;
            case FlowchartNodeShape.Stadium:
                w = textW + 34;
                break;
            case FlowchartNodeShape.Cylinder:
                h = textH + 26;
                break;
            case FlowchartNodeShape.Subroutine:
                w = textW + 36;
                break;
            case FlowchartNodeShape.Parallelogram:
            case FlowchartNodeShape.Trapezoid:
                w = textW + 40;
                break;
        }

        node.Width = Math.Max(48, w);
        node.Height = Math.Max(32, h);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout();

        var margin = PlotAreaMargin;
        var extraH = margin.Left + margin.Right;
        var extraV = margin.Top + margin.Bottom;
        if (!string.IsNullOrEmpty(Title))
        {
            extraV += TitleFontSize + 8;
        }

        // Negative PlotAreaMargin is legal; the Size constructor is not — clamp both
        // summation sinks.
        if (_contentWidth <= 0 || _contentHeight <= 0)
        {
            // No nodes: still report the space needed for the title and margins so a titled
            // (but empty) diagram does not collapse to zero.
            return new Size(Math.Max(0, extraH), Math.Max(0, extraV));
        }

        return new Size(Math.Max(0, _contentWidth + extraH), Math.Max(0, _contentHeight + extraV));
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        EnsureLayout();

        var nodes = (ObservableCollection<FlowchartNode>?)GetValue(NodesProperty);
        if (nodes == null || nodes.Count == 0 || _contentWidth <= 0 || _contentHeight <= 0)
        {
            return;
        }

        var edges = (ObservableCollection<FlowchartEdge>?)GetValue(EdgesProperty);

        // Fit the natural layout into the plot area: shrink to fit, never enlarge,
        // and center the result.
        var fit = Math.Min(plotArea.Width / _contentWidth, plotArea.Height / _contentHeight);
        var scale = Math.Min(1.0, fit);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            scale = 1.0;
        }

        var drawnWidth = _contentWidth * scale;
        var drawnHeight = _contentHeight * scale;
        var offsetX = plotArea.X + (plotArea.Width - drawnWidth) / 2.0;
        var offsetY = plotArea.Y + (plotArea.Height - drawnHeight) / 2.0;

        Rect DeviceRect(FlowchartNode n) => new(
            offsetX + n.X * scale,
            offsetY + n.Y * scale,
            n.Width * scale,
            n.Height * scale);

        Point DeviceCenter(FlowchartNode n)
        {
            var r = DeviceRect(n);
            return new Point(r.X + r.Width / 2.0, r.Y + r.Height / 2.0);
        }

        var lookup = new Dictionary<string, FlowchartNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            lookup[node.Id] = node;
        }

        var edgeBrush = EdgeBrush ?? s_defaultEdgeBrush;
        var fontFamily = ResolveFontFamily();
        var arrowSize = Math.Max(5, 9 * scale);

        // Draw edges first so nodes paint on top.
        if (edges != null)
        {
            foreach (var edge in edges)
            {
                if (!lookup.TryGetValue(edge.SourceId, out var src) ||
                    !lookup.TryGetValue(edge.TargetId, out var dst) ||
                    ReferenceEquals(src, dst))
                {
                    continue;
                }

                var brush = edge.Brush ?? edgeBrush;
                var thickness = (edge.Style == FlowchartEdgeStyle.Thick ? 3.0 : 1.5) * Math.Max(scale, 0.5);
                var pen = new Pen(brush, thickness);

                var c0 = DeviceCenter(src);
                var c1 = DeviceCenter(dst);
                var p0 = BoundaryPoint(DeviceRect(src), c0, c1);
                var p1 = BoundaryPoint(DeviceRect(dst), c1, c0);

                DrawConnector(dc, pen, p0, p1, edge.Style);

                if (edge.HasArrowHead)
                {
                    DrawArrowHead(dc, brush, p1, p0, arrowSize);
                }

                if (!string.IsNullOrEmpty(edge.Label))
                {
                    DrawEdgeLabel(dc, edge.Label!, fontFamily, scale,
                        new Point((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0));
                }
            }
        }

        // Draw nodes.
        var nodeFill = NodeFill ?? s_defaultNodeFill;
        var nodeBorder = NodeBorderBrush ?? s_defaultNodeBorder;
        var nodeFg = NodeForeground ?? Foreground ?? s_defaultNodeForeground;
        var nodePen = new Pen(nodeBorder, Math.Max(1.0, 1.4 * scale));

        foreach (var node in nodes)
        {
            var rect = DeviceRect(node);
            var fill = node.Brush ?? nodeFill;
            DrawNodeShape(dc, fill, nodePen, rect, node.Shape);

            var text = node.DisplayText;
            if (!string.IsNullOrEmpty(text))
            {
                var ft = new FormattedText(text, fontFamily, NodeFontSize * scale)
                {
                    Foreground = nodeFg
                };
                TextMeasurement.MeasureText(ft);
                var tx = rect.X + (rect.Width - ft.Width) / 2.0;
                var ty = rect.Y + (rect.Height - ft.Height) / 2.0;
                dc.DrawText(ft, new Point(tx, ty));
            }
        }
    }

    private void DrawNodeShape(DrawingContext dc, Brush fill, Pen pen, Rect rect, FlowchartNodeShape shape)
    {
        switch (shape)
        {
            case FlowchartNodeShape.Rectangle:
                dc.DrawRectangle(fill, pen, rect);
                break;

            case FlowchartNodeShape.RoundedRectangle:
                {
                    var r = Math.Min(12, rect.Height / 3.0);
                    dc.DrawRoundedRectangle(fill, pen, rect, r, r);
                    break;
                }

            case FlowchartNodeShape.Stadium:
                {
                    var r = rect.Height / 2.0;
                    dc.DrawRoundedRectangle(fill, pen, rect, r, r);
                    break;
                }

            case FlowchartNodeShape.Circle:
                dc.DrawEllipse(fill, pen, new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0),
                    rect.Width / 2.0, rect.Height / 2.0);
                break;

            case FlowchartNodeShape.Rhombus:
                dc.DrawGeometry(fill, pen, BuildPolygon(
                    new Point(rect.X + rect.Width / 2.0, rect.Y),
                    new Point(rect.Right, rect.Y + rect.Height / 2.0),
                    new Point(rect.X + rect.Width / 2.0, rect.Bottom),
                    new Point(rect.X, rect.Y + rect.Height / 2.0)));
                break;

            case FlowchartNodeShape.Hexagon:
                {
                    var inset = Math.Min(rect.Width / 4.0, rect.Height / 2.0);
                    dc.DrawGeometry(fill, pen, BuildPolygon(
                        new Point(rect.X + inset, rect.Y),
                        new Point(rect.Right - inset, rect.Y),
                        new Point(rect.Right, rect.Y + rect.Height / 2.0),
                        new Point(rect.Right - inset, rect.Bottom),
                        new Point(rect.X + inset, rect.Bottom),
                        new Point(rect.X, rect.Y + rect.Height / 2.0)));
                    break;
                }

            case FlowchartNodeShape.Parallelogram:
                {
                    var skew = Math.Min(rect.Width / 5.0, 18);
                    dc.DrawGeometry(fill, pen, BuildPolygon(
                        new Point(rect.X + skew, rect.Y),
                        new Point(rect.Right, rect.Y),
                        new Point(rect.Right - skew, rect.Bottom),
                        new Point(rect.X, rect.Bottom)));
                    break;
                }

            case FlowchartNodeShape.Trapezoid:
                {
                    var skew = Math.Min(rect.Width / 5.0, 18);
                    dc.DrawGeometry(fill, pen, BuildPolygon(
                        new Point(rect.X + skew, rect.Y),
                        new Point(rect.Right - skew, rect.Y),
                        new Point(rect.Right, rect.Bottom),
                        new Point(rect.X, rect.Bottom)));
                    break;
                }

            case FlowchartNodeShape.Asymmetric:
                {
                    var skew = Math.Min(rect.Width / 5.0, 16);
                    dc.DrawGeometry(fill, pen, BuildPolygon(
                        new Point(rect.X, rect.Y),
                        new Point(rect.Right, rect.Y),
                        new Point(rect.Right, rect.Bottom),
                        new Point(rect.X, rect.Bottom),
                        new Point(rect.X + skew, rect.Y + rect.Height / 2.0)));
                    break;
                }

            case FlowchartNodeShape.Subroutine:
                {
                    dc.DrawRectangle(fill, pen, rect);
                    var inset = Math.Min(8, rect.Width / 6.0);
                    dc.DrawLine(pen, new Point(rect.X + inset, rect.Y), new Point(rect.X + inset, rect.Bottom));
                    dc.DrawLine(pen, new Point(rect.Right - inset, rect.Y), new Point(rect.Right - inset, rect.Bottom));
                    break;
                }

            case FlowchartNodeShape.Cylinder:
                {
                    var capH = Math.Min(10, rect.Height / 4.0);
                    var body = new Rect(rect.X, rect.Y + capH / 2.0, rect.Width, rect.Height - capH);
                    dc.DrawRectangle(fill, null, body);
                    dc.DrawEllipse(fill, pen, new Point(rect.X + rect.Width / 2.0, rect.Y + capH / 2.0),
                        rect.Width / 2.0, capH / 2.0);
                    dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2.0, rect.Bottom - capH / 2.0),
                        rect.Width / 2.0, capH / 2.0);
                    dc.DrawLine(pen, new Point(rect.X, rect.Y + capH / 2.0), new Point(rect.X, rect.Bottom - capH / 2.0));
                    dc.DrawLine(pen, new Point(rect.Right, rect.Y + capH / 2.0), new Point(rect.Right, rect.Bottom - capH / 2.0));
                    break;
                }

            default:
                dc.DrawRectangle(fill, pen, rect);
                break;
        }
    }

    private static PathGeometry BuildPolygon(params Point[] points)
    {
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };
        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], true));
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point BoundaryPoint(Rect rect, Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
        {
            return from;
        }

        var halfW = rect.Width / 2.0;
        var halfH = rect.Height / 2.0;
        var tx = Math.Abs(dx) > 1e-6 ? halfW / Math.Abs(dx) : double.PositiveInfinity;
        var ty = Math.Abs(dy) > 1e-6 ? halfH / Math.Abs(dy) : double.PositiveInfinity;
        var t = Math.Min(tx, ty);
        return new Point(from.X + dx * t, from.Y + dy * t);
    }

    private static void DrawConnector(DrawingContext dc, Pen pen, Point p0, Point p1, FlowchartEdgeStyle style)
    {
        if (style != FlowchartEdgeStyle.Dotted)
        {
            dc.DrawLine(pen, p0, p1);
            return;
        }

        // Emulate a dotted line with short dashes so it works regardless of pen dash support.
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3)
        {
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        const double dash = 4.0;
        const double gap = 4.0;
        var pos = 0.0;
        while (pos < len)
        {
            var end = Math.Min(pos + dash, len);
            dc.DrawLine(pen,
                new Point(p0.X + ux * pos, p0.Y + uy * pos),
                new Point(p0.X + ux * end, p0.Y + uy * end));
            pos = end + gap;
        }
    }

    private static void DrawArrowHead(DrawingContext dc, Brush brush, Point tip, Point from, double size)
    {
        var dx = tip.X - from.X;
        var dy = tip.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3)
        {
            return;
        }

        var ux = dx / len;
        var uy = dy / len;
        var px = -uy;
        var py = ux;
        var baseX = tip.X - ux * size;
        var baseY = tip.Y - uy * size;
        var half = size * 0.55;

        var head = BuildPolygon(
            tip,
            new Point(baseX + px * half, baseY + py * half),
            new Point(baseX - px * half, baseY - py * half));
        dc.DrawGeometry(brush, null, head);
    }

    private void DrawEdgeLabel(DrawingContext dc, string label, string fontFamily, double scale, Point center)
    {
        var ft = new FormattedText(label, fontFamily, Math.Max(9, 12 * scale))
        {
            Foreground = NodeForeground ?? Foreground ?? s_defaultNodeForeground
        };
        TextMeasurement.MeasureText(ft);

        const double padX = 4;
        const double padY = 1;
        var rect = new Rect(
            center.X - ft.Width / 2.0 - padX,
            center.Y - ft.Height / 2.0 - padY,
            ft.Width + padX * 2,
            ft.Height + padY * 2);

        dc.DrawRoundedRectangle(s_defaultEdgeLabelBackground, null, rect, 3, 3);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2.0, center.Y - ft.Height / 2.0));
    }

    private string ResolveFontFamily()
        => string.IsNullOrWhiteSpace(FontFamily?.Source) ? FrameworkElement.DefaultFontFamilyName : FontFamily.Source;

    #endregion
}
