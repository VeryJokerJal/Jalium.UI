using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace System.Security.RightsManagement;

public enum AuthenticationType
{
    Windows = 0,
    Passport = 1,
    WindowsPassport = 2,
    Internal = 3,
}

public enum ContentRight
{
    View = 0,
    Edit = 1,
    Print = 2,
    Extract = 3,
    ObjectModel = 4,
    Owner = 5,
    ViewRightsData = 6,
    Forward = 7,
    Reply = 8,
    ReplyAll = 9,
    Sign = 10,
    DocumentEdit = 11,
    Export = 12,
}

public enum UserActivationMode
{
    Permanent = 0,
    Temporary = 1,
}

public enum RightsManagementFailureCode
{
    Success = 0,
    ManifestPolicyViolation = -2147183860,
    InvalidLicense = -2147168512,
    InfoNotInLicense = -2147168511,
    InvalidLicenseSignature = -2147168510,
    EncryptionNotPermitted = -2147168508,
    RightNotGranted = -2147168507,
    InvalidVersion = -2147168506,
    InvalidEncodingType = -2147168505,
    InvalidNumericalValue = -2147168504,
    InvalidAlgorithmType = -2147168503,
    EnvironmentNotLoaded = -2147168502,
    EnvironmentCannotLoad = -2147168501,
    TooManyLoadedEnvironments = -2147168500,
    IncompatibleObjects = -2147168498,
    LibraryFail = -2147168497,
    EnablingPrincipalFailure = -2147168496,
    InfoNotPresent = -2147168495,
    BadGetInfoQuery = -2147168494,
    KeyTypeUnsupported = -2147168493,
    CryptoOperationUnsupported = -2147168492,
    ClockRollbackDetected = -2147168491,
    QueryReportsNoResults = -2147168490,
    UnexpectedException = -2147168489,
    BindValidityTimeViolated = -2147168488,
    BrokenCertChain = -2147168487,
    BindPolicyViolation = -2147168485,
    BindRevokedLicense = -2147168484,
    BindRevokedIssuer = -2147168483,
    BindRevokedPrincipal = -2147168482,
    BindRevokedResource = -2147168481,
    BindRevokedModule = -2147168480,
    BindContentNotInEndUseLicense = -2147168479,
    BindAccessPrincipalNotEnabling = -2147168478,
    BindAccessUnsatisfied = -2147168477,
    BindIndicatedPrincipalMissing = -2147168476,
    BindMachineNotFoundInGroupIdentity = -2147168475,
    LibraryUnsupportedPlugIn = -2147168474,
    BindRevocationListStale = -2147168473,
    BindNoApplicableRevocationList = -2147168472,
    InvalidHandle = -2147168468,
    BindIntervalTimeViolated = -2147168465,
    BindNoSatisfiedRightsGroup = -2147168464,
    BindSpecifiedWorkMissing = -2147168463,
    NoMoreData = -2147168461,
    LicenseAcquisitionFailed = -2147168460,
    IdMismatch = -2147168459,
    TooManyCertificates = -2147168458,
    NoDistributionPointUrlFound = -2147168457,
    AlreadyInProgress = -2147168456,
    GroupIdentityNotSet = -2147168455,
    RecordNotFound = -2147168454,
    NoConnect = -2147168453,
    NoLicense = -2147168452,
    NeedsMachineActivation = -2147168451,
    NeedsGroupIdentityActivation = -2147168450,
    ActivationFailed = -2147168448,
    Aborted = -2147168447,
    OutOfQuota = -2147168446,
    AuthenticationFailed = -2147168445,
    ServerError = -2147168444,
    InstallationFailed = -2147168443,
    HidCorrupted = -2147168442,
    InvalidServerResponse = -2147168441,
    ServiceNotFound = -2147168440,
    UseDefault = -2147168439,
    ServerNotFound = -2147168438,
    InvalidEmail = -2147168437,
    ValidityTimeViolation = -2147168436,
    OutdatedModule = -2147168435,
    NotSet = -2147168434,
    MetadataNotSet = -2147168433,
    RevocationInfoNotSet = -2147168432,
    InvalidTimeInfo = -2147168431,
    RightNotSet = -2147168430,
    LicenseBindingToWindowsIdentityFailed = -2147168429,
    InvalidIssuanceLicenseTemplate = -2147168428,
    InvalidKeyLength = -2147168427,
    ExpiredOfficialIssuanceLicenseTemplate = -2147168425,
    InvalidClientLicensorCertificate = -2147168424,
    HidInvalid = -2147168423,
    EmailNotVerified = -2147168422,
    ServiceMoved = -2147168421,
    ServiceGone = -2147168420,
    AdEntryNotFound = -2147168419,
    NotAChain = -2147168418,
    RequestDenied = -2147168417,
    DebuggerDetected = -2147168416,
    InvalidLockboxType = -2147168400,
    InvalidLockboxPath = -2147168399,
    InvalidRegistryPath = -2147168398,
    NoAesCryptoProvider = -2147168397,
    GlobalOptionAlreadySet = -2147168396,
    OwnerLicenseNotFound = -2147168395,
}

