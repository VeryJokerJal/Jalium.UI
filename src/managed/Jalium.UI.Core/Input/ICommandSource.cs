using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>Describes an object that invokes a command with an optional parameter and target.</summary>
public interface ICommandSource
{
    ICommand? Command { get; }
    object? CommandParameter { get; }
    IInputElement? CommandTarget { get; }
}
