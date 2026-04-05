// IReactiveSyntaxHighlighter implementation that uses LSP semantic tokens.

using System.Text.Json;
using Jalium.UI.Controls.Editor.LanguageServer.Protocol;

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// Syntax highlighter that uses LSP semantic tokens for rich, server-driven highlighting.
/// Falls back to a base highlighter for non-semantic token lines.
/// </summary>
internal sealed class LspSemanticHighlighter : IReactiveSyntaxHighlighter
{
    private readonly LspClient _client;
    private readonly LspDocumentSync _docSync;
    private readonly ISyntaxHighlighter? _baseHighlighter;
    private TextDocument? _document;
    private Func<string?>? _filePathProvider;

    // Semantic token data
    private int[]? _tokenData;
    private string? _lastResultId;
    private string[] _tokenTypes = [];
    private string[] _tokenModifiers = [];

    // Cached per-line tokens
    private readonly Dictionary<int, SyntaxToken[]> _lineTokens = [];
    private int _lastTokenVersion = -1;

    private CancellationTokenSource? _refreshCts;
    private bool _attached;

    public event EventHandler<SyntaxHighlightInvalidatedEventArgs>? HighlightingInvalidated;

    public LspSemanticHighlighter(LspClient client, LspDocumentSync docSync, ISyntaxHighlighter? baseHighlighter = null)
    {
        _client = client;
        _docSync = docSync;
        _baseHighlighter = baseHighlighter;
    }

    public void Attach(TextDocument document, Func<string?> filePathProvider)
    {
        _document = document;
        _filePathProvider = filePathProvider;
        _attached = true;

        // Initialize token type legend from server capabilities
        var options = _client.SemanticTokensOptions;
        if (options != null)
        {
            _tokenTypes = options.Legend.TokenTypes;
            _tokenModifiers = options.Legend.TokenModifiers;
        }

        // Initial refresh
        _ = RefreshTokensAsync();
    }

