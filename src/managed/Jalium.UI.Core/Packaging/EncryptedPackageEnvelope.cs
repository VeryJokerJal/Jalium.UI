using System.Globalization;
using System.Security.Cryptography;
using System.Security.RightsManagement;
using System.Text;
using System.Xml.Linq;

namespace System.IO.Packaging;

public class EncryptedPackageEnvelope : IDisposable
{
    private readonly EnvelopeState _state;
    private bool _disposed;

    private EncryptedPackageEnvelope(EnvelopeState state)
    {
        _state = state;
        RightsManagementInformation = new RightsManagementInformation(state);
        StorageInfo = new StorageInfo(state.StorageRoot, state);
    }

    public RightsManagementInformation RightsManagementInformation { get; }

    public PackageProperties PackageProperties => GetPackage().PackageProperties;

    public FileAccess FileOpenAccess => _state.Access;

    public StorageInfo StorageInfo { get; }

    public static EncryptedPackageEnvelope Create(
        string envelopeFileName,
        PublishLicense publishLicense,
        CryptoProvider cryptoProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeFileName);
        var stream = new FileStream(envelopeFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            return CreateCore(stream, leaveOpen: false, publishLicense, cryptoProvider, packageStream: null);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static EncryptedPackageEnvelope Create(
        Stream envelopeStream,
        PublishLicense publishLicense,
        CryptoProvider cryptoProvider) =>
        CreateCore(envelopeStream, leaveOpen: true, publishLicense, cryptoProvider, packageStream: null);

    public static EncryptedPackageEnvelope CreateFromPackage(
        string envelopeFileName,
        Stream packageStream,
        PublishLicense publishLicense,
        CryptoProvider cryptoProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeFileName);
        ArgumentNullException.ThrowIfNull(packageStream);
        var stream = new FileStream(envelopeFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            return CreateCore(stream, leaveOpen: false, publishLicense, cryptoProvider, packageStream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static EncryptedPackageEnvelope CreateFromPackage(
        Stream envelopeStream,
        Stream packageStream,
        PublishLicense publishLicense,
        CryptoProvider cryptoProvider)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        return CreateCore(envelopeStream, leaveOpen: true, publishLicense, cryptoProvider, packageStream);
    }

    public static EncryptedPackageEnvelope Open(string envelopeFileName) =>
        Open(envelopeFileName, FileAccess.ReadWrite, FileShare.None);

    public static EncryptedPackageEnvelope Open(string envelopeFileName, FileAccess access) =>
        Open(envelopeFileName, access, access == FileAccess.Read ? FileShare.Read : FileShare.None);

    public static EncryptedPackageEnvelope Open(string envelopeFileName, FileAccess access, FileShare sharing)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeFileName);
        var stream = new FileStream(envelopeFileName, FileMode.Open, access, sharing);
        try
        {
            return new EncryptedPackageEnvelope(EnvelopeState.Read(stream, access, leaveOpen: false));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static EncryptedPackageEnvelope Open(Stream envelopeStream)
    {
        ArgumentNullException.ThrowIfNull(envelopeStream);
        FileAccess access = envelopeStream.CanWrite ? FileAccess.ReadWrite : FileAccess.Read;
        return new EncryptedPackageEnvelope(EnvelopeState.Read(envelopeStream, access, leaveOpen: true));
    }

    public static bool IsEncryptedPackageEnvelope(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return EnvelopeState.HasMagic(stream);
    }

    public static bool IsEncryptedPackageEnvelope(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return EnvelopeState.HasMagic(stream);
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _state.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _state.Flush();
    }

    public Package GetPackage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _state.GetPackage();
    }

    private static EncryptedPackageEnvelope CreateCore(
        Stream envelopeStream,
        bool leaveOpen,
        PublishLicense publishLicense,
        CryptoProvider cryptoProvider,
        Stream? packageStream)
    {
        ArgumentNullException.ThrowIfNull(envelopeStream);
        ArgumentNullException.ThrowIfNull(publishLicense);
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        if (!envelopeStream.CanRead || !envelopeStream.CanWrite || !envelopeStream.CanSeek)
            throw new ArgumentException("The envelope stream must be readable, writable, and seekable.", nameof(envelopeStream));
        if (!cryptoProvider.CanEncrypt)
            throw new RightsManagementException(RightsManagementFailureCode.EncryptionNotPermitted);

        var state = EnvelopeState.Create(envelopeStream, leaveOpen, publishLicense, cryptoProvider, packageStream);
        return new EncryptedPackageEnvelope(state);
    }
}

public class RightsManagementInformation
{
    private readonly EnvelopeState _state;

