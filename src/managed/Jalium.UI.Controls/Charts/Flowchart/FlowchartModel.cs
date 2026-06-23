using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Specifies the primary layout direction of a <see cref="FlowchartDiagram"/>,
/// matching the mermaid flowchart direction codes (TB/TD, BT, LR, RL).
/// </summary>
public enum FlowchartDirection
{
    /// <summary>Ranks flow from top to bottom (mermaid <c>TB</c>/<c>TD</c>).</summary>
    TopToBottom,

    /// <summary>Ranks flow from bottom to top (mermaid <c>BT</c>).</summary>
    BottomToTop,

    /// <summary>Ranks flow from left to right (mermaid <c>LR</c>).</summary>
    LeftToRight,

    /// <summary>Ranks flow from right to left (mermaid <c>RL</c>).</summary>
    RightToLeft
}

/// <summary>
/// Specifies the rendered outline of a <see cref="FlowchartNode"/>. The names map to the
/// shapes available in mermaid flowchart syntax.
/// </summary>
public enum FlowchartNodeShape
{
    /// <summary>Sharp-cornered rectangle: <c>A[Text]</c>.</summary>
    Rectangle,

    /// <summary>Rounded rectangle: <c>A(Text)</c>.</summary>
    RoundedRectangle,

    /// <summary>Stadium / pill shape: <c>A([Text])</c>.</summary>
    Stadium,

    /// <summary>Subroutine (rectangle with double side bars): <c>A[[Text]]</c>.</summary>
    Subroutine,

    /// <summary>Cylinder / database: <c>A[(Text)]</c>.</summary>
    Cylinder,

    /// <summary>Circle: <c>A((Text))</c>.</summary>
    Circle,

    /// <summary>Rhombus / decision diamond: <c>A{Text}</c>.</summary>
    Rhombus,

    /// <summary>Hexagon: <c>A{{Text}}</c>.</summary>
    Hexagon,

    /// <summary>Parallelogram: <c>A[/Text/]</c>.</summary>
    Parallelogram,

    /// <summary>Trapezoid: <c>A[/Text\]</c>.</summary>
    Trapezoid,

    /// <summary>Asymmetric flag: <c>A&gt;Text]</c>.</summary>
    Asymmetric
}

/// <summary>
/// Specifies how a <see cref="FlowchartEdge"/> line is stroked.
/// </summary>
public enum FlowchartEdgeStyle
{
    /// <summary>Solid line (mermaid <c>--&gt;</c> / <c>---</c>).</summary>
    Solid,

    /// <summary>Dotted line (mermaid <c>-.-&gt;</c> / <c>-.-</c>).</summary>
    Dotted,

    /// <summary>Thick line (mermaid <c>==&gt;</c> / <c>===</c>).</summary>
    Thick
}

/// <summary>
/// Represents a single node (box) in a <see cref="FlowchartDiagram"/>.
/// </summary>
public class FlowchartNode : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string? _label;
    private FlowchartNodeShape _shape = FlowchartNodeShape.Rectangle;
    private Brush? _brush;

    /// <summary>Initializes a new, empty <see cref="FlowchartNode"/>.</summary>
    public FlowchartNode()
    {
    }

    /// <summary>Initializes a new <see cref="FlowchartNode"/> with the supplied values.</summary>
    public FlowchartNode(string id, string? label = null, FlowchartNodeShape shape = FlowchartNodeShape.Rectangle)
    {
        _id = id ?? string.Empty;
        _label = label;
        _shape = shape;
    }

    /// <summary>Gets or sets the unique identifier used to reference this node from edges.</summary>
    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value ?? string.Empty; OnPropertyChanged(nameof(Id)); } }
    }

    /// <summary>Gets or sets the text displayed inside the node. Falls back to <see cref="Id"/> when null.</summary>
    public string? Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); } }
    }

    /// <summary>Gets the text that should be rendered for this node.</summary>
    public string DisplayText => string.IsNullOrEmpty(_label) ? _id : _label!;

    /// <summary>Gets or sets the outline shape used when rendering the node.</summary>
    public FlowchartNodeShape Shape
    {
        get => _shape;
        set { if (_shape != value) { _shape = value; OnPropertyChanged(nameof(Shape)); } }
    }

    /// <summary>Gets or sets an optional fill brush overriding the diagram default.</summary>
    public Brush? Brush
    {
        get => _brush;
        set { if (_brush != value) { _brush = value; OnPropertyChanged(nameof(Brush)); } }
    }

    // Layout output (computed by FlowchartDiagram). Not part of the logical model.
    internal double X;
    internal double Y;
    internal double Width;
    internal double Height;
    internal int Rank;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected virtual void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Represents a directed edge (connector) between two <see cref="FlowchartNode"/> instances.
/// </summary>
public class FlowchartEdge : INotifyPropertyChanged
{
    private string _sourceId = string.Empty;
    private string _targetId = string.Empty;
    private string? _label;
    private FlowchartEdgeStyle _style = FlowchartEdgeStyle.Solid;
    private bool _hasArrowHead = true;
    private Brush? _brush;

    /// <summary>Initializes a new, empty <see cref="FlowchartEdge"/>.</summary>
    public FlowchartEdge()
    {
    }

    /// <summary>Initializes a new <see cref="FlowchartEdge"/> with the supplied values.</summary>
    public FlowchartEdge(string sourceId, string targetId, string? label = null,
        FlowchartEdgeStyle style = FlowchartEdgeStyle.Solid, bool hasArrowHead = true)
    {
        _sourceId = sourceId ?? string.Empty;
        _targetId = targetId ?? string.Empty;
        _label = label;
        _style = style;
        _hasArrowHead = hasArrowHead;
    }

    /// <summary>Gets or sets the id of the source node.</summary>
    public string SourceId
    {
        get => _sourceId;
        set { if (_sourceId != value) { _sourceId = value ?? string.Empty; OnPropertyChanged(nameof(SourceId)); } }
    }

    /// <summary>Gets or sets the id of the target node.</summary>
    public string TargetId
    {
        get => _targetId;
        set { if (_targetId != value) { _targetId = value ?? string.Empty; OnPropertyChanged(nameof(TargetId)); } }
    }

    /// <summary>Gets or sets an optional label drawn near the middle of the edge.</summary>
    public string? Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); } }
    }

    /// <summary>Gets or sets the stroke style of the edge.</summary>
    public FlowchartEdgeStyle Style
    {
        get => _style;
        set { if (_style != value) { _style = value; OnPropertyChanged(nameof(Style)); } }
    }

    /// <summary>Gets or sets whether an arrow head is drawn at the target end.</summary>
    public bool HasArrowHead
    {
        get => _hasArrowHead;
        set { if (_hasArrowHead != value) { _hasArrowHead = value; OnPropertyChanged(nameof(HasArrowHead)); } }
    }

    /// <summary>Gets or sets an optional stroke brush overriding the diagram default.</summary>
    public Brush? Brush
    {
        get => _brush;
        set { if (_brush != value) { _brush = value; OnPropertyChanged(nameof(Brush)); } }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected virtual void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
