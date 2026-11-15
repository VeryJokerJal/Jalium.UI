using Jalium.UI.Media;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls.Ribbon;

[TemplatePart(Name = "PART_HeaderButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_DropDownButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_MenuRoot", Type = typeof(FrameworkElement))]
[TemplateVisualState(Name = "Opened", GroupName = "DropDownStates")]
[TemplateVisualState(Name = "Closed", GroupName = "DropDownStates")]
public partial class RibbonMenuButton
{
    private Button? _dropDownButton;
    private Popup? _menuPopup;
    private FrameworkElement? _menuRoot;
    static RibbonMenuButton() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonMenuButton), new PropertyMetadata(typeof(RibbonMenuButton)));
    private void InitializeDropDownBehavior()
    {
        Unloaded += (_, _) => SetCurrentValue(IsDropDownOpenProperty, false);
        AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(OnItemInvoked), true);
    }
    public override void OnApplyTemplate()
    {
        ReleaseDropDown(); base.OnApplyTemplate();
        _dropDownButton = GetTemplateChild("PART_DropDownButton") as Button ?? GetTemplateChild("PART_HeaderButton") as Button;
        _menuPopup = GetTemplateChild("PART_Popup") as Popup;
        _menuRoot = GetTemplateChild("PART_MenuRoot") as FrameworkElement;
        _menuRoot?.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(OnItemInvoked), true);
        if (_dropDownButton is not null) _dropDownButton.Click += OnDropDownClick;
        UpdateDropDownState();
    }
    protected override void OnTemplateChanging() { ReleaseDropDown(); base.OnTemplateChanging(); }
    private void ReleaseDropDown()
    {
        var hadParts = _menuPopup is not null || _dropDownButton is not null;
        if (_dropDownButton is not null) _dropDownButton.Click -= OnDropDownClick;
        _dropDownButton = null;
        _menuRoot?.RemoveHandler(MenuItem.ClickEvent, new RoutedEventHandler(OnItemInvoked));
        _menuRoot = null;
        if (_menuPopup is not null)
        {
            _menuPopup.ClearBinding(Popup.IsOpenProperty);
            _menuPopup.IsOpen = false;
        }
        _menuPopup = null;
        if (hadParts) SetCurrentValue(IsDropDownOpenProperty, false);
    }
    private void OnDropDownClick(object sender, RoutedEventArgs e) => SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
    private void OnItemInvoked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem { HasItems: false, StaysOpenOnClick: false }) SetCurrentValue(IsDropDownOpenProperty, false);
    }
    private void UpdateDropDownState()
    {
        if (TemplateRootInternal is not null)
            VisualStateManager.GoToState(this, IsDropDownOpen ? "Opened" : "Closed", false);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsDropDownOpenProperty) UpdateDropDownState();
        if (e.Property == IsEnabledProperty && !IsEnabled) SetCurrentValue(IsDropDownOpenProperty, false);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false); _dropDownButton?.Focus(); e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

public partial class RibbonSplitButton
{
    private Button? _primaryButton;
    static RibbonSplitButton() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonSplitButton), new PropertyMetadata(typeof(RibbonSplitButton)));
    public override void OnApplyTemplate()
    {
        ReleasePrimary(); base.OnApplyTemplate();
        _primaryButton = GetTemplateChild("PART_HeaderButton") as Button;
        if (_primaryButton is not null) _primaryButton.Click += OnPrimaryClick;
    }
    protected override void OnTemplateChanging() { ReleasePrimary(); base.OnTemplateChanging(); }
    private void ReleasePrimary() { if (_primaryButton is not null) _primaryButton.Click -= OnPrimaryClick; _primaryButton = null; }
    private void OnPrimaryClick(object sender, RoutedEventArgs e) { if (IsEnabled) InvokeHeader(); }
}

public partial class RibbonGallery
{
    static RibbonGallery() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonGallery), new PropertyMetadata(typeof(RibbonGallery)));
    internal void RefreshItemSelection()
    {
        foreach (var item in Descendants(this))
        {
            var source = item.SourceItem;
            item.SetCurrentValue(RibbonGalleryItem.IsSelectedProperty, ReferenceEquals(SelectedItem, source) || Equals(SelectedItem, source));
        }
    }
    private static IEnumerable<RibbonGalleryItem> Descendants(Visual visual)
    {
        for (var i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child is RibbonGalleryItem item) yield return item;
            if (child is not null) foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    protected override FrameworkElement GetContainerForItem(object item) => item is RibbonGalleryCategory ? (FrameworkElement)item : new RibbonGalleryItem();
    protected override bool IsItemItsOwnContainerOverride(object item) => item is RibbonGalleryCategory or RibbonGalleryItem;
}

public partial class RibbonGalleryCategory
{
    static RibbonGalleryCategory() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonGalleryCategory), new PropertyMetadata(typeof(RibbonGalleryCategory)));
    protected override FrameworkElement GetContainerForItem(object item) => new RibbonGalleryItem();
    protected override bool IsItemItsOwnContainerOverride(object item) => item is RibbonGalleryItem;
}

[TemplatePart(Name = "PART_Button", Type = typeof(Button))]
public partial class RibbonGalleryItem
{
    private Button? _button;
    static RibbonGalleryItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonGalleryItem), new PropertyMetadata(typeof(RibbonGalleryItem)));
    internal object? SourceItem
    {
        get
        {
            for (var parent = VisualParent; parent is not null; parent = parent.VisualParent)
                if (parent is RibbonGalleryCategory or RibbonGallery)
                {
                    var item = ((ItemsControl)parent).ItemContainerGenerator.ItemFromContainer(this);
                    if (!ReferenceEquals(item, DependencyProperty.UnsetValue)) return item;
                }
            return this;
        }
    }
    private RibbonGallery? FindGallery()
    {
        for (var parent = VisualParent; parent is not null; parent = parent.VisualParent)
            if (parent is RibbonGallery gallery) return gallery;
        return null;
    }
    public override void OnApplyTemplate()
    {
        ReleaseButton(); base.OnApplyTemplate(); _button = GetTemplateChild("PART_Button") as Button;
        if (_button is not null) _button.Click += OnSelect;
        FindGallery()?.RefreshItemSelection();
    }
    protected override void OnTemplateChanging() { ReleaseButton(); base.OnTemplateChanging(); }
    private void ReleaseButton()
    {
        if (_button is not null)
        {
            _button.Click -= OnSelect;
            _button.ClearBinding(ContentControl.ContentProperty);
            _button.ClearValue(ContentControl.ContentProperty);
        }
        _button = null;
    }
    private void OnSelect(object sender, RoutedEventArgs e)
    {
        if (FindGallery() is { } gallery) gallery.SetCurrentValue(RibbonGallery.SelectedItemProperty, SourceItem);
        else SetCurrentValue(IsSelectedProperty, true);
    }
}
