using System.Collections.ObjectModel;

namespace Jalium.UI.Tests;

public sealed class PropertyPathSurfaceWpfParityTests
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

    private sealed class Model
    {
        public string First { get; } = "one";
        public string Second { get; } = "two";
    }
}
