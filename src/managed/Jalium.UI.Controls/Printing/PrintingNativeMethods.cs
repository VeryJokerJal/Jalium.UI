using System.Runtime.InteropServices;

namespace Jalium.UI.Controls.Printing;

/// <summary>
/// Centralized Win32 P/Invoke declarations and interop structures used by the
/// printing platform layer (<see cref="PrintDialog"/>, <see cref="PrintQueue"/>
/// and <see cref="XpsDocumentWriter"/>).
/// </summary>
/// <remarks>
/// The printing platform layer is only meaningful on Windows. Every caller is
/// expected to gate these declarations behind a
/// <see cref="RuntimeInformation.IsOSPlatform(OSPlatform)"/> check so the
/// assembly continues to load on non-Windows runtimes.
/// </remarks>
internal static class PrintingNativeMethods
{
    #region comdlg32 - PrintDlg

    /// <summary>Return the result of printing instead of displaying the dialog (PD_RETURNDEFAULT-less print path).</summary>
    internal const uint PD_ALLPAGES = 0x00000000;

    /// <summary>The "Selection" radio button is selected when the dialog is created.</summary>
    internal const uint PD_SELECTION = 0x00000001;

    /// <summary>The "Pages" radio button is selected when the dialog is created.</summary>
    internal const uint PD_PAGENUMS = 0x00000002;

    /// <summary>Disables the "Selection" radio button.</summary>
    internal const uint PD_NOSELECTION = 0x00000004;

    /// <summary>Disables the "Pages" radio button and the associated edit controls.</summary>
    internal const uint PD_NOPAGENUMS = 0x00000008;

    /// <summary>Causes PrintDlg to return a device context instead of a handle.</summary>
    internal const uint PD_RETURNDC = 0x00000100;

    /// <summary>Returns an information context rather than a full device context.</summary>
    internal const uint PD_RETURNIC = 0x00000200;

    /// <summary>Returns the printer settings without displaying the dialog box.</summary>
    internal const uint PD_RETURNDEFAULT = 0x00000400;

    /// <summary>Hides and disables the "Print to File" check box.</summary>
    internal const uint PD_HIDEPRINTTOFILE = 0x00100000;

    /// <summary>Disables the "Print to File" check box.</summary>
    internal const uint PD_NONETWORKBUTTON = 0x00200000;

    /// <summary>The "Current Page" radio button is selected.</summary>
    internal const uint PD_CURRENTPAGE = 0x00400000;

    /// <summary>Disables the "Current Page" radio button.</summary>
    internal const uint PD_NOCURRENTPAGE = 0x00800000;

    /// <summary>The "Collate" check box is selected when the dialog is created.</summary>
    internal const uint PD_COLLATE = 0x00000010;

    /// <summary>The "Print to File" check box is selected when the dialog is created.</summary>
    internal const uint PD_PRINTTOFILE = 0x00000020;

    /// <summary>
    /// The Win32 <c>PRINTDLGW</c> structure consumed by <c>PrintDlgW</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTDLG
    {
        /// <summary>Size, in bytes, of this structure.</summary>
        public int lStructSize;

        /// <summary>Handle of the window that owns the dialog box.</summary>
        public nint hwndOwner;

        /// <summary>Handle to a movable global memory object that contains a DEVMODE structure.</summary>
        public nint hDevMode;

        /// <summary>Handle to a movable global memory object that contains a DEVNAMES structure.</summary>
        public nint hDevNames;

        /// <summary>Handle to a device context or information context, depending on the flags.</summary>
        public nint hDC;

        /// <summary>A set of bit flags that initialize the dialog box.</summary>
        public uint Flags;

        /// <summary>The initial value for the starting page of the print range.</summary>
        public ushort nFromPage;

        /// <summary>The initial value for the ending page of the print range.</summary>
        public ushort nToPage;

        /// <summary>The minimum value for the page range specified in nFromPage and nToPage.</summary>
        public ushort nMinPage;

        /// <summary>The maximum value for the page range specified in nFromPage and nToPage.</summary>
        public ushort nMaxPage;

        /// <summary>The number of copies requested.</summary>
        public ushort nCopies;

        /// <summary>Handle to the application instance (used with template members).</summary>
        public nint hInstance;

        /// <summary>Application-defined data passed to the hook procedure.</summary>
        public nint lCustData;

        /// <summary>Pointer to a print-dialog hook procedure.</summary>
        public nint lpfnPrintHook;

        /// <summary>Pointer to a setup-dialog hook procedure.</summary>
        public nint lpfnSetupHook;

        /// <summary>The name of the dialog box template resource.</summary>
        public nint lpPrintTemplateName;

        /// <summary>The name of the setup dialog box template resource.</summary>
        public nint lpSetupTemplateName;

        /// <summary>Handle to a memory object containing a dialog box template.</summary>
        public nint hPrintTemplate;

        /// <summary>Handle to a memory object containing a setup dialog box template.</summary>
        public nint hSetupTemplate;
    }

