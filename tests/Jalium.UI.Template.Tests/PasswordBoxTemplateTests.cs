using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class PasswordBoxTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultTemplateOwnsTheChromeAndSecureTextIsRenderedByItsTextView()
    {
        var box = Create(); box.Password = "secret"; Layout(box);
        var border = Assert.IsType<Border>(box.FindName("PART_Border"));
        var view = Assert.IsType<PasswordBoxView>(box.FindName("PART_TextView"));
        Assert.Same(box, view.Owner); Assert.Same(box.Background, border.Background);
        var ink = new Ink(); box.Render(ink);
        Assert.Contains(ink.Text, text => text == new string(box.PasswordChar, 6));
        Assert.DoesNotContain(ink.Text, text => text.Contains("secret"));
        Assert.Null(typeof(PasswordBox).GetField("PasswordProperty"));
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(box.FindName("PART_RevealButton")).Visibility);
        box.IsEnabled = false; Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(box, "CommonStates"));
    }
    [Fact]
    public void ReplacingTheTemplateKeepsPasswordAndSelectionButReleasesTheOldView()
    {
        var box = Create(); box.Password = "保存状态"; box.SelectAll(); Layout(box);
        var retired = Assert.IsType<PasswordBoxView>(box.FindName("PART_TextView"));
        var blank = new ControlTemplate(typeof(PasswordBox)); blank.SetVisualTree(() => new Border()); box.Template = blank; Layout(box);
        Assert.Null(retired.Owner); Assert.Equal("保存状态", box.Password);
        var ink = new Ink(); box.Render(ink); Assert.Empty(ink.Text);
        box.ClearValue(Control.TemplateProperty); Layout(box);
        var current = Assert.IsType<PasswordBoxView>(box.FindName("PART_TextView"));
        Assert.NotSame(retired, current); Assert.Same(box, current.Owner);
        Assert.Equal("保存状态", box.Password);
        Assert.Equal(4, typeof(PasswordBox).GetProperty("SelectionLength", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(box));
        box.Password = "更新"; ink = new Ink(); box.Render(ink); Assert.Contains(new string(box.PasswordChar,2),ink.Text);
    }
    [Fact]
    public void OwnerlessViewIsEmptyAndInstancesKeepIndependentSecureState()
    {
        var view = new PasswordBoxView(); Layout(view); var ink = new Ink(); view.Render(ink); Assert.Empty(ink.Text);
        var first = Create(); var second = Create(); first.Password = "first"; second.Password = "second"; Layout(first); Layout(second);
        Assert.NotSame(first.FindName("PART_TextView"),second.FindName("PART_TextView")); first.Clear(); Assert.Equal("second",second.Password);
    }
    private static PasswordBox Create() { var box = new PasswordBox(); box.Resources.MergedDictionaries.Add(Defaults.Value); return box; }
    private static void Layout(FrameworkElement element) { for(var i=0;i<3;i++){element.InvalidateMeasure();element.Measure(new Size(320,48));element.Arrange(new Rect(0,0,320,48));element.UpdateLayout();} }
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