public class ContentUser
{
    private static readonly ContentUser s_anyoneUser = new("Anyone", AuthenticationType.Internal, allowInternal: true);
    private static readonly ContentUser s_ownerUser = new("Owner", AuthenticationType.Internal, allowInternal: true);

    public ContentUser(string name, AuthenticationType authenticationType)
        : this(name, authenticationType, allowInternal: false)
    {
    }

    private ContentUser(string name, AuthenticationType authenticationType, bool allowInternal)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(authenticationType))
            throw new ArgumentOutOfRangeException(nameof(authenticationType));
        if (authenticationType == AuthenticationType.Internal && !allowInternal)
            throw new ArgumentOutOfRangeException(nameof(authenticationType));

        Name = name;
        AuthenticationType = authenticationType;
    }

    public AuthenticationType AuthenticationType { get; }

    public string Name { get; }

    public static ContentUser AnyoneUser => s_anyoneUser;

    public static ContentUser OwnerUser => s_ownerUser;

    public bool IsAuthenticated() => SecureEnvironment.IsUserActivated(this);

    public override bool Equals(object? obj) =>
        obj is ContentUser other &&
        AuthenticationType == other.AuthenticationType &&
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        AuthenticationType,
        StringComparer.OrdinalIgnoreCase.GetHashCode(Name));

    internal static ContentUser CreateInternal(string name) =>
        string.Equals(name, s_anyoneUser.Name, StringComparison.OrdinalIgnoreCase)
            ? s_anyoneUser
            : s_ownerUser;
}

public class ContentGrant
{
    public ContentGrant(ContentUser user, ContentRight right)
        : this(user, right, DateTime.MinValue, DateTime.MaxValue)
    {
    }

    public ContentGrant(ContentUser user, ContentRight right, DateTime validFrom, DateTime validUntil)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Enum.IsDefined(right))
            throw new ArgumentOutOfRangeException(nameof(right));
        if (validFrom > validUntil)
            throw new ArgumentOutOfRangeException(nameof(validFrom));

        User = user;
        Right = right;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }

    public ContentUser User { get; }

    public ContentRight Right { get; }

    public DateTime ValidFrom { get; }

    public DateTime ValidUntil { get; }
}

public class LocalizedNameDescriptionPair
{
    public LocalizedNameDescriptionPair(string name, string description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }

    public override bool Equals(object? obj) =>
        obj is LocalizedNameDescriptionPair other &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        string.Equals(Description, other.Description, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Name, Description);
}

[Serializable]
public class RightsManagementException : Exception
{
    public RightsManagementException()
        : base("Rights management operation failed.")
    {
    }

    public RightsManagementException(string? message)
        : base(message)
    {
    }

    public RightsManagementException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public RightsManagementException(RightsManagementFailureCode failureCode)
        : this(failureCode, GetDefaultMessage(failureCode))
    {
    }

    public RightsManagementException(RightsManagementFailureCode failureCode, string? message)
        : base(message)
    {
        FailureCode = failureCode;
    }

    public RightsManagementException(RightsManagementFailureCode failureCode, Exception? innerException)
        : this(failureCode, GetDefaultMessage(failureCode), innerException)
    {
    }

    public RightsManagementException(
        RightsManagementFailureCode failureCode,
        string? message,
        Exception? innerException)
        : base(message, innerException)
    {
        FailureCode = failureCode;
    }

#pragma warning disable SYSLIB0051
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.",
        DiagnosticId = "SYSLIB0051",
        UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    protected RightsManagementException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        FailureCode = (RightsManagementFailureCode)info.GetInt32(nameof(FailureCode));
    }
#pragma warning restore SYSLIB0051

    public RightsManagementFailureCode FailureCode { get; }

