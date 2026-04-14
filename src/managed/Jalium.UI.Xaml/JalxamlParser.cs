using System.Xml;

namespace Jalium.UI.Markup;

/// <summary>
/// Front door for parsing JALXAML (Jalium XAML) content. Internally this delegates to
/// <see cref="JalxamlReader"/>, a fully native lexer/parser that does NOT depend on
/// <see cref="System.Xml"/> for tokenization. The only role of <c>System.Xml</c> in
/// the wider stack is the <see cref="XmlReader"/> abstract base class that
/// <c>JalxamlReader</c> extends to stay API-compatible with existing consumers.
/// <para>
/// <see cref="JalxamlReader"/> understands Razor-XAML hybrid syntax natively:
/// <list type="bullet">
///   <item><c>@if (cond) { ... }</c> — C# operators like <c>&lt;</c>/<c>&gt;</c> inside <c>cond</c> are NOT tag starts</item>
///   <item><c>@(expression)</c> — inline expressions with any C# content</item>
///   <item><c>@path</c> — simple path expressions</item>
///   <item><c>@@</c> — literal <c>@</c></item>
///   <item><c>@* ... *@</c> — Razor comments</item>
///   <item><c>@{ ... }</c>, <c>@for</c>, <c>@foreach</c>, <c>@while</c>, <c>@switch</c>, etc. — pre-expanded via the existing interpreter</item>
/// </list>
/// </para>
/// </summary>
internal static class JalxamlParser
{
    /// <summary>
    /// Creates a reader that parses the given jalxaml content natively.
    /// </summary>
    public static XmlReader CreateReader(string jalxaml) => JalxamlReader.Create(jalxaml);

    /// <summary>
    /// Creates a reader from a <see cref="TextReader"/>.
    /// </summary>
    public static XmlReader CreateReader(TextReader textReader) => JalxamlReader.Create(textReader);

    /// <summary>
    /// Creates a reader from a <see cref="Stream"/>.
    /// </summary>
    public static XmlReader CreateReader(Stream stream) => JalxamlReader.Create(stream);
}
