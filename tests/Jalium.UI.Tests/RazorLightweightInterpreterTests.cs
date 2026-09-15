using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public class RazorLightweightInterpreterTests
{
    // ═══════════════════════════════════════════════════════════
    // Expression Evaluator Tests
    // ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData("42", 42)]
    [InlineData("3.14", 3.14)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    public void Eval_Literals(string expr, object? expected)
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Eval_StringLiteral()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("\"hello\"", _ => null);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Eval_CharLiteral()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("'A'", _ => null);
        Assert.Equal('A', result);
    }

    [Fact]
    public void Eval_Identifier_ResolvedFromResolver()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("Name", name => name == "Name" ? "Alice" : null);
        Assert.Equal("Alice", result);
    }

    [Theory]
    [InlineData("2 + 3", 5)]
    [InlineData("10 - 4", 6)]
    [InlineData("3 * 7", 21)]
    [InlineData("20 / 4", 5)]
    [InlineData("17 % 5", 2)]
    public void Eval_Arithmetic(string expr, int expected)
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Eval_ArithmeticPrecedence()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("2 + 3 * 4", _ => null);
        Assert.Equal(14, result);
    }

    [Fact]
    public void Eval_Parentheses()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("(2 + 3) * 4", _ => null);
        Assert.Equal(20, result);
    }

    [Theory]
    [InlineData("5 > 3", true)]
    [InlineData("3 > 5", false)]
    [InlineData("5 >= 5", true)]
    [InlineData("3 < 5", true)]
    [InlineData("5 <= 4", false)]
    [InlineData("5 == 5", true)]
    [InlineData("5 != 3", true)]
    public void Eval_Comparison(string expr, bool expected)
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    public void Eval_Logical(string expr, bool expected)
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Eval_Ternary()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("true ? 1 : 2", _ => null);
        Assert.Equal(1, result);

        result = RazorLightweightExpressionEvaluator.Evaluate("false ? 1 : 2", _ => null);
        Assert.Equal(2, result);
    }

    [Fact]
    public void Eval_TernaryWithExpression()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "Count > 0 ? \"Yes\" : \"No\"",
            name => name == "Count" ? 5 : null);
        Assert.Equal("Yes", result);
    }

    [Fact]
    public void Eval_NullCoalescing()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("null ?? \"default\"", _ => null);
        Assert.Equal("default", result);

        result = RazorLightweightExpressionEvaluator.Evaluate("\"value\" ?? \"default\"", _ => null);
        Assert.Equal("value", result);
    }

    [Fact]
    public void Eval_StringConcatenation()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "\"Hello \" + Name",
            name => name == "Name" ? "World" : null);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Eval_MemberAccess()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "Text.Length",
            name => name == "Text" ? "hello" : null);
        Assert.Equal(5, result);
    }

    [Fact]
    public void Eval_MethodCall()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "Value.ToString()",
            name => name == "Value" ? 42 : null);
        Assert.Equal("42", result);
    }

    [Fact]
    public void Eval_Indexer()
    {
        var arr = new[] { "a", "b", "c" };
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "Items[1]",
            name => name == "Items" ? arr : null);
        Assert.Equal("b", result);
    }

    [Fact]
    public void Eval_NewArray()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "new[] { 1, 2, 3 }", _ => null);
        Assert.IsType<int[]>(result);
        Assert.Equal(new[] { 1, 2, 3 }, (int[])result!);
    }

    [Fact]
    public void Eval_UnaryMinus()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("-5", _ => null);
        Assert.Equal(-5, result);
    }

    [Fact]
    public void Eval_StaticMemberAccess()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate("int.MaxValue", _ => null);
        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void Eval_ComplexExpression()
    {
        var result = RazorLightweightExpressionEvaluator.Evaluate(
            "Count > 0 ? Count + \" items\" : \"Empty\"",
            name => name == "Count" ? 42 : null);
        Assert.Equal("42 items", result);
    }

    // ═══════════════════════════════════════════════════════════
    // Code Block Interpreter Tests
    // ═══════════════════════════════════════════════════════════

    private static string ExpandCodeBlock(string code)
    {
        var segments = RazorCodeBlockPreprocessor.ParseCodeBlockSegments(code);
        return RazorLightweightCodeBlockInterpreter.Expand(segments);
    }

    [Fact]
    public void CodeBlock_ForLoop()
    {
        var result = ExpandCodeBlock(
            "for (var i = 0; i < 3; i++) { <Item Value=\"@(i.ToString())\" /> }");
        Assert.Contains("Value=\"0\"", result);
        Assert.Contains("Value=\"1\"", result);
        Assert.Contains("Value=\"2\"", result);
    }

    [Fact]
    public void CodeBlock_ForLoop_WithExpression()
    {
        var result = ExpandCodeBlock(
            "for (var i = 1; i <= 3; i++) { <El W=\"@(i * 10)\" /> }");
        Assert.Contains("W=\"10\"", result);
        Assert.Contains("W=\"20\"", result);
        Assert.Contains("W=\"30\"", result);
    }

    [Fact]
    public void CodeBlock_ForeachLoop()
    {
        var result = ExpandCodeBlock(
            "var items = new[] { \"A\", \"B\", \"C\" }; foreach (var x in items) { <Tag Name=\"@x\" /> }");
        Assert.Contains("Name=\"A\"", result);
        Assert.Contains("Name=\"B\"", result);
        Assert.Contains("Name=\"C\"", result);
    }

    [Fact]
    public void CodeBlock_WhileLoop()
    {
        var result = ExpandCodeBlock(
            "var n = 0; while (n < 3) { <El V=\"@(n.ToString())\" /> n++; }");
        Assert.Contains("V=\"0\"", result);
        Assert.Contains("V=\"1\"", result);
        Assert.Contains("V=\"2\"", result);
    }

    [Fact]
    public void CodeBlock_DoWhileLoop()
    {
        var result = ExpandCodeBlock(
            "var n = 1; do { <El V=\"@(n.ToString())\" /> n++; } while (n <= 3);");
        Assert.Contains("V=\"1\"", result);
        Assert.Contains("V=\"2\"", result);
        Assert.Contains("V=\"3\"", result);
    }

    [Fact]
    public void CodeBlock_IfElse()
    {
        var result = ExpandCodeBlock(
            "var x = 5; if (x > 3) { <El Result=\"big\" /> } else { <El Result=\"small\" /> }");
        Assert.Contains("Result=\"big\"", result);
        Assert.DoesNotContain("Result=\"small\"", result);
    }

    [Fact]
    public void CodeBlock_IfElse_FalseBranch()
    {
        var result = ExpandCodeBlock(
            "var x = 1; if (x > 3) { <El Result=\"big\" /> } else { <El Result=\"small\" /> }");
        Assert.Contains("Result=\"small\"", result);
        Assert.DoesNotContain("Result=\"big\"", result);
    }

    [Fact]
    public void CodeBlock_Switch()
    {
        var result = ExpandCodeBlock(
            "var s = \"B\"; switch (s) { case \"A\": <El V=\"alpha\" /> break; case \"B\": <El V=\"beta\" /> break; case \"C\": <El V=\"gamma\" /> break; }");
        Assert.Contains("V=\"beta\"", result);
        Assert.DoesNotContain("V=\"alpha\"", result);
        Assert.DoesNotContain("V=\"gamma\"", result);
    }

    [Fact]
    public void CodeBlock_TryCatch()
    {
        var result = ExpandCodeBlock(
            "try { var r = 42; <El V=\"@(r.ToString())\" /> } catch (Exception ex) { <El V=\"error\" /> }");
        Assert.Contains("V=\"42\"", result);
        Assert.DoesNotContain("V=\"error\"", result);
    }

    [Fact]
    public void CodeBlock_VariableDeclarationAndAssignment()
    {
        var result = ExpandCodeBlock(
            "var x = 10; x = x + 5; <El V=\"@(x.ToString())\" />");
        Assert.Contains("V=\"15\"", result);
    }

    [Fact]
    public void CodeBlock_CompoundAssignment()
    {
        var result = ExpandCodeBlock(
            "var x = 10; x += 5; <El V=\"@(x.ToString())\" />");
        Assert.Contains("V=\"15\"", result);
    }

    [Fact]
    public void CodeBlock_IncrementDecrement()
    {
        var result = ExpandCodeBlock(
            "var x = 10; x++; x++; <El V=\"@(x.ToString())\" />");
        Assert.Contains("V=\"12\"", result);
    }

    [Fact]
    public void CodeBlock_StringConcatInMarkup()
    {
        var result = ExpandCodeBlock(
            "var name = \"World\"; <El Text=\"@(\"Hello \" + name)\" />");
        Assert.Contains("Text=\"Hello World\"", result);
    }

    [Fact]
    public void CodeBlock_NestedForLoops_ViaCodeLevel()
    {
        // Both loops at code level (not nested inside markup)
        var result = ExpandCodeBlock(
            "for (var r = 1; r <= 2; r++) { for (var c = 1; c <= 2; c++) { <Cell R=\"@(r.ToString())\" C=\"@(c.ToString())\" /> } }");
        Assert.Contains("R=\"1\" C=\"1\"", result);
        Assert.Contains("R=\"1\" C=\"2\"", result);
        Assert.Contains("R=\"2\" C=\"1\"", result);
        Assert.Contains("R=\"2\" C=\"2\"", result);
    }

    [Fact]
    public void CodeBlock_MarkupIf_EmitsPlainTextFromTrueBody()
    {
        var result = ExpandCodeBlock("<TextBlock>@if (true) { hello }</TextBlock>");

        Assert.Contains("hello", result);
    }

    [Fact]
    public void CodeBlock_MarkupIf_EmitsPlainTextFromElseBody()
    {
        var result = ExpandCodeBlock("<TextBlock>@if (false) { wrong } else { goodbye }</TextBlock>");

        Assert.DoesNotContain("wrong", result);
        Assert.Contains("goodbye", result);
    }

    [Fact]
    public void CodeBlock_MarkupIf_EmitsPlainTextFromElseIfBody()
    {
        var result = ExpandCodeBlock(
            "<TextBlock>@if (false) { wrong } else if (true) { expected } else { also-wrong }</TextBlock>");

        Assert.DoesNotContain("wrong", result);
        Assert.Contains("expected", result);
    }

    [Fact]
    public void CodeBlock_MarkupFor_EmitsPlainTextForEveryIteration()
    {
        var result = ExpandCodeBlock("<TextBlock>@for (var i = 0; i < 3; i++) { item }</TextBlock>");

        Assert.Equal(3, result.Split("item", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CodeBlock_MarkupFor_CanContainMarkupIf()
    {
        var result = ExpandCodeBlock(
            "<Root>@for (var i = 0; i < 2; i++) { @if (true) { <Item Value=\"@i\" /> } }</Root>");

        Assert.Contains("Value=\"0\"", result);
        Assert.Contains("Value=\"1\"", result);
    }

    [Fact]
    public void CodeBlock_NestedAwaitForeach_UsesExplicitAsyncEnumeratorInterfaceMembers()
    {
        var source = new ExplicitAsyncEnumerable("A", "B");

        var (output, _) = RazorLightweightCodeBlockInterpreter.ExpandWithScope(
            """
            <Root>
              @await foreach(var item in Source) {
                <Item Value="@item" />
              }
              <After Value="@item" />
            </Root>
            """,
            name => name == "Source" ? source : null);

        Assert.Contains("Value=\"A\"", output);
        Assert.Contains("Value=\"B\"", output);
        Assert.Contains("<After Value=\"\" />", output);
        Assert.Equal(1, source.GetAsyncEnumeratorCount);
        Assert.Equal(3, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void CodeBlock_NestedAwaitForeach_DisposesEnumeratorWhenBodyThrows()
    {
        var source = new ExplicitAsyncEnumerable("A");

        Assert.Throws<InvalidOperationException>(() =>
            RazorLightweightCodeBlockInterpreter.ExpandWithScope(
                """
                <Root>
                  @await foreach(var item in Source) {
                    <Item Value="@(Fail())" />
                  }
                </Root>
                """,
                name => name switch
                {
                    "Source" => source,
                    "Fail" => new Func<object?[], object?>(_ => throw new InvalidOperationException("boom")),
                    _ => null
                }));

        Assert.True(source.Disposed);
        Assert.Equal(1, source.GetAsyncEnumeratorCount);
        Assert.Equal(1, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void CodeBlock_NestedAwaitForeach_UnwrapsGetAsyncEnumeratorException()
    {
        var expected = new AsyncEnumerationException("get enumerator");
        var source = new ThrowingAsyncEnumerable(AsyncThrowStage.GetAsyncEnumerator, expected);

        var actual = Assert.Throws<AsyncEnumerationException>(() => ExpandNestedAwaitForeach(source));

        Assert.Same(expected, actual);
        Assert.Equal(1, source.GetAsyncEnumeratorCount);
        Assert.Equal(0, source.DisposeCount);
    }

    [Fact]
    public void CodeBlock_NestedAwaitForeach_UnwrapsMoveNextAsyncExceptionAndDisposes()
    {
        var expected = new AsyncEnumerationException("move next");
        var source = new ThrowingAsyncEnumerable(AsyncThrowStage.MoveNextAsync, expected);

        var actual = Assert.Throws<AsyncEnumerationException>(() => ExpandNestedAwaitForeach(source));

        Assert.Same(expected, actual);
        Assert.Equal(1, source.GetAsyncEnumeratorCount);
        Assert.Equal(1, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void CodeBlock_NestedAwaitForeach_UnwrapsCurrentExceptionAndDisposes()
    {
        var expected = new AsyncEnumerationException("current");
        var source = new ThrowingAsyncEnumerable(AsyncThrowStage.Current, expected);

        var actual = Assert.Throws<AsyncEnumerationException>(() => ExpandNestedAwaitForeach(source));

        Assert.Same(expected, actual);
        Assert.Equal(1, source.GetAsyncEnumeratorCount);
        Assert.Equal(1, source.MoveNextCount);
        Assert.Equal(1, source.CurrentCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void CodeBlock_NestedAwaitForeach_EnforcesMaxLoopIterations()
    {
        var source = new InfiniteExplicitAsyncEnumerable();

        RazorLightweightCodeBlockInterpreter.ExpandWithScope(
            """
            <Root>
              @await foreach(var item in Source) {
              }
            </Root>
            """,
            name => name == "Source" ? source : null);

        Assert.Equal(1, source.GetAsyncEnumeratorCount);
        Assert.Equal(10_000, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void CodeBlock_EmptyCode_ReturnsEmpty()
    {
        var result = ExpandCodeBlock("");
        Assert.Equal("", result);
    }

    [Fact]
    public void CodeBlock_MathSqrt()
    {
        var result = ExpandCodeBlock(
            "var r = (int)Math.Sqrt(144); <El V=\"@(r.ToString())\" />");
        Assert.Contains("V=\"12\"", result);
    }

    [Fact]
    public void CodeBlock_VariableInCondition()
    {
        var result = ExpandCodeBlock(
            "var bg = 5 % 2 == 0 ? \"blue\" : \"gray\"; <El C=\"@bg\" />");
        Assert.Contains("C=\"gray\"", result);
    }

    [Fact]
    public void ExpandCodeBlock_PureCSharp_IsTaggedAsRuntimeCodeBlock()
    {
        // Pure C# (no XML markup) must be deferred to the runtime RazorTemplate engine,
        // and the caller must recognize that via IsRuntimeCodeBlock so it never re-enters
        // the Jalxaml tokenizer on the result (which would loop forever).
        var result = RazorCodeBlockPreprocessor.ExpandCodeBlock("d");
        Assert.True(RazorCodeBlockPreprocessor.IsRuntimeCodeBlock(result));
    }

    [Fact]
    public void ExpandCodeBlock_PureCSharp_VarDecl_IsTaggedAsRuntimeCodeBlock()
    {
        var result = RazorCodeBlockPreprocessor.ExpandCodeBlock("var x = 1;");
        Assert.True(RazorCodeBlockPreprocessor.IsRuntimeCodeBlock(result));
    }

    [Fact]
    public void IsRuntimeCodeBlock_RejectsExpandedMarkup()
    {
        // A fully expanded code block (XAML markup) must not be misclassified as a
        // runtime block, or HandleCodeBlock would emit it verbatim instead of tokenizing.
        Assert.False(RazorCodeBlockPreprocessor.IsRuntimeCodeBlock("<Item/>"));
        Assert.False(RazorCodeBlockPreprocessor.IsRuntimeCodeBlock(""));
        Assert.False(RazorCodeBlockPreprocessor.IsRuntimeCodeBlock("@x"));
    }

    // ═══════════════════════════════════════════════════════════
    // Tokenizer Tests
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Tokenizer_BasicTokens()
    {
        var tokens = new RazorTokenizer("var x = 42;").Tokenize();
        Assert.Equal(RazorTokenKind.Var, tokens[0].Kind);
        Assert.Equal(RazorTokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("x", tokens[1].Value);
        Assert.Equal(RazorTokenKind.Assign, tokens[2].Kind);
        Assert.Equal(RazorTokenKind.IntLiteral, tokens[3].Kind);
        Assert.Equal("42", tokens[3].Value);
        Assert.Equal(RazorTokenKind.Semicolon, tokens[4].Kind);
        Assert.Equal(RazorTokenKind.Eof, tokens[5].Kind);
    }

    [Fact]
    public void Tokenizer_StringLiteral()
    {
        var tokens = new RazorTokenizer("\"hello world\"").Tokenize();
        Assert.Equal(RazorTokenKind.StringLiteral, tokens[0].Kind);
        Assert.Equal("hello world", tokens[0].Value);
    }

    [Fact]
    public void Tokenizer_EscapedString()
    {
        var tokens = new RazorTokenizer("\"line1\\nline2\"").Tokenize();
        Assert.Equal("line1\nline2", tokens[0].Value);
    }

    [Fact]
    public void Tokenizer_Operators()
    {
        var tokens = new RazorTokenizer("a <= b && c != d").Tokenize();
        Assert.Equal(RazorTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal(RazorTokenKind.LessEquals, tokens[1].Kind);
        Assert.Equal(RazorTokenKind.Identifier, tokens[2].Kind);
        Assert.Equal(RazorTokenKind.And, tokens[3].Kind);
        Assert.Equal(RazorTokenKind.Identifier, tokens[4].Kind);
        Assert.Equal(RazorTokenKind.NotEquals, tokens[5].Kind);
    }

    [Fact]
    public void Tokenizer_Keywords()
    {
        var tokens = new RazorTokenizer("for foreach while if else switch case break").Tokenize();
        Assert.Equal(RazorTokenKind.For, tokens[0].Kind);
        Assert.Equal(RazorTokenKind.Foreach, tokens[1].Kind);
        Assert.Equal(RazorTokenKind.While, tokens[2].Kind);
        Assert.Equal(RazorTokenKind.If, tokens[3].Kind);
        Assert.Equal(RazorTokenKind.Else, tokens[4].Kind);
        Assert.Equal(RazorTokenKind.Switch, tokens[5].Kind);
        Assert.Equal(RazorTokenKind.Case, tokens[6].Kind);
        Assert.Equal(RazorTokenKind.Break, tokens[7].Kind);
    }

    [Fact]
    public void Tokenizer_SkipsComments()
    {
        var tokens = new RazorTokenizer("a // comment\nb").Tokenize();
        Assert.Equal(2 + 1, tokens.Count); // a, b, Eof
        Assert.Equal("a", tokens[0].Value);
        Assert.Equal("b", tokens[1].Value);
    }

    [Fact]
    public void Tokenizer_SkipsBlockComments()
    {
        var tokens = new RazorTokenizer("a /* comment */ b").Tokenize();
        Assert.Equal(2 + 1, tokens.Count);
        Assert.Equal("a", tokens[0].Value);
        Assert.Equal("b", tokens[1].Value);
    }

    private static string ExpandNestedAwaitForeach(object source)
    {
        return RazorLightweightCodeBlockInterpreter.ExpandWithScope(
            """
            <Root>
              @await foreach(var item in Source) {
                <Item Value="@item" />
              }
            </Root>
            """,
            name => name == "Source" ? source : null).Output;
    }

    private sealed class ExplicitAsyncEnumerable(params string[] values) : IAsyncEnumerable<string>
    {
        public int GetAsyncEnumeratorCount { get; private set; }

        public int MoveNextCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool Disposed => DisposeCount > 0;

        IAsyncEnumerator<string> IAsyncEnumerable<string>.GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            GetAsyncEnumeratorCount++;
            return new Enumerator(values, () => MoveNextCount++, () => DisposeCount++);
        }

        private sealed class Enumerator(string[] values, Action onMoveNext, Action onDispose) : IAsyncEnumerator<string>
        {
            private int _index = -1;

            string IAsyncEnumerator<string>.Current => values[_index];

            ValueTask<bool> IAsyncEnumerator<string>.MoveNextAsync()
            {
                onMoveNext();
                return ValueTask.FromResult(++_index < values.Length);
            }

            ValueTask IAsyncDisposable.DisposeAsync()
            {
                onDispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class InfiniteExplicitAsyncEnumerable : IAsyncEnumerable<int>
    {
        public int GetAsyncEnumeratorCount { get; private set; }

        public int MoveNextCount { get; private set; }

        public int DisposeCount { get; private set; }

        IAsyncEnumerator<int> IAsyncEnumerable<int>.GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            GetAsyncEnumeratorCount++;
            return new Enumerator(this);
        }

        private sealed class Enumerator(InfiniteExplicitAsyncEnumerable owner) : IAsyncEnumerator<int>
        {
            int IAsyncEnumerator<int>.Current => 1;

            ValueTask<bool> IAsyncEnumerator<int>.MoveNextAsync()
            {
                owner.MoveNextCount++;
                return ValueTask.FromResult(true);
            }

            ValueTask IAsyncDisposable.DisposeAsync()
            {
                owner.DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private enum AsyncThrowStage
    {
        GetAsyncEnumerator,
        MoveNextAsync,
        Current
    }

    private sealed class AsyncEnumerationException(string message) : Exception(message);

    private sealed class ThrowingAsyncEnumerable(AsyncThrowStage throwStage, Exception exception) : IAsyncEnumerable<string>
    {
        public int GetAsyncEnumeratorCount { get; private set; }

        public int MoveNextCount { get; private set; }

        public int CurrentCount { get; private set; }

        public int DisposeCount { get; private set; }

        IAsyncEnumerator<string> IAsyncEnumerable<string>.GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            GetAsyncEnumeratorCount++;
            if (throwStage == AsyncThrowStage.GetAsyncEnumerator)
                throw exception;

            return new Enumerator(this, throwStage, exception);
        }

        private sealed class Enumerator(
            ThrowingAsyncEnumerable owner,
            AsyncThrowStage throwStage,
            Exception exception) : IAsyncEnumerator<string>
        {
            private bool _moved;

            string IAsyncEnumerator<string>.Current
            {
                get
                {
                    owner.CurrentCount++;
                    if (throwStage == AsyncThrowStage.Current)
                        throw exception;

                    return "A";
                }
            }

            ValueTask<bool> IAsyncEnumerator<string>.MoveNextAsync()
            {
                owner.MoveNextCount++;
                if (throwStage == AsyncThrowStage.MoveNextAsync)
                    throw exception;

                if (_moved)
                    return ValueTask.FromResult(false);

                _moved = true;
                return ValueTask.FromResult(true);
            }

            ValueTask IAsyncDisposable.DisposeAsync()
            {
                owner.DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