#pragma warning disable SYSLIB0051
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.",
        DiagnosticId = "SYSLIB0051",
        UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        base.GetObjectData(info, context);
        info.AddValue(nameof(FailureCode), (int)FailureCode);
    }
#pragma warning restore SYSLIB0051

    private static string GetDefaultMessage(RightsManagementFailureCode failureCode) => failureCode switch
    {
        RightsManagementFailureCode.InvalidLicense => "License is not valid.",
        RightsManagementFailureCode.RightNotGranted => "The requested right was not granted.",
        RightsManagementFailureCode.EnvironmentNotLoaded => "The secure environment is not loaded.",
        RightsManagementFailureCode.EncryptionNotPermitted => "Encryption is not permitted by this license.",
        _ => $"Rights management operation failed ({failureCode}).",
    };
}

public class SecureEnvironment : IDisposable
{
    private static readonly object s_activationGate = new();
    private static readonly HashSet<ContentUser> s_activatedUsers = [];
    private bool _disposed;
    private readonly bool _removeOnDispose;

    private SecureEnvironment(string applicationManifest, ContentUser user, bool removeOnDispose)
    {
        ApplicationManifest = applicationManifest;
        User = user;
        _removeOnDispose = removeOnDispose;
    }

    public ContentUser User { get; }

    public string ApplicationManifest { get; }

    public static SecureEnvironment Create(string applicationManifest, ContentUser user)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationManifest);
        ArgumentNullException.ThrowIfNull(user);
        if (ReferenceEquals(user, ContentUser.AnyoneUser) || ReferenceEquals(user, ContentUser.OwnerUser))
            throw new ArgumentOutOfRangeException(nameof(user));

        lock (s_activationGate)
            s_activatedUsers.Add(user);
        return new SecureEnvironment(applicationManifest, user, removeOnDispose: false);
    }

    public static SecureEnvironment Create(
        string applicationManifest,
        AuthenticationType authentication,
        UserActivationMode userActivationMode)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationManifest);
        if (!Enum.IsDefined(authentication) || authentication == AuthenticationType.Internal)
            throw new ArgumentOutOfRangeException(nameof(authentication));
        if (!Enum.IsDefined(userActivationMode))
            throw new ArgumentOutOfRangeException(nameof(userActivationMode));

        string name = Environment.UserName;
        if (authentication == AuthenticationType.Passport)
            name += "@passport";

        var user = new ContentUser(name, authentication);
        lock (s_activationGate)
            s_activatedUsers.Add(user);
        return new SecureEnvironment(
            applicationManifest,
            user,
            removeOnDispose: userActivationMode == UserActivationMode.Temporary);
    }

    public static bool IsUserActivated(ContentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (s_activationGate)
            return s_activatedUsers.Contains(user);
    }

    public static ReadOnlyCollection<ContentUser> GetActivatedUsers()
    {
        lock (s_activationGate)
            return Array.AsReadOnly(s_activatedUsers.ToArray());
    }

    public static void RemoveActivatedUser(ContentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (s_activationGate)
            s_activatedUsers.Remove(user);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_removeOnDispose)
            RemoveActivatedUser(User);
    }

    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public class CryptoProvider : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private byte[]? _key;
    private readonly ReadOnlyCollection<ContentGrant> _boundGrants;

    internal CryptoProvider(byte[] key, IEnumerable<ContentGrant> boundGrants)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length is not (16 or 24 or 32))
            throw new RightsManagementException(RightsManagementFailureCode.InvalidKeyLength);

        _key = (byte[])key.Clone();
        _boundGrants = Array.AsReadOnly(boundGrants?.ToArray() ?? throw new ArgumentNullException(nameof(boundGrants)));
    }

    public int BlockSize => 16;

    public bool CanMergeBlocks => false;

    public ReadOnlyCollection<ContentGrant> BoundGrants => _boundGrants;

    public bool CanEncrypt => _key is not null && _boundGrants.Any(static grant =>
        grant.Right is ContentRight.Owner or ContentRight.Edit or ContentRight.DocumentEdit);

    public bool CanDecrypt => _key is not null && _boundGrants.Count > 0;

    public byte[] Encrypt(byte[] clearText)
    {
        ArgumentNullException.ThrowIfNull(clearText);
        if (!CanEncrypt)
            throw new RightsManagementException(RightsManagementFailureCode.EncryptionNotPermitted);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipherText = new byte[clearText.Length];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(_key!, TagSize))
            aes.Encrypt(nonce, clearText, cipherText, tag);

        byte[] result = new byte[1 + NonceSize + TagSize + cipherText.Length];
        result[0] = 1;
        nonce.CopyTo(result, 1);
        tag.CopyTo(result, 1 + NonceSize);
        cipherText.CopyTo(result, 1 + NonceSize + TagSize);
        return result;
    }

    public byte[] Decrypt(byte[] cryptoText)
    {
        ArgumentNullException.ThrowIfNull(cryptoText);
        if (!CanDecrypt)
            throw new RightsManagementException(RightsManagementFailureCode.RightNotGranted);
        if (cryptoText.Length < 1 + NonceSize + TagSize || cryptoText[0] != 1)
            throw new RightsManagementException(RightsManagementFailureCode.InvalidEncodingType);

        ReadOnlySpan<byte> nonce = cryptoText.AsSpan(1, NonceSize);
        ReadOnlySpan<byte> tag = cryptoText.AsSpan(1 + NonceSize, TagSize);
        ReadOnlySpan<byte> cipherText = cryptoText.AsSpan(1 + NonceSize + TagSize);
        byte[] clearText = new byte[cipherText.Length];
        try
        {
            using var aes = new AesGcm(_key!, TagSize);
            aes.Decrypt(nonce, cipherText, tag, clearText);
            return clearText;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(clearText);
            throw new RightsManagementException(
                RightsManagementFailureCode.InvalidLicenseSignature,
                "The encrypted content could not be authenticated.",
                ex);
        }
    }

    public void Dispose()
    {
        byte[]? key = Interlocked.Exchange(ref _key, null);
        if (key is not null)
            CryptographicOperations.ZeroMemory(key);
    }

    internal byte[] ExportKey()
    {
        ObjectDisposedException.ThrowIf(_key is null, this);
        return (byte[])_key.Clone();
    }
}

