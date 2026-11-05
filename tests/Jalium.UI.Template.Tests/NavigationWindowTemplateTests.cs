using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Navigation;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class NavigationWindowTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void ConstructionDoesNotCreateChromeAndNavigationWorksWithoutTemplateParts()
    {
        var window = new NavigationWindow();
        try
        {
            Assert.Null(window.GetValue(ContentControl.ContentProperty));
            Assert.Null(window.FindName("PART_NavigationBar"));
            Assert.Null(window.Content);
            Assert.False(window.GoBackCommand.CanExecute(null));
            Assert.False(window.RefreshCommand.CanExecute(null));
            var first = new Page();
            Assert.True(window.Navigate(first));
            Assert.Same(first, window.Content);
            Assert.Same(window.NavigationService, first.NavigationService);
            Assert.True(window.Navigate(new Page()));
            window.GoBackCommand.Execute(null);
            Assert.Same(first, window.Content);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void NavigationWindowCanBeDeclaredInXamlWithDirectPageContent()
    {
        var window = Assert.IsType<NavigationWindow>(Jalium.UI.Markup.XamlReader.Parse("""
            <NavigationWindow xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ShowsNavigationUI="False">
                <Page Title="XAML page" />
            </NavigationWindow>
            """));
        try
        {
            Assert.False(window.ShowsNavigationUI);
            Assert.IsType<Page>(window.Content);
            Assert.Equal("XAML page", window.Title);
            Assert.Same(window.NavigationService, ((Page)window.Content!).NavigationService);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void DefaultTemplateCommandsTrackTheJournalAndProjectTheActualContent()
    {
        var window = Create();
        try
        {
            var back = Part<Button>(window, "PART_BackButton");
            var forward = Part<Button>(window, "PART_ForwardButton");
            Assert.False(back.IsEnabled);
            Assert.Equal(.4, back.Opacity);
            Assert.False(forward.IsEnabled);
            Assert.False(Part<Button>(window, "PART_RefreshButton").IsEnabled);
            var first = new Page { Title = "First" };
            var second = new Page { Title = "Second" };
            window.Navigate(first);
            window.Navigate(second);
            Layout(window);
            Assert.Same(second, Part<ContentPresenter>(window, "PART_ContentPresenter").Content);
            Assert.True(back.IsEnabled);
            Assert.Equal(1, back.Opacity);
            back.PerformClick();
            Assert.Same(first, window.Content);
            Assert.Equal("First", window.Title);
            Assert.True(forward.IsEnabled);
            forward.PerformClick();
            Assert.Same(second, window.Content);
            Assert.Equal("Second", window.Title);
            Assert.Same(window.GoBackCommand, window.GetValue(NavigationWindow.GoBackCommandProperty));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void HidingChromePreservesTheContentTemplateAndExternalBinding()
    {
        var window = Create();
        try
        {
            var model = new Model();
            window.SetBinding(NavigationWindow.ShowsNavigationUIProperty,
                new Binding(nameof(Model.ShowChrome)) { Source = model, Mode = BindingMode.TwoWay });
            var content = new Page();
            window.Navigate(content);
            Layout(window);
            var presenter = Part<ContentPresenter>(window, "PART_ContentPresenter");
            var template = window.Template;
            model.ShowChrome = false;
            Layout(window);
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(window, "PART_NavigationBar").Visibility);
            Assert.Equal("NavigationHidden", VisualStateManager.GetCurrentStateName(window, "NavigationStates"));
            Assert.Same(template, window.Template);
            Assert.Same(content, presenter.Content);
            Assert.Same(presenter, window.FindName("PART_ContentPresenter"));
            window.SetCurrentValue(NavigationWindow.ShowsNavigationUIProperty, true);
            Assert.True(model.ShowChrome);
            Assert.NotNull(window.GetBindingExpression(NavigationWindow.ShowsNavigationUIProperty));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ReplacingTheTemplateRetiresOldCommandsAndKeepsCurrentContentAndState()
    {
        var window = Create();
        try
        {
            var first = new Page();
            var second = new Page();
            window.Navigate(first); window.Navigate(second);
            var back = Part<Button>(window, "PART_BackButton");
            var template = window.Template;
            window.ShowsNavigationUI = false;
            var empty = new ControlTemplate(typeof(NavigationWindow));
            empty.SetVisualTree(() => new Border());
            window.Template = empty;
            Layout(window);
            Assert.Null(back.Command);
            back.PerformClick();
            Assert.Same(second, window.Content);
            Assert.Null(window.FindName("PART_BackButton"));
            window.Template = template;
            Layout(window);
            Assert.Same(second, Part<ContentPresenter>(window, "PART_ContentPresenter").Content);
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(window, "PART_NavigationBar").Visibility);
            window.GoBackCommand.Execute(null);
            Assert.Same(first, window.Content);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void UriBindingsAndCurrentSourceFollowBackForwardAndRefresh()
    {
        var window = Create();
        try
        {
            int loads = 0;
            window.NavigationService.ContentLoader = uri => { loads++; return new Page { Title = uri.AbsolutePath }; };
            var model = new Model { Source = new Uri("https://example.test/first") };
            window.SetBinding(NavigationWindow.SourceProperty,
                new Binding(nameof(Model.Source)) { Source = model, Mode = BindingMode.TwoWay });
            var first = window.Content;
            var secondUri = new Uri("https://example.test/second");
            window.Navigate(secondUri);
            Assert.Equal(secondUri, model.Source);
            Assert.Equal(secondUri, window.CurrentSource);
            window.GoBack();
            Assert.Same(first, window.Content);
            Assert.Equal(new Uri("https://example.test/first"), model.Source);
            Assert.Equal(2, loads);
            window.GoForward();
            window.RefreshCommand.Execute(null);
            Assert.Equal(3, loads);
            Assert.Single(window.BackStack.Cast<object>());
            Assert.NotNull(window.GetBindingExpression(NavigationWindow.SourceProperty));
            Assert.Throws<InvalidOperationException>(() => window.SetValue(NavigationWindow.CurrentSourceProperty, secondUri));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void BoundContentUsesTheSameJournalAsTheClrNavigationSetter()
    {
        var window = Create();
        try
        {
            var first = new Page();
            var second = new Page();
            var third = new Page();
            var model = new Model { Content = first };
            window.SetBinding(ContentControl.ContentProperty,
                new Binding(nameof(Model.Content)) { Source = model, Mode = BindingMode.TwoWay });
            Assert.Same(first, window.NavigationService.Content);
            window.Content = second;
            Assert.Same(second, model.Content);
            model.Content = third;
            Assert.Same(third, window.NavigationService.Content);
            window.GoBack();
            Assert.Same(second, model.Content);
            Assert.Same(second, Part<ContentPresenter>(window, "PART_ContentPresenter").Content);
            Assert.NotNull(window.GetBindingExpression(ContentControl.ContentProperty));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void CanceledRequestsRestoreThePresentedContentWithoutAddingHistory()
    {
        var window = Create();
        try
        {
            var first = new Page { Title = "First" };
            window.NavigationService.ContentLoader = _ => new Page();
            var firstUri = new Uri("https://example.test/first");
            window.Navigate(firstUri);
            first = Assert.IsType<Page>(window.Content);
            var presenter = Part<ContentPresenter>(window, "PART_ContentPresenter");
            window.Navigating += (_, args) => args.Cancel = true;
            window.SetValue(ContentControl.ContentProperty, new Page());
            Assert.Same(first, window.Content);
            Assert.Same(first, presenter.Content);
            window.Source = new Uri("https://example.test/rejected");
            Assert.Equal(firstUri, window.Source);
            Assert.Equal(firstUri, window.CurrentSource);
            Assert.False(window.CanGoBack);
            window.Content = null;
            Assert.Same(first, window.Content);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void PageCallbacksResolveTheActualOwnerAndCanHideNavigationChrome()
    {
        var window = Create();
        try
        {
            var page = new Page { Title = "Owned page" };
            bool called = false;
            OnNavigatedTo(page, (_, args) =>
            {
                called = true;
                Assert.Same(window, args.Navigator);
                Assert.Same(window.NavigationService, page.NavigationService);
                Assert.Same(page, window.Content);
                page.ShowsNavigationUI = false;
            });
            window.Navigate(page);
            Assert.True(called);
            Assert.False(window.ShowsNavigationUI);
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(window, "PART_NavigationBar").Visibility);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void NestedPageNavigationKeepsTheLastCommittedPageAndTitle()
    {
        var window = Create();
        try
        {
            var first = new Page { Title = "First" };
            var second = new Page { Title = "Second" };
            OnNavigatedTo(first, (_, _) => window.Navigate(second));
            window.Navigate(first);
            Assert.Same(second, window.Content);
            Assert.Same(second, window.NavigationService.Content);
            Assert.Equal("Second", window.Title);
            Assert.Same(second, Part<ContentPresenter>(window, "PART_ContentPresenter").Content);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void KeyboardCommandsUseTheJournalAndRespectHandledAndDisabledState()
    {
        var window = Create();
        try
        {
            var first = new Page();
            var second = new Page();
            window.Navigate(first); window.Navigate(second);
            Assert.True(Key(window, Jalium.UI.Input.Key.Left, ModifierKeys.Alt));
            Assert.Same(first, window.Content);
            Assert.True(Key(window, Jalium.UI.Input.Key.Right, ModifierKeys.Alt));
            Assert.Same(second, window.Content);
            int refreshes = 0;
            window.LoadCompleted += (_, _) => refreshes++;
            Assert.True(Key(window, Jalium.UI.Input.Key.F5));
            Assert.Equal(1, refreshes);
            Key(window, Jalium.UI.Input.Key.BrowserBack, handled: true);
            Assert.Same(second, window.Content);
            window.IsEnabled = false;
            Assert.False(Key(window, Jalium.UI.Input.Key.BrowserBack));
            Assert.Same(second, window.Content);
        }
        finally { window.Close(); }
    }

    private static void OnNavigatedTo(Page page, EventHandler<NavigationEventArgs> handler)
        => typeof(Page).GetEvent("NavigatedTo", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetAddMethod(true)!.Invoke(page, new object[] { handler });

    private static DisplayedWindow Create()
    {
        var window = new DisplayedWindow { Width = 600, Height = 400 };
        window.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(window);
        return window;
    }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(Window window)
    {
        window.Measure(new Size(600, 400)); window.Arrange(new Rect(0, 0, 600, 400)); window.UpdateLayout();
    }
    private static bool Key(UIElement target, Key key, ModifierKeys modifiers = ModifierKeys.None, bool handled = false)
    {
        var args = (KeyEventArgs)Activator.CreateInstance(typeof(KeyEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { UIElement.KeyDownEvent, key, modifiers, true, false, 1 }, culture: null)!;
        args.Source = target;
        args.Handled = handled;
        target.RaiseEvent(args);
        return args.Handled;
    }
    private sealed class DisplayedWindow : NavigationWindow
    {
        public DisplayedWindow()
        {
            if (OperatingSystem.IsMacOS()) typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(this, new object[] { Visibility.Visible });
        }
    }
    private sealed class Model : INotifyPropertyChanged
    {
        private bool _showChrome = true;
        private Uri? _source;
        private object? _content;
        public bool ShowChrome { get => _showChrome; set { _showChrome = value; PropertyChanged?.Invoke(this, new(nameof(ShowChrome))); } }
        public Uri? Source { get => _source; set { _source = value; PropertyChanged?.Invoke(this, new(nameof(Source))); } }
        public object? Content { get => _content; set { _content = value; PropertyChanged?.Invoke(this, new(nameof(Content))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
