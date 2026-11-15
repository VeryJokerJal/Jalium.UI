using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPlaceholderTests
{
    [Fact]
    public void PlaceholderIsARealDoubleColonPseudoElement()
    {
        Assert.Empty(CssStyleSheet.Parse("TextBox::placeholder{color:red;}").Diagnostics);
        Assert.Empty(CssStyleSheet.Parse(".subject::PLACEHOLDER{color:red;}").Diagnostics);
        Assert.NotEmpty(CssStyleSheet.Parse("TextBox:placeholder{color:red;}").Diagnostics);
        Assert.NotEmpty(CssStyleSheet.Parse("TextBox::placeholder::before{color:red;}").Diagnostics);
    }

    [Theory]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("auto", false)]
    [InlineData("auto", true)]
    [InlineData("number", false)]
    [InlineData("number", true)]
    [InlineData("password", false)]
    [InlineData("password", true)]
    [InlineData("date", false)]
    [InlineData("time", false)]
    [InlineData("combo", true)]
    [InlineData("combo-editable", true)]
    [InlineData("tree", true)]
    public void TypographyTargetsTheDisplayedHintAndRemovingTheSheetRestoresIt(string kind, bool hosted)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene(kind, hosted); Layout(root);
        var ownerSize = input.FontSize; var ownerInk = input.Foreground;
        var sheet = CssStyleSheet.Parse(".subject::placeholder{color:#b04440;font-size:32px;font-style:italic;letter-spacing:4px;word-spacing:6px;text-transform:uppercase;}");
        Css.GetStyleSheets(root).Add(sheet); Layout(root);
        var capture = new Capture(); root.Render(capture);
        Assert.Equal("HINT TEXT", string.Concat(capture.Text.Select(item => item.Text)));
        Assert.NotEmpty(capture.Text);
        Assert.All(capture.Text, item => { Assert.Equal(32, item.Size); Assert.Equal(Color.FromRgb(176, 68, 64), Assert.IsType<SolidColorBrush>(item.Ink).Color); });
        Assert.Equal(ownerSize, input.FontSize); Assert.Same(ownerInk, input.Foreground);
        Assert.True(CssPlaceholderState.IsShown(input));
        Css.GetStyleSheets(root).Clear(); Layout(root); capture = new Capture(); root.Render(capture);
        Assert.Equal("hint text", string.Concat(capture.Text.Select(item => item.Text)));
        Assert.All(capture.Text, item => Assert.Equal(ownerSize, item.Size));
        Assert.True(CssPlaceholderState.IsShown(input));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("combo")]
    [InlineData("combo-editable")]
    [InlineData("tree")]
    public void VariablesAndRelativeFontsUseTheOriginatingControlContext(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene(kind, kind.StartsWith("combo", StringComparison.Ordinal) || kind == "tree");
        Css.SetStyle(root, "--bump:8px;--hint:#b04440;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{font-size:calc(1em + var(--bump));color:var(--hint);}"));
        Layout(root); var capture = new Capture(); root.Render(capture);
        Assert.NotEmpty(capture.Text); Assert.All(capture.Text, item => Assert.Equal(32, item.Size));
        Css.SetStyle(root, "--bump:12px;--hint:#20537a;"); Layout(root); capture = new Capture(); root.Render(capture);
        Assert.NotEmpty(capture.Text); Assert.All(capture.Text, item =>
        { Assert.Equal(36, item.Size); Assert.Equal(Color.FromRgb(32, 83, 122), Assert.IsType<SolidColorBrush>(item.Ink).Color); });
        Assert.Equal(24, input.FontSize); Assert.True(CssPlaceholderState.IsShown(input));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("combo")]
    [InlineData("combo-editable")]
    [InlineData("tree")]
    public void EnteredValuesDoNotAcquireTheHintStyleAndClearingRestoresIt(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene(kind, kind.StartsWith("combo", StringComparison.Ordinal) || kind == "tree");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject:placeholder-shown::placeholder{font-size:32px;color:#b04440;}"));
        void Value(bool filled)
        {
            switch (input)
            {
                case TextBox box: box.Text = filled ? "typed" : ""; break;
                case NumberBox box: box.Text = filled ? "42" : ""; break;
                case PasswordBox box: box.Password = filled ? "typed" : ""; break;
                case DatePicker box: box.Text = filled ? "2026/01/02" : ""; break;
                case TimePicker box: box.SelectedTime = filled ? new TimeSpan(10, 30, 0) : null; break;
                case ComboBox box:
                    if (box.Items.Count == 0) box.Items.Add("typed");
                    box.SelectedIndex = filled ? 0 : -1; if (box.IsEditable) box.Text = filled ? "typed" : ""; break;
                case TreeSelector box: box.SearchText = filled ? "typed" : ""; break;
            }
        }
        Value(true); Layout(root); Assert.False(CssPlaceholderState.IsShown(input));
        var capture = new Capture(); root.Render(capture); Assert.DoesNotContain(capture.Text, item => item.Size == 32);
        Value(false); Layout(root); Assert.True(CssPlaceholderState.IsShown(input));
        capture = new Capture(); root.Render(capture); Assert.NotEmpty(capture.Text); Assert.All(capture.Text, item => Assert.Equal(32, item.Size));
        Assert.Equal(24, input.FontSize);
    }

    [Fact]
    public void InapplicableBoxAndInlineLayoutDeclarationsCannotChangeTheHintOrControl()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene("text", false);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{color:red;opacity:.5;content:'wrong';display:none;width:900px;height:200px;margin:20px;padding:40px;line-height:100px;vertical-align:super;direction:rtl;writing-mode:vertical-rl;text-orientation:upright;}"));
        Layout(root); var capture = new Capture(); root.Render(capture);
        Assert.Equal("hint text", string.Concat(capture.Text.Select(item => item.Text)));
        Assert.Equal(1, input.Opacity); Assert.Equal(300, input.Width); Assert.Equal(96, input.Height);
        var view = CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray)!;
        Assert.Equal(.5, view.Opacity); Assert.True(double.IsNaN(view.Width)); Assert.True(double.IsNaN(view.Height)); Assert.NotEqual(100, view.LineHeight);
        Assert.Equal(FlowDirection.LeftToRight, view.FlowDirection); Assert.Equal(default, view.Margin);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    public void InlineHintBackgroundFollowsTheTextAdvance(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, _) = Scene(kind, false);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{background-color:#fff0c0;color:#b04440;}"));
        Layout(root); var capture = new Capture(); root.Render(capture);
        var backgrounds = capture.Rectangles.Where(item => item.Ink is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 240, 192)).ToArray();
        Assert.NotEmpty(backgrounds); Assert.All(backgrounds, item => Assert.InRange(item.Rect.Width, 40, 289));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("auto")]
    [InlineData("number")]
    [InlineData("password")]
    [InlineData("date")]
    [InlineData("time")]
    public void AnExplicitHintFontCanRenderWhenTheControlFontIsZero(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene(kind, false); input.FontSize = 0;
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{font-size:32px;color:#b04440;}"));
        Layout(root); var capture = new Capture(); root.Render(capture);
        Assert.NotEmpty(capture.Text); Assert.All(capture.Text, item => Assert.Equal(32, item.Size)); Assert.Equal(0, input.FontSize);
    }

    [Theory]
    [InlineData("combo")]
    [InlineData("tree")]
    [InlineData("time")]
    public void RetiredTemplatePartsLoseTheirPlaceholderOrigin(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene(kind, true); Layout(root);
        FrameworkElement? Part(DependencyObject node)
        {
            if (node is FrameworkElement { Name: "PART_SelectionPresenter" or "PART_PlaceholderText" or "PART_DisplayText" } match) return match;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (Part(VisualTreeHelper.GetChild(node, i)) is { } child) return child;
            return null;
        }
        var part = Assert.IsAssignableFrom<FrameworkElement>(Part(input)); Assert.Same(input, CssPlaceholder.AliasOwner(part));
        input.Template = null; Layout(root); Assert.Null(CssPlaceholder.AliasOwner(part));
    }

    [Fact]
    public void PageRulesDoNotPierceUnrelatedControlTemplateInputs()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var host = new Control { Width = 300, Height = 96, FontSize = 24, FontFamily = new FontFamily("Georgia") };
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() => { var input = new TextBox { PlaceholderText = "hint text", Background = null, BorderThickness = default, Padding = default }; Css.SetClass(input, "internal-hint"); return input; });
        host.Template = template;
        var root = new StackPanel { Width = 300, Height = 220 }; root.Children.Add(host);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".internal-hint::placeholder{font-size:32px;color:red;}"));
        Layout(root); var capture = new Capture(); root.Render(capture);
        Assert.NotEmpty(capture.Text); Assert.All(capture.Text, item => Assert.Equal(24, item.Size));
    }

    [Theory]
    [InlineData("combo")]
    [InlineData("tree")]
    public void VariableUpdatesInvalidateCachedTemplateHintFragments(string kind)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, _) = Scene(kind, true); root.Background = Brushes.White;
        Css.SetStyle(root, "--hint-size:32px;--hint-color:#20537a;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{font-size:var(--hint-size);color:var(--hint-color);background:#fff0c0;}"));
        byte[] Pixels()
        {
            var bitmap = new RenderTargetBitmap(300, 220, 96, 96, PixelFormats.Bgra32); bitmap.Render(root);
            var bytes = new byte[300 * 220 * 4]; bitmap.CopyPixels(bytes, 300 * 4, 0); return bytes;
        }
        Layout(root); var before = Pixels();
        Css.SetStyle(root, "--hint-size:36px;--hint-color:#b04440;"); Layout(root);
        Assert.False(before.SequenceEqual(Pixels()), "A template hint reused the old cached fragment after a custom-property update");
    }

    [Fact]
    public void UnrelatedCssKeepsNativeHintsAndRemovingTheRuleRetiresTheProjection()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene("text", false);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject{color:red;}")); Layout(root);
        Assert.Null(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{color:blue;}")); Layout(root);
        Assert.NotNull(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
        Css.GetStyleSheets(root).RemoveAt(1); Layout(root);
        Assert.Null(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
    }

    [Theory]
    [InlineData(".other::placeholder{color:red;}")]
    [InlineData(".subject:disabled::placeholder{color:red;}")]
    [InlineData(".subject::placeholder{width:400px;}")]
    public void UnmatchedOrInapplicableRulesKeepTheNativeHintPath(string css)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene("text", false);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(css)); Layout(root);
        Assert.Null(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
    }

    [Fact]
    public void StateRulesEnterAndLeaveTheProjectionWhenTheOwnerChangesState()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene("text", false);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject:disabled::placeholder{color:red;}")); Layout(root);
        Assert.Null(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
        input.IsEnabled = false; Layout(root);
        Assert.NotNull(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
        input.IsEnabled = true; Layout(root);
        Assert.Null(CssPlaceholder.Prepare(input, input, "hint text", Brushes.Gray));
    }

    [Fact]
    public void TranslationSnapshotsKeepTheirRenderPathAndAreReadableOnTheWorker()
    {
        var original = new TranslateTransform(1.25, 2.5);
        var snapshot = DrawInputSnapshotter.SnapshotTransform(original);
        original.Y = 99;
        Assert.IsType<TranslateTransform>(snapshot);
        Assert.True(snapshot.IsFrozen);
        var workerValue = Task.Run(() => snapshot.Value).GetAwaiter().GetResult();
        Assert.Equal(1.25, workerValue.OffsetX); Assert.Equal(2.5, workerValue.OffsetY);
    }

    [Fact]
    public void PerVisualRecordingPreservesTheHintPosition()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene("text", false);
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{color:red;opacity:.5;}")); Layout(root);
        var live = new PlacementCapture(); var recorder = new DrawingRecorder(); recorder.Bind(live);
        Assert.True(CssPlaceholderPainter.Draw(input, recorder, "hint text", Brushes.Gray, new Rect(0, 40, 300, 80), centered: true));
        var drawing = recorder.Commit(); var replay = new PlacementCapture(); DrawingReplayer.Replay(drawing, replay);
        Assert.NotEmpty(replay.Ys); Assert.All(replay.Ys, y => Assert.InRange(y, 40, 120));
        Assert.Equal(default, live.Offset);
        Assert.Contains(.5, replay.Opacity);
    }

    [Fact]
    public void HintOpacityAffectsRenderedBackgroundPixelsWithoutFadingTheControl()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (root, input) = Scene("text", false); root.Background = Brushes.Black;
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::placeholder{background:white;color:white;opacity:.5;}"));
        Layout(root); var bitmap = new RenderTargetBitmap(300, 220, 96, 96, PixelFormats.Bgra32); bitmap.Render(root);
        var bytes = new byte[300 * 220 * 4]; bitmap.CopyPixels(bytes, 300 * 4, 0);
        var index = (300 + 1) * 4;
        Assert.InRange(bytes[index], (byte)126, (byte)129);
        Assert.InRange(bytes[index + 1], (byte)126, (byte)129);
        Assert.InRange(bytes[index + 2], (byte)126, (byte)129);
        Assert.Equal(1, input.Opacity);
    }

    private static (StackPanel Root, Control Input) Scene(string kind, bool hosted)
    {
        Control input = kind switch
        {
            "auto" => new AutoCompleteBox { PlaceholderText = "hint text", Text = "" },
            "number" => new NumberBox { PlaceholderText = "hint text", Text = "", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden },
            "password" => new PasswordBox { PlaceholderText = "hint text", Password = "" },
            "date" => new DatePicker { PlaceholderText = "hint text" },
            "time" => new TimePicker { PlaceholderText = "hint text" },
            "combo" => new ComboBox { PlaceholderText = "hint text" },
            "combo-editable" => new ComboBox { PlaceholderText = "hint text", IsEditable = true },
            "tree" => new TreeSelector { PlaceholderText = "hint text" },
            _ => new TextBox { PlaceholderText = "hint text", Text = "" },
        };
        input.Width = 300; input.Height = 96; input.FontSize = 24; input.FontFamily = new FontFamily("Georgia");
        input.Foreground = new SolidColorBrush(Color.FromRgb(24, 60, 77)); input.Padding = default; input.BorderThickness = default; input.Background = null;
        if (input is TimePicker)
        {
            // These renderer tests deliberately have no Application/theme scope.
            // Supply the same authored placeholder part used by the default theme.
            var timeTemplate = new ControlTemplate(typeof(TimePicker));
            timeTemplate.SetVisualTree(() =>
            {
                var text = new TextBlock { Name = "PART_DisplayText" };
                text.SetTemplateBinding(TextBlock.TextProperty, TimePicker.DisplayTextProperty);
                return text;
            });
            input.Template = timeTemplate;
        }
        if (hosted && input is not TimePicker)
        {
            var template = new ControlTemplate(input.GetType());
            if (input is ComboBox combo)
                template.SetVisualTree(() =>
                {
                    if (combo.IsEditable) return new TextBox { Name = "PART_EditableTextBox" };
                    var presenter = new ContentPresenter { Name = "PART_SelectionPresenter" };
                    presenter.SetTemplateBinding(ContentPresenter.ContentProperty, ComboBox.SelectionBoxItemProperty);
                    return presenter;
                });
            else if (input is TreeSelector tree) template.SetVisualTree(() => new TextBlock { Name = "PART_PlaceholderText", Text = tree.PlaceholderText });
            else template.SetVisualTree(() => new Border { Name = "PART_ContentHost" });
            input.Template = template;
        }
        Css.SetClass(input, "subject");
        var root = new StackPanel { Width = 300, Height = 220 }; root.Children.Add(input); return (root, input);
    }

    private static void Layout(FrameworkElement root)
    {
        for (var i = 0; i < 6; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(300, 220)); root.Arrange(new(0, 0, 300, 220)); root.UpdateLayout(); }
    }

    private sealed class PlacementCapture : Capture, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        private Point _translation;
        private readonly Stack<Point> _scopes = new();
        internal readonly List<double> Ys = [];
        public override void DrawText(FormattedText text, Point origin)
        { Ys.Add(origin.Y + Offset.Y + _translation.Y); base.DrawText(text, origin); }
        public override void PushTransform(Transform transform)
        {
            _scopes.Push(_translation);
            var delta = transform.Value.Transform(default(Point));
            _translation = new(_translation.X + delta.X, _translation.Y + delta.Y);
        }
        public override void PushClip(Geometry clip) => _scopes.Push(_translation);
        public override void PushOpacity(double opacity) { _scopes.Push(_translation); base.PushOpacity(opacity); }
        public override void Pop() => _translation = _scopes.Pop();
    }

    private class Capture : DrawingContextAdapter
    {
        internal readonly List<(string Text, double Size, Brush? Ink)> Text = [];
        internal readonly List<(Brush? Ink, Rect Rect)> Rectangles = [];
        internal readonly List<double> Opacity = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add((text.Text, text.FontSize, text.Foreground));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => Rectangles.Add((brush, rect));
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawLine(Pen pen, Point start, Point end) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => Rectangles.Add((brush, rect));
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushClip(Geometry clip) { }
        public override void PushTransform(Transform transform) { }
        public override void PushOpacity(double opacity) => Opacity.Add(opacity);
        public override void Pop() { }
        public override void Close() { }
    }
}
