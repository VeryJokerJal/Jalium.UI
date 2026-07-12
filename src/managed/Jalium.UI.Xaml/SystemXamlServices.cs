using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml;

namespace Jalium.UI.Xaml;

public static class XamlServices
{
    [RequiresUnreferencedCode("Runtime XAML loading resolves types and members from markup.")]
    public static object Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Jalium.UI.Markup.XamlReader.Load(stream);
    }

    [RequiresUnreferencedCode("Runtime XAML loading resolves types and members from markup.")]
    public static object Load(TextReader textReader)
    {
        ArgumentNullException.ThrowIfNull(textReader);
        return Jalium.UI.Markup.XamlReader.Load(textReader);
    }

    [RequiresUnreferencedCode("Runtime XAML loading resolves types and members from markup.")]
    public static object Load(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        using FileStream stream = File.OpenRead(fileName);
        return Load(stream);
    }

    [RequiresUnreferencedCode("Runtime XAML loading resolves types and members from markup.")]
    public static object Load(XmlReader xmlReader)
    {
        ArgumentNullException.ThrowIfNull(xmlReader);
        return Jalium.UI.Markup.XamlReader.Load(xmlReader);
    }

    [RequiresUnreferencedCode("Runtime XAML loading resolves types and members from markup.")]
    public static object Load(XamlReader xamlReader)
    {
        ArgumentNullException.ThrowIfNull(xamlReader);
        if (xamlReader is IXmlBackedXamlReader xmlBacked)
        {
            return Jalium.UI.Markup.XamlReader.Load(xmlBacked.XmlReader);
        }

        using var writer = new XamlObjectWriter(xamlReader.SchemaContext);
        Transform(xamlReader, writer, closeWriter: false);
        return writer.Result ?? throw new XamlObjectWriterException("The XAML node stream did not produce a root object.");
    }

    [RequiresUnreferencedCode("Runtime XAML loading resolves types and members from markup.")]
    public static object Parse(string xaml)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        return Jalium.UI.Markup.XamlReader.Parse(xaml);
    }

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(Stream stream, object instance)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(instance);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        XamlWriter.Save(instance, writer);
        writer.Flush();
    }

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(TextWriter writer, object instance)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instance);
        XamlWriter.Save(instance, writer);
    }

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static string Save(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return XamlWriter.Save(instance);
    }

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(string fileName, object instance)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(instance);
        using var writer = File.CreateText(fileName);
        Save(writer, instance);
    }

    public static void Save(XamlWriter writer, object instance)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instance);
        using var reader = new XamlObjectReader(instance, writer.SchemaContext);
        Transform(reader, writer, closeWriter: false);
    }

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(XmlWriter writer, object instance)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instance);
        writer.WriteRaw(XamlWriter.Save(instance));
    }

    public static void Transform(XamlReader xamlReader, XamlWriter xamlWriter)
        => Transform(xamlReader, xamlWriter, closeWriter: true);

    public static void Transform(XamlReader xamlReader, XamlWriter xamlWriter, bool closeWriter)
    {
        ArgumentNullException.ThrowIfNull(xamlReader);
        ArgumentNullException.ThrowIfNull(xamlWriter);
        if (!ReferenceEquals(xamlReader.SchemaContext, xamlWriter.SchemaContext)
            && xamlReader.SchemaContext.GetType() != xamlWriter.SchemaContext.GetType())
        {
            throw new XamlException("The reader and writer use incompatible XAML schema contexts.");
        }

        while (xamlReader.Read())
        {
            if (xamlWriter is IXamlLineInfoConsumer consumer && consumer.ShouldProvideLineInfo && xamlReader is IXamlLineInfo lineInfo && lineInfo.HasLineInfo)
            {
                consumer.SetLineInfo(lineInfo.LineNumber, lineInfo.LinePosition);
            }
            xamlWriter.WriteNode(xamlReader);
        }

        if (closeWriter) xamlWriter.Close();
    }
}