public class UnsignedPublishLicense
{
    private readonly List<ContentGrant> _grants = [];
    private readonly Dictionary<int, LocalizedNameDescriptionPair> _localizedNames = [];

    public UnsignedPublishLicense()
    {
        ContentId = Guid.NewGuid();
    }

    public UnsignedPublishLicense(string publishLicenseTemplate)
        : this()
    {
        ArgumentNullException.ThrowIfNull(publishLicenseTemplate);
        if (!RightsManagementLicenseCodec.TryDecode(publishLicenseTemplate, out LicenseModel model))
            return;

        Owner = model.Owner;
        ReferralInfoName = model.ReferralInfoName;
        ReferralInfoUri = model.ReferralInfoUri;
        ContentId = model.ContentId;
        _grants.AddRange(model.Grants);
        foreach ((int language, LocalizedNameDescriptionPair value) in model.LocalizedNames)
            _localizedNames[language] = value;
    }

    public ContentUser? Owner { get; set; }

    public string? ReferralInfoName { get; set; }

    public Uri? ReferralInfoUri { get; set; }

    public Guid ContentId { get; set; }

    public ICollection<ContentGrant> Grants => _grants;

    public IDictionary<int, LocalizedNameDescriptionPair> LocalizedNameDescriptionDictionary => _localizedNames;

    public PublishLicense Sign(SecureEnvironment secureEnvironment, out UseLicense authorUseLicense)
    {
        ArgumentNullException.ThrowIfNull(secureEnvironment);
        secureEnvironment.ThrowIfDisposed();

        ContentUser owner = Owner ?? secureEnvironment.User;
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var grants = _grants.ToList();
        if (!grants.Any(grant => grant.User.Equals(owner) && grant.Right == ContentRight.Owner))
            grants.Add(new ContentGrant(owner, ContentRight.Owner));

        var model = new LicenseModel
        {
            Kind = LicenseKind.Publish,
            ContentId = ContentId == Guid.Empty ? Guid.NewGuid() : ContentId,
            Owner = owner,
            ReferralInfoName = ReferralInfoName,
            ReferralInfoUri = ReferralInfoUri,
            Key = key,
            Grants = grants,
            LocalizedNames = new Dictionary<int, LocalizedNameDescriptionPair>(_localizedNames),
        };

        string publishText = RightsManagementLicenseCodec.Encode(model);
        var authorModel = model.CreateUseLicense(owner, includeOwnerRight: true);
        authorUseLicense = new UseLicense(RightsManagementLicenseCodec.Encode(authorModel));
        CryptographicOperations.ZeroMemory(key);
        return new PublishLicense(publishText);
    }

