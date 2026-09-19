[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateRange(1, 2147483647)]
    [int]$TargetProcessId,

    [string]$OutputDirectory,

    [ValidateRange(0, 600)]
    [int]$SampleDelaySeconds = 0,

    [ValidateRange(1, 100)]
    [int]$TopModuleCount = 20,

    # Explicitly scope diagnostics for another locally built probe to its full
    # executable path. Omitting this retains the original MemoryProbe guard.
    [string]$ExpectedExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [string]::IsNullOrWhiteSpace($ExpectedExecutablePath)) {
    $ExpectedExecutablePath = [System.IO.Path]::GetFullPath(
        (Resolve-Path -LiteralPath $ExpectedExecutablePath).Path)
}

function ConvertTo-Mebibytes {
    param([double]$Bytes)

    return [Math]::Round($Bytes / 1MB, 3)
}

function Get-ValidatedProbeProcess {
    param([int]$Id)

    try {
        $target = [System.Diagnostics.Process]::GetProcessById($Id)
        if ($target.HasExited) {
            throw "Process $Id has already exited."
        }

        $target.Refresh()
        $processName = $target.ProcessName
        $mainModule = $target.MainModule
        if ($null -eq $mainModule) {
            throw "Process $Id did not expose a main module."
        }

        $mainModulePath = [System.IO.Path]::GetFullPath($mainModule.FileName)
        $mainModuleName = [System.IO.Path]::GetFileName($mainModulePath)
        if (-not [string]::IsNullOrWhiteSpace($ExpectedExecutablePath)) {
            if (-not [string]::Equals($mainModulePath, $ExpectedExecutablePath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "PID $Id does not match the explicitly selected executable '$ExpectedExecutablePath'. Actual='$mainModulePath'."
            }
        }
        elseif ($processName -ine 'Jalium.UI.MemoryProbe' -or
            $mainModuleName -ine 'Jalium.UI.MemoryProbe.exe') {
            throw (
                "Refusing to inspect PID $Id because it is not Jalium.UI.MemoryProbe. " +
                "ProcessName='$processName'; MainModule='$mainModulePath'.")
        }

        return $target
    }
    catch [System.ArgumentException] {
        throw "Jalium.UI.MemoryProbe PID $Id does not exist or exited before it could be opened. $($_.Exception.Message)"
    }
    catch [System.ComponentModel.Win32Exception] {
        throw (
            "Unable to inspect Jalium.UI.MemoryProbe PID $Id. " +
            "Win32Error=$($_.Exception.NativeErrorCode): $($_.Exception.Message)")
    }
    catch [System.UnauthorizedAccessException] {
        throw "Access denied while validating Jalium.UI.MemoryProbe PID $Id. $($_.Exception.Message)"
    }
    catch [System.InvalidOperationException] {
        throw "Jalium.UI.MemoryProbe PID $Id exited or became unavailable during validation. $($_.Exception.Message)"
    }
}

function Get-ModuleTags {
    param(
        [string]$Name,
        [string]$Path
    )

    $tags = New-Object 'System.Collections.Generic.List[string]'
    $lowerName = $Name.ToLowerInvariant()

    if ($lowerName.StartsWith('jalium.native.') -and $lowerName.EndsWith('.dll')) {
        [void]$tags.Add('JaliumNative')
    }

    if ($lowerName -match '^(coreclr|clr|clrjit|mscoree|hostfxr|hostpolicy|mscordaccore|mscordbi|sos)\.dll$') {
        [void]$tags.Add('CLR')
    }

    if ($lowerName -match '^(dwrite|dwritecore|usp10|fontsub|t2embed|freetype|freetype6|harfbuzz|textshaping)\.dll$') {
        [void]$tags.Add('Font')
    }

    if ($Path -match '\\DriverStore\\FileRepository\\' -or
        $lowerName -match '^(nvwgf2umx|nvldumdx|nvd3dumx|nvoglv64|nvgpucomp64|nvapi64|nvspcap64|igd10iumd64|igd12umd64|igd9dxva64|igc64|igdusc64|amdxx64|atidxx64|atio6axx|amdvlk64|amdxc64)\.dll$') {
        [void]$tags.Add('GpuDriver')
    }

    if ($lowerName -match '^(d3d12|d3d12core|d3d11|dxgi|dxcore|dcomp|d2d1|vulkan-1|opengl32)\.dll$') {
        [void]$tags.Add('GraphicsRuntime')
    }

    if ($tags.Count -eq 0) {
        [void]$tags.Add('Other')
    }

    return $tags.ToArray()
}

function Test-ModuleTag {
    param(
        [string]$Tags,
        [string]$Tag
    )

    return $Tags -match ('(^|;)' + [Regex]::Escape($Tag) + '(;|$)')
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'profile-window-memory.ps1 requires Windows.'
}

if (-not [Environment]::Is64BitProcess) {
    throw 'Run profile-window-memory.ps1 from 64-bit PowerShell so 64-bit probe modules can be enumerated accurately.'
}

$probe = Get-ValidatedProbeProcess -Id $TargetProcessId
try {
    $initialStartTimeUtc = $probe.StartTime.ToUniversalTime()
    $initialMainModulePath = [System.IO.Path]::GetFullPath($probe.MainModule.FileName)
}
finally {
    $probe.Dispose()
}

if (-not ([System.Management.Automation.PSTypeName]'JaliumWindowMemoryProfiler.NativeProfiler').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JaliumWindowMemoryProfiler
{
    public sealed class ModuleCapture
    {
        public string Name;
        public string Path;
        public ulong BaseAddress;
        public ulong EndAddress;
        public ulong ImageSizeBytes;
        public long WorkingSetPages;
        public long SharedWorkingSetPages;
        public long PrivateWorkingSetPages;
        public long ImageWorkingSetPages;
        public long NonImageWorkingSetPages;
    }

    public sealed class MemoryTypeCapture
    {
        public string Name;
        public uint NativeValue;
        public long WorkingSetPages;
        public long SharedWorkingSetPages;
        public long PrivateWorkingSetPages;
    }

    public sealed class ImageSectionCapture
    {
        public int Ordinal;
        public string Kind;
        public string Name;
        public ulong RelativeVirtualAddress;
        public ulong EndRelativeVirtualAddress;
        public ulong MappedVirtualSizeBytes;
        public ulong PeVirtualSizeBytes;
        public ulong RawDataSizeBytes;
        public uint Characteristics;
        public bool ContainsCode;
        public bool ContainsInitializedData;
        public bool ContainsUninitializedData;
        public bool IsExecutable;
        public bool IsReadable;
        public bool IsWritable;
        public bool IsDiscardable;
        public long WorkingSetPages;
        public long SharedWorkingSetPages;
        public long PrivateWorkingSetPages;
    }

    public sealed class CaptureResult
    {
        public int ProcessId;
        public int PageSizeBytes;
        public string CaptureStartedUtc;
        public string CaptureCompletedUtc;
        public int QueryWorkingSetAttempts;
        public int VirtualRegionCount;
        public long WorkingSetPages;
        public long SharedWorkingSetPages;
        public long PrivateWorkingSetPages;
        public long ModuleAttributedPages;
        public long ModuleAttributedImagePages;
        public long UnattributedImagePages;
        public ulong AdjacentWorkingSetBeforeBytes;
        public ulong AdjacentWorkingSetAfterBytes;
        public ulong AdjacentPrivateUsageBeforeBytes;
        public ulong AdjacentPrivateUsageAfterBytes;
        public string MainImagePath;
        public ulong MainImageBaseAddress;
        public ulong MainImageSizeBytes;
        public ulong MainImagePeSizeOfImageBytes;
        public ulong MainImageSizeOfHeadersBytes;
        public uint MainImageSectionAlignmentBytes;
        public long MainImageWorkingSetPages;
        public long MainImageSharedWorkingSetPages;
        public long MainImagePrivateWorkingSetPages;
        public long MainImageSectionAttributedPages;
        public long MainImageUnattributedPages;
        public long MainImageUnattributedSharedPages;
        public long MainImageUnattributedPrivatePages;
        public ModuleCapture[] Modules;
        public MemoryTypeCapture[] MemoryTypes;
        public ImageSectionCapture[] MainImageSections;
    }

    public static class NativeProfiler
    {
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint MEM_PRIVATE = 0x00020000;
        private const uint MEM_MAPPED = 0x00040000;
        private const uint MEM_IMAGE = 0x01000000;
        private const uint IMAGE_SCN_CNT_CODE = 0x00000020;
        private const uint IMAGE_SCN_CNT_INITIALIZED_DATA = 0x00000040;
        private const uint IMAGE_SCN_CNT_UNINITIALIZED_DATA = 0x00000080;
        private const uint IMAGE_SCN_MEM_DISCARDABLE = 0x02000000;
        private const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
        private const uint IMAGE_SCN_MEM_READ = 0x40000000;
        private const uint IMAGE_SCN_MEM_WRITE = 0x80000000;
        private const int ERROR_BAD_LENGTH = 24;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const int MaximumWorkingSetAttempts = 8;
        private const int MaximumWorkingSetEntries = 16 * 1024 * 1024;
        private const int MaximumPeSections = 96;

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
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
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

        private sealed class Region
        {
            public ulong BaseAddress;
            public ulong EndAddress;
            public uint Type;
        }

        private sealed class WorkingSetPage
        {
            public ulong Address;
            public bool Shared;
        }

        private sealed class PeImageLayout
        {
            public ulong SizeOfImageBytes;
            public ulong SizeOfHeadersBytes;
            public uint SectionAlignmentBytes;
            public List<ImageSectionCapture> Ranges;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern void GetNativeSystemInfo(out SYSTEM_INFO systemInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(
            IntPtr process,
            IntPtr address,
            out MEMORY_BASIC_INFORMATION information,
            UIntPtr informationLength);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryWorkingSet(IntPtr process, IntPtr buffer, int bufferLength);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr process,
            out PROCESS_MEMORY_COUNTERS_EX counters,
            uint size);

        public static CaptureResult Capture(int processId)
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
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

            try
            {
                SYSTEM_INFO systemInfo;
                GetNativeSystemInfo(out systemInfo);
                if (systemInfo.PageSize == 0 || systemInfo.PageSize > Int32.MaxValue)
                {
                    throw new InvalidOperationException(
                        "GetNativeSystemInfo returned an invalid page size: " + systemInfo.PageSize + ".");
                }

                ModuleCapture mainModule;
                List<ModuleCapture> modules = ReadModules(processId, out mainModule);
                PeImageLayout mainImageLayout = ReadPeImageLayout(mainModule);
                List<Region> regions = ReadRegions(processHandle, systemInfo);

                PROCESS_MEMORY_COUNTERS_EX before = ReadMemoryCounters(
                    processHandle,
                    processId,
                    "before QueryWorkingSet");
                int queryAttempts;
                WorkingSetPage[] pages = ReadWorkingSet(
                    processHandle,
                    checked((int)systemInfo.PageSize),
                    ToUInt64(before.WorkingSetSize),
                    out queryAttempts);
                PROCESS_MEMORY_COUNTERS_EX after = ReadMemoryCounters(
                    processHandle,
                    processId,
                    "after QueryWorkingSet");

                MemoryTypeCapture privateBucket = NewBucket("MEM_PRIVATE", MEM_PRIVATE);
                MemoryTypeCapture mappedBucket = NewBucket("MEM_MAPPED", MEM_MAPPED);
                MemoryTypeCapture imageBucket = NewBucket("MEM_IMAGE", MEM_IMAGE);
                MemoryTypeCapture otherBucket = NewBucket("OTHER", 0);

                long sharedPages = 0;
                long privatePages = 0;
                long moduleAttributedPages = 0;
                long moduleAttributedImagePages = 0;
                long mainImageSectionAttributedPages = 0;
                long mainImageUnattributedPages = 0;
                long mainImageUnattributedSharedPages = 0;
                long mainImageUnattributedPrivatePages = 0;
                int regionIndex = 0;
                int moduleIndex = 0;

                Array.Sort(pages, delegate(WorkingSetPage left, WorkingSetPage right)
                {
                    return left.Address.CompareTo(right.Address);
                });

                for (int pageIndex = 0; pageIndex < pages.Length; pageIndex++)
                {
                    WorkingSetPage page = pages[pageIndex];

                    while (regionIndex < regions.Count && page.Address >= regions[regionIndex].EndAddress)
                    {
                        regionIndex++;
                    }

                    uint memoryType = 0;
                    if (regionIndex < regions.Count &&
                        page.Address >= regions[regionIndex].BaseAddress &&
                        page.Address < regions[regionIndex].EndAddress)
                    {
                        memoryType = regions[regionIndex].Type;
                    }

                    MemoryTypeCapture bucket;
                    if (memoryType == MEM_PRIVATE)
                    {
                        bucket = privateBucket;
                    }
                    else if (memoryType == MEM_MAPPED)
                    {
                        bucket = mappedBucket;
                    }
                    else if (memoryType == MEM_IMAGE)
                    {
                        bucket = imageBucket;
                    }
                    else
                    {
                        bucket = otherBucket;
                    }

                    bucket.WorkingSetPages++;
                    if (page.Shared)
                    {
                        bucket.SharedWorkingSetPages++;
                        sharedPages++;
                    }
                    else
                    {
                        bucket.PrivateWorkingSetPages++;
                        privatePages++;
                    }

                    while (moduleIndex < modules.Count && page.Address >= modules[moduleIndex].EndAddress)
                    {
                        moduleIndex++;
                    }

                    if (moduleIndex < modules.Count &&
                        page.Address >= modules[moduleIndex].BaseAddress &&
                        page.Address < modules[moduleIndex].EndAddress)
                    {
                        ModuleCapture module = modules[moduleIndex];
                        module.WorkingSetPages++;
                        moduleAttributedPages++;
                        if (page.Shared)
                        {
                            module.SharedWorkingSetPages++;
                        }
                        else
                        {
                            module.PrivateWorkingSetPages++;
                        }

                        if (memoryType == MEM_IMAGE)
                        {
                            module.ImageWorkingSetPages++;
                            moduleAttributedImagePages++;
                        }
                        else
                        {
                            module.NonImageWorkingSetPages++;
                        }

                        if (Object.ReferenceEquals(module, mainModule))
                        {
                            ulong relativeAddress = page.Address - mainModule.BaseAddress;
                            ImageSectionCapture section = FindImageSection(
                                mainImageLayout.Ranges,
                                relativeAddress);
                            if (section == null)
                            {
                                mainImageUnattributedPages++;
                                if (page.Shared)
                                {
                                    mainImageUnattributedSharedPages++;
                                }
                                else
                                {
                                    mainImageUnattributedPrivatePages++;
                                }
                            }
                            else
                            {
                                section.WorkingSetPages++;
                                mainImageSectionAttributedPages++;
                                if (page.Shared)
                                {
                                    section.SharedWorkingSetPages++;
                                }
                                else
                                {
                                    section.PrivateWorkingSetPages++;
                                }
                            }
                        }
                    }
                }

                long memoryTypeTotal =
                    privateBucket.WorkingSetPages +
                    mappedBucket.WorkingSetPages +
                    imageBucket.WorkingSetPages +
                    otherBucket.WorkingSetPages;
                if (memoryTypeTotal != pages.LongLength || sharedPages + privatePages != pages.LongLength)
                {
                    throw new InvalidOperationException(
                        "Working-set accounting did not reconcile with QueryWorkingSet: pages=" +
                        pages.LongLength + ", memoryTypes=" + memoryTypeTotal +
                        ", sharedPlusPrivate=" + (sharedPages + privatePages) + ".");
                }

                if (mainImageSectionAttributedPages + mainImageUnattributedPages !=
                    mainModule.WorkingSetPages)
                {
                    throw new InvalidOperationException(
                        "Main-image PE section accounting did not reconcile: modulePages=" +
                        mainModule.WorkingSetPages + ", sectionPages=" +
                        mainImageSectionAttributedPages + ", unattributedPages=" +
                        mainImageUnattributedPages + ".");
                }

                return new CaptureResult
                {
                    ProcessId = processId,
                    PageSizeBytes = checked((int)systemInfo.PageSize),
                    CaptureStartedUtc = started.ToString("o"),
                    CaptureCompletedUtc = DateTimeOffset.UtcNow.ToString("o"),
                    QueryWorkingSetAttempts = queryAttempts,
                    VirtualRegionCount = regions.Count,
                    WorkingSetPages = pages.LongLength,
                    SharedWorkingSetPages = sharedPages,
                    PrivateWorkingSetPages = privatePages,
                    ModuleAttributedPages = moduleAttributedPages,
                    ModuleAttributedImagePages = moduleAttributedImagePages,
                    UnattributedImagePages = imageBucket.WorkingSetPages - moduleAttributedImagePages,
                    AdjacentWorkingSetBeforeBytes = ToUInt64(before.WorkingSetSize),
                    AdjacentWorkingSetAfterBytes = ToUInt64(after.WorkingSetSize),
                    AdjacentPrivateUsageBeforeBytes = ToUInt64(before.PrivateUsage),
                    AdjacentPrivateUsageAfterBytes = ToUInt64(after.PrivateUsage),
                    MainImagePath = mainModule.Path,
                    MainImageBaseAddress = mainModule.BaseAddress,
                    MainImageSizeBytes = mainModule.ImageSizeBytes,
                    MainImagePeSizeOfImageBytes = mainImageLayout.SizeOfImageBytes,
                    MainImageSizeOfHeadersBytes = mainImageLayout.SizeOfHeadersBytes,
                    MainImageSectionAlignmentBytes = mainImageLayout.SectionAlignmentBytes,
                    MainImageWorkingSetPages = mainModule.WorkingSetPages,
                    MainImageSharedWorkingSetPages = mainModule.SharedWorkingSetPages,
                    MainImagePrivateWorkingSetPages = mainModule.PrivateWorkingSetPages,
                    MainImageSectionAttributedPages = mainImageSectionAttributedPages,
                    MainImageUnattributedPages = mainImageUnattributedPages,
                    MainImageUnattributedSharedPages = mainImageUnattributedSharedPages,
                    MainImageUnattributedPrivatePages = mainImageUnattributedPrivatePages,
                    Modules = modules.ToArray(),
                    MemoryTypes = new[] { privateBucket, mappedBucket, imageBucket, otherBucket },
                    MainImageSections = mainImageLayout.Ranges.ToArray()
                };
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        private static List<ModuleCapture> ReadModules(
            int processId,
            out ModuleCapture mainModule)
        {
            Process process = null;
            mainModule = null;
            try
            {
                process = Process.GetProcessById(processId);
                ProcessModule processMainModule = process.MainModule;
                if (processMainModule == null)
                {
                    throw new InvalidOperationException("The target process did not expose a main module.");
                }

                ulong mainBaseAddress = ToUInt64(processMainModule.BaseAddress);
                string mainModulePath = Path.GetFullPath(processMainModule.FileName);
                var modules = new List<ModuleCapture>();
                foreach (ProcessModule processModule in process.Modules)
                {
                    ulong baseAddress = ToUInt64(processModule.BaseAddress);
                    if (processModule.ModuleMemorySize < 0)
                    {
                        throw new InvalidOperationException(
                            "Module '" + processModule.ModuleName + "' reported a negative image size.");
                    }

                    ulong imageSize = checked((ulong)processModule.ModuleMemorySize);
                    ulong endAddress = CheckedAdd(baseAddress, imageSize, "module " + processModule.ModuleName);
                    var module = new ModuleCapture
                    {
                        Name = processModule.ModuleName,
                        Path = processModule.FileName,
                        BaseAddress = baseAddress,
                        EndAddress = endAddress,
                        ImageSizeBytes = imageSize
                    };
                    modules.Add(module);

                    if (baseAddress == mainBaseAddress &&
                        String.Equals(
                            Path.GetFullPath(processModule.FileName),
                            mainModulePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        mainModule = module;
                    }
                }

                if (mainModule == null)
                {
                    throw new InvalidOperationException(
                        "The main executable was not present in the module enumeration: '" +
                        mainModulePath + "'.");
                }

                modules.Sort(delegate(ModuleCapture left, ModuleCapture right)
                {
                    int baseComparison = left.BaseAddress.CompareTo(right.BaseAddress);
                    return baseComparison != 0
                        ? baseComparison
                        : left.EndAddress.CompareTo(right.EndAddress);
                });
                return modules;
            }
            catch (Win32Exception exception)
            {
                throw new Win32Exception(
                    exception.NativeErrorCode,
                    "Module enumeration failed for PID " + processId + ": " + exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "Module enumeration failed because PID " + processId + " exited or changed: " +
                    exception.Message,
                    exception);
            }
            finally
            {
                if (process != null)
                {
                    process.Dispose();
                }
            }
        }

        private static PeImageLayout ReadPeImageLayout(ModuleCapture mainModule)
        {
            using (var stream = new FileStream(
                mainModule.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new BinaryReader(stream, Encoding.ASCII))
            {
                EnsureFileRange(stream, 0, 64, "DOS header");
                if (reader.ReadUInt16() != 0x5a4d)
                {
                    throw new InvalidDataException(
                        "Main executable does not begin with an MZ header: '" + mainModule.Path + "'.");
                }

                stream.Position = 0x3c;
                int peHeaderOffset = reader.ReadInt32();
                if (peHeaderOffset < 0)
                {
                    throw new InvalidDataException(
                        "Main executable has a negative PE header offset: '" + mainModule.Path + "'.");
                }

                EnsureFileRange(stream, peHeaderOffset, 24, "PE signature and COFF header");
                stream.Position = peHeaderOffset;
                if (reader.ReadUInt32() != 0x00004550)
                {
                    throw new InvalidDataException(
                        "Main executable has an invalid PE signature: '" + mainModule.Path + "'.");
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
                    throw new InvalidDataException(
                        "Main executable reports an unsupported PE section count of " +
                        sectionCount + ": '" + mainModule.Path + "'.");
                }

                long optionalHeaderOffset = stream.Position;
                if (optionalHeaderSize < 64)
                {
                    throw new InvalidDataException(
                        "Main executable optional header is too small: " + optionalHeaderSize +
                        " bytes in '" + mainModule.Path + "'.");
                }

                EnsureFileRange(
                    stream,
                    optionalHeaderOffset,
                    optionalHeaderSize,
                    "PE optional header");
                stream.Position = optionalHeaderOffset;
                ushort optionalMagic = reader.ReadUInt16();
                if (optionalMagic != 0x10b && optionalMagic != 0x20b)
                {
                    throw new InvalidDataException(
                        "Main executable has unsupported PE optional-header magic 0x" +
                        optionalMagic.ToString("x") + ": '" + mainModule.Path + "'.");
                }

                stream.Position = optionalHeaderOffset + 32;
                uint sectionAlignment = reader.ReadUInt32();
                stream.Position = optionalHeaderOffset + 56;
                uint sizeOfImage = reader.ReadUInt32();
                uint sizeOfHeaders = reader.ReadUInt32();
                if (sectionAlignment == 0 || sizeOfImage == 0 || sizeOfHeaders == 0)
                {
                    throw new InvalidDataException(
                        "Main executable has invalid image sizing fields: SectionAlignment=" +
                        sectionAlignment + ", SizeOfImage=" + sizeOfImage +
                        ", SizeOfHeaders=" + sizeOfHeaders + ".");
                }

                long sectionHeadersOffset = checked(optionalHeaderOffset + optionalHeaderSize);
                long sectionHeadersSize = checked((long)sectionCount * 40L);
                EnsureFileRange(stream, sectionHeadersOffset, sectionHeadersSize, "PE section table");

                ulong imageLimit = Math.Min(mainModule.ImageSizeBytes, (ulong)sizeOfImage);
                var sections = new List<ImageSectionCapture>(sectionCount);
                for (int ordinal = 0; ordinal < sectionCount; ordinal++)
                {
                    stream.Position = checked(sectionHeadersOffset + (ordinal * 40L));
                    byte[] nameBytes = reader.ReadBytes(8);
                    if (nameBytes.Length != 8)
                    {
                        throw new EndOfStreamException(
                            "Unexpected end of file while reading PE section name " + ordinal + ".");
                    }

                    int nameLength = Array.IndexOf(nameBytes, (byte)0);
                    if (nameLength < 0)
                    {
                        nameLength = nameBytes.Length;
                    }

                    string name = Encoding.ASCII.GetString(nameBytes, 0, nameLength);
                    if (String.IsNullOrWhiteSpace(name))
                    {
                        name = "<section-" + ordinal + ">";
                    }

                    uint virtualSize = reader.ReadUInt32();
                    uint virtualAddress = reader.ReadUInt32();
                    uint rawDataSize = reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    reader.ReadUInt16();
                    uint characteristics = reader.ReadUInt32();

                    ulong span = Math.Max((ulong)virtualSize, (ulong)rawDataSize);
                    ulong endAddress = CheckedAdd(
                        virtualAddress,
                        span,
                        "PE section " + name);
                    if (endAddress > imageLimit)
                    {
                        endAddress = imageLimit;
                    }
                    if ((ulong)virtualAddress > imageLimit)
                    {
                        endAddress = virtualAddress;
                    }

                    sections.Add(new ImageSectionCapture
                    {
                        Ordinal = ordinal,
                        Kind = "Section",
                        Name = name,
                        RelativeVirtualAddress = virtualAddress,
                        EndRelativeVirtualAddress = endAddress,
                        MappedVirtualSizeBytes = endAddress > virtualAddress
                            ? endAddress - virtualAddress
                            : 0,
                        PeVirtualSizeBytes = virtualSize,
                        RawDataSizeBytes = rawDataSize,
                        Characteristics = characteristics,
                        ContainsCode = (characteristics & IMAGE_SCN_CNT_CODE) != 0,
                        ContainsInitializedData =
                            (characteristics & IMAGE_SCN_CNT_INITIALIZED_DATA) != 0,
                        ContainsUninitializedData =
                            (characteristics & IMAGE_SCN_CNT_UNINITIALIZED_DATA) != 0,
                        IsExecutable = (characteristics & IMAGE_SCN_MEM_EXECUTE) != 0,
                        IsReadable = (characteristics & IMAGE_SCN_MEM_READ) != 0,
                        IsWritable = (characteristics & IMAGE_SCN_MEM_WRITE) != 0,
                        IsDiscardable = (characteristics & IMAGE_SCN_MEM_DISCARDABLE) != 0
                    });
                }

                sections.Sort(delegate(ImageSectionCapture left, ImageSectionCapture right)
                {
                    int addressComparison = left.RelativeVirtualAddress.CompareTo(
                        right.RelativeVirtualAddress);
                    return addressComparison != 0
                        ? addressComparison
                        : left.Ordinal.CompareTo(right.Ordinal);
                });

                for (int index = 0; index + 1 < sections.Count; index++)
                {
                    ulong nextStart = sections[index + 1].RelativeVirtualAddress;
                    if (sections[index].EndRelativeVirtualAddress > nextStart)
                    {
                        sections[index].EndRelativeVirtualAddress = nextStart;
                        sections[index].MappedVirtualSizeBytes =
                            nextStart > sections[index].RelativeVirtualAddress
                                ? nextStart - sections[index].RelativeVirtualAddress
                                : 0;
                    }
                }

                ulong firstSectionAddress = sections.Count == 0
                    ? imageLimit
                    : sections[0].RelativeVirtualAddress;
                ulong headerEnd = Math.Min(
                    Math.Min((ulong)sizeOfHeaders, imageLimit),
                    firstSectionAddress);
                sections.Insert(0, new ImageSectionCapture
                {
                    Ordinal = -1,
                    Kind = "Headers",
                    Name = "<headers>",
                    RelativeVirtualAddress = 0,
                    EndRelativeVirtualAddress = headerEnd,
                    MappedVirtualSizeBytes = headerEnd,
                    PeVirtualSizeBytes = sizeOfHeaders,
                    RawDataSizeBytes = sizeOfHeaders,
                    IsReadable = true
                });

                return new PeImageLayout
                {
                    SizeOfImageBytes = sizeOfImage,
                    SizeOfHeadersBytes = sizeOfHeaders,
                    SectionAlignmentBytes = sectionAlignment,
                    Ranges = sections
                };
            }
        }

        private static ImageSectionCapture FindImageSection(
            List<ImageSectionCapture> ranges,
            ulong relativeAddress)
        {
            // A normal PE has only a handful of sections. This bounded in-memory
            // scan avoids extra target-process calls and keeps gap handling exact.
            for (int index = 0; index < ranges.Count; index++)
            {
                ImageSectionCapture range = ranges[index];
                if (relativeAddress >= range.RelativeVirtualAddress &&
                    relativeAddress < range.EndRelativeVirtualAddress)
                {
                    return range;
                }
            }

            return null;
        }

        private static void EnsureFileRange(
            FileStream stream,
            long offset,
            long size,
            string description)
        {
            if (offset < 0 || size < 0 || offset > stream.Length || size > stream.Length - offset)
            {
                throw new InvalidDataException(
                    "Main executable is truncated while reading " + description +
                    ": offset=" + offset + ", size=" + size +
                    ", fileLength=" + stream.Length + ".");
            }
        }

        private static List<Region> ReadRegions(IntPtr process, SYSTEM_INFO systemInfo)
        {
            ulong current = ToUInt64(systemInfo.MinimumApplicationAddress);
            ulong maximum = ToUInt64(systemInfo.MaximumApplicationAddress);
            ulong maximumExclusive = maximum == UInt64.MaxValue ? UInt64.MaxValue : maximum + 1;
            UIntPtr informationLength = new UIntPtr((uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)));
            var regions = new List<Region>();

            while (current < maximumExclusive)
            {
                MEMORY_BASIC_INFORMATION information;
                UIntPtr result = VirtualQueryEx(
                    process,
                    new IntPtr(unchecked((long)current)),
                    out information,
                    informationLength);
                if (result == UIntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(
                        error,
                        "VirtualQueryEx failed at address 0x" + current.ToString("x") + ".");
                }

                ulong baseAddress = ToUInt64(information.BaseAddress);
                ulong regionSize = ToUInt64(information.RegionSize);
                if (regionSize == 0)
                {
                    throw new InvalidOperationException(
                        "VirtualQueryEx returned a zero-sized region at address 0x" + current.ToString("x") + ".");
                }

                ulong endAddress = CheckedAdd(baseAddress, regionSize, "virtual-memory region");
                if (endAddress <= current)
                {
                    throw new InvalidOperationException(
                        "VirtualQueryEx did not advance at address 0x" + current.ToString("x") +
                        "; base=0x" + baseAddress.ToString("x") +
                        ", size=0x" + regionSize.ToString("x") + ".");
                }

                regions.Add(new Region
                {
                    BaseAddress = baseAddress,
                    EndAddress = endAddress,
                    Type = information.Type
                });

                current = endAddress;
            }

            return regions;
        }

        private static WorkingSetPage[] ReadWorkingSet(
            IntPtr process,
            int pageSize,
            ulong workingSetEstimateBytes,
            out int attempts)
        {
            ulong estimatedPages = workingSetEstimateBytes / (ulong)pageSize;
            ulong requestedCapacity = estimatedPages + (estimatedPages / 4) + 1024;
            int capacity = checked((int)Math.Min(
                MaximumWorkingSetEntries,
                Math.Max(16384UL, requestedCapacity)));

            for (attempts = 1; attempts <= MaximumWorkingSetAttempts; attempts++)
            {
                long bufferBytes64 = checked(((long)capacity + 1L) * IntPtr.Size);
                if (bufferBytes64 > Int32.MaxValue)
                {
                    throw new InvalidOperationException(
                        "QueryWorkingSet buffer would exceed the Win32 size limit: " + bufferBytes64 + " bytes.");
                }

                IntPtr buffer = Marshal.AllocHGlobal(new IntPtr(bufferBytes64));
                try
                {
                    Marshal.WriteIntPtr(buffer, IntPtr.Zero);
                    if (!QueryWorkingSet(process, buffer, checked((int)bufferBytes64)))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != ERROR_BAD_LENGTH && error != ERROR_INSUFFICIENT_BUFFER)
                        {
                            throw new Win32Exception(
                                error,
                                "QueryWorkingSet failed on attempt " + attempts +
                                " with capacity " + capacity + " pages.");
                        }

                        if (attempts == MaximumWorkingSetAttempts || capacity >= MaximumWorkingSetEntries)
                        {
                            throw new Win32Exception(
                                error,
                                "QueryWorkingSet still required a larger buffer after " + attempts +
                                " attempts; final capacity was " + capacity + " pages.");
                        }

                        ulong requiredEntries = ReadNativeUInt(buffer, 0);
                        if (requiredEntries > MaximumWorkingSetEntries)
                        {
                            throw new Win32Exception(
                                error,
                                "QueryWorkingSet requires " + requiredEntries +
                                " entries, above the bounded limit of " + MaximumWorkingSetEntries + ".");
                        }

                        long doubled = Math.Min((long)MaximumWorkingSetEntries, (long)capacity * 2L);
                        long requiredWithSlack = requiredEntries == 0
                            ? 0
                            : Math.Min(
                                (long)MaximumWorkingSetEntries,
                                checked((long)requiredEntries + 1024L));
                        capacity = checked((int)Math.Max(doubled, requiredWithSlack));
                        continue;
                    }

                    ulong countValue = ReadNativeUInt(buffer, 0);
                    if (countValue > (ulong)capacity || countValue > Int32.MaxValue)
                    {
                        throw new InvalidOperationException(
                            "QueryWorkingSet returned " + countValue +
                            " pages for a buffer with capacity " + capacity + ".");
                    }

                    int count = checked((int)countValue);
                    var pages = new WorkingSetPage[count];
                    for (int index = 0; index < count; index++)
                    {
                        int offset = checked((index + 1) * IntPtr.Size);
                        ulong flags = ReadNativeUInt(buffer, offset);
                        ulong address = flags & ~0xfffUL;

                        // PSAPI_WORKING_SET_BLOCK: Protection[0..4], ShareCount[5..7],
                        // Shared[8], Node[9..11], VirtualPage[12..]. A clear Shared bit
                        // is the private-working-set page test; MEM_PRIVATE is separate.
                        bool shared = (flags & 0x100UL) != 0;
                        pages[index] = new WorkingSetPage
                        {
                            Address = address,
                            Shared = shared
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

        private static PROCESS_MEMORY_COUNTERS_EX ReadMemoryCounters(
            IntPtr process,
            int processId,
            string phase)
        {
            PROCESS_MEMORY_COUNTERS_EX counters = new PROCESS_MEMORY_COUNTERS_EX();
            counters.cb = checked((uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS_EX)));
            if (!GetProcessMemoryInfo(process, out counters, counters.cb))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                    error,
                    "GetProcessMemoryInfo failed " + phase + " for PID " + processId + ".");
            }

            return counters;
        }

        private static MemoryTypeCapture NewBucket(string name, uint nativeValue)
        {
            return new MemoryTypeCapture
            {
                Name = name,
                NativeValue = nativeValue
            };
        }

        private static ulong CheckedAdd(ulong left, ulong right, string description)
        {
            if (left > UInt64.MaxValue - right)
            {
                throw new InvalidOperationException(
                    "Address overflow while calculating " + description +
                    ": base=0x" + left.ToString("x") +
                    ", size=0x" + right.ToString("x") + ".");
            }

            return left + right;
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

$sampleScheduledUtc = [DateTimeOffset]::UtcNow.AddSeconds($SampleDelaySeconds)
if ($SampleDelaySeconds -gt 0) {
    $remaining = $sampleScheduledUtc - [DateTimeOffset]::UtcNow
    if ($remaining.TotalMilliseconds -gt 0) {
        Start-Sleep -Milliseconds ([int][Math]::Ceiling($remaining.TotalMilliseconds))
    }
}

$probe = Get-ValidatedProbeProcess -Id $TargetProcessId
try {
    $currentStartTimeUtc = $probe.StartTime.ToUniversalTime()
    $currentMainModulePath = [System.IO.Path]::GetFullPath($probe.MainModule.FileName)
    if ($currentStartTimeUtc -ne $initialStartTimeUtc -or
        $currentMainModulePath -ine $initialMainModulePath) {
        throw (
            "PID $TargetProcessId was reused or changed while waiting to sample. " +
            "InitialStartUtc='$($initialStartTimeUtc.ToString('o'))'; " +
            "CurrentStartUtc='$($currentStartTimeUtc.ToString('o'))'; " +
            "InitialMainModule='$initialMainModulePath'; CurrentMainModule='$currentMainModulePath'.")
    }

    $probe.Refresh()
    $workingSet64Before = [long]$probe.WorkingSet64
    $capture = [JaliumWindowMemoryProfiler.NativeProfiler]::Capture($TargetProcessId)

    if ($probe.HasExited) {
        throw "Jalium.UI.MemoryProbe PID $TargetProcessId exited during the memory snapshot."
    }

    $probe.Refresh()
    $workingSet64After = [long]$probe.WorkingSet64
    $privateMemorySize64 = [long]$probe.PrivateMemorySize64
    $threadCount = $probe.Threads.Count
    $handleCount = $probe.HandleCount
}
catch {
    $cause = $_.Exception
    while ($null -ne $cause.InnerException) {
        $cause = $cause.InnerException
    }

    if ($cause -is [System.ComponentModel.Win32Exception]) {
        throw (
            "Read-only memory profiling failed for Jalium.UI.MemoryProbe PID $TargetProcessId. " +
            "Win32Error=$($cause.NativeErrorCode): $($cause.Message)")
    }

    if ($cause -is [System.UnauthorizedAccessException]) {
        throw "Access denied while profiling Jalium.UI.MemoryProbe PID $TargetProcessId. $($cause.Message)"
    }

    throw (
        "Read-only memory profiling failed for Jalium.UI.MemoryProbe PID $TargetProcessId. " +
        "$($cause.GetType().FullName): $($cause.Message)")
}
finally {
    $probe.Dispose()
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
    $OutputDirectory = Join-Path $repositoryRoot (
        "artifacts\empty-window-memory\profile\profile-$timestamp-pid$TargetProcessId")
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$outputNames = @(
    'profile.json',
    'summary.csv',
    'working-set-comparison.csv',
    'memory-types.csv',
    'main-image-sections.csv',
    'modules.csv',
    'selected-modules.csv',
    'top-modules.csv'
)
$existingOutputs = @($outputNames | Where-Object {
    Test-Path -LiteralPath (Join-Path $OutputDirectory $_) -PathType Leaf
})
if ($existingOutputs.Count -gt 0) {
    throw (
        "Output directory '$OutputDirectory' already contains profiler output: " +
        ($existingOutputs -join ', ') + '. Choose a fresh directory.')
}

$pageSize = [long]$capture.PageSizeBytes
$queryWorkingSetBytes = [long]$capture.WorkingSetPages * $pageSize
$beforeDifference = [Math]::Abs($queryWorkingSetBytes - $workingSet64Before)
$afterDifference = [Math]::Abs($queryWorkingSetBytes - $workingSet64After)
if ($afterDifference -le $beforeDifference) {
    $nearestWorkingSet64Bytes = $workingSet64After
    $nearestWorkingSet64Phase = 'after'
}
else {
    $nearestWorkingSet64Bytes = $workingSet64Before
    $nearestWorkingSet64Phase = 'before'
}
$workingSetDifferenceBytes = $queryWorkingSetBytes - $nearestWorkingSet64Bytes

$adjacentWorkingSetBeforeBytes = [long]$capture.AdjacentWorkingSetBeforeBytes
$adjacentWorkingSetAfterBytes = [long]$capture.AdjacentWorkingSetAfterBytes
$adjacentBeforeDifference = [Math]::Abs($queryWorkingSetBytes - $adjacentWorkingSetBeforeBytes)
$adjacentAfterDifference = [Math]::Abs($queryWorkingSetBytes - $adjacentWorkingSetAfterBytes)
if ($adjacentAfterDifference -le $adjacentBeforeDifference) {
    $comparisonCounterBytes = $adjacentWorkingSetAfterBytes
    $comparisonCounterPhase = 'after'
}
else {
    $comparisonCounterBytes = $adjacentWorkingSetBeforeBytes
    $comparisonCounterPhase = 'before'
}
$counterMinusEnumeratedBytes = $comparisonCounterBytes - $queryWorkingSetBytes
$counterMinusEnumeratedPages = $counterMinusEnumeratedBytes / [double]$pageSize
$workingSetCounterIsPageAligned = (($comparisonCounterBytes % $pageSize) -eq 0)
$workingSetDeltaIsPageAligned = (($counterMinusEnumeratedBytes % $pageSize) -eq 0)

$moduleRows = @($capture.Modules | ForEach-Object {
    $tags = @(Get-ModuleTags -Name $_.Name -Path $_.Path)
    [pscustomobject][ordered]@{
        Name = $_.Name
        Tags = $tags -join ';'
        Path = $_.Path
        BaseAddress = '0x{0:x16}' -f [uint64]$_.BaseAddress
        EndAddress = '0x{0:x16}' -f [uint64]$_.EndAddress
        ImageSizeBytes = [uint64]$_.ImageSizeBytes
        ImageSizeMiB = ConvertTo-Mebibytes ([double]$_.ImageSizeBytes)
        WorkingSetPages = [long]$_.WorkingSetPages
        WorkingSetBytes = [long]$_.WorkingSetPages * $pageSize
        WorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.WorkingSetPages * $pageSize))
        SharedWorkingSetPages = [long]$_.SharedWorkingSetPages
        SharedWorkingSetBytes = [long]$_.SharedWorkingSetPages * $pageSize
        PrivateWorkingSetPages = [long]$_.PrivateWorkingSetPages
        PrivateWorkingSetBytes = [long]$_.PrivateWorkingSetPages * $pageSize
        PrivateWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.PrivateWorkingSetPages * $pageSize))
        ImageWorkingSetPages = [long]$_.ImageWorkingSetPages
        ImageWorkingSetBytes = [long]$_.ImageWorkingSetPages * $pageSize
        ImageWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.ImageWorkingSetPages * $pageSize))
        NonImageWorkingSetPages = [long]$_.NonImageWorkingSetPages
        NonImageWorkingSetBytes = [long]$_.NonImageWorkingSetPages * $pageSize
    }
})

$mainImageSectionRows = @($capture.MainImageSections | ForEach-Object {
    [pscustomobject][ordered]@{
        Ordinal = [int]$_.Ordinal
        Kind = $_.Kind
        Name = $_.Name
        RelativeVirtualAddress = '0x{0:x16}' -f [uint64]$_.RelativeVirtualAddress
        EndRelativeVirtualAddress = '0x{0:x16}' -f [uint64]$_.EndRelativeVirtualAddress
        MappedVirtualSizeBytes = [uint64]$_.MappedVirtualSizeBytes
        MappedVirtualSizeMiB = ConvertTo-Mebibytes ([double]$_.MappedVirtualSizeBytes)
        PeVirtualSizeBytes = [uint64]$_.PeVirtualSizeBytes
        RawDataSizeBytes = [uint64]$_.RawDataSizeBytes
        Characteristics = '0x{0:x8}' -f [uint32]$_.Characteristics
        ContainsCode = [bool]$_.ContainsCode
        ContainsInitializedData = [bool]$_.ContainsInitializedData
        ContainsUninitializedData = [bool]$_.ContainsUninitializedData
        IsExecutable = [bool]$_.IsExecutable
        IsReadable = [bool]$_.IsReadable
        IsWritable = [bool]$_.IsWritable
        IsDiscardable = [bool]$_.IsDiscardable
        WorkingSetPages = [long]$_.WorkingSetPages
        WorkingSetBytes = [long]$_.WorkingSetPages * $pageSize
        WorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.WorkingSetPages * $pageSize))
        SharedWorkingSetPages = [long]$_.SharedWorkingSetPages
        SharedWorkingSetBytes = [long]$_.SharedWorkingSetPages * $pageSize
        SharedWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.SharedWorkingSetPages * $pageSize))
        PrivateWorkingSetPages = [long]$_.PrivateWorkingSetPages
        PrivateWorkingSetBytes = [long]$_.PrivateWorkingSetPages * $pageSize
        PrivateWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.PrivateWorkingSetPages * $pageSize))
    }
})

