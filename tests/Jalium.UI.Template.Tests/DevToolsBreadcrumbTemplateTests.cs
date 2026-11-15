using System.Runtime.CompilerServices;
using System.Windows.Input;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsBreadcrumbTemplateTests
{
    public DevToolsBreadcrumbTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    [Fact]
    public void NativeButtonContractsNavigateAncestorsAndKeepTheCurrentEntryReadonly()
    {
        var root = new Grid(); var child = new Border { Name = "Current" }; root.Children.Add(child);
        var first = new DevToolsBreadcrumbNode(root); var current = new DevToolsBreadcrumbNode(child, true, true);
        var path = new DevToolsBreadcrumb { ItemsSource = new[] { first, current } }; Layout(path, 480);
        var buttons = Buttons(path); Assert.Equal(2, buttons.Length);
        Assert.Equal("Grid", buttons[0].Label); Assert.Equal("Grid", ((TextBlock)buttons[0].FindName("PART_Label")!).Text);
        Assert.True(buttons[0].IsEnabled); Assert.False(buttons[1].IsEnabled); Assert.True(buttons[1].IsActive); Assert.Equal("Border #Current", current.Hint);
        Visual? navigated = null; path.NavigationRequested += (_, args) => navigated = args.Target;
        ((IInvokeProvider)buttons[0].GetAutomationPeer()!).Invoke(); Assert.Same(root, navigated);
        navigated = null; path.NavigateCommand!.Execute(current); Assert.Null(navigated); Assert.False(path.NavigateCommand.CanExecute(current));
        GC.KeepAlive(root); GC.KeepAlive(child);
    }
    [Fact]
    public void MissingAndReplacedTemplatesRetireTheirOldNavigationButtons()
    {
        var root = new Grid(); var node = new DevToolsBreadcrumbNode(root); var path = new DevToolsBreadcrumb { ItemsSource = new[] { node } };
        Layout(path, 240); var original = path.Template; var retired = Assert.Single(Buttons(path)); int requests = 0;
        path.NavigationRequested += (_, _) => requests++;
        var blank = new ControlTemplate(typeof(DevToolsBreadcrumb)); blank.SetVisualTree(() => new Border()); path.Template = blank; path.ApplyTemplate();
        Assert.False(path.NavigateCommand!.CanExecute(node)); retired.PerformClick(); Assert.Equal(0, requests);
        path.Template = original; Layout(path, 240); Assert.True(path.NavigateCommand.CanExecute(node));
        retired.PerformClick(); Assert.Equal(0, requests);
        Assert.Single(Buttons(path)).PerformClick(); Assert.Equal(1, requests); GC.KeepAlive(root);
    }
    [Theory]
    [InlineData(200)] [InlineData(480)]
    public void LongPathsWrapAndConsumersCanReplaceTheItemTemplateAndCommand(double width)
    {
        var target = new AControlWithAnIntentionallyLongTypeNameForBreadcrumbWrapping();
        var node = new DevToolsBreadcrumbNode(target); var path = new DevToolsBreadcrumb { ItemsSource = new[] { node, new DevToolsBreadcrumbNode(target, true, true) } };
        Layout(path, width); Assert.InRange(path.DesiredSize.Width, 0, width + .5);
        foreach (var button in Buttons(path)) Assert.True(button.TranslatePoint(new Point(button.ActualWidth, 0), path).X <= width + .5);
        var replacement = new ProbeCommand(); path.NavigateCommand = replacement; Layout(path, width);
        Buttons(path)[0].PerformClick(); Assert.Same(node, replacement.Parameter); Assert.Equal(1, replacement.Count);
        path.ItemTemplate = new DataTemplate(); path.ItemTemplate.SetVisualTree(() => new TextBlock { Text = "custom" }); Layout(path, width);
        Assert.Empty(Buttons(path)); GC.KeepAlive(target);
    }
    [Fact]
    public void RetainedPathEntriesDoNotOwnTheirTargetsAndRejectStaleOrForeignNodes()
    {
        var stale = CreateStale(); var live = new Grid(); var liveNode = new DevToolsBreadcrumbNode(live);
        var path = new DevToolsBreadcrumb { ItemsSource = new[] { stale.Node, liveNode } }; Layout(path, 240);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Assert.False(stale.Target.IsAlive);
        Assert.False(path.NavigateCommand!.CanExecute(stale.Node));
        Assert.False(path.NavigateCommand.CanExecute(new DevToolsBreadcrumbNode(live)));
        int requests = 0; path.NavigationRequested += (_, _) => requests++;
        path.NavigateCommand.Execute(stale.Node); Assert.Equal(0, requests);
        path.ItemsSource = Array.Empty<DevToolsBreadcrumbNode>(); Layout(path, 240); Assert.False(path.NavigateCommand.CanExecute(liveNode));
        GC.KeepAlive(live); GC.KeepAlive(path);
    }
    [Theory]
    [InlineData(Key.Return)] [InlineData(Key.Space)]
    public void AncestorButtonsKeepTheStandardEnterAndSpaceActivationContract(Key key)
    {
        var target = new Grid(); var node = new DevToolsBreadcrumbNode(target); var path = new DevToolsBreadcrumb { ItemsSource = new[] { node } }; Layout(path, 240);
        int requests = 0; path.NavigationRequested += (_, _) => requests++;
        var button = Assert.Single(Buttons(path));
        button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new Source { RootVisual = button }, 1, key) { RoutedEvent = UIElement.KeyDownEvent, Source = button });
        button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new Source { RootVisual = button }, 2, key) { RoutedEvent = UIElement.KeyUpEvent, Source = button });
        Assert.Equal(1, requests); GC.KeepAlive(target);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DevToolsBreadcrumbNode Node, WeakReference Target) CreateStale() { var target = new Border(); return (new(target), new(target)); }
    private sealed class AControlWithAnIntentionallyLongTypeNameForBreadcrumbWrapping : Border { }
    private sealed class ProbeCommand : ICommand
    {
        public int Count; public object? Parameter;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { Count++; Parameter = parameter; }
    }
    private static DevToolsActionButton[] Buttons(Visual root) => Walk(root).OfType<DevToolsActionButton>().ToArray();
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } visual) foreach (var child in Walk(visual)) yield return child; }
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private static void Layout(UIElement element, double width) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 1200)); element.Arrange(new Rect(0,0,width,element.DesiredSize.Height)); element.UpdateLayout(); } }
}
