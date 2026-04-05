// High-level LSP client wrapping JsonRpcConnection with typed methods for all LSP features.

using System.Diagnostics;
using System.Text.Json;
using Jalium.UI.Controls.Editor.LanguageServer.Protocol;

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// A full-featured LSP client that manages the server lifecycle, initialization handshake,
/// and provides typed async methods for every LSP request/notification.
/// </summary>
internal sealed class LspClient : IAsyncDisposable
{
    private readonly LanguageServerConfig _config;
    private LanguageServerProcess? _serverProcess;
    private JsonRpcConnection? _connection;
    private ServerCapabilities? _serverCapabilities;
    private bool _initialized;
    private bool _disposed;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Parsed capabilities cache
    private TextDocumentSyncKind _syncKind = TextDocumentSyncKind.Full;
    private bool _saveIncludesText;
    private SemanticTokensOptions? _semanticTokensOptions;

    /// <summary>
    /// Raised when the server publishes diagnostics.
    /// </summary>
    public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

    /// <summary>
    /// Raised when the server sends a log message.
    /// </summary>
    public event Action<LogMessageParams>? LogMessageReceived;

    /// <summary>
    /// Raised when the server sends a show message notification.
    /// </summary>
    public event Action<ShowMessageParams>? ShowMessageReceived;

    /// <summary>
    /// Raised when the server sends a progress notification.
    /// </summary>
    public event Action<ProgressParams>? ProgressReceived;

    /// <summary>
    /// Raised when the server requests workspace/applyEdit.
    /// </summary>
    public event Func<ApplyWorkspaceEditParams, Task<ApplyWorkspaceEditResult>>? ApplyEditRequested;

    /// <summary>
    /// Raised when the server requests workspace/configuration.
    /// </summary>
    public event Func<ConfigurationParams, Task<JsonElement[]>>? ConfigurationRequested;

    /// <summary>
    /// Raised when the server requests window/showDocument.
    /// </summary>
    public event Func<ShowDocumentParams, Task<ShowDocumentResult>>? ShowDocumentRequested;

    /// <summary>
    /// Raised when the server connection is lost (process crashed or stream closed).
    /// </summary>
    public event Action? ConnectionLost;

    /// <summary>
    /// Raised when the server is successfully (re)initialized.
    /// </summary>
    public event Action<ServerCapabilities>? ServerInitialized;

    /// <summary>
    /// Gets the negotiated server capabilities (null before initialization).
    /// </summary>
    public ServerCapabilities? ServerCapabilities => _serverCapabilities;

    /// <summary>
    /// Gets the text document sync kind the server supports.
    /// </summary>
    public TextDocumentSyncKind SyncKind => _syncKind;

    /// <summary>
    /// Whether save notifications should include the full document text.
    /// </summary>
    public bool SaveIncludesText => _saveIncludesText;

    /// <summary>
    /// Parsed semantic tokens options from server capabilities.
    /// </summary>
    public SemanticTokensOptions? SemanticTokensOptions => _semanticTokensOptions;

    /// <summary>
    /// Whether the client is initialized and ready.
    /// </summary>
    public bool IsInitialized => _initialized;

    public LspClient(LanguageServerConfig config)
    {
        _config = config;
    }

    #region Lifecycle

