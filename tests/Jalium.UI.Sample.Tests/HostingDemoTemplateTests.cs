using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Hosting;
using Jalium.UI.HostingDemo.Services;
using Jalium.UI.HostingDemo.ViewModels;
using Jalium.UI.HostingDemo.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jalium.UI.Sample.Tests;

public sealed class HostingDemoTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "HostingDemo.Tests";
        public string ContentRootPath { get; set; } = "/tmp/hosting-demo";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static HomePage Home() => new(new HomeViewModel(new Greeter(Options.Create(new GreetingOptions()), NullLogger<Greeter>.Instance), new Environment()));
    [Fact]
    public void MvvmAndLoggingTemplatesUseCommandsAndLiveBindings()
    {
        var vm = new MvvmViewModel(); var page = new MvvmPage(vm); Layout(page);
        var count = Assert.IsType<TextBlock>(page.FindName("PART_Count")); Assert.Equal("Count = 0", count.Text);
        Assert.IsType<Button>(page.FindName("PART_Increment")).Command!.Execute(null);
        Assert.Equal(1, vm.Count); Assert.Equal("Count = 1", count.Text);
        Assert.Equal(vm.LastAction, Assert.IsType<TextBlock>(page.FindName("PART_LastAction")).Text);
        Assert.IsType<Button>(page.FindName("PART_Reset")).Command!.Execute(null); Assert.Equal("Count = 0", count.Text);
        var logging = new LoggingPage(new LoggingViewModel(NullLogger<LoggingViewModel>.Instance)); Layout(logging);
        Assert.IsType<Button>(logging.FindName("PART_LogWarning")).Command!.Execute(null);
        Assert.Contains("Warn", Assert.IsType<TextBlock>(logging.FindName("PART_LastEntry")).Text);
    }
    [Fact]
    public void AllSixPagesExpandCompiledTemplatesAndBindRealData()
    {
        var home = Home(); Layout(home); Assert.Contains("开发者", Assert.IsType<TextBlock>(home.FindName("PART_Greeting")).Text);
        var options = new OptionsPage(new OptionsViewModel(Options.Create(new JaliumRuntimeOptions()), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jalium:Render:Backend"] = "Software" }).Build()));
        Layout(options); Assert.Contains("Software", Assert.IsType<TextBlock>(options.FindName("PART_Configuration")).Text);
        using var metricsVm = new MetricsViewModel(); var metrics = new MetricsPage(metricsVm); Layout(metrics); Assert.Contains("FPS", Assert.IsType<TextBlock>(metrics.FindName("PART_Fps")).Text);
        using var clock = new ClockHostedService(NullLogger<ClockHostedService>.Instance); using var hostedVm = new HostedServicesViewModel(clock);
        var hosted = new HostedServicesPage(hostedVm); Layout(hosted); Assert.Equal(hostedVm.CurrentTime, Assert.IsType<TextBlock>(hosted.FindName("PART_CurrentTime")).Text);
    }
    [Fact]
    public void ShellRetainsPageIdentityAndDisconnectsRetiredNavigationAcrossTemplates()
    {
        var vm = new MvvmViewModel();
        Page Factory(Type type) => type == typeof(MvvmPage) ? new MvvmPage(vm) : Home();
        var shell = new HostingDemoShell { PageFactory = Factory }; Layout(shell, 1100);
        var nav = Assert.IsType<ListBox>(shell.FindName("PART_Navigation")); Assert.Equal(6, nav.Items.Count);
        nav.SelectedItem = shell.NavigationItems[1]; Assert.IsType<MvvmPage>(shell.CurrentPage); var retained = shell.CurrentPage;
        vm.Increment(); var oldFrame = Assert.IsType<Frame>(shell.FindName("PART_Frame"));
        var blank = new ControlTemplate(typeof(HostingDemoShell)); blank.SetVisualTree(() => new Border()); shell.Template = blank; Layout(shell);
        Assert.Null(oldFrame.Content); nav.SelectedItem = shell.NavigationItems[0]; Assert.Equal(typeof(MvvmPage), shell.SelectedPageType);
        shell.ClearValue(Control.TemplateProperty); Layout(shell, 480);
        Assert.Same(retained, shell.CurrentPage); Assert.Same(retained, Assert.IsType<Frame>(shell.FindName("PART_Frame")).Content);
        Assert.Equal(1, vm.Count); Assert.Equal("Compact", VisualStateManager.GetCurrentStateName(shell, "LayoutStates"));
    }
    private static void Layout(FrameworkElement element, double width = 480)
    {
        if (!element.Resources.MergedDictionaries.Contains(Defaults.Value)) element.Resources.MergedDictionaries.Add(Defaults.Value);
        element.InvalidateMeasure(); element.Measure(new Size(width, 720)); element.Arrange(new Rect(0, 0, width, 720)); element.UpdateLayout();
    }
}
