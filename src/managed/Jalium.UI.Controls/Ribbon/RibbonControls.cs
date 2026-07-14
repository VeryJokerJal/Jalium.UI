using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Globalization;
using System.Windows.Input;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Represents a button on a Ribbon.
/// </summary>
public sealed class RibbonButton : Button
{
    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(RibbonButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label
    {
        get => (string)GetValue(LabelProperty)!;
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets the small image source.
    /// </summary>
    public ImageSource? SmallImageSource { get; set; }

    /// <summary>
    /// Gets or sets the large image source.
    /// </summary>
    public ImageSource? LargeImageSource { get; set; }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }

    /// <summary>
    /// Gets or sets the tooltip title.
    /// </summary>
    public string? ToolTipTitle { get; set; }

    /// <summary>
    /// Gets or sets the tooltip description.
    /// </summary>
    public string? ToolTipDescription { get; set; }
}

/// <summary>
/// Represents a toggle button on a Ribbon.
/// </summary>
public class RibbonToggleButton : ToggleButton
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the small image source.
    /// </summary>
    public ImageSource? SmallImageSource { get; set; }

    /// <summary>
    /// Gets or sets the large image source.
    /// </summary>
    public ImageSource? LargeImageSource { get; set; }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }
}

/// <summary>
/// Represents a split button (button + dropdown) on a Ribbon.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonSplitButton : RibbonMenuButton, ICommandSource
{
    private bool _commandBaseIsEnabled = true;

    public static readonly DependencyProperty CheckedBackgroundProperty =
        DependencyProperty.Register(nameof(CheckedBackground), typeof(Brush), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CheckedBorderBrushProperty =
        DependencyProperty.Register(nameof(CheckedBorderBrush), typeof(Brush), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly RoutedEvent ClickEvent = MenuItem.ClickEvent.AddOwner(typeof(RibbonSplitButton));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(RibbonSplitButton),
            new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(RibbonSplitButton),
            new PropertyMetadata(null, OnCommandStateChanged));

    public static readonly DependencyProperty CommandTargetProperty =
        DependencyProperty.Register(nameof(CommandTarget), typeof(IInputElement), typeof(RibbonSplitButton),
            new PropertyMetadata(null, OnCommandStateChanged));

    public static readonly DependencyProperty DropDownToolTipDescriptionProperty =
        DependencyProperty.Register(nameof(DropDownToolTipDescription), typeof(string), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipFooterDescriptionProperty =
        DependencyProperty.Register(nameof(DropDownToolTipFooterDescription), typeof(string), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipFooterImageSourceProperty =
        DependencyProperty.Register(nameof(DropDownToolTipFooterImageSource), typeof(ImageSource), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipFooterTitleProperty =
        DependencyProperty.Register(nameof(DropDownToolTipFooterTitle), typeof(string), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipImageSourceProperty =
        DependencyProperty.Register(nameof(DropDownToolTipImageSource), typeof(ImageSource), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropDownToolTipTitleProperty =
        DependencyProperty.Register(nameof(DropDownToolTipTitle), typeof(string), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderKeyTipProperty =
        DependencyProperty.Register(nameof(HeaderKeyTip), typeof(string), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderQuickAccessToolBarIdProperty =
        DependencyProperty.Register(nameof(HeaderQuickAccessToolBarId), typeof(object), typeof(RibbonSplitButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsCheckableProperty =
        DependencyProperty.Register(nameof(IsCheckable), typeof(bool), typeof(RibbonSplitButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(RibbonSplitButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty LabelPositionProperty =
        DependencyProperty.Register(nameof(LabelPosition), typeof(RibbonSplitButtonLabelPosition), typeof(RibbonSplitButton),
            new PropertyMetadata(RibbonSplitButtonLabelPosition.Header));

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

    public virtual ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public virtual object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public virtual IInputElement? CommandTarget
    {
        get => (IInputElement?)GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

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

    public bool IsCheckable
    {
        get => (bool)(GetValue(IsCheckableProperty) ?? false);
        set => SetValue(IsCheckableProperty, value);
    }

    public bool IsChecked
    {
        get => (bool)(GetValue(IsCheckedProperty) ?? false);
        set => SetValue(IsCheckedProperty, value);
    }

    public RibbonSplitButtonLabelPosition LabelPosition
    {
        get => (RibbonSplitButtonLabelPosition)(GetValue(LabelPositionProperty) ?? RibbonSplitButtonLabelPosition.Header);
        set => SetValue(LabelPositionProperty, value);
    }

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!e.Handled && IsEnabled && ReferenceEquals(e.OriginalSource, this))
        {
            InvokeHeader();
            e.Handled = true;
        }
    }

    private void InvokeHeader()
    {
        if (IsCheckable)
        {
            IsChecked = !IsChecked;
        }

        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        ExecuteCommand();
    }

    private void ExecuteCommand()
    {
        var command = Command;
        if (command == null)
        {
            return;
        }

        if (command is RoutedCommand routedCommand)
        {
            var target = CommandTarget ?? this;
            if (routedCommand.CanExecute(CommandParameter, target))
            {
                routedCommand.Execute(CommandParameter, target);
            }

            return;
        }

        if (command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RibbonSplitButton splitButton)
        {
            return;
        }

        if (e.OldValue is ICommand oldCommand)
        {
            oldCommand.CanExecuteChanged -= splitButton.OnCanExecuteChanged;
        }

        if (e.OldValue == null && e.NewValue is ICommand)
        {
            splitButton._commandBaseIsEnabled = splitButton.IsEnabled;
        }

        if (e.NewValue is ICommand newCommand)
        {
            newCommand.CanExecuteChanged += splitButton.OnCanExecuteChanged;
            splitButton.UpdateCanExecute();
        }
        else
        {
            splitButton.SetCurrentValue(IsEnabledProperty, splitButton._commandBaseIsEnabled);
        }
    }

    private static void OnCommandStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RibbonSplitButton splitButton)
        {
            splitButton.UpdateCanExecute();
        }
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateCanExecute();

    private void UpdateCanExecute()
    {
        var command = Command;
        if (command == null)
        {
            return;
        }

        var canExecute = command is RoutedCommand routedCommand
            ? routedCommand.CanExecute(CommandParameter, CommandTarget ?? this)
            : command.CanExecute(CommandParameter);
        SetCurrentValue(IsEnabledProperty, _commandBaseIsEnabled && canExecute);
    }
}

/// <summary>
/// Represents a menu button on a Ribbon.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public class RibbonMenuButton : Menu
{
    private static readonly DependencyPropertyKey HasGalleryPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasGallery), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsDropDownPositionedAbovePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDropDownPositionedAbove), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey RibbonPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Ribbon), typeof(Ribbon), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CanAddToQuickAccessToolBarDirectlyProperty =
        DependencyProperty.Register(nameof(CanAddToQuickAccessToolBarDirectly), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CanUserResizeHorizontallyProperty =
        DependencyProperty.Register(nameof(CanUserResizeHorizontally), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanUserResizeVerticallyProperty =
        DependencyProperty.Register(nameof(CanUserResizeVertically), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty DropDownHeightProperty =
        DependencyProperty.Register(nameof(DropDownHeight), typeof(double), typeof(RibbonMenuButton),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty FocusedBackgroundProperty =
        DependencyProperty.Register(nameof(FocusedBackground), typeof(Brush), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FocusedBorderBrushProperty =
        DependencyProperty.Register(nameof(FocusedBorderBrush), typeof(Brush), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HasGalleryProperty = HasGalleryPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    public static readonly DependencyProperty IsDropDownPositionedAboveProperty =
        IsDropDownPositionedAbovePropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsInControlGroupProperty =
        DependencyProperty.Register(nameof(IsInControlGroup), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsInQuickAccessToolBarProperty =
        DependencyProperty.Register(nameof(IsInQuickAccessToolBar), typeof(bool), typeof(RibbonMenuButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty KeyTipProperty =
        DependencyProperty.Register(nameof(KeyTip), typeof(string), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(RibbonMenuButton),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LargeImageSourceProperty =
        DependencyProperty.Register(nameof(LargeImageSource), typeof(ImageSource), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MouseOverBackgroundProperty =
        DependencyProperty.Register(nameof(MouseOverBackground), typeof(Brush), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MouseOverBorderBrushProperty =
        DependencyProperty.Register(nameof(MouseOverBorderBrush), typeof(Brush), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(nameof(PressedBackground), typeof(Brush), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PressedBorderBrushProperty =
        DependencyProperty.Register(nameof(PressedBorderBrush), typeof(Brush), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty QuickAccessToolBarIdProperty =
        DependencyProperty.Register(nameof(QuickAccessToolBarId), typeof(object), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RibbonProperty = RibbonPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SmallImageSourceProperty =
        DependencyProperty.Register(nameof(SmallImageSource), typeof(ImageSource), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipDescriptionProperty =
        DependencyProperty.Register(nameof(ToolTipDescription), typeof(string), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipFooterDescriptionProperty =
        DependencyProperty.Register(nameof(ToolTipFooterDescription), typeof(string), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipFooterImageSourceProperty =
        DependencyProperty.Register(nameof(ToolTipFooterImageSource), typeof(ImageSource), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipFooterTitleProperty =
        DependencyProperty.Register(nameof(ToolTipFooterTitle), typeof(string), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipImageSourceProperty =
        DependencyProperty.Register(nameof(ToolTipImageSource), typeof(ImageSource), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ToolTipTitleProperty =
        DependencyProperty.Register(nameof(ToolTipTitle), typeof(string), typeof(RibbonMenuButton),
            new PropertyMetadata(null));

    public RibbonMenuButton()
    {
        IsMainMenu = false;
    }

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

    public double DropDownHeight
    {
        get => (double)(GetValue(DropDownHeightProperty) ?? double.NaN);
        set => SetValue(DropDownHeightProperty, value);
    }

    public Brush? FocusedBackground
    {
        get => (Brush?)GetValue(FocusedBackgroundProperty);
        set => SetValue(FocusedBackgroundProperty, value);
    }

    public Brush? FocusedBorderBrush
    {
        get => (Brush?)GetValue(FocusedBorderBrushProperty);
        set => SetValue(FocusedBorderBrushProperty, value);
    }

    public bool HasGallery => (bool)(GetValue(HasGalleryProperty) ?? false);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)(GetValue(IsDropDownOpenProperty) ?? false);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public bool IsDropDownPositionedAbove =>
        (bool)(GetValue(IsDropDownPositionedAboveProperty) ?? false);

    public bool IsInControlGroup
    {
        get => (bool)(GetValue(IsInControlGroupProperty) ?? false);
        set => SetValue(IsInControlGroupProperty, value);
    }

    public bool IsInQuickAccessToolBar
    {
        get => (bool)(GetValue(IsInQuickAccessToolBarProperty) ?? false);
        set => SetValue(IsInQuickAccessToolBarProperty, value);
    }

    public string? KeyTip
    {
        get => (string?)GetValue(KeyTipProperty);
        set => SetValue(KeyTipProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label
    {
        get => (string)(GetValue(LabelProperty) ?? string.Empty);
        set => SetValue(LabelProperty, value);
    }

    public ImageSource? LargeImageSource
    {
        get => (ImageSource?)GetValue(LargeImageSourceProperty);
        set => SetValue(LargeImageSourceProperty, value);
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

    public Ribbon? Ribbon => (Ribbon?)GetValue(RibbonProperty);

    public ImageSource? SmallImageSource
    {
        get => (ImageSource?)GetValue(SmallImageSourceProperty);
        set => SetValue(SmallImageSourceProperty, value);
    }

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

    public event EventHandler? DropDownClosed;

    public event EventHandler? DropDownOpened;

    internal void SetRibbon(Ribbon? ribbon) => SetValue(RibbonPropertyKey, ribbon);

    protected override Panel CreateItemsPanel() =>
        new StackPanel { Orientation = Orientation.Vertical };

    protected override FrameworkElement GetContainerForItem(object item) => new RibbonMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item) =>
        item is RibbonMenuItem or RibbonSeparator or RibbonGallery;

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        UpdateHasGallery();
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        SetValue(RibbonPropertyKey, FindRibbonAncestor());
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RibbonMenuButton menuButton)
        {
            return;
        }

        if ((bool)(e.NewValue ?? false))
        {
            menuButton.DropDownOpened?.Invoke(menuButton, EventArgs.Empty);
        }
        else
        {
            menuButton.DropDownClosed?.Invoke(menuButton, EventArgs.Empty);
        }
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
/// Represents a text box on a Ribbon.
/// </summary>
public sealed class RibbonTextBox : TextBox
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the small image source.
    /// </summary>
    public ImageSource? SmallImageSource { get; set; }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }
}

/// <summary>
/// Represents a combo box on a Ribbon.
/// </summary>
public class RibbonComboBox : RibbonMenuButton
{
    private RibbonGallery? _firstGallery;

    private static readonly DependencyPropertyKey SelectionBoxItemPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectionBoxItem), typeof(object), typeof(RibbonComboBox),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey SelectionBoxItemStringFormatPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectionBoxItemStringFormat), typeof(string), typeof(RibbonComboBox),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey SelectionBoxItemTemplatePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectionBoxItemTemplate), typeof(DataTemplate), typeof(RibbonComboBox),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey SelectionBoxItemTemplateSelectorPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectionBoxItemTemplateSelector), typeof(DataTemplateSelector), typeof(RibbonComboBox),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey ShowKeyboardCuesPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ShowKeyboardCues), typeof(bool), typeof(RibbonComboBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(RibbonComboBox),
            new PropertyMetadata(false, OnIsEditableChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(RibbonComboBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SelectionBoxItemProperty =
        SelectionBoxItemPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectionBoxItemStringFormatProperty =
        SelectionBoxItemStringFormatPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectionBoxItemTemplateProperty =
        SelectionBoxItemTemplatePropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectionBoxItemTemplateSelectorProperty =
        SelectionBoxItemTemplateSelectorPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectionBoxWidthProperty =
        DependencyProperty.Register(nameof(SelectionBoxWidth), typeof(double), typeof(RibbonComboBox),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ShowKeyboardCuesProperty =
        ShowKeyboardCuesPropertyKey.DependencyProperty;

    public static readonly DependencyProperty StaysOpenOnEditProperty =
        DependencyProperty.Register(nameof(StaysOpenOnEdit), typeof(bool), typeof(RibbonComboBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(RibbonComboBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsEditable
    {
        get => (bool)(GetValue(IsEditableProperty) ?? false);
        set => SetValue(IsEditableProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)(GetValue(IsReadOnlyProperty) ?? false);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public object? SelectionBoxItem => GetValue(SelectionBoxItemProperty);

    public string? SelectionBoxItemStringFormat =>
        (string?)GetValue(SelectionBoxItemStringFormatProperty);

    public DataTemplate? SelectionBoxItemTemplate =>
        (DataTemplate?)GetValue(SelectionBoxItemTemplateProperty);

    public DataTemplateSelector? SelectionBoxItemTemplateSelector =>
        (DataTemplateSelector?)GetValue(SelectionBoxItemTemplateSelectorProperty);

    public double SelectionBoxWidth
    {
        get => (double)(GetValue(SelectionBoxWidthProperty) ?? 0.0);
        set => SetValue(SelectionBoxWidthProperty, value);
    }

    public bool ShowKeyboardCues => (bool)(GetValue(ShowKeyboardCuesProperty) ?? false);

    public bool StaysOpenOnEdit
    {
        get => (bool)(GetValue(StaysOpenOnEditProperty) ?? false);
        set => SetValue(StaysOpenOnEditProperty, value);
    }

    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusWithinChanged(e);
        SetValue(ShowKeyboardCuesPropertyKey, (bool)(e.NewValue ?? false) && !IsDropDownOpen);
    }

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        AttachFirstGallery();
    }

    private static void OnIsEditableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RibbonComboBox comboBox)
        {
            comboBox.UpdateSelectionBox();
        }
    }

    private void AttachFirstGallery()
    {
        RibbonGallery? gallery = null;
        foreach (var item in Items)
        {
            if (item is RibbonGallery candidate)
            {
                gallery = candidate;
                break;
            }
        }

        if (ReferenceEquals(_firstGallery, gallery))
        {
            return;
        }

        if (_firstGallery != null)
        {
            _firstGallery.SelectionChanged -= OnGallerySelectionChanged;
        }

        _firstGallery = gallery;
        if (_firstGallery != null)
        {
            _firstGallery.SelectionChanged += OnGallerySelectionChanged;
        }

        UpdateSelectionBox();
    }

    private void OnGallerySelectionChanged(object sender, RoutedEventArgs e) => UpdateSelectionBox();

    private void UpdateSelectionBox()
    {
        var selectedItem = _firstGallery?.SelectedItem;
        SetValue(SelectionBoxItemPropertyKey, selectedItem ?? string.Empty);
        SetValue(SelectionBoxItemTemplatePropertyKey, _firstGallery?.ItemTemplate);
        SetValue(SelectionBoxItemTemplateSelectorPropertyKey, _firstGallery?.ItemTemplateSelector);
        SetValue(SelectionBoxItemStringFormatPropertyKey, _firstGallery?.ItemStringFormat);

        if (!IsEditable)
        {
            SetCurrentValue(TextProperty, FormatSelection(selectedItem));
        }
    }

    private string FormatSelection(object? selectedItem)
    {
        if (selectedItem == null)
        {
            return string.Empty;
        }

        var format = SelectionBoxItemStringFormat;
        return string.IsNullOrEmpty(format)
            ? Convert.ToString(selectedItem, CultureInfo.CurrentCulture) ?? string.Empty
            : string.Format(CultureInfo.CurrentCulture, format, selectedItem);
    }
}

/// <summary>
/// Represents a check box on a Ribbon.
/// </summary>
public sealed class RibbonCheckBox : CheckBox
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the small image source.
    /// </summary>
    public ImageSource? SmallImageSource { get; set; }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }
}

/// <summary>
/// Represents a separator on a Ribbon.
/// </summary>
public class RibbonSeparator : Separator
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Separator);

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? Label { get; set; }
}