$mainImageSectionPages = ($mainImageSectionRows |
    Measure-Object -Property WorkingSetPages -Sum).Sum
if ([long]$mainImageSectionPages -ne [long]$capture.MainImageSectionAttributedPages) {
    throw (
        'Main-image section rows did not reconcile with the native capture: ' +
        "rows=$mainImageSectionPages; capture=$($capture.MainImageSectionAttributedPages).")
}

$memoryTypeRows = @($capture.MemoryTypes | ForEach-Object {
    [pscustomobject][ordered]@{
        Type = $_.Name
        NativeValue = '0x{0:x8}' -f [uint32]$_.NativeValue
        WorkingSetPages = [long]$_.WorkingSetPages
        WorkingSetBytes = [long]$_.WorkingSetPages * $pageSize
        WorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.WorkingSetPages * $pageSize))
        SharedWorkingSetPages = [long]$_.SharedWorkingSetPages
        SharedWorkingSetBytes = [long]$_.SharedWorkingSetPages * $pageSize
        SharedWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.SharedWorkingSetPages * $pageSize))
        PrivateWorkingSetPages = [long]$_.PrivateWorkingSetPages
        PrivateWorkingSetBytes = [long]$_.PrivateWorkingSetPages * $pageSize
        PrivateWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$_.PrivateWorkingSetPages * $pageSize))
    }
})

