namespace Jalium.UI.Input;

internal interface IInputTreeLifetimeHost
{
    void OnInputSubtreeDetached(UIElement subtree);
}
