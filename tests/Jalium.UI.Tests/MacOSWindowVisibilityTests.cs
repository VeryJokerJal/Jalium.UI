using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWindowVisibilityTests : MacOSGeometryTestBase
{
    [Fact]
    public void UnshownWindow_IsCollapsedAndItsContentIsNotVisible()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var content = new TextBox();
        var window = new Window { Content = content };
        try
        {
            Assert.Equal(Visibility.Collapsed, window.Visibility);
            Assert.False(window.IsVisible);
            Assert.False(content.IsVisible);
            Assert.False(window.IsLoaded);
            Assert.Equal(nint.Zero, window.Handle);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void InitialVisibleRequest_DispatchesShowAfterTheSetterReturns()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayRequestWindow();
        try
        {
            window.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
            Assert.Equal(0, window.ShowRequests);
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.Equal(1, window.ShowRequests);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(Visibility.Hidden)]
    [InlineData(Visibility.Collapsed)]
    public void HiddenBeforeDispatch_CancelsNativeCreation(Visibility visibility)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayRequestWindow();
        try
        {
            window.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
            window.SetCurrentValue(UIElement.VisibilityProperty, visibility);
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.Equal(0, window.ShowRequests);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void CloseBeforeDispatch_CancelsNativeCreation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayRequestWindow();
        window.Visibility = Visibility.Visible;
        window.Close();
        Dispatcher.CurrentDispatcher.ProcessQueue();
        Assert.Equal(0, window.ShowRequests);
        Assert.Equal(nint.Zero, window.Handle);
    }

    [Fact]
    public void BindingChangesBeforeDispatch_CancelAndResumeDisplay()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayRequestWindow();
        var source = new VisibilitySource { Value = Visibility.Visible };
        try
        {
            BindingOperations.SetBinding(window, UIElement.VisibilityProperty, new Binding(nameof(VisibilitySource.Value)) { Source = source });
            source.Value = Visibility.Hidden;
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.Equal(0, window.ShowRequests);
            source.Value = Visibility.Visible;
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.Equal(1, window.ShowRequests);
            Assert.NotNull(BindingOperations.GetBindingExpression(window, UIElement.VisibilityProperty));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ReentrantVisibilityRequest_DoesNotDispatchObsoleteShow()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new DisplayRequestWindow { HideWhenVisible = true };
        try
        {
            window.Visibility = Visibility.Visible;
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.Equal(Visibility.Hidden, window.Visibility);
            Assert.Equal(0, window.ShowRequests);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ClosedWindow_RejectsVisibleRequestsAndRemainsReadable(int writer)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var content = new TextBox();
        var window = new Window { Content = content };
        int closed = 0;
        window.Closed += (_, _) => closed++;
        window.Close();
        var localValue = window.ReadLocalValue(UIElement.VisibilityProperty);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (writer == 0) window.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
                else if (writer == 1) window.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
                else window.Visibility = Visibility.Visible;
            });
            Dispatcher.CurrentDispatcher.ProcessQueue();
            Assert.Equal(Visibility.Collapsed, window.Visibility);
            Assert.Equal(localValue, window.ReadLocalValue(UIElement.VisibilityProperty));
            Assert.False(window.IsVisible);
            Assert.False(content.IsVisible);
            Assert.Equal(Rect.Empty, window.RestoreBounds);
            Assert.Equal(nint.Zero, window.Handle);
            window.Close();
            Assert.Equal(1, closed);
        }
    }

    [Fact]
    public void ClosedWindow_RejectedBindingUpdatePreservesItsBindingAndAllowsHiddenUpdates()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var window = new Window();
        var source = new VisibilitySource { Value = Visibility.Hidden };
        BindingOperations.SetBinding(window, UIElement.VisibilityProperty, new Binding(nameof(VisibilitySource.Value)) { Source = source });
        var binding = BindingOperations.GetBindingExpression(window, UIElement.VisibilityProperty);
        window.Close();

        Assert.Throws<InvalidOperationException>(() => source.Value = Visibility.Visible);
        Assert.Equal(Visibility.Collapsed, window.Visibility);
        Assert.Same(binding, BindingOperations.GetBindingExpression(window, UIElement.VisibilityProperty));
        source.Value = Visibility.Hidden;
        Assert.Equal(Visibility.Hidden, window.Visibility);
        Assert.Throws<InvalidOperationException>(() => source.Value = Visibility.Visible);
        Assert.Equal(Visibility.Hidden, window.Visibility);
        Assert.False(window.IsVisible);
        Assert.Equal(nint.Zero, window.Handle);
        BindingOperations.ClearBinding(window, UIElement.VisibilityProperty);
        Assert.Equal(Visibility.Collapsed, window.Visibility);
    }

    private sealed class DisplayRequestWindow : Window
    {
        public int ShowRequests;
        public bool HideWhenVisible;
        public override void Show() => ShowRequests++;
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (HideWhenVisible && e.Property == VisibilityProperty && e.NewValue is Visibility.Visible)
                SetValue(VisibilityProperty, Visibility.Hidden);
        }
    }
    private sealed class VisibilitySource : INotifyPropertyChanged
    {
        private Visibility _value;
        public Visibility Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

// Headless keyboard/layout tests need a displayed logical root. This avoids
// creating AppKit windows from xUnit worker threads and does not assert native
// presentation; the HostSmoke fixture owns that integration coverage.
internal sealed class DisplayedTestWindow : Window
{
    public DisplayedTestWindow()
    {
        if (OperatingSystem.IsMacOS())
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(this, [Visibility.Visible]);
    }
}

// The opt-in native tests query RenderContext.Current. Application shutdown
// checks can dispose it, so initialize it for each test instead of once for the
// collection. Ordinary managed test runs create no graphics context here.
public abstract class MacOSGeometryTestBase : IDisposable
{
#if JALIUM_MAC_NATIVE_GEOMETRY_TESTS
    private readonly Jalium.UI.Interop.RenderContext _context;
    protected MacOSGeometryTestBase()
    {
        var previous = Jalium.UI.Interop.RenderContext.Current;
        _context = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(Jalium.UI.Interop.RenderBackend.Metal);
        if (!_context.IsValid || _context.Backend != Jalium.UI.Interop.RenderBackend.Metal)
            throw new InvalidOperationException("Native macOS geometry tests require an available Metal context.");
        string? output = Environment.GetEnvironmentVariable("JALIUM_MAC_NATIVE_CONTEXT_OUTPUT");
        if (!string.IsNullOrEmpty(output))
            File.AppendAllText(output, System.Text.Json.JsonSerializer.Serialize(new
            { testClass = GetType().Name, previousBackend = previous?.Backend.ToString(),
                currentBackend = _context.Backend.ToString(), currentValid = _context.IsValid }) + "\n");
    }
    public void Dispose() => _context.Dispose();
#else
    protected MacOSGeometryTestBase() { }
    public void Dispose() { }
#endif
}

// These assertions depend on CoreText metrics rather than the managed fallback.
// Keep them in the opt-in native suite without reporting an unexecuted assertion
// as a passing check in ordinary managed runs.
public sealed class MacOSNativeGeometryFactAttribute : FactAttribute
{
    public MacOSNativeGeometryFactAttribute()
    {
#if !JALIUM_MAC_NATIVE_GEOMETRY_TESTS
        Skip = "Requires JaliumMacNativeGeometryTests=true and a native text rendering context.";
#endif
    }
}