    public override string ToString()
    {
        ContentUser owner = Owner ?? throw new RightsManagementException(RightsManagementFailureCode.RightNotSet);
        var model = new LicenseModel
        {
            Kind = LicenseKind.Unsigned,
            ContentId = ContentId,
            Owner = owner,
            ReferralInfoName = ReferralInfoName,
            ReferralInfoUri = ReferralInfoUri,
            Grants = _grants.ToList(),
            LocalizedNames = new Dictionary<int, LocalizedNameDescriptionPair>(_localizedNames),
        };
        return RightsManagementLicenseCodec.Encode(model);
    }
}

public class PublishLicense
{
    private readonly string _signedPublishLicense;
    private readonly LicenseModel _model;

    public PublishLicense(string signedPublishLicense)
    {
        ArgumentException.ThrowIfNullOrEmpty(signedPublishLicense);
        _signedPublishLicense = signedPublishLicense;
        if (!RightsManagementLicenseCodec.TryDecode(signedPublishLicense, out LicenseModel model))
            model = RightsManagementLicenseCodec.CreateFallback(signedPublishLicense, LicenseKind.Publish);
        _model = model;
    }

    public string? ReferralInfoName => _model.ReferralInfoName;

    public Uri? ReferralInfoUri => _model.ReferralInfoUri;

    public Guid ContentId => _model.ContentId;

    public Uri? UseLicenseAcquisitionUrl => _model.UseLicenseAcquisitionUrl;

    public UnsignedPublishLicense DecryptUnsignedPublishLicense(CryptoProvider cryptoProvider)
    {
        ArgumentNullException.ThrowIfNull(cryptoProvider);
        using var suppliedKey = new SensitiveKey(cryptoProvider.ExportKey());
        if (_model.Key.Length > 0 && !CryptographicOperations.FixedTimeEquals(suppliedKey.Bytes, _model.Key))
            throw new RightsManagementException(RightsManagementFailureCode.InvalidLicense);
        return new UnsignedPublishLicense(RightsManagementLicenseCodec.Encode(_model with { Kind = LicenseKind.Unsigned }));
    }

    public UseLicense AcquireUseLicense(SecureEnvironment secureEnvironment) =>
        AcquireUseLicenseCore(secureEnvironment);

    public UseLicense AcquireUseLicenseNoUI(SecureEnvironment secureEnvironment) =>
        AcquireUseLicenseCore(secureEnvironment);

    public override string ToString() => _signedPublishLicense;

    private UseLicense AcquireUseLicenseCore(SecureEnvironment secureEnvironment)
    {
        ArgumentNullException.ThrowIfNull(secureEnvironment);
        secureEnvironment.ThrowIfDisposed();
        LicenseModel useModel = _model.CreateUseLicense(secureEnvironment.User, includeOwnerRight: false);
        if (useModel.Grants.Count == 0)
            throw new RightsManagementException(RightsManagementFailureCode.RightNotGranted);
        return new UseLicense(RightsManagementLicenseCodec.Encode(useModel));
    }
}

public class UseLicense
{
    private readonly string _useLicense;
    private readonly LicenseModel _model;
    private readonly Dictionary<string, string> _applicationData;

    public UseLicense(string useLicense)
    {
        ArgumentException.ThrowIfNullOrEmpty(useLicense);
        _useLicense = useLicense;
        if (!RightsManagementLicenseCodec.TryDecode(useLicense, out LicenseModel model))
            model = RightsManagementLicenseCodec.CreateFallback(useLicense, LicenseKind.Use);
        _model = model;
        _applicationData = new Dictionary<string, string>(model.ApplicationData, StringComparer.Ordinal);
    }

    public ContentUser Owner => _model.Owner ?? ContentUser.OwnerUser;

    public Guid ContentId => _model.ContentId;

    public IDictionary<string, string> ApplicationData => _applicationData;