    internal RightsManagementInformation(EnvelopeState state)
    {
        _state = state;
    }

    public CryptoProvider? CryptoProvider
    {
        get => _state.CryptoProvider;
        set => _state.SetCryptoProvider(value);
    }

    public PublishLicense LoadPublishLicense()
    {
        string text = _state.PublishLicenseText
            ?? throw new RightsManagementException(RightsManagementFailureCode.NoLicense);
        return new PublishLicense(text);
    }

    public void SavePublishLicense(PublishLicense publishLicense)
    {
        ArgumentNullException.ThrowIfNull(publishLicense);
        _state.EnsureWritable();
        _state.PublishLicenseText = publishLicense.ToString();
        _state.MarkDirty();
    }

    public UseLicense LoadUseLicense(ContentUser userKey)
    {
        ArgumentNullException.ThrowIfNull(userKey);
        if (!_state.UseLicenses.TryGetValue(userKey, out string? text))
            throw new RightsManagementException(RightsManagementFailureCode.NoLicense);
        return new UseLicense(text);
    }

    public void SaveUseLicense(ContentUser userKey, UseLicense useLicense)
    {
        ArgumentNullException.ThrowIfNull(userKey);
        ArgumentNullException.ThrowIfNull(useLicense);
        _state.EnsureWritable();
        _state.UseLicenses[userKey] = useLicense.ToString();
        _state.MarkDirty();
    }

    public void DeleteUseLicense(ContentUser userKey)
    {
        ArgumentNullException.ThrowIfNull(userKey);
        _state.EnsureWritable();
        if (_state.UseLicenses.Remove(userKey))
            _state.MarkDirty();
    }

    public IDictionary<ContentUser, UseLicense> GetEmbeddedUseLicenses() =>
        _state.UseLicenses.ToDictionary(
            static pair => pair.Key,
            static pair => new UseLicense(pair.Value));
}

public class StorageInfo
{
    private readonly StorageNode _node;
    private readonly EnvelopeState _state;

    internal StorageInfo(StorageNode node, EnvelopeState state)
    {
        _node = node;
        _state = state;
    }

    public string Name => _node.Name;

    public StreamInfo CreateStream(string name) =>
        CreateStream(name, CompressionOption.NotCompressed, EncryptionOption.None);

    public StreamInfo CreateStream(
        string name,
        CompressionOption compressionOption,
        EncryptionOption encryptionOption)
    {
        ValidateName(name);
        _state.EnsureWritable();
        if (!Enum.IsDefined(compressionOption))
            throw new ArgumentOutOfRangeException(nameof(compressionOption));
        if (!Enum.IsDefined(encryptionOption))
            throw new ArgumentOutOfRangeException(nameof(encryptionOption));
        if (_node.Streams.ContainsKey(name))
            throw new IOException($"A stream named '{name}' already exists.");

        var entry = new StorageStreamNode(name, compressionOption, encryptionOption);
        _node.Streams.Add(name, entry);
        _state.MarkDirty();
        return new StreamInfo(entry, _state);
    }

    public StreamInfo GetStreamInfo(string name)
    {
        ValidateName(name);
        if (!_node.Streams.TryGetValue(name, out StorageStreamNode? entry))
            throw new IOException($"A stream named '{name}' does not exist.");
        return new StreamInfo(entry, _state);
    }

    public bool StreamExists(string name)
    {
        ValidateName(name);
        return _node.Streams.ContainsKey(name);
    }

    public void DeleteStream(string name)
    {
        ValidateName(name);
        _state.EnsureWritable();
        if (!_node.Streams.Remove(name))
            throw new IOException($"A stream named '{name}' does not exist.");
        _state.MarkDirty();
    }

