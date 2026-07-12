using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Xaml.Schema;

namespace Jalium.UI.Xaml;

public class XamlDirective : XamlMember
{
    private readonly IList<string> _xamlNamespaces;

    public XamlDirective(string xamlNamespace, string name) : base(name)
    {
        _xamlNamespaces = [xamlNamespace ?? throw new ArgumentNullException(nameof(xamlNamespace))];
    }

    public XamlDirective(IEnumerable<string> xamlNamespaces, string name, XamlType xamlType, XamlValueConverter<TypeConverter>? typeConverter, AllowedMemberLocations allowedLocation)
        : base(name)
    {
        _xamlNamespaces = (xamlNamespaces ?? throw new ArgumentNullException(nameof(xamlNamespaces))).ToList();
        Type = xamlType ?? throw new ArgumentNullException(nameof(xamlType));
        TypeConverter = typeConverter;
        AllowedLocation = allowedLocation;
    }

    public AllowedMemberLocations AllowedLocation { get; }
    public XamlType? Type { get; }
    public XamlValueConverter<TypeConverter>? TypeConverter { get; }
    public virtual IList<string> GetXamlNamespaces() => new ReadOnlyCollection<string>(_xamlNamespaces);
    public override string ToString() => $"{{{_xamlNamespaces.FirstOrDefault()}}}{Name}";
    public override int GetHashCode() => HashCode.Combine(Name, _xamlNamespaces.FirstOrDefault());
}

public static class XamlLanguage
{
    public const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string Xml1998Namespace = "http://www.w3.org/XML/1998/namespace";

    private static readonly XamlSchemaContext s_schema = new();
    private static readonly IList<string> s_xamlNamespaces = new ReadOnlyCollection<string>([Xaml2006Namespace]);
    private static readonly IList<string> s_xmlNamespaces = new ReadOnlyCollection<string>([Xml1998Namespace]);

    public static XamlType Array { get; } = s_schema.GetXamlType(typeof(Jalium.UI.Markup.ArrayExtension));
    public static XamlType Boolean { get; } = s_schema.GetXamlType(typeof(bool));
    public static XamlType Byte { get; } = s_schema.GetXamlType(typeof(byte));
    public static XamlType Char { get; } = s_schema.GetXamlType(typeof(char));
    public static XamlType Decimal { get; } = s_schema.GetXamlType(typeof(decimal));
    public static XamlType Double { get; } = s_schema.GetXamlType(typeof(double));
    public static XamlType Int16 { get; } = s_schema.GetXamlType(typeof(short));
    public static XamlType Int32 { get; } = s_schema.GetXamlType(typeof(int));
    public static XamlType Int64 { get; } = s_schema.GetXamlType(typeof(long));
    public static XamlType Null { get; } = s_schema.GetXamlType(typeof(Jalium.UI.Markup.NullExtension));
    public static XamlType Object { get; } = s_schema.GetXamlType(typeof(object));
    public static XamlType Single { get; } = s_schema.GetXamlType(typeof(float));
    public static XamlType Static { get; } = s_schema.GetXamlType(typeof(Jalium.UI.Markup.StaticExtension));
    public static XamlType String { get; } = s_schema.GetXamlType(typeof(string));
    public static XamlType TimeSpan { get; } = s_schema.GetXamlType(typeof(TimeSpan));
    public static XamlType Type { get; } = s_schema.GetXamlType(typeof(Jalium.UI.Markup.TypeExtension));
    public static XamlType Uri { get; } = s_schema.GetXamlType(typeof(System.Uri));
    public static XamlType Member { get; } = UnknownType("Member");
    public static XamlType Property { get; } = UnknownType("Property");
    public static XamlType Reference { get; } = UnknownType("Reference");
    public static XamlType XData { get; } = UnknownType("XData");

    public static XamlDirective Arguments { get; } = Directive("Arguments");
    public static XamlDirective AsyncRecords { get; } = Directive("AsyncRecords");
    public static XamlDirective Base { get; } = XmlDirective("base");
    public static XamlDirective Class { get; } = Directive("Class");
    public static XamlDirective ClassAttributes { get; } = Directive("ClassAttributes");
    public static XamlDirective ClassModifier { get; } = Directive("ClassModifier");
    public static XamlDirective Code { get; } = Directive("Code");
    public static XamlDirective ConnectionId { get; } = Directive("ConnectionId");
    public static XamlDirective FactoryMethod { get; } = Directive("FactoryMethod");
    public static XamlDirective FieldModifier { get; } = Directive("FieldModifier");
    public static XamlDirective Initialization { get; } = Directive("Initialization");
    public static XamlDirective Items { get; } = Directive("Items");
    public static XamlDirective Key { get; } = Directive("Key");
    public static XamlDirective Lang { get; } = XmlDirective("lang");
    public static XamlDirective Members { get; } = Directive("Members");
    public static XamlDirective Name { get; } = Directive("Name");
    public static XamlDirective PositionalParameters { get; } = Directive("PositionalParameters");
    public static XamlDirective Shared { get; } = Directive("Shared");
    public static XamlDirective Space { get; } = XmlDirective("space");
    public static XamlDirective Subclass { get; } = Directive("Subclass");
    public static XamlDirective SynchronousMode { get; } = Directive("SynchronousMode");
    public static XamlDirective TypeArguments { get; } = Directive("TypeArguments");
    public static XamlDirective Uid { get; } = Directive("Uid");
    public static XamlDirective UnknownContent { get; } = Directive("UnknownContent");

    public static ReadOnlyCollection<XamlDirective> AllDirectives { get; } = new([
        Arguments, AsyncRecords, Base, Class, ClassAttributes, ClassModifier, Code, ConnectionId,
        FactoryMethod, FieldModifier, Initialization, Items, Key, Lang, Members, Name,
        PositionalParameters, Shared, Space, Subclass, SynchronousMode, TypeArguments, Uid, UnknownContent]);

    public static ReadOnlyCollection<XamlType> AllTypes { get; } = new([
        Array, Boolean, Byte, Char, Decimal, Double, Int16, Int32, Int64, Member, Null, Object,
        Property, Reference, Single, Static, String, TimeSpan, Type, Uri, XData]);

    public static IList<string> XamlNamespaces => s_xamlNamespaces;
    public static IList<string> XmlNamespaces => s_xmlNamespaces;

    private static XamlDirective Directive(string name) => new(Xaml2006Namespace, name);
    private static XamlDirective XmlDirective(string name) => new(Xml1998Namespace, name);
    private static XamlType UnknownType(string name) => new(Xaml2006Namespace, name, [], s_schema);
}