    /// <summary>
    /// Displays the Win32 common Print dialog box and, optionally, creates a
    /// device context for the selected printer.
    /// </summary>
    /// <param name="lppd">The print dialog configuration structure.</param>
    /// <returns><see langword="true"/> if the user clicked OK; otherwise <see langword="false"/>.</returns>
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "PrintDlgW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintDlg(ref PRINTDLG lppd);

    /// <summary>Returns the most recent common dialog box error code.</summary>
    [DllImport("comdlg32.dll")]
    internal static extern uint CommDlgExtendedError();

    #endregion

    #region DEVMODE / DEVNAMES

    /// <summary>The dmOrientation member is initialized.</summary>
    internal const int DM_ORIENTATION = 0x00000001;

    /// <summary>The dmCopies member is initialized.</summary>
    internal const int DM_COPIES = 0x00000100;

    /// <summary>The dmPaperSize member is initialized.</summary>
    internal const int DM_PAPERSIZE = 0x00000002;

    /// <summary>Portrait page orientation value for dmOrientation.</summary>
    internal const short DMORIENT_PORTRAIT = 1;

    /// <summary>Landscape page orientation value for dmOrientation.</summary>
    internal const short DMORIENT_LANDSCAPE = 2;

    /// <summary>Size, in TCHARs, of the device name field within a DEVMODE structure.</summary>
    internal const int CCHDEVICENAME = 32;

    /// <summary>Size, in TCHARs, of the form name field within a DEVMODE structure.</summary>
    internal const int CCHFORMNAME = 32;

    /// <summary>
    /// The Win32 <c>DEVMODEW</c> structure. Only the leading fixed-size members
    /// are described here; the trailing display members are represented as a
    /// reserved blob because the printing layer never reads them directly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        /// <summary>"Friendly" name of the printer.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;

        /// <summary>The version number of the initialization data specification.</summary>
        public ushort dmSpecVersion;

        /// <summary>The printer driver version number.</summary>
        public ushort dmDriverVersion;

        /// <summary>The size, in bytes, of the public DEVMODE structure.</summary>
        public ushort dmSize;

        /// <summary>The number of bytes of private driver data that follow this structure.</summary>
        public ushort dmDriverExtra;

        /// <summary>A bit mask specifying which fields of the structure have been initialized.</summary>
        public uint dmFields;

        /// <summary>The orientation of the paper (portrait/landscape).</summary>
        public short dmOrientation;

        /// <summary>The size of the paper to print on.</summary>
        public short dmPaperSize;

        /// <summary>Overrides the length of the paper, in tenths of a millimeter.</summary>
        public short dmPaperLength;

        /// <summary>Overrides the width of the paper, in tenths of a millimeter.</summary>
        public short dmPaperWidth;

        /// <summary>The factor by which the printed output is to be scaled.</summary>
        public short dmScale;

        /// <summary>The number of copies printed if the device supports multiple-page copies.</summary>
        public short dmCopies;

        /// <summary>Specifies the paper source.</summary>
        public short dmDefaultSource;

        /// <summary>Specifies the printer resolution.</summary>
        public short dmPrintQuality;

        /// <summary>Switches between color and monochrome on color printers.</summary>
        public short dmColor;

        /// <summary>Selects duplex or double-sided printing.</summary>
        public short dmDuplex;

        /// <summary>Specifies the y-resolution, in DPI, of the printer.</summary>
        public short dmYResolution;

