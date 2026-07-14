using System.Collections.Specialized;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Represents a menu item used by Ribbon drop-down controls.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonMenuItem : MenuItem
{
    private static readonly DependencyPropertyKey HasGalleryPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasGallery), typeof(bool), typeof(RibbonMenuItem),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsDropDownPositionedLeftPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDropDownPositionedLeft), typeof(bool), typeof(RibbonMenuItem),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey RibbonPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Ribbon), typeof(Ribbon), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CanAddToQuickAccessToolBarDirectlyProperty =
        DependencyProperty.Register(nameof(CanAddToQuickAccessToolBarDirectly), typeof(bool), typeof(RibbonMenuItem),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CanUserResizeHorizontallyProperty =
        DependencyProperty.Register(nameof(CanUserResizeHorizontally), typeof(bool), typeof(RibbonMenuItem),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanUserResizeVerticallyProperty =
        DependencyProperty.Register(nameof(CanUserResizeVertically), typeof(bool), typeof(RibbonMenuItem),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CheckedBackgroundProperty =
        DependencyProperty.Register(nameof(CheckedBackground), typeof(Brush), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CheckedBorderBrushProperty =
        DependencyProperty.Register(nameof(CheckedBorderBrush), typeof(Brush), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownHeightProperty =
        DependencyProperty.Register(nameof(DropDownHeight), typeof(double), typeof(RibbonMenuItem),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty HasGalleryProperty = HasGalleryPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsDropDownPositionedLeftProperty =
        IsDropDownPositionedLeftPropertyKey.DependencyProperty;

    public static readonly DependencyProperty KeyTipProperty =
        DependencyProperty.Register(nameof(KeyTip), typeof(string), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MouseOverBackgroundProperty =
        DependencyProperty.Register(nameof(MouseOverBackground), typeof(Brush), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MouseOverBorderBrushProperty =
        DependencyProperty.Register(nameof(MouseOverBorderBrush), typeof(Brush), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(nameof(PressedBackground), typeof(Brush), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBorderBrushProperty =
        DependencyProperty.Register(nameof(PressedBorderBrush), typeof(Brush), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty QuickAccessToolBarIdProperty =
        DependencyProperty.Register(nameof(QuickAccessToolBarId), typeof(object), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty QuickAccessToolBarImageSourceProperty =
        DependencyProperty.Register(nameof(QuickAccessToolBarImageSource), typeof(ImageSource), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RibbonProperty = RibbonPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ToolTipDescriptionProperty =
        DependencyProperty.Register(nameof(ToolTipDescription), typeof(string), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipFooterDescriptionProperty =
        DependencyProperty.Register(nameof(ToolTipFooterDescription), typeof(string), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipFooterImageSourceProperty =
        DependencyProperty.Register(nameof(ToolTipFooterImageSource), typeof(ImageSource), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipFooterTitleProperty =
        DependencyProperty.Register(nameof(ToolTipFooterTitle), typeof(string), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipImageSourceProperty =
        DependencyProperty.Register(nameof(ToolTipImageSource), typeof(ImageSource), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipTitleProperty =
        DependencyProperty.Register(nameof(ToolTipTitle), typeof(string), typeof(RibbonMenuItem),
            new PropertyMetadata(null));

    public bool CanAddToQuickAccessToolBarDirectly
    {
        get => (bool)(GetValue(CanAddToQuickAccessToolBarDirectlyProperty) ?? true);
        set => SetValue(CanAddToQuickAccessToolBarDirectlyProperty, value);
    }

    public bool CanUserResizeHorizontally
    {
        get => (bool)(GetValue(CanUserResizeHorizontallyProperty) ?? false);
        set => SetValue(CanUserResizeHorizontallyProperty, value);
    }

    public bool CanUserResizeVertically
    {
        get => (bool)(GetValue(CanUserResizeVerticallyProperty) ?? false);
        set => SetValue(CanUserResizeVerticallyProperty, value);
    }

    public Brush? CheckedBackground
    {
        get => (Brush?)GetValue(CheckedBackgroundProperty);
        set => SetValue(CheckedBackgroundProperty, value);
    }

    public Brush? CheckedBorderBrush
    {
        get => (Brush?)GetValue(CheckedBorderBrushProperty);
        set => SetValue(CheckedBorderBrushProperty, value);
    }

    public double DropDownHeight
    {
        get => (double)(GetValue(DropDownHeightProperty) ?? double.NaN);
        set => SetValue(DropDownHeightProperty, value);
    }

    public bool HasGallery => (bool)(GetValue(HasGalleryProperty) ?? false);

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public bool IsDropDownPositionedLeft =>
        (bool)(GetValue(IsDropDownPositionedLeftProperty) ?? false);

    public string? KeyTip
    {
        get => (string?)GetValue(KeyTipProperty);
        set => SetValue(KeyTipProperty, value);
    }

    public Brush? MouseOverBackground
    {
        get => (Brush?)GetValue(MouseOverBackgroundProperty);
        set => SetValue(MouseOverBackgroundProperty, value);
    }

    public Brush? MouseOverBorderBrush
    {
        get => (Brush?)GetValue(MouseOverBorderBrushProperty);
        set => SetValue(MouseOverBorderBrushProperty, value);
    }

    public Brush? PressedBackground
    {
        get => (Brush?)GetValue(PressedBackgroundProperty);
        set => SetValue(PressedBackgroundProperty, value);
    }

    public Brush? PressedBorderBrush
    {
        get => (Brush?)GetValue(PressedBorderBrushProperty);
        set => SetValue(PressedBorderBrushProperty, value);
    }

    public object? QuickAccessToolBarId
    {
        get => GetValue(QuickAccessToolBarIdProperty);
        set => SetValue(QuickAccessToolBarIdProperty, value);
    }

    public ImageSource? QuickAccessToolBarImageSource
    {
        get => (ImageSource?)GetValue(QuickAccessToolBarImageSourceProperty);
        set => SetValue(QuickAccessToolBarImageSourceProperty, value);
    }

    public Ribbon? Ribbon => (Ribbon?)GetValue(RibbonProperty);

    public string? ToolTipDescription
    {
        get => (string?)GetValue(ToolTipDescriptionProperty);
        set => SetValue(ToolTipDescriptionProperty, value);
    }

    public string? ToolTipFooterDescription
    {
        get => (string?)GetValue(ToolTipFooterDescriptionProperty);
        set => SetValue(ToolTipFooterDescriptionProperty, value);
    }

    public ImageSource? ToolTipFooterImageSource
    {
        get => (ImageSource?)GetValue(ToolTipFooterImageSourceProperty);
        set => SetValue(ToolTipFooterImageSourceProperty, value);
    }

    public string? ToolTipFooterTitle
    {
        get => (string?)GetValue(ToolTipFooterTitleProperty);
        set => SetValue(ToolTipFooterTitleProperty, value);
    }

    public ImageSource? ToolTipImageSource
    {
        get => (ImageSource?)GetValue(ToolTipImageSourceProperty);
        set => SetValue(ToolTipImageSourceProperty, value);
    }

    public string? ToolTipTitle
    {
        get => (string?)GetValue(ToolTipTitleProperty);
        set => SetValue(ToolTipTitleProperty, value);
    }

    protected override bool IsItemItsOwnContainerOverride(object item) =>
        item is RibbonMenuItem or RibbonSeparator or RibbonGallery;

    protected override FrameworkElement GetContainerForItem(object item) => new RibbonMenuItem();

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        UpdateHasGallery();
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        SetValue(RibbonPropertyKey, FindRibbonAncestor());
    }

    private void UpdateHasGallery()
    {
        var hasGallery = false;
        foreach (var item in Items)
        {
            if (item is RibbonGallery)
            {
                hasGallery = true;
                break;
            }
        }

        SetValue(HasGalleryPropertyKey, hasGallery);
    }

    private Ribbon? FindRibbonAncestor()
    {
        for (DependencyObject? current = Parent; current != null; current = (current as FrameworkElement)?.Parent)
        {
            if (current is Ribbon ribbon)
            {
                return ribbon;
            }
        }

        return null;
    }
}

/// <summary>
/// Represents a split menu item used by Ribbon drop-down controls.
/// </summary>
public class RibbonSplitMenuItem : RibbonMenuItem
{
    public static readonly DependencyProperty DropDownToolTipDescriptionProperty =
        DependencyProperty.Register(nameof(DropDownToolTipDescription), typeof(string), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipFooterDescriptionProperty =
        DependencyProperty.Register(nameof(DropDownToolTipFooterDescription), typeof(string), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipFooterImageSourceProperty =
        DependencyProperty.Register(nameof(DropDownToolTipFooterImageSource), typeof(ImageSource), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipFooterTitleProperty =
        DependencyProperty.Register(nameof(DropDownToolTipFooterTitle), typeof(string), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipImageSourceProperty =
        DependencyProperty.Register(nameof(DropDownToolTipImageSource), typeof(ImageSource), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipTitleProperty =
        DependencyProperty.Register(nameof(DropDownToolTipTitle), typeof(string), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderKeyTipProperty =
        DependencyProperty.Register(nameof(HeaderKeyTip), typeof(string), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderQuickAccessToolBarIdProperty =
        DependencyProperty.Register(nameof(HeaderQuickAccessToolBarId), typeof(object), typeof(RibbonSplitMenuItem),
            new PropertyMetadata(null));

    public string? DropDownToolTipDescription
    {
        get => (string?)GetValue(DropDownToolTipDescriptionProperty);
        set => SetValue(DropDownToolTipDescriptionProperty, value);
    }

    public string? DropDownToolTipFooterDescription
    {
        get => (string?)GetValue(DropDownToolTipFooterDescriptionProperty);
        set => SetValue(DropDownToolTipFooterDescriptionProperty, value);
    }

    public ImageSource? DropDownToolTipFooterImageSource
    {
        get => (ImageSource?)GetValue(DropDownToolTipFooterImageSourceProperty);
        set => SetValue(DropDownToolTipFooterImageSourceProperty, value);
    }

    public string? DropDownToolTipFooterTitle
    {
        get => (string?)GetValue(DropDownToolTipFooterTitleProperty);
        set => SetValue(DropDownToolTipFooterTitleProperty, value);
    }

    public ImageSource? DropDownToolTipImageSource
    {
        get => (ImageSource?)GetValue(DropDownToolTipImageSourceProperty);
        set => SetValue(DropDownToolTipImageSourceProperty, value);
    }

    public string? DropDownToolTipTitle
    {
        get => (string?)GetValue(DropDownToolTipTitleProperty);
        set => SetValue(DropDownToolTipTitleProperty, value);
    }

    public string? HeaderKeyTip
    {
        get => (string?)GetValue(HeaderKeyTipProperty);
        set => SetValue(HeaderKeyTipProperty, value);
    }

    public object? HeaderQuickAccessToolBarId
    {
        get => GetValue(HeaderQuickAccessToolBarIdProperty);
        set => SetValue(HeaderQuickAccessToolBarIdProperty, value);
    }
}

/// <summary>
/// Specifies the visual level of an application-menu item.
/// </summary>
public enum RibbonApplicationMenuItemLevel
{
    Top = 0,
    Middle = 1,
    Sub = 2,
}

/// <summary>
/// Specifies which half of a split button displays its label.
/// </summary>
public enum RibbonSplitButtonLabelPosition
{
    Header = 0,
    DropDown = 1,
}
