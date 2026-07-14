using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class ScrollBarParityTests
{
    private static readonly string[] s_commandFields =
    [
        nameof(ScrollBar.LineUpCommand), nameof(ScrollBar.LineDownCommand),
        nameof(ScrollBar.LineLeftCommand), nameof(ScrollBar.LineRightCommand),
        nameof(ScrollBar.PageUpCommand), nameof(ScrollBar.PageDownCommand),
        nameof(ScrollBar.PageLeftCommand), nameof(ScrollBar.PageRightCommand),
        nameof(ScrollBar.ScrollToEndCommand), nameof(ScrollBar.ScrollToHomeCommand),
        nameof(ScrollBar.ScrollToRightEndCommand), nameof(ScrollBar.ScrollToLeftEndCommand),
        nameof(ScrollBar.ScrollToTopCommand), nameof(ScrollBar.ScrollToBottomCommand),
        nameof(ScrollBar.ScrollToHorizontalOffsetCommand), nameof(ScrollBar.ScrollToVerticalOffsetCommand),
        nameof(ScrollBar.DeferScrollToHorizontalOffsetCommand), nameof(ScrollBar.DeferScrollToVerticalOffsetCommand),
        nameof(ScrollBar.ScrollHereCommand),
    ];

    [Fact]
    public void Surface_DeclaresAllWpfCommandsAndExposesItsRealTrack()
    {
        foreach (string name in s_commandFields)
        {
            FieldInfo? field = typeof(ScrollBar).GetField(
                name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.NotNull(field);
            Assert.True(field!.IsInitOnly);
            Assert.IsType<RoutedCommand>(field.GetValue(null));
        }

        var bar = new ScrollBar();
        Assert.NotNull(bar.Track);
        Assert.IsType<Thumb>(bar.Track.Thumb);
    }

    [Fact]
    public void Commands_ChangeValueClampAndRaiseMatchingScrollEvents()
    {
        var bar = new ScrollBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            SmallChange = 2,
            LargeChange = 15,
        };
        var events = new List<ScrollEventType>();
        bar.Scroll += (_, e) => events.Add(e.ScrollEventType);

        ScrollBar.LineUpCommand.Execute(null, bar);
        Assert.Equal(48, bar.Value);
        ScrollBar.PageDownCommand.Execute(null, bar);
        Assert.Equal(63, bar.Value);
        ScrollBar.ScrollToVerticalOffsetCommand.Execute(91.0, bar);
        Assert.Equal(91, bar.Value);
        ScrollBar.ScrollToEndCommand.Execute(null, bar);
        Assert.Equal(100, bar.Value);

        Assert.Equal(
            [ScrollEventType.SmallDecrement, ScrollEventType.LargeIncrement, ScrollEventType.ThumbPosition, ScrollEventType.Last],
            events);
    }
}
