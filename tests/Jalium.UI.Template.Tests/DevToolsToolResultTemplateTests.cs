using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsToolResultTemplateTests
{
    public DevToolsToolResultTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void MeasurementUsesItsTemplateStatesAndAccessibleResult()
    {
        var view = new DevToolsMeasurementView(); Layout(view);
        Assert.NotNull(view.Template); Assert.False(view.HasMeasurement);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Empty").Visibility);
        Assert.Equal(AutomationControlType.Group, view.GetAutomationPeer()!.GetAutomationControlType());
        view.Measurement = Measure(new(3, 4), true); Layout(view);
        Assert.True(view.HasMeasurement); Assert.Equal("5.0", Part<TextBlock>(view, "PART_Distance").Text);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Snap").Visibility);
        Assert.Contains("Distance 5.0", view.GetAutomationPeer()!.GetName());
        view.AccentBrush = Brushes.Red;
        Assert.Same(Brushes.Red, Part<TextBlock>(view, "PART_Distance").Foreground);
        view.Measurement = null;
        Assert.False(view.HasMeasurement); Assert.Equal(Visibility.Collapsed, Part<StackPanel>(view, "PART_Result").Visibility);
        AutomationProperties.SetName(view, "Saved measurement"); Assert.Equal("Saved measurement", view.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void ColorSampleProjectsArgbChannelsAndAnOptionalCopyCommand()
    {
        var view = new DevToolsColorSampleView(); Layout(view);
        Assert.False(view.HasSample); Assert.Null(view.SwatchBrush);
        Assert.Equal(Visibility.Collapsed, Part<DevToolsActionButton>(view, "PART_Copy").Visibility);
        view.Sample = Sample(Color.FromArgb(128, 255, 0, 0)); Layout(view);
        Assert.Equal("#80FF0000", Part<TextBlock>(view, "PART_Hex").Text);
        Assert.Equal(view.Sample.Color, Assert.IsType<SolidColorBrush>(Part<Border>(view, "PART_Swatch").Background).Color);
        var command = new CopyRecorder(); view.CopyCommand = command;
        var copy = Part<DevToolsActionButton>(view, "PART_Copy");
        Assert.Equal(Visibility.Visible, copy.Visibility); Assert.Same(command, copy.Command); Assert.Equal(view.Sample.ArgbHex, copy.CommandParameter);
        view.Sample = Sample(Color.FromRgb(0, 0, 255)); Layout(view);
        Assert.Equal("#FF0000FF", copy.CommandParameter); copy.Command!.Execute(copy.CommandParameter); Assert.Equal("#FF0000FF", command.Value);
        Assert.Contains("blue 255", view.GetAutomationPeer()!.GetName());
        Assert.Throws<InvalidOperationException>(() => view.SetValue(DevToolsColorSampleView.SwatchBrushProperty, Brushes.Red));
        view.CopyCommand = null; Assert.Equal(Visibility.Collapsed, copy.Visibility);
        view.Sample = null; Assert.False(view.HasSample); Assert.Null(view.SwatchBrush);
    }

    [Fact]
    public void TemplateReplacementReleasesOldMeasurementBindingsAndAcceptsMissingParts()
    {
        var view = new DevToolsMeasurementView { Measurement = Measure(new(3, 4)) }; Layout(view);
        var original = view.Template; var retiredText = Part<TextBlock>(view, "PART_Distance");
        var empty = new ControlTemplate(typeof(DevToolsMeasurementView)); empty.SetVisualTree(() => new Border());
        view.Template = empty; view.ApplyTemplate(); view.Measurement = Measure(new(6, 8)); Layout(view);
        Assert.Equal("5.0", retiredText.Text);
        view.Template = original; Layout(view);
        Assert.Equal("10.0", Part<TextBlock>(view, "PART_Distance").Text);
        var color = new DevToolsColorSampleView { Sample = Sample(Color.FromRgb(1, 2, 3)) }; Layout(color);
        var colorTemplate = color.Template;
        var colorEmpty = new ControlTemplate(typeof(DevToolsColorSampleView)); colorEmpty.SetVisualTree(() => new Border());
        color.Template = colorEmpty; color.ApplyTemplate(); color.Sample = Sample(Color.FromRgb(4, 5, 6));
        color.Template = colorTemplate; Layout(color);
        Assert.Equal("#FF040506", Part<TextBlock>(color, "PART_Hex").Text);
    }

    [Theory]
    [InlineData(240)] [InlineData(480)]
    public void MeasurementReadoutsKeepTheWholeNumberVisible(double width)
    {
        foreach (var end in new[] { new Point(120, 80), new Point(3e200, -4e200) })
        {
            var measurement = new DevToolsMeasurementView { Measurement = Measure(end) }; Layout(measurement, width);
            foreach (var name in new[] { "PART_DeltaX", "PART_DeltaY", "PART_Angle" })
            {
                var readout = Part<DevToolsReadout>(measurement, name); var value = Part<TextBlock>(readout, "PART_Value");
                var untrimmed = new TextBlock { Text = value.Text, FontSize = value.FontSize, FontFamily = value.FontFamily };
                untrimmed.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(value.ActualWidth + .5 >= untrimmed.DesiredSize.Width, $"{name}: {value.Text} was clipped at {width} DIP");
                Assert.True(value.TranslatePoint(new Point(value.ActualWidth, 0), measurement).X <= width + .5);
            }
        }
    }

    [Theory]
    [InlineData(240)] [InlineData(480)]
    public void ResultTemplatesFitCompactWidthsAndLargeFiniteCoordinates(double width)
    {
        var view = new DevToolsColorSampleView { Sample = Sample(Color.FromArgb(0, 255, 255, 255), new(1e200, -1e200)), CopyCommand = new CopyRecorder() }; Layout(view, width);
        Assert.InRange(view.DesiredSize.Width, 0, width + .5);
        var hex = Part<TextBlock>(view, "PART_Hex");
        Assert.True(hex.TranslatePoint(new Point(hex.ActualWidth, 0), view).X <= width + .5);
        var measurement = new DevToolsMeasurementView { Measurement = Measure(new(3e200, 4e200)) }; Layout(measurement, width);
        Assert.InRange(measurement.DesiredSize.Width, 0, width + .5); Assert.True(double.IsFinite(measurement.DesiredSize.Height));
    }

    private static DevToolsRulerMeasurement Measure(Point end, bool snapped = false) { Assert.True(DevToolsRulerMeasurement.TryCreate(new(), end, snapped, out var value)); return value; }
    private static DevToolsColorSample Sample(Color color, Point position = default) { Assert.True(DevToolsColorSample.TryCreate(color, position, out var value)); return value; }
    private static T Part<T>(Control view, string name) where T : class => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static void Layout(UIElement element, double width = 380) { for (int i = 0; i < 3; i++) { element.InvalidateMeasure(); element.Measure(new Size(width, 600)); element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height)); element.UpdateLayout(); } }
    private sealed class CopyRecorder : ICommand
    {
        public object? Value;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => parameter is string;
        public void Execute(object? parameter) => Value = parameter;
    }
}
