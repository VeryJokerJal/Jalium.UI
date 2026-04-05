using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays tasks on a timeline as a Gantt chart with dependency arrows,
/// progress indicators, milestones, and a today-line marker.
/// </summary>
public class GanttChart : AxisChartBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultTaskBrush = new(Color.FromRgb(0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_defaultMilestoneBrush = new(Color.FromRgb(0xFF, 0x9E, 0x22));
    private static readonly SolidColorBrush s_defaultDependencyBrush = new(Color.FromArgb(180, 0x90, 0x90, 0x90));
    private static readonly SolidColorBrush s_defaultTodayBrush = new(Color.FromRgb(0xE0, 0x3E, 0x3E));
    private static readonly SolidColorBrush s_defaultGroupHeaderBrush = new(Color.FromArgb(60, 0x60, 0x7D, 0x8B));
    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush s_altRowBrush = new(Color.FromArgb(15, 255, 255, 255));
    private static readonly SolidColorBrush s_progressBrush = new(Color.FromArgb(80, 255, 255, 255));

    #endregion

    #region Constants

    private const double LabelAreaWidth = 140.0;
    private const double MilestoneDiamondSize = 8.0;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Tasks dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TasksProperty =
        DependencyProperty.Register(nameof(Tasks), typeof(ObservableCollection<GanttTask>), typeof(GanttChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RowHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(GanttChart),
            new PropertyMetadata(30.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TaskBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TaskBrushProperty =
        DependencyProperty.Register(nameof(TaskBrush), typeof(Brush), typeof(GanttChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MilestoneBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MilestoneBrushProperty =
        DependencyProperty.Register(nameof(MilestoneBrush), typeof(Brush), typeof(GanttChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DependencyLineBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty DependencyLineBrushProperty =
        DependencyProperty.Register(nameof(DependencyLineBrush), typeof(Brush), typeof(GanttChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowDependencies dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowDependenciesProperty =
        DependencyProperty.Register(nameof(ShowDependencies), typeof(bool), typeof(GanttChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowProgress dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowProgressProperty =
        DependencyProperty.Register(nameof(ShowProgress), typeof(bool), typeof(GanttChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowToday dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowTodayProperty =
        DependencyProperty.Register(nameof(ShowToday), typeof(bool), typeof(GanttChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TodayLineBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TodayLineBrushProperty =
        DependencyProperty.Register(nameof(TodayLineBrush), typeof(Brush), typeof(GanttChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the GroupHeaderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GroupHeaderBrushProperty =
        DependencyProperty.Register(nameof(GroupHeaderBrush), typeof(Brush), typeof(GanttChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsTaskDraggable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsTaskDraggableProperty =
        DependencyProperty.Register(nameof(IsTaskDraggable), typeof(bool), typeof(GanttChart),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the BarCornerRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BarCornerRadiusProperty =
        DependencyProperty.Register(nameof(BarCornerRadius), typeof(double), typeof(GanttChart),
            new PropertyMetadata(3.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of tasks to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<GanttTask> Tasks
    {
        get
        {
            var t = (ObservableCollection<GanttTask>?)GetValue(TasksProperty);
            if (t == null)
            {
                t = new ObservableCollection<GanttTask>();
                SetValue(TasksProperty, t);
            }
            return t;
        }
        set => SetValue(TasksProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of each task row in pixels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty)!;
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the default brush for task bars.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TaskBrush
    {
        get => (Brush?)GetValue(TaskBrushProperty);
        set => SetValue(TaskBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for milestone diamonds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? MilestoneBrush
    {
        get => (Brush?)GetValue(MilestoneBrushProperty);
        set => SetValue(MilestoneBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for dependency arrow lines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? DependencyLineBrush
    {
        get => (Brush?)GetValue(DependencyLineBrushProperty);
        set => SetValue(DependencyLineBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether dependency arrows are shown between tasks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowDependencies
    {
        get => (bool)GetValue(ShowDependenciesProperty)!;
        set => SetValue(ShowDependenciesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether progress fill is shown on task bars.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty)!;
        set => SetValue(ShowProgressProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a vertical line is drawn at today's date.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowToday
    {
        get => (bool)GetValue(ShowTodayProperty)!;
        set => SetValue(ShowTodayProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the today-line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TodayLineBrush
    {
        get => (Brush?)GetValue(TodayLineBrushProperty);
        set => SetValue(TodayLineBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for group header backgrounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? GroupHeaderBrush
    {
        get => (Brush?)GetValue(GroupHeaderBrushProperty);
        set => SetValue(GroupHeaderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether tasks can be dragged to adjust dates.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool IsTaskDraggable
    {
        get => (bool)GetValue(IsTaskDraggableProperty)!;
        set => SetValue(IsTaskDraggableProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for task bar rendering.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double BarCornerRadius
    {
        get => (double)GetValue(BarCornerRadiusProperty)!;
        set => SetValue(BarCornerRadiusProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="GanttChart"/> class.
    /// </summary>
    public GanttChart()
    {
        PlotAreaMargin = new Thickness(10, 20, 20, 40);
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.GanttChartAutomationPeer(this);
    }

    #endregion

    #region Data Collection

    private List<GanttTask> GetFlatTasks()
    {
        var tasks = (ObservableCollection<GanttTask>?)GetValue(TasksProperty);
        if (tasks == null)
            return new List<GanttTask>();

        var result = new List<GanttTask>();
        foreach (var task in tasks)
        {
            FlattenTask(task, result);
        }
        return result;
    }

    private static void FlattenTask(GanttTask task, List<GanttTask> result)
    {
        result.Add(task);
        if (task.Children != null)
        {
            foreach (var child in task.Children)
            {
                FlattenTask(child, result);
            }
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectXValues()
    {
        var tasks = GetFlatTasks();
        foreach (var task in tasks)
        {
            yield return DateTimeAxis.DateTimeToDouble(task.StartDate);
            yield return DateTimeAxis.DateTimeToDouble(task.EndDate);
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectYValues()
    {
        // Y is implicit (row index), return empty to avoid interfering with axis
        yield break;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void RenderPlotContent(DrawingContext dc, Rect plotArea,
        double xMin, double xMax, double yMin, double yMax)
    {
        var tasks = GetFlatTasks();
        if (tasks.Count == 0)
            return;

        var defaultTask = TaskBrush ?? s_defaultTaskBrush;
        var milestoneBr = MilestoneBrush ?? s_defaultMilestoneBrush;
        var depBrush = DependencyLineBrush ?? s_defaultDependencyBrush;
        var todayBrush = TodayLineBrush ?? s_defaultTodayBrush;
        var groupBrush = GroupHeaderBrush ?? s_defaultGroupHeaderBrush;

        var xAxis = XAxis ?? new DateTimeAxis();
        double rowH = RowHeight;
        double cornerR = BarCornerRadius;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;

        // The gantt area has a label portion on the left, chart portion on the right
        double chartLeft = plotArea.Left + LabelAreaWidth;
        double chartWidth = plotArea.Width - LabelAreaWidth;
        if (chartWidth < 10) chartWidth = 10;

        var chartArea = new Rect(chartLeft, plotArea.Top, chartWidth, plotArea.Height);

        // Build task index map for dependency drawing
        var taskIndexMap = new Dictionary<string, int>();
        for (int i = 0; i < tasks.Count; i++)
        {
            if (!string.IsNullOrEmpty(tasks[i].Id))
                taskIndexMap[tasks[i].Id] = i;
        }

        // Draw alternating row backgrounds and task labels
        for (int i = 0; i < tasks.Count; i++)
        {
            double rowTop = plotArea.Top + i * rowH;
            if (rowTop > plotArea.Bottom)
                break;

            // Alternating row background
            if (i % 2 == 1)
            {
                dc.DrawRectangle(s_altRowBrush, null,
                    new Rect(plotArea.Left, rowTop, plotArea.Width, rowH));
            }

            // Task name label
            var task = tasks[i];
            var labelStr = task.Name;
            if (!string.IsNullOrEmpty(labelStr))
            {
                var ft = new FormattedText(labelStr, fontFamily, 11.0)
                {
                    Foreground = s_defaultLabelBrush
                };
                TextMeasurement.MeasureText(ft);

                // Truncate if too wide
                double lx = plotArea.Left + 5;
                double ly = rowTop + (rowH - ft.Height) / 2.0;
                dc.DrawText(ft, new Point(lx, ly));
            }
        }

        // Draw task bars
        for (int i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            double rowTop = plotArea.Top + i * rowH;
            if (rowTop > plotArea.Bottom)
                break;

            double startVal = DateTimeAxis.DateTimeToDouble(task.StartDate);
            double endVal = DateTimeAxis.DateTimeToDouble(task.EndDate);

            double startX = chartArea.Left + xAxis.ValueToPixel(startVal, xMin, xMax, chartArea.Width);
            double endX = chartArea.Left + xAxis.ValueToPixel(endVal, xMin, xMax, chartArea.Width);

            // Clamp to chart area
            startX = Math.Max(startX, chartArea.Left);
            endX = Math.Min(endX, chartArea.Right);

            if (task.IsMilestone)
            {
                // Draw diamond for milestone
                double cx = startX;
                double cy = rowTop + rowH / 2.0;
                double size = MilestoneDiamondSize;
                var diamondBrush = task.Brush ?? milestoneBr;

                var figure = new PathFigure
                {
                    StartPoint = new Point(cx, cy - size),
                    IsClosed = true,
                    IsFilled = true
                };
                figure.Segments.Add(new LineSegment(new Point(cx + size, cy), true));
                figure.Segments.Add(new LineSegment(new Point(cx, cy + size), true));
                figure.Segments.Add(new LineSegment(new Point(cx - size, cy), true));
                var geo = new PathGeometry();
                geo.Figures.Add(figure);
                dc.DrawGeometry(diamondBrush, null, geo);
            }
            else
            {
                // Draw task bar
                double barWidth = Math.Max(endX - startX, 2);
                double barPad = rowH * 0.2;
                double barTop = rowTop + barPad;
                double barHeight = rowH - barPad * 2;
                if (barHeight < 2) barHeight = 2;

                var taskBrush = task.Brush ?? defaultTask;
                var barRect = new Rect(startX, barTop, barWidth, barHeight);
                var cr = new CornerRadius(cornerR);
                dc.DrawRoundedRectangle(taskBrush, null, barRect, cr);

                // Draw progress fill
                if (ShowProgress && task.Progress > 0)
                {
                    double progWidth = barWidth * Math.Clamp(task.Progress, 0, 1);
                    if (progWidth > 0.5)
                    {
                        var progRect = new Rect(startX, barTop, progWidth, barHeight);
                        dc.DrawRoundedRectangle(s_progressBrush, null, progRect, cr);
                    }
                }
            }
        }

        // Draw dependency arrows
        if (ShowDependencies)
        {
            var depPen = new Pen(depBrush, 1.5);

            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                if (task.DependsOn == null || task.DependsOn.Count == 0)
                    continue;

                foreach (var depId in task.DependsOn)
                {
                    if (!taskIndexMap.TryGetValue(depId, out int depIndex))
                        continue;

                    var depTask = tasks[depIndex];

                    // Arrow from end of dependency to start of current task
                    double depEndVal = DateTimeAxis.DateTimeToDouble(depTask.EndDate);
                    double taskStartVal = DateTimeAxis.DateTimeToDouble(task.StartDate);

                    double fromX = chartArea.Left + xAxis.ValueToPixel(depEndVal, xMin, xMax, chartArea.Width);
                    double toX = chartArea.Left + xAxis.ValueToPixel(taskStartVal, xMin, xMax, chartArea.Width);

                    double fromY = plotArea.Top + depIndex * rowH + rowH / 2.0;
                    double toY = plotArea.Top + i * rowH + rowH / 2.0;

                    // Route: right from source, then down/up, then right to target
                    double midX = Math.Max(fromX + 5, (fromX + toX) / 2.0);

                    var figure = new PathFigure
                    {
                        StartPoint = new Point(fromX, fromY),
                        IsClosed = false,
                        IsFilled = false
                    };
                    figure.Segments.Add(new LineSegment(new Point(midX, fromY), true));
                    figure.Segments.Add(new LineSegment(new Point(midX, toY), true));
                    figure.Segments.Add(new LineSegment(new Point(toX, toY), true));

                    var geo = new PathGeometry();
                    geo.Figures.Add(figure);
                    dc.DrawGeometry(null, depPen, geo);

                    // Draw arrowhead at the target end
                    double arrowSize = 5;
                    var arrowFigure = new PathFigure
                    {
                        StartPoint = new Point(toX, toY),
                        IsClosed = true,
                        IsFilled = true
                    };
                    arrowFigure.Segments.Add(new LineSegment(new Point(toX - arrowSize, toY - arrowSize), true));
                    arrowFigure.Segments.Add(new LineSegment(new Point(toX - arrowSize, toY + arrowSize), true));
                    var arrowGeo = new PathGeometry();
                    arrowGeo.Figures.Add(arrowFigure);
                    dc.DrawGeometry(depBrush, null, arrowGeo);
                }
            }
        }

        // Draw today line
        if (ShowToday)
        {
            double todayVal = DateTimeAxis.DateTimeToDouble(DateTime.Today);
            if (todayVal >= xMin && todayVal <= xMax)
            {
                double todayX = chartArea.Left + xAxis.ValueToPixel(todayVal, xMin, xMax, chartArea.Width);
                var todayPen = new Pen(todayBrush, 2.0);
                todayPen.DashStyle = new DashStyle(new DoubleCollection { 4, 3 }, 0);
                dc.DrawLine(todayPen, new Point(todayX, plotArea.Top), new Point(todayX, plotArea.Bottom));
            }
        }
    }

    #endregion
}
