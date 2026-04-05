using Jalium.UI.Controls.Editor;

namespace Jalium.UI.Tests;

public class JalxamlSyntaxHighlighterRazorTests
{
    [Fact]
    public void HighlightLine_RazorExpressionInAttribute_SplitsExpressionTokens()
    {
        const string line = """<TextBlock Text='@(Count > 0 ? "A(1)" : "B")' />""";

        var tokens = Highlight(line);

        Assert.Contains(tokens, token => token.Text == "@" && token.Classification == TokenClassification.Operator);
        Assert.Contains(tokens, token => token.Text == "(" && token.Classification == TokenClassification.Punctuation);
        Assert.Contains(tokens, token => token.Text == "Count" && token.Classification == TokenClassification.BindingPath);
        Assert.Contains(tokens, token => token.Text == ">" && token.Classification == TokenClassification.Operator);
        Assert.Contains(tokens, token => token.Text == "0" && token.Classification == TokenClassification.Number);
        Assert.Contains(tokens, token => token.Text == "\"A(1)\"" && token.Classification == TokenClassification.String);
        Assert.Contains(tokens, token => token.Text == "\"B\"" && token.Classification == TokenClassification.String);
    }

    [Fact]
    public void HighlightLine_RazorEscapesInAttribute_OnlyHighlightsDynamicSegment()
    {
        const string line = """<TextBlock Text="@@prefix \@escaped @Name" />""";

        var tokens = Highlight(line);

        Assert.Equal(1, tokens.Count(token => token.Text == "@" && token.Classification == TokenClassification.Operator));
        Assert.Equal(1, tokens.Count(token => token.Text == "Name" && token.Classification == TokenClassification.BindingPath));
        Assert.DoesNotContain(tokens, token => token.Text == "prefix" && token.Classification == TokenClassification.BindingPath);
        Assert.DoesNotContain(tokens, token => token.Text == "escaped" && token.Classification == TokenClassification.BindingPath);
    }

    [Fact]
    public void HighlightLine_RazorIfTextNode_HighlightsDirectiveConditionAndBraces()
    {
        const string line = """<StackPanel>@if(IsOnline){<Border x:Name="OnlineBorder" />}</StackPanel>""";

        var tokens = Highlight(line);

        Assert.Contains(tokens, token => token.Text == "@" && token.Classification == TokenClassification.Operator);
        Assert.Contains(tokens, token => token.Text == "if" && token.Classification == TokenClassification.ControlKeyword);
        Assert.Contains(tokens, token => token.Text == "IsOnline" && token.Classification == TokenClassification.BindingPath);
        Assert.Contains(tokens, token => token.Text == "{" && token.Classification == TokenClassification.Operator);
        Assert.Contains(tokens, token => token.Text == "}" && token.Classification == TokenClassification.Operator);
    }

    [Fact]
    public void HighlightLine_RazorMultipleAtEscapes_OnlyHighlightsDynamicTail()
    {
        const string line = """<TextBlock Text="@@@Name" />""";

        var tokens = Highlight(line);

        Assert.Equal(1, tokens.Count(token => token.Text == "@" && token.Classification == TokenClassification.Operator));
        Assert.Equal(1, tokens.Count(token => token.Text == "Name" && token.Classification == TokenClassification.BindingPath));
    }

    [Fact]
    public void HighlightLine_RazorBackslashEscapeBeforeDynamicAt_HighlightsOnlyUnescapedSegment()
    {
        const string line = """<TextBlock Text="\@@Name" />""";

        var tokens = Highlight(line);

        Assert.Equal(1, tokens.Count(token => token.Text == "@" && token.Classification == TokenClassification.Operator));
        Assert.Equal(1, tokens.Count(token => token.Text == "Name" && token.Classification == TokenClassification.BindingPath));
    }

