using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A control that renders a <see href="https://mermaid.js.org/">mermaid</see> diagram from
/// its <see cref="Source"/> text. Flowcharts are rendered with a <see cref="FlowchartDiagram"/>
/// and pie charts with a <see cref="PieChart"/>; unsupported diagram kinds fall back to showing
/// the raw source so nothing is lost.
/// </summary>
public class MermaidDiagram : ContentControl
{
    private MermaidDiagramKind _kind = MermaidDiagramKind.Unknown;
    private string? _error;

    /// <summary>Identifies the <see cref="Source"/> dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(string), typeof(MermaidDiagram),
            new PropertyMetadata(string.Empty, OnSourceChanged));

    /// <summary>Gets or sets the mermaid source text describing the diagram.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Source
    {
        get => (string)(GetValue(SourceProperty) ?? string.Empty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets the diagram kind detected from the most recently parsed <see cref="Source"/>.</summary>
    public MermaidDiagramKind DiagramKind => _kind;

    /// <summary>Gets a human-readable parse error, when the source could not be rendered as a diagram.</summary>
    public string? ParseError => _error;

    /// <summary>Initializes a new instance of the <see cref="MermaidDiagram"/> class.</summary>
    public MermaidDiagram()
    {
        // Source defaults to empty; the diagram is (re)built when Source is assigned, which avoids
        // a redundant parse of the empty default under the common object-initializer usage.
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MermaidDiagram diagram)
        {
            diagram.Rebuild();
        }
    }

    private void Rebuild()
    {
        var document = MermaidParser.Parse(Source);
        _kind = document.Kind;
        _error = document.Error;

        Content = document.Kind switch
        {
            MermaidDiagramKind.Flowchart when document.Flowchart != null => BuildFlowchart(document.Flowchart),
            MermaidDiagramKind.Pie when document.Pie != null => BuildPie(document.Pie),
            _ => BuildFallback()
        };

        InvalidateMeasure();
    }

    private static FlowchartDiagram BuildFlowchart(MermaidFlowchartModel model)
    {
        var diagram = new FlowchartDiagram { Direction = model.Direction };
        foreach (var node in model.Nodes)
        {
            diagram.Nodes.Add(node);
        }
        foreach (var edge in model.Edges)
        {
            diagram.Edges.Add(edge);
        }
        return diagram;
    }

    private static PieChart BuildPie(MermaidPieModel model)
    {
        var chart = new PieChart
        {
            Title = model.Title,
            ShowLabels = true,
            LabelPosition = PieLabelPosition.Outside,
            LabelFormat = model.ShowData ? "{0}: {1:0.##}" : "{0}: {1:P0}"
        };

        foreach (var slice in model.Slices)
        {
            chart.Series.DataPoints.Add(new PieDataPoint
            {
                Label = slice.Label,
                Value = slice.Value
            });
        }

        return chart;
    }

    private UIElement BuildFallback()
    {
        return new TextBlock
        {
            Text = string.IsNullOrEmpty(Source) ? (_error ?? "Empty mermaid diagram.") : Source,
            FontFamily = new FontFamily("Cascadia Code"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Foreground
        };
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);

        // PieChart (and other charts without an intrinsic size) measure to ~0 inside an
        // auto-sized panel; give them a sensible default box so they actually render.
        if (size.Width < 1 || size.Height < 1)
        {
            var width = double.IsInfinity(availableSize.Width)
                ? 420.0
                : Math.Max(160.0, Math.Min(availableSize.Width, 560.0));
            var height = double.IsInfinity(availableSize.Height)
                ? 300.0
                : Math.Max(160.0, Math.Min(availableSize.Height, 360.0));

            ContentElement?.Measure(new Size(width, height));
            return new Size(width, height);
        }

        return size;
    }
}
