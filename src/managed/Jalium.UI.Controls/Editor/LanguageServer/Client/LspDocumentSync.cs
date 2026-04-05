// Bridges the EditControl document model with LSP text synchronization notifications.

using Jalium.UI.Controls.Editor.LanguageServer.Protocol;

namespace Jalium.UI.Controls.Editor.LanguageServer.Client;

/// <summary>
/// Manages the synchronization of a <see cref="TextDocument"/> with an LSP server.
/// Tracks document versions, converts change events to LSP notifications,
/// and handles open/close/save lifecycle.
/// </summary>
internal sealed class LspDocumentSync : IDisposable
{
    private readonly LspClient _client;
    private TextDocument? _document;
    private string _uri = string.Empty;
    private string _languageId = "plaintext";
    private int _version;
    private bool _isOpen;
    private bool _disposed;
    private Func<string?>? _filePathProvider;

    public LspDocumentSync(LspClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the current document URI.
    /// </summary>
    public string Uri => _uri;

    /// <summary>
    /// Gets the current document version.
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// Gets whether the document is currently open on the server.
    /// </summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Attaches to a document and opens it on the server.
    /// </summary>
    public async Task OpenAsync(TextDocument document, string languageId, Func<string?> filePathProvider, CancellationToken ct = default)
    {
        if (_disposed) return;

        // Close previous if open
        if (_isOpen)
            await CloseAsync(ct).ConfigureAwait(false);

        _document = document;
        _languageId = languageId;
        _filePathProvider = filePathProvider;
        _version = 1;

        var filePath = filePathProvider();
        _uri = FilePathToUri(filePath ?? "untitled:Untitled");

        _document.Changed += OnDocumentChanged;

        if (!_client.IsInitialized) return;

        await _client.NotifyDidOpenAsync(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = _uri,
                LanguageId = _languageId,
                Version = _version,
                Text = _document.Text,
            }
        }, ct).ConfigureAwait(false);

