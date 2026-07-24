using System.Collections.ObjectModel;

namespace Jalium.UI.Tests;

public sealed class PropertyPathSurfaceParityTests
{
    [Fact]
    public void PathIsMutableAndParametersUseCollectionSurface()
    {
        var pathProperty = typeof(PropertyPath).GetProperty(nameof(PropertyPath.Path))!;
        Assert.NotNull(pathProperty.SetMethod);
        Assert.True(pathProperty.SetMethod!.IsPublic);
        Assert.Equal(
            typeof(Collection<object>),
            typeof(PropertyPath).GetProperty(nameof(PropertyPath.PathParameters))!.PropertyType);

        var path = new PropertyPath("First", "parameter");
        path.Path = "Second";
        path.PathParameters.Add(42);

        Assert.Equal("Second", path.Path);
        Assert.Equal(new object[] { "parameter", 42 }, path.PathParameters);
    }

    [Fact]
    public void ChangedPathIsUsedByResolution()
    {
        var path = new PropertyPath(nameof(Model.First));
        path.Path = nameof(Model.Second);

        Assert.Equal("two", path.ResolveValue(new Model()));
    }

    [Fact]
    public void SegmentCachesAvoidRepeatedSplitsAndInvalidateWithPath()
    {
        var path = new PropertyPath("First.Second");
        var cachedProperty = typeof(PropertyPath).GetProperty(
            "CachedPathSegments",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var first = (string[])cachedProperty.GetValue(path)!;
        var second = (string[])cachedProperty.GetValue(path)!;
        Assert.Same(first, second);

        var publicCopy = path.PathSegments;
        publicCopy[0] = "mutated";
        Assert.Equal("First", path.PathSegments[0]);

        path.Path = "Third.Fourth";
        var changed = (string[])cachedProperty.GetValue(path)!;
        Assert.NotSame(first, changed);
        Assert.Equal(new[] { "Third", "Fourth" }, changed);
    }

    private sealed class Model
    {
        public string First { get; } = "one";
        public string Second { get; } = "two";
    }
}
