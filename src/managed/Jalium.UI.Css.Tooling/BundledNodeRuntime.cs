using System.IO.Compression;
using System.Security.Cryptography;

namespace Jalium.UI.Tooling;

/// <summary>Extracts the packaged host runtime without downloading build tools.</summary>
internal static class BundledNodeRuntime
{
    internal static string Resolve(string executable, string? cacheRoot = null)
    {
        if (File.Exists(executable)) return executable;
        string archive = executable + ".br";
        string checksum = executable + ".sha256";
        if (!File.Exists(archive) || !File.Exists(checksum)) return executable;
        string expected = File.ReadAllText(checksum).Trim();
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid bundled Node SHA-256 checksum.");
        cacheRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jalium", "css-tooling");
        string directory = Path.Combine(cacheRoot, CssTooling.HostRid, expected.ToLowerInvariant());
        Directory.CreateDirectory(directory);
        string cached = Path.Combine(directory, Path.GetFileName(executable));
        if (Matches(cached, expected)) return cached;
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = File.OpenRead(archive))
            using (var decompressed = new BrotliStream(input, CompressionMode.Decompress))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = decompressed.Read(buffer)) > 0)
                {
                    total += count;
                    if (total > 256L * 1024 * 1024)
                        throw new InvalidDataException("Bundled Node runtime exceeds its size limit.");
                    output.Write(buffer, 0, count);
                    hash.AppendData(buffer, 0, count);
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bundled Node runtime SHA-256 mismatch.");
            }
            try { File.Move(temporary, cached, overwrite: true); }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException && Matches(cached, expected))
            {
                // A concurrent build may have extracted and started this runtime.
            }
            return cached;
        }
        finally { File.Delete(temporary); }
    }

    private static bool Matches(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
