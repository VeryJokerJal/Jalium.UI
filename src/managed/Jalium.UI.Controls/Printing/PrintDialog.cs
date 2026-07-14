using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Printing;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Printing;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Xps;
using RenderTargetBitmap = Jalium.UI.Media.Imaging.RenderTargetBitmap;

namespace Jalium.UI.Controls
{

/// <summary>
/// Exception thrown by PrintDialog operations.
/// </summary>
[Serializable]
public class PrintDialogException : Exception
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

    /// <summary>Initializes the exception from serialized data.</summary>
#pragma warning disable SYSLIB0051 // Required by the WPF-compatible serialization contract.
    protected PrintDialogException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}

/// <summary>
/// Invokes a standard print dialog box.
/// </summary>
public class PrintDialog
{
    private uint _minPage = 1;
    private uint _maxPage = 9999;
    private PageRange _pageRange;
    private uint? _linuxPortalPrintToken;
    private nint _linuxPortalOwner;

    /// <summary>
    /// Gets whether this API can currently submit print jobs on the running platform.
    /// </summary>
    internal static bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IsPortalPrintAvailable;

    /// <summary>
    /// Gets whether the Linux desktop session exposes the xdg Print portal.
    /// </summary>
    /// <remarks>
    /// Linux printing renders the requested visual to PDF, completes the
    /// portal's PreparePrint transaction, and passes the PDF through the Unix
    /// file-descriptor list required by the Print method. This probe lets
    /// applications capability-gate print UI without assuming a particular
    /// Linux desktop or print backend.
    /// </remarks>
    internal static bool IsPortalPrintAvailable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        LinuxDesktopPortal.IsInterfaceAvailable("org.freedesktop.portal.Print");

    #region Properties

    /// <summary>
    /// Gets or sets the minimum page number allowed in the dialog.
    /// </summary>
    public uint MinPage
    {
        get => _minPage;
        set => _minPage = Math.Max(1u, value);
    }

    /// <summary>
    /// Gets or sets the maximum page number allowed in the dialog.
    /// </summary>
    public uint MaxPage
    {
        get => _maxPage;
        set => _maxPage = Math.Max(_minPage, value);
    }

    /// <summary>
    /// Gets or sets the starting page of the page range.
    /// </summary>
    internal int PageRangeFrom
    {
        get => _pageRange.PageFrom;
        set => _pageRange.PageFrom = Math.Clamp(
            value,
            ToPageRangeValue(_minPage),
            ToPageRangeValue(_maxPage));
    }

    /// <summary>
    /// Gets or sets the ending page of the page range.
    /// </summary>
    internal int PageRangeTo
    {
        get => _pageRange.PageTo;
        set => _pageRange.PageTo = Math.Clamp(
            value,
            _pageRange.PageFrom,
            ToPageRangeValue(_maxPage));
    }

    /// <summary>
    /// Gets or sets the range used when <see cref="PageRangeSelection"/> is
    /// <see cref="Jalium.UI.Controls.PageRangeSelection.UserPages"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Either endpoint is less than one.
    /// </exception>
    public PageRange PageRange
    {
        get => _pageRange;
        set
        {
            if (value.PageFrom <= 0 || value.PageTo <= 0)
            {
                throw new ArgumentException(
                    "The beginning and end of a page range must be greater than zero.",
                    nameof(PageRange));
            }

            if (value.PageFrom > value.PageTo)
            {
                int page = value.PageFrom;
                value.PageFrom = value.PageTo;
                value.PageTo = page;
            }

            _pageRange = value;
        }
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
    public System.Printing.PrintTicket? PrintTicket { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current page option is enabled.
    /// </summary>
    public bool CurrentPageEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Selection option is enabled.
    /// </summary>
    public bool SelectedPagesEnabled { get; set; }

    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    internal int CurrentPage { get; set; } = 1;

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
    public bool? ShowDialog()
    {
        return ShowDialogInternal(Jalium.UI.Application.Current?.MainWindow);
    }

    /// <summary>
    /// Displays the print dialog with the specified owner window.
    /// </summary>
    internal bool ShowDialog(Window owner)
    {
        return ShowDialogInternal(owner);
    }

    private static int ToPageRangeValue(uint page) =>
        page > int.MaxValue ? int.MaxValue : (int)page;

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
    /// <param name="owner">The owner window for the dialog, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the user clicked Print; otherwise <see langword="false"/>.</returns>
    private bool ShowDialogInternal(Window? owner = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _linuxPortalOwner = owner?.Handle ?? nint.Zero;
            var preparation = LinuxDesktopPortal.PreparePrint(_linuxPortalOwner, "Print");
            if (preparation.IsSuccess)
            {
                _linuxPortalPrintToken = preparation.Token;
                return true;
            }

            _linuxPortalPrintToken = null;
            if (preparation.Status is LinuxPortalResponseStatus.Cancelled or
                LinuxPortalResponseStatus.Unavailable)
            {
                return false;
            }

            throw new PrintDialogException(
                preparation.Error ?? "The xdg Print portal failed to prepare the print job.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var ownerHandle = DialogOwnerResolver.Resolve(owner?.Handle ?? nint.Zero);
        return ShowWindowsPrintDialog(ownerHandle);
    }

    private static string GetUnsupportedPlatformMessage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return IsPortalPrintAvailable
                ? "The xdg Print portal failed to accept the print job."
                : "Printing is unavailable because the org.freedesktop.portal.Print portal is not present in this Linux desktop session.";
        }

        return "Printing is not supported on this platform.";
    }

    /// <summary>
    /// Prints a visual internally by rasterizing it and emitting it to the
    /// printer device context as a single page.
    /// </summary>
    /// <param name="visual">The visual to print.</param>
    /// <param name="description">A description used as the print job name.</param>
    private void PrintVisualInternal(Visual visual, string description)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            PrintVisualWithPortal(visual, description);
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PrintDialogException(GetUnsupportedPlatformMessage());
        }

        var printerName = ResolvePrinterName();
        if (string.IsNullOrEmpty(printerName))
        {
            throw new PrintDialogException("No printer is available to print to.");
        }

        var hdc = PrintingNativeMethods.CreateDC(null, printerName, null, nint.Zero);
        if (hdc == nint.Zero)
        {
            throw new PrintDialogException(
                $"Failed to create a device context for printer '{printerName}'.");
        }

