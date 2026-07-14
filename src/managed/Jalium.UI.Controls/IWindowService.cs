namespace Jalium.UI;

/// <summary>Internal contract used by navigation content to configure its host window.</summary>
internal interface IWindowService
{
    double Height { get; set; }
    string Title { get; set; }
    bool UserResized { get; }
    double Width { get; set; }
}
