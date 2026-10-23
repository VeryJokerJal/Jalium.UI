using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Jalium.UI.Xaml.SourceGenerator;

internal static class TemplateNameScope
{
    private static readonly DiagnosticDescriptor DuplicateName = new(
        "JALXAML007", "Duplicate template name",
        "The name '{0}' is already declared in this template's namescope",
        "Jalium.UI.Xaml", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static bool Apply(JalxamlParseResult result, SourceProductionContext context,
        string path, SourceText source)
    {
        var scopedNodes = new HashSet<JalxamlAstNode>();
        bool valid = true;
        Visit(result.Root!, false, null);

        // Namespace mappings supplied by an assembly are resolved after parsing. Remove
        // those template descendants by identity so an outer namesake keeps its field.
        result.NamedElements.RemoveAll(name => name.DeclaringNode != null && scopedNodes.Contains(name.DeclaringNode));
        foreach (var name in result.NamedElements)
            if (name.DeclaringNode?.ResolvedClrTypeName is { } resolved)
                name.TypeName = resolved;
        return valid;

        void Visit(JalxamlAstNode node, bool scoped, HashSet<string>? names)
        {
            if (scoped)
                scopedNodes.Add(node);
            if (names != null)
            {
                foreach (var attribute in node.Attributes)
                {
                    if (attribute.LocalName != "Name" ||
                        (attribute.Kind != JalxamlAttributeKind.XDirective &&
                         (attribute.Kind != JalxamlAttributeKind.Value || !string.IsNullOrEmpty(attribute.Prefix))))
                        continue;
                    if (!names.Add(attribute.Value))
                    {
                        int lineIndex = Math.Max(0, Math.Min(node.LineNumber - 1, source.Lines.Count - 1));
                        var line = source.Lines[lineIndex];
                        int position = line.Start + Math.Max(0, Math.Min(node.LinePosition - 1, line.Span.Length));
                        var span = new TextSpan(position, 0);
                        context.ReportDiagnostic(Diagnostic.Create(DuplicateName,
                            Location.Create(path, span, source.Lines.GetLinePositionSpan(span)), attribute.Value));
                        valid = false;
                    }
                }
            }

            bool template = JalxamlCodeGenerator.IsTemplateClrType(node.ResolvedClrTypeName);
            bool factory = template || node.LocalName == JalxamlParser.RazorSectionElementName ||
                node.LocalName == JalxamlParser.RazorVirtualizeElementName;
            var childNames = template ? new HashSet<string>(StringComparer.Ordinal) : factory ? null : names;
            foreach (var child in node.Children)
                Visit(child, scoped || factory, childNames);
            foreach (var property in node.PropertyElements)
            {
                // Resources and template metadata are definitions rather than the
                // visual tree whose names are registered on a template instance.
                var propertyNames = template || factory || property.PropertyName == "Resources" ? null : names;
                foreach (var child in property.Children)
                    Visit(child, scoped || factory, propertyNames);
            }
        }
    }
}
