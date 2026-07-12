namespace Jalium.UI.Input;

/// <summary>Represents one position source consumed by the manipulation processor.</summary>
public interface IManipulator
{
    /// <summary>Gets the identifier that is unique for this manipulator kind.</summary>
    int Id { get; }

    /// <summary>Gets the manipulator position in the requested coordinate space.</summary>
    Point GetPosition(IInputElement? relativeTo);

    /// <summary>Occurs whenever the manipulator position or contact state changes.</summary>
    event EventHandler Updated;

    /// <summary>Notifies the manipulator that its manipulation has ended.</summary>
    void ManipulationEnded(bool cancel);
}
