using System.Net;
using System.Net.Cache;
using System.Runtime.CompilerServices;

namespace System.IO.Packaging;

public static class PackageStore
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<Uri, Package> s_packages = [];

    public static Package? GetPackage(Uri uri)
    {
        ValidateUri(uri);
        lock (s_gate)
            return s_packages.GetValueOrDefault(uri);
    }

    public static void RemovePackage(Uri uri)
    {
        ValidateUri(uri);
        lock (s_gate)
            s_packages.Remove(uri);
    }

    public static void AddPackage(Uri uri, Package package)
    {
        ValidateUri(uri);
        ArgumentNullException.ThrowIfNull(package);
        lock (s_gate)
        {
            if (!s_packages.TryAdd(uri, package))
                throw new ArgumentException($"A package is already registered for '{uri}'.", nameof(uri));
        }
    }

    private static void ValidateUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute. Relative URIs are not supported.", nameof(uri));
    }
}

#pragma warning disable SYSLIB0014
public sealed class PackWebRequestFactory : IWebRequestCreate
{
    public PackWebRequestFactory()
    {
    }

    WebRequest IWebRequestCreate.Create(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new PackWebRequest(uri);
    }
}

public sealed class PackWebRequest : WebRequest
{
    private readonly Uri _requestUri;
    private WebRequest? _innerRequest;
    private RequestCachePolicy? _cachePolicy;
    private string? _connectionGroupName;
    private long _contentLength;
    private string? _contentType;
    private ICredentials? _credentials;
    private WebHeaderCollection _headers = new();
    private string _method = WebRequestMethods.File.DownloadFile;
    private bool _preAuthenticate;
    private IWebProxy? _proxy;
    private int _timeout = System.Threading.Timeout.Infinite;
    private bool _useDefaultCredentials;

    internal PackWebRequest(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!requestUri.IsAbsoluteUri || !string.Equals(requestUri.Scheme, PackUriHelper.UriSchemePack, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The request URI must be an absolute pack URI.", nameof(requestUri));
        _requestUri = requestUri;
    }

    public override RequestCachePolicy? CachePolicy
    {
        get => _innerRequest?.CachePolicy ?? _cachePolicy;
        set
        {
            _cachePolicy = value;
            if (_innerRequest is not null)
                _innerRequest.CachePolicy = value;
        }
    }

    public override string? ConnectionGroupName
    {
        get => _innerRequest?.ConnectionGroupName ?? _connectionGroupName;
        set
        {
            _connectionGroupName = value;
            if (_innerRequest is not null)
                _innerRequest.ConnectionGroupName = value;
        }
    }

    public override long ContentLength
    {
        get => _innerRequest?.ContentLength ?? _contentLength;
        set
        {
            _contentLength = value;
            if (_innerRequest is not null)
                _innerRequest.ContentLength = value;
        }
    }

    public override string? ContentType
    {
        get => _innerRequest?.ContentType ?? _contentType;
        set
        {
            _contentType = value;
            if (_innerRequest is not null)
                _innerRequest.ContentType = value;
        }
    }

    public override ICredentials? Credentials
    {
        get => _innerRequest?.Credentials ?? _credentials;
        set
        {
            _credentials = value;
            if (_innerRequest is not null)
                _innerRequest.Credentials = value!;
        }
    }

    public override WebHeaderCollection Headers
    {
        get => _innerRequest?.Headers ?? _headers;
        set
        {
            _headers = value ?? throw new ArgumentNullException(nameof(value));
            if (_innerRequest is not null)
                _innerRequest.Headers = value;
        }
    }

    public override string Method
    {
        get => _innerRequest?.Method ?? _method;
        set
        {
            _method = value ?? throw new ArgumentNullException(nameof(value));
            if (_innerRequest is not null)
                _innerRequest.Method = value;
        }
    }

    public override bool PreAuthenticate
    {
        get => _innerRequest?.PreAuthenticate ?? _preAuthenticate;
        set
        {
            _preAuthenticate = value;
            if (_innerRequest is not null)
                _innerRequest.PreAuthenticate = value;
        }
    }

    public override IWebProxy? Proxy
    {
        get => _innerRequest?.Proxy ?? _proxy;
        set
        {
            _proxy = value;
            if (_innerRequest is not null)
                _innerRequest.Proxy = value;
        }
    }

    public override Uri RequestUri => _requestUri;

    public override int Timeout
    {
        get => _innerRequest?.Timeout ?? _timeout;
        set
        {
            _timeout = value;
            if (_innerRequest is not null)
                _innerRequest.Timeout = value;
        }
    }

    public override bool UseDefaultCredentials
    {
        get => _innerRequest?.UseDefaultCredentials ?? _useDefaultCredentials;
        set
        {
            _useDefaultCredentials = value;
            if (_innerRequest is not null)
                _innerRequest.UseDefaultCredentials = value;
        }
    }

    public override Stream GetRequestStream()
    {
        Uri packageUri = PackUriHelper.GetPackageUri(_requestUri);
        Uri? partUri = PackUriHelper.GetPartUri(_requestUri);
        Package? package = PackageStore.GetPackage(packageUri);
        if (package is null || partUri is null)
            return GetInnerRequest().GetRequestStream();

        PackagePart part = package.PartExists(partUri)
            ? package.GetPart(partUri)
            : package.CreatePart(partUri, ContentType ?? "application/octet-stream");
        return part.GetStream(FileMode.OpenOrCreate, FileAccess.Write);
    }

    public override WebResponse GetResponse()
    {
        Uri packageUri = PackUriHelper.GetPackageUri(_requestUri);
        Uri? partUri = PackUriHelper.GetPartUri(_requestUri);
        Package? package = PackageStore.GetPackage(packageUri);
        WebResponse? innerResponse = null;
        Stream? ownedPackageStream = null;
        bool ownsPackage = false;

        if (package is null)
        {
            innerResponse = GetInnerRequest().GetResponse();
            if (partUri is null)
                return new PackWebResponse(_requestUri, innerResponse, innerResponse.GetResponseStream(), null, false, innerResponse.ContentType);

            ownedPackageStream = new MemoryStream();
            using (Stream responseStream = innerResponse.GetResponseStream())
                responseStream.CopyTo(ownedPackageStream);
            ownedPackageStream.Position = 0;
            package = Package.Open(ownedPackageStream, FileMode.Open, FileAccess.Read);
            ownsPackage = true;
        }

        if (partUri is null)
            throw new InvalidOperationException("A package-store request must identify a package part.");
        if (!package.PartExists(partUri))
            throw new WebException($"The package part '{partUri}' was not found.", WebExceptionStatus.ProtocolError);

        PackagePart part = package.GetPart(partUri);
        Stream partStream = part.GetStream(FileMode.Open, FileAccess.Read);
        return new PackWebResponse(
            _requestUri,
            innerResponse,
            partStream,
            ownsPackage ? package : null,
            ownsPackage,
            part.ContentType,
            ownedPackageStream);
    }

    public WebRequest GetInnerRequest()
    {
        if (_innerRequest is not null)
            return _innerRequest;

        Uri packageUri = PackUriHelper.GetPackageUri(_requestUri);
        _innerRequest = WebRequest.Create(packageUri);
        _innerRequest.CachePolicy = _cachePolicy;
        _innerRequest.ConnectionGroupName = _connectionGroupName;
        _innerRequest.ContentLength = _contentLength;
        _innerRequest.ContentType = _contentType;
        _innerRequest.Credentials = _credentials!;
        _innerRequest.Headers = _headers;
        _innerRequest.Method = _method;
        _innerRequest.PreAuthenticate = _preAuthenticate;
        _innerRequest.Proxy = _proxy;
        _innerRequest.Timeout = _timeout;
        _innerRequest.UseDefaultCredentials = _useDefaultCredentials;
        return _innerRequest;
    }
}

public sealed class PackWebResponse : WebResponse
{
    private readonly Uri _responseUri;
    private readonly WebResponse? _innerResponse;
    private readonly Stream _responseStream;
    private readonly Package? _ownedPackage;
    private readonly Stream? _ownedPackageStream;
    private readonly string? _contentType;
    private readonly long _contentLength;
    private bool _closed;

