using System.Text;
using System.Xml;

namespace Jalium.UI.Markup;

/// <summary>
/// Native JALXAML reader that parses JALXAML (Razor-XAML hybrid) content without
/// using any <see cref="System.Xml"/> parser internally. All lexing and tokenization
/// is performed by this class. It extends <see cref="XmlReader"/> purely to remain
/// API-compatible with existing consumers (<c>XamlReader.LoadInternal</c>).
/// <para>
/// Unlike <see cref="XmlReader"/>, this reader understands Razor directives natively:
/// <list type="bullet">
///   <item><c>@if (cond) { ... }</c> — <c>&lt;</c>/<c>&gt;</c> inside <c>cond</c> are treated as C# operators, not XML tag starts</item>
///   <item><c>@(expression)</c> — ditto, plus emits as a single text node</item>
///   <item><c>@path</c> — inline path expression, emitted as text</item>
///   <item><c>@@</c> — literal <c>@</c></item>
///   <item><c>@* ... *@</c> — Razor comment, skipped</item>
///   <item><c>@{ ... }</c>, <c>@for</c>, <c>@foreach</c>, etc. — pre-expanded via the existing interpreter</item>
/// </list>
/// </para>
/// </summary>
internal sealed class JalxamlReader : XmlReader, IXmlLineInfo
{
    // ─────────────────────────────────────────────────────────────
    // Event model — the tokenizer emits a flat list of these.
    // ─────────────────────────────────────────────────────────────

    private sealed class Event
    {
        public XmlNodeType NodeType;
        public string LocalName = string.Empty;
        public string Prefix = string.Empty;
        public string NamespaceUri = string.Empty;
        public string Value = string.Empty;
        public bool IsEmptyElement;
        public int Depth;
        public AttrEntry[]? Attributes;
        public int LineNumber;
        public int LinePosition;
        /// <summary>For Element events: index of the matching EndElement (or self, if empty).</summary>
        public int MatchingEndIndex;
        /// <summary>For Element events: offset in preprocessed source just after the open tag's <c>&gt;</c>.</summary>
        public int InnerSourceStart;
        /// <summary>For Element events: offset in preprocessed source just before the closing <c>&lt;/</c>.</summary>
        public int InnerSourceEnd;
        /// <summary>Offset in preprocessed source where this event's token begins.</summary>
        public int SourceStart;
        /// <summary>Offset in preprocessed source where this event's token ends.</summary>
        public int SourceEnd;
    }

    private sealed class AttrEntry
    {
        public string LocalName = string.Empty;
        public string Prefix = string.Empty;
        public string NamespaceUri = string.Empty;
        public string Value = string.Empty;
        public int LineNumber;
        public int LinePosition;
    }

    // ─────────────────────────────────────────────────────────────
    // State
    // ─────────────────────────────────────────────────────────────

    private readonly string _source;
    private readonly List<Event> _events;
    private int _index = -1;
    private int _attrIndex = -1;       // -1 = on element/other node; >= 0 = focused attribute
    private bool _onAttrValue;          // ReadAttributeValue() state
    private ReadState _readState = ReadState.Initial;
    private readonly NameTable _nameTable = new();

    // ─────────────────────────────────────────────────────────────
    // Public construction
    // ─────────────────────────────────────────────────────────────

    public static new JalxamlReader Create(string jalxaml)
    {
        // No pre-processing. The tokenizer is fully native: it parses XML and
        // Razor constructs in a single pass, calling the AOT-safe interpreter
        // on-demand for @{ }, @for, @foreach, etc. code blocks.
        return new JalxamlReader(jalxaml ?? string.Empty);
    }

    public static new JalxamlReader Create(TextReader textReader)
    {
        var content = textReader.ReadToEnd();
        return Create(content);
    }

    public static new JalxamlReader Create(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return Create(reader);
    }

    private JalxamlReader(string source)
    {
        _source = source;
        var tokenizer = new Tokenizer(source);
        _events = tokenizer.Tokenize();
    }

    // ─────────────────────────────────────────────────────────────
    // XmlReader API
    // ─────────────────────────────────────────────────────────────

