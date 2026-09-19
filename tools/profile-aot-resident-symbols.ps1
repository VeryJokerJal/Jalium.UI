[CmdletBinding(DefaultParameterSetName = 'Live')]
param(
    [Parameter(Mandatory = $true, Position = 0, ParameterSetName = 'Live')]
    [ValidateRange(1, 2147483647)]
    [int]$TargetProcessId,

    [Parameter(Mandatory = $true, Position = 1)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true, Position = 0, ParameterSetName = 'Offline')]
    [ValidateNotNullOrEmpty()]
    [string]$ImagePath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Offline')]
    [switch]$ValidatePdbOnly,

    [ValidateRange(1, 500)]
    [int]$TopCount = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-Mebibytes {
    param([double]$Bytes)

    return [Math]::Round($Bytes / 1MB, 3)
}

function ConvertTo-Percent {
    param(
        [double]$Part,
        [double]$Whole
    )

    if ($Whole -le 0) {
        return 0.0
    }

    return [Math]::Round(($Part * 100.0) / $Whole, 3)
}

function ConvertTo-MarkdownCell {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) {
        return ''
    }

    return ([string]$Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-InstrumentationSuggestion {
    param([string]$Category)

    switch ($Category) {
        'Generated themes' {
            return 'Instrument Application/theme startup, the generated theme dictionary factory, ResourceDictionary population, and default-style materialization as separate timed stages.'
        }
        'System reflection/AOT metadata' {
            return 'Instrument AotTypeRegistry/type activation, RuntimeType or Activator calls, and custom-attribute lookup around the first Application and Window construction.'
        }
        'CSS/style' {
            return 'Instrument CSS property and mapping registration, stylesheet/parser setup, selector indexing, and the first style evaluation as separate startup stages.'
        }
        'Fonts/text' {
            return 'Instrument the first text measurement, font collection/family resolution, native text-format creation, and first glyph shaping or raster preparation.'
        }
        'Jalium framework' {
            return 'Instrument Application.Run, Window construction/show, resource lookup, layout, and first render scheduling, then narrow the largest reported framework chain.'
        }
        'Interop/native boundary' {
            return 'Instrument each managed-to-native initialization boundary reached before the first visible frame and record which native subsystem is initialized.'
        }
        'Application/probe' {
            return 'Instrument the probe entry point, Application creation, Window construction, Show, and first-frame completion with monotonic timestamps.'
        }
        'NativeAOT runtime' {
            return 'Instrument process entry, managed Main entry, and the first framework call so runtime startup can be separated from application and framework initialization.'
        }
        'System libraries' {
            return 'Instrument the first calls into the reported BCL chain and the immediately preceding Jalium or application caller.'
        }
        default {
            return 'Instrument the callers immediately before and after this chain during startup, using monotonic timestamps and invocation counts.'
        }
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'profile-aot-resident-symbols.ps1 requires Windows.'
}

if (-not [Environment]::Is64BitProcess) {
    throw 'Run profile-aot-resident-symbols.ps1 from 64-bit PowerShell.'
}

if (-not ([System.Management.Automation.PSTypeName]'JaliumAotResidentSymbols.Profiler').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JaliumAotResidentSymbols
{
    public sealed class PeSectionCapture
    {
        public int Ordinal;
        public string Name;
        public ulong RelativeVirtualAddress;
        public ulong EndRelativeVirtualAddress;
        public ulong MappedSizeBytes;
        public ulong PeVirtualSizeBytes;
        public ulong RawDataSizeBytes;
        public uint Characteristics;
        public bool ContainsCode;
        public bool IsExecutable;
        public bool IsReadable;
        public bool IsWritable;
    }

    public sealed class SymbolCapture
    {
        public int Ordinal;
        public string Name;
        public string Aliases;
        public string Category;
        public string StartupChain;
        public ulong RelativeVirtualAddress;
        public ulong EndRelativeVirtualAddress;
        public uint PdbSymbolSizeBytes;
        public ulong CoverageSizeBytes;
        public string CoverageSizeSource;
        public uint Tag;
        public string TagName;
        public uint Flags;
        public long ResidentPageTouches;
        public long SoleSymbolResidentPageTouches;
        public long SharedSymbolResidentPageTouches;
        public ulong ResidentOverlapBytes;
        public double ResidentCoveragePercent;
    }

    public sealed class ResidentPageCapture
    {
        public int Index;
        public ulong PageAddress;
        public ulong PageRelativeVirtualAddress;
        public ulong PageEndRelativeVirtualAddress;
        public ulong ResidentStartRelativeVirtualAddress;
        public ulong ResidentEndRelativeVirtualAddress;
        public ulong ResidentTextBytes;
        public bool SharedWorkingSetPage;
        public ulong SymbolCoveredBytes;
        public ulong UncoveredBytes;
        public int SymbolCount;
        public int CategoryCount;
        public int StartupChainCount;
        public bool MultipleSymbolsSharePage;
        public string SymbolCoverageKind;
        public string SymbolSizeQuality;
        public string PageAttribution;
        public string Categories;
        public string StartupChains;
        public string ExampleSymbols;
    }

    public sealed class PageSymbolCapture
    {
        public int PageIndex;
        public ulong PageAddress;
        public ulong PageRelativeVirtualAddress;
        public int PageSymbolCount;
        public bool MultipleSymbolsSharePage;
        public int SymbolOrdinal;
        public ulong SymbolRelativeVirtualAddress;
        public ulong SymbolEndRelativeVirtualAddress;
        public string SymbolName;
        public string Category;
        public string StartupChain;
        public uint PdbSymbolSizeBytes;
        public ulong SymbolCoverageSizeBytes;
        public string CoverageSizeSource;
        public ulong OverlapStartRelativeVirtualAddress;
        public ulong OverlapEndRelativeVirtualAddress;
        public ulong OverlapBytes;
        public bool ExecutionEvidence;
    }

    public sealed class PageAttributionCapture
    {
        public string Name;
        public long ResidentPages;
        public ulong ResidentTextBytes;
        public long SharedWorkingSetPages;
        public long PrivateWorkingSetPages;
    }

    public sealed class RollupCapture
    {
        public string Name;
        public string Category;
        public int SymbolCount;
        public int ResidentSymbolCount;
        public long ResidentPageTouches;
        public long ExclusiveResidentPages;
        public long MixedResidentPagesTouched;
        public ulong ResidentOverlapBytesUnion;
        public string TopSymbols;
    }

    public sealed class ImageInspectionResult
    {
        public string ImagePath;
        public string ExpectedPdbPath;
        public string LoadedPdbPath;
        public string DbgHelpSymbolType;
        public ulong PreferredImageBase;
        public ulong LoadedSymbolBase;
        public ulong SizeOfImageBytes;
        public ulong SizeOfHeadersBytes;
        public uint SectionAlignmentBytes;
        public PeSectionCapture TextSection;
        public int RawSymbolCount;
        public int RawTextSymbolCount;
        public int CanonicalTextSymbolCount;
        public int ExactSizedTextSymbolCount;
        public int InferredSizedTextSymbolCount;
        public SymbolCapture[] Symbols;
        public RollupCapture[] Categories;
    }

    public sealed class CaptureResult
    {
        public string CaptureStartedUtc;
        public string CaptureCompletedUtc;
        public int ProcessId;
        public string ProcessName;
        public string ProcessStartTimeUtc;
        public ulong MainImageBaseAddress;
        public ulong MainImageMappedSizeBytes;
        public int PageSizeBytes;
        public int QueryWorkingSetAttempts;
        public long QueryWorkingSetPageCount;
        public long ResidentTextPageCount;
        public ulong ResidentTextBytes;
        public long SharedResidentTextPages;
        public long PrivateResidentTextPages;
        public ulong SymbolCoveredResidentTextBytes;
        public ulong UncoveredResidentTextBytes;
        public long FullySymbolCoveredPages;
        public long PartiallySymbolCoveredPages;
        public long UnresolvedPages;
        public long MultipleSymbolPages;
        public bool PageCountClosure;
        public bool ResidentByteClosure;
        public ImageInspectionResult Image;
        public ResidentPageCapture[] ResidentPages;
        public PageSymbolCapture[] PageSymbols;
        public PageAttributionCapture[] PageAttribution;
        public PageAttributionCapture[] SymbolCoverage;
        public RollupCapture[] Categories;
        public RollupCapture[] StartupChains;
    }

    internal sealed class PeImageLayout
    {
        internal string ImagePath;
        internal string ExpectedPdbPath;
        internal ulong PreferredImageBase;
        internal ulong SizeOfImageBytes;
        internal ulong SizeOfHeadersBytes;
        internal uint SectionAlignmentBytes;
        internal List<PeSectionCapture> Sections;
        internal PeSectionCapture TextSection;
    }

    internal sealed class RawSymbol
    {
        internal string Name;
        internal ulong Address;
        internal uint Size;
        internal uint Tag;
        internal uint Flags;
    }

    internal sealed class SymbolLoadResult
    {
        internal ulong LoadedBase;
        internal string LoadedPdbPath;
        internal string SymbolType;
        internal List<RawSymbol> RawSymbols;
    }

    internal sealed class WorkingSetPage
    {
        internal ulong Address;
        internal bool Shared;
    }

    internal sealed class Interval
    {
        internal ulong Start;
        internal ulong End;
    }

    internal sealed class SymbolOverlap
    {
        internal SymbolCapture Symbol;
        internal ulong StartRva;
        internal ulong EndRva;
    }

    internal sealed class PageWork
    {
        internal ResidentPageCapture Output;
        internal ulong ResidentAbsoluteStart;
        internal ulong ResidentAbsoluteEnd;
        internal List<SymbolOverlap> Symbols = new List<SymbolOverlap>();
    }

    internal sealed class RollupBuilder
    {
        internal string Name;
        internal string Category;
        internal HashSet<int> Symbols = new HashSet<int>();
        internal HashSet<int> ResidentSymbols = new HashSet<int>();
        internal HashSet<ulong> PageTouches = new HashSet<ulong>();
        internal HashSet<ulong> ExclusivePages = new HashSet<ulong>();
        internal HashSet<ulong> MixedPages = new HashSet<ulong>();
        internal ulong ResidentOverlapBytesUnion;
        internal Dictionary<int, ulong> SymbolOverlapBytes = new Dictionary<int, ulong>();
    }

    public static class Profiler
    {
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const int ERROR_BAD_LENGTH = 24;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const int MaximumWorkingSetAttempts = 8;
        private const int MaximumWorkingSetEntries = 16 * 1024 * 1024;
        private const int MaximumPeSections = 128;
        private const uint IMAGE_SCN_CNT_CODE = 0x00000020;
        private const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
        private const uint IMAGE_SCN_MEM_READ = 0x40000000;
        private const uint IMAGE_SCN_MEM_WRITE = 0x80000000;

        private const uint SYMOPT_UNDNAME = 0x00000002;
        private const uint SYMOPT_DEFERRED_LOADS = 0x00000004;
        private const uint SYMOPT_LOAD_LINES = 0x00000010;
        private const uint SYMOPT_FAIL_CRITICAL_ERRORS = 0x00000200;
        private const uint SYMOPT_EXACT_SYMBOLS = 0x00000400;
        private const uint SYMOPT_IGNORE_NT_SYMPATH = 0x00001000;
        private const uint SYMOPT_NO_PROMPTS = 0x00080000;
        private const uint SYMOPT_DISABLE_SYMSRV_AUTODETECT = 0x02000000;
        private const uint SYMOPT_DISABLE_SRVSTAR_ON_STARTUP = 0x40000000;
        private const uint SYMFLAG_EXPORT = 0x00000200;
        private const uint SYMFLAG_FUNCTION = 0x00000800;
        private const uint SYMFLAG_THUNK = 0x00002000;
        private const uint SYMFLAG_PUBLIC_CODE = 0x00400000;

        private const uint SymTagFunction = 5;
        private const uint SymTagData = 7;
        private const uint SymTagPublicSymbol = 10;
        private const uint SymTagLabel = 9;
        private const uint SymTagThunk = 27;

        private static readonly object DbgHelpLock = new object();

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort ProcessorArchitecture;
            public ushort Reserved;
            public uint PageSize;
            public IntPtr MinimumApplicationAddress;
            public IntPtr MaximumApplicationAddress;
            public UIntPtr ActiveProcessorMask;
            public uint NumberOfProcessors;
            public uint ProcessorType;
            public uint AllocationGranularity;
            public ushort ProcessorLevel;
            public ushort ProcessorRevision;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYMBOL_INFOW
        {
            public uint SizeOfStruct;
            public uint TypeIndex;
            public ulong Reserved0;
            public ulong Reserved1;
            public uint Index;
            public uint Size;
            public ulong ModBase;
            public uint Flags;
            public ulong Value;
            public ulong Address;
            public uint Register;
            public uint Scope;
            public uint Tag;
            public uint NameLen;
            public uint MaxNameLen;
            public ushort Name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct IMAGEHLP_MODULEW64
        {
            public uint SizeOfStruct;
            public ulong BaseOfImage;
            public uint ImageSize;
            public uint TimeDateStamp;
            public uint CheckSum;
            public uint NumSyms;
            public int SymType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ModuleName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ImageName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string LoadedImageName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string LoadedPdbName;
            public uint CVSig;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 780)]
            public string CVData;
            public uint PdbSig;
            public Guid PdbSig70;
            public uint PdbAge;
            [MarshalAs(UnmanagedType.Bool)]
            public bool PdbUnmatched;
            [MarshalAs(UnmanagedType.Bool)]
            public bool DbgUnmatched;
            [MarshalAs(UnmanagedType.Bool)]
            public bool LineNumbers;
            [MarshalAs(UnmanagedType.Bool)]
            public bool GlobalSymbols;
            [MarshalAs(UnmanagedType.Bool)]
            public bool TypeInfo;
            [MarshalAs(UnmanagedType.Bool)]
            public bool SourceIndexed;
            [MarshalAs(UnmanagedType.Bool)]
            public bool Publics;
            public uint MachineType;
            public uint Reserved;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool SymEnumSymbolsProc(IntPtr symbolInfo, uint symbolSize, IntPtr userContext);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEventW(
            IntPtr eventAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool manualReset,
            [MarshalAs(UnmanagedType.Bool)] bool initialState,
            string name);

        [DllImport("kernel32.dll")]
        private static extern void GetNativeSystemInfo(out SYSTEM_INFO systemInfo);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryWorkingSet(IntPtr process, IntPtr buffer, int bufferLength);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr process,
            out PROCESS_MEMORY_COUNTERS_EX counters,
            uint size);

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern uint SymGetOptions();

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern uint SymSetOptions(uint options);

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymInitializeW(
            IntPtr process,
            string searchPath,
            [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ulong SymLoadModuleExW(
            IntPtr process,
            IntPtr file,
            string imageName,
            string moduleName,
            ulong baseOfDll,
            uint dllSize,
            IntPtr data,
            uint flags);

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymEnumSymbolsW(
            IntPtr process,
            ulong baseOfDll,
            string mask,
            SymEnumSymbolsProc callback,
            IntPtr userContext);

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymGetModuleInfoW64(
            IntPtr process,
            ulong address,
            ref IMAGEHLP_MODULEW64 moduleInfo);

        [DllImport("dbghelp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymCleanup(IntPtr process);

        public static ImageInspectionResult InspectImage(string imagePath)
        {
            PeImageLayout image = ReadPeImage(imagePath);
            ulong loadBase = image.PreferredImageBase != 0
                ? image.PreferredImageBase
                : 0x0000000180000000UL;
            SymbolLoadResult loaded = LoadSymbols(image, loadBase);
            int rawTextCount;
            int exactCount;
            int inferredCount;
            List<SymbolCapture> symbols = CanonicalizeSymbols(
                image,
                loaded,
                out rawTextCount,
                out exactCount,
                out inferredCount);

            return BuildInspection(
                image,
                loaded,
                symbols,
                rawTextCount,
                exactCount,
                inferredCount);
        }

        public static CaptureResult Capture(int processId)
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            string processName;
            string processStartTimeUtc;
            string mainImagePath;
            ulong mainImageBase;
            ulong mainImageMappedSize;
            ulong workingSetEstimate;

            using (Process process = Process.GetProcessById(processId))
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException("Process " + processId + " has already exited.");
                }

                process.Refresh();
                ProcessModule mainModule = process.MainModule;
                if (mainModule == null)
                {
                    throw new InvalidOperationException("Process " + processId + " did not expose a main module.");
                }

                processName = process.ProcessName;
                processStartTimeUtc = process.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                mainImagePath = Path.GetFullPath(mainModule.FileName);
                mainImageBase = ToUInt64(mainModule.BaseAddress);
                if (mainModule.ModuleMemorySize <= 0)
                {
                    throw new InvalidOperationException("The main module reported an invalid mapped size.");
                }

                mainImageMappedSize = checked((ulong)mainModule.ModuleMemorySize);
                workingSetEstimate = process.WorkingSet64 > 0
                    ? checked((ulong)process.WorkingSet64)
                    : 0UL;
            }

            PeImageLayout image = ReadPeImage(mainImagePath);
            ulong mappedImageEnd = CheckedAdd(mainImageBase, mainImageMappedSize, "main image mapped range");
            ulong peImageEnd = CheckedAdd(mainImageBase, image.SizeOfImageBytes, "main image PE range");
            if (peImageEnd > mappedImageEnd)
            {
                throw new InvalidOperationException(
                    "PE SizeOfImage exceeds the target main-module mapping: PE=" +
                    image.SizeOfImageBytes + ", mapped=" + mainImageMappedSize + ".");
            }

            SYSTEM_INFO systemInfo;
            GetNativeSystemInfo(out systemInfo);
            if (systemInfo.PageSize != 4096)
            {
                throw new InvalidOperationException(
                    "PSAPI_WORKING_SET_BLOCK uses 4 KiB virtual-page units; this tool requires a 4096-byte native page size but observed " +
                    systemInfo.PageSize + ".");
            }

            IntPtr processHandle = OpenProcess(
                PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                false,
                processId);
            if (processHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    "OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ) failed for PID " + processId + ".");
            }

            WorkingSetPage[] allPages;
            int queryAttempts;
            try
            {
                if (workingSetEstimate == 0)
                {
                    workingSetEstimate = ReadWorkingSetEstimate(processHandle, processId);
                }

                allPages = ReadWorkingSet(
                    processHandle,
                    checked((int)systemInfo.PageSize),
                    workingSetEstimate,
                    out queryAttempts);
            }
            finally
            {
                CloseHandle(processHandle);
            }

            ValidateProcessIdentity(
                processId,
                processStartTimeUtc,
                mainImagePath,
                mainImageBase);

            ulong textAbsoluteStart = CheckedAdd(
                mainImageBase,
                image.TextSection.RelativeVirtualAddress,
                ".text start");
            ulong textAbsoluteEnd = CheckedAdd(
                mainImageBase,
                image.TextSection.EndRelativeVirtualAddress,
                ".text end");

            List<WorkingSetPage> residentTextPages = new List<WorkingSetPage>();
            HashSet<ulong> seenPageAddresses = new HashSet<ulong>();
            for (int index = 0; index < allPages.Length; index++)
            {
                WorkingSetPage page = allPages[index];
                ulong pageEnd = CheckedAdd(page.Address, systemInfo.PageSize, "working-set page");
                if (page.Address < textAbsoluteEnd && pageEnd > textAbsoluteStart)
                {
                    if (!seenPageAddresses.Add(page.Address))
                    {
                        throw new InvalidOperationException(
                            "QueryWorkingSet returned duplicate page address 0x" +
                            page.Address.ToString("x", CultureInfo.InvariantCulture) + ".");
                    }

                    residentTextPages.Add(page);
                }
            }

            residentTextPages.Sort(delegate(WorkingSetPage left, WorkingSetPage right)
            {
                return left.Address.CompareTo(right.Address);
            });

            SymbolLoadResult loaded = LoadSymbols(image, mainImageBase);
            int rawTextCount;
            int exactCount;
            int inferredCount;
            List<SymbolCapture> symbols = CanonicalizeSymbols(
                image,
                loaded,
                out rawTextCount,
                out exactCount,
                out inferredCount);
            ImageInspectionResult inspection = BuildInspection(
                image,
                loaded,
                symbols,
                rawTextCount,
                exactCount,
                inferredCount);

            return BuildCapture(
                started,
                processId,
                processName,
                processStartTimeUtc,
                mainImageBase,
                mainImageMappedSize,
                checked((int)systemInfo.PageSize),
                queryAttempts,
                allPages.LongLength,
                image,
                inspection,
                symbols,
                residentTextPages);
        }

        private static ImageInspectionResult BuildInspection(
            PeImageLayout image,
            SymbolLoadResult loaded,
            List<SymbolCapture> symbols,
            int rawTextCount,
            int exactCount,
            int inferredCount)
        {
            RollupCapture[] categories = BuildOfflineCategoryRollups(symbols);
            return new ImageInspectionResult
            {
                ImagePath = image.ImagePath,
                ExpectedPdbPath = image.ExpectedPdbPath,
                LoadedPdbPath = loaded.LoadedPdbPath,
                DbgHelpSymbolType = loaded.SymbolType,
                PreferredImageBase = image.PreferredImageBase,
                LoadedSymbolBase = loaded.LoadedBase,
                SizeOfImageBytes = image.SizeOfImageBytes,
                SizeOfHeadersBytes = image.SizeOfHeadersBytes,
                SectionAlignmentBytes = image.SectionAlignmentBytes,
                TextSection = image.TextSection,
                RawSymbolCount = loaded.RawSymbols.Count,
                RawTextSymbolCount = rawTextCount,
                CanonicalTextSymbolCount = symbols.Count,
                ExactSizedTextSymbolCount = exactCount,
                InferredSizedTextSymbolCount = inferredCount,
                Symbols = symbols.ToArray(),
                Categories = categories
            };
        }

        private static CaptureResult BuildCapture(
            DateTimeOffset started,
            int processId,
            string processName,
            string processStartTimeUtc,
            ulong mainImageBase,
            ulong mainImageMappedSize,
            int pageSize,
            int queryAttempts,
            long allWorkingSetPages,
            PeImageLayout image,
            ImageInspectionResult inspection,
            List<SymbolCapture> symbols,
            List<WorkingSetPage> residentTextPages)
        {
            Dictionary<ulong, PageWork> pagesByAddress = new Dictionary<ulong, PageWork>();
            List<PageWork> pageWork = new List<PageWork>(residentTextPages.Count);
            ulong residentTextBytes = 0;
            long sharedPages = 0;
            long privatePages = 0;

            ulong textAbsoluteStart = mainImageBase + image.TextSection.RelativeVirtualAddress;
            ulong textAbsoluteEnd = mainImageBase + image.TextSection.EndRelativeVirtualAddress;
            for (int index = 0; index < residentTextPages.Count; index++)
            {
                WorkingSetPage nativePage = residentTextPages[index];
                ulong pageEnd = nativePage.Address + checked((ulong)pageSize);
                ulong residentStart = Math.Max(nativePage.Address, textAbsoluteStart);
                ulong residentEnd = Math.Min(pageEnd, textAbsoluteEnd);
                if (residentEnd <= residentStart)
                {
                    throw new InvalidOperationException("A filtered .text page had no .text byte overlap.");
                }

                ulong residentBytes = residentEnd - residentStart;
                residentTextBytes = CheckedAdd(residentTextBytes, residentBytes, "resident .text bytes");
                if (nativePage.Shared)
                {
                    sharedPages++;
                }
                else
                {
                    privatePages++;
                }

                ResidentPageCapture output = new ResidentPageCapture
                {
                    Index = index + 1,
                    PageAddress = nativePage.Address,
                    PageRelativeVirtualAddress = nativePage.Address - mainImageBase,
                    PageEndRelativeVirtualAddress = pageEnd - mainImageBase,
                    ResidentStartRelativeVirtualAddress = residentStart - mainImageBase,
                    ResidentEndRelativeVirtualAddress = residentEnd - mainImageBase,
                    ResidentTextBytes = residentBytes,
                    SharedWorkingSetPage = nativePage.Shared
                };
                PageWork work = new PageWork
                {
                    Output = output,
                    ResidentAbsoluteStart = residentStart,
                    ResidentAbsoluteEnd = residentEnd
                };
                pageWork.Add(work);
                pagesByAddress.Add(nativePage.Address, work);
            }

            for (int symbolIndex = 0; symbolIndex < symbols.Count; symbolIndex++)
            {
                SymbolCapture symbol = symbols[symbolIndex];
                ulong symbolAbsoluteStart = mainImageBase + symbol.RelativeVirtualAddress;
                ulong symbolAbsoluteEnd = mainImageBase + symbol.EndRelativeVirtualAddress;
                ulong firstPage = AlignDown(symbolAbsoluteStart, checked((ulong)pageSize));
                ulong lastPage = AlignDown(symbolAbsoluteEnd - 1UL, checked((ulong)pageSize));
                for (ulong pageAddress = firstPage; ; pageAddress += checked((ulong)pageSize))
                {
                    PageWork page;
                    if (pagesByAddress.TryGetValue(pageAddress, out page))
                    {
                        ulong overlapStart = Math.Max(symbolAbsoluteStart, page.ResidentAbsoluteStart);
                        ulong overlapEnd = Math.Min(symbolAbsoluteEnd, page.ResidentAbsoluteEnd);
                        if (overlapEnd > overlapStart)
                        {
                            page.Symbols.Add(new SymbolOverlap
                            {
                                Symbol = symbol,
                                StartRva = overlapStart - mainImageBase,
                                EndRva = overlapEnd - mainImageBase
                            });
                            symbol.ResidentPageTouches++;
                            symbol.ResidentOverlapBytes = CheckedAdd(
                                symbol.ResidentOverlapBytes,
                                overlapEnd - overlapStart,
                                "symbol resident overlap");
                        }
                    }

                    if (pageAddress == lastPage)
                    {
                        break;
                    }

                    if (pageAddress > UInt64.MaxValue - checked((ulong)pageSize))
                    {
                        throw new InvalidOperationException("Address overflow while walking symbol pages.");
                    }
                }
            }

            List<PageSymbolCapture> links = new List<PageSymbolCapture>();
            Dictionary<string, PageAttributionCapture> pageAttribution = new Dictionary<string, PageAttributionCapture>(StringComparer.Ordinal);
            Dictionary<string, PageAttributionCapture> coverageAttribution = new Dictionary<string, PageAttributionCapture>(StringComparer.Ordinal);
            Dictionary<string, RollupBuilder> categoryBuilders = CreateSymbolRollupBuilders(symbols, false);
            Dictionary<string, RollupBuilder> chainBuilders = CreateSymbolRollupBuilders(symbols, true);
            ulong symbolCoveredBytes = 0;
            ulong uncoveredBytes = 0;
            long fullyCoveredPages = 0;
            long partiallyCoveredPages = 0;
            long unresolvedPages = 0;
            long multipleSymbolPages = 0;

            for (int pageIndex = 0; pageIndex < pageWork.Count; pageIndex++)
            {
                PageWork page = pageWork[pageIndex];
                page.Symbols.Sort(delegate(SymbolOverlap left, SymbolOverlap right)
                {
                    int addressComparison = left.Symbol.RelativeVirtualAddress.CompareTo(
                        right.Symbol.RelativeVirtualAddress);
                    return addressComparison != 0
                        ? addressComparison
                        : StringComparer.Ordinal.Compare(left.Symbol.Name, right.Symbol.Name);
                });

                List<Interval> allIntervals = new List<Interval>();
                Dictionary<string, List<Interval>> categoryIntervals = new Dictionary<string, List<Interval>>(StringComparer.Ordinal);
                Dictionary<string, List<Interval>> chainIntervals = new Dictionary<string, List<Interval>>(StringComparer.Ordinal);
                HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> chains = new HashSet<string>(StringComparer.Ordinal);
                bool hasExact = false;
                bool hasInferred = false;

                for (int overlapIndex = 0; overlapIndex < page.Symbols.Count; overlapIndex++)
                {
                    SymbolOverlap overlap = page.Symbols[overlapIndex];
                    SymbolCapture symbol = overlap.Symbol;
                    allIntervals.Add(new Interval { Start = overlap.StartRva, End = overlap.EndRva });
                    AddInterval(categoryIntervals, symbol.Category, overlap.StartRva, overlap.EndRva);
                    AddInterval(chainIntervals, symbol.StartupChain, overlap.StartRva, overlap.EndRva);
                    categories.Add(symbol.Category);
                    chains.Add(symbol.StartupChain);
                    if (String.Equals(symbol.CoverageSizeSource, "PDB", StringComparison.Ordinal))
                    {
                        hasExact = true;
                    }
                    else
                    {
                        hasInferred = true;
                    }
                }

                ulong covered = MergeLength(allIntervals);
                if (covered > page.Output.ResidentTextBytes)
                {
                    throw new InvalidOperationException(
                        "Merged symbol coverage exceeds resident bytes for page 0x" +
                        page.Output.PageAddress.ToString("x", CultureInfo.InvariantCulture) + ".");
                }

                page.Output.SymbolCoveredBytes = covered;
                page.Output.UncoveredBytes = page.Output.ResidentTextBytes - covered;
                page.Output.SymbolCount = page.Symbols.Count;
                page.Output.CategoryCount = categories.Count;
                page.Output.StartupChainCount = chains.Count;
                page.Output.MultipleSymbolsSharePage = page.Symbols.Count > 1;
                page.Output.SymbolCoverageKind = covered == 0
                    ? "Unresolved"
                    : (covered == page.Output.ResidentTextBytes ? "Fully symbol-covered" : "Partially symbol-covered");
                page.Output.SymbolSizeQuality = page.Symbols.Count == 0
                    ? "Unresolved"
                    : (hasExact && hasInferred ? "Mixed PDB/inferred" : (hasExact ? "PDB sizes" : "Inferred sizes"));
                if (categories.Count == 0)
                {
                    page.Output.PageAttribution = "Unresolved";
                }
                else if (covered < page.Output.ResidentTextBytes)
                {
                    page.Output.PageAttribution = categories.Count == 1
                        ? First(categories) + " + unresolved bytes"
                        : "Mixed categories + unresolved bytes";
                }
                else
                {
                    page.Output.PageAttribution = categories.Count == 1
                        ? First(categories)
                        : "Mixed categories";
                }
                page.Output.Categories = JoinSorted(categories, "; ");
                page.Output.StartupChains = JoinSorted(chains, "; ");
                page.Output.ExampleSymbols = JoinExampleSymbols(page.Symbols, 8);

                symbolCoveredBytes = CheckedAdd(symbolCoveredBytes, covered, "symbol-covered resident bytes");
                uncoveredBytes = CheckedAdd(uncoveredBytes, page.Output.UncoveredBytes, "uncovered resident bytes");
                if (covered == 0)
                {
                    unresolvedPages++;
                }
                else if (covered == page.Output.ResidentTextBytes)
                {
                    fullyCoveredPages++;
                }
                else
                {
                    partiallyCoveredPages++;
                }

                if (page.Output.MultipleSymbolsSharePage)
                {
                    multipleSymbolPages++;
                }

                AddPageAttribution(pageAttribution, page.Output.PageAttribution, page.Output);
                AddPageAttribution(coverageAttribution, page.Output.SymbolCoverageKind, page.Output);

                for (int overlapIndex = 0; overlapIndex < page.Symbols.Count; overlapIndex++)
                {
                    SymbolOverlap overlap = page.Symbols[overlapIndex];
                    SymbolCapture symbol = overlap.Symbol;
                    if (page.Symbols.Count == 1)
                    {
                        symbol.SoleSymbolResidentPageTouches++;
                    }
                    else
                    {
                        symbol.SharedSymbolResidentPageTouches++;
                    }

                    links.Add(new PageSymbolCapture
                    {
                        PageIndex = page.Output.Index,
                        PageAddress = page.Output.PageAddress,
                        PageRelativeVirtualAddress = page.Output.PageRelativeVirtualAddress,
                        PageSymbolCount = page.Symbols.Count,
                        MultipleSymbolsSharePage = page.Symbols.Count > 1,
                        SymbolOrdinal = symbol.Ordinal,
                        SymbolRelativeVirtualAddress = symbol.RelativeVirtualAddress,
                        SymbolEndRelativeVirtualAddress = symbol.EndRelativeVirtualAddress,
                        SymbolName = symbol.Name,
                        Category = symbol.Category,
                        StartupChain = symbol.StartupChain,
                        PdbSymbolSizeBytes = symbol.PdbSymbolSizeBytes,
                        SymbolCoverageSizeBytes = symbol.CoverageSizeBytes,
                        CoverageSizeSource = symbol.CoverageSizeSource,
                        OverlapStartRelativeVirtualAddress = overlap.StartRva,
                        OverlapEndRelativeVirtualAddress = overlap.EndRva,
                        OverlapBytes = overlap.EndRva - overlap.StartRva,
                        ExecutionEvidence = false
                    });
                }

                AccumulateRollupsForPage(
                    categoryBuilders,
                    categoryIntervals,
                    categories,
                    page.Output,
                    page.Symbols,
                    false);
                AccumulateRollupsForPage(
                    chainBuilders,
                    chainIntervals,
                    chains,
                    page.Output,
                    page.Symbols,
                    true);
            }

            for (int symbolIndex = 0; symbolIndex < symbols.Count; symbolIndex++)
            {
                SymbolCapture symbol = symbols[symbolIndex];
                symbol.ResidentCoveragePercent = symbol.CoverageSizeBytes == 0
                    ? 0.0
                    : Math.Round(
                        ((double)symbol.ResidentOverlapBytes * 100.0) / (double)symbol.CoverageSizeBytes,
                        6);
            }

            long categoryAttributionPages = 0;
            ulong categoryAttributionBytes = 0;
            foreach (PageAttributionCapture row in pageAttribution.Values)
            {
                categoryAttributionPages += row.ResidentPages;
                categoryAttributionBytes = CheckedAdd(
                    categoryAttributionBytes,
                    row.ResidentTextBytes,
                    "category attribution bytes");
            }

            long coveragePages = fullyCoveredPages + partiallyCoveredPages + unresolvedPages;
            bool pageClosure =
                categoryAttributionPages == residentTextPages.Count &&
                coveragePages == residentTextPages.Count &&
                sharedPages + privatePages == residentTextPages.Count;
            bool byteClosure =
                categoryAttributionBytes == residentTextBytes &&
                symbolCoveredBytes + uncoveredBytes == residentTextBytes;
            if (!pageClosure || !byteClosure)
            {
                throw new InvalidOperationException(
                    "Resident .text accounting failed to close: pages=" + residentTextPages.Count +
                    ", categoryPages=" + categoryAttributionPages +
                    ", coveragePages=" + coveragePages +
                    ", residentBytes=" + residentTextBytes +
                    ", categoryBytes=" + categoryAttributionBytes +
                    ", coveredPlusUncovered=" + (symbolCoveredBytes + uncoveredBytes) + ".");
            }

            return new CaptureResult
            {
                CaptureStartedUtc = started.ToString("o", CultureInfo.InvariantCulture),
                CaptureCompletedUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ProcessId = processId,
                ProcessName = processName,
                ProcessStartTimeUtc = processStartTimeUtc,
                MainImageBaseAddress = mainImageBase,
                MainImageMappedSizeBytes = mainImageMappedSize,
                PageSizeBytes = pageSize,
                QueryWorkingSetAttempts = queryAttempts,
                QueryWorkingSetPageCount = allWorkingSetPages,
                ResidentTextPageCount = residentTextPages.Count,
                ResidentTextBytes = residentTextBytes,
                SharedResidentTextPages = sharedPages,
                PrivateResidentTextPages = privatePages,
                SymbolCoveredResidentTextBytes = symbolCoveredBytes,
                UncoveredResidentTextBytes = uncoveredBytes,
                FullySymbolCoveredPages = fullyCoveredPages,
                PartiallySymbolCoveredPages = partiallyCoveredPages,
                UnresolvedPages = unresolvedPages,
                MultipleSymbolPages = multipleSymbolPages,
                PageCountClosure = pageClosure,
                ResidentByteClosure = byteClosure,
                Image = inspection,
                ResidentPages = ToPageArray(pageWork),
                PageSymbols = links.ToArray(),
                PageAttribution = ToPageAttributionArray(pageAttribution),
                SymbolCoverage = ToPageAttributionArray(coverageAttribution),
                Categories = ToRollupArray(categoryBuilders, symbols),
                StartupChains = ToRollupArray(chainBuilders, symbols)
            };
        }

        private static PeImageLayout ReadPeImage(string imagePath)
        {
            if (String.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path is required.", "imagePath");
            }

            string fullImagePath = Path.GetFullPath(imagePath);
            if (!File.Exists(fullImagePath))
            {
                throw new FileNotFoundException("NativeAOT image was not found.", fullImagePath);
            }

            string expectedPdbPath = Path.ChangeExtension(fullImagePath, ".pdb");
            if (!File.Exists(expectedPdbPath))
            {
                throw new FileNotFoundException(
                    "The same-directory NativeAOT PDB was not found.",
                    expectedPdbPath);
            }

            using (FileStream stream = new FileStream(
                fullImagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII))
            {
                EnsureFileRange(stream, 0, 64, "DOS header");
                if (reader.ReadUInt16() != 0x5a4d)
                {
                    throw new InvalidDataException("Image does not begin with an MZ header: '" + fullImagePath + "'.");
                }

                stream.Position = 0x3c;
                int peHeaderOffset = reader.ReadInt32();
                if (peHeaderOffset < 0)
                {
                    throw new InvalidDataException("Image has a negative PE header offset.");
                }

                EnsureFileRange(stream, peHeaderOffset, 24, "PE signature and COFF header");
                stream.Position = peHeaderOffset;
                if (reader.ReadUInt32() != 0x00004550)
                {
                    throw new InvalidDataException("Image has an invalid PE signature: '" + fullImagePath + "'.");
                }

                reader.ReadUInt16();
                ushort sectionCount = reader.ReadUInt16();
                reader.ReadUInt32();
                reader.ReadUInt32();
                reader.ReadUInt32();
                ushort optionalHeaderSize = reader.ReadUInt16();
                reader.ReadUInt16();
                if (sectionCount == 0 || sectionCount > MaximumPeSections)
                {
                    throw new InvalidDataException("Unsupported PE section count: " + sectionCount + ".");
                }

                long optionalHeaderOffset = stream.Position;
                EnsureFileRange(stream, optionalHeaderOffset, optionalHeaderSize, "PE optional header");
                stream.Position = optionalHeaderOffset;
                ushort magic = reader.ReadUInt16();
                ulong imageBase;
                if (magic == 0x20b)
                {
                    stream.Position = optionalHeaderOffset + 24;
                    imageBase = reader.ReadUInt64();
                }
                else if (magic == 0x10b)
                {
                    stream.Position = optionalHeaderOffset + 28;
                    imageBase = reader.ReadUInt32();
                }
                else
                {
                    throw new InvalidDataException(
                        "Unsupported PE optional-header magic 0x" + magic.ToString("x", CultureInfo.InvariantCulture) + ".");
                }

                stream.Position = optionalHeaderOffset + 32;
                uint sectionAlignment = reader.ReadUInt32();
                stream.Position = optionalHeaderOffset + 56;
                uint sizeOfImage = reader.ReadUInt32();
                uint sizeOfHeaders = reader.ReadUInt32();
                if (sectionAlignment == 0 || sizeOfImage == 0 || sizeOfHeaders == 0)
                {
                    throw new InvalidDataException(
                        "PE image sizing fields are invalid: SectionAlignment=" + sectionAlignment +
                        ", SizeOfImage=" + sizeOfImage + ", SizeOfHeaders=" + sizeOfHeaders + ".");
                }

                long sectionHeadersOffset = checked(optionalHeaderOffset + optionalHeaderSize);
                EnsureFileRange(
                    stream,
                    sectionHeadersOffset,
                    checked((long)sectionCount * 40L),
                    "PE section table");
                List<PeSectionCapture> sections = new List<PeSectionCapture>(sectionCount);
                for (int ordinal = 0; ordinal < sectionCount; ordinal++)
                {
                    stream.Position = checked(sectionHeadersOffset + ordinal * 40L);
                    byte[] nameBytes = reader.ReadBytes(8);
                    if (nameBytes.Length != 8)
                    {
                        throw new EndOfStreamException("Unexpected end of file while reading a PE section name.");
                    }

                    int nameLength = Array.IndexOf(nameBytes, (byte)0);
                    if (nameLength < 0)
                    {
                        nameLength = nameBytes.Length;
                    }

                    string name = Encoding.ASCII.GetString(nameBytes, 0, nameLength);
                    uint virtualSize = reader.ReadUInt32();
                    uint virtualAddress = reader.ReadUInt32();
                    uint rawSize = reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    reader.ReadUInt16();
                    uint characteristics = reader.ReadUInt32();
                    ulong span = Math.Max((ulong)virtualSize, (ulong)rawSize);
                    ulong end = CheckedAdd(virtualAddress, span, "PE section " + name);
                    if (end > sizeOfImage)
                    {
                        end = sizeOfImage;
                    }

                    sections.Add(new PeSectionCapture
                    {
                        Ordinal = ordinal,
                        Name = name,
                        RelativeVirtualAddress = virtualAddress,
                        EndRelativeVirtualAddress = end,
                        MappedSizeBytes = end > virtualAddress ? end - virtualAddress : 0,
                        PeVirtualSizeBytes = virtualSize,
                        RawDataSizeBytes = rawSize,
                        Characteristics = characteristics,
                        ContainsCode = (characteristics & IMAGE_SCN_CNT_CODE) != 0,
                        IsExecutable = (characteristics & IMAGE_SCN_MEM_EXECUTE) != 0,
                        IsReadable = (characteristics & IMAGE_SCN_MEM_READ) != 0,
                        IsWritable = (characteristics & IMAGE_SCN_MEM_WRITE) != 0
                    });
                }

                sections.Sort(delegate(PeSectionCapture left, PeSectionCapture right)
                {
                    int addressComparison = left.RelativeVirtualAddress.CompareTo(right.RelativeVirtualAddress);
                    return addressComparison != 0 ? addressComparison : left.Ordinal.CompareTo(right.Ordinal);
                });
                for (int index = 0; index + 1 < sections.Count; index++)
                {
                    ulong nextStart = sections[index + 1].RelativeVirtualAddress;
                    if (sections[index].EndRelativeVirtualAddress > nextStart)
                    {
                        sections[index].EndRelativeVirtualAddress = nextStart;
                        sections[index].MappedSizeBytes = nextStart > sections[index].RelativeVirtualAddress
                            ? nextStart - sections[index].RelativeVirtualAddress
                            : 0;
                    }
                }

                PeSectionCapture text = null;
                for (int index = 0; index < sections.Count; index++)
                {
                    if (String.Equals(sections[index].Name, ".text", StringComparison.Ordinal))
                    {
                        text = sections[index];
                        break;
                    }
                }

                if (text == null)
                {
                    throw new InvalidDataException("The main executable does not contain a .text section.");
                }

                if (!text.ContainsCode || !text.IsExecutable || text.MappedSizeBytes == 0)
                {
                    throw new InvalidDataException(
                        "The .text section is not a non-empty executable code section.");
                }

                return new PeImageLayout
                {
                    ImagePath = fullImagePath,
                    ExpectedPdbPath = Path.GetFullPath(expectedPdbPath),
                    PreferredImageBase = imageBase,
                    SizeOfImageBytes = sizeOfImage,
                    SizeOfHeadersBytes = sizeOfHeaders,
                    SectionAlignmentBytes = sectionAlignment,
                    Sections = sections,
                    TextSection = text
                };
            }
        }

        private static SymbolLoadResult LoadSymbols(PeImageLayout image, ulong requestedBase)
        {
            lock (DbgHelpLock)
            {
                uint previousOptions = SymGetOptions();
                uint requestedOptions = previousOptions |
                    SYMOPT_UNDNAME |
                    SYMOPT_DEFERRED_LOADS |
                    SYMOPT_LOAD_LINES |
                    SYMOPT_FAIL_CRITICAL_ERRORS |
                    SYMOPT_EXACT_SYMBOLS |
                    SYMOPT_IGNORE_NT_SYMPATH |
                    SYMOPT_NO_PROMPTS |
                    SYMOPT_DISABLE_SYMSRV_AUTODETECT |
                    SYMOPT_DISABLE_SRVSTAR_ON_STARTUP;
                SymSetOptions(requestedOptions);

                IntPtr session = CreateEventW(IntPtr.Zero, false, false, null);
                if (session == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    SymSetOptions(previousOptions);
                    throw new Win32Exception(error, "CreateEventW failed while creating a unique DbgHelp session key.");
                }

                bool initialized = false;
                try
                {
                    string searchPath = Path.GetDirectoryName(image.ImagePath);
                    if (!SymInitializeW(session, searchPath, false))
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, "SymInitializeW failed for search path '" + searchPath + "'.");
                    }

                    initialized = true;
                    ulong loadedBase = SymLoadModuleExW(
                        session,
                        IntPtr.Zero,
                        image.ImagePath,
                        Path.GetFileNameWithoutExtension(image.ImagePath),
                        requestedBase,
                        checked((uint)image.SizeOfImageBytes),
                        IntPtr.Zero,
                        0);
                    if (loadedBase == 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(
                            error,
                            "SymLoadModuleExW failed for image '" + image.ImagePath + "'.");
                    }

                    List<RawSymbol> symbols = new List<RawSymbol>();
                    Exception callbackException = null;
                    int nameOffset = Marshal.OffsetOf(typeof(SYMBOL_INFOW), "Name").ToInt32();
                    SymEnumSymbolsProc callback = delegate(IntPtr symbolInfo, uint callbackSize, IntPtr context)
                    {
                        try
                        {
                            SYMBOL_INFOW native = (SYMBOL_INFOW)Marshal.PtrToStructure(symbolInfo, typeof(SYMBOL_INFOW));
                            if (native.NameLen > 1024 * 1024)
                            {
                                throw new InvalidDataException("DbgHelp returned an unreasonable symbol-name length.");
                            }

                            string name = native.NameLen == 0
                                ? String.Empty
                                : Marshal.PtrToStringUni(
                                    new IntPtr(symbolInfo.ToInt64() + nameOffset),
                                    checked((int)native.NameLen));
                            if (name != null)
                            {
                                name = name.TrimEnd('\0');
                            }
                            symbols.Add(new RawSymbol
                            {
                                Name = name,
                                Address = native.Address,
                                Size = native.Size != 0 ? native.Size : callbackSize,
                                Tag = native.Tag,
                                Flags = native.Flags
                            });
                            return true;
                        }
                        catch (Exception exception)
                        {
                            callbackException = exception;
                            return false;
                        }
                    };

                    if (!SymEnumSymbolsW(session, loadedBase, null, callback, IntPtr.Zero))
                    {
                        if (callbackException != null)
                        {
                            throw new InvalidOperationException(
                                "The DbgHelp symbol callback failed.",
                                callbackException);
                        }

                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, "SymEnumSymbolsW failed for the NativeAOT image.");
                    }

                    if (symbols.Count == 0)
                    {
                        throw new InvalidDataException(
                            "DbgHelp loaded the image but enumerated no symbols from the same-directory PDB.");
                    }

                    IMAGEHLP_MODULEW64 moduleInfo = new IMAGEHLP_MODULEW64();
                    moduleInfo.SizeOfStruct = checked((uint)Marshal.SizeOf(typeof(IMAGEHLP_MODULEW64)));
                    if (!SymGetModuleInfoW64(session, loadedBase, ref moduleInfo))
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new Win32Exception(error, "SymGetModuleInfoW64 failed after symbol enumeration.");
                    }

                    if (moduleInfo.PdbUnmatched)
                    {
                        throw new InvalidDataException("DbgHelp reported that the loaded PDB does not match the NativeAOT image.");
                    }

                    if (String.IsNullOrWhiteSpace(moduleInfo.LoadedPdbName))
                    {
                        throw new InvalidDataException("DbgHelp did not report a loaded PDB path.");
                    }

                    string loadedPdb = NormalizePath(moduleInfo.LoadedPdbName);
                    string expectedPdb = NormalizePath(image.ExpectedPdbPath);
                    if (!String.Equals(loadedPdb, expectedPdb, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "DbgHelp loaded a PDB other than the required same-directory NativeAOT PDB. Expected='" +
                            expectedPdb + "'; Loaded='" + loadedPdb + "'.");
                    }

                    return new SymbolLoadResult
                    {
                        LoadedBase = loadedBase,
                        LoadedPdbPath = loadedPdb,
                        SymbolType = GetSymbolTypeName(moduleInfo.SymType),
                        RawSymbols = symbols
                    };
                }
                finally
                {
                    if (initialized)
                    {
                        SymCleanup(session);
                    }

                    CloseHandle(session);
                    SymSetOptions(previousOptions);
                }
            }
        }

        private static List<SymbolCapture> CanonicalizeSymbols(
            PeImageLayout image,
            SymbolLoadResult loaded,
            out int rawTextCount,
            out int exactCount,
            out int inferredCount)
        {
            ulong textStart = image.TextSection.RelativeVirtualAddress;
            ulong textEnd = image.TextSection.EndRelativeVirtualAddress;
            Dictionary<ulong, List<RawSymbol>> groups = new Dictionary<ulong, List<RawSymbol>>();
            rawTextCount = 0;
            for (int index = 0; index < loaded.RawSymbols.Count; index++)
            {
                RawSymbol raw = loaded.RawSymbols[index];
                if (raw.Address < loaded.LoadedBase || !IsCodeLike(raw))
                {
                    continue;
                }

                ulong rva = raw.Address - loaded.LoadedBase;
                if (rva < textStart || rva >= textEnd)
                {
                    continue;
                }

                rawTextCount++;
                List<RawSymbol> atAddress;
                if (!groups.TryGetValue(rva, out atAddress))
                {
                    atAddress = new List<RawSymbol>();
                    groups.Add(rva, atAddress);
                }

                atAddress.Add(raw);
            }

            if (groups.Count == 0)
            {
                throw new InvalidDataException(
                    "DbgHelp enumerated symbols, but none were code-like symbols inside the PE .text section.");
            }

            List<ulong> addresses = new List<ulong>(groups.Keys);
            addresses.Sort();
            List<SymbolCapture> result = new List<SymbolCapture>(addresses.Count);
            exactCount = 0;
            inferredCount = 0;
            for (int addressIndex = 0; addressIndex < addresses.Count; addressIndex++)
            {
                ulong rva = addresses[addressIndex];
                List<RawSymbol> atAddress = groups[rva];
                atAddress.Sort(delegate(RawSymbol left, RawSymbol right)
                {
                    int scoreComparison = ScoreRawSymbol(right).CompareTo(ScoreRawSymbol(left));
                    if (scoreComparison != 0)
                    {
                        return scoreComparison;
                    }

                    int sizeComparison = right.Size.CompareTo(left.Size);
                    return sizeComparison != 0
                        ? sizeComparison
                        : StringComparer.Ordinal.Compare(left.Name, right.Name);
                });
                RawSymbol canonical = atAddress[0];
                uint pdbSize = 0;
                HashSet<string> aliases = new HashSet<string>(StringComparer.Ordinal);
                for (int rawIndex = 0; rawIndex < atAddress.Count; rawIndex++)
                {
                    if (atAddress[rawIndex].Size > pdbSize)
                    {
                        pdbSize = atAddress[rawIndex].Size;
                    }

                    if (!String.IsNullOrWhiteSpace(atAddress[rawIndex].Name))
                    {
                        aliases.Add(atAddress[rawIndex].Name);
                    }
                }

                ulong end;
                string sizeSource;
                if (pdbSize != 0)
                {
                    end = CheckedAdd(rva, pdbSize, "PDB symbol range");
                    if (end > textEnd)
                    {
                        end = textEnd;
                    }

                    sizeSource = "PDB";
                    exactCount++;
                }
                else
                {
                    end = addressIndex + 1 < addresses.Count ? addresses[addressIndex + 1] : textEnd;
                    sizeSource = "Inferred to next symbol";
                    inferredCount++;
                }

                if (end <= rva)
                {
                    continue;
                }

                string name = String.IsNullOrWhiteSpace(canonical.Name)
                    ? "<unnamed@0x" + rva.ToString("x", CultureInfo.InvariantCulture) + ">"
                    : canonical.Name;
                string category = Categorize(name);
                result.Add(new SymbolCapture
                {
                    Ordinal = result.Count + 1,
                    Name = name,
                    Aliases = JoinSorted(aliases, " | "),
                    Category = category,
                    StartupChain = GetStartupChain(name, category),
                    RelativeVirtualAddress = rva,
                    EndRelativeVirtualAddress = end,
                    PdbSymbolSizeBytes = pdbSize,
                    CoverageSizeBytes = end - rva,
                    CoverageSizeSource = sizeSource,
                    Tag = canonical.Tag,
                    TagName = GetTagName(canonical.Tag),
                    Flags = canonical.Flags
                });
            }

            if (result.Count == 0)
            {
                throw new InvalidDataException("No usable .text symbol ranges remained after canonicalization.");
            }

            return result;
        }

        private static RollupCapture[] BuildOfflineCategoryRollups(List<SymbolCapture> symbols)
        {
            Dictionary<string, RollupBuilder> builders = CreateSymbolRollupBuilders(symbols, false);
            return ToRollupArray(builders, symbols);
        }

        private static Dictionary<string, RollupBuilder> CreateSymbolRollupBuilders(
            List<SymbolCapture> symbols,
            bool chains)
        {
            Dictionary<string, RollupBuilder> builders = new Dictionary<string, RollupBuilder>(StringComparer.Ordinal);
            for (int index = 0; index < symbols.Count; index++)
            {
                SymbolCapture symbol = symbols[index];
                string name = chains ? symbol.StartupChain : symbol.Category;
                string key = chains ? symbol.Category + "\u001f" + name : name;
                RollupBuilder builder;
                if (!builders.TryGetValue(key, out builder))
                {
                    builder = new RollupBuilder
                    {
                        Name = name,
                        Category = symbol.Category
                    };
                    builders.Add(key, builder);
                }

                builder.Symbols.Add(symbol.Ordinal);
            }

            return builders;
        }

        private static void AccumulateRollupsForPage(
            Dictionary<string, RollupBuilder> builders,
            Dictionary<string, List<Interval>> intervalsByName,
            HashSet<string> namesOnPage,
            ResidentPageCapture page,
            List<SymbolOverlap> overlaps,
            bool chains)
        {
            foreach (string name in namesOnPage)
            {
                string category = null;
                if (chains)
                {
                    for (int overlapIndex = 0; overlapIndex < overlaps.Count; overlapIndex++)
                    {
                        if (String.Equals(overlaps[overlapIndex].Symbol.StartupChain, name, StringComparison.Ordinal))
                        {
                            category = overlaps[overlapIndex].Symbol.Category;
                            break;
                        }
                    }
                }

                string key = chains ? category + "\u001f" + name : name;
                RollupBuilder builder = builders[key];
                builder.PageTouches.Add(page.PageAddress);
                if (namesOnPage.Count == 1)
                {
                    builder.ExclusivePages.Add(page.PageAddress);
                }
                else
                {
                    builder.MixedPages.Add(page.PageAddress);
                }

                builder.ResidentOverlapBytesUnion = CheckedAdd(
                    builder.ResidentOverlapBytesUnion,
                    MergeLength(intervalsByName[name]),
                    "rollup resident overlap");
            }

            for (int overlapIndex = 0; overlapIndex < overlaps.Count; overlapIndex++)
            {
                SymbolOverlap overlap = overlaps[overlapIndex];
                string name = chains ? overlap.Symbol.StartupChain : overlap.Symbol.Category;
                string key = chains ? overlap.Symbol.Category + "\u001f" + name : name;
                RollupBuilder builder = builders[key];
                builder.ResidentSymbols.Add(overlap.Symbol.Ordinal);
                ulong prior;
                builder.SymbolOverlapBytes.TryGetValue(overlap.Symbol.Ordinal, out prior);
                builder.SymbolOverlapBytes[overlap.Symbol.Ordinal] = CheckedAdd(
                    prior,
                    overlap.EndRva - overlap.StartRva,
                    "rollup symbol overlap");
            }
        }

        private static RollupCapture[] ToRollupArray(
            Dictionary<string, RollupBuilder> builders,
            List<SymbolCapture> symbols)
        {
            Dictionary<int, SymbolCapture> symbolsByOrdinal = new Dictionary<int, SymbolCapture>();
            for (int index = 0; index < symbols.Count; index++)
            {
                symbolsByOrdinal.Add(symbols[index].Ordinal, symbols[index]);
            }

            List<RollupCapture> rows = new List<RollupCapture>(builders.Count);
            foreach (RollupBuilder builder in builders.Values)
            {
                List<KeyValuePair<int, ulong>> rankedSymbols = new List<KeyValuePair<int, ulong>>(builder.SymbolOverlapBytes);
                rankedSymbols.Sort(delegate(KeyValuePair<int, ulong> left, KeyValuePair<int, ulong> right)
                {
                    int bytesComparison = right.Value.CompareTo(left.Value);
                    if (bytesComparison != 0)
                    {
                        return bytesComparison;
                    }

                    return StringComparer.Ordinal.Compare(
                        symbolsByOrdinal[left.Key].Name,
                        symbolsByOrdinal[right.Key].Name);
                });
                List<string> examples = new List<string>();
                for (int index = 0; index < rankedSymbols.Count && index < 5; index++)
                {
                    examples.Add(symbolsByOrdinal[rankedSymbols[index].Key].Name);
                }

                rows.Add(new RollupCapture
                {
                    Name = builder.Name,
                    Category = builder.Category,
                    SymbolCount = builder.Symbols.Count,
                    ResidentSymbolCount = builder.ResidentSymbols.Count,
                    ResidentPageTouches = builder.PageTouches.Count,
                    ExclusiveResidentPages = builder.ExclusivePages.Count,
                    MixedResidentPagesTouched = builder.MixedPages.Count,
                    ResidentOverlapBytesUnion = builder.ResidentOverlapBytesUnion,
                    TopSymbols = String.Join(" | ", examples.ToArray())
                });
            }

            rows.Sort(delegate(RollupCapture left, RollupCapture right)
            {
                int pageComparison = right.ResidentPageTouches.CompareTo(left.ResidentPageTouches);
                if (pageComparison != 0)
                {
                    return pageComparison;
                }

                int bytesComparison = right.ResidentOverlapBytesUnion.CompareTo(left.ResidentOverlapBytesUnion);
                return bytesComparison != 0
                    ? bytesComparison
                    : StringComparer.Ordinal.Compare(left.Name, right.Name);
            });
            return rows.ToArray();
        }

        private static ResidentPageCapture[] ToPageArray(List<PageWork> pages)
        {
            ResidentPageCapture[] result = new ResidentPageCapture[pages.Count];
            for (int index = 0; index < pages.Count; index++)
            {
                result[index] = pages[index].Output;
            }

            return result;
        }

        private static PageAttributionCapture[] ToPageAttributionArray(
            Dictionary<string, PageAttributionCapture> rows)
        {
            List<PageAttributionCapture> result = new List<PageAttributionCapture>(rows.Values);
            result.Sort(delegate(PageAttributionCapture left, PageAttributionCapture right)
            {
                int pageComparison = right.ResidentPages.CompareTo(left.ResidentPages);
                return pageComparison != 0
                    ? pageComparison
                    : StringComparer.Ordinal.Compare(left.Name, right.Name);
            });
            return result.ToArray();
        }

        private static void AddPageAttribution(
            Dictionary<string, PageAttributionCapture> rows,
            string name,
            ResidentPageCapture page)
        {
            PageAttributionCapture row;
            if (!rows.TryGetValue(name, out row))
            {
                row = new PageAttributionCapture { Name = name };
                rows.Add(name, row);
            }

            row.ResidentPages++;
            row.ResidentTextBytes = CheckedAdd(row.ResidentTextBytes, page.ResidentTextBytes, "page attribution");
            if (page.SharedWorkingSetPage)
            {
                row.SharedWorkingSetPages++;
            }
            else
            {
                row.PrivateWorkingSetPages++;
            }
        }

        private static void AddInterval(
            Dictionary<string, List<Interval>> intervals,
            string name,
            ulong start,
            ulong end)
        {
            List<Interval> list;
            if (!intervals.TryGetValue(name, out list))
            {
                list = new List<Interval>();
                intervals.Add(name, list);
            }

            list.Add(new Interval { Start = start, End = end });
        }

        private static ulong MergeLength(List<Interval> intervals)
        {
            if (intervals == null || intervals.Count == 0)
            {
                return 0;
            }

            intervals.Sort(delegate(Interval left, Interval right)
            {
                int startComparison = left.Start.CompareTo(right.Start);
                return startComparison != 0 ? startComparison : left.End.CompareTo(right.End);
            });
            ulong total = 0;
            ulong currentStart = intervals[0].Start;
            ulong currentEnd = intervals[0].End;
            for (int index = 1; index < intervals.Count; index++)
            {
                Interval interval = intervals[index];
                if (interval.Start <= currentEnd)
                {
                    if (interval.End > currentEnd)
                    {
                        currentEnd = interval.End;
                    }
                }
                else
                {
                    total = CheckedAdd(total, currentEnd - currentStart, "merged interval length");
                    currentStart = interval.Start;
                    currentEnd = interval.End;
                }
            }

            return CheckedAdd(total, currentEnd - currentStart, "merged interval length");
        }

        private static WorkingSetPage[] ReadWorkingSet(
            IntPtr process,
            int pageSize,
            ulong workingSetEstimateBytes,
            out int attempts)
        {
            ulong estimatedPages = workingSetEstimateBytes / checked((ulong)pageSize);
            ulong requestedCapacity = estimatedPages + estimatedPages / 4UL + 1024UL;
            int capacity = checked((int)Math.Min(
                MaximumWorkingSetEntries,
                Math.Max(16384UL, requestedCapacity)));

            for (attempts = 1; attempts <= MaximumWorkingSetAttempts; attempts++)
            {
                long bufferBytes = checked(((long)capacity + 1L) * IntPtr.Size);
                if (bufferBytes > Int32.MaxValue)
                {
                    throw new InvalidOperationException("QueryWorkingSet buffer exceeds the Win32 size limit.");
                }

                IntPtr buffer = Marshal.AllocHGlobal(new IntPtr(bufferBytes));
                try
                {
                    Marshal.WriteIntPtr(buffer, IntPtr.Zero);
                    if (!QueryWorkingSet(process, buffer, checked((int)bufferBytes)))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != ERROR_BAD_LENGTH && error != ERROR_INSUFFICIENT_BUFFER)
                        {
                            throw new Win32Exception(error, "QueryWorkingSet failed on attempt " + attempts + ".");
                        }

                        if (attempts == MaximumWorkingSetAttempts || capacity >= MaximumWorkingSetEntries)
                        {
                            throw new Win32Exception(
                                error,
                                "QueryWorkingSet still required a larger buffer after " + attempts + " attempts.");
                        }

                        ulong required = ReadNativeUInt(buffer, 0);
                        if (required > MaximumWorkingSetEntries)
                        {
                            throw new InvalidOperationException(
                                "QueryWorkingSet requested " + required + " entries, above the bounded limit.");
                        }

                        long doubled = Math.Min((long)MaximumWorkingSetEntries, (long)capacity * 2L);
                        long withSlack = required == 0
                            ? 0
                            : Math.Min((long)MaximumWorkingSetEntries, checked((long)required + 1024L));
                        capacity = checked((int)Math.Max(doubled, withSlack));
                        continue;
                    }

                    ulong countValue = ReadNativeUInt(buffer, 0);
                    if (countValue > (ulong)capacity || countValue > Int32.MaxValue)
                    {
                        throw new InvalidOperationException(
                            "QueryWorkingSet returned more entries than fit in its buffer.");
                    }

                    int count = checked((int)countValue);
                    WorkingSetPage[] pages = new WorkingSetPage[count];
                    for (int index = 0; index < count; index++)
                    {
                        ulong flags = ReadNativeUInt(buffer, checked((index + 1) * IntPtr.Size));
                        pages[index] = new WorkingSetPage
                        {
                            Address = flags & ~0xfffUL,
                            Shared = (flags & 0x100UL) != 0
                        };
                    }

                    return pages;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            throw new InvalidOperationException("QueryWorkingSet retry loop ended unexpectedly.");
        }

        private static ulong ReadWorkingSetEstimate(IntPtr process, int processId)
        {
            PROCESS_MEMORY_COUNTERS_EX counters = new PROCESS_MEMORY_COUNTERS_EX();
            counters.cb = checked((uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX)));
            if (!GetProcessMemoryInfo(process, out counters, counters.cb))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    "GetProcessMemoryInfo failed while sizing QueryWorkingSet for PID " + processId + ".");
            }

            return ToUInt64(counters.WorkingSetSize);
        }

        private static void ValidateProcessIdentity(
            int processId,
            string initialStartTimeUtc,
            string initialImagePath,
            ulong initialBaseAddress)
        {
            using (Process process = Process.GetProcessById(processId))
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException("Process " + processId + " exited during QueryWorkingSet.");
                }

                ProcessModule mainModule = process.MainModule;
                if (mainModule == null)
                {
                    throw new InvalidOperationException("Process " + processId + " lost its main-module information.");
                }

                string currentStart = process.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                string currentPath = Path.GetFullPath(mainModule.FileName);
                ulong currentBase = ToUInt64(mainModule.BaseAddress);
                if (!String.Equals(currentStart, initialStartTimeUtc, StringComparison.Ordinal) ||
                    !String.Equals(currentPath, initialImagePath, StringComparison.OrdinalIgnoreCase) ||
                    currentBase != initialBaseAddress)
                {
                    throw new InvalidOperationException(
                        "PID " + processId + " changed identity during the snapshot.");
                }
            }
        }

        private static bool IsCodeLike(RawSymbol symbol)
        {
            if (symbol.Tag == SymTagData)
            {
                return false;
            }

            if (symbol.Tag == SymTagFunction ||
                symbol.Tag == SymTagPublicSymbol ||
                symbol.Tag == SymTagThunk ||
                symbol.Tag == SymTagLabel)
            {
                return true;
            }

            return (symbol.Flags & (SYMFLAG_FUNCTION | SYMFLAG_THUNK | SYMFLAG_PUBLIC_CODE | SYMFLAG_EXPORT)) != 0;
        }

        private static int ScoreRawSymbol(RawSymbol symbol)
        {
            int score = 0;
            if (symbol.Tag == SymTagFunction)
            {
                score += 500;
            }
            else if (symbol.Tag == SymTagThunk)
            {
                score += 450;
            }
            else if (symbol.Tag == SymTagPublicSymbol)
            {
                score += 400;
            }
            else if (symbol.Tag == SymTagLabel)
            {
                score += 100;
            }

            if ((symbol.Flags & SYMFLAG_FUNCTION) != 0)
            {
                score += 100;
            }

            if ((symbol.Flags & SYMFLAG_PUBLIC_CODE) != 0)
            {
                score += 50;
            }

            if (symbol.Size != 0)
            {
                score += 25;
            }

            return score;
        }

        private static string Categorize(string name)
        {
            string lower = name.ToLowerInvariant();
            int genericArgumentStart = lower.IndexOf('<');
            if (genericArgumentStart > 0)
            {
                // NativeAOT public names embed instantiated type arguments. Classify
                // by the implementation owner, not by Jalium/CSS/font names that only
                // occur inside a System generic argument list.
                lower = lower.Substring(0, genericArgumentStart);
            }

            if (lower.Contains("jalium.ui.memoryprobe") ||
                lower.Contains("memoryprobe!program") ||
                lower.Contains("memoryprobe_program") ||
                lower.Contains("<module>__startupcode"))
            {
                return "Application/probe";
            }

            if ((lower.Contains("theme") || lower.Contains("defaultstyle")) &&
                (lower.Contains("generated") ||
                 lower.Contains("frameworktheme") ||
                 lower.Contains("themedictionary") ||
                 lower.Contains("themebuilder") ||
                 lower.Contains("themes/") ||
                 lower.Contains("themes.")))
            {
                return "Generated themes";
            }

            if (lower.Contains("system.reflection") ||
                lower.Contains("system_reflection") ||
                lower.Contains("_reflection_") ||
                lower.Contains("runtimetype") ||
                lower.Contains("runtime_typeinfos") ||
                lower.Contains("runtimeconstructorinfo") ||
                lower.Contains("runtimemethodinfo") ||
                lower.Contains("customattribute") ||
                lower.Contains("activator") ||
                lower.Contains("aottyperegistry") ||
                lower.Contains("reflectionmap") ||
                lower.Contains("typemanager") ||
                lower.Contains("metadatareader") ||
                lower.Contains("runtimeaugments"))
            {
                return "System reflection/AOT metadata";
            }

            if (lower.Contains("css") ||
                lower.Contains("stylesheet") ||
                lower.Contains("styleengine") ||
                lower.Contains("selectorparser") ||
                lower.Contains("styleproperty"))
            {
                return "CSS/style";
            }

            if (lower.Contains("font") ||
                lower.Contains("glyph") ||
                lower.Contains("textmeasurement") ||
                lower.Contains("textformat") ||
                lower.Contains("textlayout") ||
                lower.Contains("_dwrite") ||
                lower.Contains("dwrite_") ||
                lower.Contains("dwrite::") ||
                lower.Contains("harfbuzz") ||
                lower.Contains("typeface"))
            {
                return "Fonts/text";
            }

            if (lower.Contains("pinvoke") ||
                lower.Contains("interop") ||
                lower.Contains("nativemethod") ||
                lower.Contains("__p/invoke") ||
                lower.Contains("unmanagedcallersonly"))
            {
                return "Interop/native boundary";
            }

            if (lower.StartsWith("rhp", StringComparison.Ordinal) ||
                lower.StartsWith("rh", StringComparison.Ordinal) ||
                lower.StartsWith("wks::", StringComparison.Ordinal) ||
                lower.StartsWith("gcconfig::", StringComparison.Ordinal) ||
                lower.StartsWith("gctoeeinterface::", StringComparison.Ordinal) ||
                lower.StartsWith("eetogcinterface::", StringComparison.Ordinal) ||
                lower.StartsWith("gcslotdecoder", StringComparison.Ordinal) ||
                lower.StartsWith("tgcinfodecoder", StringComparison.Ordinal) ||
                lower.Contains("internal.runtime") ||
                lower.Contains("internal_runtime") ||
                lower.Contains("runtimeexceptionhelpers") ||
                lower.Contains("gchelpers") ||
                lower.Contains("portableexceptionhandlers") ||
                lower.Contains("__managed__main"))
            {
                return "NativeAOT runtime";
            }

            if (lower.StartsWith("system.", StringComparison.Ordinal) ||
                lower.StartsWith("system_", StringComparison.Ordinal) ||
                lower.StartsWith("s_p_", StringComparison.Ordinal) ||
                lower.StartsWith("microsoft_", StringComparison.Ordinal) ||
                lower.StartsWith("globalizationnative_", StringComparison.Ordinal) ||
                lower.Contains("!system.") ||
                lower.Contains(" system."))
            {
                return "System libraries";
            }

            if (lower.Contains("jalium.ui") ||
                lower.Contains("jalium_ui") ||
                lower.Contains("jalium::ui"))
            {
                return "Jalium framework";
            }

            return "Other";
        }

        private static string GetStartupChain(string name, string category)
        {
            string lower = name.ToLowerInvariant();
            if (category == "Generated themes")
            {
                int themeMarker = lower.IndexOf("_themes_", StringComparison.Ordinal);
                int jalxamlMarker = themeMarker >= 0
                    ? lower.IndexOf("_jalxaml", themeMarker + 8, StringComparison.Ordinal)
                    : -1;
                if (themeMarker >= 0 && jalxamlMarker > themeMarker + 8)
                {
                    string themePath = name.Substring(
                        themeMarker + 8,
                        jalxamlMarker - (themeMarker + 8)).Replace('_', '/');
                    return "Generated theme dictionary: " + themePath;
                }

                if (lower.Contains("frameworktheme")) return "Framework theme generation/materialization";
                if (lower.Contains("thememanager")) return "ThemeManager initialization";
                if (lower.Contains("resourcedictionary")) return "Generated theme ResourceDictionary population";
                if (lower.Contains("defaultstyle")) return "Generated default-style materialization";
                return "Generated theme initialization";
            }

            if (category == "System reflection/AOT metadata")
            {
                if (lower.Contains("aottyperegistry")) return "AotTypeRegistry initialization/lookup";
                if (lower.Contains("customattribute")) return "Custom-attribute resolution";
                if (lower.Contains("activator")) return "Activator/type construction";
                if (lower.Contains("runtimetype")) return "RuntimeType metadata lookup";
                if (lower.Contains("reflection")) return "Reflection metadata lookup";
                return "NativeAOT type metadata";
            }

            if (category == "CSS/style")
            {
                if (lower.Contains("csspropertyregistry") || lower.Contains("propertymetadata")) return "CSS property registry/metadata";
                if (lower.Contains("cssmappings") || lower.Contains("mapping")) return "CSS property mappings";
                if (lower.Contains("selector")) return "CSS selector parsing/indexing";
                if (lower.Contains("parser")) return "CSS parsing";
                if (lower.Contains("style") || lower.Contains("cascade")) return "CSS cascade/style evaluation";
                string cssOwner = ExtractOwner(name);
                return String.IsNullOrWhiteSpace(cssOwner) ? "CSS initialization" : cssOwner;
            }

            if (category == "Fonts/text")
            {
                if (lower.Contains("fontcollection") || lower.Contains("fontfamily")) return "Font collection/family resolution";
                if (lower.Contains("textmeasurement") || lower.Contains("textlayout")) return "Text measurement/layout";
                if (lower.Contains("textformat")) return "Native text-format creation";
                if (lower.Contains("glyph")) return "Glyph shaping/raster preparation";
                string textOwner = ExtractOwner(name);
                return String.IsNullOrWhiteSpace(textOwner) ? "Font/text initialization" : textOwner;
            }

            if (category == "NativeAOT runtime")
            {
                if (lower.Contains("gc") || lower.StartsWith("rhpnew", StringComparison.Ordinal)) return "NativeAOT allocation/GC helpers";
                if (lower.Contains("exception")) return "NativeAOT exception helpers";
                if (lower.Contains("type") || lower.Contains("cast")) return "NativeAOT type/cast helpers";
                return "NativeAOT runtime startup/helpers";
            }

            if (category == "Application/probe") return "Probe entry/Application/Window startup";
            if (category == "Interop/native boundary") return "Managed/native initialization boundary";

            string owner = ExtractOwner(name);
            if (String.IsNullOrWhiteSpace(owner))
            {
                return category + " miscellaneous";
            }

            return owner;
        }

        private static string ExtractOwner(string name)
        {
            string value = name.Trim();
            int paren = value.IndexOf('(');
            if (paren > 0)
            {
                value = value.Substring(0, paren);
            }

            int bang = value.LastIndexOf('!');
            if (bang >= 0 && bang + 1 < value.Length)
            {
                value = value.Substring(bang + 1);
            }

            int doubleColon = value.LastIndexOf("::", StringComparison.Ordinal);
            if (doubleColon > 0)
            {
                value = value.Substring(0, doubleColon);
            }
            else
            {
                int nativeAotMethod = value.LastIndexOf("__", StringComparison.Ordinal);
                if (nativeAotMethod > 0)
                {
                    value = value.Substring(0, nativeAotMethod).TrimEnd('_');
                }
                else
                {
                    int dot = value.LastIndexOf('.');
                    if (dot > 0)
                    {
                        value = value.Substring(0, dot);
                    }
                }
            }

            if (value.Length > 180)
            {
                value = value.Substring(value.Length - 180);
            }

            return value;
        }

        private static string GetTagName(uint tag)
        {
            switch (tag)
            {
                case 0: return "Null";
                case 5: return "Function";
                case 7: return "Data";
                case 9: return "Label";
                case 10: return "PublicSymbol";
                case 27: return "Thunk";
                default: return "Tag" + tag.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string GetSymbolTypeName(int symbolType)
        {
            switch (symbolType)
            {
                case 0: return "None";
                case 1: return "COFF";
                case 2: return "CodeView";
                case 3: return "PDB";
                case 4: return "Export";
                case 5: return "Deferred";
                case 6: return "SYM";
                case 7: return "DIA";
                case 8: return "Virtual";
                default: return "SymType" + symbolType.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string NormalizePath(string path)
        {
            string value = path;
            if (value.StartsWith("\\\\?\\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }

            return Path.GetFullPath(value);
        }

        private static string First(HashSet<string> values)
        {
            foreach (string value in values)
            {
                return value;
            }

            return String.Empty;
        }

        private static string JoinSorted(HashSet<string> values, string separator)
        {
            List<string> sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);
            return String.Join(separator, sorted.ToArray());
        }

        private static string JoinExampleSymbols(List<SymbolOverlap> symbols, int maximum)
        {
            List<string> examples = new List<string>();
            for (int index = 0; index < symbols.Count && index < maximum; index++)
            {
                examples.Add(symbols[index].Symbol.Name);
            }

            string result = String.Join(" | ", examples.ToArray());
            if (symbols.Count > maximum)
            {
                result += " | ... (" + (symbols.Count - maximum).ToString(CultureInfo.InvariantCulture) + " more)";
            }

            return result;
        }

        private static void EnsureFileRange(FileStream stream, long offset, long size, string description)
        {
            if (offset < 0 || size < 0 || offset > stream.Length || size > stream.Length - offset)
            {
                throw new InvalidDataException(
                    "Image is truncated while reading " + description +
                    ": offset=" + offset + ", size=" + size + ", fileLength=" + stream.Length + ".");
            }
        }

        private static ulong CheckedAdd(ulong left, ulong right, string description)
        {
            if (left > UInt64.MaxValue - right)
            {
                throw new InvalidOperationException("Address/size overflow while calculating " + description + ".");
            }

            return left + right;
        }

        private static ulong AlignDown(ulong value, ulong alignment)
        {
            return value - value % alignment;
        }

        private static ulong ReadNativeUInt(IntPtr address, int offset)
        {
            return IntPtr.Size == 8
                ? unchecked((ulong)Marshal.ReadInt64(address, offset))
                : unchecked((uint)Marshal.ReadInt32(address, offset));
        }

        private static ulong ToUInt64(IntPtr value)
        {
            return IntPtr.Size == 8
                ? unchecked((ulong)value.ToInt64())
                : unchecked((uint)value.ToInt32());
        }

        private static ulong ToUInt64(UIntPtr value)
        {
            return UIntPtr.Size == 8 ? value.ToUInt64() : value.ToUInt32();
        }
    }
}
'@
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists: '$OutputDirectory'. Choose a new directory."
}

$parentDirectory = Split-Path -Parent $OutputDirectory
if ([string]::IsNullOrWhiteSpace($parentDirectory)) {
    throw "Output directory has no parent path: '$OutputDirectory'."
}

[void](New-Item -ItemType Directory -Path $parentDirectory -Force)

if ($PSCmdlet.ParameterSetName -eq 'Offline') {
    $ImagePath = [System.IO.Path]::GetFullPath($ImagePath)
    $inspection = [JaliumAotResidentSymbols.Profiler]::InspectImage($ImagePath)
    [void](New-Item -ItemType Directory -Path $OutputDirectory)

    $symbolRows = @($inspection.Symbols | ForEach-Object {
        [pscustomobject][ordered]@{
            Ordinal = [int]$_.Ordinal
            RVA = '0x{0:x16}' -f [uint64]$_.RelativeVirtualAddress
            EndRVA = '0x{0:x16}' -f [uint64]$_.EndRelativeVirtualAddress
            Function = $_.Name
            Aliases = $_.Aliases
            Category = $_.Category
            StartupChain = $_.StartupChain
            PdbSymbolSizeBytes = [uint32]$_.PdbSymbolSizeBytes
            CoverageSizeBytes = [uint64]$_.CoverageSizeBytes
            CoverageSizeSource = $_.CoverageSizeSource
            Tag = $_.TagName
            TagValue = [uint32]$_.Tag
            Flags = '0x{0:x8}' -f [uint32]$_.Flags
        }
    })

    $categoryRows = @($inspection.Categories | ForEach-Object {
        [pscustomobject][ordered]@{
            Category = $_.Name
            SymbolCount = [int]$_.SymbolCount
            ExactResidentPageMetricsAvailable = $false
        }
    } | Sort-Object -Property @{ Expression = 'SymbolCount'; Descending = $true }, Category)

    $summary = [pscustomobject][ordered]@{
        SchemaVersion = 1
        Mode = 'offline NativeAOT PDB validation'
        ImagePath = $inspection.ImagePath
        ExpectedSameDirectoryPdbPath = $inspection.ExpectedPdbPath
        LoadedPdbPath = $inspection.LoadedPdbPath
        LoadedExpectedSameDirectoryPdb = ($inspection.LoadedPdbPath -ieq $inspection.ExpectedPdbPath)
        DbgHelpSymbolType = $inspection.DbgHelpSymbolType
        PreferredImageBase = '0x{0:x16}' -f [uint64]$inspection.PreferredImageBase
        LoadedSymbolBase = '0x{0:x16}' -f [uint64]$inspection.LoadedSymbolBase
        SizeOfImageBytes = [uint64]$inspection.SizeOfImageBytes
        TextRVA = '0x{0:x16}' -f [uint64]$inspection.TextSection.RelativeVirtualAddress
        TextEndRVA = '0x{0:x16}' -f [uint64]$inspection.TextSection.EndRelativeVirtualAddress
        TextMappedSizeBytes = [uint64]$inspection.TextSection.MappedSizeBytes
        RawSymbolCount = [int]$inspection.RawSymbolCount
        RawTextSymbolCount = [int]$inspection.RawTextSymbolCount
        CanonicalTextSymbolCount = [int]$inspection.CanonicalTextSymbolCount
        ExactSizedTextSymbolCount = [int]$inspection.ExactSizedTextSymbolCount
        InferredSizedTextSymbolCount = [int]$inspection.InferredSizedTextSymbolCount
        ValidationPassed = $true
    }

    $profile = [ordered]@{
        Summary = $summary
        CountingNotes = [ordered]@{
            OfflineMode = 'No process is started or sampled. This validates PE parsing, embedded C# interop, exact same-directory PDB loading, and .text symbol enumeration.'
            SymbolRangeRule = 'PDB symbol sizes are retained. A zero-sized symbol is given a clearly marked inferred range ending at the next code-like symbol or .text end.'
            ResidencyRule = 'Offline validation contains no working-set or execution evidence.'
        }
        Categories = $categoryRows
        Symbols = $symbolRows
    }

    $profile | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'validation.json') -Encoding UTF8
    $summary | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding UTF8
    $categoryRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'categories.csv') -NoTypeInformation -Encoding UTF8
    $symbolRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'symbols.csv') -NoTypeInformation -Encoding UTF8

    $report = New-Object 'System.Collections.Generic.List[string]'
    [void]$report.Add('# NativeAOT PDB offline validation')
    [void]$report.Add('')
    [void]$report.Add("- Image: ``$($inspection.ImagePath)``")
    [void]$report.Add("- Loaded PDB: ``$($inspection.LoadedPdbPath)``")
    [void]$report.Add("- DbgHelp symbol type: $($inspection.DbgHelpSymbolType)")
    $offlineTextStartRva = '0x{0:x}' -f [uint64]$inspection.TextSection.RelativeVirtualAddress
    $offlineTextEndRva = '0x{0:x}' -f [uint64]$inspection.TextSection.EndRelativeVirtualAddress
    [void]$report.Add("- .text: RVA ``$offlineTextStartRva`` to ``$offlineTextEndRva`` ($([uint64]$inspection.TextSection.MappedSizeBytes) bytes)")
    [void]$report.Add("- Symbols: $($inspection.RawSymbolCount) raw, $($inspection.RawTextSymbolCount) raw in .text, $($inspection.CanonicalTextSymbolCount) canonical .text ranges")
    [void]$report.Add("- Size quality: $($inspection.ExactSizedTextSymbolCount) PDB-sized; $($inspection.InferredSizedTextSymbolCount) inferred zero-size ranges")
    [void]$report.Add('')
    [void]$report.Add('This mode does not inspect a process and provides no evidence that a function executed. It only proves that the PE and its exact same-directory NativeAOT PDB can be parsed and enumerated.')
    $report | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.md') -Encoding UTF8

    Write-Output "Offline NativeAOT PDB validation complete: $OutputDirectory"
    Write-Output ("PDB={0}; raw symbols={1:N0}; canonical .text symbols={2:N0}" -f
        $inspection.LoadedPdbPath,
        $inspection.RawSymbolCount,
        $inspection.CanonicalTextSymbolCount)
    return
}