    /// <summary>
    /// Starts the language server, performs the initialization handshake, and begins listening.
    /// </summary>
    public async Task InitializeAsync(string? rootUri, string? rootPath = null,
        WorkspaceFolder[]? workspaceFolders = null, CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LspClient));
            if (_initialized) return;

            _serverProcess = new LanguageServerProcess(_config);
            _serverProcess.ProcessExited += OnProcessExited;
            _serverProcess.ProcessError += ex => System.Diagnostics.Debug.WriteLine($"[LSP] Process error: {ex}");

            _connection = _serverProcess.Start();
            WireNotifications(_connection);
            _connection.StartListening();

            var initParams = new InitializeParams
            {
                ProcessId = System.Environment.ProcessId,
                ClientInfo = new ClientInfo { Name = "Jalium.UI", Version = "1.0" },
                RootUri = rootUri,
                RootPath = rootPath,
                WorkspaceFolders = workspaceFolders,
                Capabilities = CreateClientCapabilities(),
                InitializationOptions = _config.InitializationOptions != null
                    ? JsonSerializer.SerializeToElement(_config.InitializationOptions)
                    : null,
                Trace = "off",
            };

            var result = await _connection.SendRequestAsync<InitializeResult>(
                LspMethods.Initialize, initParams, ct).ConfigureAwait(false);

            _serverCapabilities = result?.Capabilities ?? new ServerCapabilities();
            ParseServerCapabilities(_serverCapabilities);

            await _connection.SendNotificationAsync(LspMethods.Initialized, new { }, ct).ConfigureAwait(false);
            _initialized = true;

            ServerInitialized?.Invoke(_serverCapabilities);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gracefully shuts down the server.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        _initialized = false;
        if (_serverProcess != null)
        {
            await _serverProcess.ShutdownAsync(ct).ConfigureAwait(false);
        }
    }

    private void OnProcessExited()
    {
        _initialized = false;
        ConnectionLost?.Invoke();
    }

    private ClientCapabilities CreateClientCapabilities()
    {
        return new ClientCapabilities
        {
            Workspace = new WorkspaceClientCapabilities
            {
                ApplyEdit = true,
                WorkspaceEdit = new WorkspaceEditClientCapabilities
                {
                    DocumentChanges = true,
                    ResourceOperations = ["create", "rename", "delete"],
                },
                WorkspaceFolders = true,
                Configuration = true,
                DidChangeConfiguration = new DidChangeConfigurationClientCapabilities { DynamicRegistration = true },
                DidChangeWatchedFiles = new DidChangeWatchedFilesClientCapabilities { DynamicRegistration = true },
                Symbol = new WorkspaceSymbolClientCapabilities
                {
                    ResolveSupport = new SymbolResolveSupport { Properties = ["location.range"] },
                },
                SemanticTokens = new SemanticTokensWorkspaceClientCapabilities { RefreshSupport = true },
                Diagnostics = new DiagnosticWorkspaceClientCapabilities { RefreshSupport = true },
                InlayHint = new InlayHintWorkspaceClientCapabilities { RefreshSupport = true },
            },
            TextDocument = new TextDocumentClientCapabilities
            {
                Synchronization = new TextDocumentSyncClientCapabilities
                {
                    WillSave = true,
                    WillSaveWaitUntil = true,
                    DidSave = true,
                },
                Completion = new CompletionClientCapabilities
                {
                    CompletionItem = new CompletionItemClientCapabilities
                    {
                        SnippetSupport = false,
                        CommitCharactersSupport = true,
                        DocumentationFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
                        DeprecatedSupport = true,
                        PreselectSupport = true,
                        LabelDetailsSupport = true,
                        ResolveSupport = new CompletionItemResolveSupport
                        {
                            Properties = ["documentation", "detail", "additionalTextEdits"],
                        },
                    },
                    ContextSupport = true,
                    CompletionList = new CompletionListCapabilities
                    {
                        ItemDefaults = ["commitCharacters", "editRange", "insertTextFormat", "data"],
                    },
                },
                Hover = new HoverClientCapabilities
                {
                    ContentFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
                },
                SignatureHelp = new SignatureHelpClientCapabilities
                {
                    SignatureInformation = new SignatureInformationCapabilities
                    {
                        DocumentationFormat = [MarkupKind.Markdown, MarkupKind.PlainText],
                        ParameterInformation = new ParameterInformationCapabilities { LabelOffsetSupport = true },
                        ActiveParameterSupport = true,
                    },
                    ContextSupport = true,
                },
                Declaration = new DeclarationClientCapabilities { LinkSupport = true },
                Definition = new DefinitionClientCapabilities { LinkSupport = true },
                TypeDefinition = new TypeDefinitionClientCapabilities { LinkSupport = true },
                Implementation = new ImplementationClientCapabilities { LinkSupport = true },
                References = new ReferenceClientCapabilities(),
                DocumentHighlight = new DocumentHighlightClientCapabilities(),
                DocumentSymbol = new DocumentSymbolClientCapabilities
                {
                    HierarchicalDocumentSymbolSupport = true,
                    SymbolKind = new SymbolKindCapabilities
                    {
                        ValueSet = Enum.GetValues<SymbolKind>(),
                    },
                },
                CodeAction = new CodeActionClientCapabilities
                {
                    CodeActionLiteralSupport = new CodeActionLiteralSupport
                    {
                        CodeActionKind = new CodeActionKindCapabilities
                        {
                            ValueSet = [
                                CodeActionKind.QuickFix,
                                CodeActionKind.Refactor,
                                CodeActionKind.RefactorExtract,
                                CodeActionKind.RefactorInline,
                                CodeActionKind.RefactorRewrite,
                                CodeActionKind.Source,
                                CodeActionKind.SourceOrganizeImports,
                                CodeActionKind.SourceFixAll,
                            ],
                        },
                    },
                    IsPreferredSupport = true,
                    DisabledSupport = true,
                    DataSupport = true,
                    ResolveSupport = new CodeActionResolveSupport { Properties = ["edit"] },
                },
                CodeLens = new CodeLensClientCapabilities(),
                DocumentLink = new DocumentLinkClientCapabilities { TooltipSupport = true },
                ColorProvider = new DocumentColorClientCapabilities(),
                Formatting = new DocumentFormattingClientCapabilities(),
                RangeFormatting = new DocumentRangeFormattingClientCapabilities(),
                OnTypeFormatting = new DocumentOnTypeFormattingClientCapabilities(),
                Rename = new RenameClientCapabilities { PrepareSupport = true },
                PublishDiagnostics = new PublishDiagnosticsClientCapabilities
                {
                    RelatedInformation = true,
                    TagSupport = new DiagnosticTagSupport
                    {
                        ValueSet = [DiagnosticTag.Unnecessary, DiagnosticTag.Deprecated],
                    },
                    VersionSupport = true,
                    CodeDescriptionSupport = true,
                    DataSupport = true,
                },
                FoldingRange = new FoldingRangeClientCapabilities
                {
                    LineFoldingOnly = true,
                    FoldingRangeKind = new FoldingRangeKindCapabilities
                    {
                        ValueSet = [FoldingRangeKind.Comment, FoldingRangeKind.Imports, FoldingRangeKind.Region],
                    },
                    FoldingRange = new FoldingRangeCapabilities { CollapsedText = true },
                },
                SelectionRange = new SelectionRangeClientCapabilities(),
                LinkedEditingRange = new LinkedEditingRangeClientCapabilities(),
                CallHierarchy = new CallHierarchyClientCapabilities(),
                TypeHierarchy = new TypeHierarchyClientCapabilities(),
                SemanticTokens = new SemanticTokensClientCapabilities
                {
                    Requests = new SemanticTokensRequestCapabilities
                    {
                        Full = JsonSerializer.SerializeToElement(new { delta = true }),
                        Range = true,
                    },
                    TokenTypes = [
                        SemanticTokenTypes.Namespace, SemanticTokenTypes.Type, SemanticTokenTypes.Class,
                        SemanticTokenTypes.Enum, SemanticTokenTypes.Interface, SemanticTokenTypes.Struct,
                        SemanticTokenTypes.TypeParameter, SemanticTokenTypes.Parameter, SemanticTokenTypes.Variable,
                        SemanticTokenTypes.Property, SemanticTokenTypes.EnumMember, SemanticTokenTypes.Event,
                        SemanticTokenTypes.Function, SemanticTokenTypes.Method, SemanticTokenTypes.Macro,
                        SemanticTokenTypes.Keyword, SemanticTokenTypes.Modifier, SemanticTokenTypes.Comment,
                        SemanticTokenTypes.String, SemanticTokenTypes.Number, SemanticTokenTypes.Regexp,
                        SemanticTokenTypes.Operator, SemanticTokenTypes.Decorator,
                    ],
                    TokenModifiers = [
                        SemanticTokenModifiers.Declaration, SemanticTokenModifiers.Definition,
                        SemanticTokenModifiers.Readonly, SemanticTokenModifiers.Static,
                        SemanticTokenModifiers.Deprecated, SemanticTokenModifiers.Abstract,
                        SemanticTokenModifiers.Async, SemanticTokenModifiers.Modification,
                        SemanticTokenModifiers.Documentation, SemanticTokenModifiers.DefaultLibrary,
                    ],
                    Formats = ["relative"],
                    MultilineTokenSupport = true,
                    OverlappingTokenSupport = false,
                    AugmentsSyntaxTokens = true,
                },
                InlayHint = new InlayHintClientCapabilities
                {
                    ResolveSupport = new InlayHintResolveSupport { Properties = ["tooltip", "textEdits", "label.tooltip", "label.location", "label.command"] },
                },
                Diagnostic = new DiagnosticClientCapabilities { RelatedDocumentSupport = true },
            },
            Window = new WindowClientCapabilities
            {
                WorkDoneProgress = true,
                ShowMessage = new ShowMessageRequestClientCapabilities
                {
                    MessageActionItem = new MessageActionItemCapabilities { AdditionalPropertiesSupport = false },
                },
                ShowDocument = new ShowDocumentClientCapabilities { Support = true },
            },
            General = new GeneralClientCapabilities
            {
                PositionEncodings = ["utf-16"],
                StaleRequestSupport = new StaleRequestSupport
                {
                    Cancel = true,
                    RetryOnContentModified = [
                        LspMethods.TextDocumentCompletion,
                        LspMethods.TextDocumentSemanticTokensFull,
                        LspMethods.TextDocumentSemanticTokensFullDelta,
                        LspMethods.TextDocumentSemanticTokensRange,
                        LspMethods.TextDocumentInlayHint,
                    ],
                },
            },
        };
    }

    private void ParseServerCapabilities(ServerCapabilities caps)
    {
        // Parse textDocumentSync
        if (caps.TextDocumentSync.HasValue)
        {
            var sync = caps.TextDocumentSync.Value;
            if (sync.ValueKind == JsonValueKind.Number)
            {
                _syncKind = (TextDocumentSyncKind)sync.GetInt32();
            }
            else if (sync.ValueKind == JsonValueKind.Object)
            {
                var options = sync.Deserialize<TextDocumentSyncOptions>();
                _syncKind = options?.Change ?? TextDocumentSyncKind.None;
                if (options?.Save.HasValue == true)
                {
                    var save = options.Save.Value;
                    if (save.ValueKind == JsonValueKind.True)
                        _saveIncludesText = false;
                    else if (save.ValueKind == JsonValueKind.Object && save.TryGetProperty("includeText", out var inc))
                        _saveIncludesText = inc.GetBoolean();
                }
            }
        }

        // Parse semantic tokens
        if (caps.SemanticTokensProvider.HasValue)
        {
            var stp = caps.SemanticTokensProvider.Value;
            if (stp.ValueKind == JsonValueKind.Object)
            {
                _semanticTokensOptions = stp.Deserialize<SemanticTokensOptions>();
            }
        }
    }

    private void WireNotifications(JsonRpcConnection connection)
    {
        connection.NotificationReceived += OnNotification;
        connection.RequestReceived += OnServerRequest;
        connection.ConnectionClosed += () => ConnectionLost?.Invoke();
        connection.ConnectionError += ex =>
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Connection error: {ex}");
            ConnectionLost?.Invoke();
        };
    }

    private void OnNotification(string method, JsonElement? @params)
    {
        switch (method)
        {
            case LspMethods.TextDocumentPublishDiagnostics:
                if (@params.HasValue)
                {
                    var diag = @params.Value.Deserialize<PublishDiagnosticsParams>();
                    if (diag != null) DiagnosticsPublished?.Invoke(diag);
                }
                break;

            case LspMethods.WindowLogMessage:
                if (@params.HasValue)
                {
                    var log = @params.Value.Deserialize<LogMessageParams>();
                    if (log != null) LogMessageReceived?.Invoke(log);
                }
                break;

            case LspMethods.WindowShowMessage:
                if (@params.HasValue)
                {
                    var msg = @params.Value.Deserialize<ShowMessageParams>();
                    if (msg != null) ShowMessageReceived?.Invoke(msg);
                }
                break;

            case LspMethods.Progress:
                if (@params.HasValue)
                {
                    var progress = @params.Value.Deserialize<ProgressParams>();
                    if (progress != null) ProgressReceived?.Invoke(progress);
                }
                break;
        }
    }

    private async Task<JsonElement?> OnServerRequest(string method, JsonElement? @params, object? id)
    {
        switch (method)
        {
            case LspMethods.WorkspaceApplyEdit:
                if (@params.HasValue && ApplyEditRequested != null)
                {
                    var editParams = @params.Value.Deserialize<ApplyWorkspaceEditParams>();
                    if (editParams != null)
                    {
                        var result = await ApplyEditRequested(editParams).ConfigureAwait(false);
                        return JsonSerializer.SerializeToElement(result);
                    }
                }
                return JsonSerializer.SerializeToElement(new ApplyWorkspaceEditResult { Applied = false, FailureReason = "No handler" });

            case LspMethods.WorkspaceConfiguration:
                if (@params.HasValue && ConfigurationRequested != null)
                {
                    var cfgParams = @params.Value.Deserialize<ConfigurationParams>();
                    if (cfgParams != null)
                    {
                        var items = await ConfigurationRequested(cfgParams).ConfigureAwait(false);
                        return JsonSerializer.SerializeToElement(items);
                    }
                }
                return JsonSerializer.SerializeToElement(Array.Empty<JsonElement>());

            case LspMethods.WindowShowDocument:
                if (@params.HasValue && ShowDocumentRequested != null)
                {
                    var docParams = @params.Value.Deserialize<ShowDocumentParams>();
                    if (docParams != null)
                    {
                        var result = await ShowDocumentRequested(docParams).ConfigureAwait(false);
                        return JsonSerializer.SerializeToElement(result);
                    }
                }
                return JsonSerializer.SerializeToElement(new ShowDocumentResult { Success = false });

            case LspMethods.WindowWorkDoneProgressCreate:
                // Accept progress token creation
                return null;

            case LspMethods.WindowShowMessageRequest:
                // Auto-dismiss; a real UI would show a dialog
                return null;

            default:
                return null;
        }
    }

    #endregion

    #region Helpers

    private JsonRpcConnection GetConnection()
    {
        if (!_initialized || _connection == null)
            throw new InvalidOperationException("LSP client is not initialized.");
        return _connection;
    }

    /// <summary>
    /// Checks if a server capability is present (handles bool or object JsonElement).
    /// </summary>
    public static bool HasCapability(JsonElement? element)
    {
        if (!element.HasValue) return false;
        var e = element.Value;
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Object => true,
            _ => false,
        };
    }

    #endregion

    #region Text Synchronization

    public Task NotifyDidOpenAsync(DidOpenTextDocumentParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.TextDocumentDidOpen, @params, ct);

    public Task NotifyDidChangeAsync(DidChangeTextDocumentParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.TextDocumentDidChange, @params, ct);

    public Task NotifyDidSaveAsync(DidSaveTextDocumentParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.TextDocumentDidSave, @params, ct);

    public Task NotifyDidCloseAsync(DidCloseTextDocumentParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.TextDocumentDidClose, @params, ct);

    public Task NotifyWillSaveAsync(WillSaveTextDocumentParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.TextDocumentWillSave, @params, ct);

    public Task<TextEdit[]?> RequestWillSaveWaitUntilAsync(WillSaveTextDocumentParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TextEdit[]>(LspMethods.TextDocumentWillSaveWaitUntil, @params, ct);

    #endregion

    #region Diagnostics (Pull)

    public Task<FullDocumentDiagnosticReport?> RequestDocumentDiagnosticAsync(DocumentDiagnosticParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<FullDocumentDiagnosticReport>(LspMethods.TextDocumentDiagnostic, @params, ct);

    public Task<WorkspaceDiagnosticReport?> RequestWorkspaceDiagnosticAsync(WorkspaceDiagnosticParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<WorkspaceDiagnosticReport>(LspMethods.WorkspaceDiagnostic, @params, ct);

    #endregion

    #region Completion

    public async Task<CompletionList?> RequestCompletionAsync(CompletionParams @params, CancellationToken ct = default)
    {
        var result = await GetConnection().SendRequestAsync(LspMethods.TextDocumentCompletion, @params, ct).ConfigureAwait(false);
        if (result == null || result.Value.ValueKind == JsonValueKind.Null) return null;

        // Response can be CompletionItem[] or CompletionList
        if (result.Value.ValueKind == JsonValueKind.Array)
        {
            var items = result.Value.Deserialize<CompletionItem[]>();
            return new CompletionList { Items = items ?? [], IsIncomplete = false };
        }

        return result.Value.Deserialize<CompletionList>();
    }

    public Task<CompletionItem?> RequestCompletionResolveAsync(CompletionItem item, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CompletionItem>(LspMethods.CompletionItemResolve, item, ct);

    #endregion

    #region Hover & Signature Help

    public Task<Hover?> RequestHoverAsync(HoverParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<Hover>(LspMethods.TextDocumentHover, @params, ct);

    public Task<SignatureHelp?> RequestSignatureHelpAsync(SignatureHelpParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<SignatureHelp>(LspMethods.TextDocumentSignatureHelp, @params, ct);

    #endregion

    #region Navigation

    public Task<Location[]?> RequestDeclarationAsync(DeclarationParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<Location[]>(LspMethods.TextDocumentDeclaration, @params, ct);

    public Task<Location[]?> RequestDefinitionAsync(DefinitionParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<Location[]>(LspMethods.TextDocumentDefinition, @params, ct);

    public Task<Location[]?> RequestTypeDefinitionAsync(TypeDefinitionParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<Location[]>(LspMethods.TextDocumentTypeDefinition, @params, ct);

    public Task<Location[]?> RequestImplementationAsync(ImplementationParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<Location[]>(LspMethods.TextDocumentImplementation, @params, ct);

    public Task<Location[]?> RequestReferencesAsync(ReferenceParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<Location[]>(LspMethods.TextDocumentReferences, @params, ct);

    #endregion

    #region Highlight & Selection

    public Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(DocumentHighlightParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<DocumentHighlight[]>(LspMethods.TextDocumentDocumentHighlight, @params, ct);

    public Task<SelectionRange[]?> RequestSelectionRangeAsync(SelectionRangeParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<SelectionRange[]>(LspMethods.TextDocumentSelectionRange, @params, ct);

    #endregion

    #region Symbols

    public async Task<DocumentSymbol[]?> RequestDocumentSymbolAsync(DocumentSymbolParams @params, CancellationToken ct = default)
    {
        var result = await GetConnection().SendRequestAsync(LspMethods.TextDocumentDocumentSymbol, @params, ct).ConfigureAwait(false);
        if (result == null || result.Value.ValueKind == JsonValueKind.Null) return null;
        // Can be DocumentSymbol[] or SymbolInformation[] — try DocumentSymbol first
        return result.Value.Deserialize<DocumentSymbol[]>();
    }

    public Task<WorkspaceSymbol[]?> RequestWorkspaceSymbolAsync(WorkspaceSymbolParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<WorkspaceSymbol[]>(LspMethods.WorkspaceSymbol, @params, ct);

    public Task<WorkspaceSymbol?> RequestWorkspaceSymbolResolveAsync(WorkspaceSymbol symbol, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<WorkspaceSymbol>(LspMethods.WorkspaceSymbolResolve, symbol, ct);

    #endregion

    #region Code Action & Command

    public async Task<CodeAction[]?> RequestCodeActionAsync(CodeActionParams @params, CancellationToken ct = default)
    {
        var result = await GetConnection().SendRequestAsync(LspMethods.TextDocumentCodeAction, @params, ct).ConfigureAwait(false);
        if (result == null || result.Value.ValueKind == JsonValueKind.Null) return null;
        // Can be (Command | CodeAction)[] — deserialize as CodeAction[]
        return result.Value.Deserialize<CodeAction[]>();
    }

    public Task<CodeAction?> RequestCodeActionResolveAsync(CodeAction action, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CodeAction>(LspMethods.CodeActionResolve, action, ct);

    public Task<JsonElement?> RequestExecuteCommandAsync(string command, object[]? arguments = null, CancellationToken ct = default)
        => GetConnection().SendRequestAsync(LspMethods.WorkspaceExecuteCommand, new { command, arguments }, ct);

    #endregion

    #region CodeLens

    public Task<CodeLens[]?> RequestCodeLensAsync(CodeLensParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CodeLens[]>(LspMethods.TextDocumentCodeLens, @params, ct);

    public Task<CodeLens?> RequestCodeLensResolveAsync(CodeLens lens, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CodeLens>(LspMethods.CodeLensResolve, lens, ct);

    #endregion

    #region Document Link

    public Task<DocumentLink[]?> RequestDocumentLinkAsync(DocumentLinkParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<DocumentLink[]>(LspMethods.TextDocumentDocumentLink, @params, ct);

    public Task<DocumentLink?> RequestDocumentLinkResolveAsync(DocumentLink link, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<DocumentLink>(LspMethods.DocumentLinkResolve, link, ct);

    #endregion

    #region Color

    public Task<ColorInformation[]?> RequestDocumentColorAsync(DocumentColorParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<ColorInformation[]>(LspMethods.TextDocumentDocumentColor, @params, ct);

    public Task<ColorPresentation[]?> RequestColorPresentationAsync(ColorPresentationParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<ColorPresentation[]>(LspMethods.TextDocumentColorPresentation, @params, ct);

    #endregion

    #region Formatting

    public Task<TextEdit[]?> RequestFormattingAsync(DocumentFormattingParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TextEdit[]>(LspMethods.TextDocumentFormatting, @params, ct);

    public Task<TextEdit[]?> RequestRangeFormattingAsync(DocumentRangeFormattingParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TextEdit[]>(LspMethods.TextDocumentRangeFormatting, @params, ct);

    public Task<TextEdit[]?> RequestOnTypeFormattingAsync(DocumentOnTypeFormattingParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TextEdit[]>(LspMethods.TextDocumentOnTypeFormatting, @params, ct);

    #endregion

    #region Rename

    public Task<PrepareRenameResult?> RequestPrepareRenameAsync(PrepareRenameParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<PrepareRenameResult>(LspMethods.TextDocumentPrepareRename, @params, ct);

    public Task<WorkspaceEdit?> RequestRenameAsync(RenameParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<WorkspaceEdit>(LspMethods.TextDocumentRename, @params, ct);

    #endregion

    #region Folding & Linked Editing

    public Task<FoldingRange[]?> RequestFoldingRangeAsync(FoldingRangeParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<FoldingRange[]>(LspMethods.TextDocumentFoldingRange, @params, ct);

    public Task<LinkedEditingRanges?> RequestLinkedEditingRangeAsync(LinkedEditingRangeParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<LinkedEditingRanges>(LspMethods.TextDocumentLinkedEditingRange, @params, ct);

    #endregion

    #region Semantic Tokens

    public Task<SemanticTokens?> RequestSemanticTokensFullAsync(SemanticTokensParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<SemanticTokens>(LspMethods.TextDocumentSemanticTokensFull, @params, ct);

    public async Task<object?> RequestSemanticTokensDeltaAsync(SemanticTokensDeltaParams @params, CancellationToken ct = default)
    {
        var result = await GetConnection().SendRequestAsync(LspMethods.TextDocumentSemanticTokensFullDelta, @params, ct).ConfigureAwait(false);
        if (result == null || result.Value.ValueKind == JsonValueKind.Null) return null;
        // Can return SemanticTokens (full) or SemanticTokensDelta
        if (result.Value.TryGetProperty("edits", out _))
            return result.Value.Deserialize<SemanticTokensDelta>();
        return result.Value.Deserialize<SemanticTokens>();
    }

    public Task<SemanticTokens?> RequestSemanticTokensRangeAsync(SemanticTokensRangeParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<SemanticTokens>(LspMethods.TextDocumentSemanticTokensRange, @params, ct);

    #endregion

    #region Inlay Hints

    public Task<InlayHint[]?> RequestInlayHintAsync(InlayHintParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<InlayHint[]>(LspMethods.TextDocumentInlayHint, @params, ct);

    public Task<InlayHint?> RequestInlayHintResolveAsync(InlayHint hint, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<InlayHint>(LspMethods.InlayHintResolve, hint, ct);

    #endregion

    #region Inline Values

    public Task<JsonElement?> RequestInlineValueAsync(TextDocumentIdentifier textDocument, LspRange range, CancellationToken ct = default)
        => GetConnection().SendRequestAsync(LspMethods.TextDocumentInlineValue, new { textDocument, range }, ct);

    #endregion

    #region Call Hierarchy

    public Task<CallHierarchyItem[]?> RequestPrepareCallHierarchyAsync(CallHierarchyPrepareParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CallHierarchyItem[]>(LspMethods.TextDocumentPrepareCallHierarchy, @params, ct);

    public Task<CallHierarchyIncomingCall[]?> RequestIncomingCallsAsync(CallHierarchyIncomingCallsParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CallHierarchyIncomingCall[]>(LspMethods.CallHierarchyIncomingCalls, @params, ct);

    public Task<CallHierarchyOutgoingCall[]?> RequestOutgoingCallsAsync(CallHierarchyOutgoingCallsParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<CallHierarchyOutgoingCall[]>(LspMethods.CallHierarchyOutgoingCalls, @params, ct);

    #endregion

    #region Type Hierarchy

    public Task<TypeHierarchyItem[]?> RequestPrepareTypeHierarchyAsync(TypeHierarchyPrepareParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TypeHierarchyItem[]>(LspMethods.TextDocumentPrepareTypeHierarchy, @params, ct);

    public Task<TypeHierarchyItem[]?> RequestSupertypesAsync(TypeHierarchySupertypesParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TypeHierarchyItem[]>(LspMethods.TypeHierarchySupertypes, @params, ct);

    public Task<TypeHierarchyItem[]?> RequestSubtypesAsync(TypeHierarchySubtypesParams @params, CancellationToken ct = default)
        => GetConnection().SendRequestAsync<TypeHierarchyItem[]>(LspMethods.TypeHierarchySubtypes, @params, ct);

    #endregion

    #region Workspace Notifications

    public Task NotifyDidChangeConfigurationAsync(DidChangeConfigurationParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.WorkspaceDidChangeConfiguration, @params, ct);

    public Task NotifyDidChangeWatchedFilesAsync(DidChangeWatchedFilesParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.WorkspaceDidChangeWatchedFiles, @params, ct);

    public Task NotifyDidChangeWorkspaceFoldersAsync(DidChangeWorkspaceFoldersParams @params, CancellationToken ct = default)
        => GetConnection().SendNotificationAsync(LspMethods.WorkspaceDidChangeWorkspaceFolders, @params, ct);

    #endregion

    #region Dispose

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;

        if (_serverProcess != null)
        {
            await _serverProcess.DisposeAsync().ConfigureAwait(false);
            _serverProcess = null;
        }

        _initLock.Dispose();
    }

    #endregion
}