    public CryptoProvider Bind(SecureEnvironment secureEnvironment)
    {
        ArgumentNullException.ThrowIfNull(secureEnvironment);
        secureEnvironment.ThrowIfDisposed();

        DateTime now = DateTime.UtcNow;
        List<ContentGrant> applicable = _model.Grants.Where(grant =>
            (grant.User.Equals(secureEnvironment.User) ||
             ReferenceEquals(grant.User, ContentUser.AnyoneUser) ||
             (grant.Right == ContentRight.Owner && _model.Owner?.Equals(secureEnvironment.User) == true)) &&
            now >= grant.ValidFrom.ToUniversalTime() &&
            now <= grant.ValidUntil.ToUniversalTime()).ToList();

        if (applicable.Count == 0)
            throw new RightsManagementException(RightsManagementFailureCode.RightNotGranted);
        if (_model.Key.Length == 0)
            throw new RightsManagementException(RightsManagementFailureCode.InvalidLicense);

        return new CryptoProvider(_model.Key, applicable);
    }

    public override string ToString() => _useLicense;

    public override bool Equals(object? x) =>
        x is UseLicense other && string.Equals(_useLicense, other._useLicense, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_useLicense);
}

internal enum LicenseKind
{
    Unsigned,
    Publish,
    Use,
}

internal sealed record LicenseModel
{
    public LicenseKind Kind { get; init; }
    public Guid ContentId { get; init; }
    public ContentUser? Owner { get; init; }
    public string? ReferralInfoName { get; init; }
    public Uri? ReferralInfoUri { get; init; }
    public Uri? UseLicenseAcquisitionUrl { get; init; }
    public byte[] Key { get; init; } = [];
    public List<ContentGrant> Grants { get; init; } = [];
    public Dictionary<int, LocalizedNameDescriptionPair> LocalizedNames { get; init; } = [];
    public Dictionary<string, string> ApplicationData { get; init; } = new(StringComparer.Ordinal);

    public LicenseModel CreateUseLicense(ContentUser user, bool includeOwnerRight)
    {
        var grants = Grants.Where(grant =>
            grant.User.Equals(user) ||
            ReferenceEquals(grant.User, ContentUser.AnyoneUser) ||
            (includeOwnerRight && grant.Right == ContentRight.Owner)).ToList();

        return this with
        {
            Kind = LicenseKind.Use,
            Grants = grants,
            LocalizedNames = [],
        };
    }
}

internal static class RightsManagementLicenseCodec
{
    private const string Prefix = "JALIUM-RMS1:";