$capture = [JaliumAotResidentSymbols.Profiler]::Capture($TargetProcessId)
[void](New-Item -ItemType Directory -Path $OutputDirectory)

$symbolRows = @($capture.Image.Symbols | ForEach-Object {
    [pscustomobject][ordered]@{
        Ordinal = [int]$_.Ordinal
        RVA = '0x{0:x16}' -f [uint64]$_.RelativeVirtualAddress
        EndRVA = '0x{0:x16}' -f [uint64]$_.EndRelativeVirtualAddress
        Function = $_.Name
        Aliases = $_.Aliases
        Category = $_.Category
        StartupChain = $_.StartupChain
        PdbSymbolSizeBytes = [uint32]$_.PdbSymbolSizeBytes
        CoverageSizeBytes = [uint64]$_.CoverageSizeBytes
        CoverageSizeSource = $_.CoverageSizeSource
        Tag = $_.TagName
        Flags = '0x{0:x8}' -f [uint32]$_.Flags
        ResidentPageTouches = [long]$_.ResidentPageTouches
        SoleSymbolResidentPageTouches = [long]$_.SoleSymbolResidentPageTouches
        SharedSymbolResidentPageTouches = [long]$_.SharedSymbolResidentPageTouches
        ResidentOverlapBytes = [uint64]$_.ResidentOverlapBytes
        ResidentOverlapKiB = [Math]::Round([double]$_.ResidentOverlapBytes / 1KB, 3)
        ResidentCoveragePercent = [Math]::Round([double]$_.ResidentCoveragePercent, 3)
        ExecutionEvidence = $false
    }
})