        /// <summary>The printer's TrueType option.</summary>
        public short dmTTOption;

        /// <summary>Specifies whether collation should be used when printing multiple copies.</summary>
        public short dmCollate;

        /// <summary>Reserved for system use; must be zero.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;

        /// <summary>The number of pixels per logical inch (display only).</summary>
        public ushort dmLogPixels;

        /// <summary>The color resolution, in bits per pixel, of the display device.</summary>
        public uint dmBitsPerPel;

        /// <summary>The width, in pixels, of the visible device surface.</summary>
        public uint dmPelsWidth;

        /// <summary>The height, in pixels, of the visible device surface.</summary>
        public uint dmPelsHeight;

        /// <summary>Display flags / device-specific display mode union.</summary>
        public uint dmDisplayFlags;

        /// <summary>The frequency, in hertz, of the display device.</summary>
        public uint dmDisplayFrequency;

        /// <summary>How ICM is handled.</summary>
        public uint dmICMMethod;

        /// <summary>The ICM intent.</summary>
        public uint dmICMIntent;

        /// <summary>The type of media being printed on.</summary>
        public uint dmMediaType;

        /// <summary>How dithering is to be done.</summary>
        public uint dmDitherType;

        /// <summary>Reserved; must be zero.</summary>
        public uint dmReserved1;

        /// <summary>Reserved; must be zero.</summary>
        public uint dmReserved2;

        /// <summary>The number of pixels per logical inch on the panning region.</summary>
        public uint dmPanningWidth;

