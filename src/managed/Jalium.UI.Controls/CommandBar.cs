using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>A templated toolbar with primary commands and a keyboard-accessible overflow.</summary>
[TemplatePart(Name = "PART_PrimaryItemsControl", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_SecondaryItemsControl", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_MoreButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_OverflowPopup", Type = typeof(Popup))]
[TemplateVisualState(Name = "Open", GroupName = "OpenStates")]
[TemplateVisualState(Name = "Closed", GroupName = "OpenStates")]
[TemplateVisualState(Name = "Expanded", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "Compact", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "Minimal", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "Hidden", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "OverflowButtonVisible", GroupName = "OverflowButtonStates")]
[TemplateVisualState(Name = "OverflowButtonHidden", GroupName = "OverflowButtonStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class CommandBar : Control
{
    static CommandBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CommandBar), new PropertyMetadata(typeof(CommandBar)));
        FocusableProperty.OverrideMetadata(typeof(CommandBar), new PropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(CommandBar), new PropertyMetadata(false));
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.CommandBarAutomationPeer(this);

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(CommandBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

    /// <summary>
    /// Identifies the ClosedDisplayMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ClosedDisplayModeProperty =
        DependencyProperty.Register(nameof(ClosedDisplayMode), typeof(CommandBarClosedDisplayMode), typeof(CommandBar),
            new PropertyMetadata(CommandBarClosedDisplayMode.Compact, OnOptionsChanged));

    /// <summary>
    /// Identifies the DefaultLabelPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DefaultLabelPositionProperty =
        DependencyProperty.Register(nameof(DefaultLabelPosition), typeof(CommandBarDefaultLabelPosition), typeof(CommandBar),
            new PropertyMetadata(CommandBarDefaultLabelPosition.Bottom, OnOptionsChanged));

    /// <summary>
    /// Identifies the OverflowButtonVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty OverflowButtonVisibilityProperty =
        DependencyProperty.Register(nameof(OverflowButtonVisibility), typeof(CommandBarOverflowButtonVisibility), typeof(CommandBar),
            new PropertyMetadata(CommandBarOverflowButtonVisibility.Auto, OnOptionsChanged));

    /// <summary>
    /// Identifies the IsSticky dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsStickyProperty =
        DependencyProperty.Register(nameof(IsSticky), typeof(bool), typeof(CommandBar),
            new PropertyMetadata(false));

    #endregion


    private static readonly DependencyPropertyKey PrimaryCommandsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PrimaryCommands), typeof(ObservableCollection<ICommandBarElement>), typeof(CommandBar), new PropertyMetadata(null));
    public static readonly DependencyProperty PrimaryCommandsProperty = PrimaryCommandsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey SecondaryCommandsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SecondaryCommands), typeof(ObservableCollection<ICommandBarElement>), typeof(CommandBar), new PropertyMetadata(null));
    public static readonly DependencyProperty SecondaryCommandsProperty = SecondaryCommandsPropertyKey.DependencyProperty;

    /// <summary>Identifies the label layout inherited by stock AppBar command templates.</summary>
    public static readonly DependencyProperty LabelPositionProperty = DependencyProperty.RegisterAttached(
        "LabelPosition", typeof(CommandBarDefaultLabelPosition), typeof(CommandBar),
        new FrameworkPropertyMetadata(CommandBarDefaultLabelPosition.Bottom, FrameworkPropertyMetadataOptions.Inherits));
    public static CommandBarDefaultLabelPosition GetLabelPosition(DependencyObject element)
        => (CommandBarDefaultLabelPosition)element.GetValue(LabelPositionProperty)!;
    public static void SetLabelPosition(DependencyObject element, CommandBarDefaultLabelPosition value)
        => element.SetValue(LabelPositionProperty, value);

    /// <summary>Overrides the overflow surface; null follows the toolbar background.</summary>
    public static readonly DependencyProperty OverflowBackgroundProperty = DependencyProperty.Register(
        nameof(OverflowBackground), typeof(Brush), typeof(CommandBar), new PropertyMetadata(null, OnOptionsChanged));
    public Brush? OverflowBackground { get => (Brush?)GetValue(OverflowBackgroundProperty); set => SetValue(OverflowBackgroundProperty, value); }
    private static readonly DependencyPropertyKey IsOverflowButtonVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsOverflowButtonVisible), typeof(bool), typeof(CommandBar), new PropertyMetadata(false));
    public static readonly DependencyProperty IsOverflowButtonVisibleProperty = IsOverflowButtonVisiblePropertyKey.DependencyProperty;
    public bool IsOverflowButtonVisible => (bool)GetValue(IsOverflowButtonVisibleProperty)!;
    private static readonly DependencyPropertyKey IsOverflowOpenPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsOverflowOpen), typeof(bool), typeof(CommandBar), new PropertyMetadata(false));
    public static readonly DependencyProperty IsOverflowOpenProperty = IsOverflowOpenPropertyKey.DependencyProperty;
    public bool IsOverflowOpen => (bool)GetValue(IsOverflowOpenProperty)!;
    private static readonly DependencyPropertyKey HasPrimaryCommandsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasPrimaryCommands), typeof(bool), typeof(CommandBar), new PropertyMetadata(false));
    public static readonly DependencyProperty HasPrimaryCommandsProperty = HasPrimaryCommandsPropertyKey.DependencyProperty;
    public bool HasPrimaryCommands => (bool)GetValue(HasPrimaryCommandsProperty)!;
    private static readonly DependencyPropertyKey HasSecondaryCommandsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasSecondaryCommands), typeof(bool), typeof(CommandBar), new PropertyMetadata(false));
    public static readonly DependencyProperty HasSecondaryCommandsProperty = HasSecondaryCommandsPropertyKey.DependencyProperty;
    public bool HasSecondaryCommands => (bool)GetValue(HasSecondaryCommandsProperty)!;
    private static readonly DependencyPropertyKey EffectiveOverflowBackgroundPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(EffectiveOverflowBackground), typeof(Brush), typeof(CommandBar), new PropertyMetadata(null));
    public static readonly DependencyProperty EffectiveOverflowBackgroundProperty = EffectiveOverflowBackgroundPropertyKey.DependencyProperty;
    public Brush? EffectiveOverflowBackground => (Brush?)GetValue(EffectiveOverflowBackgroundProperty)!;

    #region CLR Properties

    /// <summary>
    /// Gets the collection of primary command elements for the CommandBar.
    /// </summary>
    public ObservableCollection<ICommandBarElement> PrimaryCommands => (ObservableCollection<ICommandBarElement>)GetValue(PrimaryCommandsProperty)!;

    /// <summary>
    /// Gets the collection of secondary command elements for the CommandBar.
    /// </summary>
    public ObservableCollection<ICommandBarElement> SecondaryCommands => (ObservableCollection<ICommandBarElement>)GetValue(SecondaryCommandsProperty)!;

    /// <summary>
    /// Gets or sets a value indicating whether the CommandBar is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the display mode when the CommandBar is closed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CommandBarClosedDisplayMode ClosedDisplayMode
    {
        get => (CommandBarClosedDisplayMode)(GetValue(ClosedDisplayModeProperty) ?? CommandBarClosedDisplayMode.Compact);
        set => SetValue(ClosedDisplayModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the default label position for primary commands.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public CommandBarDefaultLabelPosition DefaultLabelPosition
    {
        get => (CommandBarDefaultLabelPosition)(GetValue(DefaultLabelPositionProperty) ?? CommandBarDefaultLabelPosition.Bottom);
        set => SetValue(DefaultLabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the overflow button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CommandBarOverflowButtonVisibility OverflowButtonVisibility
    {
        get => (CommandBarOverflowButtonVisibility)(GetValue(OverflowButtonVisibilityProperty) ?? CommandBarOverflowButtonVisibility.Auto);
        set => SetValue(OverflowButtonVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the CommandBar remains open after a command is invoked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSticky
    {
        get => (bool)GetValue(IsStickyProperty)!;
        set => SetValue(IsStickyProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the CommandBar starts to open.
    /// </summary>
    public event EventHandler? Opening;

    /// <summary>
    /// Occurs after the CommandBar is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs when the CommandBar starts to close.
    /// </summary>
    public event EventHandler? Closing;

    /// <summary>
    /// Occurs after the CommandBar is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    private ItemsControl? _primaryItemsControl;
    private ItemsControl? _secondaryItemsControl;
    private ButtonBase? _moreButton;
    private Popup? _overflowPopup;
    private UIElement? _overflowRoot;
    private bool _hasTemplate;
    private bool _syncingPopup;
    private DispatcherOperation? _pendingFocus;

    public CommandBar()
    {
        SetValue(PrimaryCommandsPropertyKey, new ObservableCollection<ICommandBarElement>());
        SetValue(SecondaryCommandsPropertyKey, new ObservableCollection<ICommandBarElement>());
        PrimaryCommands.CollectionChanged += OnCommandsChanged;
        SecondaryCommands.CollectionChanged += OnCommandsChanged;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnBarKeyDown));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdatePresentation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hasTemplate = true;
        _primaryItemsControl = GetTemplateChild("PART_PrimaryItemsControl") as ItemsControl;
        _secondaryItemsControl = GetTemplateChild("PART_SecondaryItemsControl") as ItemsControl;
        _moreButton = GetTemplateChild("PART_MoreButton") as ButtonBase;
        _overflowPopup = GetTemplateChild("PART_OverflowPopup") as Popup;
        _overflowRoot = _overflowPopup?.Child;
        if (_moreButton != null) _moreButton.Click += OnMoreButtonClick;
        if (_overflowPopup != null) _overflowPopup.Closed += OnPopupClosed;
        if (_overflowRoot != null)
        {
            _overflowRoot.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnOverflowKeyDown));
            _overflowRoot.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnOverflowCommandClick), true);
        }
        UpdatePresentation();
        if (IsOpen) QueueFocus(opening: true);
    }

    protected override void OnTemplateChanging()
    {
        _hasTemplate = false;
        CancelFocus();
        if (_moreButton != null) _moreButton.Click -= OnMoreButtonClick;
        if (_overflowRoot != null)
        {
            _overflowRoot.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnOverflowKeyDown));
            _overflowRoot.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnOverflowCommandClick));
        }
        if (_overflowPopup != null)
        {
            _overflowPopup.Closed -= OnPopupClosed;
            _syncingPopup = true;
            try { _overflowPopup.SetCurrentValue(Popup.IsOpenProperty, false); }
            finally { _syncingPopup = false; }
        }
        RetireHost(_primaryItemsControl);
        RetireHost(_secondaryItemsControl);
        _primaryItemsControl = _secondaryItemsControl = null;
        _moreButton = null;
        _overflowPopup = null;
        _overflowRoot = null;
        base.OnTemplateChanging();
    }

    private static void RetireHost(ItemsControl? host)
    {
        if (host == null) return;
        BindingOperations.ClearBinding(host, ItemsControl.ItemsSourceProperty);
        host.SetCurrentValue(ItemsControl.ItemsSourceProperty, null);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty || e.Property == IsEnabledProperty)
        {
            if (!IsEnabled && IsOpen) SetCurrentValue(IsOpenProperty, false);
            UpdatePresentation();
        }
    }

    private bool ShouldShowMoreButton() => OverflowButtonVisibility switch
    {
        CommandBarOverflowButtonVisibility.Visible => true,
        CommandBarOverflowButtonVisibility.Collapsed => false,
        _ => SecondaryCommands.Count > 0 || ClosedDisplayMode == CommandBarClosedDisplayMode.Minimal && PrimaryCommands.Count > 0,
    };

    private void UpdatePresentation()
    {
        SetValue(IsOverflowButtonVisiblePropertyKey, ShouldShowMoreButton());
        SetValue(HasPrimaryCommandsPropertyKey, PrimaryCommands.Count > 0);
        SetValue(HasSecondaryCommandsPropertyKey, SecondaryCommands.Count > 0);
        SetValue(EffectiveOverflowBackgroundPropertyKey, OverflowBackground ?? Background);
        _syncingPopup = true;
        try
        {
            SetValue(IsOverflowOpenPropertyKey, IsOpen && SecondaryCommands.Count > 0);
            if (_overflowPopup != null)
            {
                _overflowPopup.SetCurrentValue(Popup.IsOpenProperty, IsOverflowOpen);
                if (IsOverflowOpen) _overflowPopup.EnsureOpen();
            }
        }
        finally { _syncingPopup = false; }
        if (!_hasTemplate) return;
        VisualStateManager.GoToState(this, IsOpen ? "Open" : "Closed", false);
        VisualStateManager.GoToState(this, IsOpen ? "Expanded" : ClosedDisplayMode.ToString(), false);
        VisualStateManager.GoToState(this, IsOverflowButtonVisible ? "OverflowButtonVisible" : "OverflowButtonHidden", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }

    private void OnCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        bool canShow = ShouldShowMoreButton();
        if (IsOpen && (!canShow || SecondaryCommands.Count == 0 && ClosedDisplayMode != CommandBarClosedDisplayMode.Minimal))
            SetCurrentValue(IsOpenProperty, false);
        UpdatePresentation();
    }

    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (CommandBar)d;
        if (bar.IsOpen && !bar.ShouldShowMoreButton()) bar.SetCurrentValue(IsOpenProperty, false);
        bar.UpdatePresentation();
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (CommandBar)d;
        bool opening = (bool)e.NewValue!;
        bar.CancelFocus();
        if (opening && (!bar.IsEnabled || !bar.ShouldShowMoreButton()))
        {
            bar.SetCurrentValue(IsOpenProperty, false);
            return;
        }
        if (opening) bar.Opening?.Invoke(bar, EventArgs.Empty);
        else bar.Closing?.Invoke(bar, EventArgs.Empty);
        if (bar.IsOpen != opening) return;
        bar.UpdatePresentation();
        if (opening)
        {
            bar.QueueFocus(opening: true);
            bar.Opened?.Invoke(bar, EventArgs.Empty);
        }
        else bar.Closed?.Invoke(bar, EventArgs.Empty);
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e) => SetCurrentValue(IsOpenProperty, !IsOpen);
    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (!_syncingPopup && IsOpen) SetCurrentValue(IsOpenProperty, false);
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        CancelFocus();
        if (IsOpen) SetCurrentValue(IsOpenProperty, false);
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        UpdatePresentation();
        if (IsOpen) QueueFocus(opening: true);
    }
    private void CloseAndRestoreFocus()
    {
        SetCurrentValue(IsOpenProperty, false);
        QueueFocus(opening: false);
    }
    private void OnOverflowCommandClick(object sender, RoutedEventArgs e)
    {
        if (IsOpen && !IsSticky && CommandIndex(SecondaryCommands, e.OriginalSource) >= 0) CloseAndRestoreFocus();
    }

    private void OnBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEnabled) return;
        if (e.Key == Key.Escape && IsOpen) { CloseAndRestoreFocus(); e.Handled = true; return; }
        if ((e.Key is Key.F4 or Key.Down) && (ReferenceEquals(e.OriginalSource, _moreButton) || ReferenceEquals(e.OriginalSource, this)))
        {
            SetCurrentValue(IsOpenProperty, true);
            e.Handled = IsOpen;
            return;
        }
        if (CommandIndex(PrimaryCommands, e.OriginalSource) < 0 && !ReferenceEquals(e.OriginalSource, this)) return;
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
            e.Handled = MoveCommandFocus(PrimaryCommands, e.OriginalSource, e.Key);
    }
    private void OnOverflowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsOpen) return;
        if (e.Key == Key.Escape) { CloseAndRestoreFocus(); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down or Key.Home or Key.End)
            e.Handled = MoveCommandFocus(SecondaryCommands, e.OriginalSource, e.Key);
    }
    private static int CommandIndex(IList<ICommandBarElement> commands, object? source)
    {
        for (var node = source as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ICommandBarElement command) return commands.IndexOf(command);
        return -1;
    }
    private static bool MoveCommandFocus(IList<ICommandBarElement> commands, object? source, Key key)
    {
        var targets = commands.OfType<Control>().Where(control => control.Focusable && control.IsEnabled && control.IsVisible).ToArray();
        if (targets.Length == 0) return false;
        var index = CommandIndex(commands, source);
        var current = index >= 0 ? Array.IndexOf(targets, commands[index] as Control) : -1;
        int target = key switch
        {
            Key.Home => 0,
            Key.End => targets.Length - 1,
            Key.Left or Key.Up => current < 0 ? targets.Length - 1 : (current - 1 + targets.Length) % targets.Length,
            _ => (current + 1) % targets.Length,
        };
        if (!targets[target].Focus()) return false;
        targets[target].BringIntoView();
        return true;
    }
    private void CancelFocus() { _pendingFocus?.Abort(); _pendingFocus = null; }
    private void QueueFocus(bool opening)
    {
        CancelFocus();
        if (!_hasTemplate) return;
        var expectedMore = _moreButton;
        _pendingFocus = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _pendingFocus = null;
            if (!_hasTemplate || IsOpen != opening || !IsEnabled || !IsVisible) return;
            if (opening)
            {
                if (CommandIndex(SecondaryCommands, Keyboard.FocusedElement) < 0)
                    MoveCommandFocus(SecondaryCommands, null, Key.Home);
            }
            else if (ReferenceEquals(_moreButton, expectedMore) && expectedMore?.IsVisible == true)
            {
                var focused = Keyboard.FocusedElement;
                if (focused == null || ReferenceEquals(focused, this) || ReferenceEquals(focused, expectedMore)
                    || CommandIndex(SecondaryCommands, focused) >= 0) expectedMore.Focus();
            }
        });
    }
}
