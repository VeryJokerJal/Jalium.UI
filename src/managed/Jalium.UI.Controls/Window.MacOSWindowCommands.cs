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

    private Border? _macOSWindowCommands;
    private ContentPresenter? _macOSLeftWindowCommands;
    private ContentPresenter? _macOSRightWindowCommands;
    private TextBlock? _macOSWindowTitle;

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
            var toolbar = new Grid();
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _macOSLeftWindowCommands = new ContentPresenter();
            _macOSRightWindowCommands = new ContentPresenter();
            Grid.SetColumn(_macOSRightWindowCommands, 1);
            toolbar.Children.Add(_macOSLeftWindowCommands);
            toolbar.Children.Add(_macOSRightWindowCommands);
            _macOSWindowCommands = new Border
            {
                Child = toolbar, DataContext = DataContext,
                BorderThickness = new Thickness(0, 0, 0, 1), ClipToBounds = true,
            };
            _macOSWindowCommands.SetResourceReference(BackgroundProperty, "TitleBarBackground");
            _macOSWindowCommands.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
            AddVisualChild(_macOSWindowCommands);
        }

        _macOSWindowCommands.Height = GetEffectiveTitleBarHeightDip();
        // Native traffic lights keep their system hit targets and accessibility.
        // Reserve their leading area even in fullscreen, so controls do not jump
        // or collide when AppKit reveals its title bar at the top edge.
        _macOSWindowCommands.Padding = UsesExtendedMacOSTitleBar ? new Thickness(80, 0, 0, 0) : default;
        // A right-only toolbar (for example a floating tool window) still
        // needs its title when AppKit hides the caption in extended mode.
        FrameworkElement? leftCommands = LeftWindowCommands;
        if (leftCommands == null && UsesExtendedMacOSTitleBar && IsShowTitle)
        {
            if (_macOSWindowTitle == null)
            {
                _macOSWindowTitle = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                _macOSWindowTitle.SetResourceReference(TextBlock.ForegroundProperty, "TitleBarText");
            }
            // Title changes already call ApplyTitleBarPresentation, including
            // floating panels that synchronize their selected document title.
            _macOSWindowTitle.Text = Title;
            leftCommands = _macOSWindowTitle;
        }
        _macOSLeftWindowCommands!.Content = leftCommands;
        _macOSRightWindowCommands!.Content = RightWindowCommands;
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
        _macOSLeftWindowCommands!.Content = null;
        _macOSRightWindowCommands!.Content = null;
        var toolbar = _macOSWindowCommands;
        _macOSWindowCommands = null;
        _macOSLeftWindowCommands = null;
        _macOSRightWindowCommands = null;
        _macOSWindowTitle = null;
        RemoveVisualChild(toolbar);
    }
}
