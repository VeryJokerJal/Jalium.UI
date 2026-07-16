using System.ComponentModel;
using System.Text;
using System.Xml;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using MarkupXamlReader = Jalium.UI.Markup.XamlReader;

namespace Jalium.UI.Tests;

public sealed class XamlReaderParityTests
{
    [Fact]
    public void StaticOverloadsHonorParserContextAndXmlReaders()
    {
        const string xaml = "<Grid />";
        var context = new ParserContext { BaseUri = new Uri("resource:///Tests/Grid.xaml") };

        Assert.IsType<Grid>(MarkupXamlReader.Parse(xaml, context, useRestrictiveXamlReader: true));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        Assert.IsType<Grid>(MarkupXamlReader.Load(stream, context));

        using XmlReader xml = XmlReader.Create(new StringReader(xaml));
        Assert.IsType<Grid>(MarkupXamlReader.Load(xml, useRestrictiveXamlReader: true));

        using var systemReader = new Jalium.UI.Xaml.XamlXmlReader(new StringReader(xaml));
        Assert.IsType<Grid>(MarkupXamlReader.Load(systemReader));
        Assert.Same(MarkupXamlReader.GetWpfSchemaContext(), MarkupXamlReader.GetWpfSchemaContext());
    }

    [Fact]
    public void InstanceReaderRaisesAsyncCompletionAndSupportsCancellation()
    {
        var originalContext = SynchronizationContext.Current;
        var completionContext = new QueuedSynchronizationContext();
        var reader = new MarkupXamlReader();
        var completions = new List<AsyncCompletedEventArgs>();
        reader.LoadCompleted += (_, args) => completions.Add(args);

        try
        {
            SynchronizationContext.SetSynchronizationContext(completionContext);

            using var successfulStream = new MemoryStream(Encoding.UTF8.GetBytes("<Grid />"));
            Assert.IsType<Grid>(reader.LoadAsync(successfulStream));
            Assert.Empty(completions);
            Assert.Equal(1, completionContext.PendingCount);

            completionContext.RunNext();

            AsyncCompletedEventArgs successfulCompletion = Assert.Single(completions);
            Assert.False(successfulCompletion.Cancelled);
            Assert.Null(successfulCompletion.Error);

            using var cancelledStream = new MemoryStream(Encoding.UTF8.GetBytes("<Grid />"));
            Assert.IsType<Grid>(reader.LoadAsync(cancelledStream));
            reader.CancelAsync();
            Assert.Single(completions);
            Assert.Equal(1, completionContext.PendingCount);

            completionContext.RunNext();

            Assert.Equal(2, completions.Count);
            Assert.True(completions[1].Cancelled);
            Assert.Null(completions[1].Error);
            Assert.Equal(0, completionContext.PendingCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void XamlParseExceptionCarriesWpfLineAndContextSurface()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new XamlParseException("bad markup", 3, 7, inner)
        {
            BaseUri = new Uri("resource:///View.xaml"),
            KeyContext = "key",
            NameContext = "name",
            UidContext = "uid",
        };

        Assert.IsAssignableFrom<SystemException>(exception);
        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(7, exception.LinePosition);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal("key", exception.KeyContext);
        Assert.Equal("name", exception.NameContext);
        Assert.Equal("uid", exception.UidContext);
    }

    [Fact]
    public void XamlBuilderMergesNonEmptyResourceDictionaryProperty()
    {
        XamlBuilderInitializer.Register();

        var parent = new Border();
        ResourceDictionary existing = parent.Resources;
        var merged = new ResourceDictionary { ["MergedBrush"] = "merged" };
        var child = new ResourceDictionary { ["LocalBrush"] = "local" };
        child.MergedDictionaries.Add(merged);
        XamlBuildContext context = XamlBuilder.BeginComponent(
            parent,
            sourceAssembly: typeof(XamlReaderParityTests).Assembly);

        XamlBuilder.ApplyPropertyElementChild(
            parent,
            nameof(FrameworkElement.Resources),
            child,
            context);

        Assert.Same(existing, parent.Resources);
        Assert.Equal("local", parent.Resources["LocalBrush"]);
        Assert.Same(merged, Assert.Single(parent.Resources.MergedDictionaries));
    }

    [Fact]
    public void SystemXamlReaderAndSchemaTypesProvideUsableNodeSurface()
    {
        var schema = new Jalium.UI.Xaml.XamlSchemaContext([typeof(Grid).Assembly]);
        Jalium.UI.Xaml.XamlType type = schema.GetXamlType(typeof(Grid));
        Assert.Equal(nameof(Grid), type.Name);
        Assert.Equal(typeof(Grid), type.UnderlyingType);

        using var reader = new Jalium.UI.Xaml.XamlXmlReader(new StringReader("<Grid />"));
        Assert.True(reader.Read());
        Assert.Equal(Jalium.UI.Xaml.XamlNodeType.StartObject, reader.NodeType);
        Assert.Equal(nameof(Grid), reader.Type!.Name);

        var declaration = new Jalium.UI.Xaml.NamespaceDeclaration("urn:test", "t");
        Assert.Equal("t", declaration.Prefix);
        Assert.Equal("urn:test", declaration.Namespace);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public int PendingCount => _callbacks.Count;

        public override void Post(SendOrPostCallback d, object? state)
        {
            _callbacks.Enqueue((d, state));
        }

        public void RunNext()
        {
            (SendOrPostCallback callback, object? state) = _callbacks.Dequeue();
            callback(state);
        }
    }
}
