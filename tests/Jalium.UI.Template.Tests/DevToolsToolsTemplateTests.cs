using System.Runtime.CompilerServices;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsToolsTemplateTests
{
    public DevToolsToolsTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void DefaultToolsAreInactiveUntilAConsumerProvidesCommands()
    {
        var view = new DevToolsToolsView(); Layout(view);
        Assert.NotNull(view.Template); Assert.False(view.RulerCommand.CanExecute(null));
        Assert.False(Part<DevToolsActionButton>(view, "PART_RulerAction").IsEnabled);
        Assert.Equal(Visibility.Collapsed, Part<DevToolsMeasurementView>(view, "PART_Measurement").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<DevToolsColorSampleView>(view, "PART_ColorSample").Visibility);
        Assert.Equal("NoTool", VisualStateManager.GetCurrentStateName(view, "ToolStates"));
        Assert.Equal("Target window tools", view.GetAutomationPeer()!.GetName());
        AutomationProperties.SetName(view, "Custom tools"); Assert.Equal("Custom tools", view.GetAutomationPeer()!.GetName());
    }

    [Theory]
    [InlineData(320)] [InlineData(480)] [InlineData(1040)]
    public void ToolCardsSwitchLayoutBeforeMeasurementAndStayWithinTheViewport(double width)
    {
        var view = new DevToolsToolsView(); Layout(view, width);
        var ruler = Part<DevToolsSurface>(view, "PART_RulerCard"); var picker = Part<DevToolsSurface>(view, "PART_PickerCard");
        var render = Part<DevToolsSurface>(view, "PART_RenderCard"); var focus = Part<DevToolsSurface>(view, "PART_FocusCard");
        bool compact = width <= view.CompactWidth;
        Assert.Equal(compact ? "Compact" : "Wide", VisualStateManager.GetCurrentStateName(view, "LayoutStates"));
        Assert.Equal(compact ? 1 : 0, Grid.GetRow(picker)); Assert.Equal(compact ? 3 : 2, Grid.GetRow(focus));
        foreach (var card in new[] { ruler, picker, render, focus })
        {
            var position = card.TranslatePoint(new Point(), view);
            Assert.True(position.X >= -.5 && position.X + card.ActualWidth <= width + .5, $"{card.Heading}: {position}/{card.RenderSize}; width={width}");
        }
        Layout(view, compact ? 1040 : 480);
        Assert.Equal(compact ? 2 : 0, Grid.GetColumn(picker));
    }

    [Fact]
    public void CommandsPublishTypedRequestsAndStateFeedbackComesFromTheConsumer()
    {
        var view = new DevToolsToolsView(); var provider = new Provider();
        provider.ExecuteAction = request =>
        {
            if (request.Action == DevToolsToolAction.ToggleRuler) view.ToolMode = DevToolsToolMode.Ruler;
            if (request.Action == DevToolsToolAction.ToggleColorPicker) view.ToolMode = DevToolsToolMode.ColorPicker;
        };
        view.ActionCommand = provider; Layout(view);
        Assert.True(view.RulerCommand.CanExecute(null)); view.RulerCommand.Execute(null);
        Assert.Equal(DevToolsToolAction.ToggleRuler, provider.Requests.Single().Action);
        Assert.Equal("Stop ruler", Part<DevToolsActionButton>(view, "PART_RulerAction").Label);
        view.ColorPickerCommand.Execute(null);
        Assert.Equal("Start ruler", Part<DevToolsActionButton>(view, "PART_RulerAction").Label);
        Assert.Equal("Stop picker", Part<DevToolsActionButton>(view, "PART_PickerAction").Label);
        view.OverlayMode = RenderDiagnostics.OverlayMode.Overdraw;
        Assert.True(Part<DevToolsActionButton>(view, "PART_OverdrawAction").IsActive);
        view.OverlayMode = RenderDiagnostics.OverlayMode.DirtyRegions;
        Assert.False(Part<DevToolsActionButton>(view, "PART_OverdrawAction").IsActive);
        Assert.Equal("On", Part<DevToolsActionButton>(view, "PART_DirtyAction").GetAutomationPeer()!.GetItemStatus());
        view.IsFocusTracking = true; Assert.Equal("Tracking", Part<DevToolsActionButton>(view, "PART_FocusAction").GetAutomationPeer()!.GetItemStatus());
        Assert.False(view.CopyArgbCommand.CanExecute("#FF010203"));
        Assert.True(DevToolsColorSample.TryCreate(Color.FromRgb(1, 2, 3), new(), out var sample)); view.ColorSample = sample;
        view.CopyArgbCommand.Execute(sample.ArgbHex);
        Assert.Equal(new DevToolsToolRequest(DevToolsToolAction.CopyArgb, sample.ArgbHex), provider.Requests.Last());
    }

    [Fact]
    public void CommandReentryIsRejectedAndErrorsAndAvailabilityStayObservable()
    {
        var view = new DevToolsToolsView(); var first = new Provider(); view.ActionCommand = first; Layout(view);
        first.ExecuteAction = _ => { Assert.True(view.IsExecuting); view.RulerCommand.Execute(null); throw new InvalidOperationException("Target unavailable"); };
        view.RulerCommand.Execute(null);
        Assert.Single(first.Requests); Assert.False(view.IsExecuting); Assert.Equal("Target unavailable", view.LastError);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Error").Visibility);
        first.Available = false; first.Notify(); Assert.False(Part<DevToolsActionButton>(view, "PART_RulerAction").IsEnabled);
        var second = new Provider(); view.ActionCommand = second;
        Assert.Equal(0, first.Observers); Assert.Equal(1, second.Observers);
        view.RulerCommand.Execute(null); Assert.Equal("", view.LastError);
        view.IsEnabled = false; Assert.False(view.FocusCommand.CanExecute(null));
        view.IsEnabled = true; Assert.True(view.FocusCommand.CanExecute(null));
        view.ActionCommand = null; Assert.Equal(0, second.Observers);
    }

    [Fact]
    public void ResultsStatusesAndMissingTemplatePartsRemainIndependent()
    {
        var view = new DevToolsToolsView(); Layout(view, 480);
        Assert.True(DevToolsRulerMeasurement.TryCreate(new(), new(3, 4), false, out var measurement)); view.Measurement = measurement;
        Assert.True(view.HasMeasurement); Assert.Same(measurement, Part<DevToolsMeasurementView>(view, "PART_Measurement").Measurement);
        view.HasPickerError = true; view.PickerStatusText = "Sampling unavailable"; view.StatusText = "Saved capture";
        Assert.Equal(Assert.IsType<SolidColorBrush>(view.FindResource("DevTools.Error")).Color,
            Assert.IsType<SolidColorBrush>(Part<TextBlock>(view, "PART_PickerStatus").Foreground).Color);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Status").Visibility);
        var original = view.Template; var empty = new ControlTemplate(typeof(DevToolsToolsView)); empty.SetVisualTree(() => new Border());
        view.Template = empty; Layout(view); view.ToolMode = DevToolsToolMode.Ruler;
        view.Template = original; Layout(view, 480);
        Assert.Equal("Stop ruler", Part<DevToolsActionButton>(view, "PART_RulerAction").Label);
        Assert.Equal("5.0", Part<TextBlock>(Part<DevToolsMeasurementView>(view, "PART_Measurement"), "PART_Distance").Text);
        Assert.Equal("Saved capture", Part<TextBlock>(view, "PART_Status").Text);
    }

    [Fact]
    public void CommandProviderDoesNotKeepADiscardedToolsViewAlive()
    {
        var provider = new Provider(); var (weak, command) = CreateAndRelease(provider);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.TryGetTarget(out _)); provider.Notify(); Assert.Equal(0, provider.Observers);
        Assert.False(command.CanExecute(null)); GC.KeepAlive(provider);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<DevToolsToolsView>, ICommand) CreateAndRelease(Provider provider)
    {
        var view = new DevToolsToolsView { ActionCommand = provider };
        return (new(view), view.RulerCommand);
    }
    private static T Part<T>(Control view, string name) where T : class => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static void Layout(UIElement element, double width = 1040) { for (int i = 0; i < 3; i++) { element.InvalidateMeasure(); element.Measure(new Size(width, 700)); element.Arrange(new Rect(0, 0, width, 700)); element.UpdateLayout(); } }
    private sealed class Provider : ICommand
    {
        private EventHandler? _changed;
        public bool Available = true;
        public int Observers => _changed?.GetInvocationList().Length ?? 0;
        public List<DevToolsToolRequest> Requests = [];
        public Action<DevToolsToolRequest>? ExecuteAction;
        public event EventHandler? CanExecuteChanged { add => _changed += value; remove => _changed -= value; }
        public bool CanExecute(object? parameter) => Available && parameter is DevToolsToolRequest;
        public void Execute(object? parameter) { var request = Assert.IsType<DevToolsToolRequest>(parameter); Requests.Add(request); ExecuteAction?.Invoke(request); }
        public void Notify() => _changed?.Invoke(this, EventArgs.Empty);
    }
}
