using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;

namespace Jalium.UI.Tests;

public sealed class EditControlReactiveHighlightingTests
{
    [Fact]
    public void EditControl_ShouldAttachNotifyDetach_ReactiveHighlighter()
    {
        SyntaxHighlighterRegistry.ClearForTesting();

        var editor = new EditControl
        {
            DocumentFilePath = @"C:\temp\ReactiveTest.cs"
        };
        var highlighter = new TestReactiveHighlighter();

        editor.SyntaxHighlighter = highlighter;

        Assert.Equal(1, highlighter.AttachCount);
        Assert.Same(editor.Document, highlighter.AttachedDocument);
        Assert.Equal(editor.DocumentFilePath, highlighter.LastProvidedFilePath);

        editor.Document.Insert(0, "x");
        Assert.True(highlighter.NotifyCount > 0);

        editor.SyntaxHighlighter = null;
        Assert.Equal(1, highlighter.DetachCount);
    }

    [Fact]
    public void HighlightingInvalidated_ShouldInvalidateLineOrWholeDocument()
    {
        var editor = new EditControl();
        editor.LoadText("a\nb\nc\nd");

        var highlighter = new TestReactiveHighlighter();
        editor.SyntaxHighlighter = highlighter;

        var view = GetPrivateView(editor);
        var lineCache = GetPrivateField<Dictionary<int, EditorViewLine>>(view, "_lineCache");
        var lineStates = GetPrivateField<Dictionary<int, object?>>(view, "_lineStates");

        SeedViewCaches(lineCache, lineStates);
        highlighter.RaiseInvalidated(new SyntaxHighlightInvalidatedEventArgs(3, affectsWholeDocument: false));

        Assert.Contains(1, lineCache.Keys);
        Assert.Contains(2, lineCache.Keys);
        Assert.DoesNotContain(3, lineCache.Keys);
        Assert.DoesNotContain(4, lineCache.Keys);

        Assert.Contains(1, lineStates.Keys);
        Assert.Contains(2, lineStates.Keys);
        Assert.DoesNotContain(3, lineStates.Keys);
        Assert.DoesNotContain(4, lineStates.Keys);

        SeedViewCaches(lineCache, lineStates);
        highlighter.RaiseInvalidated(SyntaxHighlightInvalidatedEventArgs.WholeDocument);

        Assert.Empty(lineCache);
        Assert.Empty(lineStates);
    }

    [Fact]
    public void PunctuationBrushKey_ShouldBeSeparateFromOperatorBrushKey()
    {
        string editorPunctuation = InvokeClassificationKeyMethod("GetEditorSyntaxBrushKey", TokenClassification.Punctuation);
        string editorOperator = InvokeClassificationKeyMethod("GetEditorSyntaxBrushKey", TokenClassification.Operator);
        string onePunctuation = InvokeClassificationKeyMethod("GetOneSyntaxBrushKey", TokenClassification.Punctuation);
        string oneOperator = InvokeClassificationKeyMethod("GetOneSyntaxBrushKey", TokenClassification.Operator);

        Assert.Equal("EditorSyntaxPunctuation", editorPunctuation);
        Assert.Equal("EditorSyntaxOperator", editorOperator);
        Assert.NotEqual(editorOperator, editorPunctuation);

        Assert.Equal("OneEditorSyntaxPunctuation", onePunctuation);
        Assert.Equal("OneEditorSyntaxOperator", oneOperator);
        Assert.NotEqual(oneOperator, onePunctuation);
    }

    [Fact]
    public void CSharpWithoutRegisteredPlugin_ShouldFallbackToRegexHighlighter()
    {
        SyntaxHighlighterRegistry.ClearForTesting();
        SyntaxHighlighterRegistry.Unregister("csharp");
        SyntaxHighlighterRegistry.Unregister("cs");

        var editor = new EditControl
        {
            Language = "csharp"
        };

        Assert.IsType<RegexSyntaxHighlighter>(editor.SyntaxHighlighter);
    }

    private static string InvokeClassificationKeyMethod(string methodName, TokenClassification classification)
    {
        var method = typeof(EditControl).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [classification]);
        return Assert.IsType<string>(result);
    }

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        return Assert.IsType<T>(value);
    }

    private static EditorView GetPrivateView(EditControl editor)
    {
        var field = typeof(EditControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<EditorView>(field!.GetValue(editor));
    }

    private static void SeedViewCaches(
        Dictionary<int, EditorViewLine> lineCache,
        Dictionary<int, object?> lineStates)
    {
        lineCache.Clear();
        lineStates.Clear();

        for (int line = 1; line <= 4; line++)
        {
            lineCache[line] = new EditorViewLine(line);
            lineStates[line] = new object();
        }
    }

    private sealed class TestReactiveHighlighter : IReactiveSyntaxHighlighter
    {
        public event EventHandler<SyntaxHighlightInvalidatedEventArgs>? HighlightingInvalidated;

        public int AttachCount { get; private set; }
        public int NotifyCount { get; private set; }
        public int DetachCount { get; private set; }
        public TextDocument? AttachedDocument { get; private set; }
        public string? LastProvidedFilePath { get; private set; }

        public object? GetInitialState() => null;

        public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(int lineNumber, string lineText, object? stateAtLineStart)
        {
            if (lineText.Length == 0)
                return ([], stateAtLineStart);

            return ([new SyntaxToken(0, lineText.Length, TokenClassification.PlainText)], stateAtLineStart);
        }

        public void Attach(TextDocument document, Func<string?> filePathProvider)
        {
            AttachCount++;
            AttachedDocument = document;
            LastProvidedFilePath = filePathProvider();
        }

        public void NotifyDocumentChanged(TextChangeEventArgs change)
        {
            NotifyCount++;
        }

        public void Detach()
        {
            DetachCount++;
            AttachedDocument = null;
        }

        public void RaiseInvalidated(SyntaxHighlightInvalidatedEventArgs args)
        {
            HighlightingInvalidated?.Invoke(this, args);
        }
    }
}
