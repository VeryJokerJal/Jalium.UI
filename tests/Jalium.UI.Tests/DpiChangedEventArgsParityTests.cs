namespace Jalium.UI.Tests;

public sealed class DpiChangedEventArgsParityTests
{
    [Fact]
    public void EventArgsIsSealedRoutedEventArgsWithDpiValues()
    {
        var oldDpi = new DpiScale(1, 1.25);
        var newDpi = new DpiScale(1.5, 2);
        var args = new DpiChangedEventArgs(oldDpi, newDpi);

        Assert.True(typeof(DpiChangedEventArgs).IsSealed);
        Assert.Equal(typeof(RoutedEventArgs), typeof(DpiChangedEventArgs).BaseType);
        Assert.Equal(oldDpi, args.OldDpi);
        Assert.Equal(newDpi, args.NewDpi);
    }
}
