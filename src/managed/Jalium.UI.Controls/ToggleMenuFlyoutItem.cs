namespace Jalium.UI.Controls;
/// <summary>
/// Represents an item in a MenuFlyout that a user can change between two states, checked or unchecked.
/// </summary>
public class ToggleMenuFlyoutItem : MenuFlyoutItem
{
#region Dependency Properties
    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(ToggleMenuFlyoutItem), new PropertyMetadata(false, (d, _) => ((ToggleMenuFlyoutItem)d).UpdateItemPresentation()));
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets whether the ToggleMenuFlyoutItem is checked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty)!; set => SetValue(IsCheckedProperty, value); }

#endregion
    /// <summary>
    /// Initializes a new instance of the ToggleMenuFlyoutItem class.
    /// </summary>
    public ToggleMenuFlyoutItem()
    {
    }

    /// <inheritdoc/>
    protected override void OnItemInvoking()
    {
        SetCurrentValue(IsCheckedProperty, !IsChecked);
    }
}
