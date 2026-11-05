using System.ComponentModel;

namespace Jalium.UI.Controls.Primitives;
/// <summary>Describes a date cell independently of its replaceable visual template.</summary>
public sealed class CalendarDateItem : INotifyPropertyChanged
{
    private DateTime _date;
    private Calendar? _owner;
    private string _text = string.Empty, _accessibleName = string.Empty;
    private bool _isSelected, _isToday, _isBlackedOut, _isInactive, _isEnabled;
    private Style? _style;
    internal CalendarDateItem(System.Windows.Input.ICommand command) => SelectCommand = command;
    public DateTime Date => _date;
    public Calendar? Owner => _owner;
    public string Text => _text;
    public Visibility Visibility => _text.Length == 0 ? Visibility.Hidden : Visibility.Visible;
    public string AccessibleName => _accessibleName;
    public bool IsSelected => _isSelected;
    public bool IsToday => _isToday;
    public bool IsBlackedOut => _isBlackedOut;
    public bool IsInactive => _isInactive;
    public bool IsEnabled => _isEnabled;
    public Style? Style => _style;
    public System.Windows.Input.ICommand SelectCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Update(Calendar? owner, DateTime date, string text, string name, bool selected, bool today, bool blackedOut, bool inactive, bool enabled, Style? style)
    {
        Set(ref _owner, owner, nameof(Owner));
        Set(ref _date, date, nameof(Date));
        Set(ref _text, text, nameof(Text));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
        Set(ref _accessibleName, name, nameof(AccessibleName));
        Set(ref _isSelected, selected, nameof(IsSelected));
        Set(ref _isToday, today, nameof(IsToday));
        Set(ref _isBlackedOut, blackedOut, nameof(IsBlackedOut));
        Set(ref _isInactive, inactive, nameof(IsInactive));
        Set(ref _isEnabled, enabled, nameof(IsEnabled));
        Set(ref _style, style, nameof(Style));
    }

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
