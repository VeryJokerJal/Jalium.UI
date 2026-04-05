// LSP Initialize request/response types and capability definitions.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jalium.UI.Controls.Editor.LanguageServer.Protocol;

/// <summary>
/// Parameters for the initialize request.
/// </summary>
public sealed class InitializeParams
{
    [JsonPropertyName("processId")]
    public int? ProcessId { get; set; }

    [JsonPropertyName("clientInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClientInfo? ClientInfo { get; set; }

    [JsonPropertyName("locale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Locale { get; set; }

    [JsonPropertyName("rootPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootPath { get; set; }

    [JsonPropertyName("rootUri")]
    public string? RootUri { get; set; }

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("initializationOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InitializationOptions { get; set; }

    [JsonPropertyName("trace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Trace { get; set; }

    [JsonPropertyName("workspaceFolders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceFolder[]? WorkspaceFolders { get; set; }
}

public sealed class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }
}

public sealed class WorkspaceFolder
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Result of the initialize request.
/// </summary>
public sealed class InitializeResult
{
    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerInfo? ServerInfo { get; set; }
}

public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }
}

/// <summary>
/// Client capabilities sent during initialization.
/// </summary>
public sealed class ClientCapabilities
{
    [JsonPropertyName("workspace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceClientCapabilities? Workspace { get; set; }

    [JsonPropertyName("textDocument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextDocumentClientCapabilities? TextDocument { get; set; }

    [JsonPropertyName("window")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WindowClientCapabilities? Window { get; set; }

    [JsonPropertyName("general")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeneralClientCapabilities? General { get; set; }
}

public sealed class WorkspaceClientCapabilities
{
    [JsonPropertyName("applyEdit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ApplyEdit { get; set; }

    [JsonPropertyName("workspaceEdit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceEditClientCapabilities? WorkspaceEdit { get; set; }

    [JsonPropertyName("didChangeConfiguration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DidChangeConfigurationClientCapabilities? DidChangeConfiguration { get; set; }

    [JsonPropertyName("didChangeWatchedFiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DidChangeWatchedFilesClientCapabilities? DidChangeWatchedFiles { get; set; }

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceSymbolClientCapabilities? Symbol { get; set; }

    [JsonPropertyName("executeCommand")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecuteCommandClientCapabilities? ExecuteCommand { get; set; }

    [JsonPropertyName("workspaceFolders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WorkspaceFolders { get; set; }

    [JsonPropertyName("configuration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Configuration { get; set; }

    [JsonPropertyName("semanticTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticTokensWorkspaceClientCapabilities? SemanticTokens { get; set; }

    [JsonPropertyName("diagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiagnosticWorkspaceClientCapabilities? Diagnostics { get; set; }

    [JsonPropertyName("inlayHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InlayHintWorkspaceClientCapabilities? InlayHint { get; set; }
}

public sealed class WorkspaceEditClientCapabilities
{
    [JsonPropertyName("documentChanges")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DocumentChanges { get; set; }

    [JsonPropertyName("resourceOperations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ResourceOperations { get; set; }

    [JsonPropertyName("changeAnnotationSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChangeAnnotationSupport? ChangeAnnotationSupport { get; set; }
}

public sealed class ChangeAnnotationSupport
{
    [JsonPropertyName("groupsOnLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? GroupsOnLabel { get; set; }
}

public sealed class DidChangeConfigurationClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DidChangeWatchedFilesClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("relativePatternSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RelativePatternSupport { get; set; }
}

public sealed class WorkspaceSymbolClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("resolveSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolResolveSupport? ResolveSupport { get; set; }
}

public sealed class SymbolResolveSupport
{
    [JsonPropertyName("properties")]
    public string[] Properties { get; set; } = [];
}

public sealed class ExecuteCommandClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class SemanticTokensWorkspaceClientCapabilities
{
    [JsonPropertyName("refreshSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RefreshSupport { get; set; }
}

public sealed class DiagnosticWorkspaceClientCapabilities
{
    [JsonPropertyName("refreshSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RefreshSupport { get; set; }
}

public sealed class InlayHintWorkspaceClientCapabilities
{
    [JsonPropertyName("refreshSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RefreshSupport { get; set; }
}

public sealed class TextDocumentClientCapabilities
{
    [JsonPropertyName("synchronization")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextDocumentSyncClientCapabilities? Synchronization { get; set; }

    [JsonPropertyName("completion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionClientCapabilities? Completion { get; set; }

    [JsonPropertyName("hover")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HoverClientCapabilities? Hover { get; set; }

    [JsonPropertyName("signatureHelp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SignatureHelpClientCapabilities? SignatureHelp { get; set; }

    [JsonPropertyName("declaration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeclarationClientCapabilities? Declaration { get; set; }

    [JsonPropertyName("definition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DefinitionClientCapabilities? Definition { get; set; }

    [JsonPropertyName("typeDefinition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeDefinitionClientCapabilities? TypeDefinition { get; set; }

    [JsonPropertyName("implementation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImplementationClientCapabilities? Implementation { get; set; }

    [JsonPropertyName("references")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReferenceClientCapabilities? References { get; set; }

    [JsonPropertyName("documentHighlight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentHighlightClientCapabilities? DocumentHighlight { get; set; }

    [JsonPropertyName("documentSymbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentSymbolClientCapabilities? DocumentSymbol { get; set; }

    [JsonPropertyName("codeAction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodeActionClientCapabilities? CodeAction { get; set; }

    [JsonPropertyName("codeLens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodeLensClientCapabilities? CodeLens { get; set; }

    [JsonPropertyName("documentLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentLinkClientCapabilities? DocumentLink { get; set; }

    [JsonPropertyName("colorProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentColorClientCapabilities? ColorProvider { get; set; }

    [JsonPropertyName("formatting")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentFormattingClientCapabilities? Formatting { get; set; }

    [JsonPropertyName("rangeFormatting")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentRangeFormattingClientCapabilities? RangeFormatting { get; set; }

    [JsonPropertyName("onTypeFormatting")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentOnTypeFormattingClientCapabilities? OnTypeFormatting { get; set; }

    [JsonPropertyName("rename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RenameClientCapabilities? Rename { get; set; }

    [JsonPropertyName("publishDiagnostics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublishDiagnosticsClientCapabilities? PublishDiagnostics { get; set; }

    [JsonPropertyName("foldingRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FoldingRangeClientCapabilities? FoldingRange { get; set; }

    [JsonPropertyName("selectionRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SelectionRangeClientCapabilities? SelectionRange { get; set; }

    [JsonPropertyName("linkedEditingRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LinkedEditingRangeClientCapabilities? LinkedEditingRange { get; set; }

    [JsonPropertyName("callHierarchy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CallHierarchyClientCapabilities? CallHierarchy { get; set; }

    [JsonPropertyName("semanticTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SemanticTokensClientCapabilities? SemanticTokens { get; set; }

    [JsonPropertyName("moniker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MonikerClientCapabilities? Moniker { get; set; }

    [JsonPropertyName("typeHierarchy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeHierarchyClientCapabilities? TypeHierarchy { get; set; }

    [JsonPropertyName("inlineValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InlineValueClientCapabilities? InlineValue { get; set; }

    [JsonPropertyName("inlayHint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InlayHintClientCapabilities? InlayHint { get; set; }

    [JsonPropertyName("diagnostic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiagnosticClientCapabilities? Diagnostic { get; set; }
}

#region Text Document Client Capability Detail Types

public sealed class TextDocumentSyncClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("willSave")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WillSave { get; set; }

    [JsonPropertyName("willSaveWaitUntil")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WillSaveWaitUntil { get; set; }

    [JsonPropertyName("didSave")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DidSave { get; set; }
}

public sealed class CompletionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("completionItem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionItemClientCapabilities? CompletionItem { get; set; }

    [JsonPropertyName("completionItemKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionItemKindCapabilities? CompletionItemKind { get; set; }

    [JsonPropertyName("contextSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ContextSupport { get; set; }

    [JsonPropertyName("insertTextMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InsertTextMode? InsertTextMode { get; set; }

    [JsonPropertyName("completionList")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionListCapabilities? CompletionList { get; set; }
}

public sealed class CompletionItemClientCapabilities
{
    [JsonPropertyName("snippetSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SnippetSupport { get; set; }

    [JsonPropertyName("commitCharactersSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CommitCharactersSupport { get; set; }

    [JsonPropertyName("documentationFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? DocumentationFormat { get; set; }

    [JsonPropertyName("deprecatedSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeprecatedSupport { get; set; }

    [JsonPropertyName("preselectSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PreselectSupport { get; set; }

    [JsonPropertyName("insertReplaceSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InsertReplaceSupport { get; set; }

    [JsonPropertyName("resolveSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionItemResolveSupport? ResolveSupport { get; set; }

    [JsonPropertyName("labelDetailsSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LabelDetailsSupport { get; set; }
}

public sealed class CompletionItemResolveSupport
{
    [JsonPropertyName("properties")]
    public string[] Properties { get; set; } = [];
}

public sealed class CompletionItemKindCapabilities
{
    [JsonPropertyName("valueSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionItemKind[]? ValueSet { get; set; }
}

public sealed class CompletionListCapabilities
{
    [JsonPropertyName("itemDefaults")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ItemDefaults { get; set; }
}

public sealed class HoverClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("contentFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ContentFormat { get; set; }
}

public sealed class SignatureHelpClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("signatureInformation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SignatureInformationCapabilities? SignatureInformation { get; set; }

    [JsonPropertyName("contextSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ContextSupport { get; set; }
}

public sealed class SignatureInformationCapabilities
{
    [JsonPropertyName("documentationFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? DocumentationFormat { get; set; }

    [JsonPropertyName("parameterInformation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ParameterInformationCapabilities? ParameterInformation { get; set; }

    [JsonPropertyName("activeParameterSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ActiveParameterSupport { get; set; }
}

public sealed class ParameterInformationCapabilities
{
    [JsonPropertyName("labelOffsetSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LabelOffsetSupport { get; set; }
}

public sealed class DeclarationClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("linkSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LinkSupport { get; set; }
}

public sealed class DefinitionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("linkSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LinkSupport { get; set; }
}

public sealed class TypeDefinitionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("linkSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LinkSupport { get; set; }
}

public sealed class ImplementationClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("linkSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LinkSupport { get; set; }
}

public sealed class ReferenceClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DocumentHighlightClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DocumentSymbolClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("symbolKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolKindCapabilities? SymbolKind { get; set; }

    [JsonPropertyName("hierarchicalDocumentSymbolSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HierarchicalDocumentSymbolSupport { get; set; }

    [JsonPropertyName("labelSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LabelSupport { get; set; }
}

public sealed class SymbolKindCapabilities
{
    [JsonPropertyName("valueSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolKind[]? ValueSet { get; set; }
}

public sealed class CodeActionClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("codeActionLiteralSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodeActionLiteralSupport? CodeActionLiteralSupport { get; set; }

    [JsonPropertyName("isPreferredSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsPreferredSupport { get; set; }

    [JsonPropertyName("disabledSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DisabledSupport { get; set; }

    [JsonPropertyName("dataSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DataSupport { get; set; }

    [JsonPropertyName("resolveSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodeActionResolveSupport? ResolveSupport { get; set; }

    [JsonPropertyName("honorsChangeAnnotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HonorsChangeAnnotations { get; set; }
}

public sealed class CodeActionLiteralSupport
{
    [JsonPropertyName("codeActionKind")]
    public CodeActionKindCapabilities CodeActionKind { get; set; } = new();
}

public sealed class CodeActionKindCapabilities
{
    [JsonPropertyName("valueSet")]
    public string[] ValueSet { get; set; } = [];
}

public sealed class CodeActionResolveSupport
{
    [JsonPropertyName("properties")]
    public string[] Properties { get; set; } = [];
}

public sealed class CodeLensClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DocumentLinkClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("tooltipSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TooltipSupport { get; set; }
}

public sealed class DocumentColorClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DocumentFormattingClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DocumentRangeFormattingClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class DocumentOnTypeFormattingClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class RenameClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("prepareSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PrepareSupport { get; set; }

    [JsonPropertyName("honorsChangeAnnotations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HonorsChangeAnnotations { get; set; }
}

public sealed class PublishDiagnosticsClientCapabilities
{
    [JsonPropertyName("relatedInformation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RelatedInformation { get; set; }

    [JsonPropertyName("tagSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiagnosticTagSupport? TagSupport { get; set; }

    [JsonPropertyName("versionSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? VersionSupport { get; set; }

    [JsonPropertyName("codeDescriptionSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CodeDescriptionSupport { get; set; }

    [JsonPropertyName("dataSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DataSupport { get; set; }
}

public sealed class DiagnosticTagSupport
{
    [JsonPropertyName("valueSet")]
    public DiagnosticTag[] ValueSet { get; set; } = [];
}

public sealed class FoldingRangeClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("rangeLimit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RangeLimit { get; set; }

    [JsonPropertyName("lineFoldingOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LineFoldingOnly { get; set; }

    [JsonPropertyName("foldingRangeKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FoldingRangeKindCapabilities? FoldingRangeKind { get; set; }

    [JsonPropertyName("foldingRange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FoldingRangeCapabilities? FoldingRange { get; set; }
}

public sealed class FoldingRangeKindCapabilities
{
    [JsonPropertyName("valueSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ValueSet { get; set; }
}

public sealed class FoldingRangeCapabilities
{
    [JsonPropertyName("collapsedText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CollapsedText { get; set; }
}

public sealed class SelectionRangeClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class LinkedEditingRangeClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class CallHierarchyClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class SemanticTokensClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("requests")]
    public SemanticTokensRequestCapabilities Requests { get; set; } = new();

    [JsonPropertyName("tokenTypes")]
    public string[] TokenTypes { get; set; } = [];

    [JsonPropertyName("tokenModifiers")]
    public string[] TokenModifiers { get; set; } = [];

    [JsonPropertyName("formats")]
    public string[] Formats { get; set; } = [];

    [JsonPropertyName("overlappingTokenSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OverlappingTokenSupport { get; set; }

    [JsonPropertyName("multilineTokenSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MultilineTokenSupport { get; set; }

    [JsonPropertyName("serverCancelSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ServerCancelSupport { get; set; }

    [JsonPropertyName("augmentsSyntaxTokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AugmentsSyntaxTokens { get; set; }
}

public sealed class SemanticTokensRequestCapabilities
{
    [JsonPropertyName("range")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Range { get; set; }

    [JsonPropertyName("full")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Full { get; set; }
}

public sealed class MonikerClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class TypeHierarchyClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class InlineValueClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }
}

public sealed class InlayHintClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("resolveSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InlayHintResolveSupport? ResolveSupport { get; set; }
}

public sealed class InlayHintResolveSupport
{
    [JsonPropertyName("properties")]
    public string[] Properties { get; set; } = [];
}

public sealed class DiagnosticClientCapabilities
{
    [JsonPropertyName("dynamicRegistration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DynamicRegistration { get; set; }

    [JsonPropertyName("relatedDocumentSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RelatedDocumentSupport { get; set; }
}

#endregion

public sealed class WindowClientCapabilities
{
    [JsonPropertyName("showMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShowMessageRequestClientCapabilities? ShowMessage { get; set; }

    [JsonPropertyName("showDocument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShowDocumentClientCapabilities? ShowDocument { get; set; }

    [JsonPropertyName("workDoneProgress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WorkDoneProgress { get; set; }
}

public sealed class ShowMessageRequestClientCapabilities
{
    [JsonPropertyName("messageActionItem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MessageActionItemCapabilities? MessageActionItem { get; set; }
}

public sealed class MessageActionItemCapabilities
{
    [JsonPropertyName("additionalPropertiesSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdditionalPropertiesSupport { get; set; }
}

public sealed class ShowDocumentClientCapabilities
{
    [JsonPropertyName("support")]
    public bool Support { get; set; }
}

public sealed class GeneralClientCapabilities
{
    [JsonPropertyName("staleRequestSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StaleRequestSupport? StaleRequestSupport { get; set; }

    [JsonPropertyName("regularExpressions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RegularExpressionsClientCapabilities? RegularExpressions { get; set; }

    [JsonPropertyName("markdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MarkdownClientCapabilities? Markdown { get; set; }

    [JsonPropertyName("positionEncodings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? PositionEncodings { get; set; }
}

public sealed class StaleRequestSupport
{
    [JsonPropertyName("cancel")]
    public bool Cancel { get; set; }

    [JsonPropertyName("retryOnContentModified")]
    public string[] RetryOnContentModified { get; set; } = [];
}

public sealed class RegularExpressionsClientCapabilities
{
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }
}

public sealed class MarkdownClientCapabilities
{
    [JsonPropertyName("parser")]
    public string Parser { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    [JsonPropertyName("allowedTags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AllowedTags { get; set; }
}

/// <summary>
/// Server capabilities returned from the initialize request.
/// </summary>
public sealed class ServerCapabilities
{
    [JsonPropertyName("positionEncoding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PositionEncoding { get; set; }

    [JsonPropertyName("textDocumentSync")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? TextDocumentSync { get; set; }

    [JsonPropertyName("notebookDocumentSync")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? NotebookDocumentSync { get; set; }

    [JsonPropertyName("completionProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionOptions? CompletionProvider { get; set; }

    [JsonPropertyName("hoverProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? HoverProvider { get; set; }

    [JsonPropertyName("signatureHelpProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SignatureHelpOptions? SignatureHelpProvider { get; set; }

    [JsonPropertyName("declarationProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DeclarationProvider { get; set; }

    [JsonPropertyName("definitionProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DefinitionProvider { get; set; }

    [JsonPropertyName("typeDefinitionProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? TypeDefinitionProvider { get; set; }

    [JsonPropertyName("implementationProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ImplementationProvider { get; set; }

    [JsonPropertyName("referencesProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ReferencesProvider { get; set; }

    [JsonPropertyName("documentHighlightProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DocumentHighlightProvider { get; set; }

    [JsonPropertyName("documentSymbolProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DocumentSymbolProvider { get; set; }

    [JsonPropertyName("codeActionProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CodeActionProvider { get; set; }

    [JsonPropertyName("codeLensProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CodeLensOptions? CodeLensProvider { get; set; }

    [JsonPropertyName("documentLinkProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentLinkOptions? DocumentLinkProvider { get; set; }

    [JsonPropertyName("colorProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ColorProvider { get; set; }

    [JsonPropertyName("documentFormattingProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DocumentFormattingProvider { get; set; }

    [JsonPropertyName("documentRangeFormattingProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DocumentRangeFormattingProvider { get; set; }

    [JsonPropertyName("documentOnTypeFormattingProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentOnTypeFormattingOptions? DocumentOnTypeFormattingProvider { get; set; }

    [JsonPropertyName("renameProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? RenameProvider { get; set; }

    [JsonPropertyName("foldingRangeProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? FoldingRangeProvider { get; set; }

    [JsonPropertyName("executeCommandProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExecuteCommandOptions? ExecuteCommandProvider { get; set; }

    [JsonPropertyName("selectionRangeProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? SelectionRangeProvider { get; set; }

    [JsonPropertyName("linkedEditingRangeProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? LinkedEditingRangeProvider { get; set; }

    [JsonPropertyName("callHierarchyProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CallHierarchyProvider { get; set; }

    [JsonPropertyName("semanticTokensProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? SemanticTokensProvider { get; set; }

    [JsonPropertyName("monikerProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? MonikerProvider { get; set; }

    [JsonPropertyName("typeHierarchyProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? TypeHierarchyProvider { get; set; }

    [JsonPropertyName("inlineValueProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InlineValueProvider { get; set; }

    [JsonPropertyName("inlayHintProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? InlayHintProvider { get; set; }

    [JsonPropertyName("diagnosticProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiagnosticOptions? DiagnosticProvider { get; set; }

    [JsonPropertyName("workspaceSymbolProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? WorkspaceSymbolProvider { get; set; }

    [JsonPropertyName("workspace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerWorkspaceCapabilities? Workspace { get; set; }

    [JsonPropertyName("experimental")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Experimental { get; set; }
}

#region Server Capability Option Types

public sealed class CompletionOptions
{
    [JsonPropertyName("triggerCharacters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TriggerCharacters { get; set; }

    [JsonPropertyName("allCommitCharacters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? AllCommitCharacters { get; set; }

    [JsonPropertyName("resolveProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResolveProvider { get; set; }

    [JsonPropertyName("completionItem")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompletionOptionsCompletionItem? CompletionItem { get; set; }
}

public sealed class CompletionOptionsCompletionItem
{
    [JsonPropertyName("labelDetailsSupport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LabelDetailsSupport { get; set; }
}

public sealed class SignatureHelpOptions
{
    [JsonPropertyName("triggerCharacters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TriggerCharacters { get; set; }

    [JsonPropertyName("retriggerCharacters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? RetriggerCharacters { get; set; }
}

public sealed class CodeLensOptions
{
    [JsonPropertyName("resolveProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResolveProvider { get; set; }
}

public sealed class DocumentLinkOptions
{
    [JsonPropertyName("resolveProvider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ResolveProvider { get; set; }
}

public sealed class DocumentOnTypeFormattingOptions
{
    [JsonPropertyName("firstTriggerCharacter")]
    public string FirstTriggerCharacter { get; set; } = string.Empty;

    [JsonPropertyName("moreTriggerCharacter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? MoreTriggerCharacter { get; set; }
}

public sealed class ExecuteCommandOptions
{
    [JsonPropertyName("commands")]
    public string[] Commands { get; set; } = [];
}

public sealed class SemanticTokensOptions
{
    [JsonPropertyName("legend")]
    public SemanticTokensLegend Legend { get; set; } = new();

    [JsonPropertyName("range")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Range { get; set; }

    [JsonPropertyName("full")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Full { get; set; }
}

public sealed class SemanticTokensLegend
{
    [JsonPropertyName("tokenTypes")]
    public string[] TokenTypes { get; set; } = [];

    [JsonPropertyName("tokenModifiers")]
    public string[] TokenModifiers { get; set; } = [];
}

public sealed class DiagnosticOptions
{
    [JsonPropertyName("identifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Identifier { get; set; }

    [JsonPropertyName("interFileDependencies")]
    public bool InterFileDependencies { get; set; }

    [JsonPropertyName("workspaceDiagnostics")]
    public bool WorkspaceDiagnostics { get; set; }
}

public sealed class ServerWorkspaceCapabilities
{
    [JsonPropertyName("workspaceFolders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceFoldersServerCapabilities? WorkspaceFolders { get; set; }

    [JsonPropertyName("fileOperations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationCapabilities? FileOperations { get; set; }
}

public sealed class WorkspaceFoldersServerCapabilities
{
    [JsonPropertyName("supported")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Supported { get; set; }

    [JsonPropertyName("changeNotifications")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ChangeNotifications { get; set; }
}

public sealed class FileOperationCapabilities
{
    [JsonPropertyName("didCreate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationRegistrationOptions? DidCreate { get; set; }

    [JsonPropertyName("willCreate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationRegistrationOptions? WillCreate { get; set; }

    [JsonPropertyName("didRename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationRegistrationOptions? DidRename { get; set; }

    [JsonPropertyName("willRename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationRegistrationOptions? WillRename { get; set; }

    [JsonPropertyName("didDelete")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationRegistrationOptions? DidDelete { get; set; }

    [JsonPropertyName("willDelete")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileOperationRegistrationOptions? WillDelete { get; set; }
}

public sealed class FileOperationRegistrationOptions
{
    [JsonPropertyName("filters")]
    public FileOperationFilter[] Filters { get; set; } = [];
}

public sealed class FileOperationFilter
{
    [JsonPropertyName("scheme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scheme { get; set; }

    [JsonPropertyName("pattern")]
    public FileOperationPattern Pattern { get; set; } = new();
}

#endregion

#region Window/Progress notification types

public sealed class ShowMessageParams
{
    [JsonPropertyName("type")]
    public MessageType Type { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class ShowMessageRequestParams
{
    [JsonPropertyName("type")]
    public MessageType Type { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("actions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MessageActionItem[]? Actions { get; set; }
}

public sealed class MessageActionItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public enum MessageType
{
    Error = 1,
    Warning = 2,
    Info = 3,
    Log = 4,
    Debug = 5,
}

public sealed class LogMessageParams
{
    [JsonPropertyName("type")]
    public MessageType Type { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class ShowDocumentParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("external")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? External { get; set; }

    [JsonPropertyName("takeFocus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TakeFocus { get; set; }

    [JsonPropertyName("selection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LspRange? Selection { get; set; }
}

public sealed class ShowDocumentResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public sealed class WorkDoneProgressCreateParams
{
    [JsonPropertyName("token")]
    public object Token { get; set; } = 0;
}

public sealed class ProgressParams
{
    [JsonPropertyName("token")]
    public object Token { get; set; } = 0;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

public sealed class WorkDoneProgressBegin
{
    [JsonPropertyName("kind")]
    public string Kind => "begin";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("cancellable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cancellable { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("percentage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Percentage { get; set; }
}

public sealed class WorkDoneProgressReport
{
    [JsonPropertyName("kind")]
    public string Kind => "report";

    [JsonPropertyName("cancellable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cancellable { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("percentage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Percentage { get; set; }
}

public sealed class WorkDoneProgressEnd
{
    [JsonPropertyName("kind")]
    public string Kind => "end";

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}

#endregion

#region Selection Range extra types

public sealed class SelectionRangeParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("positions")]
    public LspPosition[] Positions { get; set; } = [];
}

public sealed class SelectionRange
{
    [JsonPropertyName("range")]
    public LspRange Range { get; set; } = new();

    [JsonPropertyName("parent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SelectionRange? Parent { get; set; }
}

// LinkedEditingRangeParams and LinkedEditingRanges are defined in LspLinkedEditing.cs

#endregion

#region Workspace change notification types

public sealed class DidChangeWatchedFilesParams
{
    [JsonPropertyName("changes")]
    public FileEvent[] Changes { get; set; } = [];
}

public sealed class FileEvent
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public FileChangeType Type { get; set; }
}

public enum FileChangeType
{
    Created = 1,
    Changed = 2,
    Deleted = 3,
}

public sealed class DidChangeConfigurationParams
{
    [JsonPropertyName("settings")]
    public JsonElement? Settings { get; set; }
}

public sealed class DidChangeWorkspaceFoldersParams
{
    [JsonPropertyName("event")]
    public WorkspaceFoldersChangeEvent Event { get; set; } = new();
}

public sealed class WorkspaceFoldersChangeEvent
{
    [JsonPropertyName("added")]
    public WorkspaceFolder[] Added { get; set; } = [];

    [JsonPropertyName("removed")]
    public WorkspaceFolder[] Removed { get; set; } = [];
}

public sealed class ConfigurationParams
{
    [JsonPropertyName("items")]
    public ConfigurationItem[] Items { get; set; } = [];
}

public sealed class ConfigurationItem
{
    [JsonPropertyName("scopeUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScopeUri { get; set; }

    [JsonPropertyName("section")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Section { get; set; }
}

public sealed class ApplyWorkspaceEditParams
{
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("edit")]
    public WorkspaceEdit Edit { get; set; } = new();
}

public sealed class ApplyWorkspaceEditResult
{
    [JsonPropertyName("applied")]
    public bool Applied { get; set; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }

    [JsonPropertyName("failedChange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FailedChange { get; set; }
}

#endregion

#region Text Document Sync parameter types

public sealed class DidOpenTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentItem TextDocument { get; set; } = new();
}

public sealed class DidChangeTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("contentChanges")]
    public TextDocumentContentChangeEvent[] ContentChanges { get; set; } = [];
}

public sealed class DidSaveTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
}

public sealed class DidCloseTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

public sealed class WillSaveTextDocumentParams
{
    [JsonPropertyName("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = new();

    [JsonPropertyName("reason")]
    public TextDocumentSaveReason Reason { get; set; }
}

public enum TextDocumentSaveReason
{
    Manual = 1,
    AfterDelay = 2,
    FocusOut = 3,
}

public sealed class TextDocumentSyncOptions
{
    [JsonPropertyName("openClose")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OpenClose { get; set; }

    [JsonPropertyName("change")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextDocumentSyncKind? Change { get; set; }

    [JsonPropertyName("willSave")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WillSave { get; set; }

    [JsonPropertyName("willSaveWaitUntil")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WillSaveWaitUntil { get; set; }

    [JsonPropertyName("save")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Save { get; set; }
}

#endregion
