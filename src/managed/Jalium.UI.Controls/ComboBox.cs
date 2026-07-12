using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a selection control with a drop-down list that can be shown or hidden by clicking the control.
/// </summary>
public class ComboBox : Selector
{
    private const double DefaultToggleButtonWidth = 28;
    private const double PopupOpenDurationMs = 250;
    private const double PopupCloseDurationMs = 150;
    private const double PopupSlideOffsetY = -6;
    private const double ArrowFlipDurationMs = 220;
    private static readonly BackEase s_popupOpenEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.85 };
    private static readonly CubicEase s_popupCloseEase = new() { EasingMode = EasingMode.EaseInOut };
    private static readonly CubicEase s_arrowFlipEase = new() { EasingMode = EasingMode.EaseInOut };

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ComboBoxAutomationPeer(this);
    }

    private Popup? _popup;
    private ToggleButton? _toggleButton;
    private TextBox? _editableTextBox;
    private ContentPresenter? _selectionPresenter;
    private Grid? _dropDownArea;
    private bool _isDropDownOpen;
    private bool _isUpdatingEditableText;
    private bool _isApplyingTextCompletion;
    private string _textSearchPrefix = string.Empty;
    private long _lastTextSearchTick;

    private Shapes.Path? _arrowPath;
    private string? _arrowDownData;
    private bool _isCloseAnimating;
    private Threading.DispatcherTimer? _popupAnimTimer;

    // 平滑翻转动画：把 chevron【逐帧旋转烘进几何坐标】（送 native 始终是正常正定路径），
    // 而不是用 180° RenderTransform —— 后者产生 (-1,0,0,-1) 负对角矩阵，触发 native FillPath
    // 的光栅化 bug（路径被整体平移一个 Offset、箭头飞到下方/消失，Vello/Impeller 两引擎共有，
    // 已用渲染层埋点实证）。_chevronBase 为预拉伸到箭头方框、居中后的基准几何（Stretch=None），
    // 绕其中心旋转尺寸恒定、无抖动。
    private Threading.DispatcherTimer? _arrowAnimTimer;
    private PathGeometry? _chevronBase;
    private double _chevronCenterX;
    private double _chevronCenterY;
    private double _arrowAngle;

    // 朝下 chevron 兜底数据（与模板一致）；朝上=朝下绕原点 180°（取负，保 winding/sweep）。
    // 仅在无法构建 _chevronBase 时作"瞬时切换"降级用。
    private const string ArrowDownData =
        "M 733.87,841.90 L 1160.54,273.07 A 170.67,170.67,0,0,0,1024.00,0 H 170.67 A 170.67,170.67,0,0,0,34.14,273.07 L 460.80,841.90 A 170.67,170.67,0,0,0,733.87,841.90 Z";
    private const string ArrowUpData =
        "M -733.87,-841.90 L -1160.54,-273.07 A 170.67,170.67,0,0,0,-1024.00,0 H -170.67 A 170.67,170.67,0,0,0,-34.14,-273.07 L -460.80,-841.90 A 170.67,170.67,0,0,0,-733.87,-841.90 Z";

    #region Dependency Properties

    static ComboBox()
    {
        // WPF enables incremental text search for ComboBox by default, while
        // ItemsControl leaves it disabled for the general case.
        ItemsControl.IsTextSearchEnabledProperty.OverrideMetadata(
            typeof(ComboBox),
            new FrameworkPropertyMetadata(true));
    }

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
    /// Identifies the <see cref="IsReadOnly"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        TextBoxBase.IsReadOnlyProperty.AddOwner(
            typeof(ComboBox),
            new PropertyMetadata(false, OnIsReadOnlyChanged));

    /// <summary>
    /// Identifies the <see cref="ShouldPreserveUserEnteredPrefix"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShouldPreserveUserEnteredPrefixProperty =
        DependencyProperty.Register(
            nameof(ShouldPreserveUserEnteredPrefix),
            typeof(bool),
            typeof(ComboBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="StaysOpenOnEdit"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty StaysOpenOnEditProperty =
        DependencyProperty.Register(nameof(StaysOpenOnEdit), typeof(bool), typeof(ComboBox),
            new PropertyMetadata(false));

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
    private static readonly DependencyPropertyKey SelectionBoxItemPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SelectionBoxItem),
            typeof(object),
            typeof(ComboBox),
            new PropertyMetadata(string.Empty, OnSelectionBoxItemChanged));

    /// <summary>
    /// Identifies the read-only <see cref="SelectionBoxItem"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionBoxItemProperty =
        SelectionBoxItemPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectionBoxItemTemplatePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SelectionBoxItemTemplate),
            typeof(DataTemplate),
            typeof(ComboBox),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the read-only <see cref="SelectionBoxItemTemplate"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionBoxItemTemplateProperty =
        SelectionBoxItemTemplatePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectionBoxItemStringFormatPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SelectionBoxItemStringFormat),
            typeof(string),
            typeof(ComboBox),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the read-only <see cref="SelectionBoxItemStringFormat"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionBoxItemStringFormatProperty =
        SelectionBoxItemStringFormatPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsSelectionBoxHighlightedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsSelectionBoxHighlighted),
            typeof(bool),
            typeof(ComboBox),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsSelectionBoxHighlightedProperty =
        IsSelectionBoxHighlightedPropertyKey.DependencyProperty;

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

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            comboBox.UpdateReadOnlyState();
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
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the dropdown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty)!;
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets whether users can type text directly into the control.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty)!;
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the editable text area accepts user edits.
    /// Selection from the drop-down remains available in read-only mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether text completion retains the exact prefix entered by the user.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool ShouldPreserveUserEnteredPrefix
    {
        get => (bool)GetValue(ShouldPreserveUserEnteredPrefixProperty)!;
        set => SetValue(ShouldPreserveUserEnteredPrefixProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the drop-down stays open while the user edits the
    /// text of an editable combo box. When <see langword="false"/> (the
    /// default), clicking into the editable text box closes the drop-down.
    /// Has no effect unless <see cref="IsEditable"/> is <see langword="true"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool StaysOpenOnEdit
    {
        get => (bool)GetValue(StaysOpenOnEditProperty)!;
        set => SetValue(StaysOpenOnEditProperty, value);
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
        private set => SetValue(SelectionBoxItemPropertyKey, value);
    }

    /// <summary>
    /// Gets the template used to display <see cref="SelectionBoxItem"/>.
    /// </summary>
    public DataTemplate? SelectionBoxItemTemplate
    {
        get => (DataTemplate?)GetValue(SelectionBoxItemTemplateProperty);
        private set => SetValue(SelectionBoxItemTemplatePropertyKey, value);
    }

    /// <summary>
    /// Gets the composite format string used by the selection box.
    /// </summary>
    public string? SelectionBoxItemStringFormat
    {
        get => (string?)GetValue(SelectionBoxItemStringFormatProperty);
        private set => SetValue(SelectionBoxItemStringFormatPropertyKey, value);
    }

    /// <summary>
    /// Gets whether the content displayed in the closed selection box is highlighted.
    /// </summary>
    public bool IsSelectionBoxHighlighted =>
        (bool)GetValue(IsSelectionBoxHighlightedProperty)!;

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

    /// <summary>
    /// Reports that the drop-down has opened.
    /// </summary>
    protected virtual void OnDropDownOpened(EventArgs e)
    {
        DropDownOpened?.Invoke(this, e);
    }

    /// <summary>
    /// Reports that the drop-down has closed.
    /// </summary>
    protected virtual void OnDropDownClosed(EventArgs e)
    {
        DropDownClosed?.Invoke(this, e);
    }

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
        AddHandler(TextInputEvent, new TextCompositionEventHandler(OnTextInputHandler));
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocusHandler));
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
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
            _toggleButton.Cursor = Jalium.UI.Input.Cursors.Arrow;
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

        // Find the arrow Path inside ToggleButton's template for the open/close indicator.
        _arrowPath = FindDescendant<Shapes.Path>(_toggleButton);
        if (_arrowPath != null)
        {
            string? arrowData = _arrowPath.Data?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _arrowDownData = string.IsNullOrWhiteSpace(arrowData) ? ArrowDownData : arrowData;
            BuildChevronBase();
            _arrowAngle = _isDropDownOpen ? 180.0 : 0.0;
            ApplyArrowAngle(_arrowAngle);
        }

        // Update selection box
        UpdatePlaceholderState();
        UpdateEditableModeVisualState();
        UpdateReadOnlyState();
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

        SetArrowDirection(false);

        SetValue(IsDropDownOpenProperty, false);
        OnDropDownClosed(EventArgs.Empty);
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
                // Clicking into the editable text box dismisses the drop-down
                // unless StaysOpenOnEdit keeps it open while the user edits.
                if (IsDropDownOpen && !StaysOpenOnEdit)
                {
                    IsDropDownOpen = false;
                }

                // Let the inner TextBox handle focus/caret behavior.
                return;
            }

            if (TryForwardMouseButtonEventToEditableTextBox(e))
            {
                if (IsDropDownOpen && !StaysOpenOnEdit)
                {
                    IsDropDownOpen = false;
                }

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
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                if (SelectedIndex < GetItemCount() - 1)
                    SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (SelectedIndex > 0)
                    SelectedIndex--;
                e.Handled = true;
                break;

            case Key.Enter:
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
                break;

            case Key.Escape:
                if (IsDropDownOpen)
                {
                    IsDropDownOpen = false;
                    e.Handled = true;
                }
                break;

            case Key.Space:
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
                break;

            case Key.Back:
                if (!IsEditable && IsTextSearchEnabled && DeleteTextSearchCharacter())
                {
                    e.Handled = true;
                }
                break;

            case Key.Home:
                if (GetItemCount() > 0)
                    SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
            {
                var count = GetItemCount();
                if (count > 0)
                    SelectedIndex = count - 1;
                e.Handled = true;
                break;
            }

            case Key.F4:
                // F4 toggles dropdown (standard Windows behavior)
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
                break;
        }
    }

    private void OnTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (!IsEnabled || IsEditable || !IsTextSearchEnabled || string.IsNullOrEmpty(e.Text))
        {
            if (!IsTextSearchEnabled)
            {
                ResetTextSearch();
            }

            return;
        }

        if (PerformTextSearch(e.Text))
        {
            e.Handled = true;
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox comboBox)
        {
            var isOpen = (bool)e.NewValue!;
            if (isOpen)
            {
                comboBox.OpenDropDown();
            }
            else
            {
                comboBox.CloseDropDown();
            }

            comboBox.UpdateSelectionBoxHighlighted();
        }
    }

    /// <inheritdoc />
    protected override void UpdateContainerSelection()
    {
        UpdateSelectionBoxItem();
        SyncItemContainerSelection();
    }

    private void SyncItemContainerSelection()
    {
        var host = ItemsHost;
        if (host == null) return;

        var selectedItem = SelectedItem;
        foreach (UIElement child in host.Children)
        {
            if (child is ComboBoxItem container)
            {
                int index = ItemContainerGenerator.IndexFromContainer(container);
                var logicalItem = index >= 0 && index < GetItemCount()
                    ? GetItemAt(index)
                    : container.Content;
                var shouldBeSelected = Equals(logicalItem, selectedItem);
                if (container.IsSelected != shouldBeSelected)
                {
                    container.IsSelected = shouldBeSelected;
                }
            }
        }
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

    /// <inheritdoc />
    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        UpdateSelectionBoxHighlighted();
    }

    private void UpdateSelectionBoxItem()
    {
        var selectedItem = SelectedItem;
        if (selectedItem != null)
        {
            var selectedText = GetItemText(selectedItem);
            object? displayItem = selectedItem;
            DataTemplate? displayTemplate = ItemTemplate;
            string? displayStringFormat = ItemStringFormat;

            // WPF displays the content of an item container, rather than the
            // ComboBoxItem wrapper itself, and honors the container's own
            // presentation contract for own-container items.
            if (selectedItem is ContentControl contentControl)
            {
                displayItem = contentControl.Content;
                displayTemplate = contentControl.ContentTemplate;
                displayStringFormat = contentControl.ContentStringFormat;
            }

            // A UIElement cannot be parented simultaneously by the drop-down
            // container and the closed selection presenter. WPF paints a visual
            // clone in this case; a VisualBrush-backed rectangle provides the
            // same single-parent behavior here.
            if (displayTemplate == null && ItemTemplateSelector == null &&
                displayStringFormat == null && displayItem is UIElement visualItem)
            {
                displayItem = CreateSelectionVisualClone(visualItem);
            }

            SelectionBoxItem = displayItem ?? string.Empty;
            SelectionBoxItemTemplate = displayTemplate;
            SelectionBoxItemStringFormat = displayStringFormat;

            if (!_isApplyingTextCompletion &&
                !string.Equals(Text, selectedText, StringComparison.Ordinal))
            {
                SetCurrentValue(TextProperty, selectedText);
            }
        }
        else
        {
            SelectionBoxItemTemplate = null;
            SelectionBoxItemStringFormat = null;

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
        }

        SyncSelectionWithText(Text);
    }

    private void UpdatePlaceholderState()
    {
        if (_editableTextBox != null)
        {
            _editableTextBox.PlaceholderText = PlaceholderText;
        }

        if (SelectedItem == null && (!IsEditable || string.IsNullOrEmpty(Text)))
        {
            SelectionBoxItemTemplate = null;
            SelectionBoxItemStringFormat = null;
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

        UpdateReadOnlyState();
        UpdateEditableTextBoxText();
    }

    private void UpdateReadOnlyState()
    {
        if (_editableTextBox != null)
        {
            _editableTextBox.IsReadOnly = IsReadOnly;
        }
    }

    private void UpdateCursorState()
    {
        UpdateCursorState(null);
    }

    private void UpdateCursorState(Point? pointerPosition)
    {
        Cursor = !IsEditable || (pointerPosition.HasValue && IsPointInToggleArea(pointerPosition.Value))
            ? Jalium.UI.Input.Cursors.Arrow
            : Jalium.UI.Input.Cursors.IBeam;

        if (_dropDownArea != null)
        {
            _dropDownArea.Cursor = Jalium.UI.Input.Cursors.Arrow;
        }

        if (_toggleButton != null)
        {
            _toggleButton.Cursor = Jalium.UI.Input.Cursors.Arrow;
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
        if (!IsEditable || IsReadOnly || _isUpdatingEditableText || _editableTextBox == null) return;

        var editableText = _editableTextBox.Text ?? string.Empty;
        if (TryCompleteEditableText(editableText, e))
        {
            return;
        }

        if (!string.Equals(Text, editableText, StringComparison.Ordinal))
        {
            SetCurrentValue(TextProperty, editableText);
        }
    }

    private void SyncSelectionWithText(string text)
    {
        if (!IsTextSearchEnabled) return;

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
            if (string.Equals(GetItemText(GetItemAt(i)), text, StringComparison.CurrentCulture))
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

    private bool TryCompleteEditableText(string enteredText, TextChangedEventArgs e)
    {
        bool changeEndsAtTextEnd = e.Changes.Any(change =>
            change.AddedLength > 0 && change.Offset + change.AddedLength == enteredText.Length);

        if (_editableTextBox == null || !IsTextSearchEnabled || string.IsNullOrEmpty(enteredText) ||
            !changeEndsAtTextEnd)
        {
            return false;
        }

        int matchedIndex = FindMatchingIndex(enteredText, 0);
        if (matchedIndex < 0)
        {
            return false;
        }

        string matchedText = GetItemText(GetItemAt(matchedIndex));
        string completedText = ShouldPreserveUserEnteredPrefix
            ? string.Concat(enteredText, matchedText.AsSpan(Math.Min(enteredText.Length, matchedText.Length)))
            : matchedText;

        _isApplyingTextCompletion = true;
        _isUpdatingEditableText = true;
        try
        {
            if (!string.Equals(Text, completedText, StringComparison.Ordinal))
            {
                SetCurrentValue(TextProperty, completedText);
            }

            if (SelectedIndex != matchedIndex)
            {
                SelectedIndex = matchedIndex;
            }

            if (!string.Equals(_editableTextBox.Text, completedText, StringComparison.Ordinal))
            {
                _editableTextBox.Text = completedText;
            }

            int prefixLength = Math.Min(enteredText.Length, completedText.Length);
            _editableTextBox.Select(prefixLength, completedText.Length - prefixLength);
        }
        finally
        {
            _isUpdatingEditableText = false;
            _isApplyingTextCompletion = false;
        }

        return true;
    }

    private bool PerformTextSearch(string text)
    {
        long now = Environment.TickCount64;
        if (unchecked(now - _lastTextSearchTick) > 1000)
        {
            _textSearchPrefix = string.Empty;
        }

        _lastTextSearchTick = now;
        string candidate = _textSearchPrefix + text;
        int matchedIndex = FindMatchingIndex(candidate, 0);

        if (matchedIndex < 0 && _textSearchPrefix.Length > 0)
        {
            // Repeated character searches cycle through items with the same
            // prefix, matching the standard ComboBox keyboard interaction.
            candidate = text;
            matchedIndex = FindMatchingIndex(candidate, SelectedIndex + 1);
        }

        if (matchedIndex < 0)
        {
            return false;
        }

        _textSearchPrefix = candidate;
        SelectedIndex = matchedIndex;
        return true;
    }

    private bool DeleteTextSearchCharacter()
    {
        if (_textSearchPrefix.Length == 0 ||
            unchecked(Environment.TickCount64 - _lastTextSearchTick) > 1000)
        {
            ResetTextSearch();
            return false;
        }

        _textSearchPrefix = _textSearchPrefix[..^1];
        _lastTextSearchTick = Environment.TickCount64;
        if (_textSearchPrefix.Length == 0)
        {
            return true;
        }

        int matchedIndex = FindMatchingIndex(_textSearchPrefix, 0);
        if (matchedIndex >= 0)
        {
            SelectedIndex = matchedIndex;
        }

        return true;
    }

    private void ResetTextSearch()
    {
        _textSearchPrefix = string.Empty;
        _lastTextSearchTick = 0;
    }

    private int FindMatchingIndex(string prefix, int startIndex)
    {
        int count = GetItemCount();
        if (count == 0 || string.IsNullOrEmpty(prefix))
        {
            return -1;
        }

        int normalizedStart = ((startIndex % count) + count) % count;
        var comparison = IsTextSearchCaseSensitive
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;

        for (int offset = 0; offset < count; offset++)
        {
            int index = (normalizedStart + offset) % count;
            if (GetItemText(GetItemAt(index)).StartsWith(prefix, comparison))
            {
                return index;
            }
        }

        return -1;
    }

    private void UpdateSelectionBoxHighlighted()
    {
        SetValue(
            IsSelectionBoxHighlightedPropertyKey,
            !IsDropDownOpen && IsKeyboardFocusWithin);
    }

    private static UIElement CreateSelectionVisualClone(UIElement visual)
    {
        var size = visual.RenderSize;
        var brush = new VisualBrush(visual)
        {
            Stretch = Stretch.None,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(size),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(size),
        };

        return new Shapes.Rectangle
        {
            Fill = brush,
            Width = size.Width,
            Height = size.Height,
        };
    }

    /// <inheritdoc />
    protected override void OnDisplayMemberPathChanged(string oldDisplayMemberPath, string newDisplayMemberPath)
    {
        base.OnDisplayMemberPathChanged(oldDisplayMemberPath, newDisplayMemberPath);
        UpdateSelectionBoxItem();
    }

    /// <inheritdoc />
    protected override void OnItemStringFormatChanged(string? oldItemStringFormat, string? newItemStringFormat)
    {
        base.OnItemStringFormatChanged(oldItemStringFormat, newItemStringFormat);
        UpdateSelectionBoxItem();
    }

    /// <inheritdoc />
    protected override void OnItemTemplateChanged(DataTemplate? oldItemTemplate, DataTemplate? newItemTemplate)
    {
        base.OnItemTemplateChanged(oldItemTemplate, newItemTemplate);
        UpdateSelectionBoxItem();
    }

    /// <inheritdoc />
    protected override void OnItemTemplateSelectorChanged(
        DataTemplateSelector? oldItemTemplateSelector,
        DataTemplateSelector? newItemTemplateSelector)
    {
        base.OnItemTemplateSelectorChanged(oldItemTemplateSelector, newItemTemplateSelector);
        UpdateSelectionBoxItem();
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
            toggleWidth = DefaultToggleButtonWidth;
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

        OnDropDownOpened(EventArgs.Empty);
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

        OnDropDownClosed(EventArgs.Empty);
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
        _popupAnimTimer?.Stop();

        var popupChild = _popup?.Child as FrameworkElement;
        if (popupChild != null)
        {
            popupChild.Opacity = 0;
            popupChild.RenderOffset = new Point(0, PopupSlideOffsetY);

            var startTick = Environment.TickCount64;
            _popupAnimTimer = new Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
            };
            _popupAnimTimer.Tick += (_, _) =>
            {
                var elapsed = Environment.TickCount64 - startTick;
                var progress = Math.Clamp(elapsed / PopupOpenDurationMs, 0.0, 1.0);
                var eased = s_popupOpenEase.Ease(progress);

                popupChild.Opacity = eased;
                popupChild.RenderOffset = new Point(0, PopupSlideOffsetY * (1.0 - eased));

                if (progress >= 1.0)
                {
                    popupChild.Opacity = 1;
                    popupChild.RenderOffset = default;
                    _popupAnimTimer.Stop();
                    _popupAnimTimer = null;
                }
            };
            _popupAnimTimer.Start();
        }

        SetArrowDirection(true);
    }

    private void AnimateClose()
    {
        _popupAnimTimer?.Stop();

        var popupChild = _popup?.Child as FrameworkElement;
        if (popupChild != null)
        {
            var startOpacity = popupChild.Opacity;
            var startOffsetY = popupChild.RenderOffset.Y;
            var startTick = Environment.TickCount64;

            _popupAnimTimer = new Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
            };
            _popupAnimTimer.Tick += (_, _) =>
            {
                var elapsed = Environment.TickCount64 - startTick;
                var progress = Math.Clamp(elapsed / PopupCloseDurationMs, 0.0, 1.0);
                var eased = s_popupCloseEase.Ease(progress);

                popupChild.Opacity = startOpacity * (1.0 - eased);
                popupChild.RenderOffset = new Point(0, startOffsetY + (PopupSlideOffsetY - startOffsetY) * eased);

                if (progress >= 1.0)
                {
                    _popupAnimTimer.Stop();
                    _popupAnimTimer = null;

                    popupChild.Opacity = 1;
                    popupChild.RenderOffset = default;

                    _isCloseAnimating = true;
                    if (_popup != null)
                    {
                        _popup.IsOpen = false;
                    }
                    _isCloseAnimating = false;
                }
            };
            _popupAnimTimer.Start();
        }
        else
        {
            _isCloseAnimating = true;
            if (_popup != null)
            {
                _popup.IsOpen = false;
            }
            _isCloseAnimating = false;
        }

        SetArrowDirection(false);
    }

    /// <summary>
    /// 翻转下拉箭头指示方向（展开→朝上 180°，收起→朝下 0°），平滑过渡。
    /// 翻转通过【逐帧把 chevron 几何旋转到当前角度】实现，而不是 180° RenderTransform ——
    /// 后者向 native 提交 (-1,0,0,-1) 负对角矩阵，触发 native FillPath 的光栅化 bug
    /// （路径被整体平移一个 Offset，箭头飞到下方/消失；Vello/Impeller 两引擎共有，
    /// 已用渲染层埋点 PUSH#/GEOM# 实证：managed 侧矩阵全对、仅 native 应用出错）。
    /// 烘进几何的旋转点恒为正常正定路径，彻底绕开该 bug。
    /// </summary>
    private void SetArrowDirection(bool open)
    {
        AnimateArrowFlip(open ? 180.0 : 0.0);
    }

    // 把朝下 chevron 预拉伸到箭头方框、居中，作为旋转基准（Stretch 改 None，旋转时尺寸恒定不抖动）。
    private void BuildChevronBase()
    {
        _chevronBase = null;
        if (_arrowPath == null) return;

        var data = _arrowDownData ?? ArrowDownData;
        if (Geometry.Parse(data) is not PathGeometry parsed || parsed.Figures.Count == 0)
            return;

        var boxW = !double.IsNaN(_arrowPath.Width) && _arrowPath.Width > 0 ? _arrowPath.Width : 9.0;
        var boxH = !double.IsNaN(_arrowPath.Height) && _arrowPath.Height > 0 ? _arrowPath.Height : boxW;

        _chevronBase = StretchUniform(parsed, boxW, boxH);
        _chevronCenterX = boxW / 2.0;
        _chevronCenterY = boxH / 2.0;
        _arrowPath.Stretch = Stretch.None; // 几何已是目标尺寸，禁用 Path 自身的拉伸/重拟合
    }

    private void AnimateArrowFlip(double toAngle)
    {
        if (_arrowPath == null) return;

        // 无法构建基准几何时降级为瞬时切换（朝上/朝下静态 chevron）。
        if (_chevronBase == null)
        {
            _arrowPath.Data = Geometry.Parse(
                toAngle >= 90.0 ? ArrowUpData : (_arrowDownData ?? ArrowDownData));
            return;
        }

        _arrowAnimTimer?.Stop();

        var fromAngle = _arrowAngle;
        if (Math.Abs(fromAngle - toAngle) < 0.5)
        {
            ApplyArrowAngle(toAngle);
            return;
        }

        var startTick = Environment.TickCount64;
        _arrowAnimTimer = new Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _arrowAnimTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTick;
            var progress = Math.Clamp(elapsed / ArrowFlipDurationMs, 0.0, 1.0);
            var eased = s_arrowFlipEase.Ease(progress);
            ApplyArrowAngle(fromAngle + (toAngle - fromAngle) * eased);

            if (progress >= 1.0)
            {
                _arrowAnimTimer.Stop();
                _arrowAnimTimer = null;
                ApplyArrowAngle(toAngle);
            }
        };
        _arrowAnimTimer.Start();
    }

    private void ApplyArrowAngle(double angle)
    {
        _arrowAngle = angle;
        if (_arrowPath == null || _chevronBase == null) return;
        _arrowPath.Data = RotateChevron(_chevronBase, angle, _chevronCenterX, _chevronCenterY);
    }

    // 均匀拉伸 src 至 boxW×boxH 并居中（圆弧半径同比缩放）。
    private static PathGeometry StretchUniform(PathGeometry src, double boxW, double boxH)
    {
        var b = src.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return src;

        var s = Math.Min(boxW / b.Width, boxH / b.Height);
        var contentW = b.Width * s;
        var contentH = b.Height * s;
        var dx = (boxW - contentW) / 2.0 - b.X * s;
        var dy = (boxH - contentH) / 2.0 - b.Y * s;

        return TransformGeometry(src,
            p => new Point(p.X * s + dx, p.Y * s + dy),
            sz => new Size(sz.Width * s, sz.Height * s));
    }

    // 绕 (cx,cy) 旋转 angleDeg（圆弧半径不随旋转改变，故 size 透传）。
    private static PathGeometry RotateChevron(PathGeometry src, double angleDeg, double cx, double cy)
    {
        var r = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);

        Point Rot(Point p)
        {
            var dx = p.X - cx;
            var dy = p.Y - cy;
            return new Point(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
        }

        return TransformGeometry(src, Rot, sz => sz);
    }

    // 把点变换 tp / 圆弧尺寸变换 ts 应用到 src 的每个图形/段，返回新 PathGeometry（结构同 Path.ClonePathGeometry）。
    private static PathGeometry TransformGeometry(PathGeometry src, Func<Point, Point> tp, Func<Size, Size> ts)
    {
        var clone = new PathGeometry { FillRule = src.FillRule };
        foreach (var fig in src.Figures)
        {
            var nf = new PathFigure
            {
                StartPoint = tp(fig.StartPoint),
                IsClosed = fig.IsClosed,
                IsFilled = fig.IsFilled,
            };
            foreach (var seg in fig.Segments)
            {
                switch (seg)
                {
                    case LineSegment l:
                        nf.Segments.Add(new LineSegment(tp(l.Point), l.IsStroked));
                        break;
                    case PolyLineSegment pl:
                    {
                        var s = new PolyLineSegment { IsStroked = pl.IsStroked };
                        foreach (var pt in pl.Points) s.Points.Add(tp(pt));
                        nf.Segments.Add(s);
                        break;
                    }
                    case ArcSegment a:
                        nf.Segments.Add(new ArcSegment(tp(a.Point), ts(a.Size), a.RotationAngle,
                            a.IsLargeArc, a.SweepDirection, a.IsStroked));
                        break;
                    case BezierSegment bz:
                        nf.Segments.Add(new BezierSegment(tp(bz.Point1), tp(bz.Point2), tp(bz.Point3), bz.IsStroked));
                        break;
                    case PolyBezierSegment pb:
                    {
                        var s = new PolyBezierSegment { IsStroked = pb.IsStroked };
                        foreach (var pt in pb.Points) s.Points.Add(tp(pt));
                        nf.Segments.Add(s);
                        break;
                    }
                    case QuadraticBezierSegment q:
                        nf.Segments.Add(new QuadraticBezierSegment(tp(q.Point1), tp(q.Point2), q.IsStroked));
                        break;
                    case PolyQuadraticBezierSegment pq:
                    {
                        var s = new PolyQuadraticBezierSegment { IsStroked = pq.IsStroked };
                        foreach (var pt in pq.Points) s.Points.Add(tp(pt));
                        nf.Segments.Add(s);
                        break;
                    }
                }
            }
            clone.Figures.Add(nf);
        }
        return clone;
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

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item) => item is ComboBoxItem;

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item) => new ComboBoxItem();

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is not ComboBoxItem container)
        {
            return;
        }

        if (!ReferenceEquals(container, item))
        {
            container.ContentTemplateSelector = ItemTemplateSelector;
            container.ContentStringFormat = ItemStringFormat;
        }

        container.IsSelected = Equals(item, SelectedItem);

        // Recycled containers retain exactly one owner callback.
        container.ItemClicked -= OnContainerItemClicked;
        container.ItemClicked += OnContainerItemClicked;
    }

    /// <inheritdoc />
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        if (element is ComboBoxItem container)
        {
            container.ItemClicked -= OnContainerItemClicked;
            container.SetIsHighlighted(false);
            if (!ReferenceEquals(container, item))
            {
                container.IsSelected = false;
            }
        }

        base.ClearContainerForItem(element, item);
    }

    private void OnContainerItemClicked(object? sender, EventArgs e)
    {
        if (sender is not ComboBoxItem container) return;

        var index = ItemContainerGenerator.IndexFromContainer(container);
        if (index >= 0)
        {
            SelectedIndex = index;
        }
        else
        {
            SelectedItem = container;
        }

        IsDropDownOpen = false;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Text search supports PropertyAccessorRegistry registrations and retains reflection fallback for unregistered runtime item types.")]
    private string GetItemText(object? item)
    {
        if (item == null) return string.Empty;

        if (item is DependencyObject dependencyObject)
        {
            string explicitText = TextSearch.GetText(dependencyObject);
            if (!string.IsNullOrEmpty(explicitText))
            {
                return explicitText;
            }
        }

        if (item is ContentControl contentControl)
        {
            item = contentControl.Content;
            if (item == null) return string.Empty;
        }

        string textPath = TextSearch.GetTextPath(this);
        if (string.IsNullOrEmpty(textPath))
        {
            textPath = DisplayMemberPath;
        }

        if (!string.IsNullOrEmpty(textPath) && TryReadPropertyPath(item, textPath, out var value))
        {
            return value?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "PropertyAccessorRegistry is the framework AOT-aware lookup boundary; applications can register typed accessors for trimmed models.")]
    private static bool TryReadPropertyPath(object source, string path, out object? value)
    {
        object? current = source;
        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current == null || !PropertyAccessorRegistry.TryReadProperty(current, segment, out current))
            {
                value = null;
                return false;
            }
        }

        value = current;
        return true;
    }
}