        _isOpen = true;
    }

    /// <summary>
    /// Closes the document on the server.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (!_isOpen || _document == null) return;

        _document.Changed -= OnDocumentChanged;

        if (_client.IsInitialized)
        {
            try
            {
                await _client.NotifyDidCloseAsync(new DidCloseTextDocumentParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = _uri }
                }, ct).ConfigureAwait(false);
            }
            catch { /* server may already be dead */ }
        }

        _isOpen = false;
        _document = null;
    }

    /// <summary>
    /// Notifies the server that the document was saved.
    /// </summary>
    public async Task NotifySaveAsync(CancellationToken ct = default)
    {
        if (!_isOpen || !_client.IsInitialized || _document == null) return;

        await _client.NotifyDidSaveAsync(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = _uri },
            Text = _client.SaveIncludesText ? _document.Text : null,
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Called when the document content changes. Sends didChange notifications.
    /// </summary>
    private void OnDocumentChanged(object? sender, TextChangeEventArgs e)
    {
        if (!_isOpen || !_client.IsInitialized || _document == null) return;

        _version++;

        TextDocumentContentChangeEvent[] changes;

        if (_client.SyncKind == TextDocumentSyncKind.Incremental)
        {
            // Convert TextChangeEventArgs to incremental LSP change
            var startPos = OffsetToLspPosition(_document, e.Offset);
            var endPos = OffsetToLspPosition(_document, e.Offset + e.Change.RemovalLength, e.RemovedText);
            changes =
            [
                new TextDocumentContentChangeEvent
                {
                    Range = new LspRange(startPos, endPos),
                    RangeLength = e.Change.RemovalLength,
                    Text = e.InsertedText ?? string.Empty,
                }
            ];
        }
        else
        {
            // Full sync
            changes =
            [
                new TextDocumentContentChangeEvent
                {
                    Text = _document.Text,
                }
            ];
        }

        var @params = new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier
            {
                Uri = _uri,
                Version = _version,
            },
            ContentChanges = changes,
        };

        // Fire-and-forget — we don't await notification sends on the UI thread
        _ = _client.NotifyDidChangeAsync(@params);
    }

    /// <summary>
    /// Converts a document offset to an LSP Position.
    /// For the "before change" position (when text was removed), pass removedText to calculate
    /// the end position as if the text still existed.
    /// </summary>
    private static LspPosition OffsetToLspPosition(TextDocument document, int offset, string? removedText = null)
    {
        if (removedText != null)
        {
            // For the end position of a removal, we need to compute where the removed text ended
            // relative to the start position. We calculate this from the removed text itself.
            var startPos = OffsetToLspPosition(document, offset);
            int line = startPos.Line;
            int character = startPos.Character;
            foreach (char c in removedText)
            {
                if (c == '\n')
                {
                    line++;
                    character = 0;
                }
                else if (c != '\r')
                {
                    character++;
                }
            }
            return new LspPosition(line, character);
        }

        // Clamp to valid range
        offset = Math.Clamp(offset, 0, document.TextLength);
        if (document.TextLength == 0)
            return new LspPosition(0, 0);

        var docLine = document.GetLineByOffset(Math.Min(offset, document.TextLength - 1));
        int lineStart = docLine.Offset;
        return new LspPosition(docLine.LineNumber - 1, offset - lineStart);
    }

    /// <summary>
    /// Converts an LSP Position to a document offset.
    /// </summary>
    public static int LspPositionToOffset(TextDocument document, LspPosition position)
    {
        int lineNumber = position.Line + 1; // LSP is 0-based, document is 1-based
        if (lineNumber < 1) return 0;
        if (lineNumber > document.LineCount) return document.TextLength;

        var line = document.GetLineByNumber(lineNumber);
        return Math.Min(line.Offset + position.Character, line.EndOffset);
    }

    /// <summary>
    /// Converts an LSP Range to a (startOffset, endOffset) pair.
    /// </summary>
    public static (int start, int end) LspRangeToOffsets(TextDocument document, LspRange range)
    {
        return (LspPositionToOffset(document, range.Start), LspPositionToOffset(document, range.End));
    }

    /// <summary>
    /// Gets a TextDocumentIdentifier for the current document.
    /// </summary>
    public TextDocumentIdentifier GetTextDocumentIdentifier()
        => new() { Uri = _uri };

    /// <summary>
    /// Gets a VersionedTextDocumentIdentifier for the current document.
    /// </summary>
    public VersionedTextDocumentIdentifier GetVersionedIdentifier()
        => new() { Uri = _uri, Version = _version };

    /// <summary>
    /// Creates an LspPosition from a document offset using the current document.
    /// </summary>
    public LspPosition OffsetToPosition(int offset)
    {
        if (_document == null) return new LspPosition(0, 0);
        return OffsetToLspPosition(_document, offset);
    }

    /// <summary>
    /// Creates a document offset from an LspPosition using the current document.
    /// </summary>
    public int PositionToOffset(LspPosition position)
    {
        if (_document == null) return 0;
        return LspPositionToOffset(_document, position);
    }

    /// <summary>
    /// Converts a local file path to a URI suitable for LSP.
    /// </summary>
    public static string FilePathToUri(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return "untitled:Untitled";

        if (filePath.StartsWith("untitled:"))
            return filePath;

        // Normalize path separators
        filePath = filePath.Replace('\\', '/');

        // Ensure drive letter starts with /
        if (filePath.Length >= 2 && filePath[1] == ':')
            filePath = "/" + filePath;

        return "file://" + System.Uri.EscapeDataString(filePath)
            .Replace("%2F", "/")
            .Replace("%3A", ":");
    }

    /// <summary>
    /// Converts an LSP URI back to a local file path.
    /// </summary>
    public static string UriToFilePath(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return string.Empty;
        if (!uri.StartsWith("file://")) return uri;

        var path = System.Uri.UnescapeDataString(uri.Substring("file://".Length));
        // Remove leading / on Windows paths (e.g., /C:/...)
        if (path.Length >= 3 && path[0] == '/' && path[2] == ':')
            path = path.Substring(1);

        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_document != null)
            _document.Changed -= OnDocumentChanged;

        _isOpen = false;
        _document = null;
    }
}
