using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
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
    public void RazorAwaitForeach_ShouldExpandAsyncEnumerable()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              @await foreach(var item in AsyncItems()) {
              <TextBlock Text="@item" />
              }
            </StackPanel>
            """;

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
}
