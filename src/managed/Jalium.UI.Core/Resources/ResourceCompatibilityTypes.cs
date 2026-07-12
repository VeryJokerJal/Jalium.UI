namespace Jalium.UI.Resources;

/// <summary>Declares a content file associated with an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AssemblyAssociatedContentFileAttribute : Attribute
{
    /// <summary>Initializes the attribute with the assembly-relative content path.</summary>
    public AssemblyAssociatedContentFileAttribute(string relativeContentFilePath)
    {
        RelativeContentFilePath = relativeContentFilePath ??
            throw new ArgumentNullException(nameof(relativeContentFilePath));
    }

    /// <summary>Gets the assembly-relative content path.</summary>
    public string RelativeContentFilePath { get; }
}

/// <summary>Defines MIME content types used by WPF-compatible resource packaging.</summary>
public sealed class ContentTypes
{
    /// <summary>The historical WPF XAML MIME content type.</summary>
    public const string XamlContentType = "applicaton/xaml+xml";

    /// <summary>Initializes a content-type helper.</summary>
    public ContentTypes()
    {
    }
}