        try
        {
            var jobName = string.IsNullOrWhiteSpace(description) ? "Jalium.UI Document" : description;
            var docInfo = new PrintingNativeMethods.DOCINFO
            {
                cbSize = Marshal.SizeOf<PrintingNativeMethods.DOCINFO>(),
                lpszDocName = jobName
            };

            if (PrintingNativeMethods.StartDoc(hdc, ref docInfo) <= 0)
            {
                throw new PrintDialogException($"StartDoc failed for printer '{printerName}'.");
            }

            try
            {
                var copies = Math.Max(1, PrintTicket?.CopyCount ?? 1);
                for (var copy = 0; copy < copies; copy++)
                {
                    PrintVisualPage(hdc, visual);
                }

                PrintingNativeMethods.EndDoc(hdc);
            }
            catch
            {
                PrintingNativeMethods.AbortDoc(hdc);
                throw;
            }
        }
        finally
        {
            PrintingNativeMethods.DeleteDC(hdc);
        }
    }

    /// <summary>
    /// Prints a paginated document internally, emitting each page produced by
    /// the paginator as a separate printer page.
    /// </summary>
    /// <param name="documentPaginator">The document paginator.</param>
    /// <param name="description">A description used as the print job name.</param>
    private void PrintDocumentInternal(DocumentPaginator documentPaginator, string description)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            PrintDocumentWithPortal(documentPaginator, description);
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PrintDialogException(GetUnsupportedPlatformMessage());
        }

        var printerName = ResolvePrinterName();
        if (string.IsNullOrEmpty(printerName))
        {
            throw new PrintDialogException("No printer is available to print to.");
        }

        var hdc = PrintingNativeMethods.CreateDC(null, printerName, null, nint.Zero);
        if (hdc == nint.Zero)
        {
            throw new PrintDialogException(
                $"Failed to create a device context for printer '{printerName}'.");
        }

        try
        {
            var jobName = string.IsNullOrWhiteSpace(description) ? "Jalium.UI Document" : description;
            var docInfo = new PrintingNativeMethods.DOCINFO
            {
                cbSize = Marshal.SizeOf<PrintingNativeMethods.DOCINFO>(),
                lpszDocName = jobName
            };

            if (PrintingNativeMethods.StartDoc(hdc, ref docInfo) <= 0)
            {
                throw new PrintDialogException($"StartDoc failed for printer '{printerName}'.");
            }

            try
            {
                documentPaginator.ComputePageCount();
                var pageCount = documentPaginator.PageCount;
                var (first, last) = ResolveDocumentPageRange(pageCount);
                var copies = Math.Max(1, PrintTicket?.CopyCount ?? 1);

                for (var copy = 0; copy < copies; copy++)
                {
                    for (var pageIndex = first; pageIndex <= last; pageIndex++)
                    {
                        var documentPage = documentPaginator.GetPage(pageIndex);
                        if (documentPage?.Visual == null)
                        {
                            continue;
                        }

                        var pageSize = documentPage.Size.IsEmpty || documentPage.Size.Width <= 0
                            ? documentPaginator.PageSize
                            : documentPage.Size;

                        PrintVisualPage(hdc, documentPage.Visual, pageSize);
                    }
                }

                PrintingNativeMethods.EndDoc(hdc);
            }
            catch
            {
                PrintingNativeMethods.AbortDoc(hdc);
                throw;
            }
        }
        finally
        {
            PrintingNativeMethods.DeleteDC(hdc);
        }
    }

    private void PrintVisualWithPortal(Visual visual, string description)
    {
        EnsureLinuxPortalPrintPrepared(description);
        SubmitLinuxPortalPdf(
            [RenderLinuxPdfPage(visual, MeasureVisualForPrint(visual, default))],
            description);
    }

    private void PrintDocumentWithPortal(DocumentPaginator documentPaginator, string description)
    {
        EnsureLinuxPortalPrintPrepared(description);
        documentPaginator.ComputePageCount();
        var (first, last) = ResolveDocumentPageRange(documentPaginator.PageCount);
        var pages = new List<LinuxPdfRasterPage>();
        for (var pageIndex = first; pageIndex <= last; pageIndex++)
        {
            var documentPage = documentPaginator.GetPage(pageIndex);
            if (documentPage?.Visual == null)
                continue;

            var pageSize = documentPage.Size.IsEmpty || documentPage.Size.Width <= 0 ||
                           documentPage.Size.Height <= 0
                ? documentPaginator.PageSize
                : documentPage.Size;
            pages.Add(RenderLinuxPdfPage(
                documentPage.Visual,
                MeasureVisualForPrint(documentPage.Visual, pageSize)));
        }

        if (pages.Count == 0)
            throw new PrintDialogException("The document contains no printable pages.");

        SubmitLinuxPortalPdf(pages, description);
    }

    private void EnsureLinuxPortalPrintPrepared(string description)
    {
        if (_linuxPortalPrintToken.HasValue)
            return;

        var preparation = LinuxDesktopPortal.PreparePrint(
            _linuxPortalOwner,
            string.IsNullOrWhiteSpace(description) ? "Print" : description);
        if (!preparation.IsSuccess)
        {
            throw new PrintDialogException(
                preparation.Error ?? preparation.Status switch
                {
                    LinuxPortalResponseStatus.Cancelled => "The print operation was cancelled.",
                    LinuxPortalResponseStatus.TimedOut => "The print portal timed out.",
                    _ => GetUnsupportedPlatformMessage()
                });
        }

        _linuxPortalPrintToken = preparation.Token;
    }

    private void SubmitLinuxPortalPdf(
        IReadOnlyList<LinuxPdfRasterPage> pages,
        string description)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"jalium-print-{Guid.NewGuid():N}.pdf");
        try
        {
            using var pdf = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            LinuxPdfDocumentWriter.Write(pdf, pages);
            pdf.Position = 0;

            var descriptor = pdf.SafeFileHandle.DangerousGetHandle().ToInt32();
            var response = LinuxDesktopPortal.SubmitPrint(
                _linuxPortalOwner,
                string.IsNullOrWhiteSpace(description) ? "Jalium.UI Document" : description,
                descriptor,
                _linuxPortalPrintToken.GetValueOrDefault());
            if (response.Status != LinuxPortalResponseStatus.Success)
            {
                throw new PrintDialogException(
                    response.Error ?? response.Status switch
                    {
                        LinuxPortalResponseStatus.Cancelled => "The print operation was cancelled.",
                        LinuxPortalResponseStatus.TimedOut => "The print portal timed out.",
                        _ => "The xdg Print portal rejected the PDF document."
                    });
            }
        }
        finally
        {
            _linuxPortalPrintToken = null;
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // The portal has already duplicated the descriptor. A stale
                // temporary file is safer than masking a successful print job.
            }
        }
    }

    private static LinuxPdfRasterPage RenderLinuxPdfPage(Visual visual, Size pageSize)
    {
        var bitmapWidth = Math.Max(1, checked((int)Math.Ceiling(pageSize.Width)));
        var bitmapHeight = Math.Max(1, checked((int)Math.Ceiling(pageSize.Height)));
        var renderTarget = new RenderTargetBitmap(
            bitmapWidth,
            bitmapHeight,
            96.0,
            96.0,
            PixelFormat.Bgra32);
        renderTarget.Clear(Color.White);
        renderTarget.Render(visual);

        var bgra = new byte[checked(bitmapWidth * bitmapHeight * 4)];
        renderTarget.CopyPixels(
            new Int32Rect(0, 0, bitmapWidth, bitmapHeight),
            bgra,
            checked(bitmapWidth * 4),
            0);
        var rgb = LinuxPdfDocumentWriter.CompositeBgraOnWhite(bgra);
        const double pointsPerDip = 72.0 / 96.0;
        return new LinuxPdfRasterPage(
            bitmapWidth,
            bitmapHeight,
            Math.Max(pointsPerDip, pageSize.Width * pointsPerDip),
            Math.Max(pointsPerDip, pageSize.Height * pointsPerDip),
            rgb);
    }

    #endregion

    #region Windows Print Dialog Implementation

    /// <summary>
    /// Displays the Win32 common print dialog and writes the user's selection
    /// back onto this dialog's properties, <see cref="PrintQueue"/> and
    /// <see cref="PrintTicket"/>.
    /// </summary>
    /// <param name="ownerHandle">The owner window handle.</param>
    /// <returns><see langword="true"/> if the user clicked Print; otherwise <see langword="false"/>.</returns>
    private bool ShowWindowsPrintDialog(nint ownerHandle)
    {
        var printDialog = new PrintingNativeMethods.PRINTDLG
        {
            lStructSize = Marshal.SizeOf<PrintingNativeMethods.PRINTDLG>(),
            hwndOwner = ownerHandle,
            Flags = PrintingNativeMethods.PD_RETURNDC | PrintingNativeMethods.PD_HIDEPRINTTOFILE,
            nFromPage = (ushort)Math.Clamp(PageRangeFrom, 1, ushort.MaxValue),
            nToPage = (ushort)Math.Clamp(PageRangeTo, 1, ushort.MaxValue),
            nMinPage = (ushort)Math.Clamp(MinPage, 1u, (uint)ushort.MaxValue),
            nMaxPage = (ushort)Math.Clamp(MaxPage, 1u, (uint)ushort.MaxValue),
            nCopies = (ushort)Math.Clamp(PrintTicket?.CopyCount ?? 1, 1, ushort.MaxValue)
        };

        if (!UserPageRangeEnabled)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_NOPAGENUMS;
        }

        if (!CurrentPageEnabled)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_NOCURRENTPAGE;
        }

        if (!SelectedPagesEnabled)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_NOSELECTION;
        }

        if (PageRangeSelection == PageRangeSelection.UserPages)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_PAGENUMS;
        }
        else if (PageRangeSelection == PageRangeSelection.CurrentPage && CurrentPageEnabled)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_CURRENTPAGE;
        }
        else if (PageRangeSelection == PageRangeSelection.SelectedPages && SelectedPagesEnabled)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_SELECTION;
        }

        if (!PrintingNativeMethods.PrintDlg(ref printDialog))
        {
            var error = PrintingNativeMethods.CommDlgExtendedError();

            // FreeGlobalHandles is still required: even on cancel the dialog
            // may have allocated DEVMODE / DEVNAMES handles.
            FreeDialogHandles(ref printDialog);

            if (error != 0)
            {
                throw new PrintDialogException($"PrintDlg failed with common dialog error 0x{error:X8}.");
            }

            return false;
        }

        try
        {
            ApplyPrintDialogResult(ref printDialog);
        }
        finally
        {
            // The device context returned via PD_RETURNDC is not retained; the
            // print path re-opens its own DC from the resolved printer name.
            if (printDialog.hDC != nint.Zero)
            {
                PrintingNativeMethods.DeleteDC(printDialog.hDC);
            }

            FreeDialogHandles(ref printDialog);
        }

        return true;
    }

    /// <summary>
    /// Transfers the result of a successful <c>PrintDlg</c> call onto this
    /// dialog's properties, the selected <see cref="PrintQueue"/> and the
    /// active <see cref="PrintTicket"/>.
    /// </summary>
    private void ApplyPrintDialogResult(ref PrintingNativeMethods.PRINTDLG printDialog)
    {
        // Copy count and page range come straight from the structure.
        var copies = Math.Max(1, (int)printDialog.nCopies);
        PrintTicket ??= new System.Printing.PrintTicket();
        PrintTicket.CopyCount = copies;

        if ((printDialog.Flags & PrintingNativeMethods.PD_SELECTION) != 0)
        {
            PageRangeSelection = PageRangeSelection.SelectedPages;
        }
        else if ((printDialog.Flags & PrintingNativeMethods.PD_PAGENUMS) != 0)
        {
            PageRangeSelection = PageRangeSelection.UserPages;
            PageRangeFrom = printDialog.nFromPage;
            PageRangeTo = printDialog.nToPage;
        }
        else if ((printDialog.Flags & PrintingNativeMethods.PD_CURRENTPAGE) != 0)
        {
            PageRangeSelection = PageRangeSelection.CurrentPage;
        }
        else
        {
            PageRangeSelection = PageRangeSelection.AllPages;
        }

        if ((printDialog.Flags & PrintingNativeMethods.PD_COLLATE) != 0)
        {
            PrintTicket.Collation = Collation.Collated;
        }

        // The printer name and the orientation / paper size live inside the
        // DEVNAMES and DEVMODE global memory objects.
        var printerName = ReadDevNamesPrinter(printDialog.hDevNames);
        if (!string.IsNullOrEmpty(printerName))
        {
            var queue = new PrintQueue(printerName)
            {
                IsDefault = string.Equals(
                    printerName,
                    GetDefaultPrinterName(),
                    StringComparison.OrdinalIgnoreCase),
                DefaultPrintTicket = PrintTicket
            };
            PrintQueue = queue;
        }

        ApplyDevModeToPrintTicket(printDialog.hDevMode);
    }

    /// <summary>
    /// Reads the device (printer) name out of a Win32 DEVNAMES global memory
    /// object produced by the print dialog.
    /// </summary>
    private static string? ReadDevNamesPrinter(nint hDevNames)
    {
        if (hDevNames == nint.Zero)
        {
            return null;
        }

        var ptr = PrintingNativeMethods.GlobalLock(hDevNames);
        if (ptr == nint.Zero)
        {
            return null;
        }

        try
        {
            // DEVNAMES layout: three WORD offsets (driver / device / output)
            // followed by the null-terminated strings they index, measured in
            // characters from the start of the structure.
            var deviceOffset = (ushort)Marshal.ReadInt16(ptr, sizeof(ushort));
            var devicePtr = ptr + (deviceOffset * sizeof(char));
            return Marshal.PtrToStringUni(devicePtr);
        }
        finally
        {
            PrintingNativeMethods.GlobalUnlock(hDevNames);
        }
    }

    /// <summary>
    /// Reads paper orientation, paper size and copy count out of a Win32
    /// DEVMODE global memory object and stores them in the active print ticket.
    /// </summary>
    private void ApplyDevModeToPrintTicket(nint hDevMode)
    {
        if (hDevMode == nint.Zero)
        {
            return;
        }

        var ptr = PrintingNativeMethods.GlobalLock(hDevMode);
        if (ptr == nint.Zero)
        {
            return;
        }

        try
        {
            var devMode = Marshal.PtrToStructure<PrintingNativeMethods.DEVMODE>(ptr);
            PrintTicket ??= new System.Printing.PrintTicket();

            if ((devMode.dmFields & PrintingNativeMethods.DM_ORIENTATION) != 0)
            {
                PrintTicket.PageOrientation = devMode.dmOrientation == PrintingNativeMethods.DMORIENT_LANDSCAPE
                    ? PageOrientation.Landscape
                    : PageOrientation.Portrait;
            }

            if ((devMode.dmFields & PrintingNativeMethods.DM_COPIES) != 0 && devMode.dmCopies > 0)
            {
                PrintTicket.CopyCount = devMode.dmCopies;
            }

            if ((devMode.dmFields & PrintingNativeMethods.DM_PAPERSIZE) != 0 &&
                devMode.dmPaperLength > 0 && devMode.dmPaperWidth > 0)
            {
                // dmPaperLength / dmPaperWidth are expressed in tenths of a
                // millimeter; convert to 1/96-inch device-independent units.
                var widthDip = devMode.dmPaperWidth / 10.0 / 25.4 * 96.0;
                var heightDip = devMode.dmPaperLength / 10.0 / 25.4 * 96.0;
                PrintTicket.PageMediaSize = new PageMediaSize(widthDip, heightDip);
            }
        }
        finally
        {
            PrintingNativeMethods.GlobalUnlock(hDevMode);
        }
    }

    /// <summary>
    /// Releases the DEVMODE and DEVNAMES global memory objects allocated by the
    /// print dialog.
    /// </summary>
    private static void FreeDialogHandles(ref PrintingNativeMethods.PRINTDLG printDialog)
    {
        if (printDialog.hDevMode != nint.Zero)
        {
            PrintingNativeMethods.GlobalFree(printDialog.hDevMode);
            printDialog.hDevMode = nint.Zero;
        }

        if (printDialog.hDevNames != nint.Zero)
        {
            PrintingNativeMethods.GlobalFree(printDialog.hDevNames);
            printDialog.hDevNames = nint.Zero;
        }
    }

    #endregion

    #region Print Output Implementation

    /// <summary>
    /// Resolves the printer name to print to, preferring the printer selected
    /// in the dialog and falling back to the system default printer.
    /// </summary>
    private string? ResolvePrinterName()
    {
        if (!string.IsNullOrEmpty(PrintQueue?.FullName))
        {
            return PrintQueue!.FullName;
        }

        if (!string.IsNullOrEmpty(PrintQueue?.Name))
        {
            return PrintQueue!.Name;
        }

        return GetDefaultPrinterName();
    }

    /// <summary>
    /// Resolves the inclusive zero-based page range to print for a document,
    /// honoring the dialog's <see cref="PageRangeSelection"/>.
    /// </summary>
    private (int First, int Last) ResolveDocumentPageRange(int pageCount)
    {
        if (pageCount <= 0)
        {
            return (0, -1);
        }

        if (PageRangeSelection == PageRangeSelection.UserPages)
        {
            var first = Math.Clamp(PageRangeFrom - 1, 0, pageCount - 1);
            var last = Math.Clamp(PageRangeTo - 1, first, pageCount - 1);
            return (first, last);
        }

        if (PageRangeSelection == PageRangeSelection.CurrentPage)
        {
            var current = Math.Clamp(CurrentPage - 1, 0, pageCount - 1);
            return (current, current);
        }

        return (0, pageCount - 1);
    }

    /// <summary>
    /// Renders a single visual onto the supplied printer device context as one
    /// page, scaling the rasterized output to fit the printable area.
    /// </summary>
    /// <param name="hdc">The printer device context.</param>
    /// <param name="visual">The visual to render.</param>
    /// <param name="explicitPageSize">An optional logical page size override.</param>
    private void PrintVisualPage(nint hdc, Visual visual, Size explicitPageSize = default)
    {
        var sourceSize = MeasureVisualForPrint(visual, explicitPageSize);
        var bitmapWidth = Math.Max(1, (int)Math.Ceiling(sourceSize.Width));
        var bitmapHeight = Math.Max(1, (int)Math.Ceiling(sourceSize.Height));

        // Rasterize the visual into a BGRA software bitmap.
        var renderTarget = new RenderTargetBitmap(bitmapWidth, bitmapHeight, 96.0, 96.0, PixelFormat.Bgra32);
        renderTarget.Clear(Color.White);
        renderTarget.Render(visual);

        var pixels = new byte[bitmapWidth * bitmapHeight * 4];
        renderTarget.CopyPixels(new Int32Rect(0, 0, bitmapWidth, bitmapHeight), pixels, bitmapWidth * 4, 0);

        // The Win32 StretchDIBits expects a bottom-up DIB. Flip the rows and
        // composite onto white so transparent pixels do not print as black.
        var dib = BuildBottomUpBgrDib(pixels, bitmapWidth, bitmapHeight);

        if (PrintingNativeMethods.StartPage(hdc) <= 0)
        {
            throw new PrintDialogException("StartPage failed while printing a page.");
        }

        try
        {
            var printableWidth = PrintingNativeMethods.GetDeviceCaps(hdc, PrintingNativeMethods.HORZRES);
            var printableHeight = PrintingNativeMethods.GetDeviceCaps(hdc, PrintingNativeMethods.VERTRES);

            if (printableWidth <= 0 || printableHeight <= 0)
            {
                // Fall back to a 1:1 mapping when the device reports no metrics.
                printableWidth = bitmapWidth;
                printableHeight = bitmapHeight;
            }

            // Scale the bitmap proportionally so it fits inside the printable
            // area without distortion.
            var scale = Math.Min(
                printableWidth / (double)bitmapWidth,
                printableHeight / (double)bitmapHeight);
            scale = scale > 0 ? scale : 1.0;

            var destWidth = Math.Max(1, (int)Math.Round(bitmapWidth * scale));
            var destHeight = Math.Max(1, (int)Math.Round(bitmapHeight * scale));
            var destX = Math.Max(0, (printableWidth - destWidth) / 2);
            var destY = Math.Max(0, (printableHeight - destHeight) / 2);

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bitmapWidth,
                biHeight = bitmapHeight, // positive => bottom-up DIB
                biPlanes = 1,
                biBitCount = 24,
                biCompression = PrintingNativeMethods.BI_RGB,
                biSizeImage = (uint)dib.Length
            };

            var result = PrintingNativeMethods.StretchDIBits(
                hdc,
                destX, destY, destWidth, destHeight,
                0, 0, bitmapWidth, bitmapHeight,
                dib,
                ref header,
                PrintingNativeMethods.DIB_RGB_COLORS,
                PrintingNativeMethods.SRCCOPY);

            if (result == 0)
            {
                throw new PrintDialogException("StretchDIBits failed while printing a page.");
            }
        }
        finally
        {
            PrintingNativeMethods.EndPage(hdc);
        }
    }

    /// <summary>
    /// Determines the pixel size to rasterize a visual at, performing a layout
    /// pass when the visual is a <see cref="UIElement"/> that has not yet been
    /// measured and arranged.
    /// </summary>
    private Size MeasureVisualForPrint(Visual visual, Size explicitPageSize)
    {
        // Prefer an explicitly supplied page size (document pagination).
        if (!explicitPageSize.IsEmpty && explicitPageSize.Width > 0 && explicitPageSize.Height > 0)
        {
            EnsureLayout(visual, explicitPageSize);
            return explicitPageSize;
        }

        if (visual is UIElement element)
        {
            // Use the already-arranged render size when available.
            if (element.RenderSize.Width > 0 && element.RenderSize.Height > 0)
            {
                return element.RenderSize;
            }

            // Otherwise force a layout pass at the configured printable area.
            var area = new Size(
                Math.Max(1.0, PrintableAreaWidth),
                Math.Max(1.0, PrintableAreaHeight));
            EnsureLayout(visual, area);

            if (element.RenderSize.Width > 0 && element.RenderSize.Height > 0)
            {
                return element.RenderSize;
            }

            if (element.DesiredSize.Width > 0 && element.DesiredSize.Height > 0)
            {
                return element.DesiredSize;
            }

            return area;
        }

        // Non-UIElement visuals: fall back to the printable page size.
        return new Size(
            Math.Max(1.0, PrintableAreaWidth),
            Math.Max(1.0, PrintableAreaHeight));
    }

    /// <summary>
    /// Ensures that a visual has a valid layout (measure + arrange) for the
    /// supplied page size before it is rasterized for printing.
    /// </summary>
    private static void EnsureLayout(Visual visual, Size pageSize)
    {
        if (visual is not UIElement element)
        {
            return;
        }

        if (!element.IsMeasureValid)
        {
            element.Measure(pageSize);
        }

        if (!element.IsArrangeValid)
        {
            var width = element.DesiredSize.Width > 0 ? element.DesiredSize.Width : pageSize.Width;
            var height = element.DesiredSize.Height > 0 ? element.DesiredSize.Height : pageSize.Height;
            element.Arrange(new Rect(0, 0, width, height));
        }
    }

    /// <summary>
    /// Converts a top-down BGRA pixel buffer into a bottom-up 24-bit BGR
    /// device-independent bitmap, compositing translucent pixels over white.
    /// </summary>
    private static byte[] BuildBottomUpBgrDib(byte[] bgra, int width, int height)
    {
        // Each scan line of a DIB must be padded to a 4-byte boundary.
        var srcStride = width * 4;
        var dstStride = (width * 3 + 3) & ~3;
        var dib = new byte[dstStride * height];

        for (var y = 0; y < height; y++)
        {
            // Bottom-up: source row y maps to destination row (height - 1 - y).
            var srcRow = y * srcStride;
            var dstRow = (height - 1 - y) * dstStride;

            for (var x = 0; x < width; x++)
            {
                var srcIndex = srcRow + (x * 4);
                var b = bgra[srcIndex];
                var g = bgra[srcIndex + 1];
                var r = bgra[srcIndex + 2];
                var a = bgra[srcIndex + 3];

                // Composite over white so transparent areas print as paper.
                if (a < 255)
                {
                    var inverse = 255 - a;
                    b = (byte)((b * a + 255 * inverse) / 255);
                    g = (byte)((g * a + 255 * inverse) / 255);
                    r = (byte)((r * a + 255 * inverse) / 255);
                }

                var dstIndex = dstRow + (x * 3);
                dib[dstIndex] = b;
                dib[dstIndex + 1] = g;
                dib[dstIndex + 2] = r;
            }
        }

        return dib;
    }

    /// <summary>
    /// Retrieves the name of the system default printer, or <see langword="null"/>
    /// when no default printer is configured.
    /// </summary>
    private static string? GetDefaultPrinterName()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        uint size = 0;
        PrintingNativeMethods.GetDefaultPrinter(null, ref size);
        if (size == 0)
        {
            return null;
        }

        var buffer = new char[size];
        if (!PrintingNativeMethods.GetDefaultPrinter(buffer, ref size))
        {
            return null;
        }

        return new string(buffer, 0, (int)Math.Max(0, size - 1));
    }

    #endregion
}

}

