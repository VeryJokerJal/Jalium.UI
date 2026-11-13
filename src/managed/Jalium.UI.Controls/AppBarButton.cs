using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a templated button control to be displayed in a CommandBar.
/// </summary>
public class AppBarButton : Button, ICommandBarElement
{
    static AppBarButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppBarButton), new PropertyMetadata(typeof(AppBarButton)));
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.AppBarButtonAutomationPeer(this);

    #region Dependency Properties

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(IconElement), typeof(AppBarButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(AppBarButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the LabelPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty LabelPositionProperty =
        DependencyProperty.Register(nameof(LabelPosition), typeof(CommandBarLabelPosition), typeof(AppBarButton),
            new PropertyMetadata(CommandBarLabelPosition.Default));

    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(AppBarButton),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DynamicOverflowOrder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DynamicOverflowOrderProperty =
        DependencyProperty.Register(nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarButton),
            new PropertyMetadata(0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the graphic content of the app bar button.
    /// Accepts an IconElement (SymbolIcon, FontIcon, PathIcon) or a string
    /// glyph which will be automatically converted to a FontIcon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public IconElement? Icon
    {
        get => (IconElement?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Sets the icon from a glyph string (convenience for code-behind).
    /// Automatically wraps in a FontIcon with Segoe Fluent Icons.
    /// </summary>
    public void SetIconGlyph(string glyph)
    {
        Icon = new FontIcon { Glyph = glyph };
    }

    /// <summary>
    /// Gets or sets the text label displayed on the app bar button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label
    {
        get => (string?)GetValue(LabelProperty) ?? string.Empty;
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating the placement and visibility of the label.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CommandBarLabelPosition LabelPosition
    {
        get => (CommandBarLabelPosition)(GetValue(LabelPositionProperty) ?? CommandBarLabelPosition.Default);
        set => SetValue(LabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the element is shown in its compact representation.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty)!;
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// Gets or sets the priority of this element's dynamic overflow behavior.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int DynamicOverflowOrder
    {
        get => (int)GetValue(DynamicOverflowOrderProperty)!;
        set => SetValue(DynamicOverflowOrderProperty, value);
    }

    #endregion

}
