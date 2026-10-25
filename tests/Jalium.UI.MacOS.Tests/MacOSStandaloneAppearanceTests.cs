#pragma warning disable WPF0001
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSStandaloneAppearanceTests : IDisposable
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly FieldInfo ApplicationField = typeof(ThemeManager).GetField("_application", Hidden)!;
    private static readonly PropertyInfo ThemeProperty = typeof(ThemeManager).GetProperty(nameof(ThemeManager.CurrentTheme))!;
    private static readonly Dictionary<nint, Window> Registry = (Dictionary<nint, Window>)typeof(Window).GetField("_windows", Hidden)!.GetValue(null)!;
    private readonly object? _application = ApplicationField.GetValue(null), _key = ResourceDictionary.CurrentThemeKey;
    private readonly ThemeVariant _theme = ThemeManager.CurrentTheme;
    private readonly Func<uint>? _read = PlatformThemeIntegration.ReadColorScheme;
    private readonly Func<bool>? _contrast = PlatformThemeIntegration.ReadHighContrast;
    private readonly Func<object, object?>? _lookup = ResourceLookup.ApplicationResourceLookup;
    private readonly Func<object, (object? Value, ResourceDictionary? Dictionary)>? _source = ResourceLookup.ApplicationResourceLookupWithSource;
    private readonly List<(nint Key, Window Window)> _windows = [];
    private bool _dark, _highContrast;
    private static int _nextKey;

    public MacOSStandaloneAppearanceTests()
    {
        Assert.True(OperatingSystem.IsMacOS()); Assert.Null(Application.Current);
        ApplicationField.SetValue(null, null);
        PlatformThemeIntegration.ReadColorScheme = () => _dark ? 1u : 2u;
        PlatformThemeIntegration.ReadHighContrast = () => _highContrast;
        ResourceLookup.ApplicationResourceLookup = null; ResourceLookup.ApplicationResourceLookupWithSource = null;
    }

    public static IEnumerable<object[]> Windows()
    {
        foreach (string mode in new[] { "None", "System", "Light", "Dark" })
            foreach (bool dark in new[] { false, true })
                foreach (bool contrast in new[] { false, true }) yield return [mode, dark, contrast];
    }
    public static IEnumerable<object[]> ContrastWindows() => Windows().Where(row => !(bool)row[2]).Select(row => row[..2]);

    private void Seed(bool dark, bool contrast = false)
    {
        _dark = dark; _highContrast = contrast;
        ThemeProperty.SetValue(null, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        ResourceDictionary.CurrentThemeKey = contrast ? "HighContrast" : dark ? "Dark" : "Light";
    }
    private (Window Window, TextBox Editor) Open(string mode)
    {
        var window = new DisplayedTestWindow { ThemeMode = new ThemeMode(mode) };
        foreach (string name in new[] { "Light", "Dark", "HighContrast" })
            window.Resources.ThemeDictionaries[name] = new ResourceDictionary
            { ["Pane"] = new SolidColorBrush(name == "Light" ? Colors.White : name == "Dark" ? Colors.Black : Colors.Yellow) };
        var editor = new TextBox { Text = "ab🙂中文 Miii" }; window.Content = editor;
        window.SetResourceReference(Window.BackgroundProperty, "Pane"); editor.Select(4, 2);
        // Register only the managed root. Handle remains zero, so no fake native handle is queried.
        nint key = -Interlocked.Increment(ref _nextKey); Registry.Add(key, window); _windows.Add((key, window));
        return (window, editor);
    }
    private void Publish(bool dark)
    {
        _dark = dark; SystemParameters.NotifyPlatformAppearanceChanged(dark ? 1u : 2u);
    }
    private void Palette(Window window, string mode)
    {
        string expected = _highContrast ? "HighContrast" : mode is "Light" or "Dark" ? mode : _dark ? "Dark" : "Light";
        Assert.Same(window.Resources.ThemeDictionaries[expected]["Pane"], window.Background);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NoApplicationAndNoWindowsStillTrackTheSystemVariant(bool initialDark)
    {
        Seed(initialDark); Publish(!initialDark);
        Assert.Equal(!initialDark ? ThemeVariant.Dark : ThemeVariant.Light, ThemeManager.CurrentTheme);
        Assert.Equal(!initialDark ? "Dark" : "Light", ResourceDictionary.CurrentThemeKey);
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void StandalonePaletteFollowsItsWindowModeAndPreservesEditing(string mode, bool initialDark, bool contrast)
    {
        Seed(initialDark, contrast); var (window, editor) = Open(mode); Palette(window, mode);
        foreach (bool dark in new[] { !initialDark, initialDark, !initialDark })
        {
            Publish(dark); Palette(window, mode);
            Assert.Same(editor, window.Content); Assert.Equal("ab🙂中文 Miii", editor.Text);
            Assert.Equal("中文", editor.SelectedText); Assert.Equal(4, editor.SelectionStart); Assert.Equal(2, editor.SelectionLength);
        }
    }

    [Theory]
    [MemberData(nameof(ContrastWindows))]
    public void LeavingHighContrastRestoresTheLatestSystemVariant(string mode, bool initialDark)
    {
        Seed(initialDark, true); var (window, _) = Open(mode); Publish(!initialDark); Palette(window, mode);
        _highContrast = false; SystemParameters.NotifyStaticPropertyChanged(nameof(SystemParameters.HighContrast));
        Palette(window, mode);
        Assert.Equal(!initialDark ? ThemeVariant.Dark : ThemeVariant.Light, ThemeManager.CurrentTheme);
    }

    public void Dispose()
    {
        foreach (var (key, window) in _windows) { Registry.Remove(key); window.Close(); }
        ThemeProperty.SetValue(null, _theme); ResourceDictionary.CurrentThemeKey = _key;
        PlatformThemeIntegration.ReadColorScheme = _read; PlatformThemeIntegration.ReadHighContrast = _contrast;
        ResourceLookup.ApplicationResourceLookup = _lookup; ResourceLookup.ApplicationResourceLookupWithSource = _source;
        ApplicationField.SetValue(null, _application); ResourceLookup.InvalidateResourceCache();
    }
}
