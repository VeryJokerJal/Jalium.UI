// Orchestrates all LSP features for a single EditControl instance.

using System.Text.Json;
using Jalium.UI.Controls.Editor.LanguageServer.Protocol;

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// Manages the lifecycle and integration of an LSP client with a single <see cref="EditControl"/>.
/// Handles initialization, document sync, diagnostics, completion, hover, signature help,
/// navigation, code actions, formatting, rename, semantic tokens, folding, inlay hints, etc.
/// </summary>
internal sealed class EditControlLspIntegration : IAsyncDisposable
{
    private readonly EditControl _editor;
    private LspClient? _client;
    private LspDocumentSync? _docSync;
    private LspSemanticHighlighter? _semanticHighlighter;
    private LspFoldingProvider? _foldingProvider;
    private string? _activeLanguage;
    private bool _disposed;
    private CancellationTokenSource? _activeCts;

    // Diagnostic state
    private Diagnostic[] _diagnostics = [];
    private int _diagnosticVersion;

    // Completion state
    private CompletionList? _activeCompletionList;
    private int _activeCompletionIndex = -1;
    private CancellationTokenSource? _completionCts;
    private bool _isCompletionActive;

    // Hover state
    private Hover? _activeHover;
    private LspRange? _activeHoverRange;
    private CancellationTokenSource? _hoverCts;

    // Signature help state
    private SignatureHelp? _activeSignatureHelp;
    private CancellationTokenSource? _signatureHelpCts;

    // Inlay hints state
    private InlayHint[] _inlayHints = [];
    private CancellationTokenSource? _inlayHintCts;

    // Code lens state
    private CodeLens[] _codeLenses = [];
    private CancellationTokenSource? _codeLensCts;

    #region Events

    /// <summary>Raised when diagnostics are updated for the current document.</summary>
    public event Action<Diagnostic[]>? DiagnosticsUpdated;

    /// <summary>Raised when the completion list is updated.</summary>
    public event Action<CompletionList?>? CompletionUpdated;

    /// <summary>Raised when hover information is available.</summary>
    public event Action<Hover?>? HoverUpdated;

    /// <summary>Raised when signature help is available.</summary>
    public event Action<SignatureHelp?>? SignatureHelpUpdated;

    /// <summary>Raised when inlay hints are updated.</summary>
    public event Action<InlayHint[]>? InlayHintsUpdated;

    /// <summary>Raised when code lenses are updated.</summary>
    public event Action<CodeLens[]>? CodeLensesUpdated;

    /// <summary>Raised when the server wants to navigate to a location.</summary>
    public event Action<Location[]>? NavigationRequested;

    /// <summary>Raised when a workspace edit should be applied.</summary>
    public event Func<WorkspaceEdit, Task<bool>>? WorkspaceEditRequested;

    /// <summary>Raised when the server sends a log/show message.</summary>
    public event Action<string, MessageType>? ServerMessage;

    #endregion

    #region Properties

    /// <summary>Gets the current diagnostics.</summary>
    public Diagnostic[] Diagnostics => _diagnostics;

    /// <summary>Gets the diagnostics version counter.</summary>
    public int DiagnosticVersion => _diagnosticVersion;

    /// <summary>Gets whether a completion popup is active.</summary>
    public bool IsCompletionActive => _isCompletionActive;

    /// <summary>Gets the current completion list.</summary>
    public CompletionList? ActiveCompletionList => _activeCompletionList;

    /// <summary>Gets or sets the selected completion index.</summary>
    public int ActiveCompletionIndex
    {
        get => _activeCompletionIndex;
        set => _activeCompletionIndex = value;
    }

    /// <summary>Gets the current hover result.</summary>
    public Hover? ActiveHover => _activeHover;

    /// <summary>Gets the current hover range.</summary>
    public LspRange? ActiveHoverRange => _activeHoverRange;

    /// <summary>Gets the current signature help.</summary>
    public SignatureHelp? ActiveSignatureHelp => _activeSignatureHelp;

