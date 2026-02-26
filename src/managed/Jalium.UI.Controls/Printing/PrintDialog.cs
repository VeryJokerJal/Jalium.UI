namespace Jalium.UI.Controls.Printing;

/// <summary>
/// Exception thrown by PrintDialog operations.
/// </summary>
public sealed class PrintDialogException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrintDialogException"/> class.
    /// </summary>
    public PrintDialogException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrintDialogException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public PrintDialogException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrintDialogException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PrintDialogException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Invokes a standard print dialog box.
/// </summary>
public sealed class PrintDialog
{
    private int _minPage = 1;
    private int _maxPage = 9999;
    private int _pageFrom = 1;
    private int _pageTo = 9999;

    #region Properties

    /// <summary>
    /// Gets or sets the minimum page number allowed in the dialog.
    /// </summary>
    public int MinPage
    {
        get => _minPage;
        set => _minPage = Math.Max(1, value);
    }

    /// <summary>
    /// Gets or sets the maximum page number allowed in the dialog.
    /// </summary>
    public int MaxPage
    {
        get => _maxPage;
        set => _maxPage = Math.Max(_minPage, value);
    }

    /// <summary>
    /// Gets or sets the starting page of the page range.
    /// </summary>
    public int PageRangeFrom
    {
        get => _pageFrom;
        set => _pageFrom = Math.Clamp(value, _minPage, _maxPage);
    }

    /// <summary>
    /// Gets or sets the ending page of the page range.
    /// </summary>
    public int PageRangeTo
    {
        get => _pageTo;
        set => _pageTo = Math.Clamp(value, _pageFrom, _maxPage);
    }

    /// <summary>
    /// Gets or sets the page range selection.
    /// </summary>
    public PageRangeSelection PageRangeSelection { get; set; } = PageRangeSelection.AllPages;

    /// <summary>
    /// Gets or sets a value indicating whether the user can select a page range.
    /// </summary>
    public bool UserPageRangeEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the currently selected printer.
    /// </summary>
    public PrintQueue? PrintQueue { get; set; }

    /// <summary>
    /// Gets or sets the print ticket.
    /// </summary>
    public PrintTicket? PrintTicket { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current page option is enabled.
    /// </summary>
    public bool CurrentPageEnabled { get; set; }

    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// Gets the printable area width.
    /// </summary>
    public double PrintableAreaWidth => PrintTicket?.PageMediaSize?.Width ?? 816; // 8.5" at 96 DPI

    /// <summary>
    /// Gets the printable area height.
    /// </summary>
    public double PrintableAreaHeight => PrintTicket?.PageMediaSize?.Height ?? 1056; // 11" at 96 DPI

    #endregion

    #region Methods

    /// <summary>
    /// Displays the print dialog.
    /// </summary>
    /// <returns>True if the user clicked Print; otherwise, false.</returns>
    public bool ShowDialog()
    {
        return ShowDialogInternal();
    }

    /// <summary>
    /// Displays the print dialog with the specified owner window.
    /// </summary>
    public bool ShowDialog(Window owner)
    {
        return ShowDialogInternal(owner);
    }

    /// <summary>
    /// Prints a visual element.
    /// </summary>
    /// <param name="visual">The visual element to print.</param>
    /// <param name="description">A description for the print job.</param>
    public void PrintVisual(Visual visual, string description)
    {
        ArgumentNullException.ThrowIfNull(visual);
        PrintVisualInternal(visual, description);
    }

    /// <summary>
    /// Prints a document.
    /// </summary>
    /// <param name="documentPaginator">The document paginator.</param>
    /// <param name="description">A description for the print job.</param>
    public void PrintDocument(DocumentPaginator documentPaginator, string description)
    {
        ArgumentNullException.ThrowIfNull(documentPaginator);
        PrintDocumentInternal(documentPaginator, description);
    }

    #endregion

    #region Internal Methods (Platform Implementation Hooks)

    /// <summary>
    /// Shows the dialog internally.
    /// </summary>
    protected bool ShowDialogInternal(Window? owner = null)
    {
        // Platform-specific implementation
        // Would use Windows PrintDlg or Common Print Dialog
        return false;
    }

    /// <summary>
    /// Prints a visual internally.
    /// </summary>
    protected void PrintVisualInternal(Visual visual, string description)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Prints a document internally.
    /// </summary>
    protected void PrintDocumentInternal(DocumentPaginator documentPaginator, string description)
    {
        // Platform-specific implementation
    }

    #endregion
}

/// <summary>
/// Specifies the page range selection for printing.
/// </summary>
public enum PageRangeSelection
{
    /// <summary>
    /// All pages.
    /// </summary>
    AllPages,

