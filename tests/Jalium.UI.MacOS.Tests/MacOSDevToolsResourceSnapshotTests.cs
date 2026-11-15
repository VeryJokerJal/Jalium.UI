#pragma warning disable WPF0001
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsResourceSnapshotTests
{
    [Fact]
    public void WindowThemeAndRedirectedAncestryUseTheRealResourceLookupContext()
    {
        var oldTheme = ResourceDictionary.CurrentThemeKey;
        var redirect = ResourceLookup.AncestorRedirectLookup;
        var leaf = new Border(); var detached = new Border { Child = leaf };
        var window = new Window { ThemeMode = ThemeMode.Light };
        try
        {
            ResourceDictionary.CurrentThemeKey = "Dark";
            window.Resources.ThemeDictionaries["Light"] = new ResourceDictionary { ["Palette"] = "light" };
            window.Resources.ThemeDictionaries["Dark"] = new ResourceDictionary { ["Palette"] = "dark" };
            ResourceLookup.AncestorRedirectLookup = current => ReferenceEquals(current, detached) ? window : redirect?.Invoke(current);
            var snapshot = DevToolsResourceSnapshot.Capture(leaf, null, "Palette", true);
            Assert.Equal("\"light\"", Assert.Single(snapshot.Rows, row => row.Kind == DevToolsResourceRowKind.Resource).ValueText);
            window.ThemeMode = ThemeMode.Dark;
            snapshot = DevToolsResourceSnapshot.Capture(leaf, null, "Palette", true);
            Assert.Equal("\"dark\"", Assert.Single(snapshot.Rows, row => row.Kind == DevToolsResourceRowKind.Resource).ValueText);
            Assert.Equal("Dark", ResourceDictionary.CurrentThemeKey);
        }
        finally { ResourceLookup.AncestorRedirectLookup = redirect; window.Close(); ResourceDictionary.CurrentThemeKey = oldTheme; }
    }

    [Fact]
    public void RetainedPresentationRowsDoNotKeepTheInspectedElementAlive()
    {
        var capture = CaptureForCollection();
        for (int pass = 0; pass < 3 && capture.Target.IsAlive; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.False(capture.Target.IsAlive); GC.KeepAlive(capture.Rows);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Target, DevToolsResourceRowData[] Rows) CaptureForCollection()
    {
        var target = new Border(); target.Resources["Key"] = "Value";
        return (new WeakReference(target), DevToolsResourceSnapshot.Capture(target, null, null, false).Rows.ToArray());
    }

    [Fact]
    public void EmptyInspectionDoesNotAllocateResourceDictionaries()
    {
        var target = new Border { Style = new Style(typeof(Border)) };
        Assert.Null(target.ResourcesOrNull); Assert.Null(target.Style.ResourcesOrNull);
        var snapshot = DevToolsResourceSnapshot.Capture(target, null, null, false);
        Assert.Equal(1, snapshot.ScopeCount); Assert.Equal(0, snapshot.DeclarationCount);
        Assert.Single(snapshot.Rows);
        Assert.Null(target.ResourcesOrNull); Assert.Null(target.Style.ResourcesOrNull);
    }

    [Fact]
    public void LogicalLocalStyleAndAncestorScopesPreserveAuthoredNullWinners()
    {
        var leaf = new Button { Name = "Leaf", Style = new Style(typeof(Button)) };
        var parent = new Border { Child = leaf };
        parent.Resources["Shared"] = "parent";
        leaf.Style.Resources["Shared"] = "style";
        leaf.Style.Resources["StyleOnly"] = 42;
        leaf.Resources["Shared"] = null;
        var snapshot = DevToolsResourceSnapshot.Capture(leaf, null, null, false);
        Assert.Equal(3, snapshot.ScopeCount); Assert.Equal(4, snapshot.DeclarationCount); Assert.Equal(2, snapshot.WinnerCount);
        var shared = snapshot.Rows.Where(row => row.Kind == DevToolsResourceRowKind.Resource && row.KeyText == "Shared").ToArray();
        Assert.Equal(3, shared.Length); Assert.True(shared[0].IsWinner); Assert.Equal("<null>", shared[0].ValueText);
        Assert.All(shared.Skip(1), row => Assert.False(row.IsWinner));
        var filtered = DevToolsResourceSnapshot.Capture(leaf, null, "sHaReD", true);
        Assert.Single(filtered.Rows, row => row.Kind == DevToolsResourceRowKind.Resource);
        Assert.Equal(4, filtered.DeclarationCount); Assert.Equal(2, filtered.WinnerCount);
        Assert.Empty(DevToolsResourceSnapshot.Capture(leaf, null, "missing", false).Rows);
    }

    [Fact]
    public void ThemesAndReverseMergesMatchTheActualLookupSource()
    {
        var oldTheme = ResourceDictionary.CurrentThemeKey;
        try
        {
            ResourceDictionary.CurrentThemeKey = "Dark";
            var leaf = new Border(); var dictionary = leaf.Resources;
            var earlier = new ResourceDictionary { ["Shared"] = "early", ["MergedOnly"] = "early" };
            var later = new ResourceDictionary { ["Shared"] = "late", ["MergedOnly"] = "late" };
            dictionary.MergedDictionaries.Add(earlier); dictionary.MergedDictionaries.Add(later);
            dictionary.ThemeDictionaries["Dark"] = new ResourceDictionary { ["Shared"] = "dark" };
            dictionary.ThemeDictionaries["Light"] = new ResourceDictionary { ["Shared"] = "light", ["InactiveOnly"] = "light" };
            var snapshot = DevToolsResourceSnapshot.Capture(leaf, null, null, false);
            var winners = snapshot.Rows.Where(row => row.Kind == DevToolsResourceRowKind.Resource && row.IsWinner).ToArray();
            Assert.Equal(2, winners.Length);
            Assert.Equal("\"dark\"", Assert.Single(winners, row => row.KeyText == "Shared").ValueText);
            Assert.Equal("\"late\"", Assert.Single(winners, row => row.KeyText == "MergedOnly").ValueText);
            Assert.All(snapshot.Rows.Where(row => row.ValueText == "\"light\""), row => { Assert.True(row.IsInactive); Assert.False(row.IsWinner); });
            Assert.Equal("dark", leaf.TryFindResource("Shared")); Assert.Equal("late", leaf.TryFindResource("MergedOnly"));
            dictionary["Shared"] = "local";
            var local = DevToolsResourceSnapshot.Capture(leaf, null, "Shared", true);
            Assert.Equal("\"local\"", Assert.Single(local.Rows, row => row.Kind == DevToolsResourceRowKind.Resource).ValueText);
        }
        finally { ResourceDictionary.CurrentThemeKey = oldTheme; }
    }

    [Fact]
    public void CyclesAndSharedDictionariesStayFiniteAndOnlyOneDeclarationWins()
    {
        var leaf = new Border(); var shared = new ResourceDictionary { ["Shared"] = "value" };
        leaf.Resources.MergedDictionaries.Add(shared); leaf.Resources.MergedDictionaries.Add(shared);
        // Install the lookup back-edge without dispatching change notifications;
        // cyclic mutation notifications are a separate existing engine contract.
        var items = Assert.IsAssignableFrom<IList<ResourceDictionary>>(typeof(System.Collections.ObjectModel.Collection<ResourceDictionary>)
            .GetProperty("Items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shared.MergedDictionaries));
        items.Add(leaf.Resources);
        var snapshot = DevToolsResourceSnapshot.Capture(leaf, null, null, false);
        Assert.InRange(snapshot.Rows.Count, 1, 20);
        Assert.Equal(2, snapshot.DeclarationCount); Assert.Equal(1, snapshot.WinnerCount);
        Assert.Single(snapshot.Rows, row => row.Kind == DevToolsResourceRowKind.Resource && row.IsWinner);
        Assert.Contains(snapshot.Rows, row => row.Kind == DevToolsResourceRowKind.Note && row.KeyText == "Dictionary cycle");
    }

    [Fact]
    public void TypedPreviewsAndResourceErrorsRemainInspectable()
    {
        var leaf = new Border { Resources = new FaultyDictionary() };
        leaf.Resources["Tint"] = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));
        leaf.Resources["Number"] = 12.5;
        leaf.Resources["String"] = "first\nsecond";
        leaf.Resources["Broken"] = "unavailable";
        leaf.Resources["Object"] = new ThrowingText();
        var snapshot = DevToolsResourceSnapshot.Capture(leaf, null, null, false);
        var resources = snapshot.Rows.Where(row => row.Kind == DevToolsResourceRowKind.Resource).ToArray();
        Assert.Equal(5, resources.Length);
        Assert.Equal("#FF0A141E", Assert.Single(resources, row => row.KeyText == "Tint").ValueText);
        Assert.NotSame(leaf.Resources["Tint"], Assert.Single(resources, row => row.KeyText == "Tint").Swatch);
        Assert.Equal(DevToolsResourceValueKind.Number, Assert.Single(resources, row => row.KeyText == "Number").ValueKind);
        Assert.Equal("\"first\\nsecond\"", Assert.Single(resources, row => row.KeyText == "String").ValueText);
        Assert.Equal("<InvalidOperationException>", Assert.Single(resources, row => row.KeyText == "Broken").ValueText);
        Assert.Equal("<ThrowingText>", Assert.Single(resources, row => row.KeyText == "Object").ValueText);
    }

    private sealed class FaultyDictionary : ResourceDictionary
    {
        protected override void OnGettingValue(object key, ref object? value, out bool canCache)
        {
            if (Equals(key, "Broken")) throw new InvalidOperationException("Deferred resource probe");
            base.OnGettingValue(key, ref value, out canCache);
        }
    }
    private sealed class ThrowingText { public override string ToString() => throw new InvalidOperationException(); }
}
