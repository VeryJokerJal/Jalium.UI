using System.Printing;
using Jalium.UI.Documents;
using Jalium.UI.Documents.Serialization;
using Jalium.UI.Media;
using PrintTicket = System.Printing.PrintTicket;
using PrintTicketLevel = Jalium.UI.Xps.Serialization.PrintTicketLevel;

namespace Jalium.UI.Xps;

/// <summary>
/// Writes visuals and fixed documents to a print queue.
/// </summary>
public class XpsDocumentWriter : SerializerWriter
{
    private readonly PrintQueue _printQueue;
    private readonly PlatformXpsDocumentWriter _platformWriter;
    private readonly object _asyncGate = new();
    private CancellationTokenSource? _activeWrite;

    internal XpsDocumentWriter(PrintQueue printQueue)
    {
        _printQueue = printQueue ?? throw new ArgumentNullException(nameof(printQueue));
        _platformWriter = new PlatformXpsDocumentWriter(printQueue);
    }

    public override event WritingCancelledEventHandler? WritingCancelled;

    public override event WritingCompletedEventHandler? WritingCompleted;

    public override event WritingPrintTicketRequiredEventHandler? WritingPrintTicketRequired;

    public override event WritingProgressChangedEventHandler? WritingProgressChanged;

    public override void Write(Visual visual) => Write(visual, printTicket: null!);

