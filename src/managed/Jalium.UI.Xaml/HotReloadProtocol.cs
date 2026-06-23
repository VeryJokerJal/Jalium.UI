using System.IO;
using System.Text;

namespace Jalium.UI.Markup;

/// <summary>
/// The wire protocol shared by the hot-reload pipe server (<see cref="HotReloadAgent"/>) and every
/// client — the Jalium file watcher, an IDE, or tests. One request frame per connection, one result
/// frame back. Centralising the framing here is what keeps the (previously hand-rolled, duplicated)
/// server / test encodings from drifting apart.
/// </summary>
/// <remarks>
/// Request frame:  <c>[Magic u32][Version u8] xClass filePath content</c><br/>
/// Result frame:   <c>[Updated i32][Fallback i32][Failed i32] message</c><br/>
/// where each string is <c>[ByteLength i32][UTF-8 bytes]</c>. All integers little-endian
/// (<see cref="BinaryWriter"/> default). The explicit per-string byte cap guards the server against a
/// malformed or hostile frame triggering an unbounded allocation, and the magic + version let it reject
/// an incompatible / garbage client cleanly instead of mis-parsing a stream.
/// </remarks>
public static class HotReloadProtocol
{
    /// <summary>Environment variable carrying the pipe name; injected by the launcher / IDE / watcher.</summary>
    public const string PipeEnvironmentVariable = "JALIUM_HOTRELOAD_PIPE";

    /// <summary>Frame magic — ASCII "JHR1", readable in a hex dump.</summary>
    public const uint Magic = 0x4A485231u;

    /// <summary>Current protocol version. Bump on any incompatible frame change.</summary>
    public const byte Version = 1;

    /// <summary>Per-string byte cap (16 MiB). A single .jalxaml never comes close; anything larger is rejected.</summary>
    public const int MaxStringBytes = 16 * 1024 * 1024;

    /// <summary>Writes a patch request frame (client → server).</summary>
    public static void WriteRequest(Stream stream, string xClass, string filePath, string content)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        WriteString(writer, xClass);
        WriteString(writer, filePath);
        WriteString(writer, content);
        writer.Flush();
    }

    /// <summary>Reads a patch request frame (server side). Throws <see cref="InvalidDataException"/> on a bad magic/version/length.</summary>
    public static (string XClass, string FilePath, string Content) ReadRequest(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = reader.ReadUInt32();
        if (magic != Magic)
        {
            throw new InvalidDataException($"Hot-reload frame magic mismatch: 0x{magic:X8} (expected 0x{Magic:X8}).");
        }

        var version = reader.ReadByte();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported hot-reload protocol version {version} (expected {Version}).");
        }

        var xClass = ReadString(reader);
        var filePath = ReadString(reader);
        var content = ReadString(reader);
        return (xClass, filePath, content);
    }

    /// <summary>Writes a patch result frame (server → client).</summary>
    public static void WriteResult(Stream stream, HotReloadPatchResult result)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(result.UpdatedElements);
        writer.Write(result.FallbackReplacements);
        writer.Write(result.FailedElements);
        WriteString(writer, result.Message ?? string.Empty);
        writer.Flush();
    }

    /// <summary>Reads a patch result frame (client side).</summary>
    public static HotReloadPatchResult ReadResult(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var updated = reader.ReadInt32();
        var fallback = reader.ReadInt32();
        var failed = reader.ReadInt32();
        var message = ReadString(reader);
        return new HotReloadPatchResult(updated, fallback, failed, message);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > MaxStringBytes)
        {
            throw new InvalidDataException($"Hot-reload string exceeds the {MaxStringBytes}-byte cap ({bytes.Length}).");
        }

        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxStringBytes)
        {
            throw new InvalidDataException($"Hot-reload string length {length} out of range [0, {MaxStringBytes}].");
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException("Hot-reload frame truncated before the declared string length.");
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
