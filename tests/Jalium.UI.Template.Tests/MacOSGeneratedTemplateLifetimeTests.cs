using System.Runtime.CompilerServices;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSGeneratedTemplateLifetimeTests
{
    [Theory] [InlineData("data")] [InlineData("control")] [InlineData("panel")]
    public void GeneratedTemplateDoesNotRetainItsLastNamedVisualOrData(string kind)
    {
        var owner = new GeneratedTemplateLifetimeView();
        var template = Assert.IsAssignableFrom<FrameworkTemplate>(owner.Resources[kind]);
        var weak = CreateAndRelease(template);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.Visual.IsAlive); Assert.False(weak.Data.IsAlive);
        GC.KeepAlive(template); GC.KeepAlive(owner);
    }

    [Fact]
    public void GeneratedInstancesKeepTheirNamesAndAmbientResources()
    {
        var owner = new GeneratedTemplateLifetimeView();
        foreach (var kind in new[] { "data", "control", "panel" })
        {
            var template = (FrameworkTemplate)owner.Resources[kind]!;
            var first = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
            var second = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
            Assert.NotSame(first,second); Assert.Equal(kind switch { "data" => "DataRoot", "control" => "ControlRoot", _ => "PanelRoot" },first.Name); Assert.Equal(first.Name,second.Name);
            var brush = first is Border border ? border.Background : ((Panel)first).Background;
            Assert.Same(owner.Resources["LifetimeBrush"],brush);
            Assert.Null(owner.FindName(first.Name));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Visual, WeakReference Data) CreateAndRelease(FrameworkTemplate template)
    {
        var visual = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
        var data = new object(); visual.DataContext = data;
        return (new WeakReference(visual),new WeakReference(data));
    }
}

public partial class GeneratedTemplateLifetimeView : UserControl
{
    public GeneratedTemplateLifetimeView() => InitializeComponent();
}