/// <summary>
/// Represents an item in a ComboBox.
/// </summary>
public class ComboBoxItem : ListBoxItem
{
    private static readonly SolidColorBrush s_fallbackHoverBackgroundBrush = new(Themes.ThemeColors.HighlightBackground);
    private static readonly SolidColorBrush s_fallbackSelectedBackgroundBrush = new(Themes.ThemeColors.SelectionBackground);

    #region Dependency Properties

    /// <summary>
    /// Identifies the read-only <see cref="IsHighlighted"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    private static readonly DependencyPropertyKey IsHighlightedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsHighlighted),
            typeof(bool),
            typeof(ComboBoxItem),
            new PropertyMetadata(false, OnIsHighlightedChanged));

    /// <summary>
    /// Identifies the read-only <see cref="IsHighlighted"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IsHighlightedProperty =
        IsHighlightedPropertyKey.DependencyProperty;

    #endregion

    /// <summary>
    /// Gets whether this item is the current keyboard or pointer highlight.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty)!;
        protected set => SetValue(IsHighlightedPropertyKey, value);
    }

    internal void SetIsHighlighted(bool value) => IsHighlighted = value;

    /// <summary>
    /// Occurs when the item is clicked.
    /// </summary>
    public event EventHandler? ItemClicked;

    private bool _isPressed;
    private bool _isItemMouseOver;
    private Border? _backgroundBorder;

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
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        ResourcesChanged += OnResourcesChangedHandler;

        // Set up mouse event handlers for click behavior and hover tracking
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler), handledEventsToo: true);
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler), handledEventsToo: true);
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler), handledEventsToo: true);
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler), handledEventsToo: true);
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler), handledEventsToo: true);
        AddHandler(TouchMoveEvent, new RoutedEventHandler(OnTouchMoveHandler), handledEventsToo: true);
        AddHandler(TouchUpEvent, new RoutedEventHandler(OnTouchUpHandler), handledEventsToo: true);
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocusHandler), handledEventsToo: true);
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostKeyboardFocusHandler), handledEventsToo: true);
        TouchHelper.SetIsRippleEnabled(this, true);
    }

    private const double TouchPanCancelThresholdDips = 8.0;
    private int _activeTouchId = -1;
    private Point _activeTouchDownPos;

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;
        _activeTouchId = touchArgs.TouchDevice.Id;
        _activeTouchDownPos = touchArgs.GetTouchPoint(this).Position;
        _isPressed = true;
        // Suppress mouse synthesis so OnMouseDown does not trigger the item
        // immediately. PointerDown still bubbles to an ancestor ScrollViewer
        // because the dispatcher raises pointer events unconditionally.
        e.Handled = true;
    }

    private void OnTouchMoveHandler(object sender, RoutedEventArgs e)
    {
        if (!_isPressed || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        var current = touchArgs.GetTouchPoint(this).Position;
        double dx = current.X - _activeTouchDownPos.X;
        double dy = current.Y - _activeTouchDownPos.Y;
        if (dx * dx + dy * dy > TouchPanCancelThresholdDips * TouchPanCancelThresholdDips)
        {
            _isPressed = false; // cancel selection candidate, let ScrollViewer pan
            _activeTouchId = -1;
        }
    }

    private void OnTouchUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        bool wasCandidate = _isPressed;
        _isPressed = false;
        _activeTouchId = -1;
        if (wasCandidate)
        {
            ItemClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backgroundBorder = GetTemplateChild("PART_BackgroundBorder") as Border;
        UpdateContainerVisualState();
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

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        SetIsHighlighted(true);
        if (!_isItemMouseOver)
        {
            _isItemMouseOver = true;
            UpdateContainerVisualState();
        }
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        if (!IsKeyboardFocused)
        {
            SetIsHighlighted(false);
        }

        if (_isItemMouseOver)
        {
            _isItemMouseOver = false;
            UpdateContainerVisualState();
        }
    }

    private void OnGotKeyboardFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        SetIsHighlighted(true);
    }

    private void OnLostKeyboardFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isItemMouseOver)
        {
            SetIsHighlighted(false);
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

    private static void OnIsHighlightedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBoxItem item)
        {
            item.UpdateContainerVisualState();
        }
    }

    private void UpdateContainerVisualState()
    {
        if (_backgroundBorder == null)
        {
            return;
        }

        if (IsSelected)
        {
            _backgroundBorder.Background = ResolveSelectedBackgroundBrush();
            return;
        }

        if (_isItemMouseOver || IsHighlighted)
        {
            _backgroundBorder.Background = ResolveHoverBackgroundBrush();
            return;
        }

        _backgroundBorder.ClearValue(Border.BackgroundProperty);
    }

    private Brush ResolveHoverBackgroundBrush()
        => TryFindResource("HighlightBackground") as Brush ?? s_fallbackHoverBackgroundBrush;

    private Brush ResolveSelectedBackgroundBrush()
        => TryFindResource("SelectionBackground") as Brush ?? s_fallbackSelectedBackgroundBrush;

    private void OnResourcesChangedHandler(object? sender, EventArgs e)
    {
        UpdateContainerVisualState();
    }
}
