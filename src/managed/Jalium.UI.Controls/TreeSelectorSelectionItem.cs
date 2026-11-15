using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>A template-driven selected value with an accessible remove action.</summary>
[TemplatePart(Name = "PART_RemoveButton", Type = typeof(ButtonBase))]
public class TreeSelectorSelectionItem : ContentControl
{
    private ButtonBase? _removeButton;

    static TreeSelectorSelectionItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeSelectorSelectionItem), new PropertyMetadata(typeof(TreeSelectorSelectionItem)));

    public static readonly DependencyProperty RemoveButtonLabelProperty = DependencyProperty.Register(nameof(RemoveButtonLabel), typeof(string), typeof(TreeSelectorSelectionItem), new PropertyMetadata("Remove selection"));
    public string RemoveButtonLabel { get => (string)GetValue(RemoveButtonLabelProperty)!; set => SetValue(RemoveButtonLabelProperty, value); }

    public static readonly RoutedEvent RemoveRequestedEvent = EventManager.RegisterRoutedEvent(nameof(RemoveRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TreeSelectorSelectionItem));
    public event RoutedEventHandler RemoveRequested { add => AddHandler(RemoveRequestedEvent, value); remove => RemoveHandler(RemoveRequestedEvent, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_removeButton != null) _removeButton.Click -= OnRemoveClick;
        _removeButton = GetTemplateChild("PART_RemoveButton") as ButtonBase;
        if (_removeButton != null) _removeButton.Click += OnRemoveClick;
    }

    internal override void OnTemplateContentClearing()
    {
        if (_removeButton != null) _removeButton.Click -= OnRemoveClick;
        _removeButton = null;
        base.OnTemplateContentClearing();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || !ReferenceEquals(sender, _removeButton)) return;
        RaiseEvent(new RoutedEventArgs(RemoveRequestedEvent, this));
        e.Handled = true;
    }
}
