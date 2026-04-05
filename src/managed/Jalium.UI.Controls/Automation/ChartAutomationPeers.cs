using Jalium.UI.Automation;
using Jalium.UI.Controls.Charts;

namespace Jalium.UI.Controls.Automation;

#region LineChart

/// <summary>
/// Exposes <see cref="LineChart"/> to UI Automation.
/// </summary>
public sealed class LineChartAutomationPeer : FrameworkElementAutomationPeer
{
    public LineChartAutomationPeer(LineChart owner) : base(owner) { }

    private LineChart ChartOwner => (LineChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(LineChart);

    protected override string GetLocalizedControlTypeCore() => "line chart";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region BarChart

/// <summary>
/// Exposes <see cref="BarChart"/> to UI Automation.
/// </summary>
public sealed class BarChartAutomationPeer : FrameworkElementAutomationPeer
{
    public BarChartAutomationPeer(BarChart owner) : base(owner) { }

    private BarChart ChartOwner => (BarChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(BarChart);

    protected override string GetLocalizedControlTypeCore() => "bar chart";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region PieChart

/// <summary>
/// Exposes <see cref="PieChart"/> to UI Automation.
/// </summary>
public sealed class PieChartAutomationPeer : FrameworkElementAutomationPeer
{
    public PieChartAutomationPeer(PieChart owner) : base(owner) { }

    private PieChart ChartOwner => (PieChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(PieChart);

    protected override string GetLocalizedControlTypeCore() => "pie chart";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region ScatterPlot

/// <summary>
/// Exposes <see cref="ScatterPlot"/> to UI Automation.
/// </summary>
public sealed class ScatterPlotAutomationPeer : FrameworkElementAutomationPeer
{
    public ScatterPlotAutomationPeer(ScatterPlot owner) : base(owner) { }

    private ScatterPlot ChartOwner => (ScatterPlot)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(ScatterPlot);

    protected override string GetLocalizedControlTypeCore() => "scatter plot";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region Heatmap

/// <summary>
/// Exposes <see cref="Heatmap"/> to UI Automation.
/// </summary>
public sealed class HeatmapAutomationPeer : FrameworkElementAutomationPeer
{
    public HeatmapAutomationPeer(Heatmap owner) : base(owner) { }

    private Heatmap ChartOwner => (Heatmap)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(Heatmap);

    protected override string GetLocalizedControlTypeCore() => "heatmap";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region Sparkline

/// <summary>
/// Exposes <see cref="Sparkline"/> to UI Automation.
/// </summary>
public sealed class SparklineAutomationPeer : FrameworkElementAutomationPeer
{
    public SparklineAutomationPeer(Sparkline owner) : base(owner) { }

    private Sparkline SparklineOwner => (Sparkline)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(Sparkline);

    protected override string GetLocalizedControlTypeCore() => "sparkline";

    protected override string GetNameCore()
        => base.GetNameCore();
}

#endregion

#region GaugeChart

/// <summary>
/// Exposes <see cref="GaugeChart"/> to UI Automation.
/// </summary>
public sealed class GaugeChartAutomationPeer : FrameworkElementAutomationPeer
{
    public GaugeChartAutomationPeer(GaugeChart owner) : base(owner) { }

    private GaugeChart ChartOwner => (GaugeChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(GaugeChart);

    protected override string GetLocalizedControlTypeCore() => "gauge chart";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region TreeMap

/// <summary>
/// Exposes <see cref="TreeMap"/> to UI Automation.
/// </summary>
public sealed class TreeMapAutomationPeer : FrameworkElementAutomationPeer
{
    public TreeMapAutomationPeer(TreeMap owner) : base(owner) { }

    private TreeMap ChartOwner => (TreeMap)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(TreeMap);

    protected override string GetLocalizedControlTypeCore() => "tree map";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region CandlestickChart

/// <summary>
/// Exposes <see cref="CandlestickChart"/> to UI Automation.
/// </summary>
public sealed class CandlestickChartAutomationPeer : FrameworkElementAutomationPeer
{
    public CandlestickChartAutomationPeer(CandlestickChart owner) : base(owner) { }

    private CandlestickChart ChartOwner => (CandlestickChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(CandlestickChart);

    protected override string GetLocalizedControlTypeCore() => "candlestick chart";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region NetworkGraph

/// <summary>
/// Exposes <see cref="NetworkGraph"/> to UI Automation.
/// </summary>
public sealed class NetworkGraphAutomationPeer : FrameworkElementAutomationPeer
{
    public NetworkGraphAutomationPeer(NetworkGraph owner) : base(owner) { }

    private NetworkGraph ChartOwner => (NetworkGraph)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(NetworkGraph);

    protected override string GetLocalizedControlTypeCore() => "network graph";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region GanttChart

/// <summary>
/// Exposes <see cref="GanttChart"/> to UI Automation.
/// </summary>
public sealed class GanttChartAutomationPeer : FrameworkElementAutomationPeer
{
    public GanttChartAutomationPeer(GanttChart owner) : base(owner) { }

    private GanttChart ChartOwner => (GanttChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(GanttChart);

    protected override string GetLocalizedControlTypeCore() => "gantt chart";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion

#region SankeyDiagram

/// <summary>
/// Exposes <see cref="SankeyDiagram"/> to UI Automation.
/// </summary>
public sealed class SankeyDiagramAutomationPeer : FrameworkElementAutomationPeer
{
    public SankeyDiagramAutomationPeer(SankeyDiagram owner) : base(owner) { }

    private SankeyDiagram ChartOwner => (SankeyDiagram)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Custom;

    protected override string GetClassNameCore() => nameof(SankeyDiagram);

    protected override string GetLocalizedControlTypeCore() => "sankey diagram";

    protected override string GetNameCore()
        => ChartOwner.Title ?? base.GetNameCore();
}

#endregion
