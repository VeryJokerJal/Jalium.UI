using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Xml;
using Jalium.UI.Markup;

namespace Jalium.UI.Xaml;

internal readonly record struct XamlNodeSnapshot(
    XamlNodeType NodeType,
    XamlType? Type = null,
    XamlMember? Member = null,
    NamespaceDeclaration? Namespace = null,
    object? Value = null,
    object? Instance = null,
    int LineNumber = 0,
    int LinePosition = 0)
{
    public static XamlNodeSnapshot FromReader(XamlReader reader)
    {
        IXamlLineInfo? lineInfo = reader as IXamlLineInfo;
        return new XamlNodeSnapshot(
            reader.NodeType,
            reader.Type,
            reader.Member,
            reader.Namespace,
            reader.Value,
            reader is XamlObjectReader objectReader ? objectReader.Instance : null,
            lineInfo?.LineNumber ?? 0,
            lineInfo?.LinePosition ?? 0);
    }
}

internal sealed class XamlNodeBufferWriter : XamlWriter
{
    private readonly Action<XamlNodeSnapshot> _write;

    public XamlNodeBufferWriter(XamlSchemaContext schemaContext, Action<XamlNodeSnapshot> write)
    {
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public override XamlSchemaContext SchemaContext { get; }
    public override void WriteEndMember() => _write(new(XamlNodeType.EndMember));
    public override void WriteEndObject() => _write(new(XamlNodeType.EndObject));
    public override void WriteGetObject() => _write(new(XamlNodeType.GetObject));
    public override void WriteNamespace(NamespaceDeclaration namespaceDeclaration) => _write(new(XamlNodeType.NamespaceDeclaration, Namespace: namespaceDeclaration ?? throw new ArgumentNullException(nameof(namespaceDeclaration))));
    public override void WriteStartMember(XamlMember xamlMember) => _write(new(XamlNodeType.StartMember, Member: xamlMember ?? throw new ArgumentNullException(nameof(xamlMember))));
    public override void WriteStartObject(XamlType type) => _write(new(XamlNodeType.StartObject, Type: type ?? throw new ArgumentNullException(nameof(type))));
    public override void WriteValue(object? value) => _write(new(XamlNodeType.Value, Value: value));
}

internal sealed class XamlNodeListReader : XamlReader, IXamlIndexingReader, IXamlLineInfo
{
    private readonly Func<IReadOnlyList<XamlNodeSnapshot>> _nodes;
    private int _index = -1;
    private bool _isEof;

    public XamlNodeListReader(XamlSchemaContext schemaContext, Func<IReadOnlyList<XamlNodeSnapshot>> nodes)
    {
        SchemaContext = schemaContext;
        _nodes = nodes;
    }

    private XamlNodeSnapshot Current => _index >= 0 && _index < _nodes().Count ? _nodes()[_index] : default;
    public int Count => _nodes().Count;
    public int CurrentIndex
    {
        get => _index;
        set
        {
            if (value < -1 || value >= Count) throw new ArgumentOutOfRangeException(nameof(value));
            _index = value;
            _isEof = false;
        }
    }
    public override bool IsEof => _isEof;
    public override XamlMember? Member => Current.Member;
    public override NamespaceDeclaration? Namespace => Current.Namespace;
    public override XamlNodeType NodeType => Current.NodeType;
    public override XamlSchemaContext SchemaContext { get; }
    public override XamlType? Type => Current.Type;
    public override object? Value => Current.Value;
    public bool HasLineInfo => Current.LineNumber > 0;
    public int LineNumber => Current.LineNumber;
    public int LinePosition => Current.LinePosition;

    public override bool Read()
    {
        int next = _index + 1;
        if (next >= Count)
        {
            _index = Count;
            _isEof = true;
            return false;
        }

        _index = next;
        return true;
    }
}

public class XamlNodeList
{
    private readonly List<XamlNodeSnapshot> _nodes;

    public XamlNodeList(XamlSchemaContext schemaContext) : this(schemaContext, 0) { }

    public XamlNodeList(XamlSchemaContext schemaContext, int size)
    {
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        _nodes = new List<XamlNodeSnapshot>(size);
        Writer = new XamlNodeBufferWriter(schemaContext, _nodes.Add);
    }

