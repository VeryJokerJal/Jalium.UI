using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Xaml
{
    /// <summary>
    /// Identifies the XAML member currently being assigned by an object writer.
    /// </summary>
    /// <remarks>
    /// Jalium's streaming and generated XAML writers only require the stable member name
    /// for receiver dispatch.  The type intentionally lives in the canonical
    /// <c>Jalium.UI.Xaml</c> namespace even though the lightweight contract is hosted by
    /// Core, so Core-level markup receivers do not introduce a Core-to-Xaml cycle.
    /// </remarks>
    public class XamlMember
    {
        /// <summary>Initializes a member descriptor with its XAML-visible name.</summary>
        public XamlMember(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            Name = name;
        }

        /// <summary>Gets the XAML-visible member name.</summary>
        public string Name { get; }
    }
}

namespace Jalium.UI.Markup
{
    /// <summary>Names the static receiver used when a markup extension is assigned in XAML.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class XamlSetMarkupExtensionAttribute : Attribute
    {
        public XamlSetMarkupExtensionAttribute(string? xamlSetMarkupExtensionHandler)
        {
            XamlSetMarkupExtensionHandler = xamlSetMarkupExtensionHandler;
        }

        public string? XamlSetMarkupExtensionHandler { get; }
    }

    /// <summary>Names the static receiver used when a type converter is assigned in XAML.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class XamlSetTypeConverterAttribute : Attribute
    {
        public XamlSetTypeConverterAttribute(string? xamlSetTypeConverterHandler)
        {
            XamlSetTypeConverterHandler = xamlSetTypeConverterHandler;
        }

        public string? XamlSetTypeConverterHandler { get; }
    }

    /// <summary>Provides the common member, value, and handled state for XAML set receivers.</summary>
    public class XamlSetValueEventArgs : EventArgs
    {
        public XamlSetValueEventArgs(global::Jalium.UI.Xaml.XamlMember member, object value)
        {
            Member = member;
            Value = value;
        }

        public global::Jalium.UI.Xaml.XamlMember Member { get; }

        public object Value { get; }

        public bool Handled { get; set; }

        /// <summary>
        /// Invokes the receiver declared by the base XAML type when the object writer has
        /// supplied one. The standalone public event args have no base receiver, so the
        /// default implementation is intentionally empty and remains overridable.
        /// </summary>
        public virtual void CallBase()
        {
        }
    }

    /// <summary>Provides a markup extension and its value service provider to a receiver.</summary>
    public class XamlSetMarkupExtensionEventArgs : XamlSetValueEventArgs
    {
        public XamlSetMarkupExtensionEventArgs(
            global::Jalium.UI.Xaml.XamlMember member,
            MarkupExtension value,
            IServiceProvider serviceProvider)
            : base(member, value)
        {
            ServiceProvider = serviceProvider;
        }

        public MarkupExtension MarkupExtension => (MarkupExtension)Value;

        public IServiceProvider ServiceProvider { get; private set; }
    }

    /// <summary>Provides type-conversion context to a XAML set receiver.</summary>
    public class XamlSetTypeConverterEventArgs : XamlSetValueEventArgs
    {
        public XamlSetTypeConverterEventArgs(
            global::Jalium.UI.Xaml.XamlMember member,
            TypeConverter typeConverter,
            object value,
            ITypeDescriptorContext serviceProvider,
            CultureInfo cultureInfo)
            : base(member, value)
        {
            TypeConverter = typeConverter;
            ServiceProvider = serviceProvider;
            CultureInfo = cultureInfo;
        }

        public TypeConverter TypeConverter { get; private set; }

        public ITypeDescriptorContext ServiceProvider { get; private set; }

        public CultureInfo CultureInfo { get; private set; }
    }

    /// <summary>
    /// Marks Jalium resource markup extensions that are legal deferred values for a
    /// style setter. Kept internal so the public compatibility surface remains WPF-shaped.
    /// </summary>
    public interface IStyleResourceMarkupExtension
    {
    }
}
