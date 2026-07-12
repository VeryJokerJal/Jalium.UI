using System.Collections;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Animation;

/// <summary>Represents a timeline that owns child timelines.</summary>
public abstract class TimelineGroup : Timeline, IAddChild
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(TimelineCollection),
            typeof(TimelineGroup),
            new PropertyMetadata(null, OnChildrenChanged));

    protected TimelineGroup()
    {
    }

    protected TimelineGroup(TimeSpan? beginTime)
        : base(beginTime)
    {
    }

    protected TimelineGroup(TimeSpan? beginTime, Duration duration)
        : base(beginTime, duration)
    {
    }

    protected TimelineGroup(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
        : base(beginTime, duration, repeatBehavior)
    {
    }

    public TimelineCollection Children
    {
        get
        {
            if (GetValue(ChildrenProperty) is TimelineCollection children)
            {
                return children;
            }

            children = new TimelineCollection();
            SetValue(ChildrenProperty, children);
            return children;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetValue(ChildrenProperty, value);
        }
    }

    public new ClockGroup CreateClock() => (ClockGroup)base.CreateClock(hasControllableRoot: true);

    protected internal override Clock AllocateClock()
    {
        var group = new ClockGroup(this);
        foreach (Timeline child in Children)
        {
            group.AddChild(child.CreateClock(hasControllableRoot: false));
        }

        return group;
    }

    public new TimelineGroup Clone() => (TimelineGroup)base.Clone();
    public new TimelineGroup CloneCurrentValue() => (TimelineGroup)base.CloneCurrentValue();

    protected virtual void AddChild(object child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not Timeline timeline)
        {
            throw new ArgumentException("A TimelineGroup can contain only Timeline instances.", nameof(child));
        }

        Children.Add(timeline);
    }

    protected virtual void AddText(string childText)
    {
        if (!string.IsNullOrWhiteSpace(childText))
        {
            throw new InvalidOperationException("Text content is not valid inside a TimelineGroup.");
        }
    }

    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);

    protected override Duration GetNaturalDurationCore(Clock clock)
    {
        TimeSpan maximum = TimeSpan.Zero;
        bool foundDuration = false;

        foreach (Timeline child in Children)
        {
            Clock childClock = child.CreateClock(hasControllableRoot: false);
            Duration childDuration = child.GetNaturalDuration(childClock);
            if (!childDuration.HasTimeSpan)
            {
                continue;
            }

            TimeSpan total = (child.BeginTime ?? TimeSpan.Zero) + childDuration.TimeSpan;
            if (!foundDuration || total > maximum)
            {
                maximum = total;
                foundDuration = true;
            }
        }

        return foundDuration ? new Duration(maximum) : Duration.Automatic;
    }

    private static void OnChildrenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var group = (TimelineGroup)d;
        group.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, ChildrenProperty);
    }
}

/// <summary>A freezable, deep-cloning collection of timelines.</summary>
public sealed partial class TimelineCollection : Animatable, IList, IList<Timeline>
{
    private List<Timeline> _items;
    private uint _version;

    public TimelineCollection()
    {
        _items = [];
    }

    public TimelineCollection(int capacity)
    {
        _items = new List<Timeline>(capacity);
    }

    public TimelineCollection(IEnumerable<Timeline> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = [];
        foreach (Timeline timeline in collection)
        {
            Add(timeline);
        }
    }