$topModules = @($moduleRows |
    Sort-Object -Property @{ Expression = 'WorkingSetBytes'; Descending = $true },
        @{ Expression = 'PrivateWorkingSetBytes'; Descending = $true },
        @{ Expression = 'Name'; Descending = $false } |
    Select-Object -First $TopModuleCount)

$rankedTopModules = @()
for ($index = 0; $index -lt $topModules.Count; $index++) {
    $module = $topModules[$index]
    $rankedTopModules += [pscustomobject][ordered]@{
        Rank = $index + 1
        Name = $module.Name
        Tags = $module.Tags
        WorkingSetBytes = $module.WorkingSetBytes
        WorkingSetMiB = $module.WorkingSetMiB
        ImageWorkingSetPages = $module.ImageWorkingSetPages
        ImageWorkingSetBytes = $module.ImageWorkingSetBytes
        ImageWorkingSetMiB = $module.ImageWorkingSetMiB
        PrivateWorkingSetPages = $module.PrivateWorkingSetPages
        PrivateWorkingSetBytes = $module.PrivateWorkingSetBytes
        PrivateWorkingSetMiB = $module.PrivateWorkingSetMiB
        SharedWorkingSetBytes = $module.SharedWorkingSetBytes
        ImageSizeBytes = $module.ImageSizeBytes
        Path = $module.Path
    }
}