    public static string Encode(LicenseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var root = new XElement("license",
            new XAttribute("kind", model.Kind.ToString()),
            new XAttribute("contentId", model.ContentId.ToString("D", CultureInfo.InvariantCulture)));

        if (model.Owner is not null)
            root.Add(WriteUser("owner", model.Owner));
        if (model.ReferralInfoName is not null)
            root.Add(new XElement("referralName", model.ReferralInfoName));
        if (model.ReferralInfoUri is not null)
            root.Add(new XElement("referralUri", model.ReferralInfoUri.OriginalString));
        if (model.UseLicenseAcquisitionUrl is not null)
            root.Add(new XElement("acquisitionUri", model.UseLicenseAcquisitionUrl.OriginalString));
        if (model.Key.Length > 0)
            root.Add(new XElement("key", Convert.ToBase64String(model.Key)));

        var grants = new XElement("grants");
        foreach (ContentGrant grant in model.Grants)
        {
            grants.Add(new XElement("grant",
                new XAttribute("name", grant.User.Name),
                new XAttribute("authentication", grant.User.AuthenticationType),
                new XAttribute("right", grant.Right),
                new XAttribute("validFrom", grant.ValidFrom.ToUniversalTime().Ticks),
                new XAttribute("validUntil", grant.ValidUntil.ToUniversalTime().Ticks)));
        }
        root.Add(grants);

        var localized = new XElement("localized");
        foreach ((int language, LocalizedNameDescriptionPair pair) in model.LocalizedNames.OrderBy(static item => item.Key))
        {
            localized.Add(new XElement("entry",
                new XAttribute("language", language),
                new XAttribute("name", pair.Name),
                new XAttribute("description", pair.Description)));
        }
        root.Add(localized);

        var applicationData = new XElement("applicationData");
        foreach ((string key, string value) in model.ApplicationData.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            applicationData.Add(new XElement("entry",
                new XAttribute("key", key),
                new XAttribute("value", value)));
        }
        root.Add(applicationData);

        string xml = new XDocument(root).ToString(SaveOptions.DisableFormatting);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));
    }

    public static bool TryDecode(string value, out LicenseModel model)
    {
        model = null!;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            byte[] xmlBytes = Convert.FromBase64String(value[Prefix.Length..]);
            XElement root = XDocument.Parse(Encoding.UTF8.GetString(xmlBytes), LoadOptions.None).Root
                ?? throw new FormatException("The license document has no root element.");
            var grants = new List<ContentGrant>();
            foreach (XElement element in root.Element("grants")?.Elements("grant") ?? [])
            {
                var authentication = Enum.Parse<AuthenticationType>((string?)element.Attribute("authentication") ?? string.Empty);
                string name = (string?)element.Attribute("name") ?? string.Empty;
                ContentUser user = authentication == AuthenticationType.Internal
                    ? ContentUser.CreateInternal(name)
                    : new ContentUser(name, authentication);
                grants.Add(new ContentGrant(
                    user,
                    Enum.Parse<ContentRight>((string?)element.Attribute("right") ?? string.Empty),
                    new DateTime((long?)element.Attribute("validFrom")
                        ?? throw new FormatException("A grant is missing validFrom."), DateTimeKind.Utc),
                    new DateTime((long?)element.Attribute("validUntil")
                        ?? throw new FormatException("A grant is missing validUntil."), DateTimeKind.Utc)));
            }

            var localized = new Dictionary<int, LocalizedNameDescriptionPair>();
            foreach (XElement element in root.Element("localized")?.Elements("entry") ?? [])
            {
                localized.Add(
                    (int?)element.Attribute("language")
                        ?? throw new FormatException("A localized entry is missing its language identifier."),
                    new LocalizedNameDescriptionPair(
                        (string?)element.Attribute("name") ?? string.Empty,
                        (string?)element.Attribute("description") ?? string.Empty));
            }

            var applicationData = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement element in root.Element("applicationData")?.Elements("entry") ?? [])
            {
                applicationData[(string?)element.Attribute("key") ?? string.Empty] =
                    (string?)element.Attribute("value") ?? string.Empty;
            }

            model = new LicenseModel
            {
                Kind = Enum.Parse<LicenseKind>((string?)root.Attribute("kind") ?? string.Empty),
                ContentId = Guid.Parse((string?)root.Attribute("contentId") ?? string.Empty),
                Owner = ReadUser(root.Element("owner")),
                ReferralInfoName = (string?)root.Element("referralName"),
                ReferralInfoUri = ParseUri((string?)root.Element("referralUri")),
                UseLicenseAcquisitionUrl = ParseUri((string?)root.Element("acquisitionUri")),
                Key = root.Element("key") is XElement keyElement
                    ? Convert.FromBase64String(keyElement.Value)
                    : [],
                Grants = grants,
                LocalizedNames = localized,
                ApplicationData = applicationData,
            };
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            throw new RightsManagementException(
                RightsManagementFailureCode.InvalidLicense,
                "The license text is malformed.",
                ex);
        }
    }

    public static LicenseModel CreateFallback(string text, LicenseKind kind)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte[] digest = SHA256.HashData(bytes);
        Span<byte> guidBytes = stackalloc byte[16];
        digest.AsSpan(0, 16).CopyTo(guidBytes);
        return new LicenseModel
        {
            Kind = kind,
            ContentId = new Guid(guidBytes),
            Key = digest,
            Owner = ContentUser.OwnerUser,
            Grants = [new ContentGrant(ContentUser.AnyoneUser, ContentRight.View)],
        };
    }

    private static XElement WriteUser(string elementName, ContentUser user) =>
        new(elementName,
            new XAttribute("name", user.Name),
            new XAttribute("authentication", user.AuthenticationType));

    private static ContentUser? ReadUser(XElement? element)
    {
        if (element is null)
            return null;
        var authentication = Enum.Parse<AuthenticationType>((string?)element.Attribute("authentication") ?? string.Empty);
        string name = (string?)element.Attribute("name") ?? string.Empty;
        return authentication == AuthenticationType.Internal
            ? ContentUser.CreateInternal(name)
            : new ContentUser(name, authentication);
    }

    private static Uri? ParseUri(string? value) =>
        string.IsNullOrEmpty(value) ? null : new Uri(value, UriKind.RelativeOrAbsolute);
}

internal sealed class SensitiveKey : IDisposable
{
    public SensitiveKey(byte[] bytes)
    {
        Bytes = bytes;
    }

    public byte[] Bytes { get; }

    public void Dispose() => CryptographicOperations.ZeroMemory(Bytes);
}
