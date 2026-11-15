using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSTextEditingMenuTemplateTests
{
    [Fact]
    public void EditingActionsUseStandardRowsAndCurrentSelectionUndoAndClipboard()
    {
        using var scope = new FallbackScope();
        var original = Clipboard.GetDataObject();
        try
        {
            Clipboard.SetText("Paste");
            var text = Create("alpha beta");
            text.Select(0, 5);
            var menu = Open(text);
            Assert.True(menu.IsOpen);
            Assert.Equal("alpha", text.SelectedText);
            Assert.Equal(new[] { "Undo", "Redo", "Cut", "Copy", "Paste", "Select All" }, menu.Items.OfType<MenuItem>().Select(x => (string)x.Header!));
            Assert.Equal(2, menu.Items.OfType<Separator>().Count());
            Assert.IsType<Border>(menu.FindName("PART_PopupBorder"));
            Invoke(menu, "Cut");
            Assert.False(menu.IsOpen);
            Assert.Equal(" beta", text.Text);
            Assert.Equal("alpha", Clipboard.GetText());
            Invoke(Open(text), "Undo");
            Assert.Equal("alpha beta", text.Text);
            Invoke(Open(text), "Redo");
            Assert.Equal(" beta", text.Text);
            text.Select(0, 0);
            Clipboard.SetText("Z");
            Invoke(Open(text), "Paste");
            Assert.Equal("Z beta", text.Text);
            Invoke(Open(text), "Select All");
            Assert.Equal(text.Text, text.SelectedText);
        }
        finally
        {
            if (original != null)
                Clipboard.SetDataObject(original);
            else
                Clipboard.Clear();
        }
    }

    [Fact]
    public void ReadOnlyAndEmptyStatesExposeOnlyAvailableActions()
    {
        using var scope = new FallbackScope();
        var text = Create("Read only");
        text.IsReadOnly = true;
        text.SelectAll();
        var menu = Open(text);
        Assert.Equal(new[] { "Copy", "Select All" }, menu.Items.OfType<MenuItem>().Select(x => (string)x.Header!));
        Assert.All(menu.Items.OfType<MenuItem>(), x => Assert.True(x.IsEnabled));
        menu.IsOpen = false;
        text.Text = "";
        menu = Open(text);
        Assert.All(menu.Items.OfType<MenuItem>(), x => Assert.False(x.IsEnabled));
        menu.IsOpen = false;
    }

    [Theory]
    [InlineData(Key.F10, ModifierKeys.Shift)]
    [InlineData(Key.Apps, ModifierKeys.None)]
    public void KeyboardOpensAndEscapeClosesTheDefaultMenu(Key key, ModifierKeys modifiers)
    {
        using var scope = new FallbackScope();
        var text = Create("Keyboard");
        var keyEvent = new KeyEventArgs(UIElement.KeyDownEvent, key, modifiers, true, false, 0);
        text.RaiseEvent(keyEvent);
        Assert.True(keyEvent.Handled);
        var menu = Assert.IsType<ContextMenu>(Current(text));
        Assert.True(menu.IsOpen);
        Assert.Same(text, menu.PlacementTarget);
        menu.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Escape, ModifierKeys.None, true, false, 0));
        Assert.False(menu.IsOpen);
        Assert.Null(Current(text));
    }

    [Fact]
    public void CallerMenuAndLocalNullRetainTheirMeaning()
    {
        using var scope = new FallbackScope();
        var text = Create("Caller");
        var supplied = new ContextMenu();
        supplied.Items.Add(new MenuItem { Header = "Caller" });
        var parent = new Border
        {
            Child = text
        };
        ContextMenuService.SetContextMenu(parent, supplied);
        Assert.True(Request(text));
        Assert.True(supplied.IsOpen);
        Assert.Null(Current(text));
        supplied.IsOpen = false;
        parent.Child = null;
        ContextMenuService.SetContextMenu(text, null);
        Assert.False(Request(text));
        Assert.Null(Current(text));
    }

    [Fact]
    public void CancelledRequestDoesNotOpenOrConsumeTheKeyboardRequest()
    {
        using var scope = new FallbackScope();
        var text = Create("Cancelled");
        int opening = 0;
        ContextMenuService.AddContextMenuOpeningHandler(text, (_, e) =>
        {
            opening++;
            e.Handled = true;
        });
        var keyEvent = new KeyEventArgs(UIElement.KeyDownEvent, Key.F10, ModifierKeys.Shift, true, false, 0);
        text.RaiseEvent(keyEvent);
        Assert.False(keyEvent.Handled);
        Assert.Equal(1, opening);
        Assert.False(Current(text)?.IsOpen ?? false);
    }

    [Fact]
    public void DisabledServiceAndUnloadingCloseOrRejectTheBuiltInMenu()
    {
        using var scope = new FallbackScope();
        var text = Create("Lifecycle");
        ContextMenuService.SetIsEnabled(text, false);
        Assert.False(Request(text));
        ContextMenuService.SetIsEnabled(text, true);
        text.IsEnabled = false;
        Assert.False(Request(text));
        text.IsEnabled = true;
        typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, new object[] { true });
        var menu = Open(text);
        int closed = 0;
        menu.Closed += (_, _) => closed++;
        typeof(FrameworkElement).GetMethod("SetLoadedState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, new object[] { false });
        Assert.False(menu.IsOpen);
        Assert.Null(Current(text));
        Assert.Equal(1, closed);
    }

    private static TextBox Create(string value) => new()
    {
        Text = value,
        Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(TextBox)]
    };
    private static bool Request(TextBoxBase text) => (bool)typeof(TextBoxBase).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(text, new object[] { true })!;
    private static ContextMenu? Current(TextBoxBase text) => (ContextMenu? )typeof(TextBoxBase).GetField("_editingContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(text);
    private static ContextMenu Open(TextBoxBase text)
    {
        Assert.True(Request(text));
        var menu = Assert.IsType<ContextMenu>(Current(text));
        menu.ApplyTemplate();
        return menu;
    }

    private static void Invoke(ContextMenu menu, string name) => ((IInvokeProvider)new MenuItemAutomationPeer(menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, name)))).Invoke();
    private sealed class FallbackScope : IDisposable
    {
        private readonly Func<PlatformTextContextMenuRequest, bool>? _native = PlatformTextContextMenus.Show;
        internal FallbackScope()
        {
            PlatformTextContextMenus.Show = null;
            LegacyMenuTemplateTestHelper.Register();
        }

        public void Dispose() => PlatformTextContextMenus.Show = _native;
    }
}