    /// <summary>
    /// User-selected page range.
    /// </summary>
    UserPages,

    /// <summary>
    /// Current page only.
    /// </summary>
    CurrentPage,

    /// <summary>
    /// Selected content.
    /// </summary>
    SelectedPages
}

/// <summary>
/// Represents a range of pages.
/// </summary>
public struct PageRange
{
    /// <summary>
    /// Gets or sets the first page in the range.
    /// </summary>
    public int PageFrom { get; set; }

    /// <summary>
    /// Gets or sets the last page in the range.
    /// </summary>
    public int PageTo { get; set; }

    /// <summary>
    /// Initializes a new instance of the PageRange struct.
    /// </summary>
    public PageRange(int pageFrom, int pageTo)
    {
        PageFrom = pageFrom;
        PageTo = pageTo;
    }

    /// <summary>
    /// Initializes a new instance of the PageRange struct for a single page.
    /// </summary>
    public PageRange(int page)
    {
        PageFrom = page;
        PageTo = page;
    }
}

/// <summary>
/// Represents a print queue (printer).
/// </summary>
public sealed class PrintQueue
{
    /// <summary>
    /// Gets the name of the printer.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full name including server if applicable.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Gets the description of the printer.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets the location of the printer.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the printer is online.
    /// </summary>
    public bool IsOnline { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether this is the default printer.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets the default print ticket for this printer.
    /// </summary>
    public PrintTicket? DefaultPrintTicket { get; set; }

    /// <summary>
    /// Initializes a new instance of the PrintQueue class.
    /// </summary>
    public PrintQueue(string name)
    {
        Name = name;
        FullName = name;
    }

    /// <summary>
    /// Initializes a new instance of the PrintQueue class.
    /// </summary>
    public PrintQueue(string name, string fullName)
    {
        Name = name;
        FullName = fullName;
    }

    /// <summary>
    /// Gets the currently installed print queues.
    /// </summary>
    public static IEnumerable<PrintQueue> GetPrintQueues()
    {
        // Platform-specific implementation
        return Enumerable.Empty<PrintQueue>();
    }

    /// <summary>
    /// Gets the default print queue.
    /// </summary>
    public static PrintQueue? GetDefaultPrintQueue()
    {
        return GetPrintQueues().FirstOrDefault(q => q.IsDefault);
    }

    /// <summary>
    /// Gets the capabilities of this print queue.
    /// </summary>
    /// <returns>The print capabilities.</returns>
    public PrintCapabilities GetPrintCapabilities()
    {
        return GetPrintCapabilities(null);
    }

    /// <summary>
    /// Gets the capabilities of this print queue with the specified print ticket.
    /// </summary>
    /// <param name="printTicket">The print ticket to use for querying capabilities.</param>
    /// <returns>The print capabilities.</returns>
    public PrintCapabilities GetPrintCapabilities(PrintTicket? printTicket)
    {
        // Return default capabilities - platform-specific implementation
        // would query the actual printer capabilities
        return new PrintCapabilities
        {
            CollationCapability = new[] { Collation.Uncollated, Collation.Collated },
            DuplexingCapability = new[] { Duplexing.OneSided, Duplexing.TwoSidedLongEdge, Duplexing.TwoSidedShortEdge },
            PageOrientationCapability = new[] { PageOrientation.Portrait, PageOrientation.Landscape },
            OutputQualityCapability = new[] { OutputQuality.Draft, OutputQuality.Normal, OutputQuality.High },
            OutputColorCapability = new[] { OutputColor.Color, OutputColor.Grayscale, OutputColor.Monochrome },
            PageMediaSizeCapability = new[]
            {
                new PageMediaSize(PageMediaSizeName.NorthAmericaLetter, 816, 1056),
                new PageMediaSize(PageMediaSizeName.NorthAmericaLegal, 816, 1344),
                new PageMediaSize(PageMediaSizeName.ISOA4, 794, 1123),
                new PageMediaSize(PageMediaSizeName.ISOA3, 1123, 1587)
            },
            PageResolutionCapability = new[]
            {
                new PageResolution(300, 300),
                new PageResolution(600, 600),
                new PageResolution(1200, 1200)
            },
            MaxCopyCount = 999
        };
    }

    /// <summary>
    /// Creates an XpsDocumentWriter for this print queue.
    /// </summary>
    /// <returns>An XpsDocumentWriter for this queue.</returns>
    public XpsDocumentWriter CreateXpsDocumentWriter()
    {
        return new XpsDocumentWriter(this);
    }

    /// <summary>
    /// Submits a print job.
    /// </summary>
    /// <param name="jobName">The name of the print job.</param>
    /// <returns>A print job object.</returns>
    public PrintSystemJobInfo? AddJob(string jobName)
    {
        // Platform-specific implementation
        return new PrintSystemJobInfo(this, jobName);
    }

    /// <summary>
    /// Gets all print jobs in the queue.
    /// </summary>
    /// <returns>A collection of print jobs.</returns>
    public IEnumerable<PrintSystemJobInfo> GetPrintJobInfoCollection()
    {
        // Platform-specific implementation
        return Enumerable.Empty<PrintSystemJobInfo>();
    }

    /// <summary>
    /// Pauses all jobs in the queue.
    /// </summary>
    public void Pause()
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Resumes all jobs in the queue.
    /// </summary>
    public void Resume()
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Purges all jobs from the queue.
    /// </summary>
    public void Purge()
    {
        // Platform-specific implementation
    }
}

/// <summary>
/// Represents information about a print job.
/// </summary>
public sealed class PrintSystemJobInfo
{
    /// <summary>
    /// Gets the print queue associated with this job.
    /// </summary>
    public PrintQueue PrintQueue { get; }

