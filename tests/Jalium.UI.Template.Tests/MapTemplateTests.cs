using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MapTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultMapTemplateOwnsItsFrameAndProvidesStandardZoomControls()
    {
        var map=CreateMap();Layout(map);Assert.Same(map,Part<MapViewSurface>(map,"PART_MapSurface").Owner);
        Assert.Same(map.Background,Part<Border>(map,"PART_Border").Background);Assert.NotNull(map.MapContentClip);
        Assert.Same(map.ZoomInCommand,Part<Button>(map,"PART_ZoomInButton").Command);Assert.Same(map.ZoomOutCommand,Part<Button>(map,"PART_ZoomOutButton").Command);
        Assert.Equal(map.ScaleBarLabel,Part<MapScaleBar>(map,"PART_ScaleBar").Label);Assert.InRange(map.ScaleBarWidth,1,120);Assert.Equal("本地地图",map.AttributionText);
        map.IsEnabled=false;Assert.Equal(.65,Part<Grid>(map,"PART_Root").Opacity);Assert.False(map.CanZoomIn);map.IsEnabled=true;Assert.Equal(1,Part<Grid>(map,"PART_Root").Opacity);
    }
    [Fact]
    public void ZoomCommandsRespectBoundsAndFlagsWithoutReplacingTheMapModels()
    {
        var map=CreateMap();map.MinZoomLevel=2;map.MaxZoomLevel=4;map.ZoomLevel=3;Layout(map);var markers=map.Markers;
        map.ZoomInCommand.Execute(null);Assert.Equal(4,map.ZoomLevel);Assert.False(map.ZoomInCommand.CanExecute(null));Assert.False(Part<Button>(map,"PART_ZoomInButton").IsEnabled);
        map.ZoomOutCommand.Execute(null);Assert.Equal(3,map.ZoomLevel);map.IsZoomEnabled=false;Assert.False(map.ZoomOutCommand.CanExecute(null));map.ZoomOutCommand.Execute(null);Assert.Equal(3,map.ZoomLevel);
        map.ShowZoomControls=false;map.ShowScaleBar=false;map.ShowAttribution=false;Assert.Equal(Visibility.Collapsed,Part<StackPanel>(map,"PART_ZoomControls").Visibility);Assert.Equal(Visibility.Collapsed,Part<MapScaleBar>(map,"PART_ScaleBar").Visibility);Assert.Equal(Visibility.Collapsed,Part<Border>(map,"PART_Attribution").Visibility);Assert.Same(markers,map.Markers);
    }
    [Fact]
    public void RetiredMapPartsReleaseOwnersAndCommandsWhileStateSurvives()
    {
        var map=CreateMap();Layout(map);var surface=Part<MapViewSurface>(map,"PART_MapSurface");var button=Part<Button>(map,"PART_ZoomInButton");var center=map.Center;var zoom=map.ZoomLevel;var markers=map.Markers;
        var blank=new ControlTemplate(typeof(MapView));blank.SetVisualTree(()=>new Border());map.Template=blank;Layout(map);Assert.Null(surface.Owner);Assert.Null(button.Command);Assert.Equal(center,map.Center);Assert.Equal(zoom,map.ZoomLevel);Assert.Same(markers,map.Markers);
        map.ClearValue(Control.TemplateProperty);Layout(map);Assert.Same(map,Part<MapViewSurface>(map,"PART_MapSurface").Owner);Assert.NotSame(surface,map.FindName("PART_MapSurface"));
    }
    [Fact]
    public void MiniMapIsAControlAndItsTemplateOwnsTheFrameAndViewport()
    {
        var target=CreateMap();Layout(target);var mini=new MiniMap{MapViewTarget=target,AutoScale=false};mini.Resources.MergedDictionaries.Add(Defaults.Value);Layout(mini);
        Assert.IsAssignableFrom<Control>(mini);var surface=Part<MiniMapSurface>(mini,"PART_OverviewSurface");Assert.Same(mini,surface.Owner);Assert.NotNull(mini.OverviewContentClip);
        var viewport=Part<Border>(mini,"PART_Viewport");Assert.Equal(mini.ViewportBounds.Width,viewport.Width,3);Assert.Same(mini.EffectiveViewportBrush,viewport.Background);Assert.Equal(Visibility.Visible,viewport.Visibility);
        mini.IsEnabled=false;Assert.Equal(.65,Part<Grid>(mini,"PART_Root").Opacity);
        var blank=new ControlTemplate(typeof(MiniMap));blank.SetVisualTree(()=>new Border());mini.Template=blank;Layout(mini);Assert.Null(surface.Owner);Assert.Same(target,mini.MapViewTarget);
    }
    [Fact]
    public void OwnerlessSurfacesAndMissingOptionalPartsAreSafe()
    {
        Layout(new MapViewSurface());Layout(new MiniMapSurface());var map=CreateMap();var template=new ControlTemplate(typeof(MapView));template.SetVisualTree(()=>new MapViewSurface{Name="PART_MapSurface"});map.Template=template;Layout(map);Assert.Same(map,Part<MapViewSurface>(map,"PART_MapSurface").Owner);map.ZoomInCommand.Execute(null);
    }
    private static MapView CreateMap(){var map=new MapView{TileSource=new MapTileSource{Attribution="本地地图"},Center=new GeoPoint(31,121),ZoomLevel=6};map.Resources.MergedDictionaries.Add(Defaults.Value);return map;}
    private static T Part<T>(Control owner,string name)where T:class=>Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement element){for(var i=0;i<3;i++){element.InvalidateMeasure();element.Measure(new Size(360,240));element.Arrange(new Rect(0,0,360,240));element.UpdateLayout();}}
}
