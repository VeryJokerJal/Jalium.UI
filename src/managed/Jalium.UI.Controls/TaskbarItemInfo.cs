using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the state of the taskbar progress indicator.
/// </summary>
public enum TaskbarItemProgressState
{
    /// <summary>
    /// No progress indicator is shown.
    /// </summary>
    None,

    /// <summary>
    /// A green progress indicator is shown.
    /// </summary>
    Normal,

    /// <summary>
    /// A pulsing green indicator is shown.
    /// </summary>
    Indeterminate,

    /// <summary>
    /// A red progress indicator is shown.
    /// </summary>
    Error,

    /// <summary>
    /// A yellow progress indicator is shown.
    /// </summary>
    Paused
}

/// <summary>
/// Represents information about how the taskbar thumbnail is displayed.
/// </summary>
public sealed class TaskbarItemInfo : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the ProgressState dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ProgressStateProperty =
        DependencyProperty.Register(nameof(ProgressState), typeof(TaskbarItemProgressState), typeof(TaskbarItemInfo),
            new PropertyMetadata(TaskbarItemProgressState.None, OnProgressStateChanged));

    /// <summary>
    /// Identifies the ProgressValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ProgressValueProperty =
        DependencyProperty.Register(nameof(ProgressValue), typeof(double), typeof(TaskbarItemInfo),
            new PropertyMetadata(0.0, OnProgressValueChanged, CoerceProgressValue));

    /// <summary>
    /// Identifies the Overlay dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OverlayProperty =
        DependencyProperty.Register(nameof(Overlay), typeof(ImageSource), typeof(TaskbarItemInfo),
            new PropertyMetadata(null, OnOverlayChanged));

    /// <summary>
    /// Identifies the Description dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(TaskbarItemInfo),
            new PropertyMetadata(string.Empty, OnDescriptionChanged));

    /// <summary>
    /// Identifies the ThumbnailClipMargin dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ThumbnailClipMarginProperty =
        DependencyProperty.Register(nameof(ThumbnailClipMargin), typeof(Thickness), typeof(TaskbarItemInfo),
            new PropertyMetadata(Thickness.Zero, OnThumbnailClipMarginChanged));

    /// <summary>
    /// Identifies the ThumbButtonInfos dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ThumbButtonInfosProperty =
        DependencyProperty.Register(nameof(ThumbButtonInfos), typeof(ThumbButtonInfoCollection), typeof(TaskbarItemInfo),
            new PropertyMetadata(null));

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the TaskbarItemInfo class.
    /// </summary>
    public TaskbarItemInfo()
    {
        ThumbButtonInfos = new ThumbButtonInfoCollection();
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value that indicates how the progress indicator is displayed in the taskbar button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public TaskbarItemProgressState ProgressState
    {
        get => (TaskbarItemProgressState)(GetValue(ProgressStateProperty) ?? TaskbarItemProgressState.None);
        set => SetValue(ProgressStateProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates the fullness of the progress indicator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double ProgressValue
    {
        get => (double)GetValue(ProgressValueProperty)!;
        set => SetValue(ProgressValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the image that is displayed over the taskbar button icon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ImageSource? Overlay
    {
        get => (ImageSource?)GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    /// <summary>
    /// Gets or sets the text for the taskbar item tooltip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Description
    {
        get => (string)(GetValue(DescriptionProperty) ?? string.Empty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that specifies the part of the application window's client area
    /// that is displayed in the taskbar thumbnail.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Thickness ThumbnailClipMargin
    {
        get => (Thickness)GetValue(ThumbnailClipMarginProperty)!;
        set => SetValue(ThumbnailClipMarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of ThumbButtonInfo objects that are associated
    /// with the Window's taskbar thumbnail.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ThumbButtonInfoCollection? ThumbButtonInfos
    {
        get => (ThumbButtonInfoCollection?)GetValue(ThumbButtonInfosProperty);
        set => SetValue(ThumbButtonInfosProperty, value);
    }

    #endregion

    #region Property Changed

    private static void OnProgressStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarItemInfo taskbar)
        {
            taskbar.UpdateProgressStateInternal((TaskbarItemProgressState)e.NewValue!);
        }
    }

    private static void OnProgressValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarItemInfo taskbar)
        {
            taskbar.UpdateProgressValueInternal((double)e.NewValue!);
        }
    }

    private static object CoerceProgressValue(DependencyObject d, object? value)
    {
        var progress = (double)(value ?? 0.0);
        return Math.Clamp(progress, 0.0, 1.0);
    }

    private static void OnOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarItemInfo taskbar)
        {
            taskbar.UpdateOverlayInternal((ImageSource?)e.NewValue);
        }
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarItemInfo taskbar)
        {
            taskbar.UpdateDescriptionInternal((string)e.NewValue!);
        }
    }

    private static void OnThumbnailClipMarginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarItemInfo taskbar)
        {
            taskbar.UpdateThumbnailClipMarginInternal((Thickness)e.NewValue!);
        }
    }

    #endregion

    #region Internal Methods (Platform Implementation Hooks)

    /// <summary>
    /// Updates the progress state on the taskbar.
    /// </summary>
    private void UpdateProgressStateInternal(TaskbarItemProgressState state)
    {
        // Platform-specific implementation using ITaskbarList3
    }

    /// <summary>
    /// Updates the progress value on the taskbar.
    /// </summary>
    private void UpdateProgressValueInternal(double value)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Updates the overlay icon on the taskbar.
    /// </summary>
    private void UpdateOverlayInternal(ImageSource? icon)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Updates the description tooltip on the taskbar.
    /// </summary>
    private void UpdateDescriptionInternal(string description)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Updates the thumbnail clip margin.
    /// </summary>
    private void UpdateThumbnailClipMarginInternal(Thickness margin)
    {
        // Platform-specific implementation
    }

    #endregion
}

