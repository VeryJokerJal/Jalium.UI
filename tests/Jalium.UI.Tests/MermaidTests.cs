using System.Linq;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MermaidTests
{
    private const string FlowchartSource = """
        flowchart TD
            A[开始] --> B{条件判断}
            B -->|是| C[执行操作 A]
            B -->|否| D[执行操作 B]
            C --> E{子条件}
            D --> F[记录日志]
            E -->|通过| G[提交结果]
            E -->|未通过| H[回滚操作]
            H --> D
            F --> G
            G --> I([结束])
        """;

    private const string PieSource = """
        pie title 编程语言使用分布
            "TypeScript" : 35
            "Python" : 25
            "Go" : 15
            "Rust" : 12
            "Java" : 8
            "其他" : 5
        """;

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void MermaidParser_Flowchart_ParsesNodesEdgesAndDirection()
    {
        var doc = MermaidParser.Parse(FlowchartSource);

        Assert.Equal(MermaidDiagramKind.Flowchart, doc.Kind);
        Assert.NotNull(doc.Flowchart);
        var model = doc.Flowchart!;

        Assert.Equal(FlowchartDirection.TopToBottom, model.Direction);
        Assert.Equal(9, model.Nodes.Count);
        Assert.Equal(10, model.Edges.Count);

        Assert.Equal(FlowchartNodeShape.Rectangle, model.Nodes.Single(n => n.Id == "A").Shape);
        Assert.Equal(FlowchartNodeShape.Rhombus, model.Nodes.Single(n => n.Id == "B").Shape);
        Assert.Equal(FlowchartNodeShape.Stadium, model.Nodes.Single(n => n.Id == "I").Shape);
        Assert.Equal("开始", model.Nodes.Single(n => n.Id == "A").DisplayText);

        var labelledEdge = model.Edges.Single(e => e.SourceId == "B" && e.TargetId == "C");
        Assert.Equal("是", labelledEdge.Label);
        Assert.True(labelledEdge.HasArrowHead);
    }

    [Fact]
    public void MermaidParser_Flowchart_ParsesShapesAndStyles()
    {
        var doc = MermaidParser.Parse("""
            flowchart LR
                a[rect] --> b(round)
                c([stadium]) --> d{rhombus}
                e((circle)) --> f{{hex}}
                g[(db)] --> h[[sub]]
                a -.-> e
                a ==> c
                a --- g
            """);

        var model = Assert.IsType<MermaidFlowchartModel>(doc.Flowchart);
        Assert.Equal(FlowchartDirection.LeftToRight, model.Direction);

        Assert.Equal(FlowchartNodeShape.Rectangle, model.Nodes.Single(n => n.Id == "a").Shape);
        Assert.Equal(FlowchartNodeShape.RoundedRectangle, model.Nodes.Single(n => n.Id == "b").Shape);
        Assert.Equal(FlowchartNodeShape.Stadium, model.Nodes.Single(n => n.Id == "c").Shape);
        Assert.Equal(FlowchartNodeShape.Rhombus, model.Nodes.Single(n => n.Id == "d").Shape);
        Assert.Equal(FlowchartNodeShape.Circle, model.Nodes.Single(n => n.Id == "e").Shape);
        Assert.Equal(FlowchartNodeShape.Hexagon, model.Nodes.Single(n => n.Id == "f").Shape);
        Assert.Equal(FlowchartNodeShape.Cylinder, model.Nodes.Single(n => n.Id == "g").Shape);
        Assert.Equal(FlowchartNodeShape.Subroutine, model.Nodes.Single(n => n.Id == "h").Shape);

        Assert.Equal(FlowchartEdgeStyle.Dotted, model.Edges.Single(e => e.SourceId == "a" && e.TargetId == "e").Style);
        Assert.Equal(FlowchartEdgeStyle.Thick, model.Edges.Single(e => e.SourceId == "a" && e.TargetId == "c").Style);

        var openEdge = model.Edges.Single(e => e.SourceId == "a" && e.TargetId == "g");
        Assert.Equal(FlowchartEdgeStyle.Solid, openEdge.Style);
        Assert.False(openEdge.HasArrowHead);
    }

    [Fact]
    public void MermaidParser_Flowchart_ParsesInlineEdgeLabel()
    {
        var doc = MermaidParser.Parse("""
            flowchart TD
                A -- yes --> B
            """);

        var model = Assert.IsType<MermaidFlowchartModel>(doc.Flowchart);
        var edge = Assert.Single(model.Edges);
        Assert.Equal("yes", edge.Label);
        Assert.Equal("A", edge.SourceId);
        Assert.Equal("B", edge.TargetId);
    }

    [Fact]
    public void MermaidParser_Pie_ParsesTitleAndSlices()
    {
        var doc = MermaidParser.Parse(PieSource);

        Assert.Equal(MermaidDiagramKind.Pie, doc.Kind);
        var model = Assert.IsType<MermaidPieModel>(doc.Pie);

        Assert.Equal("编程语言使用分布", model.Title);
        Assert.Equal(6, model.Slices.Count);
        Assert.Equal("TypeScript", model.Slices[0].Label);
        Assert.Equal(35, model.Slices[0].Value);
        Assert.Equal("其他", model.Slices[5].Label);
        Assert.Equal(5, model.Slices[5].Value);
    }

    [Fact]
    public void MermaidParser_Unknown_ForUnsupportedDiagram()
    {
        var doc = MermaidParser.Parse("""
            sequenceDiagram
                Alice->>John: Hello John
            """);

        Assert.Equal(MermaidDiagramKind.Unknown, doc.Kind);
        Assert.False(doc.IsSupported);
        Assert.NotNull(doc.Error);
    }

    [Fact]
    public void MermaidDiagram_Flowchart_BuildsFlowchartContent()
    {
        var diagram = new MermaidDiagram { Source = FlowchartSource };

        Assert.Equal(MermaidDiagramKind.Flowchart, diagram.DiagramKind);
        var flow = Assert.IsType<FlowchartDiagram>(diagram.Content);
        Assert.Equal(9, flow.Nodes.Count);
        Assert.Equal(10, flow.Edges.Count);
    }

    [Fact]
    public void MermaidDiagram_Pie_BuildsPieChartContent()
    {
        var diagram = new MermaidDiagram { Source = PieSource };

        Assert.Equal(MermaidDiagramKind.Pie, diagram.DiagramKind);
        var pie = Assert.IsType<PieChart>(diagram.Content);
        Assert.Equal(6, pie.Series.DataPoints.Count);
        Assert.Equal("编程语言使用分布", pie.Title);
    }

    [Fact]
    public void MermaidDiagram_NoSource_HasUnknownKind()
    {
        var diagram = new MermaidDiagram();
        Assert.Equal(MermaidDiagramKind.Unknown, diagram.DiagramKind);
        Assert.Null(diagram.Content);
    }

    [Fact]
    public void FlowchartDiagram_EmptyWithTitle_MeasuresNonZero()
    {
        var flow = new FlowchartDiagram { Title = "Empty diagram" };
        flow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(flow.DesiredSize.Width > 0);
        Assert.True(flow.DesiredSize.Height > 0);
    }

    [Fact]
    public void Markdown_MermaidFence_RendersMermaidDiagram()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var markdown = new Markdown
            {
                Text = "```mermaid\n" + FlowchartSource + "\n```"
            };

            var host = new StackPanel { Width = 640, Height = 480 };
            host.Children.Add(markdown);
            host.Measure(new Size(640, 480));
            host.Arrange(new Rect(0, 0, 640, 480));

            Assert.True(ContainsVisualOfType<MermaidDiagram>(markdown));
            Assert.False(ContainsVisualOfType<MarkdownCodeBlockView>(markdown));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Markdown_InvalidMermaid_FallsBackToCodeView()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var markdown = new Markdown
            {
                Text = "```mermaid\nsequenceDiagram\n    Alice->>John: Hi\n```"
            };

            var host = new StackPanel { Width = 480, Height = 240 };
            host.Children.Add(markdown);
            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));

            Assert.False(ContainsVisualOfType<MermaidDiagram>(markdown));
            Assert.True(ContainsVisualOfType<MarkdownCodeBlockView>(markdown));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static bool ContainsVisualOfType<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T)
        {
            return true;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child != null && ContainsVisualOfType<T>(child))
            {
                return true;
            }
        }

        return false;
    }
}
