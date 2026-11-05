using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssCaretAnimationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("auto")]
    [InlineData("manual")]
    [InlineData("inherit")]
    public void AcceptsCaretAnimationKeywords(string value)
        => Assert.True(Css.Supports("caret-animation", value));

    [Theory]
    [InlineData("blink")]
    [InlineData("manual auto")]
    [InlineData("none")]
    public void RejectsOtherCaretAnimationValues(string value)
        => Assert.False(Css.Supports("caret-animation", value));

    [Fact]
    public void ManualModeInheritsAndExplicitAutoRestoresNativeAnimation()
    {
        var parent = new StackPanel();
        var child = new TextBox();
        parent.Children.Add(child);
        Invoke(typeof(TextBoxBase), child, "StartCaretTimer");
        var timer = Assert.IsType<DispatcherTimer>(Field(typeof(TextBoxBase), child, "_caretTimer"));
        Assert.True(timer.IsEnabled);
        Css.SetStyle(parent, "caret-animation: manual");
        Assert.True(CssCaretAnimationProperties.IsManual(child));
        Assert.False(timer.IsEnabled);

        Css.SetStyle(child, "caret-animation: auto");
        Assert.False(CssCaretAnimationProperties.IsManual(child));
        Css.SetStyle(child, "caret-animation: inherit");
        Assert.True(CssCaretAnimationProperties.IsManual(child));
        Css.SetStyle(child, "caret-animation: initial");
        Assert.False(CssCaretAnimationProperties.IsManual(child));
        Css.SetStyle(child, "caret-animation: unset");
        Assert.True(CssCaretAnimationProperties.IsManual(child));
        Css.SetStyle(parent, string.Empty);
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.False(CssCaretAnimationProperties.IsManual(child));
        Invoke(typeof(TextBoxBase), child, "StopCaretTimer");
    }

    [Fact]
    public void ManualModeKeepsTextCaretVisibleAndStopsItsTimer()
    {
        var box = new TextBox();
        Invoke(typeof(TextBoxBase), box, "StartCaretTimer");
        var timer = Assert.IsType<DispatcherTimer>(Field(typeof(TextBoxBase), box, "_caretTimer"));
        Assert.True(timer.IsEnabled);

        Css.SetStyle(box, "caret-animation: manual");
        Assert.False(timer.IsEnabled);
        SetField(typeof(TextBoxBase), box, "_lastCaretBlink", DateTime.Now.AddMilliseconds(-900));
        Assert.Equal(1.0, (double)Invoke(typeof(TextBoxBase), box, "UpdateCaretAnimation")!);
        Invoke(typeof(TextBoxBase), box, "StartCaretTimer");
        Assert.False(timer.IsEnabled);

        Css.SetStyle(box, "caret-animation: auto");
        SetField(typeof(TextBoxBase), box, "_lastCaretBlink", DateTime.Now.AddMilliseconds(-900));
        Assert.Equal(0.0, (double)Invoke(typeof(TextBoxBase), box, "UpdateCaretAnimation")!);
        Invoke(typeof(TextBoxBase), box, "StartCaretTimer");
        Assert.True(timer.IsEnabled);
        Invoke(typeof(TextBoxBase), box, "StopCaretTimer");
    }

    [Fact]
    public void ManualModeStopsRichTextAndPasswordCaretTimers()
    {
        var rich = new RichTextBox();
        Invoke(typeof(TextBoxBase), rich, "StartCaretTimer");
        Invoke(typeof(RichTextBox), rich, "StartCaretTimer");
        var baseTimer = Assert.IsType<DispatcherTimer>(Field(typeof(TextBoxBase), rich, "_caretTimer"));
        var richTimer = Assert.IsType<DispatcherTimer>(Field(typeof(RichTextBox), rich, "_caretTimer"));
        Assert.True(baseTimer.IsEnabled);
        Assert.True(richTimer.IsEnabled);

        Css.SetStyle(rich, "caret-animation: manual");
        Assert.False(baseTimer.IsEnabled);
        Assert.False(richTimer.IsEnabled);
        SetField(typeof(RichTextBox), rich, "_lastCaretBlink", DateTime.Now.AddMilliseconds(-900));
        Assert.Equal(1.0, (double)Invoke(typeof(RichTextBox), rich, "UpdateRichCaretAnimation")!);

        var password = new PasswordBox();
        Invoke(typeof(PasswordBox), password, "StartCaretTimer");
        var passwordTimer = Assert.IsType<DispatcherTimer>(Field(typeof(PasswordBox), password, "_caretTimer"));
        Assert.True(passwordTimer.IsEnabled);
        Css.SetStyle(password, "caret-animation: manual");
        Assert.False(passwordTimer.IsEnabled);
        SetField(typeof(PasswordBox), password, "_lastCaretBlink", DateTime.Now.AddMilliseconds(-900));
        Assert.Equal(1.0, (double)Invoke(typeof(PasswordBox), password, "UpdateCaretAnimation")!);
    }

    [Fact]
    public void ManualModeStopsCodeAndHexEditorBlinking()
    {
        var edit = new EditControl();
        var caret = Field(typeof(EditControl), edit, "_caret")!;
        Invoke(caret.GetType(), caret, "StartBlinking");
        SetField(caret.GetType(), caret, "_lastResetTime", DateTime.UtcNow.AddMilliseconds(-900));
        Assert.Equal(0.0, (double)caret.GetType().GetProperty("Opacity")!.GetValue(caret)!);
        Invoke(typeof(EditControl), edit, "StartCaretTimer");
        var editorTimer = Assert.IsType<DispatcherTimer>(Field(typeof(EditControl), edit, "_caretTimer"));
        Assert.True(editorTimer.IsEnabled);

        Css.SetStyle(edit, "caret-animation: manual");
        Assert.False(editorTimer.IsEnabled);
        Assert.Equal(1.0, (double)caret.GetType().GetProperty("Opacity")!.GetValue(caret)!);
        Invoke(typeof(EditControl), edit, "StartCaretTimer");
        Assert.Null(Field(typeof(EditControl), edit, "_caretTimer"));

        var hex = new HexEditor();
        Css.SetStyle(hex, "caret-animation: manual");
        SetField(typeof(HexEditor), hex, "_caretVisible", false);
        Invoke(typeof(HexEditor), hex, "UpdateCaretBlink");
        Assert.True((bool)Field(typeof(HexEditor), hex, "_caretVisible")!);
    }

    private static object? Invoke(Type type, object target, string method)
        => type.GetMethod(method, PrivateInstance | BindingFlags.Public)!.Invoke(target, null);

    private static object? Field(Type type, object target, string name)
        => type.GetField(name, PrivateInstance)!.GetValue(target);

    private static void SetField(Type type, object target, string name, object value)
        => type.GetField(name, PrivateInstance)!.SetValue(target, value);
}
