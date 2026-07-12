using System.Collections;
using System.Globalization;
using Jalium.UI.Data;

namespace Jalium.UI.Navigation;

/// <summary>Base class for a page that returns a value to its caller.</summary>
public abstract class PageFunctionBase : Controls.Page
{
    /// <summary>Gets or sets whether the page function is removed from the journal when it returns.</summary>
    public bool RemoveFromJournal { get; set; } = true;

    internal Guid PageFunctionId { get; } = Guid.NewGuid();

    internal Guid ParentPageFunctionId { get; set; }

    internal event EventHandler? Finished;

    internal virtual void Start()
    {
    }

    internal void NotifyFinished() => Finished?.Invoke(this, EventArgs.Empty);
}

/// <summary>Represents a page function that returns a value of type <typeparamref name="T"/>.</summary>
public class PageFunction<T> : PageFunctionBase
{
    /// <summary>Occurs when the page function returns a value.</summary>
    public event ReturnEventHandler<T>? Return;

    /// <summary>Raises the typed return event.</summary>
    protected virtual void OnReturn(ReturnEventArgs<T> e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Return?.Invoke(this, e);
        NotifyFinished();
    }
}

/// <summary>Provides the result returned by a page function.</summary>
public class ReturnEventArgs<T> : EventArgs
{
    /// <summary>Initializes empty return event data.</summary>
    public ReturnEventArgs()
    {
    }

    /// <summary>Initializes return event data with a result.</summary>
    public ReturnEventArgs(T result)
    {
        Result = result;
    }

    /// <summary>Gets or sets the returned result.</summary>
    public T? Result { get; set; }
}

/// <summary>Handles a typed page-function return.</summary>
public delegate void ReturnEventHandler<T>(object sender, ReturnEventArgs<T> e);

/// <summary>Converts journal collections for the built-in navigation UI.</summary>
public sealed class JournalEntryListConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        return value is IEnumerable entries && value is not string
            ? entries.Cast<object?>().ToList()
            : value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Journal entry list conversion is one-way.");
}

/// <summary>Identifies an entry's position in a combined journal view.</summary>
public enum JournalEntryPosition
{
    /// <summary>The entry belongs to the back journal.</summary>
    Back = 0,

    /// <summary>The entry is the current journal position.</summary>
    Current = 1,

    /// <summary>The entry belongs to the forward journal.</summary>
    Forward = 2,
}

/// <summary>Combines back and forward journal collections for navigation chrome.</summary>
public sealed class JournalEntryUnifiedViewConverter : IMultiValueConverter
{
    /// <summary>Identifies the attached journal-position property.</summary>
    public static readonly DependencyProperty JournalEntryPositionProperty =
        DependencyProperty.RegisterAttached(
            "JournalEntryPosition",
            typeof(JournalEntryPosition),
            typeof(JournalEntryUnifiedViewConverter),
            new PropertyMetadata(JournalEntryPosition.Current));

    /// <summary>Gets an entry's position in the combined journal.</summary>
    public static JournalEntryPosition GetJournalEntryPosition(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (JournalEntryPosition)(element.GetValue(JournalEntryPositionProperty) ?? JournalEntryPosition.Current);
    }

    /// <summary>Sets an entry's position in the combined journal.</summary>
    public static void SetJournalEntryPosition(DependencyObject element, JournalEntryPosition position)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(JournalEntryPositionProperty, position);
    }

    /// <inheritdoc />
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(targetType);

        var result = new List<JournalEntry>();
        IEnumerable<JournalEntry> back = values.Length > 0
            ? AsEntries(values[0])
            : [];
        IEnumerable<JournalEntry> forward = values.Length > 1
            ? AsEntries(values[1])
            : [];

        foreach (JournalEntry entry in forward.Reverse())
        {
            SetJournalEntryPosition(entry, JournalEntryPosition.Forward);
            result.Add(entry);
        }

        foreach (JournalEntry entry in back)
        {
            SetJournalEntryPosition(entry, JournalEntryPosition.Back);
            result.Add(entry);
        }

        return result;
    }

    /// <inheritdoc />
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Unified journal conversion is one-way.");

    private static IEnumerable<JournalEntry> AsEntries(object? value) =>
        value is IEnumerable enumerable
            ? enumerable.Cast<object?>().OfType<JournalEntry>()
            : [];
}
