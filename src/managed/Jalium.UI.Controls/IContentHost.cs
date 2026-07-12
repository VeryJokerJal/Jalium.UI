using System.Collections.ObjectModel;

namespace Jalium.UI;

/// <summary>
/// Exposes nonvisual content hosted by a visual text/layout element.
/// </summary>
public interface IContentHost
{
    IEnumerator<IInputElement> HostedElements { get; }

    ReadOnlyCollection<Rect> GetRectangles(ContentElement child);

    IInputElement InputHitTest(Point point);

    void OnChildDesiredSizeChanged(UIElement child);
}