/// <summary>
/// Represents information about a button in the taskbar thumbnail.
/// </summary>
public sealed class ThumbButtonInfo : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the ImageSource dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ThumbButtonInfo),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Description dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(ThumbButtonInfo),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the IsEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register(nameof(IsEnabled), typeof(bool), typeof(ThumbButtonInfo),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the Visibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VisibilityProperty =
        DependencyProperty.Register(nameof(Visibility), typeof(Visibility), typeof(ThumbButtonInfo),
            new PropertyMetadata(Visibility.Visible));

    /// <summary>
    /// Identifies the IsInteractive dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(ThumbButtonInfo),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the DismissWhenClicked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DismissWhenClickedProperty =
        DependencyProperty.Register(nameof(DismissWhenClicked), typeof(bool), typeof(ThumbButtonInfo),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsBackgroundVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsBackgroundVisibleProperty =
        DependencyProperty.Register(nameof(IsBackgroundVisible), typeof(bool), typeof(ThumbButtonInfo),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ThumbButtonInfo),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ThumbButtonInfo),
            new PropertyMetadata(null));

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the button is clicked.
    /// </summary>
    public event EventHandler? Click;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the image source for the button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the description (tooltip) for the button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Description
    {
        get => (string)(GetValue(DescriptionProperty) ?? string.Empty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the button is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty)!;
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Visibility Visibility
    {
        get => (Visibility)GetValue(VisibilityProperty)!;
        set => SetValue(VisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the button is interactive.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty)!;
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to dismiss the thumbnail when clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool DismissWhenClicked
    {
        get => (bool)GetValue(DismissWhenClickedProperty)!;
        set => SetValue(DismissWhenClickedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the button background is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsBackgroundVisible
    {
        get => (bool)GetValue(IsBackgroundVisibleProperty)!;
        set => SetValue(IsBackgroundVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to invoke when the button is clicked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the command parameter.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Raises the Click event.
    /// </summary>
    internal void RaiseClick()
    {
        Click?.Invoke(this, EventArgs.Empty);

        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    #endregion
}

/// <summary>
/// A collection of ThumbButtonInfo objects.
/// </summary>
public sealed class ThumbButtonInfoCollection : System.Collections.ObjectModel.ObservableCollection<ThumbButtonInfo>
{
    /// <summary>
    /// Maximum number of buttons allowed (Windows limitation).
    /// </summary>
    public const int MaxButtons = 7;

    /// <inheritdoc />
    protected override void InsertItem(int index, ThumbButtonInfo item)
    {
        if (Count >= MaxButtons)
            throw new InvalidOperationException($"Cannot add more than {MaxButtons} thumb buttons.");

        base.InsertItem(index, item);
    }
}
