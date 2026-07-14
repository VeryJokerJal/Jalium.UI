using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using Jalium.UI.Xaml;
using Jalium.UI.Xaml.Schema;

namespace Jalium.UI.Markup;

public abstract class MemberDefinition
{
    protected MemberDefinition() { }
    public abstract string? Name { get; set; }
}

public class PropertyDefinition : MemberDefinition
{
    public IList<Attribute> Attributes { get; } = new List<Attribute>();
    [DefaultValue(null)] public string? Modifier { get; set; }
    public override string? Name { get; set; }
    [TypeConverter(typeof(XamlTypeTypeConverter))] public XamlType? Type { get; set; }
}

[Jalium.UI.Markup.ContentProperty(nameof(Name))]
public class Reference : MarkupExtension
{
    public Reference() { }
    public Reference(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));

    [ConstructorArgument("name")]
    public string? Name { get; set; }

    [RequiresUnreferencedCode("The MarkupExtension contract permits services that resolve user supplied types.")]
    [RequiresDynamicCode("The MarkupExtension contract permits services that construct runtime supplied types.")]
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        if (string.IsNullOrEmpty(Name)) throw new InvalidOperationException("A reference name is required.");

        if (serviceProvider.GetService(typeof(Jalium.UI.Xaml.IXamlNameResolver)) is Jalium.UI.Xaml.IXamlNameResolver resolver)
        {
            object? resolved = resolver.Resolve(Name);
            if (resolved is not null) return resolved;
            if (resolver.IsFixupTokenAvailable) return resolver.GetFixupToken([Name]);
        }

        if (serviceProvider.GetService(typeof(INameScope)) is INameScope nameScope)
        {
            object? resolved = nameScope.FindName(Name);
            if (resolved is not null) return resolved;
        }

        throw new InvalidOperationException($"The XAML name '{Name}' could not be resolved.");
    }
}

[Jalium.UI.Markup.ContentProperty(nameof(Text))]
public sealed class XData
{
    public string? Text { get; set; }
    public object? XmlReader { get; set; }
}

public abstract class XamlInstanceCreator
{
    protected XamlInstanceCreator() { }
    public abstract object CreateObject();
}

public enum XamlWriterState
{
    Starting = 0,
    Finished = 1,
}

public static class XamlWriter
{
    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static string Save(object obj) => Jalium.UI.Xaml.XamlWriter.Save(obj);

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(object obj, Stream stream) => Jalium.UI.Xaml.XamlWriter.Save(obj, stream);

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(object obj, TextWriter writer) => Jalium.UI.Xaml.XamlWriter.Save(obj, writer);

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(object obj, XmlWriter xmlWriter)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(xmlWriter);
        xmlWriter.WriteRaw(Jalium.UI.Xaml.XamlWriter.Save(obj));
    }

    [RequiresUnreferencedCode("XAML serialization enumerates public runtime properties.")]
    public static void Save(object obj, XamlDesignerSerializationManager manager)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(manager);
        XmlWriter writer = manager.XmlWriter ?? throw new InvalidOperationException("The serialization manager has no XML writer.");
        Save(obj, writer);
    }
}
