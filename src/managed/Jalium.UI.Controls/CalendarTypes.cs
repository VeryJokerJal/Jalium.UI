using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a range of dates in a Calendar control.
/// </summary>
public sealed class CalendarDateRange : INotifyPropertyChanged
{
    private DateTime _start;
    private DateTime _end;

    public CalendarDateRange()
        : this(DateTime.MinValue, DateTime.MaxValue)
    {
    }

    /// <summary>
    /// Initializes a new instance with a single date.
    /// </summary>
    public CalendarDateRange(DateTime date)
        : this(date, date)
    {
    }

    /// <summary>
    /// Initializes a new instance with a start and end date.
    /// </summary>
    public CalendarDateRange(DateTime start, DateTime end)
    {
        _start = start;
        _end = end;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler<CalendarDateRangeChangingEventArgs>? Changing;

    /// <summary>Gets or sets the first date in the range.</summary>
    public DateTime Start
    {
        get => _start;
        set
        {
            if (_start == value)
            {
                return;
            }

            DateTime oldEffectiveEnd = End;
            DateTime newEffectiveEnd = CoerceEnd(value, _end);
            Changing?.Invoke(this, new CalendarDateRangeChangingEventArgs(value, newEffectiveEnd));
            _start = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Start)));
            if (newEffectiveEnd != oldEffectiveEnd)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(End)));
            }
        }
    }

    /// <summary>Gets or sets the last date in the range.</summary>
    public DateTime End
    {
        get => _end < _start ? _start : _end;
        set
        {
            DateTime oldEffectiveEnd = End;
            DateTime newEffectiveEnd = CoerceEnd(_start, value);
            if (newEffectiveEnd == oldEffectiveEnd)
            {
                return;
            }

            Changing?.Invoke(this, new CalendarDateRangeChangingEventArgs(_start, newEffectiveEnd));
            _end = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(End)));
        }
    }

    internal bool ContainsAny(CalendarDateRange range) =>
        range.End.Date >= Start.Date && End.Date >= range.Start.Date;

    private static DateTime CoerceEnd(DateTime start, DateTime end) => end < start ? start : end;
}

internal sealed class CalendarDateRangeChangingEventArgs : EventArgs
{
    public CalendarDateRangeChangingEventArgs(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }

    public DateTime Start { get; }
    public DateTime End { get; }
}

/// <summary>
/// Represents a collection of non-selectable dates in a Calendar.
/// </summary>
public sealed class CalendarBlackoutDatesCollection : ObservableCollection<CalendarDateRange>
{
    private readonly Calendar _owner;
    private readonly Thread _dispatcherThread;