    private XamlSchemaContext SchemaContext { get; }
    public int Count => _nodes.Count;
    public XamlWriter Writer { get; }
    public void Clear() => _nodes.Clear();
    public XamlReader GetReader() => new XamlNodeListReader(SchemaContext, () => _nodes.ToArray());
}

internal sealed class XamlNodeQueueReader : XamlReader, IXamlLineInfo
{
    private readonly Queue<XamlNodeSnapshot> _queue;
    private XamlNodeSnapshot _current;
    private bool _isEof;

    public XamlNodeQueueReader(XamlSchemaContext schemaContext, Queue<XamlNodeSnapshot> queue)
    {
        SchemaContext = schemaContext;
        _queue = queue;
    }

    public override bool IsEof => _isEof;
    public override XamlMember? Member => _current.Member;
    public override NamespaceDeclaration? Namespace => _current.Namespace;
    public override XamlNodeType NodeType => _current.NodeType;
    public override XamlSchemaContext SchemaContext { get; }
    public override XamlType? Type => _current.Type;
    public override object? Value => _current.Value;
    public bool HasLineInfo => _current.LineNumber > 0;
    public int LineNumber => _current.LineNumber;
    public int LinePosition => _current.LinePosition;

    public override bool Read()
    {
        lock (_queue)
        {
            if (_queue.Count == 0)
            {
                _current = default;
                _isEof = true;
                return false;
            }

            _current = _queue.Dequeue();
            _isEof = false;
            return true;
        }
    }
}

public class XamlNodeQueue
{
    private readonly Queue<XamlNodeSnapshot> _nodes = new();

    public XamlNodeQueue(XamlSchemaContext schemaContext)
    {
        ArgumentNullException.ThrowIfNull(schemaContext);
        Writer = new XamlNodeBufferWriter(schemaContext, node => { lock (_nodes) _nodes.Enqueue(node); });
        Reader = new XamlNodeQueueReader(schemaContext, _nodes);
    }

    public int Count { get { lock (_nodes) return _nodes.Count; } }
    public bool IsEmpty => Count == 0;
    public XamlReader Reader { get; }
    public XamlWriter Writer { get; }
}

public class XamlBackgroundReader : XamlReader, IXamlLineInfo
{
    private readonly XamlReader _wrappedReader;
    private readonly BlockingCollection<XamlNodeSnapshot> _buffer = new();
    private XamlNodeSnapshot _current;
    private Thread? _thread;
    private Exception? _backgroundException;
    private bool _isEof;
    private bool _disposed;

    public XamlBackgroundReader(XamlReader wrappedReader)
        => _wrappedReader = wrappedReader ?? throw new ArgumentNullException(nameof(wrappedReader));

    public bool HasLineInfo => _current.LineNumber > 0;
    public override bool IsEof => _isEof;
    public int LineNumber => _current.LineNumber;
    public int LinePosition => _current.LinePosition;
    public override XamlMember? Member => _current.Member;
    public override NamespaceDeclaration? Namespace => _current.Namespace;
    public override XamlNodeType NodeType => _current.NodeType;
    public override XamlSchemaContext SchemaContext => _wrappedReader.SchemaContext;
    public override XamlType? Type => _current.Type;
    public override object? Value => _current.Value;

    public override bool Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is null)
        {
            if (!_wrappedReader.Read())
            {
                _isEof = true;
                return false;
            }

            _current = XamlNodeSnapshot.FromReader(_wrappedReader);
            return true;
        }

        if (_buffer.TryTake(out XamlNodeSnapshot node, Timeout.Infinite))
        {
            _current = node;
            return true;
        }

        _isEof = true;
        if (_backgroundException is not null)
        {
            throw new XamlException("The background XAML reader failed.", _backgroundException);
        }
        return false;
    }

    public void StartThread() => StartThread(null);

    public void StartThread(string? threadName)
    {
        if (_thread is not null) throw new InvalidOperationException("The background reader thread has already been started.");
        _thread = new Thread(ReadWorker) { IsBackground = true, Name = threadName ?? "XamlBackgroundReader" };
        _thread.Start();
    }

    private void ReadWorker()
    {
        try
        {
            while (_wrappedReader.Read())
            {
                _buffer.Add(XamlNodeSnapshot.FromReader(_wrappedReader));
            }
        }
        catch (Exception exception)
        {
            _backgroundException = exception;
        }
        finally
        {
            _buffer.CompleteAdding();
        }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing)
        {
            _buffer.Dispose();
            _wrappedReader.Dispose();
        }
        base.Dispose(disposing);
    }
}