    internal PackWebResponse(
        Uri responseUri,
        WebResponse? innerResponse,
        Stream responseStream,
        Package? ownedPackage,
        bool ownsPackage,
        string? contentType,
        Stream? ownedPackageStream = null)
    {
        _responseUri = responseUri;
        _innerResponse = innerResponse;
        _responseStream = responseStream ?? throw new ArgumentNullException(nameof(responseStream));
        _ownedPackage = ownsPackage ? ownedPackage : null;
        _ownedPackageStream = ownedPackageStream;
        _contentType = contentType;
        _contentLength = responseStream.CanSeek ? responseStream.Length : innerResponse?.ContentLength ?? -1;
    }

    public WebResponse? InnerResponse => _innerResponse;

    public override WebHeaderCollection Headers => _innerResponse?.Headers ?? new WebHeaderCollection();

    public override Uri ResponseUri => _responseUri;

    public override bool IsFromCache => _innerResponse?.IsFromCache ?? false;

    public override string ContentType => _contentType ?? _innerResponse?.ContentType ?? string.Empty;

    public override long ContentLength => _contentLength;

    public override Stream GetResponseStream()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _responseStream;
    }

    public override void Close()
    {
        if (_closed)
            return;
        _closed = true;
        _responseStream.Dispose();
        _ownedPackage?.Close();
        _ownedPackageStream?.Dispose();
        _innerResponse?.Close();
        base.Close();
    }
}

internal static class PackWebRequestRegistration
{
#pragma warning disable CA2255 // A library initializer is required so WebRequest recognizes pack: URIs as soon as Jalium.UI.Core loads.
    [ModuleInitializer]
    internal static void Register()
    {
        try
        {
            WebRequest.RegisterPrefix(PackUriHelper.UriSchemePack, new PackWebRequestFactory());
        }
        catch (InvalidOperationException)
        {
            // Another compatible presentation assembly already registered the prefix.
        }
    }
#pragma warning restore CA2255
}
#pragma warning restore SYSLIB0014