    [Fact]
    public void HighlightLine_RazorEscapedAtWithoutDynamicTail_DoesNotEmitBindingPath()
    {
        const string line = """<TextBlock Text="@@\@Name" />""";

        var tokens = Highlight(line);

        Assert.DoesNotContain(tokens, token => token.Classification == TokenClassification.BindingPath);
        Assert.DoesNotContain(tokens, token => token.Text == "@" && token.Classification == TokenClassification.Operator);
    }

    [Fact]
    public void HighlightLine_RazorCodeBlock_HighlightsKeywordsAndMethodName()
    {
        const string line = """@{ void test(){} }""";

        var tokens = Highlight(line);

        Assert.Contains(tokens, token => token.Text == "@" && token.Classification == TokenClassification.Operator);
        Assert.Contains(tokens, token => token.Text == "void" && token.Classification == TokenClassification.Keyword);
        Assert.Contains(tokens, token => token.Text == "test" && token.Classification == TokenClassification.Method);
    }

    [Fact]
    public void HighlightLine_RazorForBlockMultiLine_HighlightsCSharpInBody()
    {
        // @for (var i = 0; i < length; i++)
        // {
        // StringBuilder stringBuilder = new StringBuilder();
        // }
        var lines = new[]
        {
            "@for (var i = 0; i < length; i++)",
            "{",
            "StringBuilder stringBuilder = new StringBuilder();",
            "}",
        };

        var allTokens = HighlightMultiLine(lines);

        // Line 0: @for keyword and expression
        var line0 = allTokens[0];
        Assert.Contains(line0, t => t.Text == "@" && t.Classification == TokenClassification.Operator);
        Assert.Contains(line0, t => t.Text == "for" && t.Classification == TokenClassification.ControlKeyword);
        Assert.Contains(line0, t => t.Text == "var" && t.Classification == TokenClassification.Keyword);
        Assert.Contains(line0, t => t.Text == "0" && t.Classification == TokenClassification.Number);

        // Line 2: C# code inside the block should be highlighted
        var line2 = allTokens[2];
        Assert.Contains(line2, t => t.Text == "new" && t.Classification == TokenClassification.Keyword);
        Assert.Contains(line2, t => t.Text == "StringBuilder" && t.Classification is TokenClassification.Identifier or TokenClassification.Method);
    }

    [Fact]
    public void HighlightLine_RazorForeachBlockMultiLine_HighlightsCSharpInBody()
    {
        var lines = new[]
        {
            "@foreach (var item in collection)",
            "{",
            "    var name = item.Name;",
            "}",
        };

        var allTokens = HighlightMultiLine(lines);

        // Line 2: C# inside foreach body
        var line2 = allTokens[2];
        Assert.Contains(line2, t => t.Text == "var" && t.Classification == TokenClassification.Keyword);
    }

    private static IReadOnlyList<(string Text, TokenClassification Classification)> Highlight(string line)
    {
        var highlighter = JalxamlSyntaxHighlighter.Create();
        var (tokens, _) = highlighter.HighlightLine(1, line, highlighter.GetInitialState());

        return tokens
            .Where(token => token.Length > 0 && token.Classification != TokenClassification.PlainText)
            .Select(token => (line.Substring(token.StartOffset, token.Length), token.Classification))
            .ToArray();
    }

    private static IReadOnlyList<(string Text, TokenClassification Classification)>[] HighlightMultiLine(string[] lines)
    {
        var highlighter = JalxamlSyntaxHighlighter.Create();
        object? state = highlighter.GetInitialState();
        var result = new IReadOnlyList<(string Text, TokenClassification Classification)>[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            var (tokens, nextState) = highlighter.HighlightLine(i + 1, lines[i], state);
            result[i] = tokens
                .Where(t => t.Length > 0 && t.Classification != TokenClassification.PlainText)
                .Select(t => (lines[i].Substring(t.StartOffset, t.Length), t.Classification))
                .ToArray();
            state = nextState;
        }

        return result;
    }
}