    /// <summary>Gets the current inlay hints.</summary>
    public InlayHint[] InlayHints => _inlayHints;

    /// <summary>Gets the current code lenses.</summary>
    public CodeLens[] CodeLenses => _codeLenses;

    /// <summary>Gets the underlying LspClient.</summary>
    public LspClient? Client => _client;

    /// <summary>Gets the document sync helper.</summary>
    public LspDocumentSync? DocumentSync => _docSync;

    /// <summary>Gets whether LSP is connected and initialized.</summary>
    public bool IsActive => _client?.IsInitialized == true;

    /// <summary>Gets the server capabilities.</summary>
    public ServerCapabilities? ServerCapabilities => _client?.ServerCapabilities;

    #endregion

    public EditControlLspIntegration(EditControl editor)
    {
        _editor = editor;
    }

    #region Lifecycle

    /// <summary>
    /// Activates LSP for the given language. If a server is already running for a different
    /// language, it is shut down first.
    /// </summary>
    public async Task ActivateAsync(string language, CancellationToken ct = default)
    {
        if (_disposed) return;
        if (_activeLanguage == language && _client?.IsInitialized == true) return;

        await DeactivateAsync().ConfigureAwait(false);

        if (!LanguageServerRegistry.TryGetConfig(language, out var config) || config == null)
            return;

        _activeLanguage = language;
        _activeCts = new CancellationTokenSource();
        var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(ct, _activeCts.Token).Token;

        _client = new LspClient(config);
        _docSync = new LspDocumentSync(_client);

        // Wire events
        _client.DiagnosticsPublished += OnDiagnosticsPublished;
        _client.ConnectionLost += OnConnectionLost;
        _client.LogMessageReceived += msg => ServerMessage?.Invoke(msg.Message, msg.Type);
        _client.ShowMessageReceived += msg => ServerMessage?.Invoke(msg.Message, msg.Type);
        _client.ApplyEditRequested += OnApplyEditRequested;
        _client.ConfigurationRequested += OnConfigurationRequested;

        try
        {
            // Determine root URI from file path
            string? rootUri = null;
            var filePath = _editor.DocumentFilePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                var dir = Path.GetDirectoryName(filePath);
                if (dir != null)
                    rootUri = LspDocumentSync.FilePathToUri(dir);
            }

            await _client.InitializeAsync(rootUri, ct: linkedCt).ConfigureAwait(false);

            // Open document on server
            await _docSync.OpenAsync(_editor.Document, language,
                () => _editor.DocumentFilePath, linkedCt).ConfigureAwait(false);

            // Set up semantic highlighter if server supports it
            if (_client.SemanticTokensOptions != null)
            {
                var baseHighlighter = _editor.SyntaxHighlighter;
                _semanticHighlighter = new LspSemanticHighlighter(_client, _docSync, baseHighlighter);
                _editor.SyntaxHighlighter = _semanticHighlighter;
            }

            // Set up folding provider if server supports it
            if (LspClient.HasCapability(_client.ServerCapabilities?.FoldingRangeProvider))
            {
                _foldingProvider = new LspFoldingProvider(_client, _docSync);
                _editor.SetLspFoldingProvider(_foldingProvider);
            }

            // Request initial features
            _ = RequestInlayHintsAsync(linkedCt);
            _ = RequestCodeLensAsync(linkedCt);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Activation failed: {ex.Message}");
            await DeactivateAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Shuts down the current LSP session.
    /// </summary>
    public async Task DeactivateAsync()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        DismissCompletion();
        DismissHover();
        DismissSignatureHelp();

        if (_semanticHighlighter != null)
        {
            _semanticHighlighter.Detach();
            // Restore base highlighter
            if (_editor.SyntaxHighlighter == _semanticHighlighter)
                _editor.SyntaxHighlighter = null;
            _semanticHighlighter = null;
        }

        if (_foldingProvider != null)
        {
            _foldingProvider.ClearCache();
            _editor.ClearLspFoldingProvider();
            _foldingProvider = null;
        }

        if (_docSync != null)
        {
            await _docSync.CloseAsync().ConfigureAwait(false);
            _docSync.Dispose();
            _docSync = null;
        }

        if (_client != null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        _diagnostics = [];
        _diagnosticVersion++;
        DiagnosticsUpdated?.Invoke(_diagnostics);

        _inlayHints = [];
        InlayHintsUpdated?.Invoke(_inlayHints);

        _codeLenses = [];
        CodeLensesUpdated?.Invoke(_codeLenses);

        _activeLanguage = null;
    }

    private void OnConnectionLost()
    {
        _ = Task.Run(async () =>
        {
            // Attempt to reactivate after a brief delay
            await Task.Delay(2000).ConfigureAwait(false);
            if (_activeLanguage != null && !_disposed)
            {
                try
                {
                    await ActivateAsync(_activeLanguage).ConfigureAwait(false);
                }
                catch { /* give up */ }
            }
        });
    }

    private async Task<ApplyWorkspaceEditResult> OnApplyEditRequested(ApplyWorkspaceEditParams editParams)
    {
        if (WorkspaceEditRequested != null)
        {
            bool applied = await WorkspaceEditRequested(editParams.Edit).ConfigureAwait(false);
            return new ApplyWorkspaceEditResult { Applied = applied };
        }
        return new ApplyWorkspaceEditResult { Applied = false, FailureReason = "No handler" };
    }

    private Task<JsonElement[]> OnConfigurationRequested(ConfigurationParams configParams)
    {
        // Return empty config for each requested item
        var results = new JsonElement[configParams.Items.Length];
        for (int i = 0; i < results.Length; i++)
            results[i] = JsonSerializer.SerializeToElement(new { });
        return Task.FromResult(results);
    }

    #endregion

    #region Diagnostics

    private void OnDiagnosticsPublished(PublishDiagnosticsParams @params)
    {
        if (_docSync == null || @params.Uri != _docSync.Uri) return;

        _diagnostics = @params.Diagnostics;
        _diagnosticVersion++;
        DiagnosticsUpdated?.Invoke(_diagnostics);
    }

    #endregion

    #region Completion

    /// <summary>
    /// Triggers completion at the current caret position.
    /// </summary>
    public async Task TriggerCompletionAsync(CompletionTriggerKind triggerKind = CompletionTriggerKind.Invoked,
        string? triggerCharacter = null, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;

        if (!(_client.ServerCapabilities?.CompletionProvider != null)) return;

        _completionCts?.Cancel();
        _completionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _completionCts.Token;

        try
        {
            var caretOffset = _editor.CaretOffset;
            var position = _docSync.OffsetToPosition(caretOffset);

            var result = await _client.RequestCompletionAsync(new CompletionParams
            {
                TextDocument = _docSync.GetTextDocumentIdentifier(),
                Position = position,
                Context = new CompletionContext
                {
                    TriggerKind = triggerKind,
                    TriggerCharacter = triggerCharacter,
                },
            }, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                _activeCompletionList = result;
                _activeCompletionIndex = result?.Items.Length > 0 ? 0 : -1;
                _isCompletionActive = result?.Items.Length > 0;
                CompletionUpdated?.Invoke(result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Completion error: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves additional details for the specified completion item.
    /// </summary>
    public async Task<CompletionItem?> ResolveCompletionItemAsync(CompletionItem item, CancellationToken ct = default)
    {
        if (!IsActive || _client == null) return item;
        if (_client.ServerCapabilities?.CompletionProvider?.ResolveProvider != true) return item;

        try
        {
            return await _client.RequestCompletionResolveAsync(item, ct).ConfigureAwait(false) ?? item;
        }
        catch { return item; }
    }

    /// <summary>
    /// Applies the selected completion item.
    /// </summary>
    public void ApplyCompletion(CompletionItem item)
    {
        if (_docSync == null) return;

        string insertText = item.InsertText ?? item.Label;

        if (item.TextEdit != null)
        {
            var (start, end) = LspDocumentSync.LspRangeToOffsets(_editor.Document, item.TextEdit.Range);
            _editor.Document.Replace(start, end - start, item.TextEdit.NewText);
            _editor.CaretOffset = start + item.TextEdit.NewText.Length;
        }
        else
        {
            // Simple insertion: replace the current word prefix
            var caretOffset = _editor.CaretOffset;
            int wordStart = FindWordStart(caretOffset);
            _editor.Document.Replace(wordStart, caretOffset - wordStart, insertText);
            _editor.CaretOffset = wordStart + insertText.Length;
        }

        // Apply additional text edits
        if (item.AdditionalTextEdits != null)
        {
            ApplyTextEdits(item.AdditionalTextEdits);
        }

        DismissCompletion();
    }

    /// <summary>
    /// Dismisses the current completion popup.
    /// </summary>
    public void DismissCompletion()
    {
        _completionCts?.Cancel();
        _isCompletionActive = false;
        _activeCompletionList = null;
        _activeCompletionIndex = -1;
        CompletionUpdated?.Invoke(null);
    }

    #endregion

    #region Hover

    /// <summary>
    /// Requests hover information at the specified document offset.
    /// </summary>
    public async Task RequestHoverAsync(int offset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.HoverProvider)) return;

        _hoverCts?.Cancel();
        _hoverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _hoverCts.Token;

        try
        {
            var position = _docSync.OffsetToPosition(offset);
            var result = await _client.RequestHoverAsync(new HoverParams
            {
                TextDocument = _docSync.GetTextDocumentIdentifier(),
                Position = position,
            }, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                _activeHover = result;
                _activeHoverRange = result?.Range;
                HoverUpdated?.Invoke(result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Hover error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dismisses the current hover tooltip.
    /// </summary>
    public void DismissHover()
    {
        _hoverCts?.Cancel();
        _activeHover = null;
        _activeHoverRange = null;
        HoverUpdated?.Invoke(null);
    }

    #endregion

    #region Signature Help

    /// <summary>
    /// Requests signature help at the current caret position.
    /// </summary>
    public async Task RequestSignatureHelpAsync(SignatureHelpTriggerKind triggerKind = SignatureHelpTriggerKind.Invoked,
        string? triggerCharacter = null, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (_client.ServerCapabilities?.SignatureHelpProvider == null) return;

        _signatureHelpCts?.Cancel();
        _signatureHelpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _signatureHelpCts.Token;

        try
        {
            var position = _docSync.OffsetToPosition(_editor.CaretOffset);
            var result = await _client.RequestSignatureHelpAsync(new SignatureHelpParams
            {
                TextDocument = _docSync.GetTextDocumentIdentifier(),
                Position = position,
                Context = new SignatureHelpContext
                {
                    TriggerKind = triggerKind,
                    TriggerCharacter = triggerCharacter,
                    IsRetrigger = _activeSignatureHelp != null,
                    ActiveSignatureHelp = _activeSignatureHelp,
                },
            }, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                _activeSignatureHelp = result;
                SignatureHelpUpdated?.Invoke(result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Signature help error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dismisses the current signature help.
    /// </summary>
    public void DismissSignatureHelp()
    {
        _signatureHelpCts?.Cancel();
        _activeSignatureHelp = null;
        SignatureHelpUpdated?.Invoke(null);
    }

    #endregion

    #region Navigation

    /// <summary>
    /// Requests go-to-declaration at the specified offset.
    /// </summary>
    public Task GoToDeclarationAsync(int offset, CancellationToken ct = default)
        => NavigateAsync(offset, LspMethods.TextDocumentDeclaration, ct);

    /// <summary>
    /// Requests go-to-definition at the specified offset.
    /// </summary>
    public Task GoToDefinitionAsync(int offset, CancellationToken ct = default)
        => NavigateAsync(offset, LspMethods.TextDocumentDefinition, ct);

    /// <summary>
    /// Requests go-to-type-definition at the specified offset.
    /// </summary>
    public Task GoToTypeDefinitionAsync(int offset, CancellationToken ct = default)
        => NavigateAsync(offset, LspMethods.TextDocumentTypeDefinition, ct);

    /// <summary>
    /// Requests go-to-implementation at the specified offset.
    /// </summary>
    public Task GoToImplementationAsync(int offset, CancellationToken ct = default)
        => NavigateAsync(offset, LspMethods.TextDocumentImplementation, ct);

    /// <summary>
    /// Finds all references at the specified offset.
    /// </summary>
    public async Task<Location[]?> FindReferencesAsync(int offset, bool includeDeclaration = true, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.ReferencesProvider)) return null;

        var position = _docSync.OffsetToPosition(offset);
        return await _client.RequestReferencesAsync(new ReferenceParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = position,
            Context = new ReferenceContext { IncludeDeclaration = includeDeclaration },
        }, ct).ConfigureAwait(false);
    }

    private async Task NavigateAsync(int offset, string method, CancellationToken ct)
    {
        if (!IsActive || _docSync == null || _client == null) return;

        JsonElement? capability = method switch
        {
            LspMethods.TextDocumentDeclaration => _client.ServerCapabilities?.DeclarationProvider,
            LspMethods.TextDocumentDefinition => _client.ServerCapabilities?.DefinitionProvider,
            LspMethods.TextDocumentTypeDefinition => _client.ServerCapabilities?.TypeDefinitionProvider,
            LspMethods.TextDocumentImplementation => _client.ServerCapabilities?.ImplementationProvider,
            _ => null,
        };

        if (!LspClient.HasCapability(capability)) return;

        try
        {
            Location[]? result = method switch
            {
                LspMethods.TextDocumentDeclaration => await _client.RequestDeclarationAsync(
                    new DeclarationParams { TextDocument = _docSync.GetTextDocumentIdentifier(), Position = _docSync.OffsetToPosition(offset) }, ct).ConfigureAwait(false),
                LspMethods.TextDocumentDefinition => await _client.RequestDefinitionAsync(
                    new DefinitionParams { TextDocument = _docSync.GetTextDocumentIdentifier(), Position = _docSync.OffsetToPosition(offset) }, ct).ConfigureAwait(false),
                LspMethods.TextDocumentTypeDefinition => await _client.RequestTypeDefinitionAsync(
                    new TypeDefinitionParams { TextDocument = _docSync.GetTextDocumentIdentifier(), Position = _docSync.OffsetToPosition(offset) }, ct).ConfigureAwait(false),
                LspMethods.TextDocumentImplementation => await _client.RequestImplementationAsync(
                    new ImplementationParams { TextDocument = _docSync.GetTextDocumentIdentifier(), Position = _docSync.OffsetToPosition(offset) }, ct).ConfigureAwait(false),
                _ => null,
            };

            if (result != null && result.Length > 0)
            {
                NavigationRequested?.Invoke(result);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Navigation error: {ex.Message}");
        }
    }

    #endregion

    #region Document Highlight

    /// <summary>
    /// Requests document highlights at the specified offset.
    /// </summary>
    public async Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(int offset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.DocumentHighlightProvider)) return null;

        var position = _docSync.OffsetToPosition(offset);
        return await _client.RequestDocumentHighlightAsync(new DocumentHighlightParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = position,
        }, ct).ConfigureAwait(false);
    }

    #endregion

    #region Document Symbols

    /// <summary>
    /// Requests document symbols.
    /// </summary>
    public async Task<DocumentSymbol[]?> RequestDocumentSymbolsAsync(CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.DocumentSymbolProvider)) return null;

        return await _client.RequestDocumentSymbolAsync(new DocumentSymbolParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
        }, ct).ConfigureAwait(false);
    }

    #endregion

    #region Code Actions

    /// <summary>
    /// Requests code actions for the specified range.
    /// </summary>
    public async Task<CodeAction[]?> RequestCodeActionsAsync(int startOffset, int endOffset,
        Diagnostic[]? diagnostics = null, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.CodeActionProvider)) return null;

        var startPos = _docSync.OffsetToPosition(startOffset);
        var endPos = _docSync.OffsetToPosition(endOffset);

        return await _client.RequestCodeActionAsync(new CodeActionParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Range = new LspRange(startPos, endPos),
            Context = new CodeActionContext
            {
                Diagnostics = diagnostics != null ? [.. diagnostics] : [],
            },
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a code action (either its edit or resolves it first).
    /// </summary>
    public async Task ApplyCodeActionAsync(CodeAction action, CancellationToken ct = default)
    {
        if (!IsActive || _client == null) return;

        if (action.Edit == null && action.Data.HasValue)
        {
            // Resolve the code action to get its edit
            var resolved = await _client.RequestCodeActionResolveAsync(action, ct).ConfigureAwait(false);
            if (resolved?.Edit != null)
                action = resolved;
        }

        if (action.Edit != null)
        {
            await ApplyWorkspaceEditAsync(action.Edit).ConfigureAwait(false);
        }

        if (action.Command != null)
        {
            await _client.RequestExecuteCommandAsync(action.Command.Command,
                action.Command.Arguments?.ToArray(), ct).ConfigureAwait(false);
        }
    }

    #endregion

    #region Formatting

    /// <summary>
    /// Formats the entire document.
    /// </summary>
    public async Task FormatDocumentAsync(CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.DocumentFormattingProvider)) return;

        var edits = await _client.RequestFormattingAsync(new DocumentFormattingParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Options = GetFormattingOptions(),
        }, ct).ConfigureAwait(false);

        if (edits != null)
            ApplyTextEdits(edits);
    }

