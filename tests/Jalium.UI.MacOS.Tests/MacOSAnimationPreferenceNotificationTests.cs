using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSAnimationPreferenceNotificationTests
{
    private static readonly string[] Names = [nameof(SystemParameters.ClientAreaAnimation), nameof(SystemParameters.ComboBoxAnimation),
        nameof(SystemParameters.ListBoxSmoothScrolling), nameof(SystemParameters.MenuAnimation), nameof(SystemParameters.MinimizeAnimation),
        nameof(SystemParameters.ToolTipAnimation), nameof(SystemParameters.UIEffects), nameof(SystemParameters.ComboBoxPopupAnimation),
        nameof(SystemParameters.MenuPopupAnimation), nameof(SystemParameters.ToolTipPopupAnimation)];

    public static IEnumerable<object[]> Deliveries()
    {
        foreach (int state in new[] { -1, 0, 1 })
        foreach (int windows in new[] { 1, 2, 4 })
        foreach (bool custom in new[] { false, true }) yield return [state, windows, custom];
    }

    [Theory, MemberData(nameof(Deliveries))]
    public void OneNativePreferenceChangePublishesOneGlobalWave(int state, int windows, bool custom)
    {
        using var scene = new Scene(state, windows, custom);
        for (int pass = 0; pass < 2; pass++) foreach (var window in scene.Windows) scene.Send(window);
        Assert.Equal(state == 1 ? 10 : 0, scene.Events.Count);
        if (state == 1) Assert.Equal(Names.Order(), scene.Events.Order());
        foreach (var pair in scene.Targets) Assert.Equal(!scene.NativeMotion, pair.Tag);
        Assert.Equal(2 * windows, scene.PerWindowEvents);
    }

    public static IEnumerable<object[]> Reentry()
    {
        foreach (int windows in new[] { 1, 2, 4 })
        foreach (bool custom in new[] { false, true })
        foreach (bool resource in new[] { false, true }) yield return [windows, custom, resource];
    }

    [Theory, MemberData(nameof(Reentry))]
    public void ASubscriberCannotRepublishTheCurrentPreference(int windows, bool custom, bool resource)
    {
        using var scene = new Scene(1, windows, custom); bool entered = false;
        void Reenter()
        {
            if (entered) return; entered = true;
            foreach (var window in scene.Windows) scene.Send(window);
        }
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == Names[0]) Reenter(); };
        Action<DependencyProperty, object?, object?> binding = (property, _, _) => { if (property == FrameworkElement.TagProperty) Reenter(); };
        if (resource) scene.Targets[0].PropertyChangedInternal += binding;
        else SystemParameters.StaticPropertyChanged += changed;
        try
        {
            foreach (var window in scene.Windows) scene.Send(window);
            Assert.True(entered); Assert.Equal(Names.Order(), scene.Events.Order());
            Assert.Equal(2 * windows, scene.PerWindowEvents);
            foreach (var pair in scene.Targets) Assert.Equal(!scene.NativeMotion, pair.Tag);
        }
        finally
        {
            scene.Targets[0].PropertyChangedInternal -= binding;
            SystemParameters.StaticPropertyChanged -= changed;
        }
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ClosingAWindowDuringGlobalPublicationRetiresItsLocalNotification(bool custom)
    {
        using var scene = new Scene(1, 1, custom); bool closed = false;
        PropertyChangedEventHandler changed = (_, e) =>
        { if (!closed && e.PropertyName == Names[0]) { closed = true; scene.Windows[0].Close(); } };
        SystemParameters.StaticPropertyChanged += changed;
        try
        {
            scene.Send(scene.Windows[0]); Assert.True(closed);
            Assert.Equal(Names.Order(), scene.Events.Order()); Assert.Equal(0, scene.PerWindowEvents);
        }
        finally { SystemParameters.StaticPropertyChanged -= changed; }
    }

    [Theory, InlineData(1, false), InlineData(2, false), InlineData(4, false), InlineData(1, true), InlineData(2, true), InlineData(4, true)]
    public void APreferenceReadBeforeWindowDeliveryDoesNotLoseItsPublication(int windows, bool custom)
    {
        using var scene = new Scene(1, windows, custom);
        Assert.True(MacOSScrollBarSettings.RefreshPreferences());
        Assert.Empty(scene.Events);
        for (int pass = 0; pass < 2; pass++) foreach (var window in scene.Windows) scene.Send(window);
        Assert.Equal(Names.Order(), scene.Events.Order());
        foreach (var pair in scene.Targets) Assert.Equal(!scene.NativeMotion, pair.Tag);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ASubscriberFailureDoesNotBlockTheNextPreferenceChange(bool custom)
    {
        using var scene = new Scene(1, 1, custom); var failure = new InvalidOperationException("preference subscriber");
        PropertyChangedEventHandler throwing = (_, e) => { if (e.PropertyName == Names[0]) throw failure; };
        SystemParameters.StaticPropertyChanged += throwing;
        try { Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => scene.Send(scene.Windows[0]))); }
        finally { SystemParameters.StaticPropertyChanged -= throwing; }
        scene.Events.Clear(); scene.SeedChangedCache(); scene.Send(scene.Windows[0]);
        Assert.Equal(Names.Order(), scene.Events.Order()); Assert.Equal(!scene.NativeMotion, scene.Targets[0].Tag);
    }

    private sealed class Scene : IDisposable
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type Settings = typeof(Window).Assembly.GetType("Jalium.UI.Controls.Platform.MacOSScrollBarSettings")!;
        private static readonly FieldInfo Motion = Settings.GetField("s_prefersReducedMotion", Static)!;
        private readonly Dictionary<FieldInfo, object?> _saved;
        private readonly Func<object, object?>? _application = ResourceLookup.ApplicationResourceLookup;
        private readonly Func<object, (object? Value, ResourceDictionary? Dictionary)>? _source = ResourceLookup.ApplicationResourceLookupWithSource;
        internal readonly List<Window> Windows = [];
        internal readonly List<Border> Targets = [];
        internal readonly List<string> Events = [];
        internal readonly bool NativeMotion;
        internal int PerWindowEvents;

        internal Scene(int state, int count, bool custom)
        {
            _saved = Settings.GetFields(Static).Where(f => f.Name is "s_prefersReducedMotion" or "s_prefersOverlayScrollBars" or
                "s_motionPreferenceRevision" or "s_publishedMotionPreferenceRevision").ToDictionary(f => f, f => f.GetValue(null));
            NativeMotion = (bool)Settings.GetMethod("ReadReducedMotion", Static)!.Invoke(null, null)!;
            Motion.SetValue(null, state == -1 ? null : state == 1 ? !NativeMotion : NativeMotion);
            ResourceLookup.ApplicationResourceLookup = null; ResourceLookup.ApplicationResourceLookupWithSource = null;
            ResourceLookup.InvalidateResourceCache();
            for (int i = 0; i < count; i++)
            {
                var target = new Border(); target.SetResourceReference(FrameworkElement.TagProperty, SystemParameters.ClientAreaAnimationKey);
                var window = new Window { Width = 300, Height = 220, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
                    Content = new ScrollViewer { Content = target } };
                window.SystemSettingsChanged += (_, _) => PerWindowEvents++;
                Windows.Add(window); Targets.Add(target);
            }
            SystemParameters.StaticPropertyChanged += Changed;
        }

        private void Changed(object? sender, PropertyChangedEventArgs e)
        { if (e.PropertyName is { } name && Names.Contains(name)) Events.Add(name); }

        internal void SeedChangedCache() => Motion.SetValue(null, !NativeMotion);

        internal void Send(Window window)
        {
            try { typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,
                [new PlatformEvent { Type = PlatformEventType.ScrollBarSettingsChanged }]); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); }
        }

        public void Dispose()
        {
            SystemParameters.StaticPropertyChanged -= Changed;
            foreach (var window in Windows) window.Close();
            foreach (var pair in _saved) pair.Key.SetValue(null, pair.Value);
            ResourceLookup.ApplicationResourceLookup = _application; ResourceLookup.ApplicationResourceLookupWithSource = _source;
            ResourceLookup.InvalidateResourceCache();
        }
    }
}