    public Timeline this[int index]
    {
        get
        {
            ReadPreamble();
            return _items[index];
        }
        set
        {
            EnsureTimeline(value);
            WritePreamble();
            Timeline oldValue = _items[index];
            if (!ReferenceEquals(oldValue, value))
            {
                OnFreezablePropertyChanged(oldValue, value);
                _items[index] = value;
            }
            _version++;
            WritePostscript();
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    public int Count
    {
        get
        {
            ReadPreamble();
            return _items.Count;
        }
    }

    bool ICollection<Timeline>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(Timeline item)
    {
        EnsureTimeline(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Add(item);
        _version++;
        WritePostscript();
    }

    int IList.Add(object? value)
    {
        Timeline item = Cast(value);
        Add(item);
        return _items.Count - 1;
    }

    public void Clear()
    {
        WritePreamble();
        foreach (Timeline item in _items)
        {
            OnFreezablePropertyChanged(item, null);
        }
        _items.Clear();
        _version++;
        WritePostscript();
    }

    public bool Contains(Timeline item)
    {
        ReadPreamble();
        return _items.Contains(item);
    }

    bool IList.Contains(object? value) => value is Timeline timeline && Contains(timeline);

    public void CopyTo(Timeline[] array, int arrayIndex)
    {
        ReadPreamble();
        _items.CopyTo(array, arrayIndex);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank != 1)
        {
            throw new ArgumentException("The destination array must be one-dimensional.", nameof(array));
        }

        for (int i = 0; i < _items.Count; i++)
        {
            array.SetValue(_items[i], index + i);
        }
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<Timeline> IEnumerable<Timeline>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(Timeline item)
    {
        ReadPreamble();
        return _items.IndexOf(item);
    }

    int IList.IndexOf(object? value) => value is Timeline timeline ? IndexOf(timeline) : -1;

    public void Insert(int index, Timeline item)
    {
        EnsureTimeline(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Insert(index, item);
        _version++;
        WritePostscript();
    }

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    public bool Remove(Timeline item)
    {
        WritePreamble();
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        OnFreezablePropertyChanged(_items[index], null);
        _items.RemoveAt(index);
        _version++;
        WritePostscript();
        return true;
    }

    void IList.Remove(object? value)
    {
        if (value is Timeline timeline)
        {
            Remove(timeline);
        }
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        Timeline item = _items[index];
        OnFreezablePropertyChanged(item, null);
        _items.RemoveAt(index);
        _version++;
        WritePostscript();
    }

    public new TimelineCollection Clone() => (TimelineCollection)base.Clone();
    public new TimelineCollection CloneCurrentValue() => (TimelineCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new TimelineCollection();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CloneItems((TimelineCollection)source, static timeline => timeline.Clone());
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CloneItems((TimelineCollection)source, static timeline => timeline.CloneCurrentValue());
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CloneItems((TimelineCollection)source, static timeline => (Timeline)timeline.GetAsFrozen());
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CloneItems((TimelineCollection)source, static timeline => (Timeline)timeline.GetCurrentValueAsFrozen());
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
        {
            return false;
        }

        foreach (Timeline timeline in _items)
        {
            if (isChecking)
            {
                if (!timeline.CanFreeze)
                {
                    return false;
                }
            }
            else
            {
                timeline.Freeze();
            }
        }

        return true;
    }

    private void CloneItems(TimelineCollection source, Func<Timeline, Timeline> clone)
    {
        _items = new List<Timeline>(source._items.Count);
        foreach (Timeline item in source._items)
        {
            Timeline copy = clone(item);
            OnFreezablePropertyChanged(null, copy);
            _items.Add(copy);
        }
        _version = 0;
    }

    private static void EnsureTimeline(Timeline? timeline)
    {
        if (timeline is null)
        {
            throw new ArgumentException("TimelineCollection does not accept null items.", nameof(timeline));
        }
    }

    private static Timeline Cast(object? value)
    {
        if (value is not Timeline timeline)
        {
            throw new ArgumentException("The value must be a Timeline.", nameof(value));
        }
        return timeline;
    }

    public struct Enumerator : IEnumerator<Timeline>
    {
        private readonly TimelineCollection _collection;
        private readonly uint _version;
        private int _index;
        private Timeline? _current;

        internal Enumerator(TimelineCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = null;
        }

        public Timeline Current => _index >= 0
            ? _current!
            : throw new InvalidOperationException("The enumerator is not positioned on an item.");

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            VerifyVersion();
            if (_index + 1 < _collection._items.Count)
            {
                _current = _collection._items[++_index];
                return true;
            }

            _index = -2;
            _current = null;
            return false;
        }

        public void Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = null;
        }

        public void Dispose()
        {
        }

        private readonly void VerifyVersion()
        {
            if (_version != _collection._version)
            {
                throw new InvalidOperationException("The collection was modified after the enumerator was created.");
            }
        }
    }
}
