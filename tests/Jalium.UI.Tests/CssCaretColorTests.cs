using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssCaretColorTests
{
    [Theory]
    [InlineData("auto")]
    [InlineData("red")]
    [InlineData("#123456")]
    [InlineData("currentColor")]
    [InlineData("color-mix(in srgb, currentColor 50%, blue)")]
    [InlineData("inherit")]
    public void AcceptsColorAndAuto(string value)
        => Assert.True(Css.Supports("caret-color", value));

    [Theory]
    [InlineData("none")]
    [InlineData("auto red")]
    [InlineData("url(caret.png)")]
    public void RejectsNonColorValues(string value)
        => Assert.False(Css.Supports("caret-color", value));

    [Fact]
    public void TextBoxCaretUsesCssUntilNativeLocalBrushTakesPriority()
    {
        var box = new TextBox();
        Css.SetStyle(box, "caret-color: #123456");
        Assert.Equal(0xFF123456u, ColorOf(ResolveTextCaret(box)));

        var nativeBrush = new SolidColorBrush(Colors.Blue);
        box.CaretBrush = nativeBrush;
        Assert.Same(nativeBrush, ResolveTextCaret(box));

        box.CaretBrush = null;
        Assert.Null(ResolveTextCaret(box));

        box.ClearValue(TextBoxBase.CaretBrushProperty);
        Assert.Equal(0xFF123456u, ColorOf(ResolveTextCaret(box)));
        Css.SetStyle(box, string.Empty);
        Assert.Null(CssCaretColorProperties.Get(box));
        Assert.NotEqual(0xFF123456u, ColorOf(ResolveTextCaret(box)));
    }

    [Fact]
    public void InheritedColorReachesChildEditorsAndAutoResetsIt()
    {
        var parent = new StackPanel();
        var box = new TextBox();
        parent.Children.Add(box);
        Css.SetStyle(parent, "caret-color: red");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolveTextCaret(box)));

        Css.SetStyle(box, "caret-color: initial");
        Assert.Null(CssCaretColorProperties.Get(box));
        Css.SetStyle(box, "caret-color: inherit");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolveTextCaret(box)));
        Css.SetStyle(box, "caret-color: auto");
        Assert.Null(CssCaretColorProperties.Get(box));
        Css.SetStyle(box, "caret-color: unset");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolveTextCaret(box)));
        Css.SetStyle(parent, "caret-color: blue");
        CssEvaluationScheduler.FlushIfPending(box.Dispatcher);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(ResolveTextCaret(box)));
        Css.SetStyle(box, string.Empty);
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(ResolveTextCaret(box)));
        Css.SetStyle(parent, string.Empty);
        Assert.Null(CssCaretColorProperties.Get(box));
    }

    [Fact]
    public void CurrentColorTracksForegroundChanges()
    {
        var foreground = new SolidColorBrush(Colors.Blue);
        var box = new TextBox { Foreground = foreground };
        Css.SetStyle(box, "caret-color: currentColor");
        Assert.Equal(Colors.Blue.ToArgb(), ColorOf(ResolveTextCaret(box)));

        foreground.Color = Colors.Red;
        CssEvaluationScheduler.FlushIfPending(box.Dispatcher);
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolveTextCaret(box)));

        Css.SetStyle(box, "caret-color: color-mix(in srgb, currentColor 50%, white)");
        Assert.Equal(0xFFFF8080u, ColorOf(ResolveTextCaret(box)));
        foreground.Color = Colors.Blue;
        CssEvaluationScheduler.FlushIfPending(box.Dispatcher);
        Assert.Equal(0xFF8080FFu, ColorOf(ResolveTextCaret(box)));
    }

    [Fact]
    public void InheritedCurrentColorKeepsTheParentsComputedColor()
    {
        var parent = new StackPanel();
        var box = new TextBox();
        parent.Children.Add(box);
        Css.SetStyle(parent, "color: red; caret-color: currentColor");
        Css.SetStyle(box, "color: blue");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolveTextCaret(box)));

        Css.SetStyle(parent, "color: green; caret-color: currentColor");
        CssEvaluationScheduler.FlushIfPending(box.Dispatcher);
        Assert.Equal(Colors.Green.ToArgb(), ColorOf(ResolveTextCaret(box)));
    }

    [Fact]
    public void NativeEditorsUseCssAndPreserveLocalCaretBrush()
    {
        var textEditors = new TextBoxBase[] { new AutoCompleteBox(), new NumberBox() };
        foreach (var editor in textEditors)
        {
            Css.SetStyle(editor, "caret-color: red");
            Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolveTextCaret(editor)));
            editor.CaretBrush = Brushes.Blue;
            Assert.Same(Brushes.Blue, ResolveTextCaret(editor));
        }

        var rich = new RichTextBox();
        Css.SetStyle(rich, "caret-color: red");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolvePrivateCaret(rich)));
        rich.CaretBrush = Brushes.Blue;
        Assert.Same(Brushes.Blue, ResolvePrivateCaret(rich));

        var password = new PasswordBox();
        Css.SetStyle(password, "caret-color: red");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(password.ResolveCaretBrush()));
        password.CaretBrush = Brushes.Blue;
        Assert.Same(Brushes.Blue, password.ResolveCaretBrush());
        password.CaretBrush = null;
        Assert.Null(password.ResolveCaretBrush());

        var edit = new EditControl();
        Css.SetStyle(edit, "caret-color: red");
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(ResolvePrivateCaret(edit)));
        edit.CaretBrush = Brushes.Blue;
        Assert.Same(Brushes.Blue, ResolvePrivateCaret(edit));
    }

    [Fact]
    public void HexEditorPaintsTheCssCaretColor()
    {
        var editor = new HexEditor { Data = [0x41] };
        Css.SetStyle(editor, "caret-color: red");
        CssBorderRadiusTests.Layout(editor, 320, 100);

        var drawing = new CaretDrawingContext();
        typeof(HexEditor).GetMethod("DrawCaret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [drawing]);
        Assert.Equal(Colors.Red.ToArgb(), ColorOf(Assert.Single(drawing.Rectangles).Brush));

        Css.SetStyle(editor, string.Empty);
        drawing.Rectangles.Clear();
        typeof(HexEditor).GetMethod("DrawCaret", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, [drawing]);
        Assert.Equal(Colors.White.ToArgb(), ColorOf(Assert.Single(drawing.Rectangles).Brush));
    }

    private static Brush? ResolveTextCaret(TextBoxBase editor)
        => (Brush?)typeof(TextBoxBase).GetMethod("ResolveCaretBrush", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, null);

    private static Brush? ResolvePrivateCaret(object editor)
        => (Brush?)editor.GetType().GetMethod("ResolveCaretBrush", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, null);

    private static uint ColorOf(Brush? brush)
        => Assert.IsType<SolidColorBrush>(brush).Color.ToArgb();

    private sealed class CaretDrawingContext : DrawingContextAdapter
    {
        internal readonly List<(Brush? Brush, Rect Bounds)> Rectangles = [];
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add((brush, rectangle));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
