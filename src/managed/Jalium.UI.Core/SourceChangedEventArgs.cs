namespace Jalium.UI;

/// <summary>Provides data when an element moves between presentation sources.</summary>
public sealed class SourceChangedEventArgs : RoutedEventArgs
{
    /// <summary>Initializes source-change data.</summary>
    public SourceChangedEventArgs(PresentationSource? oldSource, PresentationSource? newSource)
        : this(oldSource, newSource, element: null, oldParent: null)
    {
    }

    /// <summary>Initializes source-change data with the affected element and its old parent.</summary>
    public SourceChangedEventArgs(
        PresentationSource? oldSource,
        PresentationSource? newSource,
        IInputElement? element,
        IInputElement? oldParent)
    {
        OldSource = oldSource;
        NewSource = newSource;
        Element = element;
        OldParent = oldParent;
    }

    /// <summary>Gets the previous presentation source.</summary>
    public PresentationSource? OldSource { get; }

    /// <summary>Gets the new presentation source.</summary>
    public PresentationSource? NewSource { get; }

    /// <summary>Gets the input element whose source changed.</summary>
    public IInputElement? Element { get; }

    /// <summary>Gets the element's previous input parent.</summary>
    public IInputElement? OldParent { get; }

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ((SourceChangedEventHandler)genericHandler)(genericTarget, this);
    }
}

/// <summary>Handles presentation-source changes.</summary>
public delegate void SourceChangedEventHandler(object sender, SourceChangedEventArgs e);
