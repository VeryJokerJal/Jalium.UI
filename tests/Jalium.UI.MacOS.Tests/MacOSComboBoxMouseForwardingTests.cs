using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSComboBoxMouseForwardingTests
{
    public static IEnumerable<object[]> Snapshots()
    {
        foreach (bool flag in new[] { false, true })
        foreach (var item in new (ModifierKeys? Physical, ModifierKeys Logical, bool Opens)[]
        {
            (ModifierKeys.None, ModifierKeys.None, false),
            (ModifierKeys.Windows, ModifierKeys.Control, false),
            (ModifierKeys.Control, ModifierKeys.Control, true),
            (ModifierKeys.Control | ModifierKeys.Windows, ModifierKeys.Control | ModifierKeys.Windows, true),
            (ModifierKeys.Windows | ModifierKeys.Shift, ModifierKeys.Control | ModifierKeys.Shift, false),
            (ModifierKeys.Windows | ModifierKeys.Alt, ModifierKeys.Control | ModifierKeys.Alt, false),
            (null, ModifierKeys.Control, true),
            (ModifierKeys.None, ModifierKeys.Control, false),
        })
            yield return [flag, item.Physical, item.Logical, item.Opens];
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void EditablePaddingRetainsBothSnapshotsAndClickFields(
        bool readOnly, ModifierKeys? physical, ModifierKeys logical, bool unused)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(readOnly);
        var previous = PlatformTextContextMenus.Show;
        PlatformTextContextMenus.Show = null;
        try
        {
            MouseButtonEventArgs? seen = null;
            host.Editor.AddHandler(UIElement.MouseDownEvent,
                new MouseButtonEventHandler((_, e) => seen = e), handledEventsToo: true);
            var input = host.Input(physical, logical);
            host.Combo.RaiseEvent(input);
            Assert.NotNull(seen);
            Assert.NotSame(input, seen);
            Assert.Equal(physical, seen.PhysicalModifiers);
            Assert.Equal(logical, seen.KeyboardModifiers);
            Assert.Equal(input.ChangedButton, seen.ChangedButton);
            Assert.Equal(input.ButtonState, seen.ButtonState);
            Assert.Equal(input.ClickCount, seen.ClickCount);
            Assert.Equal(input.Timestamp, seen.Timestamp);
            Assert.Equal(input.Position, seen.Position);
            Assert.Equal(input.LeftButton, seen.LeftButton);
            Assert.Equal(input.MiddleButton, seen.MiddleButton);
            Assert.Equal(input.RightButton, seen.RightButton);
            Assert.Equal(input.XButton1, seen.XButton1);
            Assert.Equal(input.XButton2, seen.XButton2);
            Assert.Equal("窗口 Miii 中文🙂", host.Editor.Text);
            Assert.Equal(readOnly, host.Editor.IsReadOnly);
        }
        finally { PlatformTextContextMenus.Show = previous; }
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void ForwardedCommandClickDoesNotBecomeAControlContextMenu(
        bool shown, ModifierKeys? physical, ModifierKeys logical, bool opens)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var host = new Host(false);
        var previous = PlatformTextContextMenus.Show;
        int menus = 0;
        typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(host.Window, (nint)123);
        PlatformTextContextMenus.Show = request =>
        {
            Assert.True(request.IsCurrent());
            menus++;
            return shown;
        };
        try
        {
            Assert.True(host.Editor.Focus());
            host.Combo.RaiseEvent(host.Input(physical, logical));
            Assert.Equal(opens ? 1 : 0, menus);
            Assert.Equal("窗口 Miii 中文🙂", host.Editor.Text);
        }
        finally
        {
            PlatformTextContextMenus.Show = previous;
            typeof(Window).GetProperty(nameof(Window.Handle))!.SetValue(host.Window, (nint)0);
        }
    }

    private sealed class Host : IDisposable
    {
        internal readonly DisplayedTestWindow Window;
        internal readonly ComboBox Combo;
        internal readonly TextBox Editor;
        internal Host(bool readOnly)
        {
            Keyboard.Initialize();
            Keyboard.ClearFocus();
            Mouse.Capture(null);
            Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            TextBox? editor = null;
            var template = new ControlTemplate(typeof(ComboBox));
            template.SetVisualTree(() => new Border
            {
                Padding = new Thickness(12, 4, 36, 4),
                Background = Brushes.White,
                Child = editor = new TextBox
                {
                    Name = "PART_EditableTextBox", Text = "窗口 Miii 中文🙂",
                    Background = Brushes.White,
                },
            });
            Combo = new ComboBox { Template = template, IsEditable = true, IsReadOnly = readOnly,
                Width = 280, Height = 56, Text = "窗口 Miii 中文🙂" };
            Window = new DisplayedTestWindow { Width = 360, Height = 180,
                TitleBarStyle = WindowTitleBarStyle.Native, Content = Combo };
            Window.Measure(new Size(360, 180));
            Window.Arrange(new Rect(0, 0, 360, 180));
            Editor = Assert.IsType<TextBox>(editor);
        }
        internal MouseButtonEventArgs Input(ModifierKeys? physical, ModifierKeys logical) => new(
            UIElement.MouseDownEvent, new Point(2, 10), MouseButton.Left, MouseButtonState.Pressed, 1,
            MouseButtonState.Pressed, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Pressed, MouseButtonState.Released, logical, 913)
            { PhysicalModifiers = physical };
        public void Dispose()
        {
            Mouse.Capture(null);
            Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
            Keyboard.ClearFocus();
            Window.Close();
        }
    }
}
