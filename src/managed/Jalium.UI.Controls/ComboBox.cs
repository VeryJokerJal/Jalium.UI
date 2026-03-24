using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a selection control with a drop-down list that can be shown or hidden by clicking the control.
/// </summary>
public class ComboBox : Selector
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ComboBoxAutomationPeer(this);
    }

    private Popup? _popup;
    private ToggleButton? _toggleButton;
    private TextBox? _editableTextBox;
    private ContentPresenter? _selectionPresenter;
    private Grid? _dropDownArea;
    private StackPanel? _itemsPanel;
    private bool _isDropDownOpen;
    private bool _isUpdatingEditableText;

    private Shapes.Path? _arrowPath;
    private RotateTransform? _arrowRotate;
    private bool _isCloseAnimating;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(ComboBox),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    /// <summary>
    /// Identifies the MaxDropDownHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(nameof(MaxDropDownHeight), typeof(double), typeof(ComboBox),
            new PropertyMetadata(200.0));

    /// <summary>
    /// Identifies the IsEditable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(ComboBox),
            new PropertyMetadata(false, OnIsEditableChanged));

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ComboBox),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the Placeholder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(ComboBox),
            new PropertyMetadata("Select an item...", OnPlaceholderTextChanged));

    /// <summary>
    /// Identifies the SelectionBoxItem dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
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

    private static void OnIsEditableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            comboBox.UpdateEditableModeVisualState();
            comboBox.UpdateCursorState();
            comboBox.UpdateSelectionBoxItem();
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            comboBox.OnTextChanged();
        }
    }

    private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            comboBox.UpdatePlaceholderState();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the dropdown is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the dropdown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets whether users can type text directly into the control.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>
    /// Gets or sets the current text in the combo box.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when no item is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string PlaceholderText
    {
        get => (string)(GetValue(PlaceholderTextProperty) ?? "Select an item...");
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets the item displayed in the selection box.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
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
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        SizeChanged += OnComboBoxSizeChanged;

        // Initialize SelectionBoxItem with placeholder
        SelectionBoxItem = PlaceholderText;

        // Set up mouse handling for toggle
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocusHandler));
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

        if (_editableTextBox != null)
        {
            _editableTextBox.TextChanged -= OnEditableTextBoxTextChanged;
        }

        // Get template parts
        _toggleButton = GetTemplateChild("PART_ToggleButton") as ToggleButton;
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _editableTextBox = GetTemplateChild("PART_EditableTextBox") as TextBox;
        _selectionPresenter = GetTemplateChild("PART_SelectionPresenter") as ContentPresenter;
        _dropDownArea = GetTemplateChild("PART_DropDownArea") as Grid;

        // Wire up toggle button
        if (_toggleButton != null)
        {
            _toggleButton.Checked += OnToggleButtonChecked;
            _toggleButton.Unchecked += OnToggleButtonUnchecked;
            _toggleButton.Cursor = Jalium.UI.Cursors.Arrow;
        }

        // Wire up popup
        if (_popup != null)
        {
            _popup.Closed += OnPopupClosed;
        }

        if (_editableTextBox != null)
        {
            _editableTextBox.TextChanged += OnEditableTextBoxTextChanged;
        }

        UpdatePopupPlacementAndWidth();

        // Find the arrow Path inside ToggleButton's template for rotation animation
        _arrowPath = FindDescendant<Shapes.Path>(_toggleButton);
        if (_arrowPath != null)
        {
            _arrowRotate = new RotateTransform();
            _arrowPath.RenderTransformOrigin = new Point(0.5, 0.5);
            _arrowPath.RenderTransform = _arrowRotate;
        }

        // Update selection box
        UpdatePlaceholderState();
        UpdateEditableModeVisualState();
        UpdateCursorState();
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
        if (_isCloseAnimating) return;

        _isDropDownOpen = false;

        if (_toggleButton != null)
            _toggleButton.IsChecked = false;

        if (_arrowRotate != null)
        {
            _arrowRotate.Angle = 0;
            _arrowPath?.InvalidateVisual();
        }

        SetValue(IsDropDownOpenProperty, false);
        DropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnGotKeyboardFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsEditable || _editableTextBox == null || !ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }

        if (_editableTextBox.Focus())
        {
            _editableTextBox.CaretIndex = _editableTextBox.Text?.Length ?? 0;
            e.Handled = true;
        }
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (IsEditable)
        {
            if (IsEventFromToggleButton(e) || IsMousePositionInToggleButton(e))
            {
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
                return;
            }

            if (IsEventFromEditableTextBox(e))
            {
                // Let the inner TextBox handle focus/caret behavior.
                return;
            }

            if (TryForwardMouseButtonEventToEditableTextBox(e))
            {
                e.Handled = true;
                return;
            }
        }

        // Toggle dropdown
        IsDropDownOpen = !IsDropDownOpen;
        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!IsEditable)
        {
            return;
        }

        UpdateCursorState(e.GetPosition(this));
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        if (IsEditable && IsEventFromEditableTextBox(e))
        {
            // Preserve TextBox editing behavior in editable mode.
            if (e.Key == Key.F4)
            {
                if (IsDropDownOpen)
                    CloseDropDown();
                else
                    OpenDropDown();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
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

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);

        if (!newValue && IsDropDownOpen)
        {
            IsDropDownOpen = false;
        }
    }

    /// <inheritdoc />
    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);

        if (isFocused && IsEditable && _editableTextBox != null && !_editableTextBox.IsKeyboardFocused)
        {
            _editableTextBox.Focus();
        }
    }

    private void UpdateSelectionBoxItem()
    {
        var selectedItem = SelectedItem;
        if (selectedItem != null)
        {
            var selectedText = GetItemText(selectedItem);
            SelectionBoxItem = selectedText;

            if (IsEditable && !string.Equals(Text, selectedText, StringComparison.Ordinal))
            {
                Text = selectedText;
            }
        }
        else
        {
            if (IsEditable)
            {
                SelectionBoxItem = string.IsNullOrEmpty(Text) ? PlaceholderText : Text;
            }
            else
            {
                SelectionBoxItem = PlaceholderText;
            }
        }

        UpdateEditableTextBoxText();
    }

    private void OnTextChanged()
    {
        if (IsEditable)
        {
            SelectionBoxItem = string.IsNullOrEmpty(Text) ? PlaceholderText : Text;
            UpdateEditableTextBoxText();
            SyncSelectionWithText(Text);
        }
    }

    private void UpdatePlaceholderState()
    {
        if (_editableTextBox != null)
        {
            _editableTextBox.PlaceholderText = PlaceholderText;
        }

        if (SelectedItem == null && (!IsEditable || string.IsNullOrEmpty(Text)))
        {
            SelectionBoxItem = PlaceholderText;
        }
    }

    private void UpdateEditableModeVisualState()
    {
        if (_selectionPresenter != null)
        {
            _selectionPresenter.Visibility = IsEditable ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_editableTextBox != null)
        {
            _editableTextBox.Visibility = IsEditable ? Visibility.Visible : Visibility.Collapsed;
            _editableTextBox.PlaceholderText = PlaceholderText;
        }

        UpdateEditableTextBoxText();
    }

    private void UpdateCursorState()
    {
        UpdateCursorState(null);
    }

    private void UpdateCursorState(Point? pointerPosition)
    {
        Cursor = !IsEditable || (pointerPosition.HasValue && IsPointInToggleArea(pointerPosition.Value))
            ? Jalium.UI.Cursors.Arrow
            : Jalium.UI.Cursors.IBeam;

        if (_dropDownArea != null)
        {
            _dropDownArea.Cursor = Jalium.UI.Cursors.Arrow;
        }

        if (_toggleButton != null)
        {
            _toggleButton.Cursor = Jalium.UI.Cursors.Arrow;
        }
    }

    private void UpdateEditableTextBoxText()
    {
        if (!IsEditable || _editableTextBox == null) return;

        if (!string.Equals(_editableTextBox.Text, Text, StringComparison.Ordinal))
        {
            _isUpdatingEditableText = true;
            try
            {
                _editableTextBox.Text = Text;
            }
            finally
            {
                _isUpdatingEditableText = false;
            }
        }
    }

    private void OnEditableTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsEditable || _isUpdatingEditableText || _editableTextBox == null) return;

        var editableText = _editableTextBox.Text ?? string.Empty;
        if (!string.Equals(Text, editableText, StringComparison.Ordinal))
        {
            Text = editableText;
        }
    }

    private void SyncSelectionWithText(string text)
    {
        if (!IsEditable) return;

        if (string.IsNullOrEmpty(text))
        {
            if (SelectedIndex != -1)
            {
                SelectedIndex = -1;
            }
            return;
        }

        var matchedIndex = -1;
        var itemCount = GetItemCount();
        for (int i = 0; i < itemCount; i++)
        {
            if (string.Equals(GetItemText(GetItemAt(i)), text, StringComparison.OrdinalIgnoreCase))
            {
                matchedIndex = i;
                break;
            }
        }

        if (matchedIndex >= 0)
        {
            if (SelectedIndex != matchedIndex)
            {
                SelectedIndex = matchedIndex;
            }
        }
        else if (SelectedIndex != -1)
        {
            SelectedIndex = -1;
        }
    }

    private bool IsEventFromEditableTextBox(RoutedEventArgs e)
    {
        return IsEventFromVisualTree(e, _editableTextBox);
    }

    private bool IsEventFromToggleButton(RoutedEventArgs e)
    {
        return IsEventFromVisualTree(e, _toggleButton);
    }

    private bool IsMousePositionInToggleButton(MouseButtonEventArgs e)
    {
        return IsPointInToggleArea(e.GetPosition(this));
    }

    private bool IsPointInToggleArea(Point position)
    {
        if (position.X < 0 || position.Y < 0 || position.X > RenderSize.Width || position.Y > RenderSize.Height)
        {
            return false;
        }

        double toggleWidth = 0;
        if (_toggleButton != null)
        {
            toggleWidth = Math.Max(_toggleButton.ActualWidth, _toggleButton.RenderSize.Width);
            if (toggleWidth <= 0 && _toggleButton.VisualBounds.Width > 0)
            {
                toggleWidth = _toggleButton.VisualBounds.Width;
            }
        }

        if (toggleWidth <= 0)
        {
            toggleWidth = 28;
        }

        return position.X >= Math.Max(0, RenderSize.Width - toggleWidth);
    }

    private bool TryForwardMouseButtonEventToEditableTextBox(MouseButtonEventArgs e)
    {
        if (_editableTextBox == null || e.RoutedEvent == null)
        {
            return false;
        }

        var forwardedArgs = new MouseButtonEventArgs(
            e.RoutedEvent,
            e.Position,
            e.ChangedButton,
            e.ButtonState,
            e.ClickCount,
            e.LeftButton,
            e.MiddleButton,
            e.RightButton,
            e.XButton1,
            e.XButton2,
            e.KeyboardModifiers,
            e.Timestamp);

        _editableTextBox.RaiseEvent(forwardedArgs);
        return forwardedArgs.Handled;
    }

    private static bool IsEventFromVisualTree(RoutedEventArgs e, Visual? targetVisual)
    {
        if (targetVisual == null || e.OriginalSource is not Visual sourceVisual)
        {
            return false;
        }

        for (Visual? current = sourceVisual; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, targetVisual))
            {
                return true;
            }
        }

        return false;
    }

    private void OpenDropDown()
    {
        if (_isDropDownOpen) return;

        _isCloseAnimating = false;

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
        var popupChild = _popup?.Child as FrameworkElement;
        if (popupChild != null)
        {
            popupChild.Opacity = 1;
            popupChild.RenderOffset = default;
        }

        if (_arrowRotate != null)
        {
            _arrowRotate.Angle = 180;
            _arrowPath?.InvalidateVisual();
        }
    }

    private void AnimateClose()
    {
        var popupChild = _popup?.Child as FrameworkElement;
        if (popupChild != null)
        {
            popupChild.Opacity = 1;
            popupChild.RenderOffset = default;
        }

        if (_arrowRotate != null)
        {
            _arrowRotate.Angle = 0;
            _arrowPath?.InvalidateVisual();
        }

        _isCloseAnimating = true;
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }
        _isCloseAnimating = false;
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
public class ComboBoxItem : ContentControl
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
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            _isPressed = true;
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
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