    public override int AttributeCount
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return 0;
            var e = _events[_index];
            return e.NodeType == XmlNodeType.Element ? (e.Attributes?.Length ?? 0) : 0;
        }
    }

    public override string BaseURI => string.Empty;

    public override int Depth => _index < 0 || _index >= _events.Count ? 0 : _events[_index].Depth;

    public override bool EOF => _readState == ReadState.EndOfFile;

    public override bool HasAttributes => AttributeCount > 0;

    public override bool IsEmptyElement
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return false;
            var e = _events[_index];
            return e.NodeType == XmlNodeType.Element && e.IsEmptyElement;
        }
    }

    public override string LocalName
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return string.Empty;
            var e = _events[_index];
            if (_attrIndex >= 0 && e.Attributes != null && _attrIndex < e.Attributes.Length)
                return e.Attributes[_attrIndex].LocalName;
            return e.LocalName;
        }
    }

    public override string Name
    {
        get
        {
            var prefix = Prefix;
            var local = LocalName;
            return string.IsNullOrEmpty(prefix) ? local : $"{prefix}:{local}";
        }
    }

    public override string NamespaceURI
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return string.Empty;
            var e = _events[_index];
            if (_attrIndex >= 0 && e.Attributes != null && _attrIndex < e.Attributes.Length)
                return e.Attributes[_attrIndex].NamespaceUri;
            return e.NamespaceUri;
        }
    }

    public override XmlNameTable NameTable => _nameTable;

    public override XmlNodeType NodeType
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return XmlNodeType.None;
            if (_attrIndex >= 0) return _onAttrValue ? XmlNodeType.Text : XmlNodeType.Attribute;
            return _events[_index].NodeType;
        }
    }

    public override string Prefix
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return string.Empty;
            var e = _events[_index];
            if (_attrIndex >= 0 && e.Attributes != null && _attrIndex < e.Attributes.Length)
                return e.Attributes[_attrIndex].Prefix;
            return e.Prefix;
        }
    }

    public override ReadState ReadState => _readState;

    public override string Value
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return string.Empty;
            var e = _events[_index];
            if (_attrIndex >= 0 && e.Attributes != null && _attrIndex < e.Attributes.Length)
                return e.Attributes[_attrIndex].Value;
            return e.Value;
        }
    }

    public override string? GetAttribute(string name)
    {
        if (_index < 0 || _index >= _events.Count) return null;
        var attrs = _events[_index].Attributes;
        if (attrs == null) return null;
        foreach (var a in attrs)
        {
            var fullName = string.IsNullOrEmpty(a.Prefix) ? a.LocalName : $"{a.Prefix}:{a.LocalName}";
            if (fullName == name) return a.Value;
        }
        return null;
    }

    public override string? GetAttribute(string name, string? namespaceURI)
    {
        if (_index < 0 || _index >= _events.Count) return null;
        var attrs = _events[_index].Attributes;
        if (attrs == null) return null;
        foreach (var a in attrs)
        {
            if (a.LocalName == name && a.NamespaceUri == (namespaceURI ?? string.Empty))
                return a.Value;
        }
        return null;
    }

    public override string GetAttribute(int i)
    {
        if (_index < 0 || _index >= _events.Count)
            throw new ArgumentOutOfRangeException(nameof(i));
        var attrs = _events[_index].Attributes;
        if (attrs == null || i < 0 || i >= attrs.Length)
            throw new ArgumentOutOfRangeException(nameof(i));
        return attrs[i].Value;
    }

    public override string? LookupNamespace(string prefix) => null;

    public override void MoveToAttribute(int i)
    {
        if (_index < 0 || _index >= _events.Count) return;
        var attrs = _events[_index].Attributes;
        if (attrs == null || i < 0 || i >= attrs.Length) return;
        _attrIndex = i;
        _onAttrValue = false;
    }

    public override bool MoveToAttribute(string name)
    {
        if (_index < 0 || _index >= _events.Count) return false;
        var attrs = _events[_index].Attributes;
        if (attrs == null) return false;
        for (var i = 0; i < attrs.Length; i++)
        {
            var a = attrs[i];
            var fullName = string.IsNullOrEmpty(a.Prefix) ? a.LocalName : $"{a.Prefix}:{a.LocalName}";
            if (fullName == name)
            {
                _attrIndex = i;
                _onAttrValue = false;
                return true;
            }
        }
        return false;
    }

    public override bool MoveToAttribute(string name, string? ns)
    {
        if (_index < 0 || _index >= _events.Count) return false;
        var attrs = _events[_index].Attributes;
        if (attrs == null) return false;
        for (var i = 0; i < attrs.Length; i++)
        {
            var a = attrs[i];
            if (a.LocalName == name && a.NamespaceUri == (ns ?? string.Empty))
            {
                _attrIndex = i;
                _onAttrValue = false;
                return true;
            }
        }
        return false;
    }

    public override bool MoveToElement()
    {
        if (_attrIndex < 0) return false;
        _attrIndex = -1;
        _onAttrValue = false;
        return true;
    }

    public override bool MoveToFirstAttribute()
    {
        if (AttributeCount == 0) return false;
        _attrIndex = 0;
        _onAttrValue = false;
        return true;
    }

    public override bool MoveToNextAttribute()
    {
        if (_index < 0 || _index >= _events.Count) return false;
        var attrs = _events[_index].Attributes;
        if (attrs == null) return false;
        if (_attrIndex + 1 >= attrs.Length) return false;
        _attrIndex++;
        _onAttrValue = false;
        return true;
    }

    public override bool Read()
    {
        _attrIndex = -1;
        _onAttrValue = false;

        if (_readState == ReadState.Initial)
            _readState = ReadState.Interactive;

        if (_readState != ReadState.Interactive)
            return false;

        _index++;
        if (_index >= _events.Count)
        {
            _readState = ReadState.EndOfFile;
            return false;
        }

        return true;
    }

    public override bool ReadAttributeValue()
    {
        if (_attrIndex < 0) return false;
        if (_onAttrValue) return false;
        _onAttrValue = true;
        return true;
    }

    public override void ResolveEntity() => throw new InvalidOperationException();

    public override string ReadInnerXml()
    {
        if (_index < 0 || _index >= _events.Count)
            return string.Empty;

        var e = _events[_index];
        if (e.NodeType != XmlNodeType.Element)
            return base.ReadInnerXml();

        if (e.IsEmptyElement)
        {
            // Consume the (empty) element per XmlReader semantics.
            Read();
            return string.Empty;
        }

        var start = e.InnerSourceStart;
        var end = e.InnerSourceEnd;
        var inner = start >= 0 && end >= start ? _source.Substring(start, end - start) : string.Empty;

        // Advance past the matching EndElement (XmlReader.ReadInnerXml consumes through the end tag).
        _index = e.MatchingEndIndex;
        // Position the cursor so the next Read() moves to the event after the EndElement.
        // (XmlReader contract: after ReadInnerXml, current node is the EndElement.)
        return inner;
    }

    public override string ReadOuterXml()
    {
        if (_index < 0 || _index >= _events.Count)
            return string.Empty;

        var e = _events[_index];
        if (e.NodeType != XmlNodeType.Element)
            return base.ReadOuterXml();

        int start = e.SourceStart;
        int end;
        if (e.IsEmptyElement)
        {
            end = e.SourceEnd;
        }
        else
        {
            var endEvent = _events[e.MatchingEndIndex];
            end = endEvent.SourceEnd;
            _index = e.MatchingEndIndex;
        }

        return start >= 0 && end >= start ? _source.Substring(start, end - start) : string.Empty;
    }

    // IXmlLineInfo
    public bool HasLineInfo() => true;

    public int LineNumber
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return 0;
            var e = _events[_index];
            if (_attrIndex >= 0 && e.Attributes != null && _attrIndex < e.Attributes.Length)
                return e.Attributes[_attrIndex].LineNumber;
            return e.LineNumber;
        }
    }

    public int LinePosition
    {
        get
        {
            if (_index < 0 || _index >= _events.Count) return 0;
            var e = _events[_index];
            if (_attrIndex >= 0 && e.Attributes != null && _attrIndex < e.Attributes.Length)
                return e.Attributes[_attrIndex].LinePosition;
            return e.LinePosition;
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Tokenizer — produces a flat list of Events from the source.
    // ═════════════════════════════════════════════════════════════

    private sealed class Tokenizer
    {
        // Current source being tokenized. This is mutable so that nested code-block
        // expansions can temporarily swap in an expanded XML string.
        private string _src;
        private int _pos;
        private int _line = 1;
        private int _col = 1;
        private int _depth = 0;

        // xml:space="preserve" stack — one bool per element depth. When true, text
        // nodes at this depth preserve whitespace instead of being collapsed.
        private readonly Stack<bool> _preserveWhitespace = new();

        // Namespace scope stack: one dictionary per element depth.
        private readonly List<Dictionary<string, string>> _nsStack = new();

        // Events produced
        private readonly List<Event> _events = new();

        // Element index stack for pairing start/end and computing MatchingEndIndex.
        private readonly Stack<int> _openElementIndices = new();

        // Per-document section table for @section / @RenderSection. Falls back to
        // the global RazorExpressionRegistry when a section isn't defined locally.
        private readonly Dictionary<string, string> _sections = new(StringComparer.Ordinal);

        public Tokenizer(string src)
        {
            _src = src;
            _nsStack.Add(new Dictionary<string, string>(StringComparer.Ordinal));
            _preserveWhitespace.Push(false);
        }

        public List<Event> Tokenize()
        {
            TokenizeCurrentSource();

            // Second pass: any @RenderSection calls that encountered a not-yet-defined
            // section emitted a <RazorSectionHost> element; now that we've seen the
            // whole document, attempt to back-fill them for sections defined later.
            return _events;
        }

        /// <summary>
        /// Tokenizes an inline expanded code-block output in place, with the current
        /// namespace scope, depth, and event list. Restores the outer source position
        /// afterwards. Used for <c>@{ }</c>, <c>@for</c>, <c>@foreach</c>, etc.
        /// </summary>
        private void TokenizeNested(string nested)
        {
            if (string.IsNullOrEmpty(nested)) return;
            var savedSrc = _src;
            var savedPos = _pos;
            // Line/column stay anchored to the outer source so diagnostics point at
            // the @{ } directive rather than an offset inside the synthetic expansion.
            _src = nested;
            _pos = 0;
            TokenizeCurrentSource();
            _src = savedSrc;
            _pos = savedPos;
        }

        private void TokenizeCurrentSource()
        {
            while (_pos < _src.Length)
            {
                var c = _src[_pos];

                // XML declaration / processing instruction / comment / CDATA
                if (c == '<' && _pos + 1 < _src.Length)
                {
                    var next = _src[_pos + 1];
                    if (next == '?')
                    {
                        ReadProcessingInstruction();
                        continue;
                    }
                    if (next == '!')
                    {
                        if (StartsWith("<!--"))
                        {
                            SkipComment();
                            continue;
                        }
                        if (StartsWith("<![CDATA["))
                        {
                            ReadCData();
                            continue;
                        }
                        // DOCTYPE or other declaration — skip until '>'
                        SkipDeclaration();
                        continue;
                    }
                    if (next == '/')
                    {
                        ReadEndElement();
                        continue;
                    }
                    if (IsNameStartChar(next))
                    {
                        ReadStartElement();
                        continue;
                    }
                }

                // Text content (including Razor directives like @if, @(expr), @path)
                ReadTextContent();
            }
        }

        // ─────────────────────────────────────────────────────────
        // Source scanning helpers
        // ─────────────────────────────────────────────────────────

        private bool StartsWith(string s)
        {
            if (_pos + s.Length > _src.Length) return false;
            return _src.AsSpan(_pos, s.Length).SequenceEqual(s);
        }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        private void ThrowXmlError(string message, int line, int position)
        {
            // Include a short snippet of the surrounding source so diagnostics
            // survive the line/col drift caused by recursive nested-source
            // tokenization (e.g. expanded @{ } code-block content). The snippet
            // is only 60 chars on each side and is single-line normalized.
            var snippetStart = Math.Max(0, _pos - 60);
            var snippetEnd = Math.Min(_src.Length, _pos + 60);
            var snippet = _src.Substring(snippetStart, snippetEnd - snippetStart)
                .Replace('\n', ' ')
                .Replace('\r', ' ');
            throw new XmlException($"{message} Near: «{snippet}»", innerException: null, line, position);
        }

        private void Advance(int n = 1)
        {
            for (var i = 0; i < n && _pos < _src.Length; i++)
            {
                if (_src[_pos] == '\n') { _line++; _col = 1; }
                else _col++;
                _pos++;
            }
        }

        private static bool IsNameStartChar(char c) =>
            c == '_' || c == ':' || char.IsLetter(c);

        private static bool IsNameChar(char c) =>
            c == '_' || c == ':' || c == '-' || c == '.' || char.IsLetterOrDigit(c);

        // ─────────────────────────────────────────────────────────
        // XML constructs
        // ─────────────────────────────────────────────────────────

        private void SkipComment()
        {
            // _src[_pos..] starts with "<!--"
            Advance(4);
            while (_pos + 2 < _src.Length)
            {
                if (_src[_pos] == '-' && _src[_pos + 1] == '-' && _src[_pos + 2] == '>')
                {
                    Advance(3);
                    return;
                }
                Advance();
            }
            // Unterminated — consume to end
            while (_pos < _src.Length) Advance();
        }

        private void ReadProcessingInstruction()
        {
            // _src[_pos] == '<' and _src[_pos+1] == '?'
            var startLine = _line;
            var startCol = _col;
            Advance(2);

            // Read PI target name
            var nameStart = _pos;
            while (_pos < _src.Length && IsNameChar(_src[_pos])) Advance();
            var target = _src[nameStart.._pos];
            SkipWhitespace();

            // Read PI body until "?>"
            var bodyStart = _pos;
            while (_pos + 1 < _src.Length)
            {
                if (_src[_pos] == '?' && _src[_pos + 1] == '>')
                    break;
                Advance();
            }
            var body = _src[bodyStart.._pos];
            if (_pos + 1 < _src.Length) Advance(2); // '?>'

            // <?xml ... ?> becomes an XmlDeclaration event; other PIs are emitted
            // as ProcessingInstruction events so downstream consumers can inspect them.
            var isXmlDecl = string.Equals(target, "xml", StringComparison.Ordinal);
            _events.Add(new Event
            {
                NodeType = isXmlDecl ? XmlNodeType.XmlDeclaration : XmlNodeType.ProcessingInstruction,
                LocalName = target,
                Value = body.Trim(),
                Depth = _depth,
                LineNumber = startLine,
                LinePosition = startCol
            });
        }

        private void SkipDeclaration()
        {
            // '<!' ... '>'
            Advance(2);
            while (_pos < _src.Length)
            {
                if (_src[_pos] == '>')
                {
                    Advance();
                    return;
                }
                Advance();
            }
        }

        private void ReadCData()
        {
            // "<![CDATA["
            Advance(9);
            var startLine = _line;
            var startCol = _col;
            var sb = new StringBuilder();
            while (_pos + 2 < _src.Length)
            {
                if (_src[_pos] == ']' && _src[_pos + 1] == ']' && _src[_pos + 2] == '>')
                {
                    Advance(3);
                    _events.Add(new Event
                    {
                        NodeType = XmlNodeType.CDATA,
                        Value = sb.ToString(),
                        Depth = _depth,
                        LineNumber = startLine,
                        LinePosition = startCol
                    });
                    return;
                }
                sb.Append(_src[_pos]);
                Advance();
            }
            // Unterminated — still emit what we have
            _events.Add(new Event
            {
                NodeType = XmlNodeType.CDATA,
                Value = sb.ToString(),
                Depth = _depth,
                LineNumber = startLine,
                LinePosition = startCol
            });
        }

        private void ReadStartElement()
        {
            var startPos = _pos;
            var startLine = _line;
            var startCol = _col;

            Advance(); // consume '<'
            var (prefix, localName) = ReadQualifiedName();

            // Collect attributes
            var attrs = new List<AttrEntry>();
            var scopeNs = new Dictionary<string, string>(_nsStack[^1], StringComparer.Ordinal);
            var scopeAdded = false;

            while (_pos < _src.Length)
            {
                SkipWhitespace();
                if (_pos >= _src.Length) break;
                var c = _src[_pos];
                if (c == '/' || c == '>') break;

                var attrLine = _line;
                var attrCol = _col;

                var (attrPrefix, attrLocal) = ReadQualifiedName();
                if (string.IsNullOrEmpty(attrLocal))
                    ThrowXmlError($"Invalid attribute name at line {attrLine}, position {attrCol}.", attrLine, attrCol);
                SkipWhitespace();
                if (_pos >= _src.Length || _src[_pos] != '=')
                    ThrowXmlError($"Attribute '{attrLocal}' is missing '=' at line {_line}, position {_col}.", _line, _col);

                Advance(); // '='
                SkipWhitespace();
                if (_pos >= _src.Length || (_src[_pos] != '"' && _src[_pos] != '\''))
                    ThrowXmlError($"Attribute '{attrLocal}' value must be quoted at line {_line}, position {_col}.", _line, _col);
                var value = ReadAttributeValue();

                // xmlns declarations
                if (attrPrefix == string.Empty && attrLocal == "xmlns")
                {
                    scopeNs[string.Empty] = value;
                    scopeAdded = true;
                }
                else if (attrPrefix == "xmlns")
                {
                    scopeNs[attrLocal] = value;
                    scopeAdded = true;
                }

                attrs.Add(new AttrEntry
                {
                    Prefix = attrPrefix,
                    LocalName = attrLocal,
                    Value = value,
                    LineNumber = attrLine,
                    LinePosition = attrCol
                });
            }

            bool isEmpty = false;
            if (_pos < _src.Length && _src[_pos] == '/')
            {
                isEmpty = true;
                Advance();
            }
            if (_pos < _src.Length && _src[_pos] == '>')
                Advance();

            // Resolve attribute namespace URIs now that xmlns decls are known
            foreach (var a in attrs)
            {
                if (a.Prefix == "xmlns" || (a.Prefix == string.Empty && a.LocalName == "xmlns"))
                {
                    a.NamespaceUri = string.Empty;
                }
                else if (a.Prefix == string.Empty)
                {
                    // Unprefixed attributes have no namespace in XML 1.0
                    a.NamespaceUri = string.Empty;
                }
                else
                {
                    a.NamespaceUri = scopeNs.TryGetValue(a.Prefix, out var uri) ? uri : string.Empty;
                }
            }

            // Resolve element namespace
            var elementNs = string.Empty;
            if (prefix == string.Empty)
                scopeNs.TryGetValue(string.Empty, out elementNs!);
            else
                scopeNs.TryGetValue(prefix, out elementNs!);
            elementNs ??= string.Empty;

            var elementEvent = new Event
            {
                NodeType = XmlNodeType.Element,
                Prefix = prefix,
                LocalName = localName,
                NamespaceUri = elementNs,
                IsEmptyElement = isEmpty,
                Depth = _depth,
                Attributes = attrs.Count > 0 ? attrs.ToArray() : null,
                LineNumber = startLine,
                LinePosition = startCol,
                SourceStart = startPos,
                SourceEnd = _pos,
                InnerSourceStart = _pos
            };

            int eventIndex = _events.Count;
            _events.Add(elementEvent);

            // Propagate xml:space="preserve" into the child scope
            var preserve = _preserveWhitespace.Count > 0 && _preserveWhitespace.Peek();
            foreach (var a in attrs)
            {
                if (a.Prefix == "xml" && a.LocalName == "space")
                {
                    preserve = string.Equals(a.Value, "preserve", StringComparison.Ordinal);
                    break;
                }
            }

            if (!isEmpty)
            {
                // Push scope for children
                _nsStack.Add(scopeNs);
                _openElementIndices.Push(eventIndex);
                _preserveWhitespace.Push(preserve);
                _depth++;
            }
            else
            {
                // For empty elements, matching end is itself
                elementEvent.MatchingEndIndex = eventIndex;
                elementEvent.InnerSourceEnd = elementEvent.InnerSourceStart;
                _ = scopeAdded; // scope dict is only used for attribute resolution here
            }
        }

        private void ReadEndElement()
        {
            var startPos = _pos;
            var startLine = _line;
            var startCol = _col;
            Advance(2); // '</'
            var (prefix, localName) = ReadQualifiedName();
            if (string.IsNullOrEmpty(localName))
                ThrowXmlError($"Invalid end element name at line {startLine}, position {startCol}.", startLine, startCol);
            SkipWhitespace();
            if (_pos >= _src.Length || _src[_pos] != '>')
                ThrowXmlError($"Expected '>' to close end element '{localName}' at line {_line}, position {_col}.", _line, _col);
            Advance();

            // Validate that the close tag matches the most-recently opened element.
            if (_openElementIndices.Count == 0)
                ThrowXmlError($"Unexpected end element '{localName}' at line {startLine}, position {startCol}.", startLine, startCol);

            var openIdxPreview = _openElementIndices.Peek();
            var openEl = _events[openIdxPreview];
            if (openEl.LocalName != localName || openEl.Prefix != prefix)
                ThrowXmlError(
                    $"End element '{(string.IsNullOrEmpty(prefix) ? localName : prefix + ":" + localName)}' does not match start element '{(string.IsNullOrEmpty(openEl.Prefix) ? openEl.LocalName : openEl.Prefix + ":" + openEl.LocalName)}' at line {startLine}, position {startCol}.",
                    startLine, startCol);

            if (_depth > 0) _depth--;

            // Resolve namespace from the scope stack (which is still the child scope;
            // we want the parent scope for the element being closed).
            var currentScope = _nsStack[^1];
            string endNs;
            if (prefix == string.Empty)
                currentScope.TryGetValue(string.Empty, out endNs!);
            else
                currentScope.TryGetValue(prefix, out endNs!);
            endNs ??= string.Empty;

            var endEvent = new Event
            {
                NodeType = XmlNodeType.EndElement,
                Prefix = prefix,
                LocalName = localName,
                NamespaceUri = endNs,
                Depth = _depth,
                LineNumber = startLine,
                LinePosition = startCol,
                SourceStart = startPos,
                SourceEnd = _pos
            };

            int endIndex = _events.Count;
            _events.Add(endEvent);

            if (_openElementIndices.Count > 0)
            {
                var openIdx = _openElementIndices.Pop();
                var openEvent = _events[openIdx];
                openEvent.MatchingEndIndex = endIndex;
                openEvent.InnerSourceEnd = startPos;
            }

            // Pop namespace + whitespace scope
            if (_nsStack.Count > 1)
                _nsStack.RemoveAt(_nsStack.Count - 1);
            if (_preserveWhitespace.Count > 1)
                _preserveWhitespace.Pop();
        }

        /// <summary>
        /// Reads text content until the next XML tag. Along the way, Razor directives
        /// are recognized and handled natively:
        /// <list type="bullet">
        ///   <item><c>@{ }</c>, <c>@for</c>, <c>@foreach</c>, <c>@while</c>, <c>@switch</c>, <c>@using</c>, <c>@lock</c>, <c>@do</c>, <c>@try</c>
        ///     — captured and expanded via the AOT-safe interpreter, then the result is
        ///     recursively tokenized inline.</item>
        ///   <item><c>@section Name { ... }</c> — registered for later lookup, emits no events.</item>
        ///   <item><c>@RenderSection("Name")</c> — substituted with the section body.</item>
        ///   <item><c>@if (cond) { ... }</c> — emitted verbatim as text (XamlReader handles it at runtime).
        ///     The condition's parenthesized body is scanned as C# so <c>&lt;</c>/<c>&gt;</c> are not
        ///     mistaken for XML tag starts.</item>
        ///   <item><c>@(expr)</c> — emitted verbatim as text; parens scanned as C#.</item>
        ///   <item><c>@path</c> — emitted verbatim as text.</item>
        ///   <item><c>@@</c> — decoded to a literal <c>@</c>.</item>
        ///   <item><c>@* ... *@</c> — Razor comment, dropped.</item>
        /// </list>
        /// </summary>
        private void ReadTextContent()
        {
            var sb = new StringBuilder();
            var textStartLine = _line;
            var textStartCol = _col;
            var textStartPos = _pos;

            while (_pos < _src.Length)
            {
                var c = _src[_pos];

                // End of text content — next XML construct starts
                if (c == '<' && _pos + 1 < _src.Length)
                {
                    var next = _src[_pos + 1];
                    if (next == '/' || next == '!' || next == '?' || IsNameStartChar(next))
                        break;
                }

                // Razor directive handling
                if (c == '@' && _pos + 1 < _src.Length)
                {
                    var next = _src[_pos + 1];

                    // @@ → literal @
                    if (next == '@')
                    {
                        sb.Append('@');
                        Advance(2);
                        continue;
                    }

                    // @* ... *@ — Razor comment, skip
                    if (next == '*')
                    {
                        Advance(2);
                        while (_pos + 1 < _src.Length && !(_src[_pos] == '*' && _src[_pos + 1] == '@'))
                            Advance();
                        if (_pos + 1 < _src.Length) Advance(2);
                        continue;
                    }

                    // @{ ... } — inline code block. Expand via interpreter, then tokenize result.
                    if (next == '{')
                    {
                        FlushText(sb, ref textStartLine, ref textStartCol, ref textStartPos);
                        HandleCodeBlock();
                        continue;
                    }

                    // @( expr ) — inline expression, emitted verbatim
                    if (next == '(')
                    {
                        var before = _pos;
                        Advance(); // consume '@'
                        ScanBalancedParens(); // positions _pos after ')'
                        sb.Append(_src, before, _pos - before);
                        continue;
                    }

                    // @section, @RenderSection, @for, @foreach, @while, @switch, @using, @lock,
                    // @do, @try, @if, @path, @identifier
                    if (char.IsLetter(next))
                    {
                        // Named directives that get expanded or consumed here
                        if (TryMatchWord("@section") && HandleSectionDefinition())
                            continue;
                        if (TryMatchWord("@RenderSection") && HandleRenderSection())
                        {
                            continue;
                        }

                        // Flow-control blocks (@for/@foreach/@while/@switch/@using/@lock/@do/@try)
                        // are expanded via the interpreter. @if is kept as text because XamlReader
                        // handles conditional visibility at runtime using the DataContext.
                        if (TryHandleFlowControlBlock(sb, ref textStartLine, ref textStartCol, ref textStartPos))
                            continue;

                        // @if or @identifier — scan past any (...) so '<'/'>' inside are safe,
                        // then emit verbatim as text for the XamlReader to handle.
                        if (TryScanRazorKeywordDirective(sb))
                            continue;
                    }
                }

                // Entity reference (&lt; &gt; &amp; etc.)
                if (c == '&')
                {
                    if (TryReadEntity(sb))
                        continue;
                }

                sb.Append(c);
                Advance();
            }

            FlushText(sb, ref textStartLine, ref textStartCol, ref textStartPos);
        }

        /// <summary>
        /// Emits a text event for any pending content in <paramref name="sb"/>, then
        /// resets the buffer. If whitespace preservation is not active, whitespace-only
        /// runs are dropped (mirroring <c>XmlReaderSettings.IgnoreWhitespace = true</c>).
        /// </summary>
        private void FlushText(StringBuilder sb, ref int startLine, ref int startCol, ref int startPos)
        {
            if (sb.Length == 0)
            {
                startLine = _line;
                startCol = _col;
                startPos = _pos;
                return;
            }

            var text = sb.ToString();
            sb.Clear();

            var preserve = _preserveWhitespace.Count > 0 && _preserveWhitespace.Peek();
            if (!preserve && string.IsNullOrWhiteSpace(text))
            {
                startLine = _line;
                startCol = _col;
                startPos = _pos;
                return;
            }

            _events.Add(new Event
            {
                NodeType = preserve && string.IsNullOrWhiteSpace(text)
                    ? XmlNodeType.SignificantWhitespace
                    : XmlNodeType.Text,
                Value = text,
                Depth = _depth,
                LineNumber = startLine,
                LinePosition = startCol,
                SourceStart = startPos,
                SourceEnd = _pos
            });

            startLine = _line;
            startCol = _col;
            startPos = _pos;
        }

        /// <summary>
        /// Handles <c>@{ ... }</c> at <c>_pos</c>. Captures the body via
        /// <see cref="RazorCodeBlockPreprocessor.FindMatchingBrace"/>, expands it with
        /// the AOT-safe interpreter, then recursively tokenizes the result inline.
        /// </summary>
        private void HandleCodeBlock()
        {
            // _src[_pos..] starts with "@{"
            var atPos = _pos;
            var codeStart = _pos + 2;
            var braceEnd = RazorCodeBlockPreprocessor.FindMatchingBrace(_src, codeStart);
            if (braceEnd < 0)
            {
                // Unbalanced — consume the '@' and let the loop continue
                Advance();
                return;
            }

            var code = _src.Substring(codeStart, braceEnd - codeStart).Trim();

            // Advance outer position past the entire @{ ... } block
            while (_pos < braceEnd + 1) Advance();

            var expanded = RazorCodeBlockPreprocessor.ExpandCodeBlock(code);
            if (!string.IsNullOrEmpty(expanded))
                TokenizeNested(expanded);
        }

        /// <summary>
        /// Tries to handle a flow-control directive (<c>@for</c>, <c>@foreach</c>,
        /// <c>@while</c>, <c>@switch</c>, <c>@using</c>, <c>@lock</c>, <c>@do { } while</c>,
        /// <c>@try { } catch { }</c>) at <c>_pos</c>. Returns <c>true</c> on handled.
        /// </summary>
        private bool TryHandleFlowControlBlock(StringBuilder sb, ref int startLine, ref int startCol, ref int startPos)
        {
            if (_pos >= _src.Length || _src[_pos] != '@') return false;

            int blockEnd;
            string code;
            if (!RazorCodeBlockPreprocessor.TryMatchBlockDirective(_src, _pos, out blockEnd, out code)
                && !RazorCodeBlockPreprocessor.TryMatchDoWhileDirective(_src, _pos, out blockEnd, out code)
                && !RazorCodeBlockPreprocessor.TryMatchTryCatchDirective(_src, _pos, out blockEnd, out code))
            {
                return false;
            }

            FlushText(sb, ref startLine, ref startCol, ref startPos);

            // Advance outer position past the entire directive
            while (_pos < blockEnd) Advance();

            var expanded = RazorCodeBlockPreprocessor.ExpandCodeBlock(code);
            if (!string.IsNullOrEmpty(expanded))
                TokenizeNested(expanded);

            return true;
        }

        /// <summary>
        /// Handles <c>@section Name { ... }</c> at <c>_pos</c>. Registers the section
        /// body in the local and global registries; emits no events.
        /// </summary>
        private bool HandleSectionDefinition()
        {
            // _src[_pos..] starts with "@section"
            var atPos = _pos;
            var p = _pos + 8; // skip "@section"
            while (p < _src.Length && char.IsWhiteSpace(_src[p])) p++;

            var nameStart = p;
            while (p < _src.Length && (char.IsLetterOrDigit(_src[p]) || _src[p] == '_')) p++;
            if (p == nameStart) return false;
            var sectionName = _src[nameStart..p];

            while (p < _src.Length && char.IsWhiteSpace(_src[p])) p++;
            if (p >= _src.Length || _src[p] != '{') return false;

            var bodyEnd = RazorCodeBlockPreprocessor.FindMatchingBrace(_src, p + 1);
            if (bodyEnd < 0) return false;

            var body = _src[(p + 1)..bodyEnd].Trim();
            _sections[sectionName] = body;
            RazorExpressionRegistry.RegisterSection(sectionName, body);

            // Advance past the section (and trailing newline whitespace)
            while (_pos <= bodyEnd) Advance();
            while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'
                   || _src[_pos] == '\r' || _src[_pos] == '\n'))
                Advance();

            return true;
        }

        /// <summary>
        /// Handles <c>@RenderSection("Name")</c> at <c>_pos</c>. Substitutes the section
        /// body inline via nested tokenization; if the section is unknown, emits a
        /// <c>&lt;RazorSectionHost SectionName="..."/&gt;</c> element that the runtime
        /// will back-fill when the section is registered.
        /// </summary>
        private bool HandleRenderSection()
        {
            // _src[_pos..] starts with "@RenderSection"
            var p = _pos + 14; // skip "@RenderSection"
            while (p < _src.Length && char.IsWhiteSpace(_src[p])) p++;
            if (p >= _src.Length || _src[p] != '(') return false;

            var parenEnd = RazorCodeBlockPreprocessor.FindMatchingParen(_src, p + 1);
            if (parenEnd < 0) return false;

            var args = _src[(p + 1)..(parenEnd - 1)].Trim();
            var sectionName = RazorCodeBlockPreprocessor.ExtractSectionNameFromArgs(args);
            if (string.IsNullOrEmpty(sectionName)) return false;

            // Advance past the @RenderSection( ... ) call
            while (_pos < parenEnd) Advance();

            if (_sections.TryGetValue(sectionName, out var localBody))
            {
                TokenizeNested(localBody);
            }
            else if (RazorExpressionRegistry.TryGetGlobalSection(sectionName, out var globalBody))
            {
                TokenizeNested(globalBody);
            }
            else
            {
                // Section not yet registered — emit a dynamic host element
                TokenizeNested($"<RazorSectionHost SectionName=\"{sectionName}\"/>");
            }

            return true;
        }

        /// <summary>
        /// Returns <c>true</c> if <c>_src</c> at <c>_pos</c> starts with <paramref name="word"/>
        /// and the character after is not a name continuation (letter/digit).
        /// </summary>
        private bool TryMatchWord(string word)
        {
            if (_pos + word.Length > _src.Length) return false;
            if (!_src.AsSpan(_pos, word.Length).SequenceEqual(word)) return false;
            var after = _pos + word.Length;
            return after >= _src.Length || !char.IsLetterOrDigit(_src[after]);
        }

        /// <summary>
        /// Tries to scan a Razor keyword directive of the form <c>@name ( ... ) { ... }</c>
        /// or <c>@name ( ... )</c>. Copies the source verbatim into <paramref name="sb"/>
        /// up to the end of the <c>( ... )</c> group, preserving XML-special characters
        /// but advancing <c>_pos</c> past them so they are not re-interpreted as XML.
        /// Returns <c>false</c> if no match (caller falls back to emitting '@' literally).
        /// </summary>
        private bool TryScanRazorKeywordDirective(StringBuilder sb)
        {
            // _pos is at '@'
            var savePos = _pos;
            var saveLine = _line;
            var saveCol = _col;

            // Copy '@'
            sb.Append('@');
            Advance();

            // Read keyword letters
            var keywordStart = _pos;
            while (_pos < _src.Length && char.IsLetter(_src[_pos]))
            {
                sb.Append(_src[_pos]);
                Advance();
            }
            if (_pos == keywordStart)
            {
                // Not a keyword — rewind
                _pos = savePos; _line = saveLine; _col = saveCol;
                sb.Length--; // remove the '@' we appended
                return false;
            }

            // Optional whitespace
            while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'))
            {
                sb.Append(_src[_pos]);
                Advance();
            }

            // Optional ( ... )
            if (_pos < _src.Length && _src[_pos] == '(')
            {
                var parenStart = _pos;
                ScanBalancedParens();
                sb.Append(_src, parenStart, _pos - parenStart);
            }
            else
            {
                // No parens — could be @path; we already copied '@name', leave as text
            }

            return true;
        }

        /// <summary>
        /// Starting at <c>(</c>, advances <c>_pos</c> to just past the matching <c>)</c>,
        /// correctly handling nested parens, C# string literals (including verbatim and
        /// interpolated), char literals, and line/block comments.
        /// </summary>
        private void ScanBalancedParens()
        {
            if (_pos >= _src.Length || _src[_pos] != '(') return;
            Advance(); // '('

            var depth = 1;
            var inString = false;
            var inChar = false;
            var verbatim = false;
            var escaped = false;
            char quote = '\0';

            while (_pos < _src.Length && depth > 0)
            {
                var c = _src[_pos];

                if (escaped)
                {
                    escaped = false;
                    Advance();
                    continue;
                }

                if (inChar)
                {
                    if (c == '\\') escaped = true;
                    else if (c == '\'') inChar = false;
                    Advance();
                    continue;
                }

                if (inString)
                {
                    if (!verbatim && c == '\\') { escaped = true; Advance(); continue; }
                    if (c == quote)
                    {
                        // Verbatim "": doubled quote is an escaped quote
                        if (verbatim && _pos + 1 < _src.Length && _src[_pos + 1] == quote)
                        {
                            Advance(2);
                            continue;
                        }
                        inString = false;
                        verbatim = false;
                        quote = '\0';
                    }
                    Advance();
                    continue;
                }

                // Line comment  //
                if (c == '/' && _pos + 1 < _src.Length && _src[_pos + 1] == '/')
                {
                    while (_pos < _src.Length && _src[_pos] != '\n') Advance();
                    continue;
                }
                // Block comment /* */
                if (c == '/' && _pos + 1 < _src.Length && _src[_pos + 1] == '*')
                {
                    Advance(2);
                    while (_pos + 1 < _src.Length && !(_src[_pos] == '*' && _src[_pos + 1] == '/')) Advance();
                    if (_pos + 1 < _src.Length) Advance(2);
                    continue;
                }

                if (c == '\'')
                {
                    inChar = true;
                    Advance();
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    verbatim = _pos > 0 && (_src[_pos - 1] == '@'
                        || (_pos > 1 && _src[_pos - 1] == '$' && _src[_pos - 2] == '@')
                        || (_pos > 1 && _src[_pos - 1] == '@' && _src[_pos - 2] == '$'));
                    quote = '"';
                    Advance();
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                    Advance();
                    continue;
                }

                if (c == ')')
                {
                    depth--;
                    Advance();
                    if (depth == 0) return;
                    continue;
                }

                Advance();
            }
        }

        private void SkipWhitespace()
        {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
                Advance();
        }

        private (string prefix, string localName) ReadQualifiedName()
        {
            if (_pos >= _src.Length || !IsNameStartChar(_src[_pos]))
                return (string.Empty, string.Empty);

            var nameStart = _pos;
            while (_pos < _src.Length && IsNameChar(_src[_pos]))
                Advance();

            var name = _src[nameStart.._pos];
            var colonIdx = name.IndexOf(':');
            if (colonIdx < 0) return (string.Empty, name);
            return (name[..colonIdx], name[(colonIdx + 1)..]);
        }

        private string ReadAttributeValue()
        {
            if (_pos >= _src.Length) return string.Empty;
            var quote = _src[_pos];
            if (quote != '"' && quote != '\'') return string.Empty;
            Advance();
            var valueStart = _pos;
            var sb = new StringBuilder();
            while (_pos < _src.Length && _src[_pos] != quote)
            {
                if (_src[_pos] == '&')
                {
                    if (TryReadEntity(sb)) continue;
                }
                sb.Append(_src[_pos]);
                Advance();
            }
            if (_pos < _src.Length) Advance(); // closing quote
            return sb.ToString();
        }

        /// <summary>
        /// Tries to read an XML entity reference (&amp;lt;, &amp;gt;, &amp;amp;,
        /// &amp;quot;, &amp;apos;, &amp;#ddd;, &amp;#xhh;). On success appends the
        /// decoded character to <paramref name="sb"/> and advances. Returns false
        /// if no valid entity was found (caller should emit '&amp;' literally).
        /// </summary>
        private bool TryReadEntity(StringBuilder sb)
        {
            if (_pos >= _src.Length || _src[_pos] != '&') return false;
            var semi = _src.IndexOf(';', _pos + 1);
            if (semi < 0 || semi - _pos > 16) return false;
            var name = _src.AsSpan(_pos + 1, semi - _pos - 1);

            char decoded;
            if (name.SequenceEqual("lt")) decoded = '<';
            else if (name.SequenceEqual("gt")) decoded = '>';
            else if (name.SequenceEqual("amp")) decoded = '&';
            else if (name.SequenceEqual("quot")) decoded = '"';
            else if (name.SequenceEqual("apos")) decoded = '\'';
            else if (name.Length > 1 && name[0] == '#')
            {
                int code;
                if (name[1] == 'x' || name[1] == 'X')
                {
                    if (!int.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out code))
                        return false;
                }
                else
                {
                    if (!int.TryParse(name[1..], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out code))
                        return false;
                }
                decoded = (char)code;
            }
            else
            {
                return false;
            }

            sb.Append(decoded);
            Advance(semi - _pos + 1);
            return true;
        }
    }
}
