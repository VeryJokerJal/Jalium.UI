using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Represents a button on a Ribbon.
/// </summary>
public sealed class RibbonButton : Button
{
    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(RibbonButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
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
public sealed class RibbonToggleButton : ToggleButton
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
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
[ContentProperty("Items")]
public class RibbonSplitButton : ItemsControl
{
    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(RibbonSplitButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(RibbonSplitButton),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the dropdown is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
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
    /// Occurs when the button part is clicked.
    /// </summary>
    public event RoutedEventHandler? Click;

    /// <summary>
    /// Raises the Click event.
    /// </summary>
    protected virtual void OnClick()
    {
        Click?.Invoke(this, new RoutedEventArgs());
    }
}

/// <summary>
/// Represents a menu button on a Ribbon.
/// </summary>
[ContentProperty("Items")]
public class RibbonMenuButton : ItemsControl
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
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
    /// Gets or sets whether the dropdown is open.
    /// </summary>
    public bool IsDropDownOpen { get; set; }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }
}

/// <summary>
/// Represents a text box on a Ribbon.
/// </summary>
public sealed class RibbonTextBox : TextBox
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
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
public sealed class RibbonComboBox : ComboBox
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
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
/// Represents a check box on a Ribbon.
/// </summary>
public sealed class RibbonCheckBox : CheckBox
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
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
public sealed class RibbonSeparator : Control
{
    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    public string? Label { get; set; }
}