    public void NotifyDocumentChanged(TextChangeEventArgs change)
    {
        // Debounce: cancel previous refresh
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        // Schedule a delayed refresh
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                    await RefreshTokensAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    public void Detach()
    {
        _attached = false;
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
        _tokenData = null;
        _lineTokens.Clear();
        _document = null;
    }

    public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(
        int lineNumber, string lineText, object? stateAtLineStart)
    {
        // Always run the base highlighter to propagate state (e.g., Razor brace depth,
        // multi-line comments/strings). Even when LSP tokens override the visual output,
        // the state must flow correctly so that subsequent lines are highlighted properly.
        object? stateAtLineEnd = stateAtLineStart;
        SyntaxToken[]? baseTokens = null;
        if (_baseHighlighter != null)
        {
            var baseResult = _baseHighlighter.HighlightLine(lineNumber, lineText, stateAtLineStart);
            baseTokens = baseResult.tokens;
            stateAtLineEnd = baseResult.stateAtLineEnd;
        }

        // If we have semantic tokens for this line, use them with the propagated state
        if (_lineTokens.TryGetValue(lineNumber, out var semanticTokens))
            return (semanticTokens, stateAtLineEnd);

        // Otherwise use base highlighter tokens
        return (baseTokens ?? [], stateAtLineEnd);
    }

    public object? GetInitialState()
    {
        return _baseHighlighter?.GetInitialState();
    }

    private async Task RefreshTokensAsync(CancellationToken ct = default)
    {
        if (!_attached || !_client.IsInitialized || !_docSync.IsOpen) return;

        try
        {
            var options = _client.SemanticTokensOptions;
            if (options == null) return;

            int[]? newData = null;

            // Try delta first if we have a previous result
            if (_lastResultId != null && options.Full is JsonElement fullEl &&
                fullEl.ValueKind == JsonValueKind.Object &&
                fullEl.TryGetProperty("delta", out var deltaProp) &&
                deltaProp.GetBoolean())
            {
                var deltaResult = await _client.RequestSemanticTokensDeltaAsync(new SemanticTokensDeltaParams
                {
                    TextDocument = _docSync.GetTextDocumentIdentifier(),
                    PreviousResultId = _lastResultId,
                }, ct).ConfigureAwait(false);

                if (deltaResult is SemanticTokensDelta delta)
                {
                    newData = ApplyDelta(_tokenData ?? [], delta.Edits);
                    _lastResultId = delta.ResultId;
                }
                else if (deltaResult is SemanticTokens fullTokens)
                {
                    newData = fullTokens.Data;
                    _lastResultId = fullTokens.ResultId;
                }
            }

            // Fall back to full request
            if (newData == null)
            {
                var result = await _client.RequestSemanticTokensFullAsync(new SemanticTokensParams
                {
                    TextDocument = _docSync.GetTextDocumentIdentifier(),
                }, ct).ConfigureAwait(false);

                if (result != null)
                {
                    newData = result.Data;
                    _lastResultId = result.ResultId;
                }
            }

            if (newData != null && !ct.IsCancellationRequested)
            {
                _tokenData = newData;
                DecodeTokens(newData);
                HighlightingInvalidated?.Invoke(this, SyntaxHighlightInvalidatedEventArgs.WholeDocument);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Semantic tokens refresh error: {ex.Message}");
        }
    }

    private void DecodeTokens(int[] data)
    {
        _lineTokens.Clear();

        if (data.Length < 5) return;

        // Semantic tokens are encoded as: [deltaLine, deltaStartChar, length, tokenType, tokenModifiers] x N
        int line = 0;
        int startChar = 0;

        var lineTokensList = new Dictionary<int, List<SyntaxToken>>();

        for (int i = 0; i + 4 < data.Length; i += 5)
        {
            int deltaLine = data[i];
            int deltaStartChar = data[i + 1];
            int length = data[i + 2];
            int tokenType = data[i + 3];
            // int tokenModifiers = data[i + 4]; // bitmask, used for styling

            if (deltaLine > 0)
            {
                line += deltaLine;
                startChar = deltaStartChar;
            }
            else
            {
                startChar += deltaStartChar;
            }

            int lineNumber = line + 1; // Convert to 1-based

            var classification = MapTokenTypeToClassification(tokenType);

            if (!lineTokensList.TryGetValue(lineNumber, out var tokens))
            {
                tokens = [];
                lineTokensList[lineNumber] = tokens;
            }

            tokens.Add(new SyntaxToken(startChar, length, classification));
        }

        foreach (var (lineNum, tokens) in lineTokensList)
        {
            _lineTokens[lineNum] = tokens.ToArray();
        }
    }

    private TokenClassification MapTokenTypeToClassification(int tokenTypeIndex)
    {
        if (tokenTypeIndex < 0 || tokenTypeIndex >= _tokenTypes.Length)
            return TokenClassification.PlainText;

        return _tokenTypes[tokenTypeIndex] switch
        {
            SemanticTokenTypes.Namespace => TokenClassification.Namespace,
            SemanticTokenTypes.Type or SemanticTokenTypes.Class or SemanticTokenTypes.Struct or
            SemanticTokenTypes.Enum or SemanticTokenTypes.Interface => TokenClassification.TypeName,
            SemanticTokenTypes.TypeParameter => TokenClassification.TypeName,
            SemanticTokenTypes.Parameter => TokenClassification.Parameter,
            SemanticTokenTypes.Variable => TokenClassification.LocalVariable,
            SemanticTokenTypes.Property => TokenClassification.Property,
            SemanticTokenTypes.EnumMember => TokenClassification.Field,
            SemanticTokenTypes.Event => TokenClassification.Identifier,
            SemanticTokenTypes.Function or SemanticTokenTypes.Method => TokenClassification.Method,
            SemanticTokenTypes.Macro => TokenClassification.Preprocessor,
            SemanticTokenTypes.Keyword or SemanticTokenTypes.Modifier => TokenClassification.Keyword,
            SemanticTokenTypes.Comment => TokenClassification.Comment,
            SemanticTokenTypes.String => TokenClassification.String,
            SemanticTokenTypes.Number => TokenClassification.Number,
            SemanticTokenTypes.Regexp => TokenClassification.String,
            SemanticTokenTypes.Operator => TokenClassification.Operator,
            SemanticTokenTypes.Decorator => TokenClassification.Attribute,
            _ => TokenClassification.PlainText,
        };
    }

    private static int[] ApplyDelta(int[] current, SemanticTokensEdit[] edits)
    {
        var result = new List<int>(current);
        // Apply edits in reverse order to maintain correct offsets
        var sorted = edits.OrderByDescending(e => e.Start).ToArray();
        foreach (var edit in sorted)
        {
            int start = edit.Start;
            int deleteCount = edit.DeleteCount;
            int removeEnd = Math.Min(start + deleteCount, result.Count);
            if (removeEnd > start)
                result.RemoveRange(start, removeEnd - start);
            if (edit.Data != null)
                result.InsertRange(start, edit.Data);
        }
        return result.ToArray();
    }
}
