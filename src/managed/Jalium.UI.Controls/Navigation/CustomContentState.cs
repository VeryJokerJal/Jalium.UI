namespace Jalium.UI.Controls.Navigation;

/// <summary>
/// Enables the ability to store custom state information that is associated with
/// journal entries for navigation history.
/// </summary>
public abstract class CustomContentState
{
    /// <summary>
    /// Gets the name of the custom content state to display in navigation history.
    /// </summary>
    public abstract string JournalEntryName { get; }

    /// <summary>
    /// Called to reapply state to a piece of content when navigation occurs.
    /// </summary>
    /// <param name="navigationService">The navigation service for the current navigation.</param>
    /// <param name="mode">The mode of navigation (Back, Forward, or New).</param>
    public abstract void Replay(object content, NavigationMode mode);
}

/// <summary>
/// Interface that allows a page to provide custom state for journal entries.
/// </summary>
public interface IProvideCustomContentState
{
    /// <summary>
    /// Gets the current custom content state of the page.
    /// </summary>
    /// <returns>A CustomContentState object representing the current state.</returns>
    CustomContentState? GetContentState();
}

/// <summary>
/// Represents a simple custom content state that stores key-value pairs.
/// </summary>
public sealed class DictionaryContentState : CustomContentState
{
    private readonly Dictionary<string, object?> _values = new();
    private readonly string _journalEntryName;

    /// <summary>
    /// Initializes a new instance of the <see cref="DictionaryContentState"/> class.
    /// </summary>
    /// <param name="journalEntryName">The name for this state in the journal.</param>
    public DictionaryContentState(string journalEntryName)
    {
        _journalEntryName = journalEntryName;
    }

    /// <summary>
    /// Gets the name for this state in the journal.
    /// </summary>
    public override string JournalEntryName => _journalEntryName;

    /// <summary>
    /// Gets or sets a value in the state dictionary.
    /// </summary>
    /// <param name="key">The key for the value.</param>
    /// <returns>The value associated with the key.</returns>
    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    /// <summary>
    /// Adds a value to the state dictionary.
    /// </summary>
    /// <param name="key">The key for the value.</param>
    /// <param name="value">The value to store.</param>
    public void Add(string key, object? value)
    {
        _values[key] = value;
    }

    /// <summary>
    /// Gets a value from the state dictionary.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The key for the value.</param>
    /// <returns>The value associated with the key, or default if not found.</returns>
    public T? Get<T>(string key)
    {
        if (_values.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// Gets all keys in the state dictionary.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <inheritdoc/>
    public override void Replay(object content, NavigationMode mode)
    {
        // If the content implements IProvideCustomContentState, attempt to restore state
        if (content is IDictionaryContentStateRestorer restorer)
        {
            restorer.RestoreState(this);
        }
    }
}

/// <summary>
/// Interface for content that can restore state from a DictionaryContentState.
/// </summary>
public interface IDictionaryContentStateRestorer
{
    /// <summary>
    /// Restores the content's state from the dictionary.
    /// </summary>
    /// <param name="state">The state to restore from.</param>
    void RestoreState(DictionaryContentState state);
}

/// <summary>
/// Represents custom content state for a scroll position.
/// </summary>
public sealed class ScrollContentState : CustomContentState
{
    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset { get; }

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset { get; }

    /// <summary>
    /// Gets the name for this state in the journal.
    /// </summary>
    public override string JournalEntryName => $"Scroll: {VerticalOffset:F0}";

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollContentState"/> class.
    /// </summary>
    /// <param name="horizontalOffset">The horizontal scroll offset.</param>
    /// <param name="verticalOffset">The vertical scroll offset.</param>
    public ScrollContentState(double horizontalOffset, double verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
    }

    /// <inheritdoc/>
    public override void Replay(object content, NavigationMode mode)
    {
        // Find a ScrollViewer in the content and restore scroll position
        if (content is FrameworkElement element)
        {
            var scrollViewer = FindScrollViewer(element);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToHorizontalOffset(HorizontalOffset);
                scrollViewer.ScrollToVerticalOffset(VerticalOffset);
            }
        }
    }

    private ScrollViewer? FindScrollViewer(FrameworkElement element)
    {
        if (element is ScrollViewer sv)
        {
            return sv;
        }

        // Search in visual tree
        if (element is ContentControl cc && cc.Content is FrameworkElement child)
        {
            return FindScrollViewer(child);
        }

        if (element is Panel panel)
        {
            foreach (var panelChild in panel.Children)
            {
                if (panelChild is FrameworkElement fe)
                {
                    var result = FindScrollViewer(fe);
                    if (result != null) return result;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Represents custom content state for a data context.
/// </summary>
public sealed class DataContextContentState : CustomContentState
{
    /// <summary>
    /// Gets the data context object.
    /// </summary>
    public object? DataContext { get; }

    private readonly string _journalEntryName;

    /// <summary>
    /// Gets the name for this state in the journal.
    /// </summary>
    public override string JournalEntryName => _journalEntryName;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataContextContentState"/> class.
    /// </summary>
    /// <param name="dataContext">The data context to preserve.</param>
    /// <param name="journalEntryName">The name for this state in the journal.</param>
    public DataContextContentState(object? dataContext, string journalEntryName)
    {
        DataContext = dataContext;
        _journalEntryName = journalEntryName;
    }

    /// <inheritdoc/>
    public override void Replay(object content, NavigationMode mode)
    {
        if (content is FrameworkElement element)
        {
            element.DataContext = DataContext;
        }
    }
}

/// <summary>
/// Composite custom content state that combines multiple states.
/// </summary>
public sealed class CompositeContentState : CustomContentState
{
    private readonly List<CustomContentState> _states = new();
    private readonly string _journalEntryName;

    /// <summary>
    /// Gets the name for this state in the journal.
    /// </summary>
    public override string JournalEntryName => _journalEntryName;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeContentState"/> class.
    /// </summary>
    /// <param name="journalEntryName">The name for this state in the journal.</param>
    public CompositeContentState(string journalEntryName)
    {
        _journalEntryName = journalEntryName;
    }

    /// <summary>
    /// Adds a state to the composite.
    /// </summary>
    /// <param name="state">The state to add.</param>
    public void Add(CustomContentState state)
    {
        _states.Add(state);
    }

    /// <summary>
    /// Gets all states in the composite.
    /// </summary>
    public IReadOnlyList<CustomContentState> States => _states;

    /// <inheritdoc/>
    public override void Replay(object content, NavigationMode mode)
    {
        foreach (var state in _states)
        {
            state.Replay(content, mode);
        }
    }
}
