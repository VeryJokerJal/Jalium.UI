using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Jalium.UI.Controls.Helpers;

namespace Jalium.UI.Tests;

public sealed class IconHelperResourceTests
{
    [Fact]
    public void ResourceProbe_DistinguishesMissingAndPresentIconGroups()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var withoutIcon = PeResourceFixture.Create(hasIconGroup: false);
        using var withIcon = PeResourceFixture.Create(hasIconGroup: true);

        Assert.Equal(
            IconGroupResourceState.Absent,
            ProbeLoadedFixture(withoutIcon.Path));
        Assert.Equal(
            IconGroupResourceState.Present,
            ProbeLoadedFixture(withIcon.Path));

        // A missing or otherwise unavailable module handle is deliberately inconclusive.
        // Production keeps the established extraction chain for every inconclusive result.
        Assert.Equal(
            IconGroupResourceState.Unknown,
            IconHelper.ProbeIconGroupResources(0));
    }

    [Fact]
    public void ConfirmedAbsentCurrentExecutable_LeavesClassHandlesUnsetAndReturnsSystemPixels()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var processPath = Assert.IsType<string>(Environment.ProcessPath);

        Assert.True(IconHelper.ShouldSkipCurrentProcessIconExtraction(
            processPath,
            IconGroupResourceState.Absent));
        Assert.False(IconHelper.ShouldSkipCurrentProcessIconExtraction(
            processPath,
            IconGroupResourceState.Unknown));
        Assert.False(IconHelper.ShouldSkipCurrentProcessIconExtraction(
            processPath,
            IconGroupResourceState.Present));

        var externalPath = Path.Combine(
            Path.GetTempPath(),
            $"jalium-icon-external-{Guid.NewGuid():N}.exe");
        Assert.False(IconHelper.ShouldSkipCurrentProcessIconExtraction(
            externalPath,
            IconGroupResourceState.Absent));

        var handles = IconHelper.ResolveDefaultProcessIconHandles(
            processPath,
            IconGroupResourceState.Absent);
        Assert.Equal(0, handles.Large);
        Assert.Equal(0, handles.Small);

        // The title-bar bitmap contract remains intact: the optimized path goes directly
        // to the shared IDI_APPLICATION icon and never owns or destroys that HICON.
        var first = Assert.IsType<ProcessIconPixels>(
            IconHelper.ExtractProcessIconPixelsWindows(
                processPath,
                IconGroupResourceState.Absent));
        var second = Assert.IsType<ProcessIconPixels>(
            IconHelper.ExtractProcessIconPixelsWindows(
                processPath,
                IconGroupResourceState.Absent));

        AssertValidPixels(first);
        AssertValidPixels(second);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(first.Stride, second.Stride);
        Assert.Equal(first.Pixels, second.Pixels);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IconGroupResourceState ProbeLoadedFixture(string path)
    {
        const uint LoadLibraryAsDataFile = 0x00000002;
        const uint LoadLibraryAsImageResource = 0x00000020;

        nint module = LoadLibraryExW(
            path,
            0,
            LoadLibraryAsDataFile | LoadLibraryAsImageResource);
        if (module == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            return IconHelper.ProbeIconGroupResources(module);
        }
        finally
        {
            _ = FreeLibrary(module);
        }
    }

    private static void AssertValidPixels(ProcessIconPixels pixels)
    {
        Assert.True(pixels.Width > 0);
        Assert.True(pixels.Height > 0);
        Assert.Equal(pixels.Width * 4, pixels.Stride);
        Assert.True(pixels.Pixels.Length >= pixels.Stride * pixels.Height);
    }

    [Fact]
    public void SynchronousStockIconPath_RequiresConfirmedAbsenceInTheCurrentExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string path = Assert.IsType<string>(Environment.ProcessPath);
        Assert.False(IconHelper.TryGetUncustomizedProcessIconPixels(
            path, IconGroupResourceState.Unknown, out var unknown));
        Assert.Null(unknown);
        Assert.False(IconHelper.TryGetUncustomizedProcessIconPixels(
            path, IconGroupResourceState.Present, out var present));
        Assert.Null(present);
        Assert.False(IconHelper.TryGetUncustomizedProcessIconPixels(
            path + ".another-executable", IconGroupResourceState.Absent, out var external));
        Assert.Null(external);

        Assert.True(IconHelper.TryGetUncustomizedProcessIconPixels(
            path, IconGroupResourceState.Absent, out var stock));
        var actual = Assert.IsType<ProcessIconPixels>(stock);
        AssertValidPixels(actual);
        var original = Assert.IsType<ProcessIconPixels>(
            IconHelper.ExtractProcessIconPixelsWindows(path, IconGroupResourceState.Absent));
        Assert.Equal(original.Width, actual.Width);
        Assert.Equal(original.Height, actual.Height);
        Assert.Equal(original.Pixels, actual.Pixels);
    }

    private sealed class PeResourceFixture : IDisposable
    {
        private PeResourceFixture(string directory, string path)
        {
            Directory = directory;
            Path = path;
        }

        private string Directory { get; }
        public string Path { get; }

        public static PeResourceFixture Create(bool hasIconGroup)
        {
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Jalium.IconHelperResourceTests.{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            string path = System.IO.Path.Combine(
                directory,
                hasIconGroup ? "WithIconGroup.dll" : "WithoutIconGroup.dll");
            File.Copy(typeof(IconHelperResourceTests).Assembly.Location, path);

            nint update = BeginUpdateResourceW(path, deleteExistingResources: true);
            if (update == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            bool updateOpen = true;
            try
            {
                if (hasIconGroup)
                {
                    byte[] icon = CreateOnePixelIconResource();
                    byte[] group = CreateOnePixelIconGroup(icon.Length);
                    AddResource(update, resourceType: 3, resourceName: 1, icon);
                    AddResource(update, resourceType: 14, resourceName: 1, group);
                }

                if (!EndUpdateResourceW(update, discard: false))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                updateOpen = false;
                return new PeResourceFixture(directory, path);
            }
            catch
            {
                if (updateOpen)
                {
                    _ = EndUpdateResourceW(update, discard: true);
                }

                try
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
                catch
                {
                }

                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // A fixture cleanup failure does not change the resource-probe result.
            }
        }

        private static void AddResource(
            nint update,
            nint resourceType,
            nint resourceName,
            byte[] data)
        {
            if (!UpdateResourceW(
                    update,
                    resourceType,
                    resourceName,
                    language: 0,
                    data,
                    (uint)data.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static byte[] CreateOnePixelIconGroup(int iconByteLength)
        {
            var group = new byte[20];
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 1);  // type: icon
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), 1);  // image count
            group[6] = 1;                                                   // width
            group[7] = 1;                                                   // height
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(10), 1); // planes
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(12), 32);// bit depth
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(14), (uint)iconByteLength);
            BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(18), 1); // RT_ICON id
            return group;
        }

        private static byte[] CreateOnePixelIconResource()
        {
            // BITMAPINFOHEADER + one BGRA pixel + one DWORD-aligned AND-mask row.
            var icon = new byte[48];
            BinaryPrimitives.WriteUInt32LittleEndian(icon, 40);
            BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(8), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(14), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(icon.AsSpan(20), 8);
            icon[40] = 0x30;
            icon[41] = 0x60;
            icon[42] = 0x90;
            icon[43] = 0xFF;
            return icon;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint BeginUpdateResourceW(
        string fileName,
        [MarshalAs(UnmanagedType.Bool)] bool deleteExistingResources);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResourceW(
        nint update,
        nint resourceType,
        nint resourceName,
        ushort language,
        byte[] data,
        uint dataSize);

    [DllImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResourceW(
        nint update,
        [MarshalAs(UnmanagedType.Bool)] bool discard);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string path, nint file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
