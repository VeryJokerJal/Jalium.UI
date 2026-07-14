using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Microsoft.CSharp.RuntimeBinder;

namespace Jalium.UI.Tests;

public class RazorSyntaxTests
{
    [Fact]
    public void RazorPath_WithNotifyPropertyChanged_ShouldUpdateInRealTime()
    {
        var model = new NameNotifyModel { Name = "Alice" };
        var textBlock = new TextBlock { DataContext = model };
        LoadComponent(textBlock, "Jalium.UI.Tests.TestAssets.RazorPathTextBlock.jalxaml");
        textBlock.DataContext = model;

        Assert.Equal("Alice", textBlock.Text);

        model.Name = "Bob";
        Assert.Equal("Bob", textBlock.Text);
    }

    [Fact]
    public void RazorPath_WithPlainClrProperty_ShouldAssignOnce()
    {
        var model = new NamePlainModel { Name = "Alice" };
        var textBlock = new TextBlock { DataContext = model };
        LoadComponent(textBlock, "Jalium.UI.Tests.TestAssets.RazorPathTextBlock.jalxaml");
        textBlock.DataContext = model;

        Assert.Equal("Alice", textBlock.Text);

        model.Name = "Bob";
        Assert.Equal("Alice", textBlock.Text);
    }

    [Fact]
    public void RazorPath_OnStringProperty_ShouldConvertNonStringValueUsingToString()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       Text='@Count' />
            """;

        var model = new CounterNotifyModel { Count = 3 };
        var textBlock = (TextBlock)XamlReader.Parse(xaml);
        textBlock.DataContext = model;

        Assert.Equal("3", textBlock.Text);

        model.Count = 7;
        Assert.Equal("7", textBlock.Text);
    }

    [Fact]
    public void RazorExpression_ShouldRecalculateWhenDependenciesChange()
    {
        var model = new CounterNotifyModel { Count = 0 };
        var textBlock = new TextBlock { DataContext = model };
        LoadComponent(textBlock, "Jalium.UI.Tests.TestAssets.RazorExpressionTextBlock.jalxaml");
        textBlock.DataContext = model;

        Assert.Equal("B", textBlock.Text);

        model.Count = 2;
        Assert.Equal("A(1)", textBlock.Text);
    }

    [Fact]
    public void RazorCodeBlock_ShouldSupportLocalFunctionsAndSubsequentExpressions()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       Text='@{ string Describe(int value) => value > 0 ? "Positive" : "Zero"; var next = Count + 1; }@(Describe(next - 1))@(next * 2)' />
            """;

        var model = new CounterNotifyModel { Count = 0 };
        var textBlock = (TextBlock)XamlReader.Parse(xaml);
        textBlock.DataContext = model;

        Assert.Equal("Zero2", textBlock.Text);

