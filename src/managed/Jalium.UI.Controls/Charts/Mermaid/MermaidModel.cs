using System.Collections.Generic;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Identifies the kind of diagram described by a block of mermaid source.
/// </summary>
public enum MermaidDiagramKind
{
    /// <summary>The source could not be recognized as a supported diagram.</summary>
    Unknown,

    /// <summary>A <c>flowchart</c> / <c>graph</c> diagram.</summary>
    Flowchart,

    /// <summary>A <c>pie</c> chart.</summary>
    Pie
}

/// <summary>
/// Represents a single labelled slice parsed from a mermaid <c>pie</c> diagram.
/// </summary>
public sealed class MermaidPieSlice
{
    /// <summary>Initializes a new <see cref="MermaidPieSlice"/>.</summary>
    public MermaidPieSlice(string label, double value)
    {
        Label = label;
        Value = value;
    }

    /// <summary>Gets the slice label.</summary>
    public string Label { get; }

    /// <summary>Gets the slice value.</summary>
    public double Value { get; }
}

/// <summary>
/// The parsed contents of a mermaid <c>pie</c> diagram.
/// </summary>
public sealed class MermaidPieModel
{
    /// <summary>Gets or sets the optional chart title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets whether the source requested raw values via <c>showData</c>.</summary>
    public bool ShowData { get; set; }

    /// <summary>Gets the parsed slices in source order.</summary>
    public List<MermaidPieSlice> Slices { get; } = new();
}

/// <summary>
/// The parsed contents of a mermaid <c>flowchart</c> / <c>graph</c> diagram.
/// </summary>
public sealed class MermaidFlowchartModel
{
    /// <summary>Gets or sets the diagram direction.</summary>
    public FlowchartDirection Direction { get; set; } = FlowchartDirection.TopToBottom;

    /// <summary>Gets the nodes in first-seen order.</summary>
    public List<FlowchartNode> Nodes { get; } = new();

    /// <summary>Gets the edges in source order.</summary>
    public List<FlowchartEdge> Edges { get; } = new();
}

/// <summary>
/// The result of parsing a block of mermaid source. Exactly one of <see cref="Flowchart"/>
/// or <see cref="Pie"/> is populated when <see cref="Kind"/> identifies a supported diagram.
/// </summary>
public sealed class MermaidDocument
{
    /// <summary>Gets the recognized diagram kind.</summary>
    public MermaidDiagramKind Kind { get; init; } = MermaidDiagramKind.Unknown;

    /// <summary>Gets the parsed flowchart, when <see cref="Kind"/> is <see cref="MermaidDiagramKind.Flowchart"/>.</summary>
    public MermaidFlowchartModel? Flowchart { get; init; }

    /// <summary>Gets the parsed pie chart, when <see cref="Kind"/> is <see cref="MermaidDiagramKind.Pie"/>.</summary>
    public MermaidPieModel? Pie { get; init; }

    /// <summary>Gets an optional human-readable message describing why parsing failed.</summary>
    public string? Error { get; init; }

    /// <summary>Gets whether the document represents a successfully parsed, supported diagram.</summary>
    public bool IsSupported => Kind != MermaidDiagramKind.Unknown;
}
