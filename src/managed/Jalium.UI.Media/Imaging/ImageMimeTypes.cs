using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Jalium.UI.Media.Native;

namespace Jalium.UI.Media.Imaging;

/// <summary>Uses Image I/O registration on macOS and actual tiny decode probes for optional codecs elsewhere.</summary>
internal static partial class ImageMimeTypes
{
    private static readonly ConcurrentDictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);
    internal static bool IsSupported(string mimeType) => Cache.GetOrAdd(mimeType, Probe);
    private static bool Probe(string mime)
    {
        if (OperatingSystem.IsMacOS()) return Apple(mime);
        return ProbeOptional(mime);
    }

    internal static bool ProbeOptional(string mime)
    {
        mime = mime.ToLowerInvariant() switch { "image/x-icon" => "image/vnd.microsoft.icon", "image/heif" => "image/heic", var other => other };
        if (mime is "image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/x-ms-bmp") return true;
        var encoded = mime switch
        {
            "image/webp" => "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA",
            "image/tiff" => "SUkqAAgAAAAJAAABBAABAAAAAQAAAAEBBAABAAAAAQAAAAIBAwADAAAAegAAAAMBAwABAAAAAQAAAAYBAwABAAAAAgAAABEBBAABAAAAgAAAABUBAwABAAAAAwAAABYBBAABAAAAAQAAABcBBAABAAAAAwAAAAAAAAAIAAgACAAAgAA=",
            "image/vnd.microsoft.icon" => "AAABAAEAEBAAAAEAIABoBAAAFgAAACgAAAAQAAAAIAAAAAEAIAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AIAA/wCAAP8AgAD/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==",
            "image/avif" => "AAAAIGZ0eXBhdmlmAAAAAE1pUHJhdmlmbWlhZm1pZjEAAAEhbWV0YQAAAAAAAAAhaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAAAAAAAkZGluZgAAABxkcmVmAAAAAAAAAAEAAAAMdXJsIAAAAAEAAAAOcGl0bQAAAAAAAQAAACNpaW5mAAAAAAABAAAAFWluZmUCAAAAAAEAAGF2MDEAAAAAgWlwcnAAAABgaXBjbwAAABNjb2xybmNseAACAAIABoAAAAAMY2xsaQDLAEAAAAAUaXNwZQAAAAAAAAAEAAAAAgAAAAlpcm90AAAAABBwaXhpAAAAAAMICAgAAAAMYXYxQ4EADAAAAAAZaXBtYQAAAAAAAAABAAEGgQIDBYaEAAAAHmlsb2MAAAAARAAAAQABAAAAAQAAAVEAAAAmAAAAAW1kYXQAAAAAAAAANhIACgwAAAAAhz/+BAgIGhAyFBABkgAIIIIgAGjTBs8l/iDbAdeA",
            "image/heic" => "AAAAJGZ0eXBoZWljAAAAAG1pZjFNaVBybWlhZk1pSEJoZWljAAABh21ldGEAAAAAAAAAIWhkbHIAAAAAAAAAAHBpY3QAAAAAAAAAAAAAAAAAAAAAJGRpbmYAAAAcZHJlZgAAAAAAAAABAAAADHVybCAAAAABAAAADnBpdG0AAAAAAAEAAAAjaWluZgAAAAAAAQAAABVpbmZlAgAAAAABAABodmMxAAAAAOdpcHJwAAAAxmlwY28AAAATY29scm5jbHgAAgACAAaAAAAADGNsbGkAywBAAAAAFGlzcGUAAAAAAAAABAAAAAIAAAAJaXJvdAAAAAAQcGl4aQAAAAADCAgIAAAAcmh2Y0MBA3AAAACwAAAAAAAe8AD8/fj4AAALA6AAAQAXQAEMAf//A3AAAAMAsAAAAwAAAwAecCShAAEAJEIBAQNwAAADALAAAAMAAAMAHqAQIEHB/BBiHkkWVTcCAgYAgKIAAQAJRAHAYXJCEBTJAAAAGWlwbWEAAAAAAAAAAQABBoECAwWGhAAAAB5pbG9jAAAAAEQAAAEAAQAAAAEAAAG7AAAAKgAAAAFtZGF0AAAAAAAAADoAAAAmKAGv4CqQ5n//GZX//s7///BRd/RNWXKhE+jGs++D52/xTxsPkUw=",
            _ => null,
        };
        if (encoded is null) return false;
        try
        {
            var image = new NativeImageDecoder().Decode(Convert.FromBase64String(encoded));
            return image.Width > 0 && image.Height > 0;
        }
        catch (Exception error) when (error is NativeMediaException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    private static unsafe bool Apple(string mime)
    {
        nint tag = 0, mimeClass = 0, type = 0, supported = 0;
        try
        {
            fixed (char* text = mime) tag = CFStringCreateWithCharacters(0, text, mime.Length);
            const string className = "public.mime-type";
            fixed (char* text = className) mimeClass = CFStringCreateWithCharacters(0, text, className.Length);
            if (tag == 0 || mimeClass == 0 || (type = UTTypeCreatePreferredIdentifierForTag(mimeClass, tag, 0)) == 0) return false;
            supported = CGImageSourceCopyTypeIdentifiers();
            return supported != 0 && CFArrayContainsValue(supported, new(0, CFArrayGetCount(supported)), type) != 0;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
        finally { foreach (var value in new[] { supported, type, mimeClass, tag }) if (value != 0) CFRelease(value); }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Range(nint Location, nint Length);
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [LibraryImport(Foundation)] private static unsafe partial nint CFStringCreateWithCharacters(nint allocator, char* text, nint count);
    [LibraryImport(Foundation)] private static partial void CFRelease(nint value);
    [LibraryImport(Foundation)] private static partial nint CFArrayGetCount(nint array);
    [LibraryImport(Foundation)] private static partial byte CFArrayContainsValue(nint array, Range range, nint value);
    [LibraryImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")] private static partial nint CGImageSourceCopyTypeIdentifiers();
    [LibraryImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")] private static partial nint UTTypeCreatePreferredIdentifierForTag(nint tagClass, nint tag, nint conformsTo);
}
