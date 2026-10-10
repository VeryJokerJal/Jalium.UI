using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Reflection;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MessageBoxBehaviorTests : MacOSGeometryTestBase
{
    public MessageBoxBehaviorTests() { Keyboard.Initialize(); Keyboard.ClearFocus(); }

    public static IEnumerable<object[]> DefaultCases()
    {
        foreach (var buttons in new[] { MessageBoxButton.OK, MessageBoxButton.OKCancel, MessageBoxButton.YesNo, MessageBoxButton.YesNoCancel })
        foreach (var requested in new[] { MessageBoxResult.None, MessageBoxResult.OK, MessageBoxResult.Cancel, MessageBoxResult.Yes, MessageBoxResult.No })
        {
            var choices = Results(buttons);
            yield return [buttons, requested, choices.Contains(requested) ? requested : choices[0]];
        }
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void ReturnUsesRequestedDefaultAndLoadedFocus(MessageBoxButton buttons, MessageBoxResult requested, MessageBoxResult expected)
    {
        using var fixture = new Fixture(buttons, requested);
        fixture.Dialog.SetLoadedState(true);
        var focused = Assert.IsType<Button>(Keyboard.FocusedElement);
        Assert.True(focused.IsDefault);
        fixture.Key(Key.Enter);
        Assert.True(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Equal(expected, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public void EscapeDismissesOnlyWhenAResponseExists(MessageBoxButton buttons, MessageBoxResult expected)
    {
        using var fixture = new Fixture(buttons, Results(buttons)[0]);
        fixture.Key(Key.Escape);
        Assert.True(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Equal(expected, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public void YesNoRequiresAnExplicitSelectionAfterEscapeAndRepeatedClose()
    {
        using var fixture = new Fixture(MessageBoxButton.YesNo, MessageBoxResult.No);
        Assert.False(fixture.Dialog.IsShowCloseButton);
        fixture.Key(Key.Escape);
        fixture.Dialog.Close();
        fixture.Dialog.Close();
        Assert.False(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Null(fixture.Dialog.DialogResult);
        Assert.Equal(0, fixture.Closed);
        fixture.Buttons[0].PerformClick();
        Assert.True(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Equal(MessageBoxResult.Yes, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public void CancelledButtonCloseIsNotImmediatelyRetried()
    {
        using var fixture = new Fixture(MessageBoxButton.YesNo, MessageBoxResult.Yes);
        int attempts = 0;
        EventHandler<System.ComponentModel.CancelEventArgs> veto = (_, e) => { attempts++; e.Cancel = true; };
        fixture.Dialog.Closing += veto;
        fixture.Buttons[0].PerformClick();
        Assert.Equal(1, attempts);
        Assert.False(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Null(fixture.Dialog.DialogResult);
        fixture.Dialog.Closing -= veto;
        fixture.Buttons[1].PerformClick();
        Assert.Equal(MessageBoxResult.No, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public void YesNoCanEndWhenTheOwnerIsAlreadyClosing()
    {
        using var fixture = new Fixture(MessageBoxButton.YesNo, MessageBoxResult.Yes);
        var owner = new Window();
        fixture.Dialog.Owner = owner;
        SetField(owner, "_isClosing", true);
        fixture.Dialog.Close();
        Assert.True(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Equal(MessageBoxResult.No, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
        SetField(owner, "_isClosing", false);
        owner.Close();
    }

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public void WindowCloseDoesNotClickTheDefault(MessageBoxButton buttons, MessageBoxResult expected)
    {
        using var fixture = new Fixture(buttons, Results(buttons)[0]);
        fixture.Dialog.Close();
        Assert.True(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Equal(expected, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    [Fact]
    public void TabAndSpaceChooseTheFocusedResponse()
    {
        using var fixture = new Fixture(MessageBoxButton.OKCancel, MessageBoxResult.OK);
        fixture.Dialog.SetLoadedState(true);
        fixture.Key(Key.Tab);
        Assert.Same(fixture.Buttons[1], Keyboard.FocusedElement);
        fixture.Key(Key.Space);
        Assert.Equal(MessageBoxResult.Cancel, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    [Theory]
    [InlineData(Visibility.Hidden)]
    [InlineData(Visibility.Collapsed)]
    public void HiddenCancelIsNotAnEscapeTarget(Visibility visibility)
    {
        using var fixture = new Fixture(MessageBoxButton.OKCancel, MessageBoxResult.OK);
        fixture.Buttons[1].Visibility = visibility;
        fixture.Key(Key.Escape);
        Assert.False(fixture.Dialog.IsClosedForPlatformTermination);
        Assert.Equal(0, fixture.Closed);
        fixture.Buttons[0].PerformClick();
        Assert.Equal(MessageBoxResult.OK, fixture.Dialog.Result);
    }

    [Fact]
    public void ShortMessageTabTraversalCyclesThroughResponses()
    {
        using var fixture = new Fixture(MessageBoxButton.OKCancel, MessageBoxResult.OK);
        fixture.Dialog.SetLoadedState(true);
        fixture.Key(Key.Tab);
        Assert.Same(fixture.Buttons[1], Keyboard.FocusedElement);
        fixture.Key(Key.Tab);
        Assert.Same(fixture.Buttons[0], Keyboard.FocusedElement);
        var scroll = Assert.Single(Descendants(fixture.Dialog.Content as Visual).OfType<ScrollViewer>());
        Assert.False(scroll.Focusable);
    }

    [Theory]
    [InlineData(MessageBoxOptions.None, TextAlignment.Left, FlowDirection.LeftToRight)]
    [InlineData(MessageBoxOptions.RightAlign, TextAlignment.Right, FlowDirection.LeftToRight)]
    [InlineData(MessageBoxOptions.RtlReading, TextAlignment.Left, FlowDirection.RightToLeft)]
    [InlineData(MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading, TextAlignment.Right, FlowDirection.RightToLeft)]
    public void AlignmentAndReadingDirectionApplyIndependentlyToTheMessage(MessageBoxOptions options, TextAlignment alignment, FlowDirection direction)
    {
        using var fixture = new Fixture(MessageBoxButton.OKCancel, MessageBoxResult.OK, options);
        var text = Descendants(fixture.Dialog.Content as Visual).OfType<TextBlock>().Single(x => x.Text == Fixture.Message);
        Assert.Equal(alignment, text.TextAlignment);
        Assert.Equal(direction, text.FlowDirection);
        Assert.All(fixture.Buttons, button => Assert.Equal(FlowDirection.LeftToRight, button.FlowDirection));
    }

    [Fact]
    public void LongMessageRemainsInsideTheWindowAndCanScrollWithoutHidingResponses()
    {
        string message = string.Join("\n", Enumerable.Repeat("中文记录🙂，请检查长文本的换行与末尾。", 100));
        using var fixture = new Fixture(MessageBoxButton.OKCancel, MessageBoxResult.OK, message: message);
        var scroll = Assert.Single(Descendants(fixture.Dialog.Content as Visual).OfType<ScrollViewer>());
        Assert.True(scroll.ViewportHeight > 0);
        Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
        Assert.True(scroll.ViewportWidth <= 360);
        Assert.True(fixture.Dialog.DesiredSize.Height < SystemParameters.WorkArea.Height);
        scroll.ScrollToBottom();
        Assert.True(scroll.VerticalOffset > 0);
        fixture.Key(Key.Escape);
        Assert.Equal(MessageBoxResult.Cancel, fixture.Dialog.Result);
        Assert.Equal(1, fixture.Closed);
    }

    private static MessageBoxResult[] Results(MessageBoxButton button) => button switch
    {
        MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
        MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
        MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
        _ => [MessageBoxResult.OK]
    };
    private static IEnumerable<Visual> Descendants(Visual? root)
    {
        if (root == null) yield break;
        yield return root;
        for (int i = 0; i < root.VisualChildrenCount; i++)
            foreach (var child in Descendants(root.GetVisualChild(i))) yield return child;
    }
    private static void SetField(Window window, string name, object value) => typeof(Window).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);
    private sealed class Fixture : IDisposable
    {
        internal const string Message = "مرحبا بالعالم\n中文短行🙂";
        internal MessageBoxDialog Dialog { get; }
        internal Button[] Buttons { get; }
        private readonly WindowInputDispatcher _input;
        internal int Closed { get; private set; }
        internal Fixture(MessageBoxButton buttons, MessageBoxResult requested, MessageBoxOptions options = MessageBoxOptions.None, string message = Message)
        {
            Dialog = new MessageBoxDialog(message, "Owned message", buttons, MessageBoxImage.Information, requested, options)
                { TitleBarStyle = WindowTitleBarStyle.Native };
            if (OperatingSystem.IsMacOS())
                typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Dialog, [Visibility.Visible]);
            Dialog.Measure(new Size(400, 10000));
            Dialog.Arrange(new Rect(0, 0, 400, Dialog.DesiredSize.Height));
            Buttons = Descendants(Dialog.Content as Visual).OfType<Button>().ToArray();
            Dialog.Closed += (_, _) => Closed++;
            SetField(Dialog, "_isModal", true);
            _input = new WindowInputDispatcher(Dialog);
        }
        internal void Key(Key key)
        {
            _input.HandleKeyDown(key, ModifierKeys.None, false, 1);
            _input.HandleKeyUp(key, ModifierKeys.None, 2);
        }
        public void Dispose()
        {
            Keyboard.ClearFocus();
            if (!Dialog.IsClosedForPlatformTermination) Buttons.Last().PerformClick();
            Dialog.Close();
        }
    }
}