$selectedModuleRows = @($moduleRows | Where-Object {
    (Test-ModuleTag -Tags $_.Tags -Tag 'JaliumNative') -or
    (Test-ModuleTag -Tags $_.Tags -Tag 'CLR') -or
    (Test-ModuleTag -Tags $_.Tags -Tag 'Font') -or
    (Test-ModuleTag -Tags $_.Tags -Tag 'GpuDriver') -or
    (Test-ModuleTag -Tags $_.Tags -Tag 'GraphicsRuntime')
})

$workingSetComparison = [pscustomobject][ordered]@{
    QueryWorkingSetPages = [long]$capture.WorkingSetPages
    QueryWorkingSetBytes = $queryWorkingSetBytes
    AdjacentCounterPhase = $comparisonCounterPhase
    AdjacentProcessWorkingSetBytes = $comparisonCounterBytes
    AdjacentProcessWorkingSetPages = $comparisonCounterBytes / [double]$pageSize
    CounterMinusEnumeratedBytes = $counterMinusEnumeratedBytes
    CounterMinusEnumeratedPages = $counterMinusEnumeratedPages
    CounterIsPageAligned = $workingSetCounterIsPageAligned
    DeltaIsPageAligned = $workingSetDeltaIsPageAligned
    ExactMatch = ($counterMinusEnumeratedBytes -eq 0)
}