    /// <summary>
    /// Gets the name of the print job.
    /// </summary>
    public string JobName { get; }

    /// <summary>
    /// Gets the job identifier.
    /// </summary>
    public int JobIdentifier { get; }

    /// <summary>
    /// Gets the status of the print job.
    /// </summary>
    public PrintJobStatus JobStatus { get; internal set; }

    /// <summary>
    /// Gets the number of pages printed.
    /// </summary>
    public int NumberOfPagesPrinted { get; internal set; }

    /// <summary>
    /// Gets the total number of pages in the job.
    /// </summary>
    public int NumberOfPages { get; internal set; }

    /// <summary>
    /// Gets the time the job was submitted.
    /// </summary>
    public DateTime TimeJobSubmitted { get; }

    /// <summary>
    /// Initializes a new instance of the PrintSystemJobInfo class.
    /// </summary>
    internal PrintSystemJobInfo(PrintQueue queue, string jobName)
    {
        PrintQueue = queue;
        JobName = jobName;
        JobIdentifier = new Random().Next(1, 10000);
        TimeJobSubmitted = DateTime.Now;
        JobStatus = PrintJobStatus.Spooling;
    }

    /// <summary>
    /// Cancels this print job.
    /// </summary>
    public void Cancel()
    {
        JobStatus = PrintJobStatus.Deleted;
    }

    /// <summary>
    /// Pauses this print job.
    /// </summary>
    public void Pause()
    {
        JobStatus = PrintJobStatus.Paused;
    }

    /// <summary>
    /// Resumes this print job.
    /// </summary>
    public void Resume()
    {
        JobStatus = PrintJobStatus.Printing;
    }

    /// <summary>
    /// Restarts this print job.
    /// </summary>
    public void Restart()
    {
        NumberOfPagesPrinted = 0;
        JobStatus = PrintJobStatus.Spooling;
    }
}

/// <summary>
/// Specifies the status of a print job.
/// </summary>
[Flags]
public enum PrintJobStatus
{
    /// <summary>
    /// No status.
    /// </summary>
    None = 0,

    /// <summary>
    /// The job is paused.
    /// </summary>
    Paused = 1,

    /// <summary>
    /// An error occurred.
    /// </summary>
    Error = 2,

    /// <summary>
    /// The job is being deleted.
    /// </summary>
    Deleting = 4,

    /// <summary>
    /// The job is being spooled.
    /// </summary>
    Spooling = 8,

    /// <summary>
    /// The job is printing.
    /// </summary>
    Printing = 16,

    /// <summary>
    /// The job is offline.
    /// </summary>
    Offline = 32,

    /// <summary>
    /// Paper is out.
    /// </summary>
    PaperOut = 64,

    /// <summary>
    /// The job has been printed.
    /// </summary>
    Printed = 128,

    /// <summary>
    /// The job has been deleted.
    /// </summary>
    Deleted = 256,

    /// <summary>
    /// The job is blocked because a device is not available.
    /// </summary>
    BlockedDeviceQueue = 512,