    public StorageInfo CreateSubStorage(string name)
    {
        ValidateName(name);
        _state.EnsureWritable();
        if (_node.SubStorages.ContainsKey(name))
            throw new IOException($"A storage named '{name}' already exists.");

        var node = new StorageNode(name);
        _node.SubStorages.Add(name, node);
        _state.MarkDirty();
        return new StorageInfo(node, _state);
    }

    public StorageInfo GetSubStorageInfo(string name)
    {
        ValidateName(name);
        if (!_node.SubStorages.TryGetValue(name, out StorageNode? node))
            throw new IOException($"A storage named '{name}' does not exist.");
        return new StorageInfo(node, _state);
    }

    public bool SubStorageExists(string name)
    {
        ValidateName(name);
        return _node.SubStorages.ContainsKey(name);
    }

    public void DeleteSubStorage(string name)
    {
        ValidateName(name);
        _state.EnsureWritable();
        if (!_node.SubStorages.Remove(name))
            throw new IOException($"A storage named '{name}' does not exist.");
        _state.MarkDirty();
    }

    public StreamInfo[] GetStreams() =>
        _node.Streams.Values.Select(entry => new StreamInfo(entry, _state)).ToArray();

    public StorageInfo[] GetSubStorages() =>
        _node.SubStorages.Values.Select(node => new StorageInfo(node, _state)).ToArray();

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new ArgumentException("Storage names cannot contain path separators or null characters.", nameof(name));
    }
}

public class StreamInfo
{
    private readonly StorageStreamNode _entry;
    private readonly EnvelopeState _state;

    internal StreamInfo(StorageStreamNode entry, EnvelopeState state)
    {
        _entry = entry;
        _state = state;
    }

    public CompressionOption CompressionOption => _entry.CompressionOption;

    public EncryptionOption EncryptionOption => _entry.EncryptionOption;

    public string Name => _entry.Name;

    public Stream GetStream() => GetStream(FileMode.OpenOrCreate);

    public Stream GetStream(FileMode mode)
    {
        FileAccess access = _state.Access == FileAccess.Read ? FileAccess.Read : FileAccess.ReadWrite;
        return GetStream(mode, access);
    }

    public Stream GetStream(FileMode mode, FileAccess access)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(access))
            throw new ArgumentOutOfRangeException(nameof(access));
        if (access != FileAccess.Read)
            _state.EnsureWritable();

        bool exists = _entry.Exists;
        switch (mode)
        {
            case FileMode.CreateNew when exists:
                throw new IOException($"The stream '{Name}' already exists.");
            case FileMode.Open when !exists:
            case FileMode.Truncate when !exists:
                throw new FileNotFoundException($"The stream '{Name}' does not exist.", Name);
            case FileMode.Create:
            case FileMode.CreateNew:
            case FileMode.Truncate:
                _entry.Data = [];
                _entry.Exists = true;
                _state.MarkDirty();
                break;
            case FileMode.OpenOrCreate when !exists:
            case FileMode.Append when !exists:
                _entry.Data = [];
                _entry.Exists = true;
                _state.MarkDirty();
                break;
        }

        if (access == FileAccess.Read)
            return new MemoryStream(_entry.Data, writable: false);

        var stream = new CommittingMemoryStream(_entry.Data, bytes =>
        {
            _entry.Data = bytes;
            _entry.Exists = true;
            _state.MarkDirty();
        });
        if (mode == FileMode.Append)
            stream.Position = stream.Length;
        return stream;
    }
}

internal sealed class EnvelopeState : IDisposable
{
    private static readonly byte[] s_magic = Encoding.ASCII.GetBytes("JALIUM-EPF-1\0");
    private const int FormatVersion = 1;
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private byte[] _encryptedPackage = [];
    private MemoryStream? _packageStream;
    private Package? _package;
    private bool _dirty;
    private bool _disposed;

    private EnvelopeState(Stream stream, FileAccess access, bool leaveOpen)
    {
        _stream = stream;
        Access = access;
        _leaveOpen = leaveOpen;
        StorageRoot = new StorageNode(string.Empty);
    }

