namespace Jalium.UI.Controls.Navigation;

/// <summary>
/// Represents an entry in either back or forward navigation history.
/// </summary>
public sealed class JournalEntry : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Name dependency property.
    /// </summary>
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register(nameof(Name), typeof(string), typeof(JournalEntry),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(JournalEntry),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the KeepAlive attached property.
    /// </summary>
    public static readonly DependencyProperty KeepAliveProperty =
        DependencyProperty.RegisterAttached("KeepAlive", typeof(bool), typeof(JournalEntry),
            new PropertyMetadata(false));

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalEntry"/> class.
    /// </summary>
    public JournalEntry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalEntry"/> class with the specified source.
    /// </summary>
    /// <param name="source">The URI of the entry.</param>
    public JournalEntry(Uri? source)
    {
        Source = source;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalEntry"/> class with the specified source and name.
    /// </summary>
    /// <param name="source">The URI of the entry.</param>
    /// <param name="name">The name of the entry.</param>
    public JournalEntry(Uri? source, string? name)
    {
        Source = source;
        Name = name;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the name of the journal entry.
    /// </summary>
    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>
    /// Gets or sets the URI of the content that was navigated to.
    /// </summary>
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the custom content state associated with this entry.
    /// </summary>
    public CustomContentState? CustomContentState { get; set; }

    #endregion

    #region Attached Property Methods

    /// <summary>
    /// Gets the value of the KeepAlive attached property for a specified dependency object.
    /// </summary>
    /// <param name="dependencyObject">The dependency object to query.</param>
    /// <returns>The value of the KeepAlive attached property.</returns>
    public static bool GetKeepAlive(DependencyObject dependencyObject)
    {
        if (dependencyObject == null)
        {
            throw new ArgumentNullException(nameof(dependencyObject));
        }
        return (bool)(dependencyObject.GetValue(KeepAliveProperty) ?? false);
    }

    /// <summary>
    /// Sets the value of the KeepAlive attached property for a specified dependency object.
    /// </summary>
    /// <param name="dependencyObject">The dependency object to set the property on.</param>
    /// <param name="keepAlive">The value to set.</param>
    public static void SetKeepAlive(DependencyObject dependencyObject, bool keepAlive)
    {
        if (dependencyObject == null)
        {
            throw new ArgumentNullException(nameof(dependencyObject));
        }
        dependencyObject.SetValue(KeepAliveProperty, keepAlive);
    }

    #endregion
}

/// <summary>
/// Represents a page function that can return a value when navigation completes.
/// </summary>
/// <typeparam name="T">The type of value returned by the page function.</typeparam>
public abstract class PageFunction<T> : Page
{
    /// <summary>
    /// Occurs when the page function returns.
    /// </summary>
    public event EventHandler<ReturnEventArgs<T>>? Return;

    /// <summary>
    /// Raises the Return event with the specified value.
    /// </summary>
    /// <param name="result">The value to return.</param>
    protected void OnReturn(ReturnEventArgs<T> e)
    {
        Return?.Invoke(this, e);
    }
}

/// <summary>
/// Provides data for the Return event of a PageFunction.
/// </summary>
/// <typeparam name="T">The type of the return value.</typeparam>
public sealed class ReturnEventArgs<T> : EventArgs
{
    /// <summary>
    /// Gets the return value.
    /// </summary>
    public T? Result { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReturnEventArgs{T}"/> class.
    /// </summary>
    public ReturnEventArgs()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReturnEventArgs{T}"/> class with the specified result.
    /// </summary>
    /// <param name="result">The return value.</param>
    public ReturnEventArgs(T? result)
    {
        Result = result;
    }
}

/// <summary>
/// JournalEntryUnifiedViewConverter is used to unify the view for both
/// back and forward navigation history entries.
/// </summary>
public sealed class JournalEntryUnifiedViewConverter
{
    /// <summary>
    /// Gets the unified view for the specified navigation history entries.
    /// </summary>
    /// <param name="backEntries">The back history entries.</param>
    /// <param name="forwardEntries">The forward history entries.</param>
    /// <returns>A unified collection of journal entries with position information.</returns>
    public static IEnumerable<JournalEntryPosition> GetUnifiedView(
        IEnumerable<JournalEntry> backEntries,
        IEnumerable<JournalEntry> forwardEntries)
    {
        // Add forward entries in reverse order (most recent first)
        var forwardList = forwardEntries.ToList();
        var result = new List<JournalEntryPosition>(forwardList.Count);
        for (int i = forwardList.Count - 1; i >= 0; i--)
        {
            result.Add(new JournalEntryPosition(forwardList[i], i + 1, JournalEntryPositionType.Forward));
        }

        // Add current position marker
        result.Add(new JournalEntryPosition(null, 0, JournalEntryPositionType.Current));

        // Add back entries (most recent first)
        int backIndex = -1;
        foreach (var entry in backEntries)
        {
            result.Add(new JournalEntryPosition(entry, backIndex, JournalEntryPositionType.Back));
            backIndex--;
        }

        return result;
    }
}

/// <summary>
/// Represents a journal entry with its position in the navigation history.
/// </summary>
public sealed class JournalEntryPosition
{
    /// <summary>
    /// Gets the journal entry.
    /// </summary>
    public JournalEntry? Entry { get; }

    /// <summary>
    /// Gets the relative position (negative for back, positive for forward, zero for current).
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Gets the type of position.
    /// </summary>
    public JournalEntryPositionType PositionType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalEntryPosition"/> class.
    /// </summary>
    public JournalEntryPosition(JournalEntry? entry, int position, JournalEntryPositionType positionType)
    {
        Entry = entry;
        Position = position;
        PositionType = positionType;
    }
}

/// <summary>
/// Specifies the type of position in the navigation history.
/// </summary>
public enum JournalEntryPositionType
{
    /// <summary>
    /// The entry is in the back history.
    /// </summary>
    Back,

    /// <summary>
    /// The entry represents the current position.
    /// </summary>
    Current,

    /// <summary>
    /// The entry is in the forward history.
    /// </summary>
    Forward
}
