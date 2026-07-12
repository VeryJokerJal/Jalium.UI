using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

public partial class Window
{
    /// <summary>Identifies the <see cref="AllowsTransparency"/> dependency property.</summary>
    public static readonly DependencyProperty AllowsTransparencyProperty =
        DependencyProperty.Register(
            nameof(AllowsTransparency),
            typeof(bool),
            typeof(Window),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsActivePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsActive),
            typeof(bool),
            typeof(Window),
            new PropertyMetadata(false));

    /// <summary>Identifies the read-only <see cref="IsActive"/> dependency property.</summary>
    public static readonly DependencyProperty IsActiveProperty = IsActivePropertyKey.DependencyProperty;

    /// <summary>Identifies the <see cref="TaskbarItemInfo"/> dependency property.</summary>
    public static readonly DependencyProperty TaskbarItemInfoProperty =
        DependencyProperty.Register(
            nameof(TaskbarItemInfo),
            typeof(Jalium.UI.Shell.TaskbarItemInfo),
            typeof(Window),
            new PropertyMetadata(null));

    /// <summary>Identifies the routed <see cref="DpiChanged"/> event.</summary>
    public static readonly RoutedEvent DpiChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(DpiChanged),
            RoutingStrategy.Bubble,
            typeof(DpiChangedEventHandler),
            typeof(Window));

#pragma warning disable WPF0001 // Window.ThemeMode mirrors WPF's experimental API.
    private ThemeMode _themeMode = ThemeMode.None;

    /// <summary>Gets or sets the theme mode requested for this window.</summary>
    [Experimental("WPF0001")]
    public ThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (_themeMode == value)
            {
                return;
            }

            _themeMode = value;
            InvalidateVisual();
        }
    }
#pragma warning restore WPF0001

    /// <inheritdoc />
    protected internal override IEnumerator LogicalChildren => base.LogicalChildren;

    /// <inheritdoc />
    protected override void OnManipulationBoundaryFeedback(ManipulationBoundaryFeedbackEventArgs e)
    {
        base.OnManipulationBoundaryFeedback(e);
    }

    private void SetIsActive(bool value) => SetValue(IsActivePropertyKey, value);
}