$summary = [pscustomobject][ordered]@{
    SchemaVersion = 2
    MeasurementKind = 'single external read-only resident-page snapshot'
    TargetProcessId = $TargetProcessId
    TargetProcessName = 'Jalium.UI.MemoryProbe'
    TargetStartTimeUtc = $initialStartTimeUtc.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    TargetMainModulePath = $initialMainModulePath
    SampleDelaySeconds = $SampleDelaySeconds
    SampleScheduledUtc = $sampleScheduledUtc.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    CaptureStartedUtc = $capture.CaptureStartedUtc
    CaptureCompletedUtc = $capture.CaptureCompletedUtc
    PageSizeBytes = $pageSize
    QueryWorkingSetAttempts = $capture.QueryWorkingSetAttempts
    VirtualRegionCount = $capture.VirtualRegionCount
    ModuleCount = $moduleRows.Count
    ThreadCount = $threadCount
    HandleCount = $handleCount
    QueryWorkingSetPages = [long]$capture.WorkingSetPages
    QueryWorkingSetBytes = $queryWorkingSetBytes
    QueryWorkingSetMiB = ConvertTo-Mebibytes $queryWorkingSetBytes
    SharedFlagWorkingSetPages = [long]$capture.SharedWorkingSetPages
    SharedFlagWorkingSetBytes = [long]$capture.SharedWorkingSetPages * $pageSize
    PrivateFlagWorkingSetPages = [long]$capture.PrivateWorkingSetPages
    PrivateFlagWorkingSetBytes = [long]$capture.PrivateWorkingSetPages * $pageSize
    ProcessWorkingSet64BeforeBytes = $workingSet64Before
    ProcessWorkingSet64AfterBytes = $workingSet64After
    NearestProcessWorkingSet64Phase = $nearestWorkingSet64Phase
    NearestProcessWorkingSet64Bytes = $nearestWorkingSet64Bytes
    WorkingSetDifferenceBytes = $workingSetDifferenceBytes
    WorkingSetDifferencePages = [Math]::Round($workingSetDifferenceBytes / [double]$pageSize, 3)
    WorkingSetExactlyAligned = ($workingSetDifferenceBytes -eq 0)
    AdjacentNativeWorkingSetBeforeBytes = [uint64]$capture.AdjacentWorkingSetBeforeBytes
    AdjacentNativeWorkingSetAfterBytes = [uint64]$capture.AdjacentWorkingSetAfterBytes
    AdjacentCounterPhase = $comparisonCounterPhase
    AdjacentCounterBytes = $comparisonCounterBytes
    AdjacentCounterMinusQueryWorkingSetBytes = $counterMinusEnumeratedBytes
    AdjacentCounterMinusQueryWorkingSetPages = $counterMinusEnumeratedPages
    AdjacentCounterExactMatch = ($counterMinusEnumeratedBytes -eq 0)
    PrivateMemorySize64Bytes = $privateMemorySize64
    PrivateMemorySize64MiB = ConvertTo-Mebibytes $privateMemorySize64
    AdjacentNativePrivateUsageBeforeBytes = [uint64]$capture.AdjacentPrivateUsageBeforeBytes
    AdjacentNativePrivateUsageAfterBytes = [uint64]$capture.AdjacentPrivateUsageAfterBytes
    MainImagePath = $capture.MainImagePath
    MainImageBaseAddress = '0x{0:x16}' -f [uint64]$capture.MainImageBaseAddress
    MainImageSizeBytes = [uint64]$capture.MainImageSizeBytes
    MainImagePeSizeOfImageBytes = [uint64]$capture.MainImagePeSizeOfImageBytes
    MainImageSizeOfHeadersBytes = [uint64]$capture.MainImageSizeOfHeadersBytes
    MainImageSectionAlignmentBytes = [uint32]$capture.MainImageSectionAlignmentBytes
    MainImageWorkingSetPages = [long]$capture.MainImageWorkingSetPages
    MainImageWorkingSetBytes = [long]$capture.MainImageWorkingSetPages * $pageSize
    MainImageWorkingSetMiB = ConvertTo-Mebibytes ([double]([long]$capture.MainImageWorkingSetPages * $pageSize))
    MainImageSharedWorkingSetPages = [long]$capture.MainImageSharedWorkingSetPages
    MainImageSharedWorkingSetBytes = [long]$capture.MainImageSharedWorkingSetPages * $pageSize
    MainImagePrivateWorkingSetPages = [long]$capture.MainImagePrivateWorkingSetPages
    MainImagePrivateWorkingSetBytes = [long]$capture.MainImagePrivateWorkingSetPages * $pageSize
    MainImageSectionAttributedPages = [long]$capture.MainImageSectionAttributedPages
    MainImageUnattributedPages = [long]$capture.MainImageUnattributedPages
    MainImageUnattributedSharedPages = [long]$capture.MainImageUnattributedSharedPages
    MainImageUnattributedPrivatePages = [long]$capture.MainImageUnattributedPrivatePages
    ModuleAttributedPages = [long]$capture.ModuleAttributedPages
    ModuleAttributedBytes = [long]$capture.ModuleAttributedPages * $pageSize
    ModuleAttributedImagePages = [long]$capture.ModuleAttributedImagePages
    UnattributedImagePages = [long]$capture.UnattributedImagePages
}