namespace System.Printing
{

/// <summary>Provides the common lifecycle contract for print-system objects.</summary>
public abstract class PrintSystemObject : IDisposable
{
    private bool _disposed;
    private string _name = string.Empty;

    protected PrintSystemObject()
    {
    }

    protected PrintSystemObject(PrintSystemObjectLoadMode mode)
    {
    }

    public virtual string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    public PrintSystemObject? Parent { get; internal set; }

    public virtual void Commit()
    {
    }

    public virtual void Refresh()
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>Provides the common lifecycle contract for print-system collections.</summary>
public abstract class PrintSystemObjects : IDisposable
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

public enum PrintSystemObjectLoadMode
{
    None = 0,
    LoadUninitialized = 1,
    LoadInitialized = 2,
}

public enum PrintSystemDesiredAccess
{
    None = 0,
    EnumerateServer = 131074,
    UsePrinter = 131080,
    AdministrateServer = 983041,
    AdministratePrinter = 983052,
}

public enum PrintQueueIndexedProperty
{
    Name = 0,
    ShareName = 1,
    Comment = 2,
    Location = 3,
    Description = 4,
    Priority = 5,
    DefaultPriority = 6,
    StartTimeOfDay = 7,
    UntilTimeOfDay = 8,
    AveragePagesPerMinute = 9,
    NumberOfJobs = 10,
    QueueAttributes = 11,
    QueueDriver = 12,
    QueuePort = 13,
    QueuePrintProcessor = 14,
    HostingPrintServer = 15,
    QueueStatus = 16,
    SeparatorFile = 17,
    UserPrintTicket = 18,
    DefaultPrintTicket = 19,
}

public enum PrintServerIndexedProperty
{
    DefaultSpoolDirectory = 0,
    PortThreadPriority = 1,
    DefaultPortThreadPriority = 2,
    SchedulerPriority = 3,
    DefaultSchedulerPriority = 4,
    BeepEnabled = 5,
    NetPopup = 6,
    EventLog = 7,
    MajorVersion = 8,
    MinorVersion = 9,
    RestartJobOnPoolTimeout = 10,
    RestartJobOnPoolEnabled = 11,
}

[Flags]
public enum EnumeratedPrintQueueTypes
{
    Queued = 1,
    DirectPrinting = 2,
    Shared = 8,
    Connections = 16,
    Local = 64,
    EnableDevQuery = 128,
    KeepPrintedJobs = 256,
    WorkOffline = 1024,
    EnableBidi = 2048,
    RawOnly = 4096,
    PublishedInDirectoryServices = 8192,
    Fax = 16384,
    TerminalServer = 32768,
    PushedUserConnection = 131072,
    PushedMachineConnection = 262144,
}

/// <summary>
/// Represents a print queue (printer).
/// </summary>
public class PrintQueue : PrintSystemObject
{
    /// <summary>
    /// Gets the name of the printer.
    /// </summary>
    public override string Name { get; set; }

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
    internal bool IsOnline { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether this is the default printer.
    /// </summary>
    internal bool IsDefault { get; set; }

    /// <summary>
    /// Gets the default print ticket for this printer.
    /// </summary>
    public System.Printing.PrintTicket? DefaultPrintTicket { get; set; }

    /// <summary>Gets or sets the current user's print ticket.</summary>
    public PrintTicket? UserPrintTicket { get; set; }

    /// <summary>Gets the print server hosting this queue.</summary>
    public PrintServer HostingPrintServer { get; private set; }

    /// <summary>Initializes a queue on the specified print server.</summary>
    public PrintQueue(PrintServer printServer, string printQueueName)
        : this(printQueueName, printQueueName)
    {
        HostingPrintServer = printServer ?? throw new ArgumentNullException(nameof(printServer));
        Parent = printServer;
    }

    public PrintQueue(PrintServer printServer, string printQueueName, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, PrintQueueIndexedProperty[] propertyFilter)
        : this(printServer, printQueueName)
    {
        ArgumentNullException.ThrowIfNull(propertyFilter);
    }

    public PrintQueue(PrintServer printServer, string printQueueName, string[] propertyFilter)
        : this(printServer, printQueueName)
    {
        ArgumentNullException.ThrowIfNull(propertyFilter);
    }

    public PrintQueue(PrintServer printServer, string printQueueName, int printSchemaVersion)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, PrintQueueIndexedProperty[] propertyFilter, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName, propertyFilter)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, string[] propertyFilter, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName, propertyFilter)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, int printSchemaVersion, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName, printSchemaVersion)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PrintQueue class.
    /// </summary>
    internal PrintQueue(string name)
    {
        Name = name;
        FullName = name;
        HostingPrintServer = new PrintServer();
    }