    /// <summary>
    /// Formats the selected range.
    /// </summary>
    public async Task FormatRangeAsync(int startOffset, int endOffset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.DocumentRangeFormattingProvider)) return;

        var edits = await _client.RequestRangeFormattingAsync(new DocumentRangeFormattingParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Range = new LspRange(_docSync.OffsetToPosition(startOffset), _docSync.OffsetToPosition(endOffset)),
            Options = GetFormattingOptions(),
        }, ct).ConfigureAwait(false);

        if (edits != null)
            ApplyTextEdits(edits);
    }

    /// <summary>
    /// On-type formatting.
    /// </summary>
    public async Task FormatOnTypeAsync(int offset, string ch, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        var onType = _client.ServerCapabilities?.DocumentOnTypeFormattingProvider;
        if (onType == null) return;

        // Check if ch is a trigger character
        bool isTrigger = ch == onType.FirstTriggerCharacter;
        if (!isTrigger && onType.MoreTriggerCharacter != null)
            isTrigger = Array.IndexOf(onType.MoreTriggerCharacter, ch) >= 0;
        if (!isTrigger) return;

        var edits = await _client.RequestOnTypeFormattingAsync(new DocumentOnTypeFormattingParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = _docSync.OffsetToPosition(offset),
            Ch = ch,
            Options = GetFormattingOptions(),
        }, ct).ConfigureAwait(false);

        if (edits != null)
            ApplyTextEdits(edits);
    }

    #endregion

    #region Rename

    /// <summary>
    /// Checks if rename is available at the specified offset and returns the range/placeholder.
    /// </summary>
    public async Task<PrepareRenameResult?> PrepareRenameAsync(int offset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.RenameProvider)) return null;

        return await _client.RequestPrepareRenameAsync(new PrepareRenameParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = _docSync.OffsetToPosition(offset),
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a rename at the specified offset.
    /// </summary>
    public async Task RenameAsync(int offset, string newName, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.RenameProvider)) return;

        var edit = await _client.RequestRenameAsync(new RenameParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = _docSync.OffsetToPosition(offset),
            NewName = newName,
        }, ct).ConfigureAwait(false);

        if (edit != null)
            await ApplyWorkspaceEditAsync(edit).ConfigureAwait(false);
    }

    #endregion

    #region Inlay Hints

    /// <summary>
    /// Requests inlay hints for the visible range.
    /// </summary>
    public async Task RequestInlayHintsAsync(CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.InlayHintProvider)) return;

        _inlayHintCts?.Cancel();
        _inlayHintCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _inlayHintCts.Token;

        try
        {
            // Request hints for the full document
            var endLine = _editor.Document.LineCount;
            var result = await _client.RequestInlayHintAsync(new InlayHintParams
            {
                TextDocument = _docSync.GetTextDocumentIdentifier(),
                Range = new LspRange(
                    new LspPosition(0, 0),
                    new LspPosition(endLine, 0)),
            }, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                _inlayHints = result ?? [];
                InlayHintsUpdated?.Invoke(_inlayHints);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Inlay hints error: {ex.Message}");
        }
    }

    #endregion

    #region Code Lens

    /// <summary>
    /// Requests code lenses for the document.
    /// </summary>
    public async Task RequestCodeLensAsync(CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return;
        if (_client.ServerCapabilities?.CodeLensProvider == null) return;

        _codeLensCts?.Cancel();
        _codeLensCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _codeLensCts.Token;

        try
        {
            var result = await _client.RequestCodeLensAsync(new CodeLensParams
            {
                TextDocument = _docSync.GetTextDocumentIdentifier(),
            }, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                // Resolve each code lens if the server supports it
                if (result != null && _client.ServerCapabilities.CodeLensProvider.ResolveProvider == true)
                {
                    for (int i = 0; i < result.Length; i++)
                    {
                        if (result[i].Command == null)
                        {
                            try
                            {
                                var resolved = await _client.RequestCodeLensResolveAsync(result[i], token).ConfigureAwait(false);
                                if (resolved != null) result[i] = resolved;
                            }
                            catch { /* skip failed resolve */ }
                        }
                    }
                }

                _codeLenses = result ?? [];
                CodeLensesUpdated?.Invoke(_codeLenses);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Code lens error: {ex.Message}");
        }
    }

    #endregion

    #region Call/Type Hierarchy

    /// <summary>
    /// Prepares a call hierarchy at the specified offset.
    /// </summary>
    public async Task<CallHierarchyItem[]?> PrepareCallHierarchyAsync(int offset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.CallHierarchyProvider)) return null;

        return await _client.RequestPrepareCallHierarchyAsync(new CallHierarchyPrepareParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = _docSync.OffsetToPosition(offset),
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets incoming calls for a call hierarchy item.
    /// </summary>
    public async Task<CallHierarchyIncomingCall[]?> GetIncomingCallsAsync(CallHierarchyItem item, CancellationToken ct = default)
    {
        if (!IsActive || _client == null) return null;
        return await _client.RequestIncomingCallsAsync(new CallHierarchyIncomingCallsParams { Item = item }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets outgoing calls for a call hierarchy item.
    /// </summary>
    public async Task<CallHierarchyOutgoingCall[]?> GetOutgoingCallsAsync(CallHierarchyItem item, CancellationToken ct = default)
    {
        if (!IsActive || _client == null) return null;
        return await _client.RequestOutgoingCallsAsync(new CallHierarchyOutgoingCallsParams { Item = item }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Prepares a type hierarchy at the specified offset.
    /// </summary>
    public async Task<TypeHierarchyItem[]?> PrepareTypeHierarchyAsync(int offset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.TypeHierarchyProvider)) return null;

        return await _client.RequestPrepareTypeHierarchyAsync(new TypeHierarchyPrepareParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = _docSync.OffsetToPosition(offset),
        }, ct).ConfigureAwait(false);
    }

    #endregion

    #region Selection Range

    /// <summary>
    /// Requests smart selection ranges for the given positions.
    /// </summary>
    public async Task<SelectionRange[]?> RequestSelectionRangeAsync(int[] offsets, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.SelectionRangeProvider)) return null;

        var positions = new LspPosition[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
            positions[i] = _docSync.OffsetToPosition(offsets[i]);

        return await _client.RequestSelectionRangeAsync(new SelectionRangeParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Positions = positions,
        }, ct).ConfigureAwait(false);
    }

    #endregion

    #region Linked Editing

    /// <summary>
    /// Requests linked editing ranges at the specified offset.
    /// </summary>
    public async Task<LinkedEditingRanges?> RequestLinkedEditingRangeAsync(int offset, CancellationToken ct = default)
    {
        if (!IsActive || _docSync == null || _client == null) return null;
        if (!LspClient.HasCapability(_client.ServerCapabilities?.LinkedEditingRangeProvider)) return null;

        return await _client.RequestLinkedEditingRangeAsync(new LinkedEditingRangeParams
        {
            TextDocument = _docSync.GetTextDocumentIdentifier(),
            Position = _docSync.OffsetToPosition(offset),
        }, ct).ConfigureAwait(false);
    }

    #endregion

    #region Document Save

    /// <summary>
    /// Notifies the server that the document was saved.
    /// </summary>
    public async Task NotifyDocumentSavedAsync(CancellationToken ct = default)
    {
        if (_docSync != null)
            await _docSync.NotifySaveAsync(ct).ConfigureAwait(false);

        // Refresh features after save
        _ = RequestInlayHintsAsync(ct);
        _ = RequestCodeLensAsync(ct);
        if (_foldingProvider != null)
            _ = _foldingProvider.RefreshAsync(ct);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Gets the TextDocument from the editor.
    /// </summary>
    internal TextDocument Document => _editor.Document;

    private FormattingOptions GetFormattingOptions()
    {
        return new FormattingOptions
        {
            TabSize = _editor.TabSize,
            InsertSpaces = _editor.ConvertTabsToSpaces,
            TrimTrailingWhitespace = true,
            InsertFinalNewline = true,
            TrimFinalNewlines = true,
        };
    }

    /// <summary>
    /// Applies a set of LSP text edits to the current document.
    /// Edits are applied in reverse document order to maintain correct offsets.
    /// </summary>
    private void ApplyTextEdits(TextEdit[] edits)
    {
        if (edits.Length == 0) return;

        // Sort edits by position, descending (apply from end of document first)
        var sorted = edits.OrderByDescending(e => e.Range.Start.Line)
                          .ThenByDescending(e => e.Range.Start.Character)
                          .ToArray();

        _editor.Document.BeginUpdate();
        try
        {
            foreach (var edit in sorted)
            {
                var (start, end) = LspDocumentSync.LspRangeToOffsets(_editor.Document, edit.Range);
                _editor.Document.Replace(start, end - start, edit.NewText);
            }
        }
        finally
        {
            _editor.Document.EndUpdate();
        }
    }

    /// <summary>
    /// Applies a WorkspaceEdit (only handles changes to the current document for now).
    /// </summary>
    private async Task ApplyWorkspaceEditAsync(WorkspaceEdit edit)
    {
        if (_docSync == null) return;

        // Handle simple changes
        if (edit.Changes != null && edit.Changes.TryGetValue(_docSync.Uri, out var edits))
        {
            ApplyTextEdits(edits.ToArray());
        }

        // Handle document changes
        if (edit.DocumentChanges != null)
        {
            foreach (var docEdit in edit.DocumentChanges)
            {
                if (docEdit.TextDocument.Uri == _docSync.Uri)
                {
                    ApplyTextEdits(docEdit.Edits.ToArray());
                }
            }
        }

        // Delegate multi-file edits to the host
        if (WorkspaceEditRequested != null)
        {
            await WorkspaceEditRequested(edit).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finds the start of the word at the given offset (for completion insertion).
    /// </summary>
    private int FindWordStart(int offset)
    {
        var text = _editor.Document.Text;
        int i = offset - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            i--;
        return i + 1;
    }

    #endregion

    #region Dispose

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await DeactivateAsync().ConfigureAwait(false);
    }

    #endregion
}
