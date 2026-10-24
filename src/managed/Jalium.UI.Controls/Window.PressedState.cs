namespace Jalium.UI;

public partial class Window
{
    internal bool OwnsPressedState(UIElement element) => _inputDispatcher.OwnsPressedState(element);
}
