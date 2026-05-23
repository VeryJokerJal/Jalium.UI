using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Source generator that materializes <c>[LoggerMessage]</c>-decorated <c>static partial</c> methods
/// into zero-allocation logging calls backed by <c>Jalium.Extensions.Logging.LoggerMessage.Define</c>.
/// </summary>
/// <remarks>
/// Recognised attribute: <c>Jalium.Extensions.Logging.LoggerMessageAttribute</c>. Pattern:
/// <code>
/// internal static partial class Log
/// {
///     [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "User {UserId} logged in from {Ip}")]
///     public static partial void UserLoggedIn(ILogger logger, int userId, string ip);
/// }
/// </code>
/// Limitations vs. official MS generator:
/// <list type="bullet">
///   <item>Up to 6 typed parameters (matches our <c>LoggerMessage.Define&lt;T1…T6&gt;</c> arity).</item>
///   <item>One <c>Exception</c> parameter allowed; it's passed straight through.</item>
///   <item>Methods must be partial; first parameter (or "this") must be <c>ILogger</c>.</item>
/// </list>
/// </remarks>
[Generator]
public sealed class LoggerMessageSourceGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Jalium.Extensions.Logging.LoggerMessageAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methodInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeFullName,
            predicate: static (node, _) => node is MethodDeclarationSyntax m
                && m.Modifiers.Any(t => t.Text == "partial"),
            transform: static (ctx, _) => GatherMethodInfo(ctx))
            .Where(static x => x is not null)!;

        context.RegisterSourceOutput(methodInfos, static (spc, info) =>
        {
            if (info == null) return;
            var source = Generate(info);
            spc.AddSource($"{info.ContainingType.Replace(".", "_")}.{info.MethodName}.LoggerMessage.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static LoggerMessageMethodInfo? GatherMethodInfo(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method) return null;
        var attr = ctx.Attributes.FirstOrDefault();
        if (attr == null) return null;

        int eventId = 0;
        string? eventName = null;
        int level = 2; // Information
        string? message = null;
        bool skipEnabledCheck = false;

        // Positional ctor args (eventId, level, message)
        if (attr.ConstructorArguments.Length >= 3)
        {
            if (attr.ConstructorArguments[0].Value is int eid) eventId = eid;
            if (attr.ConstructorArguments[1].Value is int lvl) level = lvl;
            if (attr.ConstructorArguments[2].Value is string msg) message = msg;
        }

        foreach (var kv in attr.NamedArguments)
        {
            switch (kv.Key)
            {
                case "EventId": if (kv.Value.Value is int i1) eventId = i1; break;
                case "Level": if (kv.Value.Value is int i2) level = i2; break;
                case "Message": if (kv.Value.Value is string s) message = s; break;
                case "EventName": eventName = kv.Value.Value as string; break;
                case "SkipEnabledCheck": if (kv.Value.Value is bool b) skipEnabledCheck = b; break;
            }
        }

        if (string.IsNullOrEmpty(message)) return null;

        var parameters = method.Parameters;
        // Find ILogger parameter (must be first non-this, or 'this' on extension methods).
        int loggerIdx = -1;
        int exceptionIdx = -1;
        var typed = new List<(int Index, string Type, string Name)>();
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var typeName = p.Type.ToDisplayString();
            if (loggerIdx < 0 && (typeName == "Jalium.Extensions.Logging.ILogger" || typeName.StartsWith("Jalium.Extensions.Logging.ILogger<")))
            {
                loggerIdx = i;
            }
            else if (typeName == "System.Exception" && exceptionIdx < 0)
            {
                exceptionIdx = i;
            }
            else
            {
                typed.Add((i, typeName, p.Name));
            }
        }
        if (loggerIdx < 0) return null;
        if (typed.Count > 6) return null;

        return new LoggerMessageMethodInfo
        {
            ContainingType = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ContainingTypeShort = method.ContainingType.Name,
            Namespace = method.ContainingType.ContainingNamespace.IsGlobalNamespace ? null : method.ContainingType.ContainingNamespace.ToDisplayString(),
            ContainingTypeIsStatic = method.ContainingType.IsStatic,
            ContainingTypeKeyword = method.ContainingType.TypeKind == TypeKind.Struct ? "struct" : "class",
            MethodName = method.Name,
            MethodAccessibility = SyntaxFacts.GetAccessibilityKeyword(method.DeclaredAccessibility),
            IsStatic = method.IsStatic,
            EventId = eventId,
            EventName = eventName,
            Level = level,
            Message = message!,
            SkipEnabledCheck = skipEnabledCheck,
            LoggerParameterName = parameters[loggerIdx].Name,
            ExceptionParameterName = exceptionIdx >= 0 ? parameters[exceptionIdx].Name : null,
            TypedParameters = typed.Select(t => new TypedParam(t.Type, t.Name)).ToList(),
            FullParameterList = string.Join(", ", parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}")),
        };
    }

    private static string Generate(LoggerMessageMethodInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Jalium.Extensions.Logging;");
        sb.AppendLine();
        var hasNs = !string.IsNullOrEmpty(info.Namespace);
        if (hasNs) sb.AppendLine($"namespace {info.Namespace};").AppendLine();

        var modStatic = info.ContainingTypeIsStatic ? "static " : string.Empty;
        sb.AppendLine($"partial {modStatic}{info.ContainingTypeKeyword} {info.ContainingTypeShort}");
        sb.AppendLine("{");

        // Cache field for the LoggerMessage.Define delegate.
        var paramCount = info.TypedParameters.Count;
        var typeArgs = paramCount == 0 ? string.Empty : "<" + string.Join(", ", info.TypedParameters.Select(p => p.Type)) + ">";
        var delegateArgs = paramCount == 0
            ? "global::Jalium.Extensions.Logging.ILogger, global::System.Exception?"
            : "global::Jalium.Extensions.Logging.ILogger, " + string.Join(", ", info.TypedParameters.Select(p => p.Type)) + ", global::System.Exception?";

        sb.AppendLine($"    private static readonly global::System.Action<{delegateArgs}> __{info.MethodName}_Callback = global::Jalium.Extensions.Logging.LoggerMessage.Define{typeArgs}(");
        sb.AppendLine($"        (global::Jalium.Extensions.Logging.LogLevel){info.Level},");
        var eventNameLit = info.EventName == null ? "null" : "\"" + EscapeString(info.EventName) + "\"";
        sb.AppendLine($"        new global::Jalium.Extensions.Logging.EventId({info.EventId}, {eventNameLit}),");
        sb.AppendLine($"        \"{EscapeString(info.Message)}\");");
        sb.AppendLine();

        // Method body
        var staticMod = info.IsStatic ? "static " : string.Empty;
        sb.AppendLine($"    {info.MethodAccessibility} {staticMod}partial void {info.MethodName}({info.FullParameterList})");
        sb.AppendLine("    {");
        if (!info.SkipEnabledCheck)
        {
            sb.AppendLine($"        if (!{info.LoggerParameterName}.IsEnabled((global::Jalium.Extensions.Logging.LogLevel){info.Level})) return;");
        }
        var argList = info.LoggerParameterName + (info.TypedParameters.Count == 0 ? string.Empty : ", " + string.Join(", ", info.TypedParameters.Select(p => p.Name))) + ", " + (info.ExceptionParameterName ?? "null");
        sb.AppendLine($"        __{info.MethodName}_Callback({argList});");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

internal sealed class LoggerMessageMethodInfo
{
    public string ContainingType { get; set; } = string.Empty;
    public string ContainingTypeShort { get; set; } = string.Empty;
    public string ContainingTypeKeyword { get; set; } = "class";
    public string? Namespace { get; set; }
    public bool ContainingTypeIsStatic { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string MethodAccessibility { get; set; } = "public";
    public bool IsStatic { get; set; }
    public int EventId { get; set; }
    public string? EventName { get; set; }
    public int Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool SkipEnabledCheck { get; set; }
    public string LoggerParameterName { get; set; } = "logger";
    public string? ExceptionParameterName { get; set; }
    public List<TypedParam> TypedParameters { get; set; } = new();
    public string FullParameterList { get; set; } = string.Empty;
}

internal sealed class TypedParam
{
    public TypedParam(string type, string name) { Type = type; Name = name; }
    public string Type { get; }
    public string Name { get; }
}

internal static class SyntaxFacts
{
    public static string GetAccessibilityKeyword(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.Private => "private",
        _ => "internal",
    };
}