public class XamlObjectReader : XamlReader, IXamlLineInfo
{
    private static readonly string[] s_excludedProperties = ["Parent", "TemplatedParent", "Dispatcher", "DependencyObjectType", "NativeHandle"];
    private readonly XamlNodeListReader _reader;
    private readonly IReadOnlyList<XamlNodeSnapshot> _nodes;

    public XamlObjectReader(object instance) : this(instance, new XamlSchemaContext(), new XamlObjectReaderSettings()) { }
    public XamlObjectReader(object instance, XamlObjectReaderSettings settings) : this(instance, new XamlSchemaContext(), settings) { }
    public XamlObjectReader(object instance, XamlSchemaContext schemaContext) : this(instance, schemaContext, new XamlObjectReaderSettings()) { }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Object reader is an explicitly reflection-based compatibility API.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Object reader reflects over XAML-reachable runtime types preserved by the schema context.")]
    public XamlObjectReader(object instance, XamlSchemaContext schemaContext, XamlObjectReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(instance);
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        ArgumentNullException.ThrowIfNull(settings);
        var nodes = new List<XamlNodeSnapshot>();
        WriteObject(nodes, instance, schemaContext, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
        _nodes = nodes;
        _reader = new XamlNodeListReader(schemaContext, () => _nodes);
    }

    public virtual object? Instance => _reader.CurrentIndex >= 0 && _reader.CurrentIndex < _nodes.Count ? _nodes[_reader.CurrentIndex].Instance : null;
    public bool HasLineInfo => false;
    public override bool IsEof => _reader.IsEof;
    public int LineNumber => 0;
    public int LinePosition => 0;
    public override XamlMember? Member => _reader.Member;
    public override NamespaceDeclaration? Namespace => _reader.Namespace;
    public override XamlNodeType NodeType => _reader.NodeType;
    public override XamlSchemaContext SchemaContext { get; }
    public override XamlType? Type => _reader.Type;
    public override object? Value => _reader.Value;
    public override bool Read() => _reader.Read();

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Object reader is an explicitly reflection-based compatibility API.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Object reader reflects over XAML-reachable runtime types preserved by the schema context.")]
    private static void WriteObject(List<XamlNodeSnapshot> nodes, object instance, XamlSchemaContext schemaContext, HashSet<object> visited, int depth)
    {
        if (depth > 64) throw new XamlObjectReaderException("The object graph exceeds the supported depth.");
        Type runtimeType = instance.GetType();
        XamlType xamlType = schemaContext.GetXamlType(runtimeType);
        nodes.Add(new(XamlNodeType.StartObject, Type: xamlType, Instance: instance));

        bool track = !runtimeType.IsValueType && instance is not string;
        if (track && !visited.Add(instance))
        {
            nodes.Add(new(XamlNodeType.EndObject, Instance: instance));
            return;
        }

        foreach (PropertyInfo property in runtimeType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0 || s_excludedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                continue;
            }

            object? value;
            try { value = property.GetValue(instance); }
            catch { continue; }
            if (value is null) continue;

            nodes.Add(new(XamlNodeType.StartMember, Member: new XamlMember(property.Name), Instance: instance));
            if (IsScalar(value.GetType()))
            {
                nodes.Add(new(XamlNodeType.Value, Value: value, Instance: instance));
            }
            else
            {
                WriteObject(nodes, value, schemaContext, visited, depth + 1);
            }
            nodes.Add(new(XamlNodeType.EndMember, Instance: instance));
        }

        if (track) visited.Remove(instance);
        nodes.Add(new(XamlNodeType.EndObject, Instance: instance));
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Guid) || type == typeof(Uri) || type == typeof(Type);
    }
}

