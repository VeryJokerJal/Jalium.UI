using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSEditControlGraphemeKeyTests : MacOSGeometryTestBase
{
    private static readonly MethodInfo DispatchMethod = typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static IEnumerable<object[]> Clusters()
    {
        foreach (string cluster in new[] { "e\u0301", "🙂", "👋🏿", "🇨🇳", "👩‍👩‍👧‍👦", "1️⃣" })
            foreach (bool forward in new[] { false, true }) foreach (bool shift in new[] { false, true })
                yield return [cluster, forward, shift];
    }

    [Theory] [MemberData(nameof(Clusters))]
    public void RealKeyDispatchUsesWholeGraphemes(string cluster, bool forward, bool shift)
    {
        using var fixture = new Fixture("A" + cluster + "Z");
        int before = forward ? 1 : 1 + cluster.Length, expected = forward ? 1 + cluster.Length : 1;
        fixture.Editor.CaretOffset = before; fixture.Key(forward ? 0x27 : 0x25, shift);
        Assert.Equal(expected, fixture.Editor.CaretOffset);
        Assert.Equal(shift ? cluster : "", fixture.Editor.SelectedText);
        Assert.Equal("A" + cluster + "Z", fixture.Editor.Text);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void UnmodifiedArrowCollapsesAnExistingSelectionToItsEdge(bool forward)
    {
        using var fixture = new Fixture("Aone e\u0301🙂Z");
        fixture.Editor.Select(1, fixture.Editor.Text.Length - 2);
        fixture.Key(forward ? 0x27 : 0x25);
        Assert.Equal(forward ? fixture.Editor.Text.Length - 1 : 1, fixture.Editor.CaretOffset);
        Assert.Equal(0, fixture.Editor.SelectionLength);
    }

    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void CrLfIsOneNavigationAndDeletionUnit(bool forward, bool deletion)
    {
        using var fixture = new Fixture("A\r\nZ"); fixture.Editor.CaretOffset = forward ? 1 : 3;
        fixture.Key(deletion ? forward ? 0x2e : 0x08 : forward ? 0x27 : 0x25);
        Assert.Equal(deletion ? "AZ" : "A\r\nZ", fixture.Editor.Text);
        Assert.Equal(deletion || !forward ? 1 : 3, fixture.Editor.CaretOffset);
        if (deletion) { fixture.Editor.Undo(); Assert.Equal("A\r\nZ", fixture.Editor.Text); }
    }

    [Fact]
    public void ProgrammaticSelectionSnapsCrLfOutward()
    {
        using var fixture = new Fixture("A\r\nZ"); fixture.Editor.Select(2, 0);
        Assert.Equal("\r\n", fixture.Editor.SelectedText);
    }

    private sealed class Fixture : IDisposable
    {
        internal EditControl Editor { get; }
        private readonly DisplayedTestWindow _window;
        internal Fixture(string text)
        {
            Keyboard.Initialize(); Keyboard.ClearFocus();
            Editor = new EditControl { Text = text, FontFamily = "Arial", FontSize = 21, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
            _window = new DisplayedTestWindow { Content = Editor, TitleBarStyle = WindowTitleBarStyle.Native };
            _window.Measure(new Size(600, 400)); _window.Arrange(new Rect(0, 0, 600, 400)); Assert.True(Editor.Focus());
        }
        internal void Key(int key, bool shift = false) => DispatchMethod.Invoke(_window, [new PlatformEvent
        { Type = PlatformEventType.KeyDown, KeyCode = key, Modifiers = shift ? 1 : 0 }]);
        public void Dispose() { Keyboard.ClearFocus(); _window.Close(); }
    }
}