    /// <summary>
    /// Initializes a new instance of the PrintQueue class.
    /// </summary>
    internal PrintQueue(string name, string fullName)
    {
        Name = name;
        FullName = fullName;
        HostingPrintServer = new PrintServer();
    }

    /// <summary>
    /// Gets the currently installed print queues by enumerating the local and
    /// connected printers of the machine.
    /// </summary>
    /// <returns>A collection of print queues installed on the local machine.</returns>
    internal static IEnumerable<PrintQueue> GetPrintQueues()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Empty<PrintQueue>();
        }

        return EnumerateWindowsPrintQueues();
    }

    /// <summary>
    /// Gets the default print queue.
    /// </summary>
    /// <returns>The default print queue, or <see langword="null"/> when none is configured.</returns>
    internal static PrintQueue? GetDefaultPrintQueue()
    {
        var queues = GetPrintQueues().ToList();
        return queues.FirstOrDefault(q => q.IsDefault) ?? queues.FirstOrDefault();
    }

    /// <summary>
    /// Enumerates the installed Windows printers via <c>EnumPrinters</c> at
    /// information level 4 and marks the system default printer.
    /// </summary>
    private static List<PrintQueue> EnumerateWindowsPrintQueues()
    {
        var result = new List<PrintQueue>();

        const uint flags = PrintingNativeMethods.PRINTER_ENUM_LOCAL |
                           PrintingNativeMethods.PRINTER_ENUM_CONNECTIONS;
        const uint level = 4;

        // First call discovers the required buffer size.
        PrintingNativeMethods.EnumPrinters(flags, null, level, nint.Zero, 0, out var bytesNeeded, out _);
        if (bytesNeeded == 0)
        {
            return result;
        }

        var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
        try
        {
            if (!PrintingNativeMethods.EnumPrinters(
                    flags, null, level, buffer, bytesNeeded, out _, out var count))
            {
                return result;
            }

            var defaultPrinterName = GetDefaultPrinterNameStatic();
            var entrySize = Marshal.SizeOf<PrintingNativeMethods.PRINTER_INFO_4>();

            for (var i = 0; i < count; i++)
            {
                var entryPtr = buffer + (i * entrySize);
                var info = Marshal.PtrToStructure<PrintingNativeMethods.PRINTER_INFO_4>(entryPtr);

                if (string.IsNullOrEmpty(info.pPrinterName))
                {
                    continue;
                }

                var queue = new PrintQueue(info.pPrinterName, info.pPrinterName)
                {
                    IsDefault = string.Equals(
                        info.pPrinterName,
                        defaultPrinterName,
                        StringComparison.OrdinalIgnoreCase),
                    IsOnline = true
                };

                // Enrich the queue with the descriptive metadata exposed by
                // PRINTER_INFO_2 (comment / location). Failure is non-fatal.
                PopulateQueueDetails(queue);

                result.Add(queue);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    /// <summary>
    /// Populates the description and location of a print queue from the
    /// <c>PRINTER_INFO_2</c> metadata of the underlying Windows printer.
    /// </summary>
    private static void PopulateQueueDetails(PrintQueue queue)
    {
        if (!PrintingNativeMethods.OpenPrinter(queue.FullName, out var handle, nint.Zero) ||
            handle == nint.Zero)
        {
            return;
        }

        try
        {
            PrintingNativeMethods.GetPrinter(handle, 2, nint.Zero, 0, out var needed);
            if (needed == 0)
            {
                return;
            }

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (PrintingNativeMethods.GetPrinter(handle, 2, buffer, needed, out _))
                {
                    var info = Marshal.PtrToStructure<PrintingNativeMethods.PRINTER_INFO_2>(buffer);
                    queue.Description = info.pComment ?? string.Empty;
                    queue.Location = info.pLocation ?? string.Empty;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            PrintingNativeMethods.ClosePrinter(handle);
        }
    }

    /// <summary>
    /// Retrieves the system default printer name for the local machine.
    /// </summary>
    private static string? GetDefaultPrinterNameStatic()
    {
        uint size = 0;
        PrintingNativeMethods.GetDefaultPrinter(null, ref size);
        if (size == 0)
        {
            return null;
        }

        var buffer = new char[size];
        if (!PrintingNativeMethods.GetDefaultPrinter(buffer, ref size))
        {
            return null;
        }

        return new string(buffer, 0, (int)Math.Max(0, size - 1));
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
            CollationCapability = Array.AsReadOnly(new[] { Collation.Uncollated, Collation.Collated }),
            DuplexingCapability = Array.AsReadOnly(new[] { Duplexing.OneSided, Duplexing.TwoSidedLongEdge, Duplexing.TwoSidedShortEdge }),
            PageOrientationCapability = Array.AsReadOnly(new[] { PageOrientation.Portrait, PageOrientation.Landscape }),
            OutputQualityCapability = Array.AsReadOnly(new[] { OutputQuality.Draft, OutputQuality.Normal, OutputQuality.High }),
            OutputColorCapability = Array.AsReadOnly(new[] { OutputColor.Color, OutputColor.Grayscale, OutputColor.Monochrome }),
            PageMediaSizeCapability = Array.AsReadOnly(new[]
            {
                new PageMediaSize(PageMediaSizeName.NorthAmericaLetter, 816, 1056),
                new PageMediaSize(PageMediaSizeName.NorthAmericaLegal, 816, 1344),
                new PageMediaSize(PageMediaSizeName.ISOA4, 794, 1123),
                new PageMediaSize(PageMediaSizeName.ISOA3, 1123, 1587)
            }),
            PageResolutionCapability = Array.AsReadOnly(new[]
            {
                new PageResolution(300, 300),
                new PageResolution(600, 600),
                new PageResolution(1200, 1200)
            }),
            MaxCopyCount = 999
        };
    }

    /// <summary>
    /// Creates an XpsDocumentWriter for this print queue.
    /// </summary>
    /// <returns>An XpsDocumentWriter for this queue.</returns>
    internal XpsDocumentWriter CreateXpsDocumentWriter()
    {
        return new XpsDocumentWriter(this);
    }

    /// <summary>
    /// Submits a print job.
    /// </summary>
    /// <param name="jobName">The name of the print job.</param>
    /// <returns>A print job object.</returns>
    public PrintSystemJobInfo AddJob(string jobName)
    {
        // Platform-specific implementation
        return new PrintSystemJobInfo(this, jobName);
    }

    /// <summary>Adds an unnamed print job.</summary>
    public PrintSystemJobInfo AddJob() => AddJob("Print job");

    /// <summary>
    /// Gets all print jobs currently queued for this printer.
    /// </summary>
    /// <returns>A collection of print jobs.</returns>
    public PrintJobInfoCollection GetPrintJobInfoCollection()
    {
        var collection = new PrintJobInfoCollection(this, Array.Empty<string>());
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return collection;
        }

        foreach (PrintSystemJobInfo job in EnumerateWindowsJobs())
        {
            collection.Add(job);
        }

        return collection;
    }

    /// <summary>
    /// Pauses this printer so it stops processing queued jobs.
    /// </summary>
    /// <exception cref="PrintDialogException">Thrown when the printer cannot be paused.</exception>
    public void Pause()
    {
        ControlPrinter(
            PrintingNativeMethods.PRINTER_CONTROL_PAUSE,
            $"Failed to pause printer '{FullName}'.");
    }

    /// <summary>
    /// Resumes this printer after it has been paused.
    /// </summary>
    /// <exception cref="PrintDialogException">Thrown when the printer cannot be resumed.</exception>
    public void Resume()
    {
        ControlPrinter(
            PrintingNativeMethods.PRINTER_CONTROL_RESUME,
            $"Failed to resume printer '{FullName}'.");
    }

    /// <summary>
    /// Purges all jobs currently queued for this printer.
    /// </summary>
    /// <exception cref="PrintDialogException">Thrown when the queue cannot be purged.</exception>
    public void Purge()
    {
        ControlPrinter(
            PrintingNativeMethods.PRINTER_CONTROL_PURGE,
            $"Failed to purge printer '{FullName}'.");
    }

    /// <summary>
    /// Opens this printer with administrative access and issues a
    /// <c>SetPrinter</c> control command (pause / resume / purge).
    /// </summary>
    private void ControlPrinter(uint command, string failureMessage)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // The spooler control API is Windows-only; treat as a no-op
            // elsewhere so cross-platform callers are not forced to branch.
            return;
        }

        var defaults = new PrintingNativeMethods.PRINTER_DEFAULTS
        {
            DesiredAccess = PrintingNativeMethods.PRINTER_ALL_ACCESS
        };

        if (!PrintingNativeMethods.OpenPrinter(FullName, out var handle, ref defaults) ||
            handle == nint.Zero)
        {
            throw new PrintDialogException(
                $"{failureMessage} The printer could not be opened with administrative access.");
        }

        try
        {
            // SetPrinter with level 0 and a NULL printer pointer applies the
            // control command (pause / resume / purge) to the whole queue.
            if (!PrintingNativeMethods.SetPrinter(handle, 0, nint.Zero, command))
            {
                throw new PrintDialogException(failureMessage);
            }
        }
        finally
        {
            PrintingNativeMethods.ClosePrinter(handle);
        }
    }

    /// <summary>
    /// Enumerates the jobs of this printer via <c>EnumJobs</c> at level 2 and
    /// projects them onto <see cref="PrintSystemJobInfo"/> instances.
    /// </summary>
    private List<PrintSystemJobInfo> EnumerateWindowsJobs()
    {
        var jobs = new List<PrintSystemJobInfo>();

        if (!PrintingNativeMethods.OpenPrinter(FullName, out var handle, nint.Zero) ||
            handle == nint.Zero)
        {
            return jobs;
        }

        try
        {
            const uint level = 2;

            // First call discovers the buffer size; up to 256 jobs are read.
            PrintingNativeMethods.EnumJobs(handle, 0, 256, level, nint.Zero, 0, out var needed, out _);
            if (needed == 0)
            {
                return jobs;
            }

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!PrintingNativeMethods.EnumJobs(
                        handle, 0, 256, level, buffer, needed, out _, out var count))
                {
                    return jobs;
                }

                var entrySize = Marshal.SizeOf<PrintingNativeMethods.JOB_INFO_2>();
                for (var i = 0; i < count; i++)
                {
                    var entryPtr = buffer + (i * entrySize);
                    var info = Marshal.PtrToStructure<PrintingNativeMethods.JOB_INFO_2>(entryPtr);

                    jobs.Add(new PrintSystemJobInfo(this, info.pDocument ?? string.Empty)
                    {
                        JobStatus = TranslateJobStatus(info.Status),
                        NumberOfPages = (int)info.TotalPages,
                        NumberOfPagesPrinted = (int)info.PagesPrinted
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            PrintingNativeMethods.ClosePrinter(handle);
        }

        return jobs;
    }

    /// <summary>
    /// Translates a Win32 <c>JOB_STATUS_*</c> flag set into the framework
    /// <see cref="PrintJobStatus"/> flags enumeration.
    /// </summary>
    private static PrintJobStatus TranslateJobStatus(uint status)
    {
        var result = PrintJobStatus.None;

        if ((status & PrintingNativeMethods.JOB_STATUS_PAUSED) != 0) result |= PrintJobStatus.Paused;
        if ((status & PrintingNativeMethods.JOB_STATUS_ERROR) != 0) result |= PrintJobStatus.Error;
        if ((status & PrintingNativeMethods.JOB_STATUS_DELETING) != 0) result |= PrintJobStatus.Deleting;
        if ((status & PrintingNativeMethods.JOB_STATUS_SPOOLING) != 0) result |= PrintJobStatus.Spooling;
        if ((status & PrintingNativeMethods.JOB_STATUS_PRINTING) != 0) result |= PrintJobStatus.Printing;
        if ((status & PrintingNativeMethods.JOB_STATUS_OFFLINE) != 0) result |= PrintJobStatus.Offline;
        if ((status & PrintingNativeMethods.JOB_STATUS_PAPEROUT) != 0) result |= PrintJobStatus.PaperOut;
        if ((status & PrintingNativeMethods.JOB_STATUS_PRINTED) != 0) result |= PrintJobStatus.Printed;
        if ((status & PrintingNativeMethods.JOB_STATUS_DELETED) != 0) result |= PrintJobStatus.Deleted;
        if ((status & PrintingNativeMethods.JOB_STATUS_USER_INTERVENTION) != 0) result |= PrintJobStatus.UserIntervention;
        if ((status & PrintingNativeMethods.JOB_STATUS_RESTART) != 0) result |= PrintJobStatus.Restarted;
        if ((status & PrintingNativeMethods.JOB_STATUS_COMPLETE) != 0) result |= PrintJobStatus.Completed;
        if ((status & PrintingNativeMethods.JOB_STATUS_RETAINED) != 0) result |= PrintJobStatus.Retained;

        return result;
    }
}

/// <summary>
/// Represents information about a print job.
/// </summary>
public class PrintSystemJobInfo : PrintSystemObject
{
    /// <summary>
    /// Gets the print queue associated with this job.
    /// </summary>
    public PrintQueue HostingPrintQueue { get; set; }

    /// <summary>Gets or sets the print server hosting this job.</summary>
    public PrintServer HostingPrintServer { get; set; }

    /// <summary>
    /// Gets the name of the print job.
    /// </summary>
    public string JobName { get; set; }

    /// <summary>
    /// Gets the job identifier.
    /// </summary>
    public int JobIdentifier { get; set; }

    /// <summary>
    /// Gets the status of the print job.
    /// </summary>
    public PrintJobStatus JobStatus { get; set; }

    /// <summary>
    /// Gets the number of pages printed.
    /// </summary>
    public int NumberOfPagesPrinted { get; set; }

    /// <summary>
    /// Gets the total number of pages in the job.
    /// </summary>
    public int NumberOfPages { get; set; }

    /// <summary>
    /// Gets the time the job was submitted.
    /// </summary>
    public DateTime TimeJobSubmitted { get; set; }

    public bool IsBlocked => (JobStatus & PrintJobStatus.Blocked) != 0;
    public bool IsCompleted => (JobStatus & PrintJobStatus.Completed) != 0;
    public bool IsDeleted => (JobStatus & PrintJobStatus.Deleted) != 0;
    public bool IsDeleting => (JobStatus & PrintJobStatus.Deleting) != 0;
    public bool IsInError => (JobStatus & PrintJobStatus.Error) != 0;
    public bool IsOffline => (JobStatus & PrintJobStatus.Offline) != 0;
    public bool IsPaperOut => (JobStatus & PrintJobStatus.PaperOut) != 0;
    public bool IsPaused => (JobStatus & PrintJobStatus.Paused) != 0;
    public bool IsPrinted => (JobStatus & PrintJobStatus.Printed) != 0;
    public bool IsPrinting => (JobStatus & PrintJobStatus.Printing) != 0;
    public bool IsRestarted => (JobStatus & PrintJobStatus.Restarted) != 0;
    public bool IsRetained => (JobStatus & PrintJobStatus.Retained) != 0;
    public bool IsSpooling => (JobStatus & PrintJobStatus.Spooling) != 0;
    public bool IsUserInterventionRequired => (JobStatus & PrintJobStatus.UserIntervention) != 0;

    /// <summary>
    /// Initializes a new instance of the PrintSystemJobInfo class.
    /// </summary>
    internal PrintSystemJobInfo(PrintQueue queue, string jobName)
    {
        HostingPrintQueue = queue;
        HostingPrintServer = queue.HostingPrintServer;
        Parent = queue;
        Name = jobName;
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
    Blocked = 512,

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
public sealed class PrintTicket : INotifyPropertyChanged
{
    private byte[]? _sourceXml;
    private Collation? _collation;
    private int? _copyCount;
    private Duplexing? _duplexing;
    private InputBin? _inputBin;
    private OutputColor? _outputColor;
    private OutputQuality? _outputQuality;
    private PageMediaSize? _pageMediaSize;
    private PageOrder? _pageOrder;
    private PageOrientation? _pageOrientation;
    private PageResolution? _pageResolution;
    private int? _pagesPerSheet;
    private Stapling? _stapling;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Initializes an empty print ticket.</summary>
    public PrintTicket()
    {
    }

    /// <summary>Initializes a print ticket from its XML representation.</summary>
    public PrintTicket(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);
        if (!xmlStream.CanRead)
        {
            throw new ArgumentException("The print-ticket stream must be readable.", nameof(xmlStream));
        }

        using var copy = new MemoryStream();
        xmlStream.CopyTo(copy);
        _sourceXml = copy.ToArray();
    }

    public Collation? Collation
    {
        get => _collation;
        set => SetValue(ref _collation, value);
    }

    public int? CopyCount
    {
        get => _copyCount;
        set => SetValue(ref _copyCount, value);
    }

    public Duplexing? Duplexing
    {
        get => _duplexing;
        set => SetValue(ref _duplexing, value);
    }

    public InputBin? InputBin
    {
        get => _inputBin;
        set => SetValue(ref _inputBin, value);
    }

    public OutputColor? OutputColor
    {
        get => _outputColor;
        set => SetValue(ref _outputColor, value);
    }

    public OutputQuality? OutputQuality
    {
        get => _outputQuality;
        set => SetValue(ref _outputQuality, value);
    }

    public PageMediaSize? PageMediaSize
    {
        get => _pageMediaSize;
        set => SetValue(ref _pageMediaSize, value);
    }

    public PageOrder? PageOrder
    {
        get => _pageOrder;
        set => SetValue(ref _pageOrder, value);
    }

    public PageOrientation? PageOrientation
    {
        get => _pageOrientation;
        set => SetValue(ref _pageOrientation, value);
    }

    public PageResolution? PageResolution
    {
        get => _pageResolution;
        set => SetValue(ref _pageResolution, value);
    }

    public int? PagesPerSheet
    {
        get => _pagesPerSheet;
        set => SetValue(ref _pagesPerSheet, value);
    }

    public Stapling? Stapling
    {
        get => _stapling;
        set => SetValue(ref _stapling, value);
    }

    /// <summary>Creates an independent copy of this ticket.</summary>
    public PrintTicket Clone()
    {
        return new PrintTicket
        {
            _sourceXml = _sourceXml?.ToArray(),
            _collation = _collation,
            _copyCount = _copyCount,
            _duplexing = _duplexing,
            _inputBin = _inputBin,
            _outputColor = _outputColor,
            _outputQuality = _outputQuality,
            _pageMediaSize = _pageMediaSize,
            _pageOrder = _pageOrder,
            _pageOrientation = _pageOrientation,
            _pageResolution = _pageResolution,
            _pagesPerSheet = _pagesPerSheet,
            _stapling = _stapling,
        };
    }

    /// <summary>Gets the XML representation supplied to this ticket.</summary>
    public MemoryStream GetXmlStream() => new(_sourceXml ?? CreateMinimalPrintTicketXml(), writable: false);

    /// <summary>Saves the XML representation to a writable stream.</summary>
    public void SaveTo(Stream outStream)
    {
        ArgumentNullException.ThrowIfNull(outStream);
        if (!outStream.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(outStream));
        }

        using MemoryStream source = GetXmlStream();
        source.CopyTo(outStream);
    }

    private void SetValue<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        _sourceXml = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static byte[] CreateMinimalPrintTicketXml() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><psf:PrintTicket xmlns:psf=\"http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework\" version=\"1\" />"u8.ToArray();
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
    /// Initializes a media size identified by a standard Print Schema name.
    /// </summary>
    public PageMediaSize(PageMediaSizeName mediaSizeName)
    {
        PageMediaSizeName = mediaSizeName;
    }

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

    /// <inheritdoc />
    public override string ToString() => PageMediaSizeName?.ToString() ?? $"{Width} x {Height}";
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

    /// <summary>Gets the qualitative resolution.</summary>
    public PageQualitativeResolution? QualitativeResolution { get; }

    /// <summary>Initializes a qualitative page resolution.</summary>
    public PageResolution(PageQualitativeResolution qualitative)
    {
        QualitativeResolution = qualitative;
    }

    /// <summary>
    /// Initializes a new instance of the PageResolution class.
    /// </summary>
    public PageResolution(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Initializes a numeric and qualitative page resolution.</summary>
    public PageResolution(int resolutionX, int resolutionY, PageQualitativeResolution qualitative)
    {
        X = resolutionX;
        Y = resolutionY;
        QualitativeResolution = qualitative;
    }

    /// <inheritdoc />
    public override string ToString() =>
        X is not null && Y is not null ? $"{X} x {Y}" : QualitativeResolution?.ToString() ?? string.Empty;
}

/// <summary>Specifies a qualitative page-resolution setting.</summary>
public enum PageQualitativeResolution
{
    Unknown = 0,
    Default = 1,
    Draft = 2,
    High = 3,
    Normal = 4,
    Other = 5,
}

/// <summary>
/// Specifies the collation setting.
/// </summary>
public enum Collation
{
    Unknown = 0,
    Collated = 1,
    Uncollated = 2,
}

/// <summary>
/// Specifies the duplex printing mode.
/// </summary>
public enum Duplexing
{
    Unknown = 0,
    OneSided = 1,
    TwoSidedShortEdge = 2,
    TwoSidedLongEdge = 3,
}

/// <summary>
/// Specifies the page orientation.
/// </summary>
public enum PageOrientation
{
    Unknown = 0,
    Landscape = 1,
    Portrait = 2,
    ReverseLandscape = 3,
    ReversePortrait = 4,
}

/// <summary>
/// Specifies the output quality.
/// </summary>
public enum OutputQuality
{
    Unknown = 0,
    Automatic = 1,
    Draft = 2,
    Fax = 3,
    High = 4,
    Normal = 5,
    Photographic = 6,
    Text = 7,
}

/// <summary>
/// Specifies the output color.
/// </summary>
public enum OutputColor
{
    Unknown = 0,
    Color = 1,
    Grayscale = 2,
    Monochrome = 3,
}

/// <summary>
/// Specifies standard page media sizes.
/// </summary>
public enum PageMediaSizeName
{
    Unknown = 0,
    ISOA0 = 1,
    ISOA1 = 2,
    ISOA10 = 3,
    ISOA2 = 4,
    ISOA3 = 5,
    ISOA3Rotated = 6,
    ISOA3Extra = 7,
    ISOA4 = 8,
    ISOA4Rotated = 9,
    ISOA4Extra = 10,
    ISOA5 = 11,
    ISOA5Rotated = 12,
    ISOA5Extra = 13,
    ISOA6 = 14,
    ISOA6Rotated = 15,
    ISOA7 = 16,
    ISOA8 = 17,
    ISOA9 = 18,
    ISOB0 = 19,
    ISOB1 = 20,
    ISOB10 = 21,
    ISOB2 = 22,
    ISOB3 = 23,
    ISOB4 = 24,
    ISOB4Envelope = 25,
    ISOB5Envelope = 26,
    ISOB5Extra = 27,
    ISOB7 = 28,
    ISOB8 = 29,
    ISOB9 = 30,
    ISOC0 = 31,
    ISOC1 = 32,
    ISOC10 = 33,
    ISOC2 = 34,
    ISOC3 = 35,
    ISOC3Envelope = 36,
    ISOC4 = 37,
    ISOC4Envelope = 38,
    ISOC5 = 39,
    ISOC5Envelope = 40,
    ISOC6 = 41,
    ISOC6Envelope = 42,
    ISOC6C5Envelope = 43,
    ISOC7 = 44,
    ISOC8 = 45,
    ISOC9 = 46,
    ISODLEnvelope = 47,
    ISODLEnvelopeRotated = 48,
    ISOSRA3 = 49,
    JapanQuadrupleHagakiPostcard = 50,
    JISB0 = 51,
    JISB1 = 52,
    JISB10 = 53,
    JISB2 = 54,
    JISB3 = 55,
    JISB4 = 56,
    JISB4Rotated = 57,
    JISB5 = 58,
    JISB5Rotated = 59,
    JISB6 = 60,
    JISB6Rotated = 61,
    JISB7 = 62,
    JISB8 = 63,
    JISB9 = 64,
    JapanChou3Envelope = 65,
    JapanChou3EnvelopeRotated = 66,
    JapanChou4Envelope = 67,
    JapanChou4EnvelopeRotated = 68,
    JapanHagakiPostcard = 69,
    JapanHagakiPostcardRotated = 70,
    JapanKaku2Envelope = 71,
    JapanKaku2EnvelopeRotated = 72,
    JapanKaku3Envelope = 73,
    JapanKaku3EnvelopeRotated = 74,
    JapanYou4Envelope = 75,
    NorthAmerica10x11 = 76,
    NorthAmerica10x14 = 77,
    NorthAmerica11x17 = 78,
    NorthAmerica9x11 = 79,
    NorthAmericaArchitectureASheet = 80,
    NorthAmericaArchitectureBSheet = 81,
    NorthAmericaArchitectureCSheet = 82,
    NorthAmericaArchitectureDSheet = 83,
    NorthAmericaArchitectureESheet = 84,
    NorthAmericaCSheet = 85,
    NorthAmericaDSheet = 86,
    NorthAmericaESheet = 87,
    NorthAmericaExecutive = 88,
    NorthAmericaGermanLegalFanfold = 89,
    NorthAmericaGermanStandardFanfold = 90,
    NorthAmericaLegal = 91,
    NorthAmericaLegalExtra = 92,
    NorthAmericaLetter = 93,
    NorthAmericaLetterRotated = 94,
    NorthAmericaLetterExtra = 95,
    NorthAmericaLetterPlus = 96,
    NorthAmericaMonarchEnvelope = 97,
    NorthAmericaNote = 98,
    NorthAmericaNumber10Envelope = 99,
    NorthAmericaNumber10EnvelopeRotated = 100,
    NorthAmericaNumber9Envelope = 101,
    NorthAmericaNumber11Envelope = 102,
    NorthAmericaNumber12Envelope = 103,
    NorthAmericaNumber14Envelope = 104,
    NorthAmericaPersonalEnvelope = 105,
    NorthAmericaQuarto = 106,
    NorthAmericaStatement = 107,
    NorthAmericaSuperA = 108,
    NorthAmericaSuperB = 109,
    NorthAmericaTabloid = 110,
    NorthAmericaTabloidExtra = 111,
    OtherMetricA4Plus = 112,
    OtherMetricA3Plus = 113,
    OtherMetricFolio = 114,
    OtherMetricInviteEnvelope = 115,
    OtherMetricItalianEnvelope = 116,
    PRC1Envelope = 117,
    PRC1EnvelopeRotated = 118,
    PRC10Envelope = 119,
    PRC10EnvelopeRotated = 120,
    PRC16K = 121,
    PRC16KRotated = 122,
    PRC2Envelope = 123,
    PRC2EnvelopeRotated = 124,
    PRC32K = 125,
    PRC32KRotated = 126,
    PRC32KBig = 127,
    PRC3Envelope = 128,
    PRC3EnvelopeRotated = 129,
    PRC4Envelope = 130,
    PRC4EnvelopeRotated = 131,
    PRC5Envelope = 132,
    PRC5EnvelopeRotated = 133,
    PRC6Envelope = 134,
    PRC6EnvelopeRotated = 135,
    PRC7Envelope = 136,
    PRC7EnvelopeRotated = 137,
    PRC8Envelope = 138,
    PRC8EnvelopeRotated = 139,
    PRC9Envelope = 140,
    PRC9EnvelopeRotated = 141,
    Roll04Inch = 142,
    Roll06Inch = 143,
    Roll08Inch = 144,
    Roll12Inch = 145,
    Roll15Inch = 146,
    Roll18Inch = 147,
    Roll22Inch = 148,
    Roll24Inch = 149,
    Roll30Inch = 150,
    Roll36Inch = 151,
    Roll54Inch = 152,
    JapanDoubleHagakiPostcard = 153,
    JapanDoubleHagakiPostcardRotated = 154,
    JapanLPhoto = 155,
    Japan2LPhoto = 156,
    JapanYou1Envelope = 157,
    JapanYou2Envelope = 158,
    JapanYou3Envelope = 159,
    JapanYou4EnvelopeRotated = 160,
    JapanYou6Envelope = 161,
    JapanYou6EnvelopeRotated = 162,
    NorthAmerica4x6 = 163,
    NorthAmerica4x8 = 164,
    NorthAmerica5x7 = 165,
    NorthAmerica8x10 = 166,
    NorthAmerica10x12 = 167,
    NorthAmerica14x17 = 168,
    BusinessCard = 169,
    CreditCard = 170,
}

/// <summary>
/// Defines the capabilities of a print queue.
/// </summary>
public sealed class PrintCapabilities
{
    private static readonly ReadOnlyCollection<Collation> EmptyCollation = Array.AsReadOnly(Array.Empty<Collation>());
    private static readonly ReadOnlyCollection<Duplexing> EmptyDuplexing = Array.AsReadOnly(Array.Empty<Duplexing>());
    private static readonly ReadOnlyCollection<InputBin> EmptyInputBins = Array.AsReadOnly(Array.Empty<InputBin>());
    private static readonly ReadOnlyCollection<OutputColor> EmptyOutputColors = Array.AsReadOnly(Array.Empty<OutputColor>());
    private static readonly ReadOnlyCollection<OutputQuality> EmptyOutputQualities = Array.AsReadOnly(Array.Empty<OutputQuality>());
    private static readonly ReadOnlyCollection<PageMediaSize> EmptyMediaSizes = Array.AsReadOnly(Array.Empty<PageMediaSize>());
    private static readonly ReadOnlyCollection<PageOrder> EmptyPageOrders = Array.AsReadOnly(Array.Empty<PageOrder>());
    private static readonly ReadOnlyCollection<PageOrientation> EmptyOrientations = Array.AsReadOnly(Array.Empty<PageOrientation>());
    private static readonly ReadOnlyCollection<PageResolution> EmptyResolutions = Array.AsReadOnly(Array.Empty<PageResolution>());
    private static readonly ReadOnlyCollection<int> EmptyPagesPerSheet = Array.AsReadOnly(Array.Empty<int>());
    private static readonly ReadOnlyCollection<Stapling> EmptyStapling = Array.AsReadOnly(Array.Empty<Stapling>());

    /// <summary>Initializes capabilities from Print Schema XML.</summary>
    public PrintCapabilities(Stream xmlStream)
    {
        ArgumentNullException.ThrowIfNull(xmlStream);
        if (!xmlStream.CanRead)
        {
            throw new ArgumentException("The print-capabilities stream must be readable.", nameof(xmlStream));
        }
    }

    internal PrintCapabilities()
    {
    }

    /// <summary>
    /// Gets the collection of supported collation options.
    /// </summary>
    public ReadOnlyCollection<Collation> CollationCapability { get; internal set; } = EmptyCollation;

    /// <summary>
    /// Gets the collection of supported duplex options.
    /// </summary>
    public ReadOnlyCollection<Duplexing> DuplexingCapability { get; internal set; } = EmptyDuplexing;

    /// <summary>Gets the supported input bins.</summary>
    public ReadOnlyCollection<InputBin> InputBinCapability { get; internal set; } = EmptyInputBins;

    /// <summary>
    /// Gets the collection of supported page orientations.
    /// </summary>
    public ReadOnlyCollection<PageOrientation> PageOrientationCapability { get; internal set; } = EmptyOrientations;

    /// <summary>
    /// Gets the collection of supported output qualities.
    /// </summary>
    public ReadOnlyCollection<OutputQuality> OutputQualityCapability { get; internal set; } = EmptyOutputQualities;

    /// <summary>
    /// Gets the collection of supported output colors.
    /// </summary>
    public ReadOnlyCollection<OutputColor> OutputColorCapability { get; internal set; } = EmptyOutputColors;

    /// <summary>
    /// Gets the collection of supported page media sizes.
    /// </summary>
    public ReadOnlyCollection<PageMediaSize> PageMediaSizeCapability { get; internal set; } = EmptyMediaSizes;

    /// <summary>Gets the supported page orders.</summary>
    public ReadOnlyCollection<PageOrder> PageOrderCapability { get; internal set; } = EmptyPageOrders;

    /// <summary>
    /// Gets the collection of supported page resolutions.
    /// </summary>
    public ReadOnlyCollection<PageResolution> PageResolutionCapability { get; internal set; } = EmptyResolutions;

    /// <summary>Gets the supported page counts per sheet.</summary>
    public ReadOnlyCollection<int> PagesPerSheetCapability { get; internal set; } = EmptyPagesPerSheet;

    /// <summary>Gets the supported stapling settings.</summary>
    public ReadOnlyCollection<Stapling> StaplingCapability { get; internal set; } = EmptyStapling;

    /// <summary>
    /// Gets the maximum supported copies.
    /// </summary>
    public int? MaxCopyCount { get; internal set; }
}

/// <summary>Represents a collection of print queues.</summary>
public class PrintQueueCollection : PrintSystemObjects, IEnumerable<PrintQueue>
{
    private readonly List<PrintQueue> _queues = [];

    public PrintQueueCollection()
    {
    }

    public PrintQueueCollection(PrintServer printServer, string[] propertyFilter)
        : this(printServer, propertyFilter, Array.Empty<EnumeratedPrintQueueTypes>())
    {
    }

    public PrintQueueCollection(
        PrintServer printServer,
        string[] propertyFilter,
        EnumeratedPrintQueueTypes[] enumerationFlag)
    {
        ArgumentNullException.ThrowIfNull(printServer);
        ArgumentNullException.ThrowIfNull(propertyFilter);
        ArgumentNullException.ThrowIfNull(enumerationFlag);
        _queues.AddRange(PrintQueue.GetPrintQueues());
    }

    public object SyncRoot => ((ICollection)_queues).SyncRoot;

    public void Add(PrintQueue printObject)
    {
        ArgumentNullException.ThrowIfNull(printObject);
        _queues.Add(printObject);
    }

    public IEnumerator<PrintQueue> GetEnumerator() => _queues.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator GetNonGenericEnumerator() => ((IEnumerable)_queues).GetEnumerator();
}

/// <summary>Represents a collection of jobs in a print queue.</summary>
public class PrintJobInfoCollection : PrintSystemObjects, IEnumerable<PrintSystemJobInfo>
{
    private readonly List<PrintSystemJobInfo> _jobs = [];

    public PrintJobInfoCollection(PrintQueue printQueue, string[] propertyFilter)
    {
        ArgumentNullException.ThrowIfNull(printQueue);
        ArgumentNullException.ThrowIfNull(propertyFilter);
    }

    public void Add(PrintSystemJobInfo printObject)
    {
        ArgumentNullException.ThrowIfNull(printObject);
        _jobs.Add(printObject);
    }

    public IEnumerator<PrintSystemJobInfo> GetEnumerator() => _jobs.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator GetNonGenericEnumerator() => ((IEnumerable)_jobs).GetEnumerator();
}

/// <summary>
/// Represents a print server.
/// </summary>
public class PrintServer : PrintSystemObject
{
    /// <summary>
    /// Gets the name of the print server.
    /// </summary>
    public override string Name { get; set; }

    /// <summary>
    /// Initializes a new instance of the PrintServer class for the local server.
    /// </summary>
    public PrintServer()
    {
        Name = Environment.MachineName;
    }

    public PrintServer(PrintSystemDesiredAccess desiredAccess)
        : this()
    {
    }

    /// <summary>
    /// Initializes a new instance of the PrintServer class.
    /// </summary>
    /// <param name="serverName">The name of the print server.</param>
    public PrintServer(string serverName)
    {
        Name = serverName;
    }

    public PrintServer(string path, PrintSystemDesiredAccess desiredAccess)
        : this(path)
    {
    }

    public PrintServer(string path, string[] propertiesFilter)
        : this(path)
    {
        ArgumentNullException.ThrowIfNull(propertiesFilter);
    }

    public PrintServer(string path, PrintServerIndexedProperty[] propertiesFilter)
        : this(path)
    {
        ArgumentNullException.ThrowIfNull(propertiesFilter);
    }

    public PrintServer(string path, string[] propertiesFilter, PrintSystemDesiredAccess desiredAccess)
        : this(path, propertiesFilter)
    {
    }

    public PrintServer(string path, PrintServerIndexedProperty[] propertiesFilter, PrintSystemDesiredAccess desiredAccess)
        : this(path, propertiesFilter)
    {
    }

    /// <summary>
    /// Gets all print queues from this server.
    /// </summary>
    public PrintQueueCollection GetPrintQueues()
    {
        return new PrintQueueCollection(this, Array.Empty<string>());
    }

    public PrintQueueCollection GetPrintQueues(string[] propertiesFilter) =>
        new(this, propertiesFilter);

    public PrintQueueCollection GetPrintQueues(PrintQueueIndexedProperty[] propertiesFilter) =>
        new(this, propertiesFilter.Select(static value => value.ToString()).ToArray());

    public PrintQueueCollection GetPrintQueues(EnumeratedPrintQueueTypes[] enumerationFlag) =>
        new(this, Array.Empty<string>(), enumerationFlag);

    public PrintQueueCollection GetPrintQueues(string[] propertiesFilter, EnumeratedPrintQueueTypes[] enumerationFlag) =>
        new(this, propertiesFilter, enumerationFlag);

    public PrintQueueCollection GetPrintQueues(PrintQueueIndexedProperty[] propertiesFilter, EnumeratedPrintQueueTypes[] enumerationFlag) =>
        new(this, propertiesFilter.Select(static value => value.ToString()).ToArray(), enumerationFlag);

    public PrintQueue GetPrintQueue(string printQueueName) => new(this, printQueueName);

    public PrintQueue GetPrintQueue(string printQueueName, string[] propertiesFilter) =>
        new(this, printQueueName, propertiesFilter);

    /// <summary>
    /// Gets the default print queue from this server.
    /// </summary>
    internal PrintQueue? GetDefaultPrintQueue()
    {
        return PrintQueue.GetDefaultPrintQueue();
    }

}

/// <summary>
/// Provides helper methods for XPS document printing.
/// </summary>
internal sealed class PlatformXpsDocumentWriter
{
    private readonly PrintQueue _printQueue;

    /// <summary>
    /// Initializes a new instance of the XpsDocumentWriter class.
    /// </summary>
    /// <param name="printQueue">The print queue to write to.</param>
    internal PlatformXpsDocumentWriter(PrintQueue printQueue)
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
    public void Write(Visual visual, System.Printing.PrintTicket? printTicket)
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
    public void Write(DocumentPaginator documentPaginator, System.Printing.PrintTicket? printTicket)
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
    public void Write(IDocumentPaginatorSource documentPaginatorSource, System.Printing.PrintTicket? printTicket)
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

    private void WriteInternal(Visual visual, System.Printing.PrintTicket? printTicket)
    {
        // Route the visual through the platform print path, reusing the print
        // queue this writer was created for. Public serialization events are
        // raised by the canonical Jalium.UI.Xps.XpsDocumentWriter facade.
        var dialog = new PrintDialog
        {
            PrintQueue = _printQueue,
            PrintTicket = printTicket ?? _printQueue.DefaultPrintTicket
        };

        dialog.PrintVisual(visual, _printQueue.Name);
    }

    private void WriteInternal(DocumentPaginator paginator, System.Printing.PrintTicket? printTicket)
    {
        var dialog = new PrintDialog
        {
            PrintQueue = _printQueue,
            PrintTicket = printTicket ?? _printQueue.DefaultPrintTicket
        };

        dialog.PrintDocument(paginator, _printQueue.Name);
    }
}

/// <summary>
/// Specifies input and output bins for printing.
/// </summary>
public enum InputBin
{
    Unknown = 0,
    AutoSelect = 1,
    Cassette = 2,
    Tractor = 3,
    AutoSheetFeeder = 4,
    Manual = 5,
}

/// <summary>
/// Specifies stapling options for printing.
/// </summary>
public enum Stapling
{
    Unknown = 0,
    SaddleStitch = 1,
    BottomLeft = 2,
    BottomRight = 3,
    DualLeft = 4,
    DualRight = 5,
    DualTop = 6,
    DualBottom = 7,
    TopLeft = 8,
    TopRight = 9,
    None = 10,
}

/// <summary>
/// Specifies page ordering for multi-page printing.
/// </summary>
public enum PageOrder
{
    Unknown = 0,
    Standard = 1,
    Reverse = 2,
}

}
