using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays a network/graph visualization with nodes and links, supporting force-directed,
/// circular, and hierarchical layout algorithms with interactive node dragging.
/// </summary>
public class NetworkGraph : ChartBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultNodeBrush = new(Color.FromRgb(0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_defaultLinkBrush = new(Color.FromArgb(120, 0x90, 0x90, 0x90));
    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(200, 200, 200));

    #endregion

    #region Private State

    private bool _layoutDirty = true;
    private Dictionary<string, LayoutNode> _layoutNodes = new();
    private NetworkNode? _draggedNode;
    private Point _dragOffset;

    private sealed class LayoutNode
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Nodes dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(nameof(Nodes), typeof(ObservableCollection<NetworkNode>), typeof(NetworkGraph),
            new PropertyMetadata(null, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the Links dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LinksProperty =
        DependencyProperty.Register(nameof(Links), typeof(ObservableCollection<NetworkLink>), typeof(NetworkGraph),
            new PropertyMetadata(null, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the LayoutAlgorithm dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty LayoutAlgorithmProperty =
        DependencyProperty.Register(nameof(LayoutAlgorithm), typeof(NetworkLayoutAlgorithm), typeof(NetworkGraph),
            new PropertyMetadata(NetworkLayoutAlgorithm.ForceDirected, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the NodeRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeRadiusProperty =
        DependencyProperty.Register(nameof(NodeRadius), typeof(double), typeof(NetworkGraph),
            new PropertyMetadata(15.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NodeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NodeBrushProperty =
        DependencyProperty.Register(nameof(NodeBrush), typeof(Brush), typeof(NetworkGraph),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LinkBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LinkBrushProperty =
        DependencyProperty.Register(nameof(LinkBrush), typeof(Brush), typeof(NetworkGraph),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LinkThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LinkThicknessProperty =
        DependencyProperty.Register(nameof(LinkThickness), typeof(double), typeof(NetworkGraph),
            new PropertyMetadata(1.5, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(NetworkGraph),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsNodeDraggable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsNodeDraggableProperty =
        DependencyProperty.Register(nameof(IsNodeDraggable), typeof(bool), typeof(NetworkGraph),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the RepulsionForce dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty RepulsionForceProperty =
        DependencyProperty.Register(nameof(RepulsionForce), typeof(double), typeof(NetworkGraph),
            new PropertyMetadata(100.0, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the AttractionForce dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty AttractionForceProperty =
        DependencyProperty.Register(nameof(AttractionForce), typeof(double), typeof(NetworkGraph),
            new PropertyMetadata(0.01, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the Damping dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty DampingProperty =
        DependencyProperty.Register(nameof(Damping), typeof(double), typeof(NetworkGraph),
            new PropertyMetadata(0.9, OnDataPropertyChanged));

    /// <summary>
    /// Identifies the MaxIterations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty MaxIterationsProperty =
        DependencyProperty.Register(nameof(MaxIterations), typeof(int), typeof(NetworkGraph),
            new PropertyMetadata(300, OnDataPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<NetworkNode> Nodes
    {
        get
        {
            var n = (ObservableCollection<NetworkNode>?)GetValue(NodesProperty);
            if (n == null)
            {
                n = new ObservableCollection<NetworkNode>();
                SetValue(NodesProperty, n);
            }
            return n;
        }
        set => SetValue(NodesProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of links between nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<NetworkLink> Links
    {
        get
        {
            var l = (ObservableCollection<NetworkLink>?)GetValue(LinksProperty);
            if (l == null)
            {
                l = new ObservableCollection<NetworkLink>();
                SetValue(LinksProperty, l);
            }
            return l;
        }
        set => SetValue(LinksProperty, value);
    }

    /// <summary>
    /// Gets or sets the layout algorithm used to position nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public NetworkLayoutAlgorithm LayoutAlgorithm
    {
        get => (NetworkLayoutAlgorithm)GetValue(LayoutAlgorithmProperty)!;
        set => SetValue(LayoutAlgorithmProperty, value);
    }

    /// <summary>
    /// Gets or sets the default radius for node circles.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double NodeRadius
    {
        get => (double)GetValue(NodeRadiusProperty)!;
        set => SetValue(NodeRadiusProperty, value);
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
    /// Gets or sets the default brush for links.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? LinkBrush
    {
        get => (Brush?)GetValue(LinkBrushProperty);
        set => SetValue(LinkBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of link lines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LinkThickness
    {
        get => (double)GetValue(LinkThicknessProperty)!;
        set => SetValue(LinkThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets whether node labels are shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty)!;
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether nodes can be dragged interactively.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool IsNodeDraggable
    {
        get => (bool)GetValue(IsNodeDraggableProperty)!;
        set => SetValue(IsNodeDraggableProperty, value);
    }

    /// <summary>
    /// Gets or sets the repulsion force constant for force-directed layout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double RepulsionForce
    {
        get => (double)GetValue(RepulsionForceProperty)!;
        set => SetValue(RepulsionForceProperty, value);
    }

    /// <summary>
    /// Gets or sets the attraction force constant for force-directed layout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double AttractionForce
    {
        get => (double)GetValue(AttractionForceProperty)!;
        set => SetValue(AttractionForceProperty, value);
    }

    /// <summary>
    /// Gets or sets the velocity damping factor for force-directed layout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double Damping
    {
        get => (double)GetValue(DampingProperty)!;
        set => SetValue(DampingProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of iterations for force-directed layout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public int MaxIterations
    {
        get => (int)GetValue(MaxIterationsProperty)!;
        set => SetValue(MaxIterationsProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkGraph"/> class.
    /// </summary>
    public NetworkGraph()
    {
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnGraphMouseDown));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnGraphMouseUp));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnGraphMouseMove));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.NetworkGraphAutomationPeer(this);
    }

    #endregion

    #region Property Changed

    private static void OnDataPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NetworkGraph graph)
        {
            graph._layoutDirty = true;
            graph.InvalidateVisual();
        }
    }

    #endregion

    #region Layout Algorithms

    private void EnsureLayout(Rect plotArea)
    {
        if (!_layoutDirty)
            return;
        _layoutDirty = false;

        var nodes = (ObservableCollection<NetworkNode>?)GetValue(NodesProperty);
        var links = (ObservableCollection<NetworkLink>?)GetValue(LinksProperty);
        if (nodes == null || nodes.Count == 0)
        {
            _layoutNodes.Clear();
            return;
        }

        switch (LayoutAlgorithm)
        {
            case NetworkLayoutAlgorithm.ForceDirected:
                RunForceDirectedLayout(nodes, links, plotArea);
                break;
            case NetworkLayoutAlgorithm.Circular:
                RunCircularLayout(nodes, plotArea);
                break;
            case NetworkLayoutAlgorithm.Hierarchical:
                RunHierarchicalLayout(nodes, links, plotArea);
                break;
        }

        // Write positions back to node objects
        foreach (var node in nodes)
        {
            if (_layoutNodes.TryGetValue(node.Id, out var ln))
            {
                node.X = ln.X;
                node.Y = ln.Y;
            }
        }
    }

    private void RunForceDirectedLayout(ObservableCollection<NetworkNode> nodes,
        ObservableCollection<NetworkLink>? links, Rect plotArea)
    {
        var rng = new Random(42);
        _layoutNodes.Clear();

        // Initialize positions randomly within plot area
        foreach (var node in nodes)
        {
            _layoutNodes[node.Id] = new LayoutNode
            {
                X = plotArea.Left + rng.NextDouble() * plotArea.Width,
                Y = plotArea.Top + rng.NextDouble() * plotArea.Height,
                Vx = 0,
                Vy = 0
            };
        }

        var repulsion = RepulsionForce;
        var attraction = AttractionForce;
        var damping = Damping;
        var maxIter = MaxIterations;

        var nodeList = new List<(string id, LayoutNode ln)>();
        foreach (var node in nodes)
        {
            if (_layoutNodes.TryGetValue(node.Id, out var ln))
                nodeList.Add((node.Id, ln));
        }

        var linkList = new List<(LayoutNode source, LayoutNode target, double weight)>();
        if (links != null)
        {
            foreach (var link in links)
            {
                if (_layoutNodes.TryGetValue(link.SourceId, out var src) &&
                    _layoutNodes.TryGetValue(link.TargetId, out var tgt))
                {
                    linkList.Add((src, tgt, link.Weight));
                }
            }
        }

        double centerX = plotArea.Left + plotArea.Width / 2.0;
        double centerY = plotArea.Top + plotArea.Height / 2.0;

        for (int iter = 0; iter < maxIter; iter++)
        {
            double temperature = repulsion * (1.0 - (double)iter / maxIter);

            // Repulsion between all pairs (Fruchterman-Reingold)
            for (int i = 0; i < nodeList.Count; i++)
            {
                var a = nodeList[i].ln;
                for (int j = i + 1; j < nodeList.Count; j++)
                {
                    var b = nodeList[j].ln;
                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    double distSq = dx * dx + dy * dy;
                    if (distSq < 0.01) distSq = 0.01;
                    double dist = Math.Sqrt(distSq);

                    double force = repulsion * repulsion / dist;
                    double fx = dx / dist * force;
                    double fy = dy / dist * force;

                    a.Vx += fx;
                    a.Vy += fy;
                    b.Vx -= fx;
                    b.Vy -= fy;
                }
            }

            // Attraction along links
            foreach (var (src, tgt, weight) in linkList)
            {
                double dx = tgt.X - src.X;
                double dy = tgt.Y - src.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 0.01) dist = 0.01;

                double force = dist * dist * attraction * weight;
                double fx = dx / dist * force;
                double fy = dy / dist * force;

                src.Vx += fx;
                src.Vy += fy;
                tgt.Vx -= fx;
                tgt.Vy -= fy;
            }

            // Gentle gravity toward center
            foreach (var (_, ln) in nodeList)
            {
                double dx = centerX - ln.X;
                double dy = centerY - ln.Y;
                ln.Vx += dx * 0.001;
                ln.Vy += dy * 0.001;
            }

            // Apply velocity with damping and temperature limit
            foreach (var (_, ln) in nodeList)
            {
                ln.Vx *= damping;
                ln.Vy *= damping;

                double speed = Math.Sqrt(ln.Vx * ln.Vx + ln.Vy * ln.Vy);
                if (speed > temperature)
                {
                    ln.Vx = ln.Vx / speed * temperature;
                    ln.Vy = ln.Vy / speed * temperature;
                }

                ln.X += ln.Vx;
                ln.Y += ln.Vy;

                // Keep within bounds
                double margin = NodeRadius;
                ln.X = Math.Clamp(ln.X, plotArea.Left + margin, plotArea.Right - margin);
                ln.Y = Math.Clamp(ln.Y, plotArea.Top + margin, plotArea.Bottom - margin);
            }
        }
    }

    private void RunCircularLayout(ObservableCollection<NetworkNode> nodes, Rect plotArea)
    {
        _layoutNodes.Clear();
        double cx = plotArea.Left + plotArea.Width / 2.0;
        double cy = plotArea.Top + plotArea.Height / 2.0;
        double radius = Math.Min(plotArea.Width, plotArea.Height) / 2.0 - NodeRadius - 20;
        if (radius < 10) radius = 10;

        for (int i = 0; i < nodes.Count; i++)
        {
            double angle = 2.0 * Math.PI * i / nodes.Count - Math.PI / 2.0;
            _layoutNodes[nodes[i].Id] = new LayoutNode
            {
                X = cx + radius * Math.Cos(angle),
                Y = cy + radius * Math.Sin(angle)
            };
        }
    }

    private void RunHierarchicalLayout(ObservableCollection<NetworkNode> nodes,
        ObservableCollection<NetworkLink>? links, Rect plotArea)
    {
        _layoutNodes.Clear();

        // Build adjacency and compute in-degree
        var inDegree = new Dictionary<string, int>();
        var children = new Dictionary<string, List<string>>();
        foreach (var node in nodes)
        {
            inDegree[node.Id] = 0;
            children[node.Id] = new List<string>();
        }

        if (links != null)
        {
            foreach (var link in links)
            {
                if (inDegree.ContainsKey(link.TargetId))
                    inDegree[link.TargetId]++;
                if (children.ContainsKey(link.SourceId))
                    children[link.SourceId].Add(link.TargetId);
            }
        }

        // Topological layer assignment using BFS from roots
        var layers = new Dictionary<string, int>();
        var queue = new Queue<string>();

        foreach (var node in nodes)
        {
            if (inDegree[node.Id] == 0)
            {
                layers[node.Id] = 0;
                queue.Enqueue(node.Id);
            }
        }

        // If no roots found, pick the first node
        if (queue.Count == 0 && nodes.Count > 0)
        {
            layers[nodes[0].Id] = 0;
            queue.Enqueue(nodes[0].Id);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            int currentLayer = layers[id];
            foreach (var childId in children[id])
            {
                if (!layers.ContainsKey(childId) || layers[childId] < currentLayer + 1)
                {
                    layers[childId] = currentLayer + 1;
                    queue.Enqueue(childId);
                }
            }
        }

        // Assign any unvisited nodes to layer 0
        foreach (var node in nodes)
        {
            if (!layers.ContainsKey(node.Id))
                layers[node.Id] = 0;
        }

        // Group nodes by layer
        var layerGroups = new Dictionary<int, List<NetworkNode>>();
        int maxLayer = 0;
        foreach (var node in nodes)
        {
            int layer = layers[node.Id];
            if (layer > maxLayer) maxLayer = layer;
            if (!layerGroups.ContainsKey(layer))
                layerGroups[layer] = new List<NetworkNode>();
            layerGroups[layer].Add(node);
        }

        int layerCount = maxLayer + 1;
        double layerSpacing = layerCount > 1 ? plotArea.Height / (layerCount + 1) : plotArea.Height / 2.0;

        for (int layer = 0; layer <= maxLayer; layer++)
        {
            if (!layerGroups.TryGetValue(layer, out var group))
                continue;

            double y = plotArea.Top + (layer + 1) * layerSpacing;
            double nodeSpacing = plotArea.Width / (group.Count + 1);

            for (int i = 0; i < group.Count; i++)
            {
                double x = plotArea.Left + (i + 1) * nodeSpacing;
                _layoutNodes[group[i].Id] = new LayoutNode { X = x, Y = y };
            }
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        var nodes = (ObservableCollection<NetworkNode>?)GetValue(NodesProperty);
        var links = (ObservableCollection<NetworkLink>?)GetValue(LinksProperty);

        if (nodes == null || nodes.Count == 0)
            return;

        EnsureLayout(plotArea);

        var defaultNodeBrush = NodeBrush ?? s_defaultNodeBrush;
        var defaultLinkBrush = LinkBrush ?? s_defaultLinkBrush;
        var linkPen = new Pen(defaultLinkBrush, LinkThickness);
        var radius = NodeRadius;

        // Draw links
        if (links != null)
        {
            foreach (var link in links)
            {
                if (!_layoutNodes.TryGetValue(link.SourceId, out var srcLayout) ||
                    !_layoutNodes.TryGetValue(link.TargetId, out var tgtLayout))
                    continue;

                var currentLinkBrush = link.Brush ?? defaultLinkBrush;
                var currentLinkPen = link.Brush != null ? new Pen(currentLinkBrush, LinkThickness) : linkPen;

                var p1 = new Point(srcLayout.X, srcLayout.Y);
                var p2 = new Point(tgtLayout.X, tgtLayout.Y);

                // Draw as a subtle bezier curve for aesthetics
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double curvature = 0.15;
                var cp1 = new Point(p1.X + dx * 0.25 - dy * curvature, p1.Y + dy * 0.25 + dx * curvature);
                var cp2 = new Point(p1.X + dx * 0.75 + dy * curvature, p1.Y + dy * 0.75 - dx * curvature);

                var figure = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
                figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                dc.DrawGeometry(null, currentLinkPen, geometry);
            }
        }

        // Draw nodes
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        int nodeIndex = 0;
        foreach (var node in nodes)
        {
            if (!_layoutNodes.TryGetValue(node.Id, out var layout))
                continue;

            var nodeBrush = node.Brush ?? NodeBrush ?? GetSeriesBrush(nodeIndex);
            var nodeR = node.Radius > 0 ? node.Radius : radius;
            var center = new Point(layout.X, layout.Y);

            dc.DrawEllipse(nodeBrush, null, center, nodeR, nodeR);

            // Draw label below node
            if (ShowLabels)
            {
                var labelText = node.Label ?? node.Id;
                if (!string.IsNullOrEmpty(labelText))
                {
                    var ft = new FormattedText(labelText, fontFamily, 11.0)
                    {
                        Foreground = s_defaultLabelBrush
                    };
                    TextMeasurement.MeasureText(ft);

                    double lx = layout.X - ft.Width / 2.0;
                    double ly = layout.Y + nodeR + 3;
                    dc.DrawText(ft, new Point(lx, ly));
                }
            }

            nodeIndex++;
        }
    }

    #endregion

    #region Drag Interaction

    private void OnGraphMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsNodeDraggable || e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(this);
        var nodes = (ObservableCollection<NetworkNode>?)GetValue(NodesProperty);
        if (nodes == null)
            return;

        foreach (var node in nodes)
        {
            if (!_layoutNodes.TryGetValue(node.Id, out var layout))
                continue;

            double dx = pos.X - layout.X;
            double dy = pos.Y - layout.Y;
            double r = node.Radius > 0 ? node.Radius : NodeRadius;
            if (dx * dx + dy * dy <= r * r)
            {
                _draggedNode = node;
                _dragOffset = new Point(dx, dy);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }
    }

    private void OnGraphMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedNode != null && e.ChangedButton == MouseButton.Left)
        {
            _draggedNode = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnGraphMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedNode == null)
            return;

        var pos = e.GetPosition(this);
        if (_layoutNodes.TryGetValue(_draggedNode.Id, out var layout))
        {
            layout.X = pos.X - _dragOffset.X;
            layout.Y = pos.Y - _dragOffset.Y;
            _draggedNode.X = layout.X;
            _draggedNode.Y = layout.Y;
            InvalidateVisual();
        }
    }

    #endregion
}
