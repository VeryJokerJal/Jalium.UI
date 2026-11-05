using System.ComponentModel;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class CssStyleSheetSourceTests
{
    static CssStyleSheetSourceTests()
    {
        // The pack-resource loader is supplied by Jalium.UI.Xaml. Touch its runtime
        // before tests that set attached properties directly, as a markup app does.
        _ = XamlReader.Parse("<Control xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />");
    }

    private const string Primary = "TestAssets/CssSource/Primary.css";
    private const string Secondary = "TestAssets/CssSource/Secondary.css";
    private static string Component(string path) => $"/{typeof(CssStyleSheetSourceTests).Assembly.GetName().Name};component/{path}";

    [Fact]
    public void RuntimeJalxamlLoadsProjectRelativeCssForADerivedControlAndItsImports()
    {
        var control = Assert.IsType<CssSourceProbe>(LoadWithAssembly($"""
            <test:CssSourceProbe xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:test="clr-namespace:Jalium.UI.Tests;assembly=Jalium.UI.Tests"
                Css.Class="source-target" Css.StyleSheetSource="{Primary}" />
            """));

        Flush();
        Assert.Equal(144, control.Width);
        Assert.Equal(32, control.Height); // Base.css, imported by Primary.css.
        Assert.Equal(new Thickness(8), control.Padding);
        Assert.Equal(Primary, Css.GetStyleSheetSource(control));
        Assert.Empty(Assert.Single(Css.GetStyleSheets(control)).Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentAndPackUrisWorkWithoutACodeBehindAssembly(bool pack)
    {
        var source = (pack ? "pack://application:,,," : "") + Component(Primary);
        var control = Assert.IsType<Control>(XamlReader.Parse($"""
            <Control xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Css.Class="source-target" Css.StyleSheetSource="{source}" />
            """));
        Flush();
        Assert.Equal(144, control.Width);
        Assert.Equal(32, control.Height);
    }

    [Theory]
    [InlineData("./TestAssets/CssSource/Primary.css")]
    [InlineData("/TestAssets/CssSource/Primary.css")]
    [InlineData("TestAssets\\CssSource\\Primary.css")]
    [InlineData("TestAssets/CssSource/../CssSource/Primary.css")]
    public void ProjectResourcePathsAreNormalized(string path)
    {
        var control = LoadWithAssembly($"""
            <Control xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Css.Class="source-target" Css.StyleSheetSource="{path}" />
            """);
        Flush();
        Assert.Equal(144, control.Width);
    }

    [Fact]
    public void ReplacingAndClearingSourcePreservesOtherScopedSheets()
    {
        var control = new Control();
        Css.SetClass(control, "source-target");
        var manual = CssStyleSheet.Parse(".source-target { opacity: 0.5 }");
        Css.GetStyleSheets(control).Add(manual);
        Css.SetStyleSheetSource(control, Component(Primary));
        Flush();
        Assert.Equal(144, control.Width);

        var primary = Css.GetStyleSheets(control)[1];
        Css.SetStyleSheetSource(control, Component(Secondary));
        Flush();
        Assert.Equal(216, control.Width);
        Assert.DoesNotContain(primary, Css.GetStyleSheets(control));
        Assert.Equal(2, Css.GetStyleSheets(control).Count);

        control.ClearValue(Css.StyleSheetSourceProperty);
        Flush();
        Assert.True(double.IsNaN(control.Width));
        Assert.Equal(0.5, control.Opacity);
        Assert.Same(manual, Assert.Single(Css.GetStyleSheets(control)));
        Assert.Equal(string.Empty, Css.GetStyleSheetSource(control));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void BlankOrNullSourceRemovesTheLoadedSheet(string? source)
    {
        var control = new Control();
        Css.SetStyleSheetSource(control, Component(Primary));
        Css.SetStyleSheetSource(control, source);
        Assert.Empty(Css.GetStyleSheets(control));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredStylesheetTextWinsRegardlessOfAttributeOrder(bool sourceFirst)
    {
        var declarations = $"Css.StyleSheetSource=\"{Primary}\" Css.StyleSheet=\".source-target {{ width: 300px }}\"";
        if (!sourceFirst) declarations = $"Css.StyleSheet=\".source-target {{ width: 300px }}\" Css.StyleSheetSource=\"{Primary}\"";
        var control = LoadWithAssembly($"""
            <Control xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Css.Class="source-target" {declarations} />
            """);
        Flush();
        Assert.Equal(300, control.Width);
        Css.SetStyleSheetSource(control, Secondary);
        Flush();
        Assert.Equal(300, control.Width);
        Css.SetStyleSheetSource(control, null);
        Flush();
        Assert.Equal(300, control.Width);
        Assert.Single(Css.GetStyleSheets(control));
    }

    [Fact]
    public void FailedReplacementKeepsTheWorkingSheetAndReportsTheSource()
    {
        var control = new Control();
        Css.SetClass(control, "source-target");
        Css.SetStyleSheetSource(control, Component(Primary));
        var original = Assert.Single(Css.GetStyleSheets(control));
        var error = Assert.Throws<InvalidOperationException>(() =>
            Css.SetStyleSheetSource(control, Component("TestAssets/CssSource/Missing.css")));

        Assert.Contains("Missing.css", error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Same(original, Assert.Single(Css.GetStyleSheets(control)));
        Flush();
        Assert.Equal(144, control.Width);
    }

    [Fact]
    public void SourceBindingCanSwitchAndClearStylesAfterTheParserHasFinished()
    {
        var control = LoadWithAssembly("""
            <Control xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Css.Class="source-target" Css.StyleSheetSource="{Binding SourcePath}" />
            """);
        var model = new SourceModel { SourcePath = Primary };
        control.DataContext = model;
        Flush();
        Assert.Equal(144, control.Width);
        var binding = control.GetBindingExpression(Css.StyleSheetSourceProperty);
        Assert.NotNull(binding);

        model.SourcePath = Secondary;
        Flush();
        Assert.Equal(216, control.Width);
        model.SourcePath = null;
        Flush();
        Assert.True(double.IsNaN(control.Width));
        Assert.Empty(Css.GetStyleSheets(control));
        Assert.Same(binding, control.GetBindingExpression(Css.StyleSheetSourceProperty));
    }

    [Fact]
    public void SourceIsScopedToItsHostSubtree()
    {
        var root = Assert.IsType<StackPanel>(LoadWithAssembly($"""
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border Css.StyleSheetSource="{Primary}"><Control Css.Class="source-target" /></Border>
                <Control Css.Class="source-target" />
            </StackPanel>
            """));
        Flush();
        Assert.Equal(144, Assert.IsType<Control>(Assert.IsType<Border>(root.Children[0]).Child).Width);
        Assert.True(double.IsNaN(Assert.IsType<Control>(root.Children[1]).Width));
    }

    [Fact]
    public void FileUriLoadsCssAndRelativeImports()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jalium-css-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "base.css"), ".source-target { height: 41px }");
            var path = Path.Combine(directory, "main.css");
            File.WriteAllText(path, "@import 'base.css'; .source-target { width: 155px }");
            var control = new Control();
            Css.SetClass(control, "source-target");
            Css.SetStyleSheetSource(control, new Uri(path).AbsoluteUri);
            Flush();
            Assert.Equal(155, control.Width);
            Assert.Equal(41, control.Height);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FrameworkContentElementsCanOwnSourceSheets()
    {
        var span = new Span();
        Css.SetStyleSheetSource(span, Component(Primary));
        Assert.Single(Css.GetStyleSheets(span));
        Css.SetStyleSheetSource(span, null);
        Assert.Empty(Css.GetStyleSheets(span));
    }

    [Fact]
    public void LocalPropertiesKeepTheirExistingPrecedenceAndRepeatedSourceDoesNotDuplicateSheets()
    {
        var control = new Control { Width = 99 };
        Css.SetClass(control, "source-target");
        Css.SetStyleSheetSource(control, Component(Primary));
        Css.SetStyleSheetSource(control, Component(Primary));
        Flush();
        Assert.Equal(99, control.Width);
        Assert.Single(Css.GetStyleSheets(control));
        control.ClearValue(FrameworkElement.WidthProperty);
        Flush();
        Assert.Equal(144, control.Width);
    }

    [Fact]
    public void BuilderRetainsTheDeclarationAssemblyForCompiledAttachedBindings()
    {
        var control = new Control();
        var context = XamlBuilder.BeginComponent(control, sourceAssembly: typeof(CssStyleSheetSourceTests).Assembly);
        XamlBuilder.PushParent(control, context);
        XamlBuilder.SetCompiledAttachedBinding(control, "Css", "StyleSheetSource", "SourcePath", [], [], context);
        XamlBuilder.PopParent(context);
        XamlBuilder.EndComponent(control, context);
        Css.SetClass(control, "source-target");
        var model = new SourceModel { SourcePath = Primary };
        control.DataContext = model;
        Flush();
        Assert.Equal(144, control.Width);
        model.SourcePath = Secondary;
        Flush();
        Assert.Equal(216, control.Width);
    }

    [Fact]
    public void BuilderAcceptsXNullToClearTheSource()
    {
        var control = new Control();
        var context = XamlBuilder.BeginComponent(control, sourceAssembly: typeof(CssStyleSheetSourceTests).Assembly);
        XamlBuilder.PushParent(control, context);
        XamlBuilder.SetAttachedProperty(control, "Css", "StyleSheetSource", Primary, context);
        XamlBuilder.SetAttachedProperty(control, "Css", "StyleSheetSource", "{x:Null}", context);
        XamlBuilder.PopParent(context);
        XamlBuilder.EndComponent(control, context);
        Assert.Empty(Css.GetStyleSheets(control));
    }

    private static FrameworkElement LoadWithAssembly(string markup)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markup));
        return Assert.IsAssignableFrom<FrameworkElement>(XamlReader.Load(
            stream, "Views/CssSourceFixture.jalxaml", typeof(CssStyleSheetSourceTests).Assembly));
    }

    private static void Flush() => CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);

    private sealed class SourceModel : INotifyPropertyChanged
    {
        private string? _sourcePath;
        public string? SourcePath
        {
            get => _sourcePath;
            set { _sourcePath = value; PropertyChanged?.Invoke(this, new(nameof(SourcePath))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

public sealed class CssSourceProbe : Control { }
