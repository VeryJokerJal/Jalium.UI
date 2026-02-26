using Jalium.UI.Input;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// A key chord mapped to a command identifier.
/// </summary>
public readonly record struct EditorKeyBinding(Key Key, ModifierKeys Modifiers, string CommandId)
{
    public bool Matches(KeyEventArgs args)
    {
        return args.Key == Key && args.KeyboardModifiers == Modifiers;
    }
}
