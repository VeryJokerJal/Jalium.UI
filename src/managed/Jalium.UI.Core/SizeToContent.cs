namespace Jalium.UI;

/// <summary>Specifies whether a window sizes itself to fit its content.</summary>
public enum SizeToContent
{
    /// <summary>The window uses its explicitly assigned size.</summary>
    Manual = 0,

    /// <summary>The window automatically sizes its width.</summary>
    Width = 1,

    /// <summary>The window automatically sizes its height.</summary>
    Height = 2,

    /// <summary>The window automatically sizes both dimensions.</summary>
    WidthAndHeight = 3,
}
