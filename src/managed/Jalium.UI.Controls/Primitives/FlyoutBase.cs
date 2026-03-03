namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the preferred placement of a flyout relative to a visual element.
/// </summary>
public enum FlyoutPlacementMode
{
    Top,
    Bottom,
    Left,
    Right,
    Full,
    TopEdgeAlignedLeft,
    TopEdgeAlignedRight,
    BottomEdgeAlignedLeft,
    BottomEdgeAlignedRight,
    LeftEdgeAlignedTop,
    LeftEdgeAlignedBottom,
    RightEdgeAlignedTop,
    RightEdgeAlignedBottom,
    Auto
}

/// <summary>
/// Specifies the show mode for a flyout.
/// </summary>
public enum FlyoutShowMode
{
    Auto,
    Standard,
    Transient,
    TransientWithDismissOnPointerMoveAway
}

/// <summary>
/// Provides options for showing a flyout.
/// </summary>
public sealed class FlyoutShowOptions
{
    /// <summary>
    /// Gets or sets the preferred placement of the flyout.
    /// </summary>
    public FlyoutPlacementMode Placement { get; set; } = FlyoutPlacementMode.Auto;

    /// <summary>
    /// Gets or sets the point relative to the placement target at which to show the flyout.
    /// </summary>
    public Point? Position { get; set; }

    /// <summary>
    /// Gets or sets the show mode for the flyout.
    /// </summary>
    public FlyoutShowMode ShowMode { get; set; } = FlyoutShowMode.Auto;
}

/// <summary>
/// Represents the base class for flyout controls that display lightweight UI.
/// </summary>
public abstract class FlyoutBase : DependencyObject
{
    private Popup? _popup;
    private Control? _presenter;
    private FrameworkElement? _target;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(FlyoutPlacementMode), typeof(FlyoutBase),
            new PropertyMetadata(FlyoutPlacementMode.Bottom));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the default placement to be used for the flyout.
    /// </summary>
    public FlyoutPlacementMode Placement
    {
        get => (FlyoutPlacementMode)(GetValue(PlacementProperty) ?? FlyoutPlacementMode.Bottom);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the flyout is currently open.
    /// </summary>
    public bool IsOpen => _popup?.IsOpen == true;

    #endregion

    #region Events

    /// <summary>
    /// Occurs before the flyout is opened.
    /// </summary>
    public event EventHandler? Opening;

    /// <summary>
    /// Occurs after the flyout is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs before the flyout is closed.
    /// </summary>
    public event EventHandler? Closing;

    /// <summary>
    /// Occurs after the flyout is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    /// <summary>
    /// Shows the flyout placed in relation to the specified element.
    /// </summary>
    public void ShowAt(FrameworkElement placementTarget)
    {
        ShowAt(placementTarget, new FlyoutShowOptions());
    }

    /// <summary>
    /// Shows the flyout placed in relation to the specified element using the specified options.
    /// </summary>
    public void ShowAt(FrameworkElement placementTarget, FlyoutShowOptions options)
    {
        _target = placementTarget;

        Opening?.Invoke(this, EventArgs.Empty);

        EnsurePopup();
        if (_popup == null) return;

        _popup.PlacementTarget = placementTarget;
        _popup.Placement = MapPlacement(options.Placement != FlyoutPlacementMode.Auto ? options.Placement : Placement);

        if (options.Position.HasValue)
        {
            _popup.HorizontalOffset = options.Position.Value.X;
            _popup.VerticalOffset = options.Position.Value.Y;
        }

        _popup.IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the flyout.
    /// </summary>
    public void Hide()
    {
        if (_popup == null || !_popup.IsOpen) return;

        Closing?.Invoke(this, EventArgs.Empty);
        _popup.IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// When overridden in a derived class, initializes a control to show the flyout content.
    /// </summary>
    protected abstract Control CreatePresenter();

    private void EnsurePopup()
    {
        if (_popup != null) return;

        _presenter = CreatePresenter();
        if (_presenter == null) return;

        _popup = new Popup
        {
            StaysOpen = false,
            IsLightDismissEnabled = true
        };

        if (_presenter is global::Jalium.UI.Controls.MenuFlyoutPresenter)
        {
            // MenuFlyoutPresenter already paints its own popup chrome.
            // Avoid wrapping it with another Border, which creates double-border visuals.
            _popup.Child = _presenter;
        }
        else
        {
            var border = new Border
            {
                Child = _presenter,
                Background = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(45, 45, 48)),
                BorderBrush = new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(67, 67, 70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4)
            };

            _popup.Child = border;
        }
        _popup.Closed += (_, _) =>
        {
            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    private static PlacementMode MapPlacement(FlyoutPlacementMode flyoutPlacement)
    {
        return flyoutPlacement switch
        {
            FlyoutPlacementMode.Top => PlacementMode.Top,
            FlyoutPlacementMode.Left => PlacementMode.Left,
            FlyoutPlacementMode.Right => PlacementMode.Right,
            _ => PlacementMode.Bottom
        };
    }
}
