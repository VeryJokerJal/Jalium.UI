using System.Runtime.CompilerServices;
using Jalium.UI.Markup;

[assembly: InternalsVisibleTo("Jalium.UI.Xaml")]
[assembly: InternalsVisibleTo("Jalium.UI.Tests")]
// Platform integration packages implement INotificationBackend directly and
// need access to internal handle/helper members in Notifications/.
[assembly: InternalsVisibleTo("Jalium.UI.Desktop")]
[assembly: InternalsVisibleTo("Jalium.UI.Android")]

// Expose CLR namespaces defined in Jalium.UI.Controls under the canonical JALXAML namespace.
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Animation", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Annotations", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Annotations.Storage", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Automation", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Charts", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.DevTools", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Helpers", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Ink", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Ink", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Input.StylusPlugIns", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Navigation", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Primitives", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Ribbon", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Shapes", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Shell", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Shell", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.TextEffects", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.TextEffects.Effects", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Controls.Virtualization", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Documents", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Documents.DocumentStructures", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Hosting", AssemblyName = "Jalium.UI.Managed")]

// Redirect legacy Jalium URIs (and WPF's presentation URI) to the canonical namespace so existing
// documents continue to parse without modification.
[assembly: XmlnsCompatibleWith(JalxamlNamespaces.LegacyJaliumUi, JalxamlNamespaces.Presentation)]
[assembly: XmlnsCompatibleWith(JalxamlNamespaces.LegacyJaliumDev, JalxamlNamespaces.Presentation)]
[assembly: XmlnsCompatibleWith(JalxamlNamespaces.WpfPresentation, JalxamlNamespaces.Presentation)]

[assembly: XmlnsPrefix(JalxamlNamespaces.Presentation, "ui")]
[assembly: XmlnsPrefix(JalxamlNamespaces.XamlMarkup, "x")]