    public override void Write(Visual visual, PrintTicket printTicket)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ExecuteSynchronously(() => WriteVisualCore(visual, printTicket, state: null), state: null);
    }

    public override void Write(DocumentPaginator documentPaginator) =>
        Write(documentPaginator, printTicket: null!);

    public override void Write(DocumentPaginator documentPaginator, PrintTicket printTicket)
    {
        ArgumentNullException.ThrowIfNull(documentPaginator);
        ExecuteSynchronously(
            () => WritePaginatorCore(documentPaginator, printTicket, state: null),
            state: null);
    }

    public override void Write(FixedPage fixedPage) => Write(fixedPage, printTicket: null!);

    public override void Write(FixedPage fixedPage, PrintTicket printTicket)
    {
        ArgumentNullException.ThrowIfNull(fixedPage);
        Write((Visual)fixedPage, printTicket);
    }

    public override void Write(FixedDocument fixedDocument) =>
        Write(fixedDocument, printTicket: null!);

    public override void Write(FixedDocument fixedDocument, PrintTicket printTicket)
    {
        ArgumentNullException.ThrowIfNull(fixedDocument);
        Write(fixedDocument.DocumentPaginator, printTicket);
    }

    public override void Write(FixedDocumentSequence fixedDocumentSequence) =>
        Write(fixedDocumentSequence, printTicket: null!);

    public override void Write(FixedDocumentSequence fixedDocumentSequence, PrintTicket printTicket)
    {
        ArgumentNullException.ThrowIfNull(fixedDocumentSequence);
        Write(fixedDocumentSequence.DocumentPaginator, printTicket);
    }

    /// <summary>
    /// Submits an existing XPS document path to the configured print queue.
    /// </summary>
    public void Write(string documentPath) =>
        Write(documentPath, XpsDocumentNotificationLevel.None);

    /// <summary>
    /// Submits an existing XPS document path to the configured print queue.
    /// </summary>
    public void Write(string documentPath, XpsDocumentNotificationLevel notificationLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ValidateNotificationLevel(notificationLevel);
        throw new NotSupportedException(
            "Submitting an existing XPS package is not supported by this print backend.");
    }

    public override void WriteAsync(Visual visual) =>
        WriteAsync(visual, printTicket: null!, userState: null);

    public override void WriteAsync(Visual visual, object? userState) =>
        WriteAsync(visual, printTicket: null!, userState);

    public override void WriteAsync(Visual visual, PrintTicket printTicket) =>
        WriteAsync(visual, printTicket, userState: null);

    public override void WriteAsync(Visual visual, PrintTicket printTicket, object? userState)
    {
        ArgumentNullException.ThrowIfNull(visual);
        QueueAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            WriteVisualCore(visual, printTicket, userState);
        }, userState);
    }

    public override void WriteAsync(DocumentPaginator documentPaginator) =>
        WriteAsync(documentPaginator, printTicket: null!, userState: null);

    public override void WriteAsync(DocumentPaginator documentPaginator, object? userState) =>
        WriteAsync(documentPaginator, printTicket: null!, userState);

    public override void WriteAsync(DocumentPaginator documentPaginator, PrintTicket printTicket) =>
        WriteAsync(documentPaginator, printTicket, userState: null);

    public override void WriteAsync(
        DocumentPaginator documentPaginator,
        PrintTicket printTicket,
        object? userState)
    {
        ArgumentNullException.ThrowIfNull(documentPaginator);
        QueueAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            WritePaginatorCore(documentPaginator, printTicket, userState);
        }, userState);
    }

    public override void WriteAsync(FixedPage fixedPage) =>
        WriteAsync(fixedPage, printTicket: null!, userState: null);

    public override void WriteAsync(FixedPage fixedPage, object? userState) =>
        WriteAsync(fixedPage, printTicket: null!, userState);

    public override void WriteAsync(FixedPage fixedPage, PrintTicket printTicket) =>
        WriteAsync(fixedPage, printTicket, userState: null);

    public override void WriteAsync(
        FixedPage fixedPage,
        PrintTicket printTicket,
        object? userState)
    {
        ArgumentNullException.ThrowIfNull(fixedPage);
        WriteAsync((Visual)fixedPage, printTicket, userState);
    }

    public override void WriteAsync(FixedDocument fixedDocument) =>
        WriteAsync(fixedDocument, printTicket: null!, userState: null);

    public override void WriteAsync(FixedDocument fixedDocument, object? userState) =>
        WriteAsync(fixedDocument, printTicket: null!, userState);

    public override void WriteAsync(FixedDocument fixedDocument, PrintTicket printTicket) =>
        WriteAsync(fixedDocument, printTicket, userState: null);

    public override void WriteAsync(
        FixedDocument fixedDocument,
        PrintTicket printTicket,
        object? userState)
    {
        ArgumentNullException.ThrowIfNull(fixedDocument);
        WriteAsync(fixedDocument.DocumentPaginator, printTicket, userState);
    }

    public override void WriteAsync(FixedDocumentSequence fixedDocumentSequence) =>
        WriteAsync(fixedDocumentSequence, printTicket: null!, userState: null);

    public override void WriteAsync(FixedDocumentSequence fixedDocumentSequence, object? userState) =>
        WriteAsync(fixedDocumentSequence, printTicket: null!, userState);

    public override void WriteAsync(
        FixedDocumentSequence fixedDocumentSequence,
        PrintTicket printTicket) =>
        WriteAsync(fixedDocumentSequence, printTicket, userState: null);

    public override void WriteAsync(
        FixedDocumentSequence fixedDocumentSequence,
        PrintTicket printTicket,
        object? userState)
    {
        ArgumentNullException.ThrowIfNull(fixedDocumentSequence);
        WriteAsync(fixedDocumentSequence.DocumentPaginator, printTicket, userState);
    }

    /// <summary>
    /// Asynchronously submits an existing XPS document path to the configured print queue.
    /// </summary>
    public void WriteAsync(string documentPath) =>
        WriteAsync(documentPath, XpsDocumentNotificationLevel.None);

    /// <summary>
    /// Asynchronously submits an existing XPS document path to the configured print queue.
    /// </summary>
    public void WriteAsync(string documentPath, XpsDocumentNotificationLevel notificationLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ValidateNotificationLevel(notificationLevel);
        QueueAsync(
            _ => throw new NotSupportedException(
                "Submitting an existing XPS package is not supported by this print backend."),
            state: null);
    }

    public override void CancelAsync()
    {
        lock (_asyncGate)
        {
            _activeWrite?.Cancel();
        }

        _platformWriter.CancelAsync();
    }

    public override SerializerWriterCollator CreateVisualsCollator() =>
        new PrintWriterCollator(this, documentSequencePrintTicket: null, documentPrintTicket: null);

    public override SerializerWriterCollator CreateVisualsCollator(
        PrintTicket documentSequencePT,
        PrintTicket documentPT)
    {
        ArgumentNullException.ThrowIfNull(documentSequencePT);
        ArgumentNullException.ThrowIfNull(documentPT);
        return new PrintWriterCollator(this, documentSequencePT, documentPT);
    }

    private void ExecuteSynchronously(Action action, object? state)
    {
        try
        {
            action();
            OnWritingCompleted(cancelled: false, state, exception: null);
        }
        catch (Exception exception)
        {
            OnWritingCompleted(cancelled: false, state, exception);
            throw;
        }
    }

    private void WriteVisualCore(Visual visual, PrintTicket? printTicket, object? state)
    {
        _platformWriter.Write(visual, ResolvePrintTicket(printTicket));
        OnWritingProgressChanged(
            WritingProgressChangeLevel.FixedPageWritingProgress,
            number: 1,
            progressPercentage: 100,
            state);
    }

    private void WritePaginatorCore(
        DocumentPaginator documentPaginator,
        PrintTicket? printTicket,
        object? state)
    {
        _platformWriter.Write(documentPaginator, ResolvePrintTicket(printTicket));
        var pageCount = documentPaginator.IsPageCountValid ? documentPaginator.PageCount : 0;
        if (pageCount == 0)
        {
            OnWritingProgressChanged(
                WritingProgressChangeLevel.FixedDocumentWritingProgress,
                number: 0,
                progressPercentage: 100,
                state);
            return;
        }

        for (var page = 1; page <= pageCount; page++)
        {
            OnWritingProgressChanged(
                WritingProgressChangeLevel.FixedPageWritingProgress,
                page,
                (page * 100) / pageCount,
                state);
        }
    }

    private PrintTicket? ResolvePrintTicket(PrintTicket? explicitTicket)
    {
        if (explicitTicket != null)
        {
            return explicitTicket;
        }

        var args = new WritingPrintTicketRequiredEventArgs(
            PrintTicketLevel.FixedDocumentSequencePrintTicket,
            sequence: 0);
        WritingPrintTicketRequired?.Invoke(this, args);
        return args.CurrentPrintTicket ?? _printQueue.DefaultPrintTicket;
    }

    private void QueueAsync(Action<CancellationToken> action, object? state)
    {
        CancellationTokenSource cancellation;
        lock (_asyncGate)
        {
            if (_activeWrite != null)
            {
                throw new InvalidOperationException("An asynchronous write is already in progress.");
            }

            cancellation = new CancellationTokenSource();
            _activeWrite = cancellation;
        }

        _ = Task.Run(() =>
        {
            try
            {
                action(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                OnWritingCompleted(cancelled: false, state, exception: null);
            }
            catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
            {
                WritingCancelled?.Invoke(this, new WritingCancelledEventArgs(exception));
                OnWritingCompleted(cancelled: true, state, exception: null);
            }
            catch (Exception exception)
            {
                OnWritingCompleted(cancelled: false, state, exception);
            }
            finally
            {
                lock (_asyncGate)
                {
                    if (ReferenceEquals(_activeWrite, cancellation))
                    {
                        _activeWrite = null;
                    }
                }

                cancellation.Dispose();
            }
        });
    }

    private void OnWritingCompleted(bool cancelled, object? state, Exception? exception) =>
        WritingCompleted?.Invoke(this, new WritingCompletedEventArgs(cancelled, state, exception));

    private void OnWritingProgressChanged(
        WritingProgressChangeLevel level,
        int number,
        int progressPercentage,
        object? state) =>
        WritingProgressChanged?.Invoke(
            this,
            new WritingProgressChangedEventArgs(level, number, progressPercentage, state));

    private static void ValidateNotificationLevel(XpsDocumentNotificationLevel notificationLevel)
    {
        if (notificationLevel is < XpsDocumentNotificationLevel.None or
            > XpsDocumentNotificationLevel.ReceiveNotificationDisabled)
        {
            throw new ArgumentOutOfRangeException(nameof(notificationLevel));
        }
    }

    private sealed class PrintWriterCollator : SerializerWriterCollator
    {
        private readonly XpsDocumentWriter _writer;
        private readonly PrintTicket? _documentSequencePrintTicket;
        private readonly PrintTicket? _documentPrintTicket;
        private bool _batchStarted;

        internal PrintWriterCollator(
            XpsDocumentWriter writer,
            PrintTicket? documentSequencePrintTicket,
            PrintTicket? documentPrintTicket)
        {
            _writer = writer;
            _documentSequencePrintTicket = documentSequencePrintTicket;
            _documentPrintTicket = documentPrintTicket;
        }

        public override void BeginBatchWrite()
        {
            if (_batchStarted)
            {
                throw new InvalidOperationException("A batch write has already been started.");
            }

            _batchStarted = true;
        }

        public override void EndBatchWrite()
        {
            EnsureBatchStarted();
            _batchStarted = false;
        }

        public override void Write(Visual visual) =>
            Write(visual, GetDefaultTicket());

        public override void Write(Visual visual, PrintTicket printTicket)
        {
            EnsureBatchStarted();
            _writer.Write(visual, printTicket);
        }

        public override void WriteAsync(Visual visual) =>
            WriteAsync(visual, GetDefaultTicket(), userState: null);

        public override void WriteAsync(Visual visual, object? userState) =>
            WriteAsync(visual, GetDefaultTicket(), userState);

        public override void WriteAsync(Visual visual, PrintTicket printTicket) =>
            WriteAsync(visual, printTicket, userState: null);

        public override void WriteAsync(
            Visual visual,
            PrintTicket printTicket,
            object? userState)
        {
            EnsureBatchStarted();
            _writer.WriteAsync(visual, printTicket, userState);
        }

        public override void CancelAsync() => _writer.CancelAsync();

        public override void Cancel() => _writer.CancelAsync();

        private PrintTicket GetDefaultTicket() =>
            _documentPrintTicket ?? _documentSequencePrintTicket ?? new PrintTicket();

        private void EnsureBatchStarted()
        {
            if (!_batchStarted)
            {
                throw new InvalidOperationException("BeginBatchWrite must be called first.");
            }
        }
    }
}

/// <summary>
/// Specifies whether document-sequence notifications are requested while writing XPS content.
/// </summary>
public enum XpsDocumentNotificationLevel
{
    None = 0,
    ReceiveNotificationEnabled = 1,
    ReceiveNotificationDisabled = 2,
}