    public FileAccess Access { get; }
    public CryptoProvider? CryptoProvider { get; private set; }
    public string? PublishLicenseText { get; set; }
    public Dictionary<ContentUser, string> UseLicenses { get; } = [];
    public StorageNode StorageRoot { get; private set; }

    public static EnvelopeState Create(
        Stream stream,
        bool leaveOpen,
        PublishLicense publishLicense,
        CryptoProvider cryptoProvider,
        Stream? sourcePackage)
    {
        var state = new EnvelopeState(stream, FileAccess.ReadWrite, leaveOpen)
        {
            PublishLicenseText = publishLicense.ToString(),
            CryptoProvider = cryptoProvider,
            _packageStream = new MemoryStream(),
            _dirty = true,
        };

        if (sourcePackage is not null)
        {
            if (!sourcePackage.CanRead)
                throw new ArgumentException("The package stream must be readable.", nameof(sourcePackage));
            sourcePackage.CopyTo(state._packageStream);
            state._packageStream.Position = 0;
            state._package = Package.Open(state._packageStream, FileMode.Open, FileAccess.ReadWrite);
        }
        else
        {
            // ZipPackage does not emit an end-of-central-directory record for a newly
            // created, empty package until it is closed. Seed the backing stream with a
            // complete empty OPC package before keeping it open for caller mutations.
            using (Package emptyPackage = Package.Open(state._packageStream, FileMode.Create, FileAccess.ReadWrite))
            {
            }

            state._packageStream.Position = 0;
            state._package = Package.Open(state._packageStream, FileMode.Open, FileAccess.ReadWrite);
        }

        state.Flush();
        return state;
    }