$profile = [ordered]@{
    Summary = $summary
    CountingNotes = [ordered]@{
        SharedPageRule = 'PSAPI_WORKING_SET_BLOCK.Shared bit 8 set; PrivateFlagWorkingSet is the complement.'
        MemoryTypeRule = 'MEM_PRIVATE, MEM_MAPPED, and MEM_IMAGE come from the containing VirtualQueryEx region and are independent of the shared-page flag.'
        ModuleRule = 'A resident page is charged to a module when its virtual address is inside that module image range.'
        PeSectionRule = 'Main-executable resident pages are charged by RVA to PE headers or section virtual ranges using max(VirtualSize, SizeOfRawData), clipped at the next section and SizeOfImage. Gap pages remain explicitly unattributed.'
        SnapshotRule = 'The target is not suspended. QueryWorkingSet and process counters are not read atomically; their signed difference is retained and is never attributed to a module or memory type.'
    }
    WorkingSetComparison = $workingSetComparison
    MemoryTypes = $memoryTypeRows
    MainImage = [ordered]@{
        Path = $capture.MainImagePath
        BaseAddress = '0x{0:x16}' -f [uint64]$capture.MainImageBaseAddress
        ModuleImageSizeBytes = [uint64]$capture.MainImageSizeBytes
        PeSizeOfImageBytes = [uint64]$capture.MainImagePeSizeOfImageBytes
        SizeOfHeadersBytes = [uint64]$capture.MainImageSizeOfHeadersBytes
        SectionAlignmentBytes = [uint32]$capture.MainImageSectionAlignmentBytes
        WorkingSetPages = [long]$capture.MainImageWorkingSetPages
        SharedWorkingSetPages = [long]$capture.MainImageSharedWorkingSetPages
        PrivateWorkingSetPages = [long]$capture.MainImagePrivateWorkingSetPages
        SectionAttributedPages = [long]$capture.MainImageSectionAttributedPages
        UnattributedPages = [long]$capture.MainImageUnattributedPages
        UnattributedSharedPages = [long]$capture.MainImageUnattributedSharedPages
        UnattributedPrivatePages = [long]$capture.MainImageUnattributedPrivatePages
        Sections = $mainImageSectionRows
    }
    RelevantModules = [ordered]@{
        JaliumNative = @($moduleRows | Where-Object { Test-ModuleTag -Tags $_.Tags -Tag 'JaliumNative' })
        CLR = @($moduleRows | Where-Object { Test-ModuleTag -Tags $_.Tags -Tag 'CLR' })
        Font = @($moduleRows | Where-Object { Test-ModuleTag -Tags $_.Tags -Tag 'Font' })
        GpuDriver = @($moduleRows | Where-Object { Test-ModuleTag -Tags $_.Tags -Tag 'GpuDriver' })
        GraphicsRuntime = @($moduleRows | Where-Object { Test-ModuleTag -Tags $_.Tags -Tag 'GraphicsRuntime' })
    }
    TopModules = $rankedTopModules
    Modules = $moduleRows
}

