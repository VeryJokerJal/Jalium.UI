using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;
/// <summary>
/// Represents a button used in Calendar controls for month and year selection.
/// </summary>
public sealed class CalendarButton : Button
{
    /// <summary>Gets or sets the theme/state text role used when Foreground is not overridden.</summary>
    public static readonly DependencyProperty CalendarForegroundProperty = DependencyProperty.Register(nameof(CalendarForeground), typeof(Brush), typeof(CalendarButton), new PropertyMetadata(Brushes.White));
    public Brush? CalendarForeground { get => (Brush? )GetValue(CalendarForegroundProperty); set => SetValue(CalendarForegroundProperty, value); }

#region Dependency Properties
    /// <summary>
    /// Identifies the HasSelectedDays read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey HasSelectedDaysPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasSelectedDays), typeof(bool), typeof(CalendarButton), new PropertyMetadata(false, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the HasSelectedDays dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty HasSelectedDaysProperty = HasSelectedDaysPropertyKey.DependencyProperty;
    /// <summary>
    /// Identifies the IsInactive dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsInactiveProperty = DependencyProperty.Register(nameof(IsInactive), typeof(bool), typeof(CalendarButton), new PropertyMetadata(false, OnVisualPropertyChanged));
#endregion
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(CalendarButton), new PropertyMetadata(false, (d, e) => ((CalendarButton)d).SetHasSelectedDays((bool)e.NewValue!)));
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty)!; set => SetValue(IsSelectedProperty, value); }

#region CLR Properties
    /// <summary>
    /// Gets a value indicating whether the month/year contains selected days.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool HasSelectedDays => (bool)GetValue(HasSelectedDaysProperty)!;

    /// <summary>
    /// Gets or sets a value indicating whether this button is for display purposes only.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsInactive { get => (bool)GetValue(IsInactiveProperty)!; set => SetValue(IsInactiveProperty, value); }

    /// <summary>
    /// Gets or sets the Calendar that owns this button.
    /// </summary>
    private static readonly DependencyPropertyKey HasCalendarOwnerPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasCalendarOwner), typeof(bool), typeof(CalendarButton), new PropertyMetadata(false));
    public static readonly DependencyProperty HasCalendarOwnerProperty = HasCalendarOwnerPropertyKey.DependencyProperty;
    public bool HasCalendarOwner => (bool)GetValue(HasCalendarOwnerProperty)!;

    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner), typeof(Calendar), typeof(CalendarButton), new PropertyMetadata(null, (d, e) => d.SetValue(HasCalendarOwnerPropertyKey, e.NewValue is Calendar)));
    public Calendar? Owner { get => (Calendar? )GetValue(OwnerProperty); set => SetValue(OwnerProperty, value); }

#endregion
#region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref = "CalendarButton"/> class.
    /// </summary>
    static CalendarButton() => DefaultStyleKeyProperty.OverrideMetadata(typeof(CalendarButton), new PropertyMetadata(typeof(CalendarButton)));
    public CalendarButton()
    {
        UseTemplateContentManagement();
    }

#endregion
#region Internal Methods
    /// <summary>
    /// Sets the HasSelectedDays property.
    /// </summary>
    internal void SetHasSelectedDays(bool value)
    {
        SetValue(HasSelectedDaysPropertyKey, value);
    }

#endregion
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates();
    }

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, HasSelectedDays ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, IsInactive ? "Inactive" : "Active", false);
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CalendarButton)d).UpdateStates();
}
