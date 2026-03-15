using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class EditorThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void RichTextBox_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var richTextBox = new RichTextBox();
            var host = new StackPanel { Width = 360, Height = 120 };
            host.Children.Add(richTextBox);

            host.Measure(new Size(360, 120));
            host.Arrange(new Rect(0, 0, 360, 120));

            Assert.True(app.Resources.TryGetValue(typeof(RichTextBox), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(richTextBox.HasLocalValue(Control.BackgroundProperty));
            Assert.False(richTextBox.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(richTextBox.HasLocalValue(Control.PaddingProperty));
            Assert.NotNull(richTextBox.Background);
            Assert.NotNull(richTextBox.BorderBrush);
            Assert.NotNull(richTextBox.SelectionBrush);
            Assert.NotNull(richTextBox.CaretBrush);
            Assert.Equal(4, richTextBox.Padding.Left);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void EditControl_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var editor = new EditControl();
            var host = new StackPanel { Width = 360, Height = 200 };
            host.Children.Add(editor);

            host.Measure(new Size(360, 200));
            host.Arrange(new Rect(0, 0, 360, 200));

            Assert.True(app.Resources.TryGetValue(typeof(EditControl), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(editor.HasLocalValue(Control.BackgroundProperty));
            Assert.False(editor.HasLocalValue(Control.ForegroundProperty));
            Assert.False(editor.HasLocalValue(Control.FontFamilyProperty));
            Assert.False(editor.HasLocalValue(Control.FontSizeProperty));
            Assert.NotNull(editor.Background);
            Assert.NotNull(editor.Foreground);
            Assert.NotNull(editor.SelectionBrush);
            Assert.NotNull(editor.CaretBrush);
            Assert.NotNull(editor.LineNumberForeground);
            Assert.NotNull(editor.CurrentLineBackground);
            Assert.NotNull(editor.GutterBackground);
            Assert.Equal("Cascadia Code", editor.FontFamily);
            Assert.Equal(14, editor.FontSize);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void RichTextBox_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var caretBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var richTextBox = new RichTextBox();

            Assert.Same(selectionBrush, InvokePrivateBrushResolver(richTextBox, "ResolveSelectionBrush"));
            Assert.Same(caretBrush, InvokePrivateBrushResolver(richTextBox, "ResolveCaretBrush"));
            Assert.Same(caretBrush, InvokePrivateBrushResolver(richTextBox, "ResolveDocumentForegroundBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void EditControl_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var editorForeground = Assert.IsAssignableFrom<Brush>(app.Resources["EditorSyntaxPlainText"]);
            var selectionBrush = Assert.IsAssignableFrom<Brush>(app.Resources["SelectionBackground"]);
            var caretBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);
            var lineNumberBrush = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);
            var currentLineBrush = Assert.IsAssignableFrom<Brush>(app.Resources["HighlightBackground"]);
            var gutterBrush = Assert.IsAssignableFrom<Brush>(app.Resources["ControlBackground"]);
            var foldingGuideBrush = ResolveExpectedBrush(app, "OneEditorIndentGuide", "ControlBorder");
            var foldingChevronBrush = ResolveExpectedBrush(app, "TextSecondary", "OneEditorIndentGuide");
            var foldingChevronSelectedBrush = ResolveExpectedBrush(app, "OneBorderFocused", "AccentBrush");

            var editor = new EditControl();

            Assert.Same(editorForeground, InvokePrivateBrushResolver(editor, "ResolveForegroundBrush"));
            Assert.Same(selectionBrush, InvokePrivateBrushResolver(editor, "ResolveSelectionBrush"));
            Assert.Same(caretBrush, InvokePrivateBrushResolver(editor, "ResolveCaretBrush"));
            Assert.Same(lineNumberBrush, InvokePrivateBrushResolver(editor, "ResolveLineNumberForegroundBrush"));
            Assert.Same(currentLineBrush, InvokePrivateBrushResolver(editor, "ResolveCurrentLineBackgroundBrush"));
            Assert.Same(gutterBrush, InvokePrivateBrushResolver(editor, "ResolveGutterBackgroundBrush"));
            Assert.Same(foldingGuideBrush, InvokePrivatePenResolver(editor, "ResolveFoldingGuidePen").Brush);
            Assert.Same(foldingChevronBrush, InvokePrivatePenResolver(editor, "ResolveFoldingChevronPen", false).Brush);
            Assert.Same(foldingChevronSelectedBrush, InvokePrivatePenResolver(editor, "ResolveFoldingChevronPen", true).Brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void EditorView_FallbackClassificationBrushes_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var view = new EditorView();
            var keywordBrush = Assert.IsAssignableFrom<Brush>(app.Resources["EditorSyntaxKeyword"]);
            var punctuationBrush = Assert.IsAssignableFrom<Brush>(app.Resources["EditorSyntaxPunctuation"]);

            Assert.Same(keywordBrush, InvokePrivateBrushResolver(view, "GetFallbackBrushForClassification", TokenClassification.Keyword, new SolidColorBrush(Color.Black)));
            Assert.Same(punctuationBrush, InvokePrivateBrushResolver(view, "GetFallbackBrushForClassification", TokenClassification.Punctuation, new SolidColorBrush(Color.Black)));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(object control, string methodName)
    {
        var method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, null));
    }

    private static Brush InvokePrivateBrushResolver(object control, string methodName, params object[] args)
    {
        var method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, args));
    }

    private static Pen InvokePrivatePenResolver(object control, string methodName, params object[] args)
    {
        var method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Pen>(method!.Invoke(control, args));
    }

    private static Brush ResolveExpectedBrush(Application app, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (app.Resources.TryGetValue(key, out var value) && value is Brush brush)
                return brush;
        }

        throw new KeyNotFoundException($"None of the expected resource keys were found: {string.Join(", ", keys)}");
    }
}
