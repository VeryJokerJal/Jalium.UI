using System.Collections.ObjectModel;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays flow data as a Sankey diagram with proportionally-sized nodes and
/// curved flow-band links between source and target nodes.
/// </summary>
public class SankeyDiagram : ChartBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultNodeBrush = new(Color.FromRgb(0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(200, 200, 200));

    #endregion

    #region Internal Layout Types

    private sealed class SankeyLayoutNode
    {
        public string Id = string.Empty;
        public int Layer;
        public double X;
        public double Y;
        public double Height;
        public double TotalValue;
        public double SourceOffset;
        public double TargetOffset;
        public Brush? Brush;
        public string? Label;
    }

    private sealed class SankeyLayoutLink
    {
        public SankeyLayoutNode Source = null!;
        public SankeyLayoutNode Target = null!;
        public double Value;
        public double SourceY;
        public double TargetY;
        public double Thickness;
        public Brush? Brush;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Nodes dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(nameof(Nodes), typeof(ObservableCollection<SankeyNode>), typeof(SankeyDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Links dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LinksProperty =
        DependencyProperty.Register(nameof(Links), typeof(ObservableCollection<SankeyLink>), typeof(SankeyDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NodeWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeWidthProperty =
        DependencyProperty.Register(nameof(NodeWidth), typeof(double), typeof(SankeyDiagram),
            new PropertyMetadata(20.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NodeSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty NodeSpacingProperty =
        DependencyProperty.Register(nameof(NodeSpacing), typeof(double), typeof(SankeyDiagram),
            new PropertyMetadata(10.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LinkOpacity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LinkOpacityProperty =
        DependencyProperty.Register(nameof(LinkOpacity), typeof(double), typeof(SankeyDiagram),
            new PropertyMetadata(0.4, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NodeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeBrushProperty =
        DependencyProperty.Register(nameof(NodeBrush), typeof(Brush), typeof(SankeyDiagram),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(SankeyDiagram),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowValues dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowValuesProperty =
        DependencyProperty.Register(nameof(ShowValues), typeof(bool), typeof(SankeyDiagram),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LabelPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LabelPositionProperty =
        DependencyProperty.Register(nameof(LabelPosition), typeof(SankeyLabelPosition), typeof(SankeyDiagram),
            new PropertyMetadata(SankeyLabelPosition.Right, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(SankeyDiagram),
            new PropertyMetadata(Orientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Iterations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IterationsProperty =
        DependencyProperty.Register(nameof(Iterations), typeof(int), typeof(SankeyDiagram),
            new PropertyMetadata(32, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of Sankey nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<SankeyNode> Nodes
    {
        get
        {
            var n = (ObservableCollection<SankeyNode>?)GetValue(NodesProperty);
            if (n == null)
            {
                n = new ObservableCollection<SankeyNode>();
                SetValue(NodesProperty, n);
            }
            return n;
        }
        set => SetValue(NodesProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of Sankey links.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<SankeyLink> Links
    {
        get
        {
            var l = (ObservableCollection<SankeyLink>?)GetValue(LinksProperty);
            if (l == null)
            {
                l = new ObservableCollection<SankeyLink>();
                SetValue(LinksProperty, l);
            }
            return l;
        }
        set => SetValue(LinksProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of node rectangles.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double NodeWidth
    {
        get => (double)GetValue(NodeWidthProperty)!;
        set => SetValue(NodeWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical spacing between nodes within a layer.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double NodeSpacing
    {
        get => (double)GetValue(NodeSpacingProperty)!;
        set => SetValue(NodeSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of link flow bands (0..1).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LinkOpacity
    {
        get => (double)GetValue(LinkOpacityProperty)!;
        set => SetValue(LinkOpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets the default brush for nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NodeBrush
    {
        get => (Brush?)GetValue(NodeBrushProperty);
        set => SetValue(NodeBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether labels are shown on nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty)!;
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether values are shown on labels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowValues
    {
        get => (bool)GetValue(ShowValuesProperty)!;
        set => SetValue(ShowValuesProperty, value);
    }

    /// <summary>
    /// Gets or sets the position of node labels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public SankeyLabelPosition LabelPosition
    {
        get => (SankeyLabelPosition)GetValue(LabelPositionProperty)!;
        set => SetValue(LabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the diagram orientation.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of relaxation iterations for node positioning.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public int Iterations
    {
        get => (int)GetValue(IterationsProperty)!;
        set => SetValue(IterationsProperty, value);
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.SankeyDiagramAutomationPeer(this);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        var nodes = (ObservableCollection<SankeyNode>?)GetValue(NodesProperty);
        var links = (ObservableCollection<SankeyLink>?)GetValue(LinksProperty);

        if (nodes == null || nodes.Count == 0)
            return;

        // Build layout
        var layoutNodes = new Dictionary<string, SankeyLayoutNode>();
        int nodeIdx = 0;
        foreach (var node in nodes)
        {
            layoutNodes[node.Id] = new SankeyLayoutNode
            {
                Id = node.Id,
                Label = node.Label ?? node.Id,
                Brush = node.Brush,
                TotalValue = 0,
                SourceOffset = 0,
                TargetOffset = 0
            };
            nodeIdx++;
        }

        var layoutLinks = new List<SankeyLayoutLink>();
        if (links != null)
        {
            foreach (var link in links)
            {
                if (layoutNodes.TryGetValue(link.SourceId, out var src) &&
                    layoutNodes.TryGetValue(link.TargetId, out var tgt))
                {
                    layoutLinks.Add(new SankeyLayoutLink
                    {
                        Source = src,
                        Target = tgt,
                        Value = link.Value,
                        Brush = link.Brush
                    });
                }
            }
        }

        // Step 1: Topological sort and layer assignment
        AssignLayers(layoutNodes, layoutLinks);

        // Step 2: Compute node values (max of incoming/outgoing flow)
        ComputeNodeValues(layoutNodes, layoutLinks);

        // Step 3: Position nodes within layers
        PositionNodes(layoutNodes, layoutLinks, plotArea);

        // Step 4: Compute link positions
        ComputeLinkPositions(layoutLinks);

        // Step 5: Render
        RenderLinks(dc, layoutLinks, plotArea);
        RenderNodes(dc, layoutNodes, plotArea);

        if (ShowLabels)
        {
            RenderLabels(dc, layoutNodes, plotArea);
        }
    }

    #endregion

    #region Layout Algorithm

    private static void AssignLayers(Dictionary<string, SankeyLayoutNode> nodes, List<SankeyLayoutLink> links)
    {
        // Build adjacency
        var incomingCount = new Dictionary<string, int>();
        var outgoing = new Dictionary<string, List<string>>();

        foreach (var node in nodes.Values)
        {
            incomingCount[node.Id] = 0;
            outgoing[node.Id] = new List<string>();
        }

        foreach (var link in links)
        {
            if (incomingCount.ContainsKey(link.Target.Id))
                incomingCount[link.Target.Id]++;
            if (outgoing.ContainsKey(link.Source.Id))
                outgoing[link.Source.Id].Add(link.Target.Id);
        }

        // BFS topological layer assignment
        var queue = new Queue<string>();
        foreach (var kvp in incomingCount)
        {
            if (kvp.Value == 0)
            {
                nodes[kvp.Key].Layer = 0;
                queue.Enqueue(kvp.Key);
            }
        }

        // If no root nodes, start from the first
        if (queue.Count == 0 && nodes.Count > 0)
        {
            var firstId = nodes.Values.First().Id;
            nodes[firstId].Layer = 0;
            queue.Enqueue(firstId);
        }

        var visited = new HashSet<string>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id))
                continue;

            int currentLayer = nodes[id].Layer;
            foreach (var childId in outgoing[id])
            {
                if (nodes.TryGetValue(childId, out var child))
                {
                    int newLayer = currentLayer + 1;
                    if (newLayer > child.Layer)
                        child.Layer = newLayer;
                    queue.Enqueue(childId);
                }
            }
        }

        // Assign unvisited nodes to layer 0
        foreach (var node in nodes.Values)
        {
            if (!visited.Contains(node.Id))
                node.Layer = 0;
        }
    }

    private static void ComputeNodeValues(Dictionary<string, SankeyLayoutNode> nodes, List<SankeyLayoutLink> links)
    {
        // Accumulate incoming and outgoing values
        var incoming = new Dictionary<string, double>();
        var outgoingVal = new Dictionary<string, double>();

        foreach (var node in nodes.Values)
        {
            incoming[node.Id] = 0;
            outgoingVal[node.Id] = 0;
        }

        foreach (var link in links)
        {
            outgoingVal[link.Source.Id] += link.Value;
            incoming[link.Target.Id] += link.Value;
        }

        foreach (var node in nodes.Values)
        {
            node.TotalValue = Math.Max(incoming[node.Id], outgoingVal[node.Id]);
            if (node.TotalValue <= 0)
                node.TotalValue = 1; // Minimum so node is visible
        }
    }

    private void PositionNodes(Dictionary<string, SankeyLayoutNode> nodes,
        List<SankeyLayoutLink> links, Rect plotArea)
    {
        bool isHorizontal = Orientation == Orientation.Horizontal;
        double nodeW = NodeWidth;
        double spacing = NodeSpacing;
        int iterations = Iterations;

        // Group by layer
        var layers = new Dictionary<int, List<SankeyLayoutNode>>();
        int maxLayer = 0;
        foreach (var node in nodes.Values)
        {
            if (node.Layer > maxLayer) maxLayer = node.Layer;
            if (!layers.ContainsKey(node.Layer))
                layers[node.Layer] = new List<SankeyLayoutNode>();
            layers[node.Layer].Add(node);
        }

        int layerCount = maxLayer + 1;

        // Compute total value to determine scaling
        double maxLayerValue = 0;
        foreach (var kvp in layers)
        {
            double layerTotal = 0;
            foreach (var node in kvp.Value)
                layerTotal += node.TotalValue;
            layerTotal += (kvp.Value.Count - 1) * spacing;
            if (layerTotal > maxLayerValue)
                maxLayerValue = layerTotal;
        }

        double availableLength = isHorizontal ? plotArea.Height : plotArea.Width;
        double scale = maxLayerValue > 0 ? (availableLength - spacing) / maxLayerValue : 1;
        if (scale > 10) scale = 10; // Cap scale for tiny datasets

        // Position nodes initially: evenly distributed
        double layerAxisLength = isHorizontal ? plotArea.Width : plotArea.Height;
        double layerStep = layerCount > 1 ? (layerAxisLength - nodeW) / (layerCount) : 0;

        for (int layer = 0; layer <= maxLayer; layer++)
        {
            if (!layers.TryGetValue(layer, out var layerNodes))
                continue;

            double layerPos;
            if (isHorizontal)
            {
                layerPos = plotArea.Left + layer * layerStep + nodeW / 2.0;
            }
            else
            {
                layerPos = plotArea.Top + layer * layerStep + nodeW / 2.0;
            }

            double offset = 0;
            foreach (var node in layerNodes)
            {
                node.Height = node.TotalValue * scale;
                if (node.Height < 2) node.Height = 2;

                if (isHorizontal)
                {
                    node.X = layerPos;
                    node.Y = plotArea.Top + offset;
                }
                else
                {
                    node.X = plotArea.Left + offset;
                    node.Y = layerPos;
                }

                offset += node.Height + spacing;
            }

            // Center the layer vertically
            double totalLayerHeight = offset - spacing;
            double centerOffset = (availableLength - totalLayerHeight) / 2.0;
            if (centerOffset > 0)
            {
                foreach (var node in layerNodes)
                {
                    if (isHorizontal)
                        node.Y += centerOffset;
                    else
                        node.X += centerOffset;
                }
            }
        }

        // Iterative relaxation: shift nodes to minimize link crossing
        for (int iter = 0; iter < iterations; iter++)
        {
            double alpha = 1.0 - (double)iter / iterations;

            // Forward pass
            for (int layer = 1; layer <= maxLayer; layer++)
            {
                if (!layers.TryGetValue(layer, out var layerNodes))
                    continue;

                foreach (var node in layerNodes)
                {
                    double weightedSum = 0;
                    double totalWeight = 0;

                    foreach (var link in links)
                    {
                        if (link.Target == node)
                        {
                            double sourceCenter = isHorizontal
                                ? link.Source.Y + link.Source.Height / 2.0
                                : link.Source.X + link.Source.Height / 2.0;
                            weightedSum += sourceCenter * link.Value;
                            totalWeight += link.Value;
                        }
                    }

                    if (totalWeight > 0)
                    {
                        double targetCenter = weightedSum / totalWeight;
                        double currentCenter = isHorizontal
                            ? node.Y + node.Height / 2.0
                            : node.X + node.Height / 2.0;
                        double delta = (targetCenter - currentCenter) * alpha;

                        if (isHorizontal)
                            node.Y += delta;
                        else
                            node.X += delta;
                    }
                }

                // Resolve overlaps
                ResolveOverlaps(layerNodes, isHorizontal, spacing, plotArea, availableLength);
            }

            // Backward pass
            for (int layer = maxLayer - 1; layer >= 0; layer--)
            {
                if (!layers.TryGetValue(layer, out var layerNodes))
                    continue;

                foreach (var node in layerNodes)
                {
                    double weightedSum = 0;
                    double totalWeight = 0;

                    foreach (var link in links)
                    {
                        if (link.Source == node)
                        {
                            double targetCenter = isHorizontal
                                ? link.Target.Y + link.Target.Height / 2.0
                                : link.Target.X + link.Target.Height / 2.0;
                            weightedSum += targetCenter * link.Value;
                            totalWeight += link.Value;
                        }
                    }

                    if (totalWeight > 0)
                    {
                        double targetCenter = weightedSum / totalWeight;
                        double currentCenter = isHorizontal
                            ? node.Y + node.Height / 2.0
                            : node.X + node.Height / 2.0;
                        double delta = (targetCenter - currentCenter) * alpha;

                        if (isHorizontal)
                            node.Y += delta;
                        else
                            node.X += delta;
                    }
                }

                ResolveOverlaps(layerNodes, isHorizontal, spacing, plotArea, availableLength);
            }
        }
    }

    private static void ResolveOverlaps(List<SankeyLayoutNode> layerNodes, bool isHorizontal,
        double spacing, Rect plotArea, double availableLength)
    {
        // Sort by position
        layerNodes.Sort((a, b) =>
        {
            double posA = isHorizontal ? a.Y : a.X;
            double posB = isHorizontal ? b.Y : b.X;
            return posA.CompareTo(posB);
        });

        double minPos = isHorizontal ? plotArea.Top : plotArea.Left;

        // Push nodes down to resolve overlaps
        double currentPos = minPos;
        foreach (var node in layerNodes)
        {
            double nodePos = isHorizontal ? node.Y : node.X;
            if (nodePos < currentPos)
            {
                if (isHorizontal)
                    node.Y = currentPos;
                else
                    node.X = currentPos;
            }

            double actualPos = isHorizontal ? node.Y : node.X;
            currentPos = actualPos + node.Height + spacing;
        }

        // If overflow, compress back
        var last = layerNodes[layerNodes.Count - 1];
        double lastEnd = (isHorizontal ? last.Y : last.X) + last.Height;
        double maxPos = minPos + availableLength;

        if (lastEnd > maxPos)
        {
            double overflow = lastEnd - maxPos;
            double shift = overflow / layerNodes.Count;
            for (int i = layerNodes.Count - 1; i >= 0; i--)
            {
                if (isHorizontal)
                    layerNodes[i].Y -= shift * (layerNodes.Count - i);
                else
                    layerNodes[i].X -= shift * (layerNodes.Count - i);

                // Clamp to area
                if (isHorizontal)
                    layerNodes[i].Y = Math.Max(layerNodes[i].Y, minPos);
                else
                    layerNodes[i].X = Math.Max(layerNodes[i].X, minPos);
            }
        }
    }

    private void ComputeLinkPositions(List<SankeyLayoutLink> links)
    {
        bool isHorizontal = Orientation == Orientation.Horizontal;

        // Reset source/target offsets
        var sourceOffsets = new Dictionary<string, double>();
        var targetOffsets = new Dictionary<string, double>();

        // Sort links by target position for more aesthetic ordering
        links.Sort((a, b) =>
        {
            double posA = isHorizontal ? a.Target.Y : a.Target.X;
            double posB = isHorizontal ? b.Target.Y : b.Target.X;
            return posA.CompareTo(posB);
        });

        foreach (var link in links)
        {
            if (!sourceOffsets.ContainsKey(link.Source.Id))
                sourceOffsets[link.Source.Id] = 0;
            if (!targetOffsets.ContainsKey(link.Target.Id))
                targetOffsets[link.Target.Id] = 0;

            double sourceVal = link.Source.TotalValue;
            double targetVal = link.Target.TotalValue;

            link.Thickness = sourceVal > 0
                ? (link.Value / sourceVal) * link.Source.Height
                : 2;

            double targetThickness = targetVal > 0
                ? (link.Value / targetVal) * link.Target.Height
                : 2;

            if (isHorizontal)
            {
                link.SourceY = link.Source.Y + sourceOffsets[link.Source.Id];
                link.TargetY = link.Target.Y + targetOffsets[link.Target.Id];
            }
            else
            {
                link.SourceY = link.Source.X + sourceOffsets[link.Source.Id];
                link.TargetY = link.Target.X + targetOffsets[link.Target.Id];
            }

            sourceOffsets[link.Source.Id] += link.Thickness;
            targetOffsets[link.Target.Id] += targetThickness;
        }
    }

    #endregion

    #region Render Helpers

    private void RenderLinks(DrawingContext dc, List<SankeyLayoutLink> links, Rect plotArea)
    {
        bool isHorizontal = Orientation == Orientation.Horizontal;
        double nodeW = NodeWidth;
        double opacity = Math.Clamp(LinkOpacity, 0.0, 1.0);
        byte alphaByte = (byte)(opacity * 255);
        int linkIdx = 0;

        foreach (var link in links)
        {
            // Determine link color
            Brush linkBrush;
            var baseBrush = link.Brush ?? link.Source.Brush ?? NodeBrush ?? GetSeriesBrush(linkIdx);
            if (baseBrush is SolidColorBrush scb)
            {
                linkBrush = new SolidColorBrush(Color.FromArgb(alphaByte, scb.Color.R, scb.Color.G, scb.Color.B));
            }
            else
            {
                linkBrush = baseBrush;
            }

            if (isHorizontal)
            {
                double x0 = link.Source.X + nodeW / 2.0;
                double x1 = link.Target.X - nodeW / 2.0;
                double y0Top = link.SourceY;
                double y0Bot = link.SourceY + link.Thickness;
                double y1Top = link.TargetY;
                double targetThickness = link.Target.TotalValue > 0
                    ? (link.Value / link.Target.TotalValue) * link.Target.Height
                    : link.Thickness;
                double y1Bot = link.TargetY + targetThickness;

                double midX = (x0 + x1) / 2.0;

                // Top edge: cubic bezier from (x0, y0Top) to (x1, y1Top)
                var figure = new PathFigure
                {
                    StartPoint = new Point(x0, y0Top),
                    IsClosed = true,
                    IsFilled = true
                };
                figure.Segments.Add(new BezierSegment(
                    new Point(midX, y0Top), new Point(midX, y1Top), new Point(x1, y1Top), true));

                // Right edge down
                figure.Segments.Add(new LineSegment(new Point(x1, y1Bot), true));

                // Bottom edge: cubic bezier from (x1, y1Bot) to (x0, y0Bot)
                figure.Segments.Add(new BezierSegment(
                    new Point(midX, y1Bot), new Point(midX, y0Bot), new Point(x0, y0Bot), true));

                var geo = new PathGeometry();
                geo.Figures.Add(figure);
                dc.DrawGeometry(linkBrush, null, geo);
            }
            else
            {
                // Vertical orientation: swap x/y logic
                double y0 = link.Source.Y + nodeW / 2.0;
                double y1 = link.Target.Y - nodeW / 2.0;
                double x0Left = link.SourceY;
                double x0Right = link.SourceY + link.Thickness;
                double x1Left = link.TargetY;
                double targetThickness = link.Target.TotalValue > 0
                    ? (link.Value / link.Target.TotalValue) * link.Target.Height
                    : link.Thickness;
                double x1Right = link.TargetY + targetThickness;

                double midY = (y0 + y1) / 2.0;

                var figure = new PathFigure
                {
                    StartPoint = new Point(x0Left, y0),
                    IsClosed = true,
                    IsFilled = true
                };
                figure.Segments.Add(new BezierSegment(
                    new Point(x0Left, midY), new Point(x1Left, midY), new Point(x1Left, y1), true));
                figure.Segments.Add(new LineSegment(new Point(x1Right, y1), true));
                figure.Segments.Add(new BezierSegment(
                    new Point(x1Right, midY), new Point(x0Right, midY), new Point(x0Right, y0), true));

                var geo = new PathGeometry();
                geo.Figures.Add(figure);
                dc.DrawGeometry(linkBrush, null, geo);
            }

            linkIdx++;
        }
    }

    private void RenderNodes(DrawingContext dc, Dictionary<string, SankeyLayoutNode> nodes, Rect plotArea)
    {
        bool isHorizontal = Orientation == Orientation.Horizontal;
        double nodeW = NodeWidth;
        int idx = 0;

        foreach (var node in nodes.Values)
        {
            var nodeBrush = node.Brush ?? NodeBrush ?? GetSeriesBrush(idx);

            Rect nodeRect;
            if (isHorizontal)
            {
                nodeRect = new Rect(node.X - nodeW / 2.0, node.Y, nodeW, node.Height);
            }
            else
            {
                nodeRect = new Rect(node.X, node.Y - nodeW / 2.0, node.Height, nodeW);
            }

            dc.DrawRectangle(nodeBrush, null, nodeRect);
            idx++;
        }
    }

    private void RenderLabels(DrawingContext dc, Dictionary<string, SankeyLayoutNode> nodes, Rect plotArea)
    {
        bool isHorizontal = Orientation == Orientation.Horizontal;
        double nodeW = NodeWidth;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var labelPos = LabelPosition;
        bool showVals = ShowValues;

        foreach (var node in nodes.Values)
        {
            var text = node.Label ?? node.Id;
            if (showVals)
                text = $"{text} ({node.TotalValue:G4})";

            if (string.IsNullOrEmpty(text))
                continue;

            var ft = new FormattedText(text, fontFamily, 11.0)
            {
                Foreground = s_defaultLabelBrush
            };
            TextMeasurement.MeasureText(ft);

            double lx, ly;

            if (isHorizontal)
            {
                double nodeCenterY = node.Y + node.Height / 2.0;
                ly = nodeCenterY - ft.Height / 2.0;

                switch (labelPos)
                {
                    case SankeyLabelPosition.Left:
                        lx = node.X - nodeW / 2.0 - ft.Width - 4;
                        break;
                    case SankeyLabelPosition.Inside:
                        lx = node.X - ft.Width / 2.0;
                        break;
                    case SankeyLabelPosition.Both:
                        // Draw on right side (primary)
                        lx = node.X + nodeW / 2.0 + 4;
                        dc.DrawText(ft, new Point(lx, ly));
                        // Also on left
                        lx = node.X - nodeW / 2.0 - ft.Width - 4;
                        dc.DrawText(ft, new Point(lx, ly));
                        continue;
                    default: // Right
                        lx = node.X + nodeW / 2.0 + 4;
                        break;
                }
            }
            else
            {
                double nodeCenterX = node.X + node.Height / 2.0;
                lx = nodeCenterX - ft.Width / 2.0;

                switch (labelPos)
                {
                    case SankeyLabelPosition.Left:
                        ly = node.Y - nodeW / 2.0 - ft.Height - 2;
                        break;
                    case SankeyLabelPosition.Inside:
                        ly = node.Y - ft.Height / 2.0;
                        break;
                    case SankeyLabelPosition.Both:
                        ly = node.Y + nodeW / 2.0 + 2;
                        dc.DrawText(ft, new Point(lx, ly));
                        ly = node.Y - nodeW / 2.0 - ft.Height - 2;
                        dc.DrawText(ft, new Point(lx, ly));
                        continue;
                    default: // Right (below in vertical)
                        ly = node.Y + nodeW / 2.0 + 2;
                        break;
                }
            }

            dc.DrawText(ft, new Point(lx, ly));
        }
    }

    #endregion
}
