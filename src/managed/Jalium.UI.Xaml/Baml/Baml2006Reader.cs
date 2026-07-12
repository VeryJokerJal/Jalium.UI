using System.Reflection;
using System.Text;
using System.Xml;
using Jalium.UI.Xaml;

namespace Jalium.UI.Markup;

/// <summary>
/// Settings for <see cref="Baml2006Reader"/>. The type remains public for Jalium source
/// compatibility while using the repository's canonical XAML-reader settings contract.
/// </summary>
public class Baml2006ReaderSettings : XamlReaderSettings
{
    public Baml2006ReaderSettings()
    {
    }

    public Baml2006ReaderSettings(Baml2006ReaderSettings settings)
        : base(settings ?? throw new ArgumentNullException(nameof(settings)))
    {
        OwnsStream = settings.OwnsStream;
        IsBamlFragment = settings.IsBamlFragment;
    }

    public Baml2006ReaderSettings(XamlReaderSettings settings)
        : base(settings ?? throw new ArgumentNullException(nameof(settings)))
    {
    }

    /// <summary>Gets or sets whether disposing the reader also disposes its source stream.</summary>
    public bool OwnsStream { get; set; }

    /// <summary>Gets or sets whether the source represents a fragment rather than a document.</summary>
    public bool IsBamlFragment { get; set; }
}

/// <summary>
/// Exposes a XAML node stream for markup supplied through the WPF BAML-reader compatibility API.
/// Jalium does not consume WPF's private binary BAML record format; it maps this entry point to
/// the native JALXAML tokenizer so textual markup still produces a complete, line-aware node stream.
/// </summary>
public class Baml2006Reader : Jalium.UI.Xaml.XamlReader, IXamlLineInfo
{
    private readonly record struct ReaderNode(
        XamlNodeType NodeType,
        XamlType? Type = null,
        XamlMember? Member = null,
        NamespaceDeclaration? Namespace = null,
        object? Value = null,
        int LineNumber = 0,
        int LinePosition = 0);

    private readonly record struct AttributeNode(
        string LocalName,
        string Prefix,
        string NamespaceUri,
        string Value,
        bool IsNamespace,
        int LineNumber,
        int LinePosition);

    private readonly Stream _stream;
    private readonly Baml2006ReaderSettings _settings;
    private readonly XamlSchemaContext _schemaContext;
    private List<ReaderNode>? _nodes;
    private int _index = -1;
    private bool _isEof;
    private bool _isDisposed;

    public Baml2006Reader(string fileName)
        : this(OpenFile(fileName), new Baml2006ReaderSettings { OwnsStream = true })
    {
    }

    public Baml2006Reader(Stream stream)
        : this(stream, new Baml2006ReaderSettings())
    {
    }

    public Baml2006Reader(Stream stream, XamlReaderSettings xamlReaderSettings)
        : this(stream, new Baml2006ReaderSettings(xamlReaderSettings))
    {
    }

    /// <summary>
    /// Initializes a reader with Jalium's extended ownership settings.
    /// </summary>
    public Baml2006Reader(Stream stream, Baml2006ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(settings);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The source stream must be readable.", nameof(stream));
        }

