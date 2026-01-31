using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a selection control with a drop-down list that can be shown or hidden by clicking the control.
/// </summary>
public class ComboBox : Selector
{
    private Popup? _popup;
    private Border? _dropdownBorder;
    private StackPanel? _itemsPanel;
    private TextBlock? _selectedTextBlock;
    private bool _isDropDownOpen;

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
        // Use dark theme colors
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        Foreground = new SolidColorBrush(Color.White);
        BorderThickness = new Thickness(1);
        MinHeight = 32;
        MinWidth = 120;
        Padding = new Thickness(8, 4, 8, 4);

        // Create the selected text display
        _selectedTextBlock = new TextBlock
        {
            Text = PlaceholderText,
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Set up mouse handling
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));

        // Create the dropdown popup
        CreateDropdownPopup();
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        // Toggle dropdown
        IsDropDownOpen = !IsDropDownOpen;
        e.Handled = true;
    }

    private void CreateDropdownPopup()
    {
        _itemsPanel = new StackPanel();

        _dropdownBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 4, 0, 4),
            Child = _itemsPanel
        };

        _popup = new Popup
        {
            Child = _dropdownBorder,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };

        _popup.Closed += (s, e) =>
        {
            _isDropDownOpen = false;
            SetValue(IsDropDownOpenProperty, false);
        };
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
        UpdateSelectedText();
    }

    private void OpenDropDown()
    {
        if (_popup == null || _isDropDownOpen) return;

        // Populate dropdown items
        PopulateDropdownItems();

        // Set dropdown size
        if (_dropdownBorder != null)
        {
            var dropdownWidth = ActualWidth > 0 ? ActualWidth : (MinWidth > 0 ? MinWidth : 120);
            _dropdownBorder.MinWidth = dropdownWidth;
            _dropdownBorder.Width = dropdownWidth;

            // Calculate height based on items
            var itemCount = Items.Count;
            var itemHeight = 32.0; // Default item height
            var padding = 8.0; // Top + bottom padding
            var calculatedHeight = itemCount * itemHeight + padding;

            // Apply MaxDropDownHeight constraint
            var maxHeight = MaxDropDownHeight > 0 ? MaxDropDownHeight : 200;
            var finalHeight = Math.Min(calculatedHeight, maxHeight);

            // Ensure minimum height
            finalHeight = Math.Max(finalHeight, itemHeight + padding);

            _dropdownBorder.Height = finalHeight;
        }

        _popup.IsOpen = true;
        _isDropDownOpen = true;
        DropDownOpened?.Invoke(this, EventArgs.Empty);
    }

    private void CloseDropDown()
    {
        if (_popup == null || !_isDropDownOpen) return;

        _popup.IsOpen = false;
        _isDropDownOpen = false;
        DropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private void PopulateDropdownItems()
    {
        if (_itemsPanel == null) return;

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

    private void UpdateSelectedText()
    {
        if (_selectedTextBlock == null) return;

        var selectedItem = SelectedItem;
        if (selectedItem != null)
        {
            _selectedTextBlock.Text = GetItemText(selectedItem);
            _selectedTextBlock.Foreground = Foreground ?? new SolidColorBrush(Color.White);
        }
        else
        {
            _selectedTextBlock.Text = PlaceholderText;
            _selectedTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure text
        var textWidth = Math.Max(MinWidth, 120);
        var textHeight = Math.Max(MinHeight, 32);

        return new Size(
            Math.Min(textWidth + Padding.Left + Padding.Right + 20, availableSize.Width), // +20 for arrow
            Math.Min(textHeight, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    // Override to prevent ItemsControl from rendering its ItemsHost as a visual child
    // ComboBox displays selected item text directly, not through the items panel
    // The items are only shown in the popup dropdown
    public override int VisualChildrenCount => 0;

    public override Visual? GetVisualChild(int index)
    {
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.Left > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            dc.DrawRectangle(null, pen, bounds);
        }

        // Draw selected text
        if (_selectedTextBlock != null)
        {
            var fontFamily = _selectedTextBlock.FontFamily ?? "Segoe UI";
            var fontSize = _selectedTextBlock.FontSize > 0 ? _selectedTextBlock.FontSize : 14;
            var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);

            var text = new FormattedText(
                _selectedTextBlock.Text,
                fontFamily,
                fontSize)
            {
                Foreground = _selectedTextBlock.Foreground ?? new SolidColorBrush(Color.Black),
                MaxTextWidth = ActualWidth - Padding.Left - Padding.Right - 20,
                MaxTextHeight = ActualHeight - Padding.Top - Padding.Bottom
            };

            var textY = (ActualHeight - fontMetrics.LineHeight) / 2;
            dc.DrawText(text, new Point(Padding.Left, textY));
        }

        // Draw dropdown arrow
        var arrowFontMetrics = TextMeasurement.GetFontMetrics("Segoe UI", 10);
        var arrowText = new FormattedText("\u25BC", "Segoe UI", 10)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
        };

        var arrowX = ActualWidth - Padding.Right - 12;
        var arrowY = (ActualHeight - arrowFontMetrics.LineHeight) / 2;
        dc.DrawText(arrowText, new Point(arrowX, arrowY));

        // Note: Do not call base.OnRender - ComboBox handles all rendering directly
    }
}

/// <summary>
/// Represents an item in a ComboBox.
/// </summary>
public class ComboBoxItem : ContentControl
{
    private bool _isMouseOver;
    private bool _isPressed;

    /// <summary>
    /// Gets or sets whether this item is selected.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Occurs when the item is clicked.
    /// </summary>
    public event EventHandler? ItemClicked;

    public ComboBoxItem()
    {
        Background = new SolidColorBrush(Color.Transparent);
        Foreground = new SolidColorBrush(Color.White);
        Padding = new Thickness(8, 4, 8, 4);
        MinHeight = 28;

        // Set up mouse event handlers
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
    }

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        _isMouseOver = true;
        // State change handled by style triggers via IsMouseOver property
        InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        _isMouseOver = false;
        // State change handled by style triggers via IsMouseOver property
        InvalidateVisual();
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            CaptureMouse();
            _isPressed = true;
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            var wasPressed = _isPressed;
            _isPressed = false;
            ReleaseMouseCapture();

            // Only fire click if mouse is still over the item
            if (wasPressed && _isMouseOver)
            {
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

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(availableSize.Width, MinHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw content text
        var contentText = Content?.ToString() ?? "";
        if (!string.IsNullOrEmpty(contentText))
        {
            var fontMetrics = TextMeasurement.GetFontMetrics("Segoe UI", 14);
            var text = new FormattedText(contentText, "Segoe UI", 14)
            {
                Foreground = Foreground ?? new SolidColorBrush(Color.White),
                MaxTextWidth = ActualWidth - Padding.Left - Padding.Right,
                MaxTextHeight = ActualHeight - Padding.Top - Padding.Bottom
            };

            var textY = (ActualHeight - fontMetrics.LineHeight) / 2;
            dc.DrawText(text, new Point(Padding.Left, textY));
        }

        // Note: Do not call base.OnRender - ComboBoxItem handles all rendering directly
    }
}
