using System.Collections;
using System.ComponentModel;

namespace Jalium.UI.Markup;

public sealed class XmlAttributeProperties
{
    [Browsable(false)]
    public static readonly DependencyProperty XmlNamespaceMapsProperty = DependencyProperty.RegisterAttached(
        "XmlNamespaceMaps", typeof(Hashtable), typeof(XmlAttributeProperties), new PropertyMetadata(null));

    [Browsable(false)]
    public static readonly DependencyProperty XmlnsDefinitionProperty = DependencyProperty.RegisterAttached(
        "XmlnsDefinition", typeof(string), typeof(XmlAttributeProperties), new PropertyMetadata(null));

    [Browsable(false)]
    public static readonly DependencyProperty XmlnsDictionaryProperty = DependencyProperty.RegisterAttached(
        "XmlnsDictionary", typeof(XmlnsDictionary), typeof(XmlAttributeProperties), new PropertyMetadata(null));

    [Browsable(false)]
    public static readonly DependencyProperty XmlSpaceProperty = DependencyProperty.RegisterAttached(
        "XmlSpace", typeof(string), typeof(XmlAttributeProperties), new PropertyMetadata(string.Empty));

    private XmlAttributeProperties() { }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static Hashtable? GetXmlNamespaceMaps(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        return dependencyObject.GetValue(XmlNamespaceMapsProperty) as Hashtable;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    [DesignerSerializationOptions(DesignerSerializationOptions.SerializeAsAttribute)]
    public static string? GetXmlnsDefinition(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        return dependencyObject.GetValue(XmlnsDefinitionProperty) as string;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static XmlnsDictionary? GetXmlnsDictionary(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        return dependencyObject.GetValue(XmlnsDictionaryProperty) as XmlnsDictionary;
    }

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    [DesignerSerializationOptions(DesignerSerializationOptions.SerializeAsAttribute)]
    public static string GetXmlSpace(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        return dependencyObject.GetValue(XmlSpaceProperty) as string ?? string.Empty;
    }

    public static void SetXmlNamespaceMaps(DependencyObject dependencyObject, Hashtable? value)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        dependencyObject.SetValue(XmlNamespaceMapsProperty, value);
    }

    public static void SetXmlnsDefinition(DependencyObject dependencyObject, string? value)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        dependencyObject.SetValue(XmlnsDefinitionProperty, value);
    }

    public static void SetXmlnsDictionary(DependencyObject dependencyObject, XmlnsDictionary? value)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        dependencyObject.SetValue(XmlnsDictionaryProperty, value);
    }

    public static void SetXmlSpace(DependencyObject dependencyObject, string? value)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        dependencyObject.SetValue(XmlSpaceProperty, value ?? string.Empty);
    }
}