public class XamlObjectWriter : XamlWriter, IXamlLineInfoConsumer
{
    private sealed class Frame
    {
        public required object Instance { get; init; }
        public required XamlType Type { get; init; }
        public XamlMember? Member { get; set; }
        public XamlMember? ParentMember { get; init; }
        public HashSet<string> WrittenMembers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class WriterNameScope : INameScope
    {
        private readonly Dictionary<string, object> _names = new(StringComparer.Ordinal);
        public object? FindName(string name) => _names.TryGetValue(name, out object? value) ? value : null;
        public void RegisterName(string name, object scopedElement) => _names.Add(name, scopedElement);
        public void UnregisterName(string name) => _names.Remove(name);
    }

    private readonly Stack<Frame> _frames = new();
    private readonly XamlObjectWriterSettings _settings;
    private int _lineNumber;
    private int _linePosition;

    public XamlObjectWriter(XamlSchemaContext schemaContext) : this(schemaContext, new XamlObjectWriterSettings()) { }

    public XamlObjectWriter(XamlSchemaContext schemaContext, XamlObjectWriterSettings settings)
    {
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        _settings = settings is null ? new XamlObjectWriterSettings() : new XamlObjectWriterSettings(settings);
        RootNameScope = _settings.ExternalNameScope ?? new WriterNameScope();
    }

    public virtual object? Result { get; private set; }
    public INameScope RootNameScope { get; }
    public override XamlSchemaContext SchemaContext { get; }
    public bool ShouldProvideLineInfo => true;

    public void Clear() { _frames.Clear(); Result = null; }
    public void SetLineInfo(int lineNumber, int linePosition) { _lineNumber = lineNumber; _linePosition = linePosition; }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Object writer is an explicitly reflection-based compatibility API.")]
    public override void WriteStartObject(XamlType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        XamlMember? parentMember = _frames.Count > 0 ? _frames.Peek().Member : null;
        object instance;
        if (_frames.Count == 0 && _settings.RootObjectInstance is not null)
        {
            instance = _settings.RootObjectInstance;
            if (type.UnderlyingType is not null && !type.UnderlyingType.IsInstanceOfType(instance))
                throw new XamlParseException("The supplied root object is not compatible with the requested XAML type.");
        }
        else
        {
            Type runtimeType = type.UnderlyingType ?? throw new XamlObjectWriterException($"Cannot create unknown XAML type '{type}'.");
            instance = Activator.CreateInstance(runtimeType) ?? throw new XamlObjectWriterException($"Could not create '{runtimeType}'.");
        }

        var frame = new Frame { Instance = instance, Type = type, ParentMember = parentMember };
        _frames.Push(frame);
        if (instance is ISupportInitialize initialize) initialize.BeginInit();
        OnAfterBeginInit(instance);
        OnBeforeProperties(instance);
    }

    public override void WriteGetObject()
    {
        if (_frames.Count == 0 || _frames.Peek().Member is null) throw new XamlObjectWriterException("GetObject requires an active member.");
        Frame parent = _frames.Peek();
        XamlMember member = parent.Member!;
        object? instance = GetMemberValue(parent.Instance, member);
        if (instance is null) throw new XamlObjectWriterException($"Member '{member.Name}' returned null.");
        _frames.Push(new Frame { Instance = instance, Type = SchemaContext.GetXamlType(instance.GetType()), ParentMember = member });
    }

    public override void WriteStartMember(XamlMember xamlMember)
    {
        ArgumentNullException.ThrowIfNull(xamlMember);
        if (_frames.Count == 0) throw new XamlObjectWriterException("A member must be written inside an object.");
        Frame frame = _frames.Peek();
        if (frame.Member is not null) throw new XamlObjectWriterException("A member is already active.");
        if (!_settings.SkipDuplicatePropertyCheck && !frame.WrittenMembers.Add(xamlMember.Name))
            throw new XamlDuplicateMemberException(xamlMember, frame.Type);
        frame.Member = xamlMember;
    }

    public override void WriteValue(object? value)
    {
        if (_frames.Count == 0) throw new XamlObjectWriterException("A value must be written inside an object.");
        Frame frame = _frames.Peek();
        if (frame.Member is null)
        {
            AddToCollection(frame.Instance, value);
            return;
        }
        SetMemberValue(frame.Instance, frame.Member, value);
    }

    public override void WriteEndMember()
    {
        if (_frames.Count == 0 || _frames.Peek().Member is null) throw new XamlObjectWriterException("No member is active.");
        _frames.Peek().Member = null;
    }

