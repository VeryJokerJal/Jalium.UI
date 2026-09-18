using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public enum ContentDialogResult
{
    None = 0,
    Primary = 1,
    Secondary = 2
}

public enum ContentDialogButton
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Close = 3
}

public enum ContentDialogPlacement
{
    Popup = 0,
    InPlace = 1,
    UnconstrainedPopup = 2
}

[TemplatePart(Name = "PART_TitleHost", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_ButtonPanel", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_DialogCard", Type = typeof(Border))]
[TemplatePart(Name = "PART_PrimaryButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_SecondaryButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_CloseButton", Type = typeof(Button))]
[Jalium.UI.Markup.ContentProperty("Content")]
public class ContentDialog : ContentControl
{
    private const double DefaultDialogMargin = 24.0;
    private const double DefaultDialogCardMinWidth = 320.0;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(object), typeof(ContentDialog),
            new PropertyMetadata(null, OnDialogVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleTemplateProperty =
        DependencyProperty.Register(nameof(TitleTemplate), typeof(DataTemplate), typeof(ContentDialog),
            new PropertyMetadata(null, OnDialogVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrimaryButtonTextProperty =
        DependencyProperty.Register(nameof(PrimaryButtonText), typeof(string), typeof(ContentDialog),
            new PropertyMetadata(string.Empty, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SecondaryButtonTextProperty =
        DependencyProperty.Register(nameof(SecondaryButtonText), typeof(string), typeof(ContentDialog),
            new PropertyMetadata(string.Empty, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CloseButtonTextProperty =
        DependencyProperty.Register(nameof(CloseButtonText), typeof(string), typeof(ContentDialog),
            new PropertyMetadata(string.Empty, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty DefaultButtonProperty =
        DependencyProperty.Register(nameof(DefaultButton), typeof(ContentDialogButton), typeof(ContentDialog),
            new PropertyMetadata(ContentDialogButton.None, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPrimaryButtonEnabledProperty =
        DependencyProperty.Register(nameof(IsPrimaryButtonEnabled), typeof(bool), typeof(ContentDialog),
            new PropertyMetadata(true, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSecondaryButtonEnabledProperty =
        DependencyProperty.Register(nameof(IsSecondaryButtonEnabled), typeof(bool), typeof(ContentDialog),
            new PropertyMetadata(true, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrimaryButtonCommandProperty =
        DependencyProperty.Register(nameof(PrimaryButtonCommand), typeof(ICommand), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonCommandChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SecondaryButtonCommandProperty =
        DependencyProperty.Register(nameof(SecondaryButtonCommand), typeof(ICommand), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonCommandChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CloseButtonCommandProperty =
        DependencyProperty.Register(nameof(CloseButtonCommand), typeof(ICommand), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonCommandChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrimaryButtonCommandParameterProperty =
        DependencyProperty.Register(nameof(PrimaryButtonCommandParameter), typeof(object), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SecondaryButtonCommandParameterProperty =
        DependencyProperty.Register(nameof(SecondaryButtonCommandParameter), typeof(object), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CloseButtonCommandParameterProperty =
        DependencyProperty.Register(nameof(CloseButtonCommandParameter), typeof(object), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrimaryButtonStyleProperty =
        DependencyProperty.Register(nameof(PrimaryButtonStyle), typeof(Style), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SecondaryButtonStyleProperty =
        DependencyProperty.Register(nameof(SecondaryButtonStyle), typeof(Style), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CloseButtonStyleProperty =
        DependencyProperty.Register(nameof(CloseButtonStyle), typeof(Style), typeof(ContentDialog),
            new PropertyMetadata(null, OnButtonConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FullSizeDesiredProperty =
        DependencyProperty.Register(nameof(FullSizeDesired), typeof(bool), typeof(ContentDialog),
            new PropertyMetadata(false, OnDialogVisualPropertyChanged));

    private FrameworkElement? _titleHost;
    private FrameworkElement? _buttonPanel;
    private Border? _dialogCard;
    private ScrollViewer? _contentScrollViewer;
    private Button? _primaryButton;
    private Button? _secondaryButton;
    private Button? _closeButton;
    private ContentDialogOverlayHost? _popupHost;
    private Window? _hostWindow;
    private TaskCompletionSource<ContentDialogResult>? _showTaskSource;
    private Task? _closeTask;
    private ContentDialogPlacement _activePlacement;
    private Size _layoutViewport;
    private readonly List<(UIElement Element, bool WasEnabled)> _disabledSiblings = new();

    public ContentDialog()
    {
        UseTemplateContentManagement();
        // Centered cards can otherwise settle on half a device pixel, splitting
        // their horizontal outline across two rows while the sides stay sharp.
        // Inherit rounding through the template so its centering panels, border
        // and content use the same pixel grid. Render transforms remain continuous.
        UseLayoutRounding = true;
        Visibility = Visibility.Collapsed;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        BackdropEffect = new BackdropBlurEffect(18f);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnDialogKeyDown), handledEventsToo: true);
        SizeChanged += OnDialogSizeChanged;
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate? TitleTemplate
    {
        get => (DataTemplate?)GetValue(TitleTemplateProperty);
        set => SetValue(TitleTemplateProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string PrimaryButtonText
    {
        get => (string)(GetValue(PrimaryButtonTextProperty) ?? string.Empty);
        set => SetValue(PrimaryButtonTextProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string SecondaryButtonText
    {
        get => (string)(GetValue(SecondaryButtonTextProperty) ?? string.Empty);
        set => SetValue(SecondaryButtonTextProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string CloseButtonText
    {
        get => (string)(GetValue(CloseButtonTextProperty) ?? string.Empty);
        set => SetValue(CloseButtonTextProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ContentDialogButton DefaultButton
    {
        get => (ContentDialogButton)(GetValue(DefaultButtonProperty) ?? ContentDialogButton.None);
        set => SetValue(DefaultButtonProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPrimaryButtonEnabled
    {
        get => (bool)(GetValue(IsPrimaryButtonEnabledProperty) ?? true);
        set => SetValue(IsPrimaryButtonEnabledProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSecondaryButtonEnabled
    {
        get => (bool)(GetValue(IsSecondaryButtonEnabledProperty) ?? true);
        set => SetValue(IsSecondaryButtonEnabledProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ICommand? PrimaryButtonCommand
    {
        get => (ICommand?)GetValue(PrimaryButtonCommandProperty);
        set => SetValue(PrimaryButtonCommandProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ICommand? SecondaryButtonCommand
    {
        get => (ICommand?)GetValue(SecondaryButtonCommandProperty);
        set => SetValue(SecondaryButtonCommandProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ICommand? CloseButtonCommand
    {
        get => (ICommand?)GetValue(CloseButtonCommandProperty);
        set => SetValue(CloseButtonCommandProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? PrimaryButtonCommandParameter
    {
        get => GetValue(PrimaryButtonCommandParameterProperty);
        set => SetValue(PrimaryButtonCommandParameterProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? SecondaryButtonCommandParameter
    {
        get => GetValue(SecondaryButtonCommandParameterProperty);
        set => SetValue(SecondaryButtonCommandParameterProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? CloseButtonCommandParameter
    {
        get => GetValue(CloseButtonCommandParameterProperty);
        set => SetValue(CloseButtonCommandParameterProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Style? PrimaryButtonStyle
    {
        get => (Style?)GetValue(PrimaryButtonStyleProperty);
        set => SetValue(PrimaryButtonStyleProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Style? SecondaryButtonStyle
    {
        get => (Style?)GetValue(SecondaryButtonStyleProperty);
        set => SetValue(SecondaryButtonStyleProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Style? CloseButtonStyle
    {
        get => (Style?)GetValue(CloseButtonStyleProperty);
        set => SetValue(CloseButtonStyleProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool FullSizeDesired
    {
        get => (bool)(GetValue(FullSizeDesiredProperty) ?? false);
        set => SetValue(FullSizeDesiredProperty, value);
    }

    public event EventHandler<ContentDialogOpenedEventArgs>? Opened;
    public event EventHandler<ContentDialogClosingEventArgs>? Closing;
    public event EventHandler<ContentDialogClosedEventArgs>? Closed;
    public event EventHandler<ContentDialogButtonClickEventArgs>? PrimaryButtonClick;
    public event EventHandler<ContentDialogButtonClickEventArgs>? SecondaryButtonClick;
    public event EventHandler<ContentDialogButtonClickEventArgs>? CloseButtonClick;

    public Task<ContentDialogResult> ShowAsync()
    {
        return ShowAsync(ContentDialogPlacement.Popup);
    }

    public Task<ContentDialogResult> ShowAsync(ContentDialogPlacement placement)
    {
        VerifyAccess();

        if (_showTaskSource != null)
        {
            throw new InvalidOperationException("ContentDialog is already open.");
        }

        var effectivePlacement = placement;
        if (effectivePlacement == ContentDialogPlacement.InPlace && VisualParent == null)
        {
            effectivePlacement = ContentDialogPlacement.Popup;
        }

        if ((effectivePlacement == ContentDialogPlacement.Popup || effectivePlacement == ContentDialogPlacement.UnconstrainedPopup) && VisualParent != null)
        {
            throw new InvalidOperationException("Popup-hosted ContentDialog must not already be attached to the visual tree.");
        }

        _hostWindow = ResolveHostWindow(effectivePlacement)
            ?? throw new InvalidOperationException("ContentDialog could not resolve a host window.");

        if (effectivePlacement != ContentDialogPlacement.InPlace)
        {
            if (_hostWindow.ActiveContentDialog != null && !ReferenceEquals(_hostWindow.ActiveContentDialog, this))
            {
                throw new InvalidOperationException("Only one ContentDialog can be open per Window.");
            }
        }

        _activePlacement = effectivePlacement;
        _showTaskSource = new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (effectivePlacement == ContentDialogPlacement.InPlace)
        {
            _hostWindow.ActiveInPlaceDialogs.Add(this);
        }
        else
        {
            _hostWindow.ActiveContentDialog = this;
        }
        _hostWindow.OverlayLayer.CloseLightDismissPopups();

        if (_activePlacement == ContentDialogPlacement.Popup || _activePlacement == ContentDialogPlacement.UnconstrainedPopup)
        {
            PrepareDialogSubtree(this);
            InvalidateSubtree(this);
            AttachToPopupOverlay(_hostWindow);
        }
        else
        {
            DisableInPlaceSiblings();
            Visibility = Visibility.Visible;
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
        }

        ApplyTemplate();
        UpdateVisualState();
        ScheduleInitialFocus();
        Opened?.Invoke(this, new ContentDialogOpenedEventArgs());

        return _showTaskSource.Task;
    }

    public void Hide()
    {
        VerifyAccess();

        if (_showTaskSource == null)
        {
            return;
        }

        _ = RequestCloseAsync(ContentDialogButton.Close, ContentDialogResult.None, raiseButtonClickEvent: false, invokeCommand: false);
    }

    internal void OnHostWindowClosed()
    {
        if (_showTaskSource == null)
        {
            return;
        }

        CompleteClose(ContentDialogResult.None);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        var margin = Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        var overlayAvailable = new Size(
            Math.Max(0, availableSize.Width - marginWidth),
            Math.Max(0, availableSize.Height - marginHeight));

        // The slot layout just handed us is the freshest viewport this element can see:
        // the overlay host measures modal roots against the live window client size, so
        // it is already the post-resize value. Cache it for the hooks that run outside a
        // layout pass (OnApplyTemplate, property changes, SizeChanged).
        _layoutViewport = overlayAvailable;

        // Ensure card constraints are up-to-date before the template tree is measured.
        // UpdateCardLayout sets MaxWidth/MaxHeight on PART_DialogCard; doing it here
        // guarantees the constraints are in place even during the very first measure
        // pass (when OnApplyTemplate may not have run yet or ran during this measure).
        UpdateCardLayout();

        var contentSize = MeasureOverride(overlayAvailable);

        if (_activePlacement == ContentDialogPlacement.UnconstrainedPopup)
        {
            return new Size(
                Math.Max(0, contentSize.Width + marginWidth),
                Math.Max(0, contentSize.Height + marginHeight));
        }

        var desiredWidth = double.IsInfinity(overlayAvailable.Width)
            ? contentSize.Width
            : overlayAvailable.Width;
        var desiredHeight = double.IsInfinity(overlayAvailable.Height)
            ? contentSize.Height
            : overlayAvailable.Height;

        return new Size(
            Math.Max(0, desiredWidth + marginWidth),
            Math.Max(0, desiredHeight + marginHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Width/Height and Min/Max on ContentDialog constrain PART_DialogCard; the
        // ContentDialog itself remains the modal overlay and must cover the entire
        // host viewport. FrameworkElement.ArrangeCore is intentionally sealed for
        // WPF parity, so preserve the overlay-specific contract at this virtual hook.
        var viewport = ResolveViewportSize();
        var overlaySize = viewport.Width > 0 && viewport.Height > 0
            ? viewport
            : finalSize;

        base.ArrangeOverride(overlaySize);
        return overlaySize;
    }

    public override void OnApplyTemplate()
    {
        DetachButtonHandlers();
        base.OnApplyTemplate();

        _titleHost = GetTemplateChild("PART_TitleHost") as FrameworkElement;
        _buttonPanel = GetTemplateChild("PART_ButtonPanel") as FrameworkElement;
        _dialogCard = GetTemplateChild("PART_DialogCard") as Border;
        _contentScrollViewer = GetTemplateChild("PART_ContentScrollViewer") as ScrollViewer;
        _primaryButton = GetTemplateChild("PART_PrimaryButton") as Button;
        _secondaryButton = GetTemplateChild("PART_SecondaryButton") as Button;
        _closeButton = GetTemplateChild("PART_CloseButton") as Button;

        AttachButtonHandlers();
        UpdateVisualState();
        UpdateCardLayout();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == WidthProperty ||
            e.Property == HeightProperty ||
            e.Property == MinWidthProperty ||
            e.Property == MinHeightProperty ||
            e.Property == MaxWidthProperty ||
            e.Property == MaxHeightProperty)
        {
            UpdateCardLayout();
        }
    }

    private static void OnDialogVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentDialog dialog)
        {
            dialog.UpdateVisualState();
        }
    }

    private static void OnButtonConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentDialog dialog)
        {
            dialog.UpdateButtonState();
        }
    }

    private static void OnButtonCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentDialog dialog)
        {
            return;
        }

        if (e.OldValue is ICommand oldCommand)
        {
            oldCommand.CanExecuteChanged -= dialog.OnButtonCommandCanExecuteChanged;
        }

        if (e.NewValue is ICommand newCommand)
        {
            newCommand.CanExecuteChanged += dialog.OnButtonCommandCanExecuteChanged;
        }

        dialog.UpdateButtonState();
    }

    private void OnButtonCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdateButtonState();
    }

    private void AttachButtonHandlers()
    {
        if (_primaryButton != null)
        {
            _primaryButton.Click += OnPrimaryButtonClicked;
        }

        if (_secondaryButton != null)
        {
            _secondaryButton.Click += OnSecondaryButtonClicked;
        }

        if (_closeButton != null)
        {
            _closeButton.Click += OnCloseButtonClicked;
        }
    }

    private void DetachButtonHandlers()
    {
        if (_primaryButton != null)
        {
            _primaryButton.Click -= OnPrimaryButtonClicked;
        }

        if (_secondaryButton != null)
        {
            _secondaryButton.Click -= OnSecondaryButtonClicked;
        }

        if (_closeButton != null)
        {
            _closeButton.Click -= OnCloseButtonClicked;
        }
    }

    private async void OnPrimaryButtonClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RequestCloseAsync(ContentDialogButton.Primary, ContentDialogResult.Primary, raiseButtonClickEvent: true, invokeCommand: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            System.Diagnostics.Debug.WriteLine($"ContentDialog primary button handler failed: {ex}");
        }
    }

    private async void OnSecondaryButtonClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RequestCloseAsync(ContentDialogButton.Secondary, ContentDialogResult.Secondary, raiseButtonClickEvent: true, invokeCommand: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            System.Diagnostics.Debug.WriteLine($"ContentDialog secondary button handler failed: {ex}");
        }
    }

    private async void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RequestCloseAsync(ContentDialogButton.Close, ContentDialogResult.None, raiseButtonClickEvent: true, invokeCommand: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            System.Diagnostics.Debug.WriteLine($"ContentDialog close button handler failed: {ex}");
        }
    }

    private async Task RequestCloseAsync(ContentDialogButton button, ContentDialogResult result, bool raiseButtonClickEvent, bool invokeCommand)
    {
        VerifyAccess();

        if (_showTaskSource == null)
        {
            return;
        }

        if (_closeTask != null)
        {
            await _closeTask;
            return;
        }

        _closeTask = RequestCloseCoreAsync(button, result, raiseButtonClickEvent, invokeCommand);
        try
        {
            await _closeTask;
        }
        finally
        {
            _closeTask = null;
        }
    }

    private async Task RequestCloseCoreAsync(ContentDialogButton button, ContentDialogResult result, bool raiseButtonClickEvent, bool invokeCommand)
    {
        if (raiseButtonClickEvent)
        {
            var buttonArgs = new ContentDialogButtonClickEventArgs();
            RaiseButtonClickEvent(button, buttonArgs);
            await buttonArgs.WaitForDeferralsAsync();
            if (buttonArgs.Cancel)
            {
                return;
            }

            if (invokeCommand)
            {
                ExecuteAssociatedCommand(button);
            }
        }

        var closingArgs = new ContentDialogClosingEventArgs(result);
        Closing?.Invoke(this, closingArgs);
        await closingArgs.WaitForDeferralsAsync();
        if (closingArgs.Cancel)
        {
            return;
        }

        CompleteClose(result);
    }

    private void RaiseButtonClickEvent(ContentDialogButton button, ContentDialogButtonClickEventArgs args)
    {
        switch (button)
        {
            case ContentDialogButton.Primary:
                PrimaryButtonClick?.Invoke(this, args);
                break;
            case ContentDialogButton.Secondary:
                SecondaryButtonClick?.Invoke(this, args);
                break;
            case ContentDialogButton.Close:
                CloseButtonClick?.Invoke(this, args);
                break;
        }
    }

    private void ExecuteAssociatedCommand(ContentDialogButton button)
    {
        switch (button)
        {
            case ContentDialogButton.Primary:
                if (PrimaryButtonCommand?.CanExecute(PrimaryButtonCommandParameter) == true)
                {
                    PrimaryButtonCommand.Execute(PrimaryButtonCommandParameter);
                }
                break;
            case ContentDialogButton.Secondary:
                if (SecondaryButtonCommand?.CanExecute(SecondaryButtonCommandParameter) == true)
                {
                    SecondaryButtonCommand.Execute(SecondaryButtonCommandParameter);
                }
                break;
            case ContentDialogButton.Close:
                if (CloseButtonCommand?.CanExecute(CloseButtonCommandParameter) == true)
                {
                    CloseButtonCommand.Execute(CloseButtonCommandParameter);
                }
                break;
        }
    }

    private void CompleteClose(ContentDialogResult result)
    {
        var showTaskSource = _showTaskSource;
        if (showTaskSource == null)
        {
            return;
        }

        _showTaskSource = null;

        var hostWindow = _hostWindow;
        if (_activePlacement == ContentDialogPlacement.InPlace)
        {
            hostWindow?.ActiveInPlaceDialogs.Remove(this);
        }
        else if (hostWindow?.ActiveContentDialog == this)
        {
            hostWindow.ActiveContentDialog = null;
        }

        if (_activePlacement == ContentDialogPlacement.Popup || _activePlacement == ContentDialogPlacement.UnconstrainedPopup)
        {
            DetachFromPopupOverlay();
        }

        RestoreInPlaceSiblings();
        Visibility = Visibility.Collapsed;
        _hostWindow = null;
        _activePlacement = ContentDialogPlacement.Popup;

        Closed?.Invoke(this, new ContentDialogClosedEventArgs(result));
        showTaskSource.TrySetResult(result);
    }

    private void AttachToPopupOverlay(Window hostWindow)
    {
        _popupHost ??= new ContentDialogOverlayHost();
        _popupHost.Child = this;
        UpdatePopupHostViewport(hostWindow);
        hostWindow.SizeChanged -= OnHostWindowSizeChanged;
        hostWindow.SizeChanged += OnHostWindowSizeChanged;
        Visibility = Visibility.Visible;
        Canvas.SetLeft(_popupHost, 0);
        Canvas.SetTop(_popupHost, 0);
        hostWindow.OverlayLayer.AddModalRoot(_popupHost);
        hostWindow.RequestFullInvalidation();
        hostWindow.InvalidateWindow();
    }

    private void DetachFromPopupOverlay()
    {
        if (_hostWindow != null)
        {
            _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
        }

        if (_popupHost == null)
        {
            return;
        }

        _popupHost.Child = null;
        _hostWindow?.OverlayLayer.RemoveModalRoot(_popupHost);
        _hostWindow?.RequestFullInvalidation();
        _hostWindow?.InvalidateWindow();
    }

    private void OnHostWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_hostWindow == null)
        {
            return;
        }

        UpdatePopupHostViewport(_hostWindow);
    }

    private void UpdatePopupHostViewport(Window hostWindow)
    {
        if (_popupHost == null)
        {
            return;
        }

        // The host must never carry an explicit Width/Height: those would win over the
        // constraint OverlayLayer passes down and freeze the overlay at whatever size was
        // copied in last. The modal size comes from the layout pass; FallbackViewport only
        // covers a host that measures with an infinite constraint.
        _popupHost.Width = double.NaN;
        _popupHost.Height = double.NaN;
        _popupHost.FallbackViewport = ResolveHostWindowViewport(hostWindow);
        _popupHost.InvalidateMeasure();
        _popupHost.InvalidateArrange();
        UpdateCardLayout();
    }

    private Window? ResolveHostWindow(ContentDialogPlacement placement)
    {
        if (placement == ContentDialogPlacement.InPlace)
        {
            return FindWindowAncestor(this);
        }

        if (placement == ContentDialogPlacement.UnconstrainedPopup)
        {
            return FindWindowAncestor(this) ?? DialogOwnerResolver.ResolveWindow();
        }

        return FindWindowAncestor(this) ?? DialogOwnerResolver.ResolveWindow();
    }

    private static Window? FindWindowAncestor(Visual? start)
    {
        for (var current = start; current != null; current = current.VisualParent)
        {
            if (current is Window window)
            {
                return window;
            }
        }

        return null;
    }

    private void UpdateVisualState()
    {
        UpdateTitleState();
        UpdateButtonState();
        UpdateBackdropState();
        UpdateCardLayout();
    }

    private void UpdateBackdropState()
    {
        if (_dialogCard == null)
        {
            return;
        }

        _dialogCard.BackdropEffect = BackdropEffect;
    }

    private void UpdateTitleState()
    {
        if (_titleHost == null)
        {
            return;
        }

        _titleHost.Visibility = HasVisibleTitle() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtonState()
    {
        if (_primaryButton != null)
        {
            bool visible = HasVisibleText(PrimaryButtonText);
            _primaryButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _primaryButton.IsDefault = visible && DefaultButton == ContentDialogButton.Primary;
            _primaryButton.IsCancel = false;
            _primaryButton.IsEnabled = visible && IsPrimaryButtonEnabled && CanExecute(PrimaryButtonCommand, PrimaryButtonCommandParameter);
            _primaryButton.Style = PrimaryButtonStyle ?? ResolveDefaultButtonStyle(ContentDialogButton.Primary);
        }

        if (_secondaryButton != null)
        {
            bool visible = HasVisibleText(SecondaryButtonText);
            _secondaryButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _secondaryButton.IsDefault = visible && DefaultButton == ContentDialogButton.Secondary;
            _secondaryButton.IsCancel = false;
            _secondaryButton.IsEnabled = visible && IsSecondaryButtonEnabled && CanExecute(SecondaryButtonCommand, SecondaryButtonCommandParameter);
            _secondaryButton.Style = SecondaryButtonStyle ?? ResolveDefaultButtonStyle(ContentDialogButton.Secondary);
        }

        if (_closeButton != null)
        {
            bool visible = HasVisibleText(CloseButtonText);
            _closeButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _closeButton.IsDefault = visible && DefaultButton == ContentDialogButton.Close;
            _closeButton.IsCancel = visible;
            _closeButton.IsEnabled = visible && CanExecute(CloseButtonCommand, CloseButtonCommandParameter);
            _closeButton.Style = CloseButtonStyle ?? ResolveDefaultButtonStyle(ContentDialogButton.Close);
        }

        if (_buttonPanel != null)
        {
            bool hasButtons =
                _primaryButton?.Visibility == Visibility.Visible ||
                _secondaryButton?.Visibility == Visibility.Visible ||
                _closeButton?.Visibility == Visibility.Visible;
            _buttonPanel.Visibility = hasButtons ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Style? ResolveDefaultButtonStyle(ContentDialogButton button)
    {
        if (DefaultButton != button)
        {
            return null;
        }

        return TryFindResource("ContentDialogAccentButtonStyle") as Style;
    }

    private static bool CanExecute(ICommand? command, object? parameter)
    {
        return command?.CanExecute(parameter) ?? true;
    }

    private bool HasVisibleTitle()
    {
        return Title switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    private static bool HasVisibleText(string? text)
    {
        return !string.IsNullOrWhiteSpace(text);
    }

    private void UpdateCardLayout()
    {
        if (_dialogCard == null)
        {
            return;
        }

        _dialogCard.Margin = new Thickness(DefaultDialogMargin);

        if (FullSizeDesired)
        {
            _dialogCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            _dialogCard.VerticalAlignment = VerticalAlignment.Stretch;
            _dialogCard.Width = double.NaN;
            _dialogCard.Height = double.NaN;
            _dialogCard.MinWidth = 0;
            _dialogCard.MinHeight = 0;
            _dialogCard.MaxWidth = double.PositiveInfinity;
            _dialogCard.MaxHeight = double.PositiveInfinity;
            return;
        }

        _dialogCard.HorizontalAlignment = HorizontalAlignment.Center;
        _dialogCard.VerticalAlignment = VerticalAlignment.Center;

        var viewport = ResolveViewportSize();
        var availableCardWidth = viewport.Width > 0
            ? Math.Max(0, viewport.Width - (DefaultDialogMargin * 2))
            : double.PositiveInfinity;
        var availableCardHeight = viewport.Height > 0
            ? Math.Max(0, viewport.Height - (DefaultDialogMargin * 2))
            : double.PositiveInfinity;

        var explicitWidth = NormalizeExplicitLength(Width);
        var explicitHeight = NormalizeExplicitLength(Height);
        var hasExplicitWidth = !double.IsNaN(explicitWidth);
        var hasExplicitHeight = !double.IsNaN(explicitHeight);

        _dialogCard.Width = hasExplicitWidth ? explicitWidth : double.NaN;
        _dialogCard.Height = hasExplicitHeight ? explicitHeight : double.NaN;

        // 关键修复：仅在用户显式在 <ContentDialog> 上设置 Min/Max 尺寸时才下推到 PART_DialogCard，
        // 否则尊重模板里声明的默认值（Themes/Controls/Dialogs.jalxaml 里 Card 自带 MinWidth=320 /
        // MaxWidth=560）。早期实现无条件把 MaxWidth/MaxHeight 设为"viewport-48"，导致 Card 被
        // 无上限拉到接近整个窗口宽，视觉上像撑满 viewport — 这是设计 bug 不是预期行为。
        //
        // 当 viewport 比模板上限还小时，Card 的 measure 阶段会自然受 parent availableSize 限制，
        // 不会真的渲染到 560 宽，所以无需在这里手动 clamp 到 viewport。ClipToBounds=true 仅作为
        // 兜底防御。
        if (MinWidth > 0)
        {
            _dialogCard.MinWidth = MinWidth;
        }
        if (MinHeight > 0)
        {
            _dialogCard.MinHeight = MinHeight;
        }
        // The template's 560px maximum is a compact default, not a hard cap:
        // explicit dialog Width and naturally wider content may grow up to the
        // host viewport. A caller-supplied MaxWidth remains authoritative.
        _dialogCard.MaxWidth = HasExplicitMaximum(MaxWidth)
            ? ClampMaximumToAvailable(MaxWidth, availableCardWidth)
            : availableCardWidth;
        if (HasExplicitMaximum(MaxHeight))
        {
            _dialogCard.MaxHeight = ClampMaximumToAvailable(MaxHeight, availableCardHeight);
        }

        // 内容滚动区封顶：把 PART_ContentScrollViewer 的 MaxHeight 限到"可用窗口高 − 标题/按钮/内边距"。
        // 这样任意高内容（如分类很多的设置面板、长列表）在对话框内滚动，而不会把卡片撑出屏幕；
        // 内容不超高时该 MaxHeight 触不到，卡片照旧贴合内容（短对话框依旧紧凑、不留空白）。
        // 不直接给 Card 设 MaxHeight —— 那会让 Card 的 '*' 内容行被有限高填满、反而对短内容留白（既有注释所述）。
        if (_contentScrollViewer != null)
        {
            if (double.IsInfinity(availableCardHeight) || availableCardHeight <= 0)
            {
                _contentScrollViewer.MaxHeight = double.PositiveInfinity;
            }
            else
            {
                var chrome = (_titleHost?.ActualHeight ?? 0) + (_buttonPanel?.ActualHeight ?? 0) + 56;
                _contentScrollViewer.MaxHeight = Math.Max(160, availableCardHeight - chrome);
            }
        }

        _dialogCard.ClipToBounds = true;
    }

    private void OnDialogSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCardLayout();
        RequestHostFullInvalidation();
    }

    private void RequestHostFullInvalidation()
    {
        if (_showTaskSource == null || _hostWindow == null)
        {
            return;
        }

        _hostWindow.RequestFullInvalidation();
        _hostWindow.InvalidateWindow();
    }

    private bool IsPopupHosted =>
        _activePlacement != ContentDialogPlacement.InPlace &&
        _hostWindow != null &&
        ReferenceEquals(_popupHost?.Child, this);

    private Size ResolveViewportSize()
    {
        // 模态弹层：viewport 恒等于宿主窗口客户区，取"当前"值 —— Window.Width/Height 在 WM_SIZE
        // 里就地写入，而 ActualWidth/ActualHeight 要等下一轮 Arrange 才回写。SizeChanged 是从
        // WM_SIZE 直接 raise 的，处理器里读 ActualWidth 拿到的是上一次布局的尺寸；模态遮罩照抄它
        // 就会永远落后一次 resize —— 最大化后遮罩仍是还原尺寸、还原后仍是最大化尺寸，居中的卡片
        // 跟着整块偏移。这就是"最大化 / 还原都会位移"的根因。
        if (IsPopupHosted)
        {
            var windowViewport = ResolveHostWindowViewport(_hostWindow!);
            if (windowViewport.Width > 0 && windowViewport.Height > 0)
            {
                return windowViewport;
            }
        }

        // 布局最近一次分配的可用区。InPlace 模式下它才是正确的 viewport（对话框嵌在某个区域里，
        // 不该按整窗算），且它同样早于 ActualWidth 更新。
        if (IsUsableViewport(_layoutViewport))
        {
            return _layoutViewport;
        }

        double width = ActualWidth > 0 ? ActualWidth : (_popupHost?.ActualWidth ?? 0);
        double height = ActualHeight > 0 ? ActualHeight : (_popupHost?.ActualHeight ?? 0);
        if (width <= 0 || height <= 0)
        {
            var fallback = _hostWindow != null ? ResolveHostWindowViewport(_hostWindow) : default;
            if (width <= 0) width = fallback.Width;
            if (height <= 0) height = fallback.Height;
        }

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private Size ResolveHostWindowViewport(Window hostWindow)
    {
        var margin = Margin;
        var width = SelectViewportLength(hostWindow.Width, hostWindow.ActualWidth) - (margin.Left + margin.Right);
        var height = SelectViewportLength(hostWindow.Height, hostWindow.ActualHeight) - (margin.Top + margin.Bottom);
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private static double SelectViewportLength(double liveLength, double arrangedLength)
    {
        if (double.IsFinite(liveLength) && liveLength > 0)
        {
            return liveLength;
        }

        return double.IsFinite(arrangedLength) && arrangedLength > 0 ? arrangedLength : 0;
    }

    private static bool IsUsableViewport(Size viewport)
    {
        return double.IsFinite(viewport.Width) && viewport.Width > 0 &&
               double.IsFinite(viewport.Height) && viewport.Height > 0;
    }

    private static double NormalizeExplicitLength(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return double.NaN;
        }

        return Math.Max(0, value);
    }

    private static bool HasExplicitMaximum(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    private static double ClampMaximumToAvailable(double requestedMaximum, double availableMaximum)
    {
        if (double.IsNaN(availableMaximum) || double.IsInfinity(availableMaximum))
        {
            return requestedMaximum;
        }

        return double.IsInfinity(requestedMaximum)
            ? availableMaximum
            : Math.Min(requestedMaximum, availableMaximum);
    }

    private static double ClampMinimumToMaximum(double requestedMinimum, double effectiveMaximum)
    {
        var minimum = Math.Max(0, requestedMinimum);
        if (double.IsInfinity(effectiveMaximum))
        {
            return minimum;
        }

        return Math.Min(minimum, Math.Max(0, effectiveMaximum));
    }

    private static double SnapDialogLayoutValue(double value)
    {
        // Pixel snapping is disabled: the dialog-overlay origin passes through as
        // continuous floating-point, consistent with FrameworkElement.SnapLayoutValue.
        return double.IsFinite(value) ? value : 0;
    }

    private void ScheduleInitialFocus()
    {
        Dispatcher.BeginInvokeCritical(() =>
        {
            if (_showTaskSource == null)
            {
                return;
            }

            ResolveInitialFocusElement()?.Focus();
        });
    }

    private UIElement? ResolveInitialFocusElement()
    {
        if (_primaryButton is { Visibility: Visibility.Visible, IsEnabled: true } && DefaultButton == ContentDialogButton.Primary)
        {
            return _primaryButton;
        }

        if (_secondaryButton is { Visibility: Visibility.Visible, IsEnabled: true } && DefaultButton == ContentDialogButton.Secondary)
        {
            return _secondaryButton;
        }

        if (_closeButton is { Visibility: Visibility.Visible, IsEnabled: true } && DefaultButton == ContentDialogButton.Close)
        {
            return _closeButton;
        }

        var focusableElements = new List<UIElement>();
        CollectFocusableElements(this, focusableElements);
        foreach (var element in focusableElements)
        {
            if (!ReferenceEquals(element, this))
            {
                return element;
            }
        }

        return this;
    }

    private void OnDialogKeyDown(object sender, KeyEventArgs e)
    {
        if (_showTaskSource == null || e.Key != Key.Tab)
        {
            return;
        }

        var focusableElements = new List<UIElement>();
        CollectFocusableElements(this, focusableElements);
        focusableElements.RemoveAll(element => ReferenceEquals(element, this));
        if (focusableElements.Count == 0)
        {
            Focus();
            e.Handled = true;
            return;
        }

        var current = Keyboard.FocusedElement as UIElement;
        int currentIndex = current != null ? focusableElements.IndexOf(current) : -1;
        int nextIndex;
        if (e.IsShiftDown)
        {
            nextIndex = currentIndex <= 0 ? focusableElements.Count - 1 : currentIndex - 1;
        }
        else
        {
            nextIndex = currentIndex < 0 || currentIndex == focusableElements.Count - 1 ? 0 : currentIndex + 1;
        }

        focusableElements[nextIndex].Focus();
        e.Handled = true;
    }

    private static void CollectFocusableElements(UIElement root, List<UIElement> results)
    {
        if (!root.IsEnabled || root.Visibility != Visibility.Visible)
        {
            return;
        }

        if (root.Focusable && KeyboardNavigation.GetIsTabStop(root))
        {
            results.Add(root);
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                CollectFocusableElements(child, results);
            }
        }
    }

    private static void PrepareDialogSubtree(UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            fe.ApplyImplicitStyleIfNeeded();
            fe.ReactivateBindings();
        }

        if (element is Control control)
        {
            control.ApplyTemplate();
        }

        for (int i = 0; i < element.VisualChildrenCount; i++)
        {
            if (element.GetVisualChild(i) is UIElement child)
            {
                PrepareDialogSubtree(child);
            }
        }
    }

    private static void InvalidateSubtree(UIElement element)
    {
        element.InvalidateMeasure();
        element.InvalidateArrange();
        for (int i = 0; i < element.VisualChildrenCount; i++)
        {
            if (element.GetVisualChild(i) is UIElement child)
            {
                InvalidateSubtree(child);
            }
        }
    }

    private void DisableInPlaceSiblings()
    {
        _disabledSiblings.Clear();
        var parent = VisualParent;
        if (parent == null)
        {
            return;
        }

        for (int i = 0; i < parent.VisualChildrenCount; i++)
        {
            if (parent.GetVisualChild(i) is UIElement sibling && !ReferenceEquals(sibling, this))
            {
                _disabledSiblings.Add((sibling, sibling.IsEnabled));
                sibling.IsEnabled = false;
            }
        }
    }

    private void RestoreInPlaceSiblings()
    {
        foreach (var (element, wasEnabled) in _disabledSiblings)
        {
            element.IsEnabled = wasEnabled;
        }

        _disabledSiblings.Clear();
    }
}

public sealed class ContentDialogOpenedEventArgs : EventArgs
{
}

public sealed class ContentDialogClosedEventArgs : EventArgs
{
    public ContentDialogClosedEventArgs(ContentDialogResult result)
    {
        Result = result;
    }

    public ContentDialogResult Result { get; }
}

public sealed class ContentDialogButtonClickEventArgs : ContentDialogDeferrableEventArgs
{
    public bool Cancel { get; set; }
}

public sealed class ContentDialogClosingEventArgs : ContentDialogDeferrableEventArgs
{
    public ContentDialogClosingEventArgs(ContentDialogResult result)
    {
        Result = result;
    }

    public bool Cancel { get; set; }
    public ContentDialogResult Result { get; }
}

public sealed class ContentDialogDeferral
{
    private ContentDialogDeferralManager? _manager;

    internal ContentDialogDeferral(ContentDialogDeferralManager manager)
    {
        _manager = manager;
    }

    public void Complete()
    {
        Interlocked.Exchange(ref _manager, null)?.Complete();
    }
}

public abstract class ContentDialogDeferrableEventArgs : EventArgs
{
    private readonly ContentDialogDeferralManager _deferralManager = new();

    public ContentDialogDeferral GetDeferral()
    {
        return _deferralManager.GetDeferral();
    }

    internal Task WaitForDeferralsAsync()
    {
        return _deferralManager.WaitForDeferralsAsync();
    }
}

internal sealed class ContentDialogDeferralManager
{
    private readonly TaskCompletionSource<bool> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _outstandingDeferrals;

    public ContentDialogDeferral GetDeferral()
    {
        Interlocked.Increment(ref _outstandingDeferrals);
        return new ContentDialogDeferral(this);
    }

    public Task WaitForDeferralsAsync()
    {
        return Volatile.Read(ref _outstandingDeferrals) == 0
            ? Task.CompletedTask
            : _completionSource.Task;
    }

    internal void Complete()
    {
        if (Interlocked.Decrement(ref _outstandingDeferrals) == 0)
        {
            _completionSource.TrySetResult(true);
        }
    }
}

internal sealed class ContentDialogOverlayHost : FrameworkElement
{
    private UIElement? _child;

    /// <summary>
    /// Viewport used only when this host is measured with an infinite constraint.
    /// OverlayLayer measures modal roots against the live window client size, so the
    /// bounded path is authoritative and never lags a resize; this is the defensive
    /// fallback for a host that cannot supply one.
    /// </summary>
    public Size FallbackViewport { get; set; }

    public UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            var oldChild = _child;
            if (oldChild != null)
            {
                // Clear the authoritative child slot first. RemoveVisualChild updates
                // DependencyObject's compatibility count from this override while it
                // raises parent-change teardown; leaving _child set until afterwards
                // publishes a stale count of one and later makes layout-manager cleanup
                // call GetVisualChild(0) on an empty host.
                _child = null;
                RemoveVisualChild(oldChild);
            }

            _child = value;

            if (_child != null)
            {
                AddVisualChild(_child);
            }

            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    protected override int VisualChildrenCount => _child != null ? 1 : 0;

    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0 || _child == null)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _child;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : FallbackViewport.Width;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : FallbackViewport.Height;
        var hostSize = new Size(Math.Max(0, width), Math.Max(0, height));

        _child?.Measure(hostSize);
        return hostSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _child?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