$residentPageRows = @($capture.ResidentPages | ForEach-Object {
    [pscustomobject][ordered]@{
        Index = [int]$_.Index
        PageAddress = '0x{0:x16}' -f [uint64]$_.PageAddress
        PageRVA = '0x{0:x16}' -f [uint64]$_.PageRelativeVirtualAddress
        PageEndRVA = '0x{0:x16}' -f [uint64]$_.PageEndRelativeVirtualAddress
        ResidentStartRVA = '0x{0:x16}' -f [uint64]$_.ResidentStartRelativeVirtualAddress
        ResidentEndRVA = '0x{0:x16}' -f [uint64]$_.ResidentEndRelativeVirtualAddress
        ResidentTextBytes = [uint64]$_.ResidentTextBytes
        SharedWorkingSetPage = [bool]$_.SharedWorkingSetPage
        SymbolCoveredBytes = [uint64]$_.SymbolCoveredBytes
        UncoveredBytes = [uint64]$_.UncoveredBytes
        SymbolCount = [int]$_.SymbolCount
        CategoryCount = [int]$_.CategoryCount
        StartupChainCount = [int]$_.StartupChainCount
        MultipleSymbolsSharePage = [bool]$_.MultipleSymbolsSharePage
        SymbolCoverageKind = $_.SymbolCoverageKind
        SymbolSizeQuality = $_.SymbolSizeQuality
        PageAttribution = $_.PageAttribution
        Categories = $_.Categories
        StartupChains = $_.StartupChains
        ExampleSymbols = $_.ExampleSymbols
        ExecutionEvidence = $false
    }
})

