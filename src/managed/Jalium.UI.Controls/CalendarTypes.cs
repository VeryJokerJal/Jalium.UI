using System.Collections.ObjectModel;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a range of dates in a Calendar control.
/// </summary>
public sealed class CalendarDateRange
{
    /// <summary>
    /// Initializes a new instance with a single date.
    /// </summary>
    public CalendarDateRange(DateTime date)
    {
        Start = date;
        End = date;
    }

    /// <summary>
    /// Initializes a new instance with a start and end date.
    /// </summary>
    public CalendarDateRange(DateTime start, DateTime end)
    {
        if (DateTime.Compare(end, start) < 0)
        {
            Start = end;
            End = start;
        }
        else
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>Gets the first date in the range.</summary>
    public DateTime Start { get; }

    /// <summary>Gets the last date in the range.</summary>
    public DateTime End { get; }
}

/// <summary>
/// Represents a collection of non-selectable dates in a Calendar.
/// </summary>
public sealed class CalendarBlackoutDatesCollection : ObservableCollection<CalendarDateRange>
{
    private readonly Calendar _owner;

    internal CalendarBlackoutDatesCollection(Calendar owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Adds a range of dates to the collection.
    /// </summary>
    public void AddDatesInPast()
    {
        Add(new CalendarDateRange(DateTime.MinValue, DateTime.Today.AddDays(-1)));
    }

    /// <summary>
    /// Returns a value indicating whether the specified date is in this collection.
    /// </summary>
    public bool Contains(DateTime date)
    {
        foreach (var range in this)
        {
            if (date >= range.Start && date <= range.End)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether the specified date range overlaps with dates in this collection.
    /// </summary>
    public bool ContainsAny(CalendarDateRange range)
    {
        foreach (var blackout in this)
        {
            if (range.Start <= blackout.End && range.End >= blackout.Start)
                return true;
        }
        return false;
    }
}

/// <summary>
/// Provides data for the <see cref="Calendar.DisplayModeChanged"/> event.
/// </summary>
public sealed class CalendarModeChangedEventArgs : RoutedEventArgs
{
    public CalendarModeChangedEventArgs(CalendarMode oldMode, CalendarMode newMode)
    {
        OldMode = oldMode;
        NewMode = newMode;
    }

    /// <summary>Gets the previous display mode.</summary>
    public CalendarMode OldMode { get; }

    /// <summary>Gets the new display mode.</summary>
    public CalendarMode NewMode { get; }
}

/// <summary>
/// Provides data for the <see cref="DatePicker.DateValidationError"/> event.
/// </summary>
public sealed class DatePickerDateValidationErrorEventArgs : EventArgs
{
    public DatePickerDateValidationErrorEventArgs(Exception exception, string text)
    {
        Exception = exception;
        Text = text;
    }

    /// <summary>Gets the initial exception associated with the validation error.</summary>
    public Exception Exception { get; }

    /// <summary>Gets the text that caused the validation error.</summary>
    public string Text { get; }

    /// <summary>Gets or sets a value indicating whether the exception should be thrown.</summary>
    public bool ThrowException { get; set; }
}

/// <summary>
/// Represents the collection of selected dates in a Calendar.
/// </summary>
public sealed class SelectedDatesCollection : ObservableCollection<DateTime>
{
    private readonly Calendar _owner;

    internal SelectedDatesCollection(Calendar owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Adds a range of dates to the collection.
    /// </summary>
    public void AddRange(DateTime start, DateTime end)
    {
        DateTime effectiveStart, effectiveEnd;
        if (start <= end)
        {
            effectiveStart = start;
            effectiveEnd = end;
        }
        else
        {
            effectiveStart = end;
            effectiveEnd = start;
        }

        for (var date = effectiveStart; date <= effectiveEnd; date = date.AddDays(1))
        {
            if (!Contains(date))
                Add(date);
        }
    }
}
