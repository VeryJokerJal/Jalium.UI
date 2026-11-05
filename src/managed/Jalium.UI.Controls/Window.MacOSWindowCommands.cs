using Jalium.UI.Controls;

namespace Jalium.UI;

public partial class Window
{
    /// <summary>Identifies the <see cref="ExtendContentIntoTitleBar"/> dependency property.</summary>
    public static readonly DependencyProperty ExtendContentIntoTitleBarProperty =
        DependencyProperty.Register(nameof(ExtendContentIntoTitleBar), typeof(bool), typeof(Window),
            new PropertyMetadata(false, OnExtendContentIntoTitleBarChanged));

    /// <summary>
    /// Extends the macOS native window's client area behind its title bar, keeping
    /// AppKit's traffic lights above LeftWindowCommands and RightWindowCommands.
    /// A window without command controls retains its standard caption. Other platforms and
    /// borderless windows retain their existing presentation.
    /// </summary>
    public bool ExtendContentIntoTitleBar
    {
        get => (bool)(GetValue(ExtendContentIntoTitleBarProperty) ?? false);
        set => SetValue(ExtendContentIntoTitleBarProperty, value);
    }

    private bool UsesExtendedMacOSTitleBar => OperatingSystem.IsMacOS() &&
        ExtendContentIntoTitleBar && TitleBarStyle == WindowTitleBarStyle.Native &&
        WindowStyle != WindowStyle.None && IsShowTitleBar &&
        (LeftWindowCommands != null || RightWindowCommands != null);

    private static void OnExtendContentIntoTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        window.ApplyTitleBarPresentation();
        window.InvalidateMeasure();
    }

    private WindowCommandBar? _macOSWindowCommands;

    // AppKit owns the caption and traffic lights. Keep application commands in
    // a toolbar without replacing Content: floating/docking windows
    // rely on their original Content identity when detaching and restoring panels.
    private void UpdateMacOSWindowCommands()
    {
        bool showCommands = OperatingSystem.IsMacOS() &&
            TitleBarStyle == WindowTitleBarStyle.Native && WindowStyle != WindowStyle.None &&
            IsShowTitleBar && (LeftWindowCommands != null || RightWindowCommands != null);
        if (!showCommands)
        {
            RemoveMacOSWindowCommands();
            return;
        }

        if (_macOSWindowCommands == null)
        {
            _macOSWindowCommands = new WindowCommandBar { DataContext = DataContext };
            AddVisualChild(_macOSWindowCommands);
        }

        _macOSWindowCommands.SetCurrentValue(HeightProperty, GetEffectiveTitleBarHeightDip());
        // AppKit retains its native traffic-light hit targets and accessibility.
        _macOSWindowCommands.SetCurrentValue(PaddingProperty,
            UsesExtendedMacOSTitleBar ? new Thickness(80, 0, 0, 0) : default(Thickness));
        _macOSWindowCommands.SetCurrentValue(WindowCommandBar.TitleProperty, Title);
        _macOSWindowCommands.SetCurrentValue(WindowCommandBar.IsTitleVisibleProperty,
            LeftWindowCommands == null && UsesExtendedMacOSTitleBar && IsShowTitle);
        _macOSWindowCommands.SetCurrentValue(WindowCommandBar.LeftCommandsProperty, LeftWindowCommands);
        _macOSWindowCommands.SetCurrentValue(WindowCommandBar.RightCommandsProperty, RightWindowCommands);
    }

    private bool IsExtendedMacOSTitleBarCaption(Point point)
    {
        if (!UsesExtendedMacOSTitleBar || _macOSWindowCommands == null ||
            point.X < 80 || point.X >= Width || point.Y < 0 ||
            point.Y >= GetEffectiveTitleBarHeightDip()) return false;
        var hit = _macOSWindowCommands.HitTest(point)?.VisualHit as DependencyObject;
        // Text and empty toolbar space are draggable. Interactive descendants
        // (including templated button content and the search TextBox) stay client
        // controls and keep receiving clicks, selection and keyboard focus.
        for (var element = hit; element != null && element != _macOSWindowCommands;
            element = Media.VisualTreeHelper.GetParent(element))
        {
            if (element is UIElement { Focusable: true } ||
                element is Controls.Primitives.ButtonBase || element is Controls.Primitives.TextBoxBase)
                return false;
        }
        return true;
    }

    private void RemoveMacOSWindowCommands()
    {
        if (_macOSWindowCommands == null) return;
        _macOSWindowCommands.SetCurrentValue(WindowCommandBar.LeftCommandsProperty, null);
        _macOSWindowCommands.SetCurrentValue(WindowCommandBar.RightCommandsProperty, null);
        var toolbar = _macOSWindowCommands;
        _macOSWindowCommands = null;
        RemoveVisualChild(toolbar);
    }
}
