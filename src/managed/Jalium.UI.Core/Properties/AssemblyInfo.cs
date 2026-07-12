using System.Runtime.CompilerServices;
using Jalium.UI.Markup;

[assembly: InternalsVisibleTo("Jalium.UI.Input")]
[assembly: InternalsVisibleTo("Jalium.UI.Controls")]
[assembly: InternalsVisibleTo("Jalium.UI.Interop")]
[assembly: InternalsVisibleTo("Jalium.UI.Xaml")]
[assembly: InternalsVisibleTo("Jalium.UI.Media")]
[assembly: InternalsVisibleTo("Jalium.UI.Tests")]
[assembly: InternalsVisibleTo("ReactiveUI.Wpf")]

// Expose CLR namespaces defined in Jalium.UI.Core under the canonical JALXAML namespace so that
// documents using xmlns="http://schemas.jalium.com/jalxaml" can reference these types directly.
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Automation", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Automation.Peers", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Collections", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Data", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Documents", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Input", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Interactivity", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Markup", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Media.Animation", AssemblyName = "Jalium.UI.Managed")]
[assembly: XmlnsDefinition(JalxamlNamespaces.Presentation, "Jalium.UI.Threading", AssemblyName = "Jalium.UI.Managed")]