    public override void WriteEndObject()
    {
        if (_frames.Count == 0) throw new XamlObjectWriterException("No object is active.");
        Frame frame = _frames.Pop();
        OnAfterProperties(frame.Instance);
        if (frame.Instance is ISupportInitialize initialize) initialize.EndInit();
        OnAfterEndInit(frame.Instance);

        if (_frames.Count == 0)
        {
            Result = frame.Instance;
        }
        else if (frame.ParentMember is not null)
        {
            SetMemberValue(_frames.Peek().Instance, frame.ParentMember, frame.Instance);
        }
        else
        {
            AddToCollection(_frames.Peek().Instance, frame.Instance);
        }
    }

    public override void WriteNamespace(NamespaceDeclaration namespaceDeclaration) => ArgumentNullException.ThrowIfNull(namespaceDeclaration);

    protected virtual void OnAfterBeginInit(object value) => _settings.AfterBeginInitHandler?.Invoke(this, CreateEventArgs(value));
    protected virtual void OnAfterEndInit(object value) => _settings.AfterEndInitHandler?.Invoke(this, CreateEventArgs(value));
    protected virtual void OnAfterProperties(object value) => _settings.AfterPropertiesHandler?.Invoke(this, CreateEventArgs(value));
    protected virtual void OnBeforeProperties(object value) => _settings.BeforePropertiesHandler?.Invoke(this, CreateEventArgs(value));

    protected virtual bool OnSetValue(object eventSender, XamlMember member, object? value)
    {
        if (_settings.XamlSetValueHandler is null) return false;
        var args = new XamlSetValueEventArgs(member, value!);
        _settings.XamlSetValueHandler(eventSender, args);
        return args.Handled;
    }

    private XamlObjectEventArgs CreateEventArgs(object value) => new(value, _lineNumber, _linePosition, _settings.SourceBamlUri);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Object writer is an explicitly reflection-based compatibility API.")]
    private static object? GetMemberValue(object instance, XamlMember member)
        => instance.GetType().GetProperty(member.Name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Object writer is an explicitly reflection-based compatibility API.")]
    private void SetMemberValue(object instance, XamlMember member, object? value)
    {
        if (OnSetValue(instance, member, value)) return;
        PropertyInfo? property = instance.GetType().GetProperty(member.Name, BindingFlags.Instance | BindingFlags.Public);
        if (property is null) throw new XamlObjectWriterException($"Member '{member.Name}' was not found on '{instance.GetType()}'.");

        if (property.CanWrite)
        {
            property.SetValue(instance, ConvertValue(value, property.PropertyType));
        }
        else if (property.GetValue(instance) is object collection)
        {
            AddToCollection(collection, value);
        }
        else
        {
            throw new XamlObjectWriterException($"Member '{member.Name}' is read-only.");
        }

        if (string.Equals(member.Name, "Name", StringComparison.Ordinal) && value is string name && !string.IsNullOrEmpty(name))
        {
            try { RootNameScope.RegisterName(name, instance); } catch (ArgumentException) { }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Type conversion is the purpose of this reflection compatibility API.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "XAML-reachable destination types are preserved by the schema context and generated registries.")]
    private static object? ConvertValue(object? value, Type destinationType)
    {
        if (value is null || destinationType.IsInstanceOfType(value)) return value;
        Type effectiveType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        if (value is string text)
        {
            if (effectiveType.IsEnum) return Enum.Parse(effectiveType, text, ignoreCase: true);
            System.ComponentModel.TypeConverter converter = TypeDescriptor.GetConverter(effectiveType);
            if (converter.CanConvertFrom(typeof(string))) return converter.ConvertFromInvariantString(text);
        }
        return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Object writer is an explicitly reflection-based compatibility API.")]
    private static void AddToCollection(object collection, object? value)
    {
        if (collection is IList list) { list.Add(value); return; }
        MethodInfo? add = collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "Add" && method.GetParameters().Length == 1);
        if (add is null) throw new XamlObjectWriterException($"'{collection.GetType()}' is not a writable collection.");
        add.Invoke(collection, [value]);
    }
}

public class XamlXmlWriter : XamlWriter
{
    private readonly XmlWriter _writer;
    private readonly bool _ownsWriter;
    private readonly Stack<string> _objects = new();
    private readonly Stack<bool> _members = new();
    private readonly List<NamespaceDeclaration> _pendingNamespaces = new();

