namespace Jalium.UI.Controls.DevTools;

/// <summary>A replaceable, vertically arranged list of inspector detail controls.</summary>
public class DevToolsDetailsList : ItemsControl
{
    static DevToolsDetailsList() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsDetailsList), new PropertyMetadata(typeof(DevToolsDetailsList)));
}
