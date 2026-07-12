namespace Jalium.UI.Media;

/// <summary>Specifies synthetic styles applied to a physical font face.</summary>
[Flags]
public enum StyleSimulations
{
    None = 0,
    BoldSimulation = 1,
    ItalicSimulation = 2,
    BoldItalicSimulation = BoldSimulation | ItalicSimulation,
}
