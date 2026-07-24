using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Navigation;

namespace Jalium.UI.Controls;

/// <summary>Represents a WPF-compatible navigation page.</summary>
[ContentProperty(nameof(Content))]
public class Page : FrameworkElement, IAddChild, IWindowService
{
    public static readonly DependencyProperty BackgroundProperty =
        Control.BackgroundProperty.AddOwner(
            typeof(Page),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty ContentProperty =
        ContentControl.ContentProperty.AddOwner(
            typeof(Page),
            new PropertyMetadata(null, OnContentPropertyChanged));

    public static readonly DependencyProperty FontFamilyProperty =
        Control.FontFamilyProperty.AddOwner(
            typeof(Page),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.Inherits,
                OnLayoutPropertyChanged));

    public static readonly DependencyProperty FontSizeProperty =
        Control.FontSizeProperty.AddOwner(
            typeof(Page),
            new PropertyMetadata(14.0, OnLayoutPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty ForegroundProperty =
        Control.ForegroundProperty.AddOwner(
            typeof(Page),
            new PropertyMetadata(
                new SolidColorBrush(Themes.ThemeColors.TextPrimary),
                OnVisualPropertyChanged,
                null,
                inherits: true));

    public static readonly DependencyProperty KeepAliveProperty =
        JournalEntry.KeepAliveProperty.AddOwner(typeof(Page));

    public static readonly DependencyProperty TemplateProperty =
        Control.TemplateProperty.AddOwner(
            typeof(Page),
            new PropertyMetadata(null, OnTemplatePropertyChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(Page),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyProperty NavigationCacheModeProperty =
        DependencyProperty.Register(
            nameof(NavigationCacheMode),
            typeof(NavigationCacheMode),
            typeof(Page),
            new PropertyMetadata(NavigationCacheMode.Disabled));

    private UIElement? _contentVisual;
    private FrameworkElement? _templateRoot;
    private IList<TriggerBase>? _appliedTemplateTriggers;
    private bool _templateApplied;
    private bool _showsNavigationUI = true;
    private bool _showsNavigationUISet;
    private double _windowHeight = double.NaN;
    private double _windowWidth = double.NaN;
    private string _windowTitle = string.Empty;
    private bool _windowHeightSet;
    private bool _windowWidthSet;
    private bool _windowTitleSet;

    public Page()
    {
        Loaded += (_, _) => ApplyWindowServiceValues();
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty) ?? SystemFonts.MessageFontFamily;
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)(GetValue(FontSizeProperty) ?? 14.0);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public bool KeepAlive
    {
        get => (bool)(GetValue(KeepAliveProperty) ?? false);
        set => SetValue(KeepAliveProperty, value);
    }

    public NavigationService? NavigationService =>
        Frame?.NavigationService ?? Jalium.UI.Navigation.NavigationService.GetNavigationService(this);

    public bool ShowsNavigationUI
    {
        get => _showsNavigationUI;
        set
        {
            _showsNavigationUI = value;
            _showsNavigationUISet = true;
            if (Window.GetWindow(this) is NavigationWindow navigationWindow)
                navigationWindow.ShowsNavigationUI = value;
        }
    }

    public ControlTemplate? Template
    {
        get => (ControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    public double WindowHeight
    {
        get => _windowHeightSet ? _windowHeight : Window.GetWindow(this)?.Height ?? double.NaN;
        set
        {
            ValidateWindowDimension(value, nameof(value));
            _windowHeight = value;
            _windowHeightSet = true;
            if (Window.GetWindow(this) is { } window)
                window.Height = value;
        }
    }

    public string WindowTitle
    {
        get => _windowTitleSet ? _windowTitle : Window.GetWindow(this)?.Title ?? string.Empty;
        set
        {
            _windowTitle = value ?? string.Empty;
            _windowTitleSet = true;
            if (Window.GetWindow(this) is { } window)
                window.Title = _windowTitle;
        }
    }

    public double WindowWidth
    {
        get => _windowWidthSet ? _windowWidth : Window.GetWindow(this)?.Width ?? double.NaN;
        set
        {
            ValidateWindowDimension(value, nameof(value));
            _windowWidth = value;
            _windowWidthSet = true;
            if (Window.GetWindow(this) is { } window)
                window.Width = value;
        }
    }

    internal NavigationCacheMode NavigationCacheMode
    {
        get => (NavigationCacheMode)(GetValue(NavigationCacheModeProperty) ?? NavigationCacheMode.Disabled);
        set => SetValue(NavigationCacheModeProperty, value);
    }

    internal Frame? Frame { get; set; }

    internal object? NavigationParameter { get; set; }

    internal event EventHandler<NavigationEventArgs>? NavigatedTo;
    internal event EventHandler<NavigationEventArgs>? NavigatedFrom;
    internal event EventHandler<NavigatingCancelEventArgs>? NavigatingFrom;

    internal void OnNavigatedTo(NavigationEventArgs e) => NavigatedTo?.Invoke(this, e);
    internal void OnNavigatedFrom(NavigationEventArgs e) => NavigatedFrom?.Invoke(this, e);
    internal void OnNavigatingFrom(NavigatingCancelEventArgs e) => NavigatingFrom?.Invoke(this, e);

    public bool ShouldSerializeShowsNavigationUI() => _showsNavigationUISet;
    public bool ShouldSerializeTitle() => HasLocalValue(TitleProperty);
    public bool ShouldSerializeWindowHeight() => _windowHeightSet;
    public bool ShouldSerializeWindowTitle() => _windowTitleSet;
    public bool ShouldSerializeWindowWidth() => _windowWidthSet;

    protected override bool ApplyTemplateCore()
    {
        if (_templateApplied)
            return false;

        RebuildVisualTree();
        return _templateApplied;
    }

    protected override int VisualChildrenCount =>
        _templateRoot is not null || _contentVisual is not null ? 1 : 0;

    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _templateRoot ?? _contentVisual
            ?? throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ApplyTemplate();
        UIElement? child = _templateRoot ?? _contentVisual;
        child?.Measure(availableSize);
        return child?.DesiredSize ?? default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElement? child = _templateRoot ?? _contentVisual;
        child?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Background is { } background && RenderSize.Width > 0 && RenderSize.Height > 0)
            drawingContext.DrawRectangle(background, null, new Rect(RenderSize));
    }

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Content is not null)
            throw new InvalidOperationException("Page can contain only one logical child.");
        Content = value;
    }

    void IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Content is null)
            Content = text;
        else if (Content is string existing)
            Content = existing + text;
        else
            throw new InvalidOperationException("Text cannot be added after object content.");
    }

    double IWindowService.Height { get => WindowHeight; set => WindowHeight = value; }
    string IWindowService.Title { get => WindowTitle; set => WindowTitle = value; }
    bool IWindowService.UserResized => false;
    double IWindowService.Width { get => WindowWidth; set => WindowWidth = value; }

    private static void OnContentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (Page)d;
        if (!ReferenceEquals(e.OldValue, e.NewValue))
        {
            page.RemoveLogicalChild(e.OldValue);
            page.AddLogicalChild(e.NewValue);
        }
        page.RebuildVisualTree();
    }

    private static void OnTemplatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (Page)d;
        page._templateApplied = false;
        page.RebuildVisualTree();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Page)d).InvalidateVisual();

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (Page)d;
        page.InvalidateMeasure();
        page.InvalidateVisual();
    }

    private void RebuildVisualTree()
    {
        ClearVisualTree();

        if (Template?.LoadContent() is FrameworkElement templateRoot)
        {
            _templateRoot = templateRoot;
            _templateRoot.IsTemplatedRoot = true;
            Control.SetTemplatedParentRecursive(_templateRoot, this);
            AddVisualChild(_templateRoot);
            Control.PromoteTemplateLocalValuesRecursive(_templateRoot);
            Control.ReactivateBindingsRecursive(_templateRoot);

            if (Template.Triggers.Count != 0)
            {
                _appliedTemplateTriggers = Template.Triggers;
                foreach (TriggerBase trigger in _appliedTemplateTriggers)
                {
                    trigger.ParentTemplateTriggers = _appliedTemplateTriggers;
                    trigger.Attach(this);
                }
            }

            _templateApplied = true;
            OnApplyTemplate();
        }
        else
        {
            _contentVisual = CreateDirectContentVisual(Content);
            if (_contentVisual is not null)
                AddVisualChild(_contentVisual);
            _templateApplied = true;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ClearVisualTree()
    {
        if (_appliedTemplateTriggers is not null)
        {
            foreach (TriggerBase trigger in _appliedTemplateTriggers)
            {
                trigger.Detach(this);
                trigger.ParentTemplateTriggers = null;
            }
            _appliedTemplateTriggers = null;
        }

        if (_templateRoot is not null)
        {
            var root = _templateRoot;
            _templateRoot = null;
            root.IsTemplatedRoot = false;
            RemoveVisualChild(root);
        }

        if (_contentVisual is not null)
        {
            var visual = _contentVisual;
            _contentVisual = null;
            RemoveVisualChild(visual);
        }
    }

    private UIElement? CreateDirectContentVisual(object? content)
    {
        if (content is UIElement element)
            return element;
        if (content is null)
            return null;

        return new TextBlock
        {
            Text = content.ToString() ?? string.Empty,
            FontFamily = FontFamily,
            FontSize = FontSize,
            Foreground = Foreground,
        };
    }

    private void ApplyWindowServiceValues()
    {
        if (Window.GetWindow(this) is not { } window)
            return;

        if (_windowHeightSet)
            window.Height = _windowHeight;
        if (_windowWidthSet)
            window.Width = _windowWidth;
        if (_windowTitleSet)
            window.Title = _windowTitle;
        if (_showsNavigationUISet && window is NavigationWindow navigationWindow)
            navigationWindow.ShowsNavigationUI = _showsNavigationUI;
    }

    private static void ValidateWindowDimension(double value, string parameterName)
    {
        if ((!double.IsNaN(value) && (!double.IsFinite(value) || value < 0)))
            throw new ArgumentException("The window dimension must be NaN or a finite non-negative value.", parameterName);
    }
}

internal enum NavigationCacheMode
{
    Disabled,
    Enabled,
    Required,
}
