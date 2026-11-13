using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildDiagramsSection() => new GallerySampleDiagramsSection();
}

/// <summary>Templated diagrams with independently replaceable sample data.</summary>
[TemplatePart(Name = "PART_Sankey", Type = typeof(SankeyDiagram))]
[TemplatePart(Name = "PART_Gantt", Type = typeof(GanttChart))]
[TemplatePart(Name = "PART_Network", Type = typeof(NetworkGraph))]
[TemplatePart(Name = "PART_Flowchart", Type = typeof(FlowchartDiagram))]
[TemplatePart(Name = "PART_Mermaid", Type = typeof(MermaidDiagram))]
public class GallerySampleDiagramsSection : GallerySampleHeader
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(GallerySampleDiagramsData), typeof(GallerySampleDiagramsSection), new PropertyMetadata(null));
    public GallerySampleDiagramsData? Data { get => (GallerySampleDiagramsData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public GallerySampleDiagramsSection() { DefaultStyleKey = typeof(GallerySampleDiagramsSection); SetCurrentValue(DataProperty, new GallerySampleDiagramsData()); }
}

/// <summary>Editable diagram models, independent of the view template.</summary>
public sealed class GallerySampleDiagramsData
{
    public ObservableCollection<SankeyNode> SankeyNodes { get; } = new();
    public ObservableCollection<SankeyLink> SankeyLinks { get; } = new();
    public ObservableCollection<GanttTask> Tasks { get; } = new();
    public ObservableCollection<NetworkNode> NetworkNodes { get; } = new();
    public ObservableCollection<NetworkLink> NetworkLinks { get; } = new();
    public ObservableCollection<FlowchartNode> FlowNodes { get; } = new();
    public ObservableCollection<FlowchartEdge> FlowEdges { get; } = new();
    public string MermaidSource { get; set; } = "graph TD;\n  A[Start] --> B{OK?};\n  B -->|yes| C[Ship];\n  B -->|no| D[Fix];";
    public GallerySampleDiagramsData()
    {
        SankeyNodes.Add(new SankeyNode { Id = "visits", Label = "Visits" }); SankeyNodes.Add(new SankeyNode { Id = "signup", Label = "Sign-ups" });
        SankeyNodes.Add(new SankeyNode { Id = "paid", Label = "Paid" }); SankeyNodes.Add(new SankeyNode { Id = "churn", Label = "Churned" });
        SankeyLinks.Add(new SankeyLink { SourceId = "visits", TargetId = "signup", Value = 60 }); SankeyLinks.Add(new SankeyLink { SourceId = "signup", TargetId = "paid", Value = 35 }); SankeyLinks.Add(new SankeyLink { SourceId = "signup", TargetId = "churn", Value = 25 });
        var start = new DateTime(2026, 1, 1);
        Tasks.Add(new GanttTask { Id = "1", Name = "Design", StartDate = start, EndDate = start.AddDays(5), Progress = 1 });
        Tasks.Add(new GanttTask { Id = "2", Name = "Build", StartDate = start.AddDays(4), EndDate = start.AddDays(12), Progress = .6 });
        Tasks.Add(new GanttTask { Id = "3", Name = "Test", StartDate = start.AddDays(10), EndDate = start.AddDays(16), Progress = .2 });
        Tasks.Add(new GanttTask { Id = "4", Name = "Ship", StartDate = start.AddDays(16), EndDate = start.AddDays(18), Progress = 0 });
        foreach (var id in new[] { "UI", "API", "Auth", "DB", "Cache" }) NetworkNodes.Add(new NetworkNode { Id = id, Label = id });
        foreach (var (source, target) in new[] { ("UI", "API"), ("API", "Auth"), ("API", "DB"), ("API", "Cache") }) NetworkLinks.Add(new NetworkLink { SourceId = source, TargetId = target, Weight = 1 });
        FlowNodes.Add(new FlowchartNode("start", "Start")); FlowNodes.Add(new FlowchartNode("process", "Process")); FlowNodes.Add(new FlowchartNode("done", "Done"));
        FlowEdges.Add(new FlowchartEdge("start", "process")); FlowEdges.Add(new FlowchartEdge("process", "done", "ok"));
    }
}