$pageSymbolRows = @($capture.PageSymbols | ForEach-Object {
    [pscustomobject][ordered]@{
        PageIndex = [int]$_.PageIndex
        PageAddress = '0x{0:x16}' -f [uint64]$_.PageAddress
        PageRVA = '0x{0:x16}' -f [uint64]$_.PageRelativeVirtualAddress
        PageSymbolCount = [int]$_.PageSymbolCount
        MultipleSymbolsSharePage = [bool]$_.MultipleSymbolsSharePage
        SymbolOrdinal = [int]$_.SymbolOrdinal
        SymbolRVA = '0x{0:x16}' -f [uint64]$_.SymbolRelativeVirtualAddress
        SymbolEndRVA = '0x{0:x16}' -f [uint64]$_.SymbolEndRelativeVirtualAddress
        Function = $_.SymbolName
        Category = $_.Category
        StartupChain = $_.StartupChain
        PdbSymbolSizeBytes = [uint32]$_.PdbSymbolSizeBytes
        SymbolCoverageSizeBytes = [uint64]$_.SymbolCoverageSizeBytes
        CoverageSizeSource = $_.CoverageSizeSource
        OverlapStartRVA = '0x{0:x16}' -f [uint64]$_.OverlapStartRelativeVirtualAddress
        OverlapEndRVA = '0x{0:x16}' -f [uint64]$_.OverlapEndRelativeVirtualAddress
        OverlapBytes = [uint64]$_.OverlapBytes
        ExecutionEvidence = [bool]$_.ExecutionEvidence
    }
})

