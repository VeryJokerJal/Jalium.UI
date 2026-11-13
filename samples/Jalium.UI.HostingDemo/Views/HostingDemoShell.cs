using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.HostingDemo.Themes;
using Jalium.UI.Media;
using Jalium.UI.Navigation;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>A templateable DI navigation shell with optional navigation and frame parts.</summary>
[TemplatePart(Name = "PART_Navigation", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_Frame", Type = typeof(Frame))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Ready", GroupName = "NavigationStates")]
[TemplateVisualState(Name = "Failed", GroupName = "NavigationStates")]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
public class HostingDemoShell : Control
{
    public sealed record NavigationEntry(string Label, Type PageType);
    private static readonly IReadOnlyList<NavigationEntry> Entries = Array.AsReadOnly(new[]
    {
        new NavigationEntry("首页 · Services / Environment", typeof(HomePage)), new NavigationEntry("MVVM · AddView<T, VM>()", typeof(MvvmPage)),
        new NavigationEntry("Logging · ILogger<T>", typeof(LoggingPage)), new NavigationEntry("Metrics · JaliumMeter", typeof(MetricsPage)),
        new NavigationEntry("Configuration · JaliumRuntimeOptions", typeof(OptionsPage)), new NavigationEntry("IHostedService · BackgroundService", typeof(HostedServicesPage))
    });
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(HostingDemoShell), new PropertyMetadata("AppBuilder 全家桶"));
    public static readonly DependencyProperty SelectedPageTypeProperty = DependencyProperty.Register(nameof(SelectedPageType), typeof(Type), typeof(HostingDemoShell), new PropertyMetadata(typeof(HomePage), OnSelectedPageChanged), value => value is Type type && typeof(Page).IsAssignableFrom(type));
    public static readonly DependencyProperty PageFactoryProperty = DependencyProperty.Register(nameof(PageFactory), typeof(Func<Type, Page>), typeof(HostingDemoShell), new PropertyMetadata(null, OnSelectedPageChanged));
    public static readonly DependencyProperty LoggerProperty = DependencyProperty.Register(nameof(Logger), typeof(ILogger), typeof(HostingDemoShell), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey NavigationItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(NavigationItems), typeof(IReadOnlyList<NavigationEntry>), typeof(HostingDemoShell), new PropertyMetadata(Entries));
    public static readonly DependencyProperty NavigationItemsProperty = NavigationItemsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CurrentPagePropertyKey = DependencyProperty.RegisterReadOnly(nameof(CurrentPage), typeof(Page), typeof(HostingDemoShell), new PropertyMetadata(null));
    public static readonly DependencyProperty CurrentPageProperty = CurrentPagePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey MetricsTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(MetricsText), typeof(string), typeof(HostingDemoShell), new PropertyMetadata("FPS: —"));
    public static readonly DependencyProperty MetricsTextProperty = MetricsTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey NavigationErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(NavigationError), typeof(string), typeof(HostingDemoShell), new PropertyMetadata(""));
    public static readonly DependencyProperty NavigationErrorProperty = NavigationErrorPropertyKey.DependencyProperty;

    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public Type SelectedPageType { get => (Type)GetValue(SelectedPageTypeProperty)!; set => SetValue(SelectedPageTypeProperty, value); }
    public Func<Type, Page?>? PageFactory { get => (Func<Type, Page?>?)GetValue(PageFactoryProperty); set => SetValue(PageFactoryProperty, value); }
    public ILogger? Logger { get => (ILogger?)GetValue(LoggerProperty); set => SetValue(LoggerProperty, value); }
    public IReadOnlyList<NavigationEntry> NavigationItems => (IReadOnlyList<NavigationEntry>)GetValue(NavigationItemsProperty)!;
    public Page? CurrentPage => (Page?)GetValue(CurrentPageProperty);
    public string MetricsText => (string)GetValue(MetricsTextProperty)!;
    public string NavigationError => (string)GetValue(NavigationErrorProperty)!;

    private Frame? _frame;
    private ListBox? _navigation;
    private bool _synchronizing, _sampling;
    public HostingDemoShell()
    {
        HostingDemoTheme.Register(); DefaultStyleKey = typeof(HostingDemoShell);
        SizeChanged += (_, _) => UpdateStates();
        Loaded += (_, _) => { if (!_sampling) { _sampling = true; CompositionTarget.Rendering += OnRendering; } };
        Unloaded += (_, _) => { if (_sampling) { _sampling = false; CompositionTarget.Rendering -= OnRendering; } };
    }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _frame = GetTemplateChild("PART_Frame") as Frame;
        if (_frame is not null) _frame.Navigated += OnNavigated;
        _navigation = GetTemplateChild("PART_Navigation") as ListBox;
        if (_navigation is not null) _navigation.SelectionChanged += OnSelectionChanged;
        NavigateCurrent(); UpdateStates();
    }
    protected override void OnTemplateChanging()
    {
        // Keep the page instance (and its VM) while retiring the old frame tree.
        var frame = _frame; ReleaseParts();
        if (frame is not null) { frame.StopLoading(); frame.SetCurrentValue(ContentControl.ContentProperty, null); }
        base.OnTemplateChanging();
    }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    public void Navigate(Type pageType) => SetCurrentValue(SelectedPageTypeProperty, pageType);
    private static void OnSelectedPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HostingDemoShell)d).NavigateCurrent();
    private void NavigateCurrent()
    {
        if (_synchronizing || _frame is null) return;
        _synchronizing = true;
        try
        {
            Logger?.LogDebug("导航到 {PageType}", SelectedPageType.Name);
            var retained = CurrentPage?.GetType() == SelectedPageType ? CurrentPage : null;
            bool success = retained is not null ? _frame.Navigate(retained)
                : PageFactory is { } factory ? _frame.Navigate(factory(SelectedPageType) ?? throw new InvalidOperationException("PageFactory 未创建页面。")) : _frame.Navigate(SelectedPageType);
            if (!success) throw new InvalidOperationException($"无法导航到 {SelectedPageType.Name}。");
            SetValue(CurrentPagePropertyKey, _frame.Content as Page);
            SetValue(NavigationErrorPropertyKey, "");
            SyncSelection();
        }
        catch (Exception error)
        {
            SetValue(NavigationErrorPropertyKey, error.Message);
            Logger?.LogWarning(error, "导航到 {PageType} 失败", SelectedPageType.Name);
        }
        finally { _synchronizing = false; UpdateStates(); }
    }
    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        if (_synchronizing || _frame?.Content is not Page page) return;
        _synchronizing = true;
        try { SetValue(CurrentPagePropertyKey, page); SetCurrentValue(SelectedPageTypeProperty, page.GetType()); SyncSelection(); }
        finally { _synchronizing = false; }
    }
    private void SyncSelection() { if (_navigation is not null) _navigation.SetCurrentValue(ListBox.SelectedItemProperty, Entries.FirstOrDefault(x => x.PageType == SelectedPageType)); }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_synchronizing && IsEnabled && _navigation?.SelectedItem is NavigationEntry entry) Navigate(entry.PageType); }
    private void ReleaseParts()
    {
        if (_frame is not null) _frame.Navigated -= OnNavigated;
        if (_navigation is not null) _navigation.SelectionChanged -= OnSelectionChanged;
        _frame = null; _navigation = null;
    }
    private void OnRendering(object? sender, EventArgs e) => SetValue(MetricsTextPropertyKey, $"FPS: {JaliumMeter.CurrentFps:F1}    Frame: {JaliumMeter.LastFrameDurationMs:F2} ms    Meter: {JaliumMeter.MeterName} ({(JaliumMeter.IsRunning ? "running" : "stopped")})");
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(NavigationError) ? "Ready" : "Failed", false);
        VisualStateManager.GoToState(this, ActualWidth < 760 ? "Compact" : "Wide", false);
    }
}
