using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Diagram section: flow, network, gantt, sankey and mermaid-style diagrams, each
/// populated with a small in-memory model. Collections are filled with explicit
/// <c>.Add(...)</c> calls.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildDiagramsSection() => Section(
        "Diagrams",
        "Flow, network, gantt, sankey and mermaid-style diagrams from small sample models.",
        Card("SankeyDiagram", SankeyDemo(), width: 480),
        Card("GanttChart", GanttDemo(), width: 480),
        Card("NetworkGraph", NetworkDemo(), width: 400),
        Card("FlowchartDiagram", FlowchartDemo(), width: 400),
        Card("MermaidDiagram", MermaidDemo(), width: 420),
        Card("GeographicHeatmap", Placeholder("GeographicHeatmap", "Geographic intensity overlay built from weighted points and a color gradient.")));

    private static UIElement SankeyDemo()
    {
        var diagram = new SankeyDiagram { Width = 440, Height = 260 };
        diagram.Nodes.Add(new SankeyNode { Id = "visits", Label = "Visits" });
        diagram.Nodes.Add(new SankeyNode { Id = "signup", Label = "Sign-ups" });
        diagram.Nodes.Add(new SankeyNode { Id = "paid", Label = "Paid" });
        diagram.Nodes.Add(new SankeyNode { Id = "churn", Label = "Churned" });
        diagram.Links.Add(new SankeyLink { SourceId = "visits", TargetId = "signup", Value = 60 });
        diagram.Links.Add(new SankeyLink { SourceId = "signup", TargetId = "paid", Value = 35 });
        diagram.Links.Add(new SankeyLink { SourceId = "signup", TargetId = "churn", Value = 25 });
        return diagram;
    }

    private static UIElement GanttDemo()
    {
        var gantt = new GanttChart { Width = 440, Height = 220 };
        var start = new DateTime(2026, 1, 1);
        gantt.Tasks.Add(new GanttTask { Id = "1", Name = "Design", StartDate = start, EndDate = start.AddDays(5), Progress = 1.0 });
        gantt.Tasks.Add(new GanttTask { Id = "2", Name = "Build", StartDate = start.AddDays(4), EndDate = start.AddDays(12), Progress = 0.6 });
        gantt.Tasks.Add(new GanttTask { Id = "3", Name = "Test", StartDate = start.AddDays(10), EndDate = start.AddDays(16), Progress = 0.2 });
        gantt.Tasks.Add(new GanttTask { Id = "4", Name = "Ship", StartDate = start.AddDays(16), EndDate = start.AddDays(18), Progress = 0.0 });
        return gantt;
    }

    private static UIElement NetworkDemo()
    {
        var graph = new NetworkGraph { Width = 380, Height = 240 };
        foreach (var id in new[] { "UI", "API", "Auth", "DB", "Cache" })
            graph.Nodes.Add(new NetworkNode { Id = id, Label = id });
        graph.Links.Add(new NetworkLink { SourceId = "UI", TargetId = "API", Weight = 1 });
        graph.Links.Add(new NetworkLink { SourceId = "API", TargetId = "Auth", Weight = 1 });
        graph.Links.Add(new NetworkLink { SourceId = "API", TargetId = "DB", Weight = 1 });
        graph.Links.Add(new NetworkLink { SourceId = "API", TargetId = "Cache", Weight = 1 });
        return graph;
    }

    private static UIElement FlowchartDemo()
    {
        var flow = new FlowchartDiagram { Width = 380, Height = 240 };
        flow.Nodes.Add(new FlowchartNode("start", "Start"));
        flow.Nodes.Add(new FlowchartNode("process", "Process"));
        flow.Nodes.Add(new FlowchartNode("done", "Done"));
        flow.Edges.Add(new FlowchartEdge("start", "process"));
        flow.Edges.Add(new FlowchartEdge("process", "done", "ok"));
        return flow;
    }

    private static UIElement MermaidDemo()
    {
        return new MermaidDiagram
        {
            Source = "graph TD;\n  A[Start] --> B{OK?};\n  B -->|yes| C[Ship];\n  B -->|no| D[Fix];",
            Width = 400,
            Height = 240,
        };
    }
}