$categoryRows = @($capture.Categories | ForEach-Object {
    [pscustomobject][ordered]@{
        Category = $_.Name
        SymbolCount = [int]$_.SymbolCount
        ResidentSymbolCount = [int]$_.ResidentSymbolCount
        ResidentPageTouches = [long]$_.ResidentPageTouches
        ExclusiveResidentPages = [long]$_.ExclusiveResidentPages
        MixedResidentPagesTouched = [long]$_.MixedResidentPagesTouched
        ResidentOverlapBytesUnion = [uint64]$_.ResidentOverlapBytesUnion
        ResidentOverlapMiBUnion = ConvertTo-Mebibytes ([double]$_.ResidentOverlapBytesUnion)
        TopSymbols = $_.TopSymbols
        AdditiveAcrossCategories = $false
    }
} | Sort-Object -Property @{ Expression = 'ResidentPageTouches'; Descending = $true },
    @{ Expression = 'ResidentOverlapBytesUnion'; Descending = $true }, Category)

$startupChainRows = @($capture.StartupChains | ForEach-Object {
    [pscustomobject][ordered]@{
        Category = $_.Category
        StartupChain = $_.Name
        SymbolCount = [int]$_.SymbolCount
        ResidentSymbolCount = [int]$_.ResidentSymbolCount
        ResidentPageTouches = [long]$_.ResidentPageTouches
        ExclusiveResidentPages = [long]$_.ExclusiveResidentPages
        MixedResidentPagesTouched = [long]$_.MixedResidentPagesTouched
        ResidentOverlapBytesUnion = [uint64]$_.ResidentOverlapBytesUnion
        ResidentOverlapMiBUnion = ConvertTo-Mebibytes ([double]$_.ResidentOverlapBytesUnion)
        TopSymbols = $_.TopSymbols
        AdditiveAcrossChains = $false
    }
} | Sort-Object -Property @{ Expression = 'ResidentPageTouches'; Descending = $true },
    @{ Expression = 'ExclusiveResidentPages'; Descending = $true },
    @{ Expression = 'ResidentOverlapBytesUnion'; Descending = $true }, StartupChain)

