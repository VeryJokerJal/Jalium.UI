using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

var version = "24.21.0";
var artifacts = new (string Rid, string Archive, string Sha256)[]
{
    ("win-x64", "node-v24.21.0-win-x64.zip", "158f7685b44de51f6c0df1d153526cbcd3e1bc739a8dfc607721cef75de9e541"),
    ("win-arm64", "node-v24.21.0-win-arm64.zip", "8779b1bde1d39f8d420e3b57aa657b39891af434d3de44a919044cec06785921"),
    ("linux-x64", "node-v24.21.0-linux-x64.tar.gz", "6e1db87ef58b8819e5d5402eff1536491b18edd8eb7bee5ef7897876e88dc5ff"),
    ("linux-arm64", "node-v24.21.0-linux-arm64.tar.gz", "724282c3b43aec998aa9527380465b45d229e021b58035f5f4f63095eabfe5d5"),
    ("osx-x64", "node-v24.21.0-darwin-x64.tar.gz", "1462cb3b3046b815cf8ea436d3da450ec1a9f11dac7e5a46b0ada5305d7e8097"),
    ("osx-arm64", "node-v24.21.0-darwin-arm64.tar.gz", "bed7eea5325e1108f32ce5228ddd6a5f0f08a499ee42aa7442aea583702f6057"),
};

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/managed/Jalium.UI.Css.Tooling/tools/css-tooling/node"));
var selected = args.Length == 0 ? artifacts : artifacts.Where(item => args.Contains(item.Rid, StringComparer.OrdinalIgnoreCase)).ToArray();
if (selected.Length == 0) throw new ArgumentException("Specify one or more supported RIDs, or omit arguments for all.");
using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
foreach (var (rid, archive, expected) in selected)
{
    var executable = Path.Combine(root, rid, rid.StartsWith("win-", StringComparison.Ordinal) ? "node.exe" : "node");
    if (File.Exists(executable))
    {
        Console.WriteLine($"{rid}: already present");
        continue;
    }
    Console.WriteLine($"{rid}: downloading Node {version}");
    var bytes = await client.GetByteArrayAsync($"https://nodejs.org/download/release/v{version}/{archive}");
    var actual = Convert.ToHexString(SHA256.HashData(bytes));
    if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"SHA-256 mismatch for {archive}: {actual}");
    Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
    await using var archiveStream = new MemoryStream(bytes);
    var entryName = rid.StartsWith("win-", StringComparison.Ordinal) ? "node.exe" : "bin/node";
    if (archive.EndsWith(".zip", StringComparison.Ordinal))
    {
        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var entry = zip.Entries.Single(item => item.FullName.EndsWith("/" + entryName, StringComparison.Ordinal));
        await using var input = entry.Open();
        await using var output = File.Create(executable);
        await input.CopyToAsync(output);
    }
    else
    {
        await using var gzip = new GZipStream(archiveStream, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        TarEntry? entry;
        var found = false;
        while ((entry = tar.GetNextEntry()) is not null)
        {
            if (!entry.Name.EndsWith("/" + entryName, StringComparison.Ordinal)) continue;
            if (entry.DataStream is null) throw new InvalidDataException($"Missing Node binary in {archive}");
            await using var output = File.Create(executable);
            await entry.DataStream.CopyToAsync(output);
            found = true;
            break;
        }
        if (!found) throw new InvalidDataException($"Missing Node binary in {archive}");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
    Console.WriteLine($"{rid}: ready");
}