    public static EnvelopeState Read(Stream stream, FileAccess access, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The envelope stream must be readable and seekable.", nameof(stream));
        if (access != FileAccess.Read && !stream.CanWrite)
            throw new ArgumentException("The requested access requires a writable stream.", nameof(stream));

        long originalPosition = stream.Position;
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            byte[] magic = reader.ReadBytes(s_magic.Length);
            if (!magic.AsSpan().SequenceEqual(s_magic))
                throw new FileFormatException("The stream is not a Jalium encrypted package envelope.");
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new FileFormatException($"Unsupported encrypted package envelope version {version}.");
            int headerLength = reader.ReadInt32();
            if (headerLength < 0 || headerLength > stream.Length - stream.Position)
                throw new FileFormatException("The encrypted package envelope header is invalid.");

            byte[] header = reader.ReadBytes(headerLength);
            var state = new EnvelopeState(stream, access, leaveOpen);
            state.ReadHeader(header);
            state._encryptedPackage = reader.ReadBytes(checked((int)(stream.Length - stream.Position)));
            state._dirty = false;
            return state;
        }
        catch
        {
            stream.Position = originalPosition;
            throw;
        }
    }

    public static bool HasMagic(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
            return false;

        long position = stream.Position;
        try
        {
            Span<byte> actual = stackalloc byte[s_magic.Length];
            int read = stream.Read(actual);
            return read == actual.Length && actual.SequenceEqual(s_magic);
        }
        finally
        {
            stream.Position = position;
        }
    }

    public void SetCryptoProvider(CryptoProvider? cryptoProvider)
    {
        ThrowIfDisposed();
        if (ReferenceEquals(CryptoProvider, cryptoProvider))
            return;
        if (_package is not null && Access != FileAccess.Read)
            Flush();

        _package?.Close();
        _package = null;
        _packageStream?.Dispose();
        _packageStream = null;
        CryptoProvider = cryptoProvider;
    }

    public Package GetPackage()
    {
        ThrowIfDisposed();
        if (_package is not null)
            return _package;
        if (CryptoProvider is null)
            throw new RightsManagementException(RightsManagementFailureCode.EnvironmentNotLoaded);

        byte[] packageBytes = CryptoProvider.Decrypt(_encryptedPackage);
        try
        {
            _packageStream = new MemoryStream(packageBytes.Length + 4096);
            _packageStream.Write(packageBytes);
            _packageStream.Position = 0;
            _package = Package.Open(
                _packageStream,
                FileMode.Open,
                Access == FileAccess.Read ? FileAccess.Read : FileAccess.ReadWrite);
            return _package;
        }
        catch (Exception ex) when (ex is IOException or FileFormatException)
        {
            _packageStream?.Dispose();
            _packageStream = null;
            throw new RightsManagementException(
                RightsManagementFailureCode.InvalidLicense,
                "The content key does not open this encrypted package.",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(packageBytes);
        }
    }

    public void Flush()
    {
        ThrowIfDisposed();
        if (Access == FileAccess.Read)
            return;
        if (!_dirty && _package is null)
            return;
        if (_package is not null)
        {
            if (CryptoProvider is null)
                throw new RightsManagementException(RightsManagementFailureCode.EnvironmentNotLoaded);
            _package.Flush();
            byte[] clearPackage = CreatePackageSnapshot(_package);
            try
            {
                _encryptedPackage = CryptoProvider.Encrypt(clearPackage);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearPackage);
            }
        }

        byte[] header = WriteHeader();
        _stream.Position = 0;
        _stream.SetLength(0);
        using (var writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(s_magic);
            writer.Write(FormatVersion);
            writer.Write(header.Length);
            writer.Write(header);
            writer.Write(_encryptedPackage);
            writer.Flush();
        }
        _stream.Flush();
        _dirty = false;
    }

    public void MarkDirty()
    {
        ThrowIfDisposed();
        _dirty = true;
    }

    public void EnsureWritable()
    {
        ThrowIfDisposed();
        if (Access == FileAccess.Read)
            throw new UnauthorizedAccessException("The encrypted package envelope was opened read-only.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (Access != FileAccess.Read)
            Flush();

        _disposed = true;
        _package?.Close();
        _packageStream?.Dispose();
        if (!_leaveOpen)
            _stream.Dispose();
    }

    private static byte[] CreatePackageSnapshot(Package source)
    {
        using var stream = new MemoryStream();
        using (Package destination = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite))
        {
            CopyPackageProperties(source.PackageProperties, destination.PackageProperties);

            foreach (PackagePart sourcePart in source.GetParts())
            {
                PackagePart destinationPart = destination.CreatePart(
                    sourcePart.Uri,
                    sourcePart.ContentType,
                    sourcePart.CompressionOption);
                using (Stream input = sourcePart.GetStream(FileMode.Open, FileAccess.Read))
                using (Stream output = destinationPart.GetStream(FileMode.Create, FileAccess.Write))
                    input.CopyTo(output);

                foreach (PackageRelationship relationship in sourcePart.GetRelationships())
                {
                    destinationPart.CreateRelationship(
                        relationship.TargetUri,
                        relationship.TargetMode,
                        relationship.RelationshipType,
                        relationship.Id);
                }
            }

            foreach (PackageRelationship relationship in source.GetRelationships())
            {
                destination.CreateRelationship(
                    relationship.TargetUri,
                    relationship.TargetMode,
                    relationship.RelationshipType,
                    relationship.Id);
            }
        }

        return stream.ToArray();
    }

    private static void CopyPackageProperties(PackageProperties source, PackageProperties destination)
    {
        destination.Category = source.Category;
        destination.ContentStatus = source.ContentStatus;
        destination.ContentType = source.ContentType;
        destination.Created = source.Created;
        destination.Creator = source.Creator;
        destination.Description = source.Description;
        destination.Identifier = source.Identifier;
        destination.Keywords = source.Keywords;
        destination.Language = source.Language;
        destination.LastModifiedBy = source.LastModifiedBy;
        destination.LastPrinted = source.LastPrinted;
        destination.Modified = source.Modified;
        destination.Revision = source.Revision;
        destination.Subject = source.Subject;
        destination.Title = source.Title;
        destination.Version = source.Version;
    }

    private byte[] WriteHeader()
    {
        var root = new XElement("envelope");
        if (PublishLicenseText is not null)
            root.Add(new XElement("publishLicense", PublishLicenseText));

        var useLicenses = new XElement("useLicenses");
        foreach ((ContentUser user, string license) in UseLicenses.OrderBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase))
        {
            useLicenses.Add(new XElement("useLicense",
                new XAttribute("name", user.Name),
                new XAttribute("authentication", user.AuthenticationType),
                license));
        }
        root.Add(useLicenses);
        root.Add(WriteStorage(StorageRoot));
        return Encoding.UTF8.GetBytes(new XDocument(root).ToString(SaveOptions.DisableFormatting));
    }

    private void ReadHeader(byte[] header)
    {
        XElement root = XDocument.Parse(Encoding.UTF8.GetString(header), LoadOptions.None).Root
            ?? throw new FileFormatException("The encrypted package envelope has no header root.");
        PublishLicenseText = (string?)root.Element("publishLicense");
        foreach (XElement element in root.Element("useLicenses")?.Elements("useLicense") ?? [])
        {
            string name = (string?)element.Attribute("name") ?? string.Empty;
            var authentication = Enum.Parse<AuthenticationType>(
                (string?)element.Attribute("authentication") ?? string.Empty);
            ContentUser user = authentication == AuthenticationType.Internal
                ? ContentUser.CreateInternal(name)
                : new ContentUser(name, authentication);
            UseLicenses[user] = element.Value;
        }

        StorageRoot = root.Element("storage") is XElement storage
            ? ReadStorage(storage)
            : new StorageNode(string.Empty);
    }

    private static XElement WriteStorage(StorageNode node)
    {
        var element = new XElement("storage", new XAttribute("name", node.Name));
        foreach (StorageStreamNode stream in node.Streams.Values)
        {
            element.Add(new XElement("stream",
                new XAttribute("name", stream.Name),
                new XAttribute("compression", stream.CompressionOption),
                new XAttribute("encryption", stream.EncryptionOption),
                new XAttribute("exists", stream.Exists),
                Convert.ToBase64String(stream.Data)));
        }
        foreach (StorageNode storage in node.SubStorages.Values)
            element.Add(WriteStorage(storage));
        return element;
    }

    private static StorageNode ReadStorage(XElement element)
    {
        var node = new StorageNode((string?)element.Attribute("name") ?? string.Empty);
        foreach (XElement stream in element.Elements("stream"))
        {
            var entry = new StorageStreamNode(
                (string?)stream.Attribute("name") ?? string.Empty,
                Enum.Parse<CompressionOption>((string?)stream.Attribute("compression") ?? string.Empty),
                Enum.Parse<EncryptionOption>((string?)stream.Attribute("encryption") ?? string.Empty))
            {
                Exists = (bool?)stream.Attribute("exists") ?? true,
                Data = string.IsNullOrEmpty(stream.Value) ? [] : Convert.FromBase64String(stream.Value),
            };
            node.Streams.Add(entry.Name, entry);
        }
        foreach (XElement storage in element.Elements("storage"))
        {
            StorageNode child = ReadStorage(storage);
            node.SubStorages.Add(child.Name, child);
        }
        return node;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed class StorageNode
{
    public StorageNode(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public Dictionary<string, StorageStreamNode> Streams { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, StorageNode> SubStorages { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class StorageStreamNode
{
    public StorageStreamNode(
        string name,
        CompressionOption compressionOption,
        EncryptionOption encryptionOption)
    {
        Name = name;
        CompressionOption = compressionOption;
        EncryptionOption = encryptionOption;
    }

    public string Name { get; }
    public CompressionOption CompressionOption { get; }
    public EncryptionOption EncryptionOption { get; }
    public bool Exists { get; set; } = true;
    public byte[] Data { get; set; } = [];
}

internal sealed class CommittingMemoryStream : MemoryStream
{
    private Action<byte[]>? _commit;

    public CommittingMemoryStream(byte[] initial, Action<byte[]> commit)
        : base(Math.Max(initial.Length, 256))
    {
        ArgumentNullException.ThrowIfNull(initial);
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        Write(initial, 0, initial.Length);
        Position = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Action<byte[]>? commit = Interlocked.Exchange(ref _commit, null);
            commit?.Invoke(ToArray());
        }
        base.Dispose(disposing);
    }
}