$pageAttributionRows = @($capture.PageAttribution | ForEach-Object {
    [pscustomobject][ordered]@{
        PageAttribution = $_.Name
        ResidentPages = [long]$_.ResidentPages
        ResidentTextBytes = [uint64]$_.ResidentTextBytes
        ResidentTextMiB = ConvertTo-Mebibytes ([double]$_.ResidentTextBytes)
        SharedWorkingSetPages = [long]$_.SharedWorkingSetPages
        PrivateWorkingSetPages = [long]$_.PrivateWorkingSetPages
        AdditiveAndClosesToResidentText = $true
    }
})

$symbolCoverageRows = @($capture.SymbolCoverage | ForEach-Object {
    [pscustomobject][ordered]@{
        SymbolCoverageKind = $_.Name
        ResidentPages = [long]$_.ResidentPages
        ResidentTextBytes = [uint64]$_.ResidentTextBytes
        ResidentTextMiB = ConvertTo-Mebibytes ([double]$_.ResidentTextBytes)
        SharedWorkingSetPages = [long]$_.SharedWorkingSetPages
        PrivateWorkingSetPages = [long]$_.PrivateWorkingSetPages
        AdditiveAndClosesToResidentText = $true
    }
})

$actionableCategories = @(
    'Generated themes',
    'System reflection/AOT metadata',
    'CSS/style',
    'Fonts/text',
    'Jalium framework',
    'Interop/native boundary',
    'Application/probe'
)
$recommendedChain = @($startupChainRows | Where-Object {
    $_.ResidentPageTouches -gt 0 -and $actionableCategories -contains $_.Category
} | Sort-Object -Property @{ Expression = 'ExclusiveResidentPages'; Descending = $true },
    @{ Expression = 'ResidentPageTouches'; Descending = $true },
    @{ Expression = 'ResidentOverlapBytesUnion'; Descending = $true } | Select-Object -First 1)