        model.Count = 2;
        Assert.Equal("Positive6", textBlock.Text);
    }

    [Fact]
    public void RazorCodeBlock_PartiallyWrittenIdentifier_DoesNotStackOverflow()
    {
        // Regression for an editor scenario where the user is mid-edit and the code block
        // contains only an unresolved identifier. The parser used to feed ExpandCodeBlock's
        // "@{d}" output back through TokenizeNested, recursing until the stack blew up.
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ d }
            </TextBlock>
            """;

        var textBlock = (TextBlock)XamlReader.Parse(xaml);
        Assert.NotNull(textBlock);
    }

    [Fact]
    public void RazorCodeBlock_IncompleteIfDirective_DoesNotThrow()
    {
        // Regression: while editing the user may type "@{ if }" before adding the
        // (condition) and { body }. InterpretIfMixed then reads empty strings for
        // both and passes "" into Evaluate, which used to throw XamlParseException
        // ("Unexpected token '' (Eof) at position 0"). An empty expression must
        // evaluate to null so the in-progress edit doesn't break the whole parse.
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ if }
            </Grid>
            """;

        var grid = (Grid)XamlReader.Parse(xaml);
        Assert.NotNull(grid);
    }

    [Fact]
    public void RazorCodeBlock_IncompleteWhileDirective_DoesNotThrow()
    {
        // Same protection covers @while/@for/@switch with missing condition.
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ while }
            </Grid>
            """;

        var grid = (Grid)XamlReader.Parse(xaml);
        Assert.NotNull(grid);
    }

    [Fact]
    public void RazorCodeBlock_StandaloneKeyword_DoesNotThrowXamlParseException()
    {
        // Regression: while the user is mid-edit they may type a lone C# keyword like
        // "is" / "as" / "switch" inside @{ ... }. The lightweight interpreter throws
        // XamlParseException for the unexpected token. Runtime template evaluation must
        // catch this and report failure (return false from TryEvaluate) instead of
        // tearing down the entire XAML parse.
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ is }
            </Grid>
            """;

        var grid = (Grid)XamlReader.Parse(xaml);
        Assert.NotNull(grid);
    }

    [Fact]
    public void RazorCodeBlock_InsideReadOnlyCollectionContent_DoesNotThrow()
    {
        // Regression: while editing, the user may place @{ i } inside a Grid (or any panel
        // whose ContentProperty is a read-only IList like Children). The runtime
        // RazorTemplate engine used to call PropertyInfo.SetValue on Children and crash with
        // "Property set method not found.". Read-only collection content properties must
        // route through IList.Add (or silently ignore an incompatible element type) rather
        // than throwing during a transient edit.
        const string xaml = """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid.RowDefinitions>
                <RowDefinition Height="48" />
              </Grid.RowDefinitions>
              @{ i }
            </Grid>
            """;

        var grid = (Grid)XamlReader.Parse(xaml);
        Assert.NotNull(grid);
    }

    [Fact]
    public void RazorCodeBlock_ShouldSupportWriteInsideLoops()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ for (var i = 0; i != Count; i++) { Write(i); } }
            </TextBlock>
            """;

        var model = new CounterNotifyModel { Count = 3 };
        var textBlock = (TextBlock)XamlReader.Parse(xaml);
        textBlock.DataContext = model;

        Assert.Equal("012", textBlock.Text);

        model.Count = 4;
        Assert.Equal("0123", textBlock.Text);
    }

    [Fact]
    public void RazorCodeBlock_ShouldAllowSingleComputedNonStringValue()
    {
        const string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Width='@{ var computed = Count * 25; }@computed' />
            """;

        var model = new CounterNotifyModel { Count = 4 };
        var border = (Border)XamlReader.Parse(xaml);
        border.DataContext = model;

        Assert.Equal(100d, border.Width);

        model.Count = 5;
        Assert.Equal(125d, border.Width);
    }

    [Fact]
    public void RazorMixedTextNode_ShouldUpdateDynamicSegment()
    {
        var model = new UserContainer { User = new UserNotifyModel { Name = "Alice" } };
        var textBlock = new TextBlock { DataContext = model };
        LoadComponent(textBlock, "Jalium.UI.Tests.TestAssets.RazorMixedTextNodeTextBlock.jalxaml");
        textBlock.DataContext = model;

        Assert.Equal("Hello Alice", textBlock.Text);

        model.User.Name = "Bob";
        Assert.Equal("Hello Bob", textBlock.Text);
    }

    [Fact]
    public void RazorNestedMixedText_WithAncestorDataContext_ShouldResolveAndUpdate()
    {
        var model = new CounterNotifyModel { Count = 0 };
        var host = new RazorNestedHost { DataContext = model };

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorNestedMixedPage.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.Equal("Count: 0", host.CountText?.Text);
        Assert.Equal("ZeroOrNegative", host.ExprText?.Text);

        model.Count = 2;
        Assert.Equal("Count: 2", host.CountText?.Text);
        Assert.Equal("Positive", host.ExprText?.Text);
    }

    [Fact]
    public void RazorIfBlock_ShouldToggleChildVisibilityWhenConditionChanges()
    {
        var model = new OnlineNotifyModel { IsOnline = false };
        var host = new RazorIfBlockHost { DataContext = model };

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorIfBlockPanel.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.NotNull(host.OnlineBorder);
        Assert.Equal(Visibility.Collapsed, host.OnlineBorder!.Visibility);

        model.IsOnline = true;
        Assert.Equal(Visibility.Visible, host.OnlineBorder.Visibility);

        model.IsOnline = false;
        Assert.Equal(Visibility.Collapsed, host.OnlineBorder.Visibility);
    }

    [Fact]
    public void LoadComponent_WithExistingInstance_ShouldRegisterNamedElementsForFindName()
    {
        var host = new RazorIfBlockHost();

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorIfBlockPanel.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.Same(host.OnlineBorder, host.FindName("OnlineBorder"));
    }

    [Fact]
    public void RazorIfBlock_ChainedDirectivesInSameTextNode_ShouldParseAndToggle()
    {
        var model = new OnlineNotifyModel { IsOnline = false };
        var host = new RazorIfBlockHost { DataContext = model };

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorIfBlockChainedPanel.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.NotNull(host.OnlineBorder);
        Assert.NotNull(host.OfflineBorder);
        Assert.Equal(Visibility.Collapsed, host.OnlineBorder!.Visibility);
        Assert.Equal(Visibility.Visible, host.OfflineBorder!.Visibility);

        model.IsOnline = true;
        Assert.Equal(Visibility.Visible, host.OnlineBorder.Visibility);
        Assert.Equal(Visibility.Collapsed, host.OfflineBorder.Visibility);
    }

    [Fact]
    public void RazorIfBlock_InlineSyntax_ShouldParseAndToggle()
    {
        var model = new OnlineNotifyModel { IsOnline = false };
        var host = new RazorIfBlockHost { DataContext = model };

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorIfBlockInlinePanel.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.NotNull(host.OnlineBorder);
        Assert.NotNull(host.OfflineBorder);
        Assert.Equal(Visibility.Collapsed, host.OnlineBorder!.Visibility);
        Assert.Equal(Visibility.Visible, host.OfflineBorder!.Visibility);

        model.IsOnline = true;
        Assert.Equal(Visibility.Visible, host.OnlineBorder.Visibility);
        Assert.Equal(Visibility.Collapsed, host.OfflineBorder.Visibility);
    }

    [Fact]
    public void RazorIfBlock_InlineNestedElements_ShouldParseAndToggle()
    {
        var model = new OnlineNotifyModel { IsOnline = false };
        var host = new RazorIfBlockHost { DataContext = model };

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorIfBlockInlineNestedPanel.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.NotNull(host.OnlineBorder);
        Assert.NotNull(host.OfflineBorder);
        Assert.Equal(Visibility.Collapsed, host.OnlineBorder!.Visibility);
        Assert.Equal(Visibility.Visible, host.OfflineBorder!.Visibility);

        model.IsOnline = true;
        Assert.Equal(Visibility.Visible, host.OnlineBorder.Visibility);
        Assert.Equal(Visibility.Collapsed, host.OfflineBorder.Visibility);
    }

    [Fact]
    public void RazorIfBlock_WithoutDataContext_ShouldNotThrowAndStayCollapsed()
    {
        var host = new RazorIfBlockHost();

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorIfBlockInlinePanel.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.NotNull(host.OnlineBorder);
        Assert.NotNull(host.OfflineBorder);
        Assert.Equal(Visibility.Collapsed, host.OnlineBorder!.Visibility);
        Assert.Equal(Visibility.Collapsed, host.OfflineBorder!.Visibility);
    }

    [Fact]
    public void RazorIfBlock_WithoutDataContext_ShouldNotRaiseNullToBoolRuntimeBinderException()
    {
        var host = new RazorIfBlockHost();
        var binderExceptionCount = 0;

        void OnFirstChanceException(object? _, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is RuntimeBinderException ex &&
                ex.Message.Contains("Cannot convert null to 'bool'", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref binderExceptionCount);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        try
        {
            XamlReader.LoadComponent(
                host,
                "Jalium.UI.Tests.TestAssets.RazorIfBlockInlinePanel.jalxaml",
                typeof(RazorSyntaxTests).Assembly);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        Assert.Equal(0, binderExceptionCount);
    }

    [Fact]
    public void RazorExpressionText_WithoutDataContext_ShouldNotRaiseRuntimeBinderException()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       Text='@(IsOnline ? "Online" : "Offline")' />
            """;

        var binderExceptionCount = 0;
        void OnFirstChanceException(object? _, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is RuntimeBinderException ex &&
                ex.Message.Contains("null", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref binderExceptionCount);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        try
        {
            _ = (TextBlock)XamlReader.Parse(xaml);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        Assert.Equal(0, binderExceptionCount);
    }

    [Fact]
    public void RazorExpressionText_CodeBehindUnsetValue_ShouldNotRaiseRuntimeBinderException()
    {
        var host = new RazorIsOnlineUnsetValueHost();
        var binderExceptionCount = 0;

        void OnFirstChanceException(object? _, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is RuntimeBinderException ex &&
                ex.Message.Contains("bool", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref binderExceptionCount);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        try
        {
            XamlReader.LoadComponent(
                host,
                "Jalium.UI.Tests.TestAssets.RazorIsOnlineExpressionTextBlock.jalxaml",
                typeof(RazorSyntaxTests).Assembly);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        Assert.Equal(0, binderExceptionCount);
        Assert.NotNull(host.StatusText);
    }

    [Fact]
    public void Razor_ShouldPreferDataContext_AndFallbackToCodeBehind()
    {
        var host = new RazorCodeBehindHost { SharedValue = "CodeBehind" };
        var model = new SharedValueNotifyModel { SharedValue = "DataContext" };
        host.DataContext = model;

        XamlReader.LoadComponent(
            host,
            "Jalium.UI.Tests.TestAssets.RazorCodeBehindTextBlock.jalxaml",
            typeof(RazorSyntaxTests).Assembly);

        Assert.Equal("DataContext", host.Tag);

        model.SharedValue = "DataContext2";
        Assert.Equal("DataContext2", host.Tag);

        host.DataContext = new object();
        Assert.Equal("CodeBehind", host.Tag);

        host.SharedValue = "CodeBehind2";
        Assert.Equal("CodeBehind2", host.Tag);
    }

    [Fact]
    public void RazorMixedValue_OnNonStringProperty_ShouldThrowWithLineInfo()
    {
        const string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Width="100@x" />
            """;

        var ex = Assert.Throws<XamlParseException>(() => XamlReader.Parse(xaml));
        Assert.Contains("Width", ex.Message);
        Assert.Contains("Line=", ex.Message);
    }

    [Fact]
    public void RazorEscapes_ShouldSupportAtEscapes()
    {
        var textBlock = new TextBlock { DataContext = new NameNotifyModel { Name = "Alice" } };
        LoadComponent(textBlock, "Jalium.UI.Tests.TestAssets.RazorEscapesTextBlock.jalxaml");

        Assert.Equal("@prefix @escaped Alice", textBlock.Text);
    }

    [Fact]
    public void RazorEscapes_WithNoDynamicSegments_ShouldStillCollapseEscapes()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       Text="@@(expr) and \@path" />
            """;

        var textBlock = (TextBlock)XamlReader.Parse(xaml);
        Assert.Equal("@(expr) and @path", textBlock.Text);
    }

    [Fact]
    public void RazorInvalidExpression_ShouldThrowParseException()
    {
        const string xaml = """
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                       Text='@(Count > )' />
            """;

        var ex = Assert.Throws<XamlParseException>(() => XamlReader.Parse(xaml));
        Assert.Contains("Razor expression compile failed", ex.Message);
    }

    [Fact]
    public void RazorForLoop_ShouldGenerateRepeatedChildren()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @for(var i = 0; i < 3; i++) {
              <TextBlock Text="@(i.ToString())" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Equal(3, panel.Children.Count);
        Assert.Equal("0", ((TextBlock)panel.Children[0]).Text);
        Assert.Equal("1", ((TextBlock)panel.Children[1]).Text);
        Assert.Equal("2", ((TextBlock)panel.Children[2]).Text);
    }

    [Fact]
    public void RazorForeach_ShouldGenerateChildrenFromCollection()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @foreach(var name in new[]{"Alice", "Bob", "Charlie"}) {
              <TextBlock Text="@name" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Equal(3, panel.Children.Count);
        Assert.Equal("Alice", ((TextBlock)panel.Children[0]).Text);
        Assert.Equal("Bob", ((TextBlock)panel.Children[1]).Text);
        Assert.Equal("Charlie", ((TextBlock)panel.Children[2]).Text);
    }

    [Fact]
    public void RazorForeach_WithExpression_ShouldEvaluatePerIteration()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @foreach(var item in new[]{1, 2, 3}) {
              <TextBlock Text="@(item * 10)" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Equal(3, panel.Children.Count);
        Assert.Equal("10", ((TextBlock)panel.Children[0]).Text);
        Assert.Equal("20", ((TextBlock)panel.Children[1]).Text);
        Assert.Equal("30", ((TextBlock)panel.Children[2]).Text);
    }

    [Fact]
    public void RazorWhile_ShouldGenerateRepeatedChildren()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ var n = 0; }
              @while(n < 2) {
              <Border Width="@(n * 10)" />
              @{ n++; }
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Equal(2, panel.Children.Count);
    }

    [Fact]
    public void RazorAdjacentExecutionGroup_ShouldExecuteEachConstructOnce()
    {
        RazorExpressionRegistry.RegisterNamespaceType(
            nameof(RazorExecutionProbe),
            typeof(RazorExecutionProbe));
        RazorExecutionProbe.Reset();

        try
        {
            const string xaml = """
                <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  @{
                    RazorExecutionProbe.Increment();
                    <TextBlock Text="Once" />
                  }

                  @while(false) {
                    <TextBlock Text="Never" />
                  }
                </StackPanel>
                """;

            var panel = (StackPanel)XamlReader.Parse(xaml);

            Assert.Single(panel.Children);
            Assert.Equal("Once", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal(1, RazorExecutionProbe.Count);
        }
        finally
        {
            RazorExecutionProbe.Reset();
        }
    }

    [Fact]
    public void RazorAdjacentExecutionGroup_WithMultipleDirectives_ShouldShareScope()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ var stage = 0; }

              @while(stage == 0) {
                <TextBlock Text="A" />
                @{ stage++; }
              }

              @while(stage == 1) {
                <TextBlock Text="B" />
                @{ stage++; }
              }

              @while(stage == 2) {
                <TextBlock Text="C" />
                @{ stage++; }
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);

        Assert.Equal(3, panel.Children.Count);
        Assert.Equal("A", ((TextBlock)panel.Children[0]).Text);
        Assert.Equal("B", ((TextBlock)panel.Children[1]).Text);
        Assert.Equal("C", ((TextBlock)panel.Children[2]).Text);
    }

    [Fact]
    public void RazorSwitch_ShouldExpandMatchingCase()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @switch("B") {
                case "A":
                  <TextBlock Text="Alpha" />
                  break;
                case "B":
                  <TextBlock Text="Beta" />
                  break;
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Single(panel.Children);
        Assert.Equal("Beta", ((TextBlock)panel.Children[0]).Text);
    }

    [Fact]
    public void RazorDoWhile_ShouldGenerateRepeatedChildren()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{ var k = 0; }
              @do {
              <Border />
              @{ k++; }
              } while(k < 3);
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Equal(3, panel.Children.Count);
    }

    [Fact]
    public void RazorTryCatch_ShouldExpandTryBlock()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @try {
              <TextBlock Text="OK" />
              } catch(System.Exception) {
              <TextBlock Text="Error" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Single(panel.Children);
        Assert.Equal("OK", ((TextBlock)panel.Children[0]).Text);
    }

    [Fact]
    public void RazorComment_ShouldBeStripped()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @* This is a Razor comment and should not appear *@
              <TextBlock Text="Visible" />
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Single(panel.Children);
        Assert.Equal("Visible", ((TextBlock)panel.Children[0]).Text);
    }

    [Fact]
    public void RazorCodeBlock_WithForeachAndLocalFunctions_ShouldGenerateChildren()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{
                var now = DateTime.Now;
                string Greeting() => now.Hour < 12 ? "Good Morning" : now.Hour < 18 ? "Good Afternoon" : "Good Evening";
                var info = new[] {
                    ("Time", now.ToString("HH:mm:ss")),
                    ("Date", now.ToString("yyyy-MM-dd")),
                    ("Greeting", Greeting())
                };
                foreach (var kv in info) {
                  <StackPanel Orientation="Horizontal">
                    <TextBlock Text="@(kv.Item1 + ":")" />
                    <TextBlock Text="@kv.Item2" />
                  </StackPanel>
                }
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Equal(3, panel.Children.Count);

        // Each child is a horizontal StackPanel with two TextBlocks
        var row0 = (StackPanel)panel.Children[0];
        Assert.Equal("Time:", ((TextBlock)row0.Children[0]).Text);
        Assert.NotEmpty(((TextBlock)row0.Children[1]).Text); // e.g. "16:30:00"

        var row1 = (StackPanel)panel.Children[1];
        Assert.Equal("Date:", ((TextBlock)row1.Children[0]).Text);
        Assert.NotEmpty(((TextBlock)row1.Children[1]).Text); // e.g. "2026-04-08"

        var row2 = (StackPanel)panel.Children[2];
        Assert.Equal("Greeting:", ((TextBlock)row2.Children[0]).Text);
        Assert.Contains(((TextBlock)row2.Children[2 - 2]).Text, new[] { "Greeting:" });
        var greetingValue = ((TextBlock)row2.Children[1]).Text;
        Assert.Contains(greetingValue, new[] { "Good Morning", "Good Afternoon", "Good Evening" });
    }

    [Fact]
    public void RazorUsing_ShouldExpandBlock()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @using(var writer = new System.IO.StringWriter()) {
              <TextBlock Text="@(writer.GetType().Name)" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Single(panel.Children);
        Assert.Equal("StringWriter", ((TextBlock)panel.Children[0]).Text);
    }

    [Fact]
    public void RazorUsing_InCodeBlock_ShouldExpandBlock()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{
                using(var writer = new System.IO.StringWriter()) {
                  <TextBlock Text="@(writer.ToString().Length)" />
                }
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Single(panel.Children);
        Assert.Equal("0", ((TextBlock)panel.Children[0]).Text);
    }

    [Fact]
    public void RazorGetTypeName_ShouldResolveChainedMethodCall()
    {
        // GetType() returns a Type instance; .Name should access instance property, not static
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{
                var x = "hello";
                <TextBlock Text="@(x.GetType().Name)" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xaml);
        Assert.Single(panel.Children);
        Assert.Equal("String", ((TextBlock)panel.Children[0]).Text);
    }

    [Fact]
    public void RazorAwaitForeach_ShouldExpandAsyncEnumerable()
    {
        // await foreach is compiled by Roslyn scripting — needs the helper method in scope.
        // Since the preprocessor uses CSharpScript which supports top-level await,
        // we test with a simple async enumerable via inline code.
        const string xamlWithHelper = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @{
                async System.Collections.Generic.IAsyncEnumerable<string> Produce() {
                  yield return "X";
                  yield return "Y";
                }
              }
              @await foreach(var item in Produce()) {
              <TextBlock Text="@item" />
              }
            </StackPanel>
            """;

        var panel = (StackPanel)XamlReader.Parse(xamlWithHelper);
        Assert.Equal(2, panel.Children.Count);
        Assert.Equal("X", ((TextBlock)panel.Children[0]).Text);
        Assert.Equal("Y", ((TextBlock)panel.Children[1]).Text);
    }

    private static void LoadComponent(object component, string resourceName)
    {
        XamlReader.LoadComponent(component, resourceName, typeof(RazorSyntaxTests).Assembly);
    }

    private static class RazorExecutionProbe
    {
        private static int s_count;

        public static int Count => Volatile.Read(ref s_count);

        public static int Increment() => Interlocked.Increment(ref s_count);

        public static void Reset() => Volatile.Write(ref s_count, 0);
    }

    private sealed class NameNotifyModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                    return;

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class NamePlainModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CounterNotifyModel : INotifyPropertyChanged
    {
        private int _count;

        public int Count
        {
            get => _count;
            set
            {
                if (_count == value)
                    return;

                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class UserContainer
    {
        public required UserNotifyModel User { get; init; }
    }

    private sealed class UserNotifyModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                    return;

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class SharedValueNotifyModel : INotifyPropertyChanged
    {
        private string _sharedValue = string.Empty;

        public string SharedValue
        {
            get => _sharedValue;
            set
            {
                if (_sharedValue == value)
                    return;

                _sharedValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SharedValue)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class RazorCodeBehindHost : FrameworkElement, INotifyPropertyChanged
    {
        private string _sharedValue = string.Empty;

        public string SharedValue
        {
            get => _sharedValue;
            set
            {
                if (_sharedValue == value)
                    return;

                _sharedValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SharedValue)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class RazorNestedHost : UserControl
    {
        public TextBlock? CountText { get; set; }

        public TextBlock? ExprText { get; set; }
    }

    private sealed class RazorIfBlockHost : UserControl
    {
        public Border? OnlineBorder { get; set; }

        public Border? OfflineBorder { get; set; }
    }

    private sealed class OnlineNotifyModel : INotifyPropertyChanged
    {
        private bool _isOnline;

        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline == value)
                    return;

                _isOnline = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOnline)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class RazorIsOnlineUnsetValueHost : UserControl
    {
        public object IsOnline => DependencyProperty.UnsetValue;

        public TextBlock? StatusText { get; set; }
    }

    // -----------------------------------------------------------------------
    // DataTemplate + @if(#.Adult) per-item conditional rendering
    // Repro for: bug where @if in a DataTemplate evaluates once against the
    // template container (not the per-item DataContext), so every realized
    // item either all-render or all-hide the conditional child.
    // -----------------------------------------------------------------------

    [Fact]
    public void RazorIf_InsideDataTemplate_ShouldUpdateWhenAdultChanges()
    {
        // Reproduces: "@if(#.Adult) only evaluates once at init; when Adult flips to false,
        // the child is still rendered."
        // This verifies the MultiBinding + DataContext trigger actually fires on property change.
        const string templateXaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                @if(#.Adult)
                {
                  <TextBlock x:Name="AdultText" Text="{Binding Age}" />
                }
                <TextBlock x:Name="NameText" Text="{Binding Name}" />
              </Grid>
            </DataTemplate>
            """;

        var template = (DataTemplate)XamlReader.Parse(templateXaml);
        var model = new PersonModel { Name = "Charlie", Age = 20, Adult = true };

        var grid = (Grid)template.LoadContent()!;
        grid.DataContext = model;

        var adultText = (TextBlock)grid.Children[0];

        Assert.Equal(Visibility.Visible, adultText.Visibility);

        // Flip Adult to false — child must hide.
        model.Adult = false;
        Assert.Equal(Visibility.Collapsed, adultText.Visibility);

        // Flip back — child must re-show.
        model.Adult = true;
        Assert.Equal(Visibility.Visible, adultText.Visibility);
    }

    [Fact]
    public void RazorIf_InsideItemsControlDataTemplate_ShouldRenderPerItem()
    {
        // Reproduces the reported scenario: ItemsControl + DataTemplate + @if(#.Adult)
        // where each item has its own Adult value.
        const string xaml = """
            <ItemsControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ItemsControl.ItemTemplate>
                <DataTemplate>
                  <Grid>
                    @if(#.Adult)
                    {
                      <TextBlock Text="{Binding Age}" />
                    }
                    <TextBlock Text="{Binding Name}" />
                  </Grid>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;

        var items = new List<PersonModel>
        {
            new() { Name = "Alice", Age = 30, Adult = true },
            new() { Name = "Bob",   Age = 15, Adult = false },
        };

        var itemsControl = (ItemsControl)XamlReader.Parse(xaml);
        itemsControl.ItemsSource = items;

        // Force the container generator to realize all items.
        itemsControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        itemsControl.Arrange(new Rect(0, 0, 500, 500));

        var realizedGrids = new List<Grid>();
        CollectGrids(itemsControl, realizedGrids);

        Assert.Equal(2, realizedGrids.Count);

        var (aliceAdultText, _) = ExtractTexts(realizedGrids[0]);
        var (bobAdultText, _)   = ExtractTexts(realizedGrids[1]);

        Assert.Equal(Visibility.Visible,   aliceAdultText.Visibility);
        Assert.Equal(Visibility.Collapsed, bobAdultText.Visibility);
    }

    private static void CollectGrids(Visual root, List<Grid> grids)
    {
        if (root is Grid g && g.Children.Count == 2 && g.Children[0] is TextBlock && g.Children[1] is TextBlock)
            grids.Add(g);

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is Visual child)
                CollectGrids(child, grids);
        }
    }

    private static (TextBlock AdultText, TextBlock NameText) ExtractTexts(Grid grid)
    {
        return ((TextBlock)grid.Children[0], (TextBlock)grid.Children[1]);
    }

    [Fact]
    public void RazorIf_InsideDataTemplate_ShouldBindPerItemDataContext()
    {
        const string templateXaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                @if(#.Adult)
                {
                  <TextBlock x:Name="AdultText" Text="{Binding Age}" />
                }
                <TextBlock x:Name="NameText" Text="{Binding Name}" />
              </Grid>
            </DataTemplate>
            """;

        var template = (DataTemplate)XamlReader.Parse(templateXaml);

        // Instance 1: Adult = true — Age TextBlock should be VISIBLE
        var instance1 = (Grid)template.LoadContent()!;
        instance1.DataContext = new PersonModel { Name = "Alice", Age = 30, Adult = true };
        Assert.Equal(2, instance1.Children.Count);
        var adultText1 = (TextBlock)instance1.Children[0];
        Assert.Equal(Visibility.Visible, adultText1.Visibility);

        // Instance 2: Adult = false — Age TextBlock should be COLLAPSED
        var instance2 = (Grid)template.LoadContent()!;
        instance2.DataContext = new PersonModel { Name = "Bob", Age = 15, Adult = false };
        Assert.Equal(2, instance2.Children.Count);
        var adultText2 = (TextBlock)instance2.Children[0];
        Assert.Equal(Visibility.Collapsed, adultText2.Visibility);
    }

    private sealed class PersonModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _age;
        private bool _adult;

        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }

        public int Age
        {
            get => _age;
            set { _age = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age))); }
        }

        public bool Adult
        {
            get => _adult;
            set { _adult = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Adult))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

