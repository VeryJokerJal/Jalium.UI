using System.Runtime.CompilerServices;
using Jalium.UI.Markup;

[assembly: InternalsVisibleTo("Jalium.UI.Interop")]
[assembly: InternalsVisibleTo("Jalium.UI.Tests")]

// Expose CLR namespaces defined in Jalium.UI.Media under the canonical JALXAML namespace.
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Animation")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Effects")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Imaging")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Media3D")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.TextFormatting")]
