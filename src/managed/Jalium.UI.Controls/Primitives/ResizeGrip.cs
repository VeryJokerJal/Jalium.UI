using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a resize grip for Window controls.
/// </summary>
[TemplatePart(Name = "PART_Grip", Type = typeof(Jalium.UI.Shapes.Path))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class ResizeGrip : Control
{
    static ResizeGrip() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ResizeGrip), new PropertyMetadata(typeof(ResizeGrip)));
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Thumb);

    #region Private Fields

    private const double DefaultSize = 12;

    #endregion

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateGripState();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateGripState();
    }

    private void UpdateGripState() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ResizeGrip"/> class.
    /// </summary>
    public ResizeGrip()
    {
        Width = DefaultSize;
        Height = DefaultSize;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Cursor = Jalium.UI.Input.Cursors.SizeNWSE;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Size(DefaultSize, DefaultSize);
    }

    #endregion

}