        _stream = stream;
        _settings = new Baml2006ReaderSettings(settings);
        _schemaContext = new XamlSchemaContext(GetReferenceAssemblies(settings.LocalAssembly));
    }

    public override XamlNodeType NodeType => Current.NodeType;

    public override bool IsEof => _isEof;

    public override NamespaceDeclaration? Namespace => Current.Namespace;

    public override XamlType? Type => Current.Type;

    public override object? Value => Current.Value;

    public override XamlMember? Member => Current.Member;

    public override XamlSchemaContext SchemaContext => _schemaContext;

    bool IXamlLineInfo.HasLineInfo =>
        _settings.ProvideLineInfo && Current.LineNumber > 0;

    int IXamlLineInfo.LineNumber =>
        _settings.ProvideLineInfo ? Current.LineNumber : 0;

    int IXamlLineInfo.LinePosition =>
        _settings.ProvideLineInfo ? Current.LinePosition : 0;

    private ReaderNode Current =>
        _nodes is not null && _index >= 0 && _index < _nodes.Count
            ? _nodes[_index]
            : default;

    public override bool Read()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isEof)
        {
            return false;
        }

        EnsureParsed();
        int next = _index + 1;
        if (next >= _nodes!.Count)
        {
            _index = _nodes.Count;
            _isEof = true;
            return false;
        }

        _index = next;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing && _settings.OwnsStream)
        {
            _stream.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }

    private void EnsureParsed()
    {
        if (_nodes is not null)
        {
            return;
        }

        string markup;
        using (var textReader = new StreamReader(
            _stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true))
        {
            markup = textReader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(markup))
        {
            _nodes = [];
            return;
        }

        if (markup.TrimStart()[0] != '<')
        {
            throw new NotSupportedException(
                "The WPF private binary BAML record format is not portable. " +
                "Supply textual XAML/JALXAML when using Jalium.UI's mapped Baml2006Reader.");
        }

        var nodes = new List<ReaderNode>();
        using XmlReader reader = JalxamlParser.CreateReader(markup);
        while (reader.Read())
        {
            (int lineNumber, int linePosition) = GetLineInfo(reader);
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    AddElementNodes(reader, nodes, lineNumber, linePosition);
                    break;

                case XmlNodeType.EndElement:
                    nodes.Add(new ReaderNode(
                        XamlNodeType.EndObject,
                        LineNumber: lineNumber,
                        LinePosition: linePosition));
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    nodes.Add(new ReaderNode(
                        XamlNodeType.Value,
                        Value: reader.Value,
                        LineNumber: lineNumber,
                        LinePosition: linePosition));
                    break;
            }
        }

        _nodes = nodes;
    }

    private void AddElementNodes(
        XmlReader reader,
        List<ReaderNode> nodes,
        int lineNumber,
        int linePosition)
    {
        List<AttributeNode> attributes = ReadAttributes(reader);
        foreach (AttributeNode attribute in attributes.Where(static attribute => attribute.IsNamespace))
        {
            string prefix = attribute.Prefix == "xmlns" ? attribute.LocalName : string.Empty;
            nodes.Add(new ReaderNode(
                XamlNodeType.NamespaceDeclaration,
                Namespace: new NamespaceDeclaration(attribute.Value, prefix),
                LineNumber: attribute.LineNumber,
                LinePosition: attribute.LinePosition));
        }

        XamlType type = _schemaContext.GetXamlType(reader.NamespaceURI, reader.LocalName)
            ?? new XamlType(reader.NamespaceURI, reader.LocalName, [], _schemaContext);
        nodes.Add(new ReaderNode(
            XamlNodeType.StartObject,
            Type: type,
            LineNumber: lineNumber,
            LinePosition: linePosition));

        foreach (AttributeNode attribute in attributes.Where(static attribute => !attribute.IsNamespace))
        {
            var member = new XamlMember(attribute.LocalName);
            nodes.Add(new ReaderNode(
                XamlNodeType.StartMember,
                Member: member,
                LineNumber: attribute.LineNumber,
                LinePosition: attribute.LinePosition));
            nodes.Add(new ReaderNode(
                XamlNodeType.Value,
                Value: attribute.Value,
                LineNumber: attribute.LineNumber,
                LinePosition: attribute.LinePosition));
            nodes.Add(new ReaderNode(
                XamlNodeType.EndMember,
                LineNumber: attribute.LineNumber,
                LinePosition: attribute.LinePosition));
        }

        if (reader.IsEmptyElement)
        {
            nodes.Add(new ReaderNode(
                XamlNodeType.EndObject,
                LineNumber: lineNumber,
                LinePosition: linePosition));
        }
    }

    private static List<AttributeNode> ReadAttributes(XmlReader reader)
    {
        var attributes = new List<AttributeNode>(reader.AttributeCount);
        if (!reader.MoveToFirstAttribute())
        {
            return attributes;
        }

        do
        {
            (int lineNumber, int linePosition) = GetLineInfo(reader);
            bool isNamespace = reader.Prefix == "xmlns" || reader.Name == "xmlns";
            attributes.Add(new AttributeNode(
                reader.LocalName,
                reader.Prefix,
                reader.NamespaceURI,
                reader.Value,
                isNamespace,
                lineNumber,
                linePosition));
        }
        while (reader.MoveToNextAttribute());

        reader.MoveToElement();
        return attributes;
    }

    private static (int LineNumber, int LinePosition) GetLineInfo(XmlReader reader)
    {
        if (reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            return (lineInfo.LineNumber, lineInfo.LinePosition);
        }

        return (0, 0);
    }

    private static IEnumerable<Assembly> GetReferenceAssemblies(Assembly? localAssembly)
    {
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies();
        return localAssembly is null ? assemblies : assemblies.Prepend(localAssembly).Distinct();
    }

    private static FileStream OpenFile(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
