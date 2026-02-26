using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a templated button control to be displayed in a CommandBar.
/// </summary>
public sealed class AppBarButton : Button, ICommandBarElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(IconElement), typeof(AppBarButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(AppBarButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the LabelPosition dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelPositionProperty =
        DependencyProperty.Register(nameof(LabelPosition), typeof(CommandBarLabelPosition), typeof(AppBarButton),
            new PropertyMetadata(CommandBarLabelPosition.Default));

    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(AppBarButton),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DynamicOverflowOrder dependency property.
    /// </summary>
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
    public string Label
    {
        get => (string?)GetValue(LabelProperty) ?? string.Empty;
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating the placement and visibility of the label.
    /// </summary>
    public CommandBarLabelPosition LabelPosition
    {
        get => (CommandBarLabelPosition)(GetValue(LabelPositionProperty) ?? CommandBarLabelPosition.Default);
        set => SetValue(LabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the element is shown in its compact representation.
    /// </summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty)!;
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// Gets or sets the priority of this element's dynamic overflow behavior.
    /// </summary>
    public int DynamicOverflowOrder
    {
        get => (int)GetValue(DynamicOverflowOrderProperty)!;
        set => SetValue(DynamicOverflowOrderProperty, value);
    }

    #endregion

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureDefaultTemplateIfMissing();
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == FrameworkElement.StyleProperty && Template == null)
        {
            EnsureDefaultTemplateIfMissing();
        }
    }

    private void EnsureDefaultTemplateIfMissing()
    {
        if (Template != null)
            return;

        var activeStyle = GetEffectiveStyle();

        // Explicit/implicit style already defines a template.
        if (TryGetTemplateFromStyle(activeStyle, out _))
            return;

        var appResources = Application.Current?.Resources;
        if (appResources == null)
            return;

        var themeStyle = FindMergedStyle(appResources.MergedDictionaries, typeof(AppBarButton));
        if (themeStyle == null)
            return;

        if (!TryGetTemplateFromStyle(themeStyle, out var template) || template == null)
            return;

        // Preserve style color overrides while restoring default visual structure.
        Template = template;
    }

    private Style? GetEffectiveStyle()
    {
        // Explicit style path.
        if (Style != null)
            return Style;

        // Implicit style path (FrameworkElement applies implicit style without assigning Style DP).
        return TryFindResource(typeof(AppBarButton)) as Style;
    }

    private static Style? FindMergedStyle(IList<ResourceDictionary> dictionaries, object key)
    {
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            var dictionary = dictionaries[i];

            if (dictionary.TryGetValue(key, out var styleValue) && styleValue is Style style)
                return style;

            var nested = FindMergedStyle(dictionary.MergedDictionaries, key);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static bool TryGetTemplateFromStyle(Style? style, out ControlTemplate? template)
    {
        while (style != null)
        {
            foreach (var setter in style.Setters)
            {
                if (setter.Property == TemplateProperty && setter.Value is ControlTemplate controlTemplate)
                {
                    template = controlTemplate;
                    return true;
                }
            }

            style = style.BasedOn;
        }

        template = null;
        return false;
    }
}