        /// <summary>The height, in pixels, of the panning region.</summary>
        public uint dmPanningHeight;
    }

    #endregion

    #region kernel32 - global memory

    /// <summary>Locks a global memory object and returns a pointer to its first byte.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GlobalLock(nint hMem);

    /// <summary>Decrements the lock count associated with a global memory object.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(nint hMem);

    /// <summary>Frees a global memory object and invalidates its handle.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GlobalFree(nint hMem);

    #endregion

    #region gdi32 - device context and printing

    /// <summary>Index for GetDeviceCaps that returns the printable width in pixels.</summary>
    internal const int HORZRES = 8;

    /// <summary>Index for GetDeviceCaps that returns the printable height in pixels.</summary>
    internal const int VERTRES = 10;

    /// <summary>Index for GetDeviceCaps that returns the horizontal resolution in DPI.</summary>
    internal const int LOGPIXELSX = 88;

    /// <summary>Index for GetDeviceCaps that returns the vertical resolution in DPI.</summary>
    internal const int LOGPIXELSY = 90;

    /// <summary>Index for GetDeviceCaps that returns the unprintable left margin in pixels.</summary>
    internal const int PHYSICALOFFSETX = 112;

    /// <summary>Index for GetDeviceCaps that returns the unprintable top margin in pixels.</summary>
    internal const int PHYSICALOFFSETY = 113;

    /// <summary>Index for GetDeviceCaps that returns the full physical page width in pixels.</summary>
    internal const int PHYSICALWIDTH = 110;

    /// <summary>Index for GetDeviceCaps that returns the full physical page height in pixels.</summary>
    internal const int PHYSICALHEIGHT = 111;

    /// <summary>Raster operation: copy the source rectangle directly to the destination.</summary>
    internal const int SRCCOPY = 0x00CC0020;

    /// <summary>DIB color table contains literal RGB values.</summary>
    internal const int DIB_RGB_COLORS = 0;

    /// <summary>Creates a device context for the specified device.</summary>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDCW", SetLastError = true)]
    internal static extern nint CreateDC(string? lpszDriver, string lpszDevice, string? lpszOutput, nint lpInitData);

    /// <summary>Deletes the specified device context.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint hdc);

    /// <summary>Retrieves device-specific information for the specified device.</summary>
    [DllImport("gdi32.dll")]
    internal static extern int GetDeviceCaps(nint hdc, int index);

    /// <summary>The Win32 <c>DOCINFOW</c> structure passed to <c>StartDocW</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DOCINFO
    {
        /// <summary>Size, in bytes, of the structure.</summary>
        public int cbSize;

        /// <summary>The name of the document.</summary>
        public string? lpszDocName;

        /// <summary>The name of the output file (used with "FILE:" output).</summary>
        public string? lpszOutput;

        /// <summary>The type of data used to record the print job.</summary>
        public string? lpszDatatype;

        /// <summary>Additional information about the print job (e.g. append-only).</summary>
        public int fwType;
    }

    /// <summary>Starts a print job.</summary>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartDocW", SetLastError = true)]
    internal static extern int StartDoc(nint hdc, ref DOCINFO lpdi);

    /// <summary>Ends a print job started by StartDoc.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int EndDoc(nint hdc);

    /// <summary>Prepares the printer driver to accept data for a new page.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int StartPage(nint hdc);

    /// <summary>Notifies the device that the application has finished writing to a page.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int EndPage(nint hdc);

    /// <summary>Aborts the current print job and erases everything drawn since the last EndPage.</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int AbortDoc(nint hdc);

    /// <summary>Uncompressed RGB device-independent bitmap (BI_RGB).</summary>
    internal const uint BI_RGB = 0;

    /// <summary>
    /// Copies the color data of a device-independent bitmap (DIB) to a
    /// destination rectangle, scaling the image if necessary.
    /// </summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int StretchDIBits(
        nint hdc,
        int xDest, int yDest, int destWidth, int destHeight,
        int xSrc, int ySrc, int srcWidth, int srcHeight,
        byte[] lpBits,
        ref BITMAPINFOHEADER lpbmi,
        uint iUsage,
        int rop);

    #endregion

    #region winspool - printer enumeration and management

    /// <summary>EnumPrinters level 4 - fast enumeration of local and connected printers.</summary>
    internal const uint PRINTER_ENUM_LOCAL = 0x00000002;

    /// <summary>EnumPrinters flag - include printer connections in the enumeration.</summary>
    internal const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

    /// <summary>PRINTER_INFO_4 attribute flag indicating a local printer.</summary>
    internal const uint PRINTER_ATTRIBUTE_LOCAL = 0x00000040;

    /// <summary>Access right that grants the ability to administer a printer.</summary>
    internal const uint PRINTER_ALL_ACCESS = 0x000F000C;

    /// <summary>SetPrinter command that pauses the printer.</summary>
    internal const uint PRINTER_CONTROL_PAUSE = 1;

    /// <summary>SetPrinter command that resumes a paused printer.</summary>
    internal const uint PRINTER_CONTROL_RESUME = 2;

    /// <summary>SetPrinter command that deletes all print jobs queued for the printer.</summary>
    internal const uint PRINTER_CONTROL_PURGE = 3;

    /// <summary>SetJob command that pauses an individual print job.</summary>
    internal const uint JOB_CONTROL_PAUSE = 1;

    /// <summary>SetJob command that resumes a paused print job.</summary>
    internal const uint JOB_CONTROL_RESUME = 2;

    /// <summary>SetJob command that cancels (deletes) a print job.</summary>
    internal const uint JOB_CONTROL_CANCEL = 3;

    /// <summary>SetJob command that restarts a print job from the beginning.</summary>
    internal const uint JOB_CONTROL_RESTART = 4;

    /// <summary>Job status flag: the job is paused.</summary>
    internal const uint JOB_STATUS_PAUSED = 0x00000001;

    /// <summary>Job status flag: an error occurred.</summary>
    internal const uint JOB_STATUS_ERROR = 0x00000002;

    /// <summary>Job status flag: the job is being deleted.</summary>
    internal const uint JOB_STATUS_DELETING = 0x00000004;

    /// <summary>Job status flag: the job is spooling.</summary>
    internal const uint JOB_STATUS_SPOOLING = 0x00000008;

    /// <summary>Job status flag: the job is printing.</summary>
    internal const uint JOB_STATUS_PRINTING = 0x00000010;

    /// <summary>Job status flag: the job has been printed.</summary>
    internal const uint JOB_STATUS_PRINTED = 0x00000080;

    /// <summary>Job status flag: the job has been deleted.</summary>
    internal const uint JOB_STATUS_DELETED = 0x00000100;

    /// <summary>Job status flag: the job is complete (Windows Vista and later).</summary>
    internal const uint JOB_STATUS_COMPLETE = 0x00001000;

    /// <summary>Job status flag: the job has been restarted.</summary>
    internal const uint JOB_STATUS_RESTART = 0x00000800;

    /// <summary>Job status flag: user intervention is required.</summary>
    internal const uint JOB_STATUS_USER_INTERVENTION = 0x00000400;

    /// <summary>Job status flag: the printer is offline.</summary>
    internal const uint JOB_STATUS_OFFLINE = 0x00000020;

    /// <summary>Job status flag: the printer is out of paper.</summary>
    internal const uint JOB_STATUS_PAPEROUT = 0x00000040;

    /// <summary>Job status flag: the job has been retained in the queue after printing.</summary>
    internal const uint JOB_STATUS_RETAINED = 0x00002000;

    /// <summary>
    /// The Win32 <c>PRINTER_INFO_4</c> structure returned by <c>EnumPrinters</c>
    /// at level 4. This level is the recommended fast path for browsing
    /// installed printers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_INFO_4
    {
        /// <summary>The name of the printer (local name or "\\server\printer").</summary>
        public string pPrinterName;

        /// <summary>The name of the server (null for local printers).</summary>
        public string? pServerName;

        /// <summary>The printer attributes (PRINTER_ATTRIBUTE_* flags).</summary>
        public uint Attributes;
    }

    /// <summary>
    /// The Win32 <c>PRINTER_INFO_2</c> structure used by <c>GetPrinter</c> at
    /// level 2. Only the members the printing layer reads or writes are kept
    /// strongly typed; the remainder are still declared so the struct size
    /// matches the unmanaged layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_INFO_2
    {
        /// <summary>The name of the server hosting the printer.</summary>
        public string? pServerName;

        /// <summary>The name of the printer.</summary>
        public string pPrinterName;

        /// <summary>The name of the share point for the printer.</summary>
        public string? pShareName;

        /// <summary>The name of the port the printer is connected to.</summary>
        public string? pPortName;

        /// <summary>The name of the printer driver.</summary>
        public string? pDriverName;

        /// <summary>A brief description of the printer.</summary>
        public string? pComment;

        /// <summary>The physical location of the printer.</summary>
        public string? pLocation;

        /// <summary>Pointer to a DEVMODE structure with default printer data.</summary>
        public nint pDevMode;

        /// <summary>The name of the file used to separate print jobs.</summary>
        public string? pSepFile;

        /// <summary>The name of the print processor used by the printer.</summary>
        public string? pPrintProcessor;

        /// <summary>The default data type of the print job.</summary>
        public string? pDatatype;

        /// <summary>Default print-processor parameters.</summary>
        public string? pParameters;

        /// <summary>Pointer to a SECURITY_DESCRIPTOR for the printer.</summary>
        public nint pSecurityDescriptor;

        /// <summary>The printer attributes (PRINTER_ATTRIBUTE_* flags).</summary>
        public uint Attributes;

        /// <summary>A priority value the spooler uses to route print jobs.</summary>
        public uint Priority;

        /// <summary>The default priority value assigned to each print job.</summary>
        public uint DefaultPriority;

        /// <summary>The earliest time the printer will begin printing a job.</summary>
        public uint StartTime;

        /// <summary>The latest time the printer will print a job.</summary>
        public uint UntilTime;

        /// <summary>The printer status (PRINTER_STATUS_* flags).</summary>
        public uint Status;

        /// <summary>The number of print jobs queued for the printer.</summary>
        public uint cJobs;

        /// <summary>The average number of pages per minute the printer produces.</summary>
        public uint AveragePPM;
    }

    /// <summary>
    /// The Win32 <c>JOB_INFO_2</c> structure returned by <c>EnumJobs</c> at
    /// level 2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JOB_INFO_2
    {
        /// <summary>The job identifier.</summary>
        public uint JobId;

        /// <summary>The name of the printer for which the job is spooled.</summary>
        public string? pPrinterName;

        /// <summary>The name of the machine that created the print job.</summary>
        public string? pMachineName;

        /// <summary>The name of the user that owns the print job.</summary>
        public string? pUserName;

        /// <summary>The name of the document.</summary>
        public string? pDocument;

        /// <summary>The name of the print-processor notification window.</summary>
        public string? pNotifyName;

        /// <summary>The type of data used to record the print job.</summary>
        public string? pDatatype;

        /// <summary>The name of the print processor.</summary>
        public string? pPrintProcessor;

        /// <summary>Print-processor parameters.</summary>
        public string? pParameters;

        /// <summary>The name of the printer driver.</summary>
        public string? pDriverName;

        /// <summary>Pointer to a DEVMODE structure for the job.</summary>
        public nint pDevMode;

        /// <summary>A description of the job status.</summary>
        public string? pStatus;

        /// <summary>Pointer to a SECURITY_DESCRIPTOR for the job.</summary>
        public nint pSecurityDescriptor;

        /// <summary>The job status (JOB_STATUS_* flags).</summary>
        public uint Status;

        /// <summary>The job priority.</summary>
        public uint Priority;

        /// <summary>The position of the job in the print queue.</summary>
        public uint Position;

        /// <summary>The time, in milliseconds, before the job starts printing.</summary>
        public uint StartTime;

        /// <summary>The time, in milliseconds, after which the job is not available.</summary>
        public uint UntilTime;

        /// <summary>The total number of pages in the document.</summary>
        public uint TotalPages;

        /// <summary>The size, in bytes, of the print job.</summary>
        public uint Size;

        /// <summary>The time the job was submitted.</summary>
        public SYSTEMTIME Submitted;

        /// <summary>The total time, in milliseconds, the job has been printing.</summary>
        public uint Time;

        /// <summary>The number of pages that have printed.</summary>
        public uint PagesPrinted;
    }

    /// <summary>The Win32 <c>SYSTEMTIME</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME
    {
        /// <summary>The year.</summary>
        public ushort wYear;

        /// <summary>The month (1 = January).</summary>
        public ushort wMonth;

        /// <summary>The day of the week (0 = Sunday).</summary>
        public ushort wDayOfWeek;

        /// <summary>The day of the month.</summary>
        public ushort wDay;

        /// <summary>The hour.</summary>
        public ushort wHour;

        /// <summary>The minute.</summary>
        public ushort wMinute;

        /// <summary>The second.</summary>
        public ushort wSecond;

        /// <summary>The milliseconds.</summary>
        public ushort wMilliseconds;
    }

    /// <summary>Enumerates available printers, print servers, domains, or print providers.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "EnumPrintersW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        nint pPrinterEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    /// <summary>Retrieves the name of the default printer for the current user.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetDefaultPrinter(
        [Out] char[]? buffer,
        ref uint bufferSize);

    /// <summary>Retrieves a handle to the specified printer or print server.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "OpenPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenPrinter(string pPrinterName, out nint phPrinter, nint pDefault);

    /// <summary>The Win32 <c>PRINTER_DEFAULTS</c> structure used by <c>OpenPrinter</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PRINTER_DEFAULTS
    {
        /// <summary>The default data type for a print job.</summary>
        public string? pDatatype;

        /// <summary>Pointer to a DEVMODE structure for the print job.</summary>
        public nint pDevMode;

        /// <summary>The access rights requested for the printer.</summary>
        public uint DesiredAccess;
    }

    /// <summary>Retrieves a handle to the specified printer with explicit access rights.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "OpenPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenPrinter(string pPrinterName, out nint phPrinter, ref PRINTER_DEFAULTS pDefault);

    /// <summary>Closes a handle to the specified printer object.</summary>
    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClosePrinter(nint hPrinter);

    /// <summary>Retrieves information about the specified printer.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "GetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPrinter(
        nint hPrinter,
        uint level,
        nint pPrinter,
        uint cbBuf,
        out uint pcbNeeded);

    /// <summary>Sets the data for the specified printer or pauses/resumes/purges it.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "SetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetPrinter(nint hPrinter, uint level, nint pPrinter, uint command);

    /// <summary>Sets or controls the status of the specified print job.</summary>
    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetJob(nint hPrinter, uint jobId, uint level, nint pJob, uint command);

    /// <summary>Retrieves information about the queued print jobs for a printer.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, EntryPoint = "EnumJobsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumJobs(
        nint hPrinter,
        uint firstJob,
        uint noJobs,
        uint level,
        nint pJob,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    #endregion
}