    /// <summary>
    /// User intervention is required.
    /// </summary>
    UserIntervention = 1024,

    /// <summary>
    /// The job has been restarted.
    /// </summary>
    Restarted = 2048,

    /// <summary>
    /// The job is complete.
    /// </summary>
    Completed = 4096,

    /// <summary>
    /// The job has been retained.
    /// </summary>
    Retained = 8192
}

/// <summary>
/// Represents print settings and capabilities.
/// </summary>
public sealed class PrintTicket
{
    /// <summary>
    /// Gets or sets the number of copies.
    /// </summary>
    public int CopyCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether to collate copies.
    /// </summary>
    public Collation? Collation { get; set; }

    /// <summary>
    /// Gets or sets the duplex mode.
    /// </summary>
    public Duplexing? Duplexing { get; set; }

    /// <summary>
    /// Gets or sets the page media size.
    /// </summary>
    public PageMediaSize? PageMediaSize { get; set; }

    /// <summary>
    /// Gets or sets the page orientation.
    /// </summary>
    public PageOrientation? PageOrientation { get; set; }

    /// <summary>
    /// Gets or sets the print quality.
    /// </summary>
    public OutputQuality? OutputQuality { get; set; }

    /// <summary>
    /// Gets or sets the output color.
    /// </summary>
    public OutputColor? OutputColor { get; set; }

    /// <summary>
    /// Gets or sets the page resolution.
    /// </summary>
    public PageResolution? PageResolution { get; set; }
}

/// <summary>
/// Represents a page media size.
/// </summary>
public sealed class PageMediaSize
{
    /// <summary>
    /// Gets the width in 1/96 inch units.
    /// </summary>
    public double? Width { get; }

    /// <summary>
    /// Gets the height in 1/96 inch units.
    /// </summary>
    public double? Height { get; }

    /// <summary>
    /// Gets the media size name.
    /// </summary>
    public PageMediaSizeName? PageMediaSizeName { get; }

