using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ResizeGeometryThemedTests
{
    [Fact]
    public void ThemeTemplates_RepeatedExtremeResize_CompleteLayoutAndRendering()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                TickFrequency = 10,
                TickPlacement = TickPlacement.BottomRight,
            };
            var rangeSlider = new RangeSlider
            {
                Minimum = 0,
                Maximum = 100,
                RangeStart = 25,
                RangeEnd = 75,
                TickFrequency = 10,
            };
            var commandBar = new CommandBar();
            commandBar.PrimaryCommands.Add(new AppBarButton { Label = "Action" });
            var menuBar = new MenuBar();
            menuBar.Items.Add(new MenuBarItem { Title = "File" });
            var tabControl = new TabControl();
            tabControl.Items.Add(new TabItem { Header = "Tab", Content = "Content" });

            FrameworkElement[] controls =
            [
                new NumberBox { Header = "Number", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline },
                new DatePicker { Header = "Date" },
                new TimePicker { Header = "Time" },
                new TextBox { PlaceholderText = "Placeholder" },
                new SymbolIcon { Symbol = Symbol.Accept },
                slider,
                rangeSlider,
                commandBar,
                menuBar,
                tabControl,
                new Calendar { DisplayMode = CalendarMode.Decade },
                new DiffViewer(),
            ];

            foreach (var control in controls)
            {
                control.Width = double.NaN;
                control.Height = double.NaN;
                control.MinWidth = 0;
                control.MinHeight = 0;

                foreach (var size in new[]
                {
                    new Size(320, 180),
                    new Size(20, 104.51953125),
                    new Size(8, 8),
                    new Size(1, 96),
                    new Size(96, 1),
                    new Size(0, 0),
                    new Size(320, 180),
                })
                {
                    control.Measure(size);
                    control.Arrange(new Rect(size));

                    var drawing = new DrawingGroup();
                    using var drawingContext = drawing.Open();
                    control.Render(drawingContext);
                }
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?
            .SetValue(null, null);
        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?
            .Invoke(null, null);
    }
}
