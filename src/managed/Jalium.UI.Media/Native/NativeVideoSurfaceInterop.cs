using System.Runtime.InteropServices;

namespace Jalium.UI.Media.Native;

/// <summary>
/// P/Invoke surface for the <c>jalium_video_surface_*</c> ABI in
/// <c>jalium.native.core</c>. See <c>jalium_video_surface.h</c> for the
/// native contract.
/// </summary>
internal static partial class NativeVideoSurfaceInterop
{
    internal const string CoreLib = "jalium.native.core";

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVideoSurfacePlaneDescriptor
    {
        public int Fd;
        public uint StrideBytes;
        public uint OffsetBytes;
        public uint Reserved;
        public ulong Modifier;
        public ulong SizeBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVideoSurfaceDescriptor
    {
        public int    Kind;          // JaliumVideoSurfaceKind
        public uint   Width;
        public uint   Height;
        public ulong  Handle0;
        public ulong  Handle1;
        public uint   FormatHint;    // JaliumVideoSurfaceFormat
        public uint   Reserved;
        public uint   PlaneCount;
        public uint   DrmFourcc;
        public uint   DescriptorFlags;
        public uint   ColorSpace;
        public NativeVideoSurfacePlaneDescriptor Plane0;
        public NativeVideoSurfacePlaneDescriptor Plane1;
        public NativeVideoSurfacePlaneDescriptor Plane2;
        public NativeVideoSurfacePlaneDescriptor Plane3;
        public int    AcquireFenceFd;
        public uint   Reserved2;
        public ulong  LifetimeContext;
        public ulong  LifetimeRetainCallback;
        public ulong  LifetimeReleaseCallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVideoSurfaceDirtyRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVideoSurfaceStats
    {
        public ulong Version;
        public ulong SurfacesCreated;
        public ulong SurfacesDestroyed;
        public ulong CpuUploads;
        public ulong CpuUploadBytes;
        public ulong ExternalImports;
        public ulong ExternalImportFails;
        public long  GpuResidentBytes;

        // reserved[16]
        public ulong _r00; public ulong _r01; public ulong _r02; public ulong _r03;
        public ulong _r04; public ulong _r05; public ulong _r06; public ulong _r07;
        public ulong _r08; public ulong _r09; public ulong _r10; public ulong _r11;
        public ulong _r12; public ulong _r13; public ulong _r14; public ulong _r15;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_create")]
    internal static partial nint Create(nint ctx, uint width, uint height, uint formatHint);

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_wrap_external")]
    internal static partial nint WrapExternal(nint ctx, in NativeVideoSurfaceDescriptor desc);

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_destroy")]
    internal static partial void Destroy(nint surface);

    // ── BGRA8 staging path ───────────────────────────────────────────────

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_lock")]
    internal static unsafe partial int Lock(nint surface, out byte* outPtr, out uint outStride);

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_unlock")]
    internal static partial int Unlock(nint surface, nint dirtyRectOrNull);

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_unlock")]
    internal static partial int UnlockWithDirtyRect(nint surface, in NativeVideoSurfaceDirtyRect dirtyRect);

    // ── Accessors ────────────────────────────────────────────────────────

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_get_width")]
    internal static partial uint GetWidth(nint surface);

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_get_height")]
    internal static partial uint GetHeight(nint surface);

    [LibraryImport(CoreLib, EntryPoint = "jalium_video_surface_get_kind")]
    internal static partial int GetKind(nint surface);

    // ── Telemetry ────────────────────────────────────────────────────────

    [LibraryImport(CoreLib, EntryPoint = "jalium_query_video_surface_stats")]
    internal static partial void QueryStats(out NativeVideoSurfaceStats stats);

    [LibraryImport(CoreLib, EntryPoint = "jalium_reset_video_surface_stats")]
    internal static partial void ResetStats();
}
