using System.Runtime.CompilerServices;
using Jalium.UI.Markup;

[assembly: InternalsVisibleTo("Jalium.UI.Interop")]
[assembly: InternalsVisibleTo("Jalium.UI.Tests")]

// Expose CLR namespaces defined in Jalium.UI.Media under the canonical JALXAML namespace.
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Animation", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Effects", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Imaging", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Media3D", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.TextFormatting", AssemblyName = "Jalium.UI.Managed")]
