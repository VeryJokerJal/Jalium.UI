namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the direction in which item generation will occur.
/// </summary>
public enum GeneratorDirection
{
    /// <summary>Generate items in the forward direction.</summary>
    Forward,
    /// <summary>Generate items in the backward direction.</summary>
    Backward
}

/// <summary>
/// Describes the status of the generator.
/// </summary>
public enum GeneratorStatus
{
    /// <summary>The generator has not tried to generate content.</summary>
    NotStarted,
    /// <summary>The generator is generating containers.</summary>
    GeneratingContainers,
    /// <summary>The generator has finished generating containers.</summary>
    ContainersGenerated,
    /// <summary>The generator has encountered an error.</summary>
    Error
}

/// <summary>
/// An interface that is implemented by classes which are responsible for generating
/// UI content on behalf of a host.
/// </summary>
public interface IItemContainerGenerator
{
    /// <summary>
    /// Returns the GeneratorPosition corresponding to the item at the given index.
    /// </summary>
    GeneratorPosition GeneratorPositionFromIndex(int itemIndex);

    /// <summary>
    /// Returns the item index corresponding to the given GeneratorPosition.
    /// </summary>
    int IndexFromGeneratorPosition(GeneratorPosition position);

    /// <summary>
    /// Prepares the generator to generate items, starting at the given position and direction.
    /// Returns an IDisposable that must be disposed when generation is finished.
    /// </summary>
    IDisposable StartAt(GeneratorPosition position, GeneratorDirection direction);

    /// <summary>
    /// Prepares the generator to generate items, starting at the given position and direction.
    /// </summary>
    IDisposable StartAt(GeneratorPosition position, GeneratorDirection direction, bool allowStartAtRealizedItem);

    /// <summary>
    /// Returns the container element for the next item. Also indicates whether the
    /// container element has been newly generated (realized).
    /// </summary>
    DependencyObject? GenerateNext(out bool isNewlyRealized);

    /// <summary>
    /// Prepares the specified element as the container for the corresponding item.
    /// </summary>
    void PrepareItemContainer(DependencyObject container);

    /// <summary>
    /// Removes one or more generated (realized) items.
    /// </summary>
    void Remove(GeneratorPosition position, int count);

    /// <summary>
    /// Removes all generated (realized) items.
    /// </summary>
    void RemoveAll();

    /// <summary>
    /// Occurs when the Items collection associated with this generator has changed.
    /// </summary>
    event ItemsChangedEventHandler ItemsChanged;
}

/// <summary>
/// Extends IItemContainerGenerator to support recycling of containers.
/// </summary>
public interface IRecyclingItemContainerGenerator : IItemContainerGenerator
{
    /// <summary>
    /// Disassociates item containers from their data items and saves the containers
    /// for later reuse rather than discarding them.
    /// </summary>
    void Recycle(GeneratorPosition position, int count);
}
