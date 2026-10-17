using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Styling;
using SystemParameters = Jalium.UI.SystemParameters;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSMotionPreferenceTests
{
    private static readonly FieldInfo ReducedMotion = typeof(MacOSScrollBarSettings)
        .GetField("s_prefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> AnimationFlags()
    {
        foreach (string name in new[] { "ClientAreaAnimation", "ComboBoxAnimation", "ListBoxSmoothScrolling",
            "MenuAnimation", "MinimizeAnimation", "ToolTipAnimation", "UIEffects" })
            foreach (bool reduced in new[] { false, true }) yield return [name, reduced];
    }

    [MacOSMotionTheory]
    [MemberData(nameof(AnimationFlags))]
    public void PublicAnimationFlagsFollowMacOSReducedMotion(string name, bool reduced)
    {
        using var preference = new MotionPreference(reduced);
        Assert.Equal(!reduced, typeof(SystemParameters).GetProperty(name)!.GetValue(null));
    }

    [MacOSMotionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CssMediaQueriesReactToBothDirectionsAndKeepAuthorValues(bool initial)
    {
        using var preference = new MotionPreference(initial);
        var root = new StackPanel();
        var probe = new Border(); root.Children.Add(probe);
        Css.SetClass(probe, "motion");
        Css.SetStyleSheet(root, """
            .motion { opacity: 1; }
            @media (prefers-reduced-motion: reduce) { .motion { opacity: .25; } }
            """);
        try
        {
            foreach (bool reduced in new[] { initial, !initial, initial })
            {
                ReducedMotion.SetValue(null, reduced);
                SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.ClientAreaAnimation));
                CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                Assert.Equal(reduced ? .25 : 1, probe.Opacity);
            }
            probe.Opacity = .7;
            ReducedMotion.SetValue(null, !initial);
            SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.UIEffects));
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            Assert.Equal(.7, probe.Opacity);
        }
        finally { Css.SetStyleSheets(root, null); }
    }

    [MacOSMotionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultAutomaticTransitionPolicyUsesTheSystemPreference(bool reduced)
    {
        RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
        using var preference = new MotionPreference(reduced);
        Assert.NotNull(UIElement.AutomaticTransitionsEnabledProvider);
        Assert.Equal(!reduced, UIElement.AutomaticTransitionsEnabledProvider!());
    }

    [MacOSMotionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemResourceKeysResolveTheCurrentAnimationPreference(bool reduced)
    {
        using var preference = new MotionPreference(reduced);
        foreach (var row in AnimationFlags().Where(row => (bool)row[1] == reduced))
        {
            string name = (string)row[0];
            var key = typeof(SystemParameters).GetProperty(name + "Key")!.GetValue(null)!;
            Assert.True(SystemParameters.TryGetResource(key, out var value));
            Assert.Equal(!reduced, value);
        }
    }

    [MacOSMotionTheory]
    [InlineData("ComboBoxPopupAnimation", false)]
    [InlineData("ComboBoxPopupAnimation", true)]
    [InlineData("MenuPopupAnimation", false)]
    [InlineData("MenuPopupAnimation", true)]
    [InlineData("ToolTipPopupAnimation", false)]
    [InlineData("ToolTipPopupAnimation", true)]
    public void DerivedPopupAnimationFollowsMotionPreference(string name, bool reduced)
    {
        using var preference = new MotionPreference(reduced);
        var value = typeof(SystemParameters).GetProperty(name)!.GetValue(null);
        Assert.Equal(reduced, Equals(value, Jalium.UI.Controls.Primitives.PopupAnimation.None));
    }

    [MacOSMotionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationLookupIncludesParameterKeysWithTheirSystemSource(bool reduced)
    {
        using var preference = new MotionPreference(reduced);
        var application = (Application)RuntimeHelpers.GetUninitializedObject(typeof(Application));
        Assert.Equal(!reduced, application.TryFindResource(SystemParameters.ClientAreaAnimationKey));
        var lookup = typeof(Application).GetMethod("TryFindApplicationOrSystemResourceWithSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = ((object?, ResourceDictionary?))lookup.Invoke(application, [SystemParameters.ClientAreaAnimationKey])!;
        Assert.Equal(!reduced, result.Item1); Assert.Null(result.Item2);
    }

    [MacOSMotionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationDictionariesRetainPrecedenceOverSystemPreference(bool reduced)
    {
        using var preference = new MotionPreference(reduced);
        var application = (Application)RuntimeHelpers.GetUninitializedObject(typeof(Application));
        var resources = new ResourceDictionary { [SystemParameters.ClientAreaAnimationKey] = reduced };
        typeof(Application).GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(application, resources);
        Assert.Equal(reduced, application.TryFindResource(SystemParameters.ClientAreaAnimationKey));
        var lookup = typeof(Application).GetMethod("TryFindApplicationOrSystemResourceWithSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = ((object?, ResourceDictionary?))lookup.Invoke(application, [SystemParameters.ClientAreaAnimationKey])!;
        Assert.Equal(reduced, result.Item1); Assert.Same(resources, result.Item2);
    }

    private sealed class MotionPreference : IDisposable
    {
        private readonly object? _before = ReducedMotion.GetValue(null);
        internal MotionPreference(bool reduced) => ReducedMotion.SetValue(null, reduced);
        public void Dispose()
        {
            ReducedMotion.SetValue(null, _before);
            SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.ClientAreaAnimation));
            SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.UIEffects));
        }
    }
}

internal sealed class MacOSMotionTheoryAttribute : TheoryAttribute
{
    public MacOSMotionTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Requires macOS system preference semantics.";
    }
}
