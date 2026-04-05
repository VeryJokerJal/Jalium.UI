using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation;

#region MapView

/// <summary>
/// Exposes <see cref="MapView"/> to UI Automation.
/// </summary>
public sealed class MapViewAutomationPeer : FrameworkElementAutomationPeer
{
    public MapViewAutomationPeer(MapView owner) : base(owner) { }

    private MapView MapViewOwner => (MapView)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(MapView);

    protected override string GetNameCore()
    {
        var center = MapViewOwner.Center;
        var zoom = MapViewOwner.ZoomLevel;
        return $"Map at ({center.Latitude:F2}, {center.Longitude:F2}), zoom {zoom:F1}";
    }

    protected override string GetLocalizedControlTypeCore() => "map";

    protected override string GetHelpTextCore()
    {
        var center = MapViewOwner.Center;
        return $"Center: {center.Latitude:F4}, {center.Longitude:F4} | Zoom: {MapViewOwner.ZoomLevel:F1}";
    }
}

#endregion

#region MiniMap

/// <summary>
/// Exposes <see cref="MiniMap"/> to UI Automation.
/// </summary>
public sealed class MiniMapAutomationPeer : FrameworkElementAutomationPeer
{
    public MiniMapAutomationPeer(MiniMap owner) : base(owner) { }

    private MiniMap MiniMapOwner => (MiniMap)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(MiniMap);

    protected override string GetNameCore()
    {
        if (MiniMapOwner.MapViewTarget != null)
            return "Mini map overview";
        if (MiniMapOwner.Target != null)
            return "Mini map for " + MiniMapOwner.Target.GetType().Name;
        return "Mini map";
    }

    protected override string GetLocalizedControlTypeCore() => "mini map";
}

#endregion

#region GeographicHeatmap

/// <summary>
/// Exposes <see cref="GeographicHeatmap"/> to UI Automation.
/// </summary>
public sealed class GeographicHeatmapAutomationPeer : FrameworkElementAutomationPeer
{
    public GeographicHeatmapAutomationPeer(GeographicHeatmap owner) : base(owner) { }

    private GeographicHeatmap HeatmapOwner => (GeographicHeatmap)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Image;

    protected override string GetClassNameCore() => nameof(GeographicHeatmap);

    protected override string GetNameCore()
    {
        var points = HeatmapOwner.Points;
        int count = points?.Count ?? 0;
        return $"Geographic heatmap with {count} data point{(count == 1 ? "" : "s")}";
    }

    protected override string GetLocalizedControlTypeCore() => "heatmap";

    protected override string GetHelpTextCore()
    {
        var points = HeatmapOwner.Points;
        return $"Points: {points?.Count ?? 0}, Radius: {HeatmapOwner.Radius:F0}, Intensity: {HeatmapOwner.Intensity:F1}";
    }
}

#endregion