    public XamlXmlWriter(Stream stream, XamlSchemaContext schemaContext) : this(stream, schemaContext, new XamlXmlWriterSettings()) { }
    public XamlXmlWriter(Stream stream, XamlSchemaContext schemaContext, XamlXmlWriterSettings settings)
        : this(XmlWriter.Create(stream ?? throw new ArgumentNullException(nameof(stream)), new XmlWriterSettings { Indent = true, CloseOutput = settings?.CloseOutput ?? false }), schemaContext, settings, ownsWriter: true) { }
    public XamlXmlWriter(TextWriter textWriter, XamlSchemaContext schemaContext) : this(textWriter, schemaContext, new XamlXmlWriterSettings()) { }
    public XamlXmlWriter(TextWriter textWriter, XamlSchemaContext schemaContext, XamlXmlWriterSettings settings)
        : this(XmlWriter.Create(textWriter ?? throw new ArgumentNullException(nameof(textWriter)), new XmlWriterSettings { Indent = true, CloseOutput = settings?.CloseOutput ?? false }), schemaContext, settings, ownsWriter: true) { }
    public XamlXmlWriter(XmlWriter xmlWriter, XamlSchemaContext schemaContext) : this(xmlWriter, schemaContext, new XamlXmlWriterSettings()) { }
    public XamlXmlWriter(XmlWriter xmlWriter, XamlSchemaContext schemaContext, XamlXmlWriterSettings settings)
        : this(xmlWriter, schemaContext, settings, ownsWriter: false) { }

    private XamlXmlWriter(XmlWriter xmlWriter, XamlSchemaContext schemaContext, XamlXmlWriterSettings? settings, bool ownsWriter)
    {
        _writer = xmlWriter ?? throw new ArgumentNullException(nameof(xmlWriter));
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        Settings = settings?.Copy() ?? new XamlXmlWriterSettings();
        _ownsWriter = ownsWriter;
    }

    public override XamlSchemaContext SchemaContext { get; }
    public XamlXmlWriterSettings Settings { get; }
    public void Flush() => _writer.Flush();

    public override void WriteNamespace(NamespaceDeclaration namespaceDeclaration)
    {
        ArgumentNullException.ThrowIfNull(namespaceDeclaration);
        _pendingNamespaces.Add(namespaceDeclaration);
    }

    public override void WriteStartObject(XamlType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        string ns = IsXmlNamespace(type.PreferredXamlNamespace) ? type.PreferredXamlNamespace : string.Empty;
        string? prefix = _pendingNamespaces.FirstOrDefault(item => item.Namespace == ns)?.Prefix;
        _writer.WriteStartElement(prefix, type.Name, ns);
        foreach (NamespaceDeclaration declaration in _pendingNamespaces)
        {
            if (string.IsNullOrEmpty(declaration.Prefix)) _writer.WriteAttributeString("xmlns", declaration.Namespace);
            else _writer.WriteAttributeString("xmlns", declaration.Prefix, null, declaration.Namespace);
        }
        _pendingNamespaces.Clear();
        _objects.Push(type.Name);
    }

    public override void WriteGetObject() { }

    public override void WriteStartMember(XamlMember xamlMember)
    {
        ArgumentNullException.ThrowIfNull(xamlMember);
        string owner = _objects.Count > 0 ? _objects.Peek() : "Object";
        _writer.WriteStartElement($"{owner}.{xamlMember.Name}");
        _members.Push(true);
    }

    public override void WriteValue(object? value)
    {
        if (value is null) return;
        _writer.WriteString(value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty);
    }

    public override void WriteEndMember()
    {
        if (_members.Count == 0) throw new InvalidOperationException("No XAML member is active.");
        _members.Pop();
        _writer.WriteEndElement();
    }

    public override void WriteEndObject()
    {
        if (_objects.Count == 0) throw new InvalidOperationException("No XAML object is active.");
        _objects.Pop();
        _writer.WriteEndElement();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Settings.CloseOutput || _ownsWriter) _writer.Dispose();
            else _writer.Flush();
        }
        base.Dispose(disposing);
    }

    private static bool IsXmlNamespace(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("clr-namespace:", StringComparison.Ordinal);
}
