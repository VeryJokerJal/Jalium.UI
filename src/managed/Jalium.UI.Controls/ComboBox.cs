using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a selection control with a drop-down list that can be shown or hidden by clicking the control.
/// </summary>
public class ComboBox : Selector
{
    private Popup? _popup;
    private ToggleButton? _toggleButton;
    private StackPanel? _itemsPanel;
    private bool _isDropDownOpen;

    // Animation
    private Shapes.Path? _arrowPath;
    private RotateTransform? _arrowRotate;
    private DispatcherTimer? _animationTimer;
    private bool _isCloseAnimating;

    private const double OpenDurationMs = 250;
    private const double CloseDurationMs = 180;
    private static readonly CubicEase OpenEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase CloseEase = new() { EasingMode = EasingMode.EaseIn };

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(ComboBox),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    /// <summary>
    /// Identifies the MaxDropDownHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(nameof(MaxDropDownHeight), typeof(double), typeof(ComboBox),
            new PropertyMetadata(200.0));

    /// <summary>
    /// Identifies the PlaceholderText dependency property.
    /// </summary>
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(ComboBox),
            new PropertyMetadata("Select an item..."));

    /// <summary>
    /// Identifies the SelectionBoxItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionBoxItemProperty =
        DependencyProperty.Register(nameof(SelectionBoxItem), typeof(object), typeof(ComboBox),
            new PropertyMetadata(null, OnSelectionBoxItemChanged));

    private static void OnSelectionBoxItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            // Trigger UI update when selection box item changes
            comboBox.InvalidateVisual();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the dropdown is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the dropdown.
    /// </summary>
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when no item is selected.
    /// </summary>
    public string PlaceholderText
    {
        get => (string)(GetValue(PlaceholderTextProperty) ?? "Select an item...");
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets the item displayed in the selection box.
    /// </summary>
    public object? SelectionBoxItem
    {
        get => GetValue(SelectionBoxItemProperty);
        private set => SetValue(SelectionBoxItemProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the dropdown opens.
    /// </summary>
    public event EventHandler? DropDownOpened;

    /// <summary>
    /// Occurs when the dropdown closes.
    /// </summary>
    public event EventHandler? DropDownClosed;

    #endregion

    public ComboBox()
    {
        MinWidth = 120;
        Focusable = true;
        SizeChanged += OnComboBoxSizeChanged;

        // Initialize SelectionBoxItem with placeholder
        SelectionBoxItem = PlaceholderText;

        // Set up mouse handling for toggle
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));

        // Set up keyboard handling for navigation
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unhook old events
        if (_toggleButton != null)
        {
            _toggleButton.Checked -= OnToggleButtonChecked;
            _toggleButton.Unchecked -= OnToggleButtonUnchecked;
        }

        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
        }

        // Get template parts
        _toggleButton = GetTemplateChild("PART_ToggleButton") as ToggleButton;
        _popup = GetTemplateChild("PART_Popup") as Popup;

        // Wire up toggle button
        if (_toggleButton != null)
        {
            _toggleButton.Checked += OnToggleButtonChecked;
            _toggleButton.Unchecked += OnToggleButtonUnchecked;
        }

        // Wire up popup
        if (_popup != null)
        {
            _popup.Closed += OnPopupClosed;
        }

        UpdatePopupPlacementAndWidth();

        // Find the arrow Path inside ToggleButton's template for rotation animation
        _arrowPath = FindDescendant<Shapes.Path>(_toggleButton);
        if (_arrowPath != null)
        {
            _arrowRotate = new RotateTransform();
            _arrowPath.RenderTransform = _arrowRotate;
        }

        // Update selection box
        UpdateSelectionBoxItem();
    }

    private void OnToggleButtonChecked(object? sender, RoutedEventArgs e)
    {
        IsDropDownOpen = true;
    }

    private void OnToggleButtonUnchecked(object? sender, RoutedEventArgs e)
    {
        IsDropDownOpen = false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        // If CloseDropDown's animation is already handling the close, skip
        if (_isCloseAnimating) return;

        // Popup was closed externally (light dismiss / StaysOpen=false)
        // The popup is already gone, so just animate the arrow back
        _animationTimer?.Stop();
        _isDropDownOpen = false;

        if (_toggleButton != null)
            _toggleButton.IsChecked = false;

        // Animate arrow back to 0° (popup child is already hidden)
        if (_arrowRotate != null && Math.Abs(_arrowRotate.Angle) > 0.5)
        {
            var arrowStartAngle = _arrowRotate.Angle;
            var startTime = Environment.TickCount64;

            _animationTimer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
            _animationTimer.Tick += (s, ev) =>
            {
                var elapsed = Environment.TickCount64 - startTime;
                var progress = Math.Min(1.0, elapsed / CloseDurationMs);
                var eased = CloseEase.Ease(progress);

                _arrowRotate.Angle = arrowStartAngle * (1.0 - eased);
                _arrowPath!.InvalidateVisual();

                if (progress >= 1.0)
                {
                    _animationTimer!.Stop();
                    _arrowRotate.Angle = 0;
                    _arrowPath!.InvalidateVisual();
                }
            };
            _animationTimer.Start();
        }

        SetValue(IsDropDownOpenProperty, false);
        DropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        // Toggle dropdown
        IsDropDownOpen = !IsDropDownOpen;
        e.Handled = true;
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;
        if (e is not KeyEventArgs keyArgs) return;

        switch (keyArgs.Key)
        {
            case Key.Down:
                if (SelectedIndex < Items.Count - 1)
                    SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (SelectedIndex > 0)
                    SelectedIndex--;
                e.Handled = true;
                break;

            case Key.Enter:
                if (IsDropDownOpen)
                {
                    CloseDropDown();
                }
                else
                {
                    OpenDropDown();
                }
                e.Handled = true;
                break;

            case Key.Escape:
                if (IsDropDownOpen)
                {
                    CloseDropDown();
                    e.Handled = true;
                }
                break;

            case Key.Space:
                if (IsDropDownOpen)
                    CloseDropDown();
                else
                    OpenDropDown();
                e.Handled = true;
                break;

            case Key.Home:
                if (Items.Count > 0)
                    SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                if (Items.Count > 0)
                    SelectedIndex = Items.Count - 1;
                e.Handled = true;
                break;

            case Key.F4:
                // F4 toggles dropdown (standard Windows behavior)
                if (IsDropDownOpen)
                    CloseDropDown();
                else
                    OpenDropDown();
                e.Handled = true;
                break;
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            var isOpen = (bool)e.NewValue;
            if (isOpen)
            {
                comboBox.OpenDropDown();
            }
            else
            {
                comboBox.CloseDropDown();
            }
        }
    }

    /// <inheritdoc />
    protected override void UpdateContainerSelection()
    {
        UpdateSelectionBoxItem();
    }

    private void UpdateSelectionBoxItem()
    {
        var selectedItem = SelectedItem;
        if (selectedItem != null)
        {
            SelectionBoxItem = GetItemText(selectedItem);
        }
        else
        {
            SelectionBoxItem = PlaceholderText;
        }
    }

    private void OpenDropDown()
    {
        if (_isDropDownOpen) return;

        // Cancel any close animation in progress
        if (_isCloseAnimating)
        {
            _animationTimer?.Stop();
            _isCloseAnimating = false;
        }

        PopulateDropdownItems();

        if (_popup != null)
        {
            UpdatePopupPlacementAndWidth();
            _popup.IsOpen = true;
        }

        _isDropDownOpen = true;

        if (_toggleButton != null)
        {
            _toggleButton.IsChecked = true;
        }

        // Animate: arrow 0→180°, popup child fade+slide in
        AnimateOpen();

        DropDownOpened?.Invoke(this, EventArgs.Empty);
    }

    private void CloseDropDown()
    {
        if (!_isDropDownOpen) return;

        _isDropDownOpen = false;

        if (_toggleButton != null)
        {
            _toggleButton.IsChecked = false;
        }

        // Animate: arrow 180→0°, popup child fade+slide out, then close popup
        AnimateClose();

        DropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnComboBoxSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdatePopupPlacementAndWidth();
    }

    private void UpdatePopupPlacementAndWidth()
    {
        if (_popup == null) return;

        _popup.PlacementTarget = this;

        var popupWidth = ActualWidth;
        if (popupWidth <= 0 || double.IsNaN(popupWidth) || double.IsInfinity(popupWidth))
        {
            if (!double.IsNaN(Width) && !double.IsInfinity(Width) && Width > 0)
                popupWidth = Width;
        }

        if (popupWidth > 0 && !double.IsNaN(popupWidth) && !double.IsInfinity(popupWidth))
        {
            _popup.Width = popupWidth;
            _popup.MinWidth = popupWidth;
            _popup.MaxWidth = popupWidth;
        }
    }

    #region Animation

    private void AnimateOpen()
    {
        _animationTimer?.Stop();

        var popupChild = _popup?.Child as FrameworkElement;

        // Initial state: transparent + shifted up
        if (popupChild != null)
        {
            popupChild.Opacity = 0;
            popupChild.RenderOffset = new Point(0, -8);
        }

        var arrowStartAngle = _arrowRotate?.Angle ?? 0;
        var startTime = Environment.TickCount64;

        _animationTimer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        _animationTimer.Tick += (s, e) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = Math.Min(1.0, elapsed / OpenDurationMs);
            var eased = OpenEase.Ease(progress);

            // Arrow: rotate to 180°
            if (_arrowRotate != null)
            {
                _arrowRotate.Angle = arrowStartAngle + (180.0 - arrowStartAngle) * eased;
                _arrowPath!.InvalidateVisual();
            }

            // Popup child: fade in + slide down
            if (popupChild != null)
            {
                popupChild.Opacity = eased;
                popupChild.RenderOffset = new Point(0, -8 * (1.0 - eased));
            }

            if (progress >= 1.0)
            {
                _animationTimer!.Stop();
                if (popupChild != null)
                {
                    popupChild.Opacity = 1;
                    popupChild.RenderOffset = default;
                }
                if (_arrowRotate != null)
                {
                    _arrowRotate.Angle = 180;
                    _arrowPath!.InvalidateVisual();
                }
            }
        };
        _animationTimer.Start();
    }

    private void AnimateClose()
    {
        _animationTimer?.Stop();

        var popupChild = _popup?.Child as FrameworkElement;
        var arrowStartAngle = _arrowRotate?.Angle ?? 180;
        var startOpacity = popupChild?.Opacity ?? 1.0;
        var startOffsetY = popupChild?.RenderOffset.Y ?? 0;
        var startTime = Environment.TickCount64;

        _isCloseAnimating = true;

        _animationTimer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        _animationTimer.Tick += (s, e) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = Math.Min(1.0, elapsed / CloseDurationMs);
            var eased = CloseEase.Ease(progress);

            // Arrow: rotate back to 0°
            if (_arrowRotate != null)
            {
                _arrowRotate.Angle = arrowStartAngle * (1.0 - eased);
                _arrowPath!.InvalidateVisual();
            }

            // Popup child: fade out + slide up
            if (popupChild != null)
            {
                popupChild.Opacity = startOpacity * (1.0 - eased);
                popupChild.RenderOffset = new Point(0, startOffsetY + (-8 - startOffsetY) * eased);
            }

            if (progress >= 1.0)
            {
                _animationTimer!.Stop();

                // Actually close the popup after animation completes
                // Keep _isCloseAnimating=true until AFTER popup closes to prevent OnPopupClosed re-entry
                if (_popup != null)
                {
                    _popup.IsOpen = false;
                }
                _isCloseAnimating = false;

                // Reset state
                if (popupChild != null)
                {
                    popupChild.Opacity = 1;
                    popupChild.RenderOffset = default;
                }
                if (_arrowRotate != null)
                {
                    _arrowRotate.Angle = 0;
                    _arrowPath!.InvalidateVisual();
                }
            }
        };
        _animationTimer.Start();
    }

    private static T? FindDescendant<T>(Visual? root) where T : Visual
    {
        if (root == null) return null;
        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    #endregion

    private void PopulateDropdownItems()
    {
        // Find the items panel in the popup
        if (_popup?.Child is Border border)
        {
            // Look for ScrollViewer containing ItemsPresenter or StackPanel
            if (border.Child is ScrollViewer scrollViewer)
            {
                if (scrollViewer.Content is StackPanel panel)
                {
                    _itemsPanel = panel;
                }
            }
        }

        if (_itemsPanel == null)
        {
            // Create items panel if not found in template
            _itemsPanel = new StackPanel();

            if (_popup?.Child is Border b && b.Child is ScrollViewer sv)
            {
                sv.Content = _itemsPanel;
            }
        }

        _itemsPanel.Children.Clear();

        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var index = i;

            var itemContainer = new ComboBoxItem
            {
                Content = GetItemText(item),
                Tag = item,
                IsSelected = item == SelectedItem
            };

            itemContainer.ItemClicked += (s, e) =>
            {
                SelectedIndex = index;
                SelectedItem = item;
                CloseDropDown();
            };

            _itemsPanel.Children.Add(itemContainer);
        }
    }

    private string GetItemText(object? item)
    {
        if (item == null) return string.Empty;

        // If item is a ComboBoxItem, get its content
        if (item is ComboBoxItem cbi)
        {
            return cbi.Content?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }
}

/// <summary>
/// Represents an item in a ComboBox.
/// </summary>
public sealed class ComboBoxItem : ContentControl
{
    private bool _isPressed;

    /// <summary>
    /// Gets or sets whether this item is selected.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Occurs when the item is clicked.
    /// </summary>
    public event EventHandler? ItemClicked;

    /// <summary>
    /// Directly invokes the click action - used by Popup for reliable click handling.
    /// </summary>
    internal void InvokeClick()
    {
        if (IsEnabled)
        {
            ItemClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    public ComboBoxItem()
    {
        // Use ControlTemplate-based rendering (defined in SelectionControls.jalxaml)
        UseTemplateContentManagement();

        // Set up mouse event handlers for click behavior
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            _isPressed = true;
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            if (_isPressed)
            {
                _isPressed = false;
                ItemClicked?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isPressed)
        {
            _isPressed = false;
        }
    }
}