if ($recommendedChain.Count -eq 0) {
    $recommendedChain = @($startupChainRows | Where-Object { $_.ResidentPageTouches -gt 0 } |
        Select-Object -First 1)
}

if ($recommendedChain.Count -gt 0) {
    $recommendedCategory = [string]$recommendedChain[0].Category
    $recommendedChainName = [string]$recommendedChain[0].StartupChain
    $recommendedSuggestion = Get-InstrumentationSuggestion -Category $recommendedCategory
}
else {
    $recommendedCategory = 'Unresolved'
    $recommendedChainName = 'No symbol-covered resident startup chain'
    $recommendedSuggestion = 'Improve symbol coverage first, then add startup instrumentation around process entry, managed Main, Window.Show, and first-frame completion.'
}

$pageAttributionTotal = [long](($pageAttributionRows | Measure-Object -Property ResidentPages -Sum).Sum)
$pageAttributionBytes = [uint64](($pageAttributionRows | Measure-Object -Property ResidentTextBytes -Sum).Sum)
$coveragePageTotal = [long](($symbolCoverageRows | Measure-Object -Property ResidentPages -Sum).Sum)
$coverageByteTotal = [uint64](($symbolCoverageRows | Measure-Object -Property ResidentTextBytes -Sum).Sum)
if ($pageAttributionTotal -ne [long]$capture.ResidentTextPageCount -or
    $coveragePageTotal -ne [long]$capture.ResidentTextPageCount -or
    $pageAttributionBytes -ne [uint64]$capture.ResidentTextBytes -or
    $coverageByteTotal -ne [uint64]$capture.ResidentTextBytes) {
    throw 'PowerShell output projection did not preserve resident .text page/byte closure.'
}

