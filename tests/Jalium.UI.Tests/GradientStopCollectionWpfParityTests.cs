using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class GradientStopCollectionWpfParityTests
{
    [Fact]
    public void GradientStopIsAnUnclampedCloneableAnimatable()
    {
        var stop = new GradientStop(Colors.Red, -2);
        Assert.IsAssignableFrom<Animatable>(stop);
        Assert.Equal(-2, stop.Offset);
        Assert.Equal("#FFFF0000,-2", stop.ToString(CultureInfo.InvariantCulture));

        GradientStop clone = stop.CloneCurrentValue();
        Assert.NotSame(stop, clone);
        Assert.Equal(stop.Color, clone.Color);
        Assert.Equal(stop.Offset, clone.Offset);
    }

    [Fact]
    public void GradientStopCollectionParsesClonesFreezesAndRaisesChanges()
    {
        GradientStopCollection collection = GradientStopCollection.Parse("Red,0 #FF0000FF,1");
        Assert.IsAssignableFrom<Animatable>(collection);
        Assert.Equal(2, collection.Count);
        Assert.Equal("#FFFF0000,0 #FF0000FF,1", collection.ToString(CultureInfo.InvariantCulture));

        int changes = 0;
        collection.Changed += (_, _) => changes++;
        collection[0].Offset = 0.25;
        collection.Add(new GradientStop(Colors.White, 2));
        Assert.True(changes >= 2);

        GradientStopCollection clone = collection.Clone();
        Assert.Equal(collection.Count, clone.Count);
        Assert.NotSame(collection[0], clone[0]);

        collection.Freeze();
        Assert.True(collection.IsFrozen);
        Assert.All(collection, static stop => Assert.True(stop.IsFrozen));
        Assert.Throws<InvalidOperationException>(() => collection.Add(new GradientStop()));
    }
}
