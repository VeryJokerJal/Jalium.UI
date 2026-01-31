using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button that can be selected, but not cleared, by a user.
/// RadioButtons in the same group are mutually exclusive.
/// </summary>
public class RadioButton : ToggleButton
{
    #region Static Fields

    /// <summary>
    /// Tracks RadioButtons by group name for mutual exclusion.
    /// </summary>
    private static readonly Dictionary<string, List<WeakReference<RadioButton>>> _groupMap = new();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the GroupName dependency property.
    /// </summary>
    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.Register(nameof(GroupName), typeof(string), typeof(RadioButton),
            new PropertyMetadata(string.Empty, OnGroupNameChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the name of the group that the RadioButton belongs to.
    /// RadioButtons in the same group are mutually exclusive.
    /// </summary>
    public string GroupName
    {
        get => (string)(GetValue(GroupNameProperty) ?? string.Empty);
        set => SetValue(GroupNameProperty, value);
    }

    #endregion

    #region Constructor

    private const double RadioButtonSize = 18.0;
    private const double RadioButtonMargin = 8.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="RadioButton"/> class.
    /// </summary>
    public RadioButton()
    {
        RegisterInGroup(GroupName);
    }

    #endregion

    #region Group Management

    private void RegisterInGroup(string groupName)
    {
        var effectiveGroup = string.IsNullOrEmpty(groupName) ? GetDefaultGroupName() : groupName;

        if (!_groupMap.TryGetValue(effectiveGroup, out var list))
        {
            list = new List<WeakReference<RadioButton>>();
            _groupMap[effectiveGroup] = list;
        }

        // Clean up dead references and add ourselves
        list.RemoveAll(wr => !wr.TryGetTarget(out _));
        list.Add(new WeakReference<RadioButton>(this));
    }

    private void UnregisterFromGroup(string groupName)
    {
        var effectiveGroup = string.IsNullOrEmpty(groupName) ? GetDefaultGroupName() : groupName;

        if (_groupMap.TryGetValue(effectiveGroup, out var list))
        {
            list.RemoveAll(wr => !wr.TryGetTarget(out var target) || target == this);
            if (list.Count == 0)
            {
                _groupMap.Remove(effectiveGroup);
            }
        }
    }

    private string GetDefaultGroupName()
    {
        // Use parent as default group scope
        return VisualParent?.GetHashCode().ToString() ?? "__default__";
    }

    private void UncheckOthersInGroup()
    {
        var effectiveGroup = string.IsNullOrEmpty(GroupName) ? GetDefaultGroupName() : GroupName;

        if (_groupMap.TryGetValue(effectiveGroup, out var list))
        {
            foreach (var wr in list)
            {
                if (wr.TryGetTarget(out var radioButton) && radioButton != this && radioButton.IsChecked == true)
                {
                    radioButton.IsChecked = false;
                }
            }
        }
    }

    #endregion

    #region Toggle Handling

    /// <inheritdoc />
    protected override void OnToggle()
    {
        // RadioButton can only be checked, not unchecked by clicking
        if (IsChecked != true)
        {
            IsChecked = true;
        }
    }

    /// <inheritdoc />
    protected override void OnIsCheckedChanged(bool? oldValue, bool? newValue)
    {
        if (newValue == true)
        {
            // Uncheck other RadioButtons in the same group
            UncheckOthersInGroup();
        }

        base.OnIsCheckedChanged(oldValue, newValue);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var contentSize = MeasureContent(new Size(
            Math.Max(0, availableSize.Width - RadioButtonSize - RadioButtonMargin),
            availableSize.Height));

        return new Size(
            RadioButtonSize + RadioButtonMargin + contentSize.Width,
            Math.Max(RadioButtonSize, contentSize.Height));
    }

    private Size MeasureContent(Size availableSize)
    {
        if (Content is string text)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 14;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(formattedText.Width, formattedText.Height);
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return element.DesiredSize;
        }

        return new Size(0, 0);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Content is FrameworkElement fe)
        {
            var contentRect = new Rect(
                RadioButtonSize + RadioButtonMargin,
                0,
                Math.Max(0, finalSize.Width - RadioButtonSize - RadioButtonMargin),
                finalSize.Height);

            fe.Arrange(contentRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        // Calculate radio button circle position (vertically centered)
        var circleY = RenderSize.Height / 2;
        var circleX = RadioButtonSize / 2;
        var radius = RadioButtonSize / 2 - 1;

        // Use property values - styles and triggers handle state changes
        var bgBrush = Background;
        var borderBrush = BorderBrush;
        var fgBrush = Foreground;

        // Draw background circle
        if (bgBrush != null)
        {
            dc.DrawEllipse(bgBrush, null, new Point(circleX, circleY), radius, radius);
        }

        // Draw border circle
        if (borderBrush != null)
        {
            var borderPen = new Pen(borderBrush, 1.5);
            dc.DrawEllipse(null, borderPen, new Point(circleX, circleY), radius, radius);
        }

        // Draw inner circle when checked
        if (IsChecked == true)
        {
            // Use white for the inner dot (contrasts with blue accent background)
            var checkedBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            var innerRadius = radius * 0.45;
            dc.DrawEllipse(checkedBrush, null, new Point(circleX, circleY), innerRadius, innerRadius);
        }

        // Draw content text
        if (Content is string text && !string.IsNullOrEmpty(text) && fgBrush != null)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 14;
            var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);

            var formattedText = new FormattedText(text, fontFamily, fontSize)
            {
                Foreground = fgBrush
            };

            var textX = RadioButtonSize + RadioButtonMargin;
            var textY = (RenderSize.Height - fontMetrics.LineHeight) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnGroupNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RadioButton radioButton)
        {
            radioButton.UnregisterFromGroup((string?)e.OldValue ?? string.Empty);
            radioButton.RegisterInGroup((string?)e.NewValue ?? string.Empty);
        }
    }

    #endregion
}