$summary = [pscustomobject][ordered]@{
    SchemaVersion = 1
    MeasurementKind = 'single external read-only main-executable .text resident-page snapshot'
    TargetProcessId = [int]$capture.ProcessId
    TargetProcessName = $capture.ProcessName
    TargetStartTimeUtc = $capture.ProcessStartTimeUtc
    CaptureStartedUtc = $capture.CaptureStartedUtc
    CaptureCompletedUtc = $capture.CaptureCompletedUtc
    MainImagePath = $capture.Image.ImagePath
    MainImageBaseAddress = '0x{0:x16}' -f [uint64]$capture.MainImageBaseAddress
    MainImageMappedSizeBytes = [uint64]$capture.MainImageMappedSizeBytes
    SameDirectoryPdbPath = $capture.Image.LoadedPdbPath
    LoadedExpectedSameDirectoryPdb = ($capture.Image.LoadedPdbPath -ieq $capture.Image.ExpectedPdbPath)
    DbgHelpSymbolType = $capture.Image.DbgHelpSymbolType
    TextRVA = '0x{0:x16}' -f [uint64]$capture.Image.TextSection.RelativeVirtualAddress
    TextEndRVA = '0x{0:x16}' -f [uint64]$capture.Image.TextSection.EndRelativeVirtualAddress
    TextMappedSizeBytes = [uint64]$capture.Image.TextSection.MappedSizeBytes
    PageSizeBytes = [int]$capture.PageSizeBytes
    QueryWorkingSetAttempts = [int]$capture.QueryWorkingSetAttempts
    QueryWorkingSetPageCount = [long]$capture.QueryWorkingSetPageCount
    ResidentTextPages = [long]$capture.ResidentTextPageCount
    ResidentTextBytes = [uint64]$capture.ResidentTextBytes
    ResidentTextMiB = ConvertTo-Mebibytes ([double]$capture.ResidentTextBytes)
    SharedResidentTextPages = [long]$capture.SharedResidentTextPages
    PrivateResidentTextPages = [long]$capture.PrivateResidentTextPages
    SymbolCoveredResidentTextBytes = [uint64]$capture.SymbolCoveredResidentTextBytes
    SymbolCoveredResidentTextPercent = ConvertTo-Percent ([double]$capture.SymbolCoveredResidentTextBytes) ([double]$capture.ResidentTextBytes)
    UncoveredResidentTextBytes = [uint64]$capture.UncoveredResidentTextBytes
    FullySymbolCoveredPages = [long]$capture.FullySymbolCoveredPages
    PartiallySymbolCoveredPages = [long]$capture.PartiallySymbolCoveredPages
    UnresolvedPages = [long]$capture.UnresolvedPages
    MultipleSymbolPages = [long]$capture.MultipleSymbolPages
    RawPdbSymbols = [int]$capture.Image.RawSymbolCount
    RawTextSymbols = [int]$capture.Image.RawTextSymbolCount
    CanonicalTextSymbols = [int]$capture.Image.CanonicalTextSymbolCount
    ExactSizedTextSymbols = [int]$capture.Image.ExactSizedTextSymbolCount
    InferredSizedTextSymbols = [int]$capture.Image.InferredSizedTextSymbolCount
    PageCountClosure = [bool]$capture.PageCountClosure
    ResidentByteClosure = [bool]$capture.ResidentByteClosure
    PageAttributionRowsClose = ($pageAttributionTotal -eq [long]$capture.ResidentTextPageCount)
    RecommendedNextStartupCategory = $recommendedCategory
    RecommendedNextStartupChain = $recommendedChainName
    RecommendationBasis = 'largest actionable chain by exclusive resident pages, then unique page touches and union overlap bytes; residency is not execution evidence'
}

$profile = [ordered]@{
    Summary = $summary
    CountingNotes = [ordered]@{
        ResidentPageRule = 'QueryWorkingSet supplies unique currently resident virtual pages. Only pages intersecting the main executable PE .text range are included.'
        PageClosureRule = 'resident-pages.csv contains each .text resident page exactly once. page-attribution.csv and symbol-coverage.csv are additive and each closes to the resident .text page and byte totals.'
        FunctionRule = 'symbols.csv and page-symbols.csv describe PDB symbol ranges that overlap resident bytes. Their page touches are non-additive because several methods can share one resident page.'
        SharedPageRule = 'MultipleSymbolsSharePage means code ranges share a resident 4 KiB page. It does not mean any listed method executed.'
        ExecutionRule = 'A resident instruction page is presence evidence only. QueryWorkingSet and PDB coverage do not provide instruction-pointer samples, call counts, or proof of execution.'
        CategoryRule = 'Category page touches are non-additive. PageAttribution assigns each page once to its sole category, a mixed-category bucket, an unresolved bucket, or an explicit category/mixed plus unresolved-bytes bucket so page and byte totals remain closed without charging unresolved bytes silently.'
        SymbolSizeRule = 'PDB sizes are retained. Zero-sized symbols use a clearly labeled inferred range to the next code-like symbol or .text end.'
        SnapshotRule = 'The target is not suspended. The script opens it read-only for QueryWorkingSet, validates PID identity immediately afterward, and resolves symbols offline from the image and same-directory PDB.'
    }
    Recommendation = [ordered]@{
        Category = $recommendedCategory
        StartupChain = $recommendedChainName
        Instrumentation = $recommendedSuggestion
    }
    PageAttribution = $pageAttributionRows
    SymbolCoverage = $symbolCoverageRows
    Categories = $categoryRows
    StartupChains = $startupChainRows
    ResidentPages = $residentPageRows
    PageSymbols = $pageSymbolRows
    Symbols = $symbolRows
}

$profile | ConvertTo-Json -Depth 9 |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'profile.json') -Encoding UTF8
$summary | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding UTF8
$residentPageRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'resident-pages.csv') -NoTypeInformation -Encoding UTF8
$pageSymbolRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'page-symbols.csv') -NoTypeInformation -Encoding UTF8
$symbolRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'symbols.csv') -NoTypeInformation -Encoding UTF8
$pageAttributionRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'page-attribution.csv') -NoTypeInformation -Encoding UTF8
$symbolCoverageRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'symbol-coverage.csv') -NoTypeInformation -Encoding UTF8
$categoryRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'categories.csv') -NoTypeInformation -Encoding UTF8
$startupChainRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'startup-chains.csv') -NoTypeInformation -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
[void]$report.Add('# NativeAOT resident .text symbols')
[void]$report.Add('')
[void]$report.Add("PID $($capture.ProcessId), ``$($capture.ProcessName)``, captured $($capture.CaptureStartedUtc).")
[void]$report.Add('')
[void]$report.Add("The main executable has **$($capture.ResidentTextPageCount)** resident .text pages ($([Math]::Round([double]$capture.ResidentTextBytes / 1MB, 3)) MiB). Symbol ranges cover $([Math]::Round([double]$capture.SymbolCoveredResidentTextBytes / 1MB, 3)) MiB as a per-page union; $($capture.MultipleSymbolPages) pages contain code ranges from more than one symbol.")
[void]$report.Add('')
[void]$report.Add('Resident code pages show which instruction bytes are present in the working set. They do not show that a method executed. Every shared page is counted once in the closed page totals, while function and category page touches are explicitly non-additive.')
[void]$report.Add('')
[void]$report.Add('## Closed page attribution')
[void]$report.Add('')
[void]$report.Add('| Attribution | Pages | Resident MiB | Shared pages | Private pages |')
[void]$report.Add('|---|---:|---:|---:|---:|')
foreach ($row in $pageAttributionRows) {
    [void]$report.Add("| $(ConvertTo-MarkdownCell $row.PageAttribution) | $($row.ResidentPages) | $($row.ResidentTextMiB) | $($row.SharedWorkingSetPages) | $($row.PrivateWorkingSetPages) |")
}
[void]$report.Add('')
[void]$report.Add('## Largest symbol categories')
[void]$report.Add('')
[void]$report.Add('| Category | Page touches | Exclusive pages | Mixed pages touched | Union overlap MiB | Resident symbols |')
[void]$report.Add('|---|---:|---:|---:|---:|---:|')
foreach ($row in @($categoryRows | Select-Object -First 12)) {
    [void]$report.Add("| $(ConvertTo-MarkdownCell $row.Category) | $($row.ResidentPageTouches) | $($row.ExclusiveResidentPages) | $($row.MixedResidentPagesTouched) | $($row.ResidentOverlapMiBUnion) | $($row.ResidentSymbolCount) |")
}
[void]$report.Add('')
[void]$report.Add('## Highest-value next startup chain')
[void]$report.Add('')
[void]$report.Add("**$recommendedCategory → $recommendedChainName**")
[void]$report.Add('')
[void]$report.Add($recommendedSuggestion)
[void]$report.Add('')
[void]$report.Add('This recommendation ranks resident page coverage to choose where measurement is most likely to pay off. Add timing and invocation counters before treating it as a causal startup cost.')
[void]$report.Add('')
[void]$report.Add('## Largest resident symbol overlaps')
[void]$report.Add('')
[void]$report.Add('| Function | Category | RVA | PDB size | Resident overlap KiB | Page touches | Shared-page touches |')
[void]$report.Add('|---|---|---:|---:|---:|---:|---:|')
foreach ($row in @($symbolRows | Where-Object { $_.ResidentPageTouches -gt 0 } |
    Sort-Object -Property @{ Expression = 'ResidentOverlapBytes'; Descending = $true },
        @{ Expression = 'ResidentPageTouches'; Descending = $true }, Function |
    Select-Object -First $TopCount)) {
    [void]$report.Add("| $(ConvertTo-MarkdownCell $row.Function) | $(ConvertTo-MarkdownCell $row.Category) | ``$($row.RVA)`` | $($row.PdbSymbolSizeBytes) | $($row.ResidentOverlapKiB) | $($row.ResidentPageTouches) | $($row.SharedSymbolResidentPageTouches) |")
}
$report | Set-Content -LiteralPath (Join-Path $OutputDirectory 'report.md') -Encoding UTF8

Write-Output "NativeAOT resident-symbol profile complete: $OutputDirectory"
Write-Output ("Resident .text={0:N3} MiB ({1:N0} pages); symbol-covered union={2:N3} MiB; multi-symbol pages={3:N0}" -f
    (ConvertTo-Mebibytes ([double]$capture.ResidentTextBytes)),
    $capture.ResidentTextPageCount,
    (ConvertTo-Mebibytes ([double]$capture.SymbolCoveredResidentTextBytes)),
    $capture.MultipleSymbolPages)
Write-Output "Next startup chain to instrument: $recommendedCategory -> $recommendedChainName"
