// IFoldingStrategy implementation using LSP folding ranges.

using Jalium.UI.Controls.Editor.LanguageServer.Protocol;

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// Folding strategy that requests folding ranges from an LSP server.
/// Caches the last set of ranges and updates them asynchronously.
/// </summary>
internal sealed class LspFoldingProvider : IFoldingStrategy
{
    private readonly LspClient _client;
    private readonly LspDocumentSync _docSync;
    private FoldingRange[]? _cachedRanges;
    private CancellationTokenSource? _refreshCts;

    public LspFoldingProvider(LspClient client, LspDocumentSync docSync)
    {
        _client = client;
        _docSync = docSync;
    }

    /// <summary>
    /// Event raised when new folding ranges are available from the server.
    /// </summary>
    public event Action? FoldingRangesUpdated;

    /// <summary>
    /// Creates foldings from the last cached LSP folding ranges.
    /// Call <see cref="RefreshAsync"/> to fetch new ranges from the server.
    /// </summary>
    public IEnumerable<FoldingSection> CreateFoldings(TextDocument document)
    {
        var ranges = _cachedRanges;
        if (ranges == null || ranges.Length == 0)
            yield break;

        foreach (var range in ranges)
        {
            int startLine = range.StartLine + 1; // LSP is 0-based, FoldingSection is 1-based
            int endLine = range.EndLine + 1;

            if (startLine >= endLine || startLine < 1 || endLine > document.LineCount)
                continue;

            string title = range.CollapsedText ?? "...";

            // Compute start column from the first non-whitespace character
            int startColumn = 0;
            if (startLine <= document.LineCount)
            {
                var line = document.GetLineByNumber(startLine);
                string lineText = document.GetText(line.Offset, line.Length);
                for (int i = 0; i < lineText.Length; i++)
                {
                    if (!char.IsWhiteSpace(lineText[i]))
                    {
                        startColumn = i;
                        break;
                    }
                }
            }

            yield return new FoldingSection(startLine, endLine, title, startColumn);
        }
    }

    /// <summary>
    /// Requests folding ranges from the server and updates the cache.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!_client.IsInitialized || !_docSync.IsOpen) return;

        if (!LspClient.HasCapability(_client.ServerCapabilities?.FoldingRangeProvider))
            return;

        _refreshCts?.Cancel();
        _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _refreshCts.Token;

        try
        {
            var ranges = await _client.RequestFoldingRangeAsync(new FoldingRangeParams
            {
                TextDocument = _docSync.GetTextDocumentIdentifier(),
            }, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested)
            {
                _cachedRanges = ranges;
                FoldingRangesUpdated?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP] Folding range error: {ex.Message}");
        }
    }

    public void ClearCache()
    {
        _cachedRanges = null;
        _refreshCts?.Cancel();
    }
}
