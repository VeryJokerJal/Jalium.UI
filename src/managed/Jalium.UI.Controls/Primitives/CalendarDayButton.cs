using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;
/// <summary>
/// Represents a button for a single day in a Calendar control.
/// </summary>
public sealed class CalendarDayButton : Button
{
    /// <summary>Gets or sets the theme/state text role used when Foreground is not overridden.</summary>
    public static readonly DependencyProperty CalendarForegroundProperty = DependencyProperty.Register(nameof(CalendarForeground), typeof(Brush), typeof(CalendarDayButton), new PropertyMetadata(Brushes.White));
    public Brush? CalendarForeground { get => (Brush? )GetValue(CalendarForegroundProperty); set => SetValue(CalendarForegroundProperty, value); }

#region Dependency Properties
    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the IsToday dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTodayProperty = DependencyProperty.Register(nameof(IsToday), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the IsBlackedOut dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsBlackedOutProperty = DependencyProperty.Register(nameof(IsBlackedOut), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the IsInactive dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsInactiveProperty = DependencyProperty.Register(nameof(IsInactive), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the IsHighlighted dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false, OnVisualPropertyChanged));
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets a value indicating whether this day is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty)!; set => SetValue(IsSelectedProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether this day is today.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsToday { get => (bool)GetValue(IsTodayProperty)!; set => SetValue(IsTodayProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether this day is blacked out (not selectable).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsBlackedOut { get => (bool)GetValue(IsBlackedOutProperty)!; set => SetValue(IsBlackedOutProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether this day is from an adjacent month.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsInactive { get => (bool)GetValue(IsInactiveProperty)!; set => SetValue(IsInactiveProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether this day is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty)!; set => SetValue(IsHighlightedProperty, value); }

    /// <summary>
    /// Gets or sets the Calendar that owns this button.
    /// </summary>
    private static readonly DependencyPropertyKey HasCalendarOwnerPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasCalendarOwner), typeof(bool), typeof(CalendarDayButton), new PropertyMetadata(false));
    public static readonly DependencyProperty HasCalendarOwnerProperty = HasCalendarOwnerPropertyKey.DependencyProperty;
    public bool HasCalendarOwner => (bool)GetValue(HasCalendarOwnerProperty)!;

    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner), typeof(Calendar), typeof(CalendarDayButton), new PropertyMetadata(null, (d, e) => d.SetValue(HasCalendarOwnerPropertyKey, e.NewValue is Calendar)));
    public Calendar? Owner { get => (Calendar? )GetValue(OwnerProperty); set => SetValue(OwnerProperty, value); }

    /// <summary>
    /// Gets or sets the date this button represents.
    /// </summary>
    public static readonly DependencyProperty DateProperty = DependencyProperty.Register(nameof(Date), typeof(DateTime? ), typeof(CalendarDayButton), new PropertyMetadata(null));
    public DateTime? Date { get => (DateTime? )GetValue(DateProperty); set => SetValue(DateProperty, value); }

#endregion
#region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref = "CalendarDayButton"/> class.
    /// </summary>
    static CalendarDayButton() => DefaultStyleKeyProperty.OverrideMetadata(typeof(CalendarDayButton), new PropertyMetadata(typeof(CalendarDayButton)));
    public CalendarDayButton()
    {
        UseTemplateContentManagement();
    }

#endregion
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates();
    }

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, IsToday ? "Today" : "RegularDay", false);
        VisualStateManager.GoToState(this, IsBlackedOut ? "Blackout" : "Available", false);
        VisualStateManager.GoToState(this, IsInactive ? "Inactive" : "Active", false);
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CalendarDayButton)d).UpdateStates();
}