    /// <summary>
    /// Initializes a new instance of the PageMediaSize class.
    /// </summary>
    public PageMediaSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Initializes a new instance of the PageMediaSize class.
    /// </summary>
    public PageMediaSize(PageMediaSizeName name, double width, double height)
    {
        PageMediaSizeName = name;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Represents page resolution.
/// </summary>
public sealed class PageResolution
{
    /// <summary>
    /// Gets the X resolution in DPI.
    /// </summary>
    public int? X { get; }

    /// <summary>
    /// Gets the Y resolution in DPI.
    /// </summary>
    public int? Y { get; }

    /// <summary>
    /// Initializes a new instance of the PageResolution class.
    /// </summary>
    public PageResolution(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Specifies the collation setting.
/// </summary>
public enum Collation
{
    /// <summary>
    /// Uncollated output.
    /// </summary>
    Uncollated,

    /// <summary>
    /// Collated output.
    /// </summary>
    Collated
}

/// <summary>
/// Specifies the duplex printing mode.
/// </summary>
public enum Duplexing
{
    /// <summary>
    /// One-sided printing.
    /// </summary>
    OneSided,

    /// <summary>
    /// Two-sided printing, short edge.
    /// </summary>
    TwoSidedShortEdge,

    /// <summary>
    /// Two-sided printing, long edge.
    /// </summary>
    TwoSidedLongEdge
}

/// <summary>
/// Specifies the page orientation.
/// </summary>
public enum PageOrientation
{
    /// <summary>
    /// Portrait orientation.
    /// </summary>
    Portrait,

    /// <summary>
    /// Landscape orientation.
    /// </summary>
    Landscape,

    /// <summary>
    /// Reverse portrait orientation.
    /// </summary>
    ReversePortrait,

    /// <summary>
    /// Reverse landscape orientation.
    /// </summary>
    ReverseLandscape
}

/// <summary>
/// Specifies the output quality.
/// </summary>
public enum OutputQuality
{
    /// <summary>
    /// Draft quality.
    /// </summary>
    Draft,

    /// <summary>
    /// Normal quality.
    /// </summary>
    Normal,

    /// <summary>
    /// High quality.
    /// </summary>
    High,

    /// <summary>
    /// Photo quality.
    /// </summary>
    Photographic
}

/// <summary>
/// Specifies the output color.
/// </summary>
public enum OutputColor
{
    /// <summary>
    /// Color output.
    /// </summary>
    Color,

    /// <summary>
    /// Grayscale output.
    /// </summary>
    Grayscale,

    /// <summary>
    /// Monochrome output.
    /// </summary>
    Monochrome
}

/// <summary>
/// Specifies standard page media sizes.
/// </summary>
public enum PageMediaSizeName
{
    /// <summary>
    /// Unknown size.
    /// </summary>
    Unknown,

    /// <summary>
    /// A3 (297mm x 420mm).
    /// </summary>
    ISOA3,

    /// <summary>
    /// A4 (210mm x 297mm).
    /// </summary>
    ISOA4,

    /// <summary>
    /// A5 (148mm x 210mm).
    /// </summary>
    ISOA5,

    /// <summary>
    /// Letter (8.5" x 11").
    /// </summary>
    NorthAmericaLetter,

    /// <summary>
    /// Legal (8.5" x 14").
    /// </summary>
    NorthAmericaLegal,

    /// <summary>
    /// Tabloid (11" x 17").
    /// </summary>
    NorthAmericaTabloid,

    /// <summary>
    /// Executive (7.25" x 10.5").
    /// </summary>
    NorthAmericaExecutive
}

/// <summary>
/// Provides pagination for documents.
/// </summary>
public abstract class DocumentPaginator
{
    /// <summary>
    /// Gets a value indicating whether the document is being paginated.
    /// </summary>
    public abstract bool IsPageCountValid { get; }

    /// <summary>
    /// Gets the number of pages.
    /// </summary>
    public abstract int PageCount { get; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public abstract Size PageSize { get; set; }

    /// <summary>
    /// Gets or sets the source document.
    /// </summary>
    public abstract object? Source { get; }

    /// <summary>
    /// Gets the DocumentPage for the specified page number.
    /// </summary>
    public abstract DocumentPage GetPage(int pageNumber);

    /// <summary>
    /// Forces pagination to complete.
    /// </summary>
    public void ComputePageCount()
    {
        // Default implementation
    }

    /// <summary>
    /// Occurs when pagination is complete.
    /// </summary>
    public event EventHandler<PaginationCompletedEventArgs>? ComputePageCountCompleted;

    /// <summary>
    /// Occurs when the page count changes.
    /// </summary>
    public event EventHandler<PaginationProgressEventArgs>? PaginationProgress;

    /// <summary>
    /// Raises the ComputePageCountCompleted event.
    /// </summary>
    protected void OnComputePageCountCompleted(Exception? error)
    {
        ComputePageCountCompleted?.Invoke(this, new PaginationCompletedEventArgs(error));
    }

    /// <summary>
    /// Raises the PaginationProgress event.
    /// </summary>
    protected void OnPaginationProgress(int pageCount)
    {
        PaginationProgress?.Invoke(this, new PaginationProgressEventArgs(pageCount));
    }
}

/// <summary>
/// Represents a single page of a document.
/// </summary>
public sealed class DocumentPage
{
    /// <summary>
    /// Gets a blank document page.
    /// </summary>
    public static DocumentPage Missing { get; } = new(null);

    /// <summary>
    /// Gets the visual content of the page.
    /// </summary>
    public Visual? Visual { get; }

    /// <summary>
    /// Gets the size of the page.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the content box (area containing actual content).
    /// </summary>
    public Rect ContentBox { get; }

    /// <summary>
    /// Gets the bleed box (for printing marks).
    /// </summary>
    public Rect BleedBox { get; }

    /// <summary>
    /// Initializes a new instance of the DocumentPage class.
    /// </summary>
    public DocumentPage(Visual? visual)
    {
        Visual = visual;
        Size = Size.Empty;
        ContentBox = Rect.Empty;
        BleedBox = Rect.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the DocumentPage class.
    /// </summary>
    public DocumentPage(Visual visual, Size pageSize, Rect contentBox, Rect bleedBox)
    {
        Visual = visual;
        Size = pageSize;
        ContentBox = contentBox;
        BleedBox = bleedBox;
    }
}

/// <summary>
/// Event arguments for pagination completed events.
/// </summary>
public sealed class PaginationCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error if pagination failed.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Gets a value indicating whether pagination was cancelled.
    /// </summary>
    public bool Cancelled { get; }

    /// <summary>
    /// Initializes a new instance of the PaginationCompletedEventArgs class.
    /// </summary>
    public PaginationCompletedEventArgs(Exception? error, bool cancelled = false)
    {
        Error = error;
        Cancelled = cancelled;
    }
}

/// <summary>
/// Event arguments for pagination progress events.
/// </summary>
public sealed class PaginationProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the current page count.
    /// </summary>
    public int PageCount { get; }

    /// <summary>
    /// Initializes a new instance of the PaginationProgressEventArgs class.
    /// </summary>
    public PaginationProgressEventArgs(int pageCount)
    {
        PageCount = pageCount;
    }
}

/// <summary>
/// Interface for objects that can provide a DocumentPaginator.
/// </summary>
public interface IDocumentPaginatorSource
{
    /// <summary>
    /// Gets the DocumentPaginator for this source.
    /// </summary>
    DocumentPaginator DocumentPaginator { get; }
}

/// <summary>
/// Defines the capabilities of a print queue.
/// </summary>
public sealed class PrintCapabilities
{
    /// <summary>
    /// Gets the collection of supported collation options.
    /// </summary>
    public IReadOnlyCollection<Collation> CollationCapability { get; init; } = Array.Empty<Collation>();

    /// <summary>
    /// Gets the collection of supported duplex options.
    /// </summary>
    public IReadOnlyCollection<Duplexing> DuplexingCapability { get; init; } = Array.Empty<Duplexing>();

    /// <summary>
    /// Gets the collection of supported page orientations.
    /// </summary>
    public IReadOnlyCollection<PageOrientation> PageOrientationCapability { get; init; } = Array.Empty<PageOrientation>();

    /// <summary>
    /// Gets the collection of supported output qualities.
    /// </summary>
    public IReadOnlyCollection<OutputQuality> OutputQualityCapability { get; init; } = Array.Empty<OutputQuality>();

    /// <summary>
    /// Gets the collection of supported output colors.
    /// </summary>
    public IReadOnlyCollection<OutputColor> OutputColorCapability { get; init; } = Array.Empty<OutputColor>();

    /// <summary>
    /// Gets the collection of supported page media sizes.
    /// </summary>
    public IReadOnlyCollection<PageMediaSize> PageMediaSizeCapability { get; init; } = Array.Empty<PageMediaSize>();

    /// <summary>
    /// Gets the collection of supported page resolutions.
    /// </summary>
    public IReadOnlyCollection<PageResolution> PageResolutionCapability { get; init; } = Array.Empty<PageResolution>();

    /// <summary>
    /// Gets the maximum supported copies.
    /// </summary>
    public int? MaxCopyCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether stapling is supported.
    /// </summary>
    public bool? StaplingCapability { get; init; }

    /// <summary>
    /// Gets a value indicating whether page ordering is supported.
    /// </summary>
    public bool? PageOrderCapability { get; init; }

    /// <summary>
    /// Gets the printable area offset from the origin.
    /// </summary>
    public Point? OriginOffset { get; init; }

    /// <summary>
    /// Gets the printable area margins.
    /// </summary>
    public Thickness? PrintableAreaMargins { get; init; }
}

/// <summary>
/// Represents a print server.
/// </summary>
public sealed class PrintServer : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets the name of the print server.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the PrintServer class for the local server.
    /// </summary>
    public PrintServer()
    {
        Name = Environment.MachineName;
    }

    /// <summary>
    /// Initializes a new instance of the PrintServer class.
    /// </summary>
    /// <param name="serverName">The name of the print server.</param>
    public PrintServer(string serverName)
    {
        Name = serverName;
    }

    /// <summary>
    /// Gets all print queues from this server.
    /// </summary>
    public IEnumerable<PrintQueue> GetPrintQueues()
    {
        // Platform-specific implementation would enumerate printers
        return PrintQueue.GetPrintQueues();
    }

    /// <summary>
    /// Gets the default print queue from this server.
    /// </summary>
    public PrintQueue? GetDefaultPrintQueue()
    {
        return PrintQueue.GetDefaultPrintQueue();
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    protected void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Provides helper methods for XPS document printing.
/// </summary>
public sealed class XpsDocumentWriter
{
    private readonly PrintQueue _printQueue;

    /// <summary>
    /// Initializes a new instance of the XpsDocumentWriter class.
    /// </summary>
    /// <param name="printQueue">The print queue to write to.</param>
    public XpsDocumentWriter(PrintQueue printQueue)
    {
        _printQueue = printQueue ?? throw new ArgumentNullException(nameof(printQueue));
    }

    /// <summary>
    /// Writes a visual to the print queue.
    /// </summary>
    /// <param name="visual">The visual to print.</param>
    public void Write(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        WriteInternal(visual, null);
    }

    /// <summary>
    /// Writes a visual to the print queue with the specified print ticket.
    /// </summary>
    /// <param name="visual">The visual to print.</param>
    /// <param name="printTicket">The print ticket to use.</param>
    public void Write(Visual visual, PrintTicket? printTicket)
    {
        ArgumentNullException.ThrowIfNull(visual);
        WriteInternal(visual, printTicket);
    }

    /// <summary>
    /// Writes a document paginator to the print queue.
    /// </summary>
    /// <param name="documentPaginator">The document paginator to print.</param>
    public void Write(DocumentPaginator documentPaginator)
    {
        ArgumentNullException.ThrowIfNull(documentPaginator);
        WriteInternal(documentPaginator, null);
    }

    /// <summary>
    /// Writes a document paginator to the print queue with the specified print ticket.
    /// </summary>
    /// <param name="documentPaginator">The document paginator to print.</param>
    /// <param name="printTicket">The print ticket to use.</param>
    public void Write(DocumentPaginator documentPaginator, PrintTicket? printTicket)
    {
        ArgumentNullException.ThrowIfNull(documentPaginator);
        WriteInternal(documentPaginator, printTicket);
    }

    /// <summary>
    /// Writes a document to the print queue.
    /// </summary>
    /// <param name="documentPaginatorSource">The document to print.</param>
    public void Write(IDocumentPaginatorSource documentPaginatorSource)
    {
        ArgumentNullException.ThrowIfNull(documentPaginatorSource);
        Write(documentPaginatorSource.DocumentPaginator);
    }

    /// <summary>
    /// Writes a document to the print queue with the specified print ticket.
    /// </summary>
    /// <param name="documentPaginatorSource">The document to print.</param>
    /// <param name="printTicket">The print ticket to use.</param>
    public void Write(IDocumentPaginatorSource documentPaginatorSource, PrintTicket? printTicket)
    {
        ArgumentNullException.ThrowIfNull(documentPaginatorSource);
        Write(documentPaginatorSource.DocumentPaginator, printTicket);
    }

    /// <summary>
    /// Cancels the current print operation.
    /// </summary>
    public void CancelAsync()
    {
        // Platform-specific cancellation
    }

    /// <summary>
    /// Occurs when an asynchronous write operation is completed.
    /// </summary>
    public event EventHandler<WritingCompletedEventArgs>? WritingCompleted;

    /// <summary>
    /// Occurs when a page is written.
    /// </summary>
    public event EventHandler<WritingProgressChangedEventArgs>? WritingProgressChanged;

    /// <summary>
    /// Occurs when a print subtask is completed.
    /// </summary>
    public event EventHandler<WritingPrintTicketRequiredEventArgs>? WritingPrintTicketRequired;

    private void WriteInternal(Visual visual, PrintTicket? printTicket)
    {
        // Platform-specific implementation
        OnWritingCompleted(null, false);
    }

    private void WriteInternal(DocumentPaginator paginator, PrintTicket? printTicket)
    {
        // Platform-specific implementation
        for (int i = 0; i < paginator.PageCount; i++)
        {
            OnWritingProgressChanged(i + 1, paginator.PageCount);
        }
        OnWritingCompleted(null, false);
    }

    /// <summary>
    /// Raises the WritingCompleted event.
    /// </summary>
    protected void OnWritingCompleted(Exception? error, bool cancelled)
    {
        WritingCompleted?.Invoke(this, new WritingCompletedEventArgs(error, cancelled));
    }

    /// <summary>
    /// Raises the WritingProgressChanged event.
    /// </summary>
    protected void OnWritingProgressChanged(int currentPage, int totalPages)
    {
        WritingProgressChanged?.Invoke(this, new WritingProgressChangedEventArgs(currentPage, totalPages));
    }
}

/// <summary>
/// Event arguments for writing completed events.
/// </summary>
public sealed class WritingCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error if the operation failed.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Gets a value indicating whether the operation was cancelled.
    /// </summary>
    public bool Cancelled { get; }

    /// <summary>
    /// Initializes a new instance of the WritingCompletedEventArgs class.
    /// </summary>
    public WritingCompletedEventArgs(Exception? error, bool cancelled)
    {
        Error = error;
        Cancelled = cancelled;
    }
}

/// <summary>
/// Event arguments for writing progress changed events.
/// </summary>
public sealed class WritingProgressChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the current page number.
    /// </summary>
    public int CurrentPage { get; }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages { get; }

    /// <summary>
    /// Gets the progress percentage.
    /// </summary>
    public int ProgressPercentage => TotalPages > 0 ? (CurrentPage * 100) / TotalPages : 0;

    /// <summary>
    /// Initializes a new instance of the WritingProgressChangedEventArgs class.
    /// </summary>
    public WritingProgressChangedEventArgs(int currentPage, int totalPages)
    {
        CurrentPage = currentPage;
        TotalPages = totalPages;
    }
}

/// <summary>
/// Event arguments for when a print ticket is required.
/// </summary>
public sealed class WritingPrintTicketRequiredEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the print ticket.
    /// </summary>
    public PrintTicket? PrintTicket { get; set; }

    /// <summary>
    /// Gets the sequence number for this print ticket request.
    /// </summary>
    public int Sequence { get; }

    /// <summary>
    /// Gets the level at which this print ticket applies.
    /// </summary>
    public PrintTicketLevel PrintTicketLevel { get; }

    /// <summary>
    /// Initializes a new instance of the WritingPrintTicketRequiredEventArgs class.
    /// </summary>
    public WritingPrintTicketRequiredEventArgs(PrintTicketLevel level, int sequence)
    {
        PrintTicketLevel = level;
        Sequence = sequence;
    }
}

/// <summary>
/// Specifies the level at which a print ticket applies.
/// </summary>
public enum PrintTicketLevel
{
    /// <summary>
    /// The print ticket applies to the job.
    /// </summary>
    Job,

    /// <summary>
    /// The print ticket applies to a document within the job.
    /// </summary>
    Document,

    /// <summary>
    /// The print ticket applies to a page within the document.
    /// </summary>
    Page
}

/// <summary>
/// Specifies input and output bins for printing.
/// </summary>
public enum InputBin
{
    /// <summary>
    /// Automatic tray selection.
    /// </summary>
    AutoSelect,

    /// <summary>
    /// Cassette tray.
    /// </summary>
    Cassette,

    /// <summary>
    /// Tray 1.
    /// </summary>
    Tray1,

    /// <summary>
    /// Tray 2.
    /// </summary>
    Tray2,

    /// <summary>
    /// Tray 3.
    /// </summary>
    Tray3,

    /// <summary>
    /// Manual feed.
    /// </summary>
    Manual,

    /// <summary>
    /// Auto sheet feeder.
    /// </summary>
    AutoSheetFeeder
}

/// <summary>
/// Specifies stapling options for printing.
/// </summary>
public enum Stapling
{
    /// <summary>
    /// No stapling.
    /// </summary>
    None,

    /// <summary>
    /// Staple in the top left corner.
    /// </summary>
    StapleTopLeft,

    /// <summary>
    /// Staple in the top right corner.
    /// </summary>
    StapleTopRight,

    /// <summary>
    /// Staple in the bottom left corner.
    /// </summary>
    StapleBottomLeft,

    /// <summary>
    /// Staple in the bottom right corner.
    /// </summary>
    StapleBottomRight,

    /// <summary>
    /// Dual staple on the left side.
    /// </summary>
    StapleDualLeft,

    /// <summary>
    /// Dual staple on the right side.
    /// </summary>
    StapleDualRight,

    /// <summary>
    /// Dual staple on the top.
    /// </summary>
    StapleDualTop,

    /// <summary>
    /// Dual staple on the bottom.
    /// </summary>
    StapleDualBottom,

    /// <summary>
    /// Saddle stitch.
    /// </summary>
    SaddleStitch
}

/// <summary>
/// Specifies page ordering for multi-page printing.
/// </summary>
public enum PageOrder
{
    /// <summary>
    /// Standard page order (1, 2, 3...).
    /// </summary>
    Standard,

    /// <summary>
    /// Reverse page order (...3, 2, 1).
    /// </summary>
    Reverse
}

/// <summary>
/// Specifies pages per sheet options.
/// </summary>
public enum PagesPerSheet
{
    /// <summary>
    /// One page per sheet.
    /// </summary>
    One = 1,

    /// <summary>
    /// Two pages per sheet.
    /// </summary>
    Two = 2,

    /// <summary>
    /// Four pages per sheet.
    /// </summary>
    Four = 4,

    /// <summary>
    /// Six pages per sheet.
    /// </summary>
    Six = 6,

    /// <summary>
    /// Nine pages per sheet.
    /// </summary>
    Nine = 9,

    /// <summary>
    /// Sixteen pages per sheet.
    /// </summary>
    Sixteen = 16
}