    public CalendarBlackoutDatesCollection(Calendar owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _dispatcherThread = Thread.CurrentThread;
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
        DateTime day = date.Date;
        foreach (var range in this)
        {
            if (day >= range.Start.Date && day <= range.End.Date)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a value indicating whether the collection contains the exact date range.
    /// The time component is ignored, matching Calendar's day-based selection semantics.
    /// </summary>
    public bool Contains(DateTime start, DateTime end)
    {
        DateTime rangeStart = start.Date;
        DateTime rangeEnd = end.Date;
        if (rangeEnd < rangeStart)
        {
            (rangeStart, rangeEnd) = (rangeEnd, rangeStart);
        }

        foreach (var range in this)
        {
            if (range.Start.Date == rangeStart && range.End.Date == rangeEnd)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a value indicating whether the specified date range overlaps with dates in this collection.
    /// </summary>
    public bool ContainsAny(CalendarDateRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        foreach (var blackout in this)
        {
            if (blackout.ContainsAny(range))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        VerifyAccess();
        foreach (var item in Items)
        {
            UnregisterItem(item);
        }

        base.ClearItems();
        _owner.RefreshCalendarView();
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, CalendarDateRange item)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(item);
        ValidateRange(item.Start, item.End);
        RegisterItem(item);
        try
        {
            base.InsertItem(index, item);
        }
        catch
        {
            UnregisterItem(item);
            throw;
        }

        _owner.RefreshCalendarView();
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        VerifyAccess();
        CalendarDateRange item = this[index];
        UnregisterItem(item);
        base.RemoveItem(index);
        _owner.RefreshCalendarView();
    }

    /// <inheritdoc />
    protected override void SetItem(int index, CalendarDateRange item)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(item);
        ValidateRange(item.Start, item.End);

        CalendarDateRange oldItem = this[index];
        UnregisterItem(oldItem);
        RegisterItem(item);
        try
        {
            base.SetItem(index, item);
        }
        catch
        {
            UnregisterItem(item);
            RegisterItem(oldItem);
            throw;
        }

        _owner.RefreshCalendarView();
    }

    private void RegisterItem(CalendarDateRange item)
    {
        item.Changing += OnItemChanging;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void UnregisterItem(CalendarDateRange item)
    {
        item.Changing -= OnItemChanging;
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemChanging(object? sender, CalendarDateRangeChangingEventArgs e) =>
        ValidateRange(e.Start, e.End);

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _owner.RefreshCalendarView();

    private void ValidateRange(DateTime start, DateTime end)
    {
        DateTime effectiveEnd = end < start ? start : end;
        foreach (DateTime selectedDate in _owner.SelectedDates)
        {
            if (selectedDate.Date >= start.Date && selectedDate.Date <= effectiveEnd.Date)
            {
                throw new ArgumentOutOfRangeException(nameof(start),
                    "A blackout range cannot contain an already selected date.");
            }
        }
    }

    private void VerifyAccess()
    {
        if (Thread.CurrentThread != _dispatcherThread)
        {
            throw new NotSupportedException("Calendar collections can only be changed on their creating thread.");
        }
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
    private readonly Thread _dispatcherThread;
    private readonly List<DateTime> _pendingAdded = new();
    private readonly List<DateTime> _pendingRemoved = new();
    private int _batchDepth;

    public SelectedDatesCollection(Calendar owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _dispatcherThread = Thread.CurrentThread;
    }

    internal DateTime? MinimumDate => Count == 0 ? null : this.Min();
    internal DateTime? MaximumDate => Count == 0 ? null : this.Max();

    /// <summary>
    /// Adds a range of dates to the collection.
    /// </summary>
    public void AddRange(DateTime start, DateTime end)
    {
        VerifyAccess();
        if (_owner.SelectionMode == CalendarSelectionMode.None)
        {
            throw new InvalidOperationException("Dates cannot be selected when SelectionMode is None.");
        }

        var dates = EnumerateRange(start, end).ToArray();
        if (_owner.SelectionMode == CalendarSelectionMode.SingleDate && dates.Length > 1)
        {
            throw new InvalidOperationException("SingleDate selection mode accepts only one date.");
        }

        foreach (DateTime date in dates)
        {
            ValidateDate(date);
        }

        BeginBatch();
        try
        {
            if (_owner.SelectionMode == CalendarSelectionMode.SingleRange && Count > 0)
            {
                ClearItemsCore();
            }

            foreach (DateTime date in dates)
            {
                if (!ContainsDay(date))
                {
                    base.InsertItem(Count, date);
                    _pendingAdded.Add(date);
                }
            }
        }
        finally
        {
            EndBatch();
        }
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        VerifyAccess();
        BeginBatch();
        try
        {
            ClearItemsCore();
        }
        finally
        {
            EndBatch();
        }
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, DateTime item)
    {
        VerifyAccess();
        if (ContainsDay(item))
        {
            return;
        }

        ValidateSelectionModeForInsert();
        ValidateDate(item);

        BeginBatch();
        try
        {
            if (_owner.SelectionMode == CalendarSelectionMode.SingleRange && Count > 0)
            {
                ClearItemsCore();
                index = 0;
            }

            base.InsertItem(index, item);
            _pendingAdded.Add(item);
        }
        finally
        {
            EndBatch();
        }
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        VerifyAccess();
        DateTime removed = this[index];
        base.RemoveItem(index);
        _pendingRemoved.Add(removed);
        FlushOwnerIfNeeded();
    }

    /// <inheritdoc />
    protected override void SetItem(int index, DateTime item)
    {
        VerifyAccess();
        ValidateDate(item);
        if (ContainsDay(item, index))
        {
            return;
        }

        DateTime removed = this[index];
        base.SetItem(index, item);
        _pendingRemoved.Add(removed);
        _pendingAdded.Add(item);
        FlushOwnerIfNeeded();
    }

    internal void ReplaceFromSelectedDate(DateTime? selectedDate)
    {
        VerifyAccess();
        BeginBatch();
        try
        {
            ClearItemsCore();
            if (selectedDate.HasValue)
            {
                base.InsertItem(0, selectedDate.Value);
                _pendingAdded.Add(selectedDate.Value);
            }
        }
        finally
        {
            EndBatch();
        }
    }

    internal bool RemoveDay(DateTime date)
    {
        int index = IndexOfDay(date);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    private void ClearItemsCore()
    {
        if (Count == 0)
        {
            return;
        }

        _pendingRemoved.AddRange(this);
        base.ClearItems();
    }

    private void ValidateSelectionModeForInsert()
    {
        switch (_owner.SelectionMode)
        {
            case CalendarSelectionMode.None:
                throw new InvalidOperationException("Dates cannot be selected when SelectionMode is None.");
            case CalendarSelectionMode.SingleDate when Count > 0:
                throw new InvalidOperationException("SingleDate selection mode accepts only one date.");
        }
    }

    private void ValidateDate(DateTime date)
    {
        if (!_owner.IsDateSelectableForSelection(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), "The date is outside the selectable range or is blacked out.");
        }
    }

    private bool ContainsDay(DateTime date, int ignoredIndex = -1) => IndexOfDay(date, ignoredIndex) >= 0;

    private int IndexOfDay(DateTime date, int ignoredIndex = -1)
    {
        DateTime day = date.Date;
        for (int index = 0; index < Count; index++)
        {
            if (index != ignoredIndex && this[index].Date == day)
            {
                return index;
            }
        }

        return -1;
    }

    private void BeginBatch() => _batchDepth++;

    private void EndBatch()
    {
        _batchDepth--;
        FlushOwnerIfNeeded();
    }

    private void FlushOwnerIfNeeded()
    {
        if (_batchDepth != 0 || (_pendingAdded.Count == 0 && _pendingRemoved.Count == 0))
        {
            return;
        }

        DateTime[] removed = _pendingRemoved.ToArray();
        DateTime[] added = _pendingAdded.ToArray();
        _pendingRemoved.Clear();
        _pendingAdded.Clear();
        _owner.OnSelectedDatesCollectionChanged(removed, added);
    }

    private static IEnumerable<DateTime> EnumerateRange(DateTime start, DateTime end)
    {
        int direction = end >= start ? 1 : -1;
        DateTime current = start;
        while (true)
        {
            yield return current;
            if (current == end)
            {
                yield break;
            }

            if ((direction > 0 && current == DateTime.MaxValue) ||
                (direction < 0 && current == DateTime.MinValue))
            {
                yield break;
            }

            DateTime next = current.AddDays(direction);
            if ((direction > 0 && next > end) || (direction < 0 && next < end))
            {
                yield break;
            }

            current = next;
        }
    }

    private void VerifyAccess()
    {
        if (Thread.CurrentThread != _dispatcherThread)
        {
            throw new NotSupportedException("Calendar collections can only be changed on their creating thread.");
        }
    }
}
