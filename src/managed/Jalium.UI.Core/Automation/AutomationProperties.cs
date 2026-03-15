namespace Jalium.UI.Automation;

/// <summary>
/// Provides attached properties for UI Automation.
/// </summary>
public static class AutomationProperties
{
    #region Name Property

    /// <summary>
    /// Identifies the Name attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.RegisterAttached(
            "Name",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty, OnNameChanged));

    /// <summary>
    /// Gets the Name attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public static string GetName(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(NameProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the Name attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Framework)]
    public static void SetName(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(NameProperty, value);
    }

    private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var peer = element.GetAutomationPeer();
            peer?.RaisePropertyChangedEvent(
                AutomationProperty.NameProperty,
                e.OldValue,
                e.NewValue);
        }
    }

    #endregion

    #region AutomationId Property

    /// <summary>
    /// Identifies the AutomationId attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AutomationIdProperty =
        DependencyProperty.RegisterAttached(
            "AutomationId",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty, OnAutomationIdChanged));

    /// <summary>
    /// Gets the AutomationId attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetAutomationId(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(AutomationIdProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the AutomationId attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetAutomationId(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AutomationIdProperty, value);
    }

    private static void OnAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var peer = element.GetAutomationPeer();
            peer?.RaisePropertyChangedEvent(
                AutomationProperty.AutomationIdProperty,
                e.OldValue,
                e.NewValue);
        }
    }

    #endregion

    #region HelpText Property

    /// <summary>
    /// Identifies the HelpText attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HelpTextProperty =
        DependencyProperty.RegisterAttached(
            "HelpText",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the HelpText attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetHelpText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(HelpTextProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the HelpText attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetHelpText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HelpTextProperty, value);
    }

    #endregion

    #region ItemStatus Property

    /// <summary>
    /// Identifies the ItemStatus attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemStatusProperty =
        DependencyProperty.RegisterAttached(
            "ItemStatus",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the ItemStatus attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static string GetItemStatus(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(ItemStatusProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the ItemStatus attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static void SetItemStatus(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ItemStatusProperty, value);
    }

    #endregion

    #region ItemType Property

    /// <summary>
    /// Identifies the ItemType attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ItemTypeProperty =
        DependencyProperty.RegisterAttached(
            "ItemType",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the ItemType attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetItemType(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(ItemTypeProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the ItemType attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetItemType(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ItemTypeProperty, value);
    }

    #endregion

    #region LabeledBy Property

    /// <summary>
    /// Identifies the LabeledBy attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LabeledByProperty =
        DependencyProperty.RegisterAttached(
            "LabeledBy",
            typeof(UIElement),
            typeof(AutomationProperties),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the LabeledBy attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static UIElement? GetLabeledBy(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (UIElement?)element.GetValue(LabeledByProperty);
    }

    /// <summary>
    /// Sets the LabeledBy attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static void SetLabeledBy(DependencyObject element, UIElement? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LabeledByProperty, value);
    }

    #endregion

    #region IsRequiredForForm Property

    /// <summary>
    /// Identifies the IsRequiredForForm attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsRequiredForFormProperty =
        DependencyProperty.RegisterAttached(
            "IsRequiredForForm",
            typeof(bool),
            typeof(AutomationProperties),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets the IsRequiredForForm attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsRequiredForForm(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsRequiredForFormProperty) ?? false);
    }

    /// <summary>
    /// Sets the IsRequiredForForm attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static void SetIsRequiredForForm(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsRequiredForFormProperty, value);
    }

    #endregion

    #region AcceleratorKey Property

    /// <summary>
    /// Identifies the AcceleratorKey attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AcceleratorKeyProperty =
        DependencyProperty.RegisterAttached(
            "AcceleratorKey",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the AcceleratorKey attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetAcceleratorKey(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(AcceleratorKeyProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the AcceleratorKey attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetAcceleratorKey(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AcceleratorKeyProperty, value);
    }

    #endregion

    #region AccessKey Property

    /// <summary>
    /// Identifies the AccessKey attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AccessKeyProperty =
        DependencyProperty.RegisterAttached(
            "AccessKey",
            typeof(string),
            typeof(AutomationProperties),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets the AccessKey attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static string GetAccessKey(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)(element.GetValue(AccessKeyProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the AccessKey attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetAccessKey(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AccessKeyProperty, value);
    }

    #endregion

    #region LiveSetting Property

    /// <summary>
    /// Identifies the LiveSetting attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LiveSettingProperty =
        DependencyProperty.RegisterAttached(
            "LiveSetting",
            typeof(AutomationLiveSetting),
            typeof(AutomationProperties),
            new PropertyMetadata(AutomationLiveSetting.Off));

    /// <summary>
    /// Gets the LiveSetting attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static AutomationLiveSetting GetLiveSetting(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (AutomationLiveSetting)(element.GetValue(LiveSettingProperty) ?? AutomationLiveSetting.Off);
    }

    /// <summary>
    /// Sets the LiveSetting attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetLiveSetting(DependencyObject element, AutomationLiveSetting value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LiveSettingProperty, value);
    }

    #endregion

    #region IsOffscreenBehavior Property

    /// <summary>
    /// Identifies the IsOffscreenBehavior attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOffscreenBehaviorProperty =
        DependencyProperty.RegisterAttached(
            "IsOffscreenBehavior",
            typeof(IsOffscreenBehavior),
            typeof(AutomationProperties),
            new PropertyMetadata(IsOffscreenBehavior.Default));

    /// <summary>
    /// Gets the IsOffscreenBehavior attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static IsOffscreenBehavior GetIsOffscreenBehavior(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (IsOffscreenBehavior)(element.GetValue(IsOffscreenBehaviorProperty) ?? IsOffscreenBehavior.Default);
    }

    /// <summary>
    /// Sets the IsOffscreenBehavior attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static void SetIsOffscreenBehavior(DependencyObject element, IsOffscreenBehavior value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsOffscreenBehaviorProperty, value);
    }

    #endregion
}

/// <summary>
/// Specifies the live setting behavior for UI Automation.
/// </summary>
public enum AutomationLiveSetting
{
    /// <summary>
    /// The element does not send notifications.
    /// </summary>
    Off,

    /// <summary>
    /// The element sends non-interruptive notifications.
    /// </summary>
    Polite,

    /// <summary>
    /// The element sends interruptive notifications.
    /// </summary>
    Assertive
}

/// <summary>
/// Specifies how an element reports its offscreen behavior.
/// </summary>
public enum IsOffscreenBehavior
{
    /// <summary>
    /// Use the default behavior.
    /// </summary>
    Default,

    /// <summary>
    /// The element is offscreen if not visible.
    /// </summary>
    Offscreen,

    /// <summary>
    /// The element is never considered offscreen.
    /// </summary>
    Onscreen,

    /// <summary>
    /// Use the parent's coordinates to determine offscreen.
    /// </summary>
    FromClip
}
