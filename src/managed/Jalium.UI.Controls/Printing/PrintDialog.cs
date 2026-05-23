using System.Runtime.InteropServices;
using Jalium.UI.Media;

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
        return ShowDialogInternal(Jalium.UI.Application.Current?.MainWindow);
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
    /// <param name="owner">The owner window for the dialog, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the user clicked Print; otherwise <see langword="false"/>.</returns>
    private bool ShowDialogInternal(Window? owner = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // The common print dialog is a Windows-only feature.
            return false;
        }

        var ownerHandle = DialogOwnerResolver.Resolve(owner?.Handle ?? nint.Zero);
        return ShowWindowsPrintDialog(ownerHandle);
    }

    /// <summary>
    /// Prints a visual internally by rasterizing it and emitting it to the
    /// printer device context as a single page.
    /// </summary>
    /// <param name="visual">The visual to print.</param>
    /// <param name="description">A description used as the print job name.</param>
    private void PrintVisualInternal(Visual visual, string description)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PrintDialogException("Printing is only supported on Windows.");
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PrintDialogException("Printing is only supported on Windows.");
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
            nMinPage = (ushort)Math.Clamp(MinPage, 1, ushort.MaxValue),
            nMaxPage = (ushort)Math.Clamp(MaxPage, 1, ushort.MaxValue),
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

        // The print dialog always disables "Selection" because the framework
        // print path operates on visuals/documents rather than a selection.
        printDialog.Flags |= PrintingNativeMethods.PD_NOSELECTION;

        if (PageRangeSelection == PageRangeSelection.UserPages)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_PAGENUMS;
        }
        else if (PageRangeSelection == PageRangeSelection.CurrentPage && CurrentPageEnabled)
        {
            printDialog.Flags |= PrintingNativeMethods.PD_CURRENTPAGE;
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
        PrintTicket ??= new PrintTicket();
        PrintTicket.CopyCount = copies;

        if ((printDialog.Flags & PrintingNativeMethods.PD_PAGENUMS) != 0)
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
            PrintTicket ??= new PrintTicket();

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

            var header = new PrintingNativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<PrintingNativeMethods.BITMAPINFOHEADER>(),
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
    /// Gets the currently installed print queues by enumerating the local and
    /// connected printers of the machine.
    /// </summary>
    /// <returns>A collection of print queues installed on the local machine.</returns>
    public static IEnumerable<PrintQueue> GetPrintQueues()
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
    public static PrintQueue? GetDefaultPrintQueue()
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
    /// Gets all print jobs currently queued for this printer.
    /// </summary>
    /// <returns>A collection of print jobs.</returns>
    public IEnumerable<PrintSystemJobInfo> GetPrintJobInfoCollection()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Empty<PrintSystemJobInfo>();
        }

        return EnumerateWindowsJobs();
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
    private void Dispose(bool disposing)
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

    /// <summary>Raises the <see cref="WritingPrintTicketRequired"/> event from a print pipeline implementation.</summary>
    internal void RaiseWritingPrintTicketRequired(WritingPrintTicketRequiredEventArgs e) => WritingPrintTicketRequired?.Invoke(this, e);

    private void WriteInternal(Visual visual, PrintTicket? printTicket)
    {
        try
        {
            // Route the visual through the platform print path, reusing the
            // print queue this writer was created for.
            var dialog = new PrintDialog
            {
                PrintQueue = _printQueue,
                PrintTicket = ResolvePrintTicket(printTicket)
            };

            dialog.PrintVisual(visual, _printQueue.Name);
            OnWritingProgressChanged(1, 1);
            OnWritingCompleted(null, false);
        }
        catch (Exception ex)
        {
            OnWritingCompleted(ex, false);
            throw;
        }
    }

    private void WriteInternal(DocumentPaginator paginator, PrintTicket? printTicket)
    {
        try
        {
            var dialog = new PrintDialog
            {
                PrintQueue = _printQueue,
                PrintTicket = ResolvePrintTicket(printTicket)
            };

            dialog.PrintDocument(paginator, _printQueue.Name);

            var pageCount = paginator.IsPageCountValid ? paginator.PageCount : 0;
            for (var i = 0; i < pageCount; i++)
            {
                OnWritingProgressChanged(i + 1, pageCount);
            }

            OnWritingCompleted(null, false);
        }
        catch (Exception ex)
        {
            OnWritingCompleted(ex, false);
            throw;
        }
    }

    /// <summary>
    /// Resolves the effective print ticket, raising the
    /// <see cref="WritingPrintTicketRequired"/> event so a host can supply one
    /// when none is provided explicitly.
    /// </summary>
    private PrintTicket? ResolvePrintTicket(PrintTicket? explicitTicket)
    {
        if (explicitTicket != null)
        {
            return explicitTicket;
        }

        var args = new WritingPrintTicketRequiredEventArgs(PrintTicketLevel.Job, 0);
        RaiseWritingPrintTicketRequired(args);
        return args.PrintTicket ?? _printQueue.DefaultPrintTicket;
    }

    /// <summary>
    /// Raises the WritingCompleted event.
    /// </summary>
    private void OnWritingCompleted(Exception? error, bool cancelled)
    {
        WritingCompleted?.Invoke(this, new WritingCompletedEventArgs(error, cancelled));
    }

    /// <summary>
    /// Raises the WritingProgressChanged event.
    /// </summary>
    private void OnWritingProgressChanged(int currentPage, int totalPages)
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
