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
[ContentProperty("Content")]
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
    private Button? _primaryButton;
    private Button? _secondaryButton;
    private Button? _closeButton;
    private ContentDialogOverlayHost? _popupHost;
    private Window? _hostWindow;
    private TaskCompletionSource<ContentDialogResult>? _showTaskSource;
    private Task? _closeTask;
    private ContentDialogPlacement _activePlacement;
    private Size _previousOverlayRenderSize;
    private readonly List<(UIElement Element, bool WasEnabled)> _disabledSiblings = new();

    public ContentDialog()
    {
        UseTemplateContentManagement();
        Visibility = Visibility.Collapsed;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        BackdropEffect = new BlurEffect(18f);
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

    protected override Size ArrangeCore(Rect finalRect)
    {
        var margin = Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        var overlaySize = new Size(
            Math.Max(0, finalRect.Width - marginWidth),
            Math.Max(0, finalRect.Height - marginHeight));

        ArrangeOverride(overlaySize);

        SetVisualBounds(new Rect(
            SnapDialogLayoutValue(finalRect.X + margin.Left),
            SnapDialogLayoutValue(finalRect.Y + margin.Top),
            overlaySize.Width,
            overlaySize.Height));

        // Keep the overlay render size authoritative so popup sizing does not
        // inherit card-level Width/Height constraints.
        _renderSize = overlaySize;

        if (overlaySize != _previousOverlayRenderSize)
        {
            var widthChanged = overlaySize.Width != _previousOverlayRenderSize.Width;
            var heightChanged = overlaySize.Height != _previousOverlayRenderSize.Height;
            var sizeInfo = new SizeChangedInfo(this, _previousOverlayRenderSize, widthChanged, heightChanged);
            _previousOverlayRenderSize = overlaySize;
            OnSizeChanged(sizeInfo);
        }

        return overlaySize;
    }

    protected override void OnApplyTemplate()
    {
        DetachButtonHandlers();
        base.OnApplyTemplate();

        _titleHost = GetTemplateChild("PART_TitleHost") as FrameworkElement;
        _buttonPanel = GetTemplateChild("PART_ButtonPanel") as FrameworkElement;
        _dialogCard = GetTemplateChild("PART_DialogCard") as Border;
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
        UpdatePopupHostSize(hostWindow);
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

        UpdatePopupHostSize(_hostWindow);
    }

    private void UpdatePopupHostSize(Window hostWindow)
    {
        if (_popupHost == null)
        {
            return;
        }

        var width = hostWindow.ActualWidth > 0 ? hostWindow.ActualWidth : hostWindow.Width;
        var height = hostWindow.ActualHeight > 0 ? hostWindow.ActualHeight : hostWindow.Height;
        _popupHost.Width = Math.Max(0, width);
        _popupHost.Height = Math.Max(0, height);
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
        var hasExplicitMinWidth = MinWidth > 0;
        var hasExplicitMinHeight = MinHeight > 0;
        var hasExplicitMaxWidth = HasExplicitMaximum(MaxWidth);
        var hasExplicitMaxHeight = HasExplicitMaximum(MaxHeight);

        var requestedMinWidth = hasExplicitMinWidth
            ? MinWidth
            : hasExplicitWidth ? 0.0 : DefaultDialogCardMinWidth;
        var requestedMinHeight = hasExplicitMinHeight ? MinHeight : 0.0;
        var requestedMaxWidth = hasExplicitMaxWidth
            ? MaxWidth
            : double.PositiveInfinity;
        var requestedMaxHeight = hasExplicitMaxHeight ? MaxHeight : double.PositiveInfinity;

        var effectiveMaxWidth = ClampMaximumToAvailable(requestedMaxWidth, availableCardWidth);
        var effectiveMaxHeight = ClampMaximumToAvailable(requestedMaxHeight, availableCardHeight);

        _dialogCard.Width = hasExplicitWidth ? explicitWidth : double.NaN;
        _dialogCard.Height = hasExplicitHeight ? explicitHeight : double.NaN;
        _dialogCard.MinWidth = ClampMinimumToMaximum(requestedMinWidth, effectiveMaxWidth);
        _dialogCard.MinHeight = ClampMinimumToMaximum(requestedMinHeight, effectiveMaxHeight);
        _dialogCard.MaxWidth = effectiveMaxWidth;
        _dialogCard.MaxHeight = effectiveMaxHeight;
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

    private Size ResolveViewportSize()
    {
        var width = ActualWidth > 0
            ? ActualWidth
            : _popupHost?.Width ?? (_hostWindow?.ActualWidth > 0 ? _hostWindow.ActualWidth : _hostWindow?.Width ?? 0);
        var height = ActualHeight > 0
            ? ActualHeight
            : _popupHost?.Height ?? (_hostWindow?.ActualHeight > 0 ? _hostWindow.ActualHeight : _hostWindow?.Height ?? 0);

        return new Size(Math.Max(0, width), Math.Max(0, height));
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
        if (!double.IsFinite(value))
        {
            return 0;
        }

        return Math.Round(value, MidpointRounding.AwayFromZero);
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

    public UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            if (_child != null)
            {
                RemoveVisualChild(_child);
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

    public override int VisualChildrenCount => _child != null ? 1 : 0;

    public override Visual? GetVisualChild(int index)
    {
        if (index != 0 || _child == null)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _child;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = !double.IsNaN(Width) ? Width : availableSize.Width;
        var height = !double.IsNaN(Height) ? Height : availableSize.Height;
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