$profile | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'profile.json') -Encoding UTF8
$summary | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding UTF8
$workingSetComparison | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'working-set-comparison.csv') -NoTypeInformation -Encoding UTF8
$memoryTypeRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'memory-types.csv') -NoTypeInformation -Encoding UTF8
$mainImageSectionRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'main-image-sections.csv') -NoTypeInformation -Encoding UTF8
$moduleRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'modules.csv') -NoTypeInformation -Encoding UTF8
$selectedModuleRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'selected-modules.csv') -NoTypeInformation -Encoding UTF8
$rankedTopModules | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'top-modules.csv') -NoTypeInformation -Encoding UTF8

Write-Output "Memory profile complete: $OutputDirectory"
Write-Output (
    'QueryWorkingSet={0:N3} MiB; PrivateFlag={1:N3} MiB; Process.WorkingSet64({2})={3:N3} MiB; Delta={4} bytes' -f
    (ConvertTo-Mebibytes $queryWorkingSetBytes),
    (ConvertTo-Mebibytes ([long]$capture.PrivateWorkingSetPages * $pageSize)),
    $nearestWorkingSet64Phase,
    (ConvertTo-Mebibytes $nearestWorkingSet64Bytes),
    $workingSetDifferenceBytes)
$rankedTopModules |
    Format-Table Rank, Name, Tags, WorkingSetMiB, PrivateWorkingSetMiB, Path -AutoSize
Write-Output 'Main executable PE-section working set:'
$mainImageSectionRows |
    Where-Object { $_.WorkingSetPages -gt 0 } |
    Sort-Object -Property @{ Expression = 'WorkingSetBytes'; Descending = $true },
        @{ Expression = 'Ordinal'; Descending = $false } |
    Format-Table Kind, Name, WorkingSetMiB, PrivateWorkingSetMiB, SharedWorkingSetMiB, MappedVirtualSizeMiB -AutoSize
