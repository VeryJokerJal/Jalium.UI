using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace System.IO.Packaging;

public enum CertificateEmbeddingOption
{
    InCertificatePart = 0,
    InSignaturePart = 1,
    NotEmbedded = 2,
}

public enum VerifyResult
{
    Success = 0,
    InvalidSignature = 1,
    CertificateRequired = 2,
    InvalidCertificate = 3,
    ReferenceNotFound = 4,
    NotSigned = 5,
}

public delegate void InvalidSignatureEventHandler(object sender, SignatureVerificationEventArgs e);

public class SignatureVerificationEventArgs : EventArgs
{
    internal SignatureVerificationEventArgs(PackageDigitalSignature signature, VerifyResult verifyResult)
    {
        Signature = signature;
        VerifyResult = verifyResult;
    }

    public PackageDigitalSignature Signature { get; }

    public VerifyResult VerifyResult { get; }
}

public class PackageDigitalSignature
{
    private static readonly XNamespace s_dsigNamespace = SignedXml.XmlDsigNamespaceUrl;
    private readonly PackageDigitalSignatureManager _manager;
    private readonly PackagePart _signaturePart;

    internal PackageDigitalSignature(PackageDigitalSignatureManager manager, PackagePart signaturePart)
    {
        _manager = manager;
        _signaturePart = signaturePart;
    }

    public ReadOnlyCollection<Uri> SignedParts =>
        Array.AsReadOnly(ReadDocument().Root!
            .Element("signedParts")?
            .Elements("part")
            .Select(static element => new Uri((string)element.Attribute("uri")!, UriKind.Relative))
            .ToArray() ?? []);

    public ReadOnlyCollection<PackageRelationshipSelector> SignedRelationshipSelectors =>
        Array.AsReadOnly(ReadDocument().Root!
            .Element("relationshipSelectors")?
            .Elements("selector")
            .Select(static element => new PackageRelationshipSelector(
                new Uri((string)element.Attribute("sourceUri")!, UriKind.Relative),
                Enum.Parse<PackageRelationshipSelectorType>((string)element.Attribute("selectorType")!),
                (string)element.Attribute("selectionCriteria")!))
            .ToArray() ?? []);

    public PackagePart SignaturePart => _signaturePart;

    public X509Certificate? Signer
    {
        get
        {
            XDocument document = ReadDocument();
            XElement? embedded = document.Root?.Element("certificate");
            if (embedded is not null && !string.IsNullOrWhiteSpace(embedded.Value))
                return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(embedded.Value));

            string? partUriText = (string?)document.Root?.Element("certificatePart")?.Attribute("uri");
            if (partUriText is null)
                return null;
            var partUri = new Uri(partUriText, UriKind.Relative);
            if (!_manager.Package.PartExists(partUri))
                return null;
            using Stream stream = _manager.Package.GetPart(partUri).GetStream(FileMode.Open, FileAccess.Read);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return X509CertificateLoader.LoadCertificate(buffer.ToArray());
        }
    }

    public DateTime SigningTime => DateTime.Parse(
        (string?)ReadDocument().Root?.Attribute("signingTime") ?? string.Empty,
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind);

    public string TimeFormat => (string?)ReadDocument().Root?.Attribute("timeFormat") ?? string.Empty;

    public byte[] SignatureValue => Signature.SignatureValue is { } value ? (byte[])value.Clone() : [];

    public string SignatureType => Signature.SignedInfo?.SignatureMethod ?? string.Empty;

    public Signature Signature
    {
        get
        {
            XDocument document = ReadDocument();
            XElement signatureElement = document.Root?.Element(s_dsigNamespace + "Signature")
                ?? throw new CryptographicException("The package signature does not contain an XML signature.");
            var xmlDocument = new XmlDocument { PreserveWhitespace = true };
            xmlDocument.LoadXml(document.ToString(SaveOptions.DisableFormatting));
            SignedXml signedXml = PackageSignedXmlFactory.Create(xmlDocument);
            signedXml.LoadXml((XmlElement)xmlDocument.ImportNode(ToXmlElement(signatureElement), deep: true));
            return signedXml.Signature;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            XDocument document = ReadDocument();
            XElement? oldSignature = document.Root?.Element(s_dsigNamespace + "Signature");
            oldSignature?.ReplaceWith(XElement.Parse(value.GetXml().OuterXml, LoadOptions.PreserveWhitespace));
            WriteDocument(document);
        }
    }

    public List<string> GetPartTransformList(Uri partName)
    {
        ArgumentNullException.ThrowIfNull(partName);
        XElement? part = ReadDocument().Root?
            .Element("signedParts")?
            .Elements("part")
            .FirstOrDefault(element => UriEquals((string?)element.Attribute("uri"), partName));
        string? transform = (string?)part?.Attribute("transform");
        return string.IsNullOrEmpty(transform) ? [] : [transform];
    }

    public VerifyResult Verify()
    {
        X509Certificate? signer = Signer;
        return signer is null ? VerifyResult.CertificateRequired : Verify(signer);
    }

    public VerifyResult Verify(X509Certificate signingCertificate)
    {
        ArgumentNullException.ThrowIfNull(signingCertificate);
        XDocument document;
        try
        {
            document = ReadDocument();
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            return VerifyResult.InvalidSignature;
        }

        XElement? signatureElement = document.Root?.Element(s_dsigNamespace + "Signature");
        if (signatureElement is null)
            return VerifyResult.InvalidSignature;

        try
        {
            var xmlDocument = new XmlDocument { PreserveWhitespace = true };
            xmlDocument.LoadXml(document.ToString(SaveOptions.DisableFormatting));
            SignedXml signedXml = PackageSignedXmlFactory.Create(xmlDocument);
            signedXml.LoadXml((XmlElement)xmlDocument.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);
            X509Certificate2 certificate = ToCertificate2(signingCertificate);
            if (!signedXml.CheckSignature(certificate, verifySignatureOnly: true))
                return VerifyResult.InvalidSignature;

            foreach (XElement part in document.Root!.Element("signedParts")?.Elements("part") ?? [])
            {
                var uri = new Uri((string)part.Attribute("uri")!, UriKind.Relative);
                if (!_manager.Package.PartExists(uri))
                    return VerifyResult.ReferenceNotFound;
                byte[] actual = PackageDigitalSignatureManager.ComputePartDigest(
                    _manager.Package.GetPart(uri),
                    (string)document.Root.Attribute("hashAlgorithm")!);
                byte[] expected = Convert.FromBase64String((string)part.Attribute("digest")!);
                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                    return VerifyResult.InvalidSignature;
            }

            foreach (XElement selector in document.Root.Element("relationshipSelectors")?.Elements("selector") ?? [])
            {
                var relationshipSelector = new PackageRelationshipSelector(
                    new Uri((string)selector.Attribute("sourceUri")!, UriKind.Relative),
                    Enum.Parse<PackageRelationshipSelectorType>((string)selector.Attribute("selectorType")!),
                    (string)selector.Attribute("selectionCriteria")!);
                byte[] actual = PackageDigitalSignatureManager.ComputeRelationshipDigest(
                    relationshipSelector,
                    _manager.Package,
                    (string)document.Root.Attribute("hashAlgorithm")!);
                byte[] expected = Convert.FromBase64String((string)selector.Attribute("digest")!);
                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                    return VerifyResult.InvalidSignature;
            }

            return VerifyResult.Success;
        }
        catch (Exception ex) when (
            ex is CryptographicException or XmlException or FormatException or InvalidOperationException)
        {
            return VerifyResult.InvalidSignature;
        }
    }

    internal Uri? GetCertificatePartUri()
    {
        string? value = (string?)ReadDocument().Root?.Element("certificatePart")?.Attribute("uri");
        return value is null ? null : new Uri(value, UriKind.Relative);
    }

    private XDocument ReadDocument()
    {
        using Stream stream = _signaturePart.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private void WriteDocument(XDocument document)
    {
        using Stream stream = _signaturePart.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
        _manager.Package.Flush();
    }

    private static XmlElement ToXmlElement(XElement element)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(element.ToString(SaveOptions.DisableFormatting));
        return document.DocumentElement!;
    }

    private static X509Certificate2 ToCertificate2(X509Certificate certificate) =>
        certificate as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

    private static bool UriEquals(string? value, Uri partName) =>
        value is not null && Uri.Compare(
            new Uri(value, UriKind.Relative),
            partName,
            UriComponents.SerializationInfoString,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;
}

public sealed class PackageDigitalSignatureManager
{
    internal const string SignatureContentType = "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml";
    internal const string CertificateContentType = "application/vnd.openxmlformats-package.digital-signature-certificate";
    private const string OriginContentType = "application/vnd.openxmlformats-package.digital-signature-origin";
    private const string SignatureRelationshipType = "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature";
    private const string CertificateRelationshipType = "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/certificate";
    private static readonly Uri s_signatureOrigin = PackUriHelper.CreatePartUri(
        new Uri("/package/services/digital-signature/origin.psdsor", UriKind.Relative));
    private readonly Package _package;
    private readonly Dictionary<string, string> _transformMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.openxmlformats-package.relationships+xml"] = SignedXml.XmlDsigC14NTransformUrl,
        [SignatureContentType] = SignedXml.XmlDsigC14NTransformUrl,
    };
    private IntPtr _parentWindow;
    private string _hashAlgorithm = DefaultHashAlgorithm;
    private CertificateEmbeddingOption _certificateOption = CertificateEmbeddingOption.InCertificatePart;
    private string _timeFormat = "YYYY-MM-DDThh:mm:ss.sTZD";

    public PackageDigitalSignatureManager(Package package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    internal Package Package => _package;

    public bool IsSigned => Signatures.Count != 0;

    public ReadOnlyCollection<PackageDigitalSignature> Signatures => Array.AsReadOnly(
        _package.GetParts()
            .Where(static part => string.Equals(part.ContentType, SignatureContentType, StringComparison.OrdinalIgnoreCase))
            .Select(part => new PackageDigitalSignature(this, part))
            .OrderBy(static signature => signature.SignaturePart.Uri.OriginalString, StringComparer.Ordinal)
            .ToArray());

    public Dictionary<string, string> TransformMapping => _transformMapping;

    public IntPtr ParentWindow
    {
        get => _parentWindow;
        set => _parentWindow = value;
    }

    public string HashAlgorithm
    {
        get => _hashAlgorithm;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _ = CreateHashAlgorithm(value);
            _hashAlgorithm = value;
        }
    }

    public CertificateEmbeddingOption CertificateOption
    {
        get => _certificateOption;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _certificateOption = value;
        }
    }

    public string TimeFormat
    {
        get => _timeFormat;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _timeFormat = value;
        }
    }

    public Uri SignatureOrigin => s_signatureOrigin;

    public static string SignatureOriginRelationshipType =>
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin";

    public static string DefaultHashAlgorithm => SignedXml.XmlDsigSHA1Url;

    public event InvalidSignatureEventHandler? InvalidSignatureEvent;

    public PackageDigitalSignature Sign(IEnumerable<Uri> parts) =>
        Sign(parts, SelectSigningCertificate());

    public PackageDigitalSignature Sign(IEnumerable<Uri> parts, X509Certificate certificate) =>
        Sign(parts, certificate, relationshipSelectors: null);

    public PackageDigitalSignature Sign(
        IEnumerable<Uri> parts,
        X509Certificate certificate,
        IEnumerable<PackageRelationshipSelector>? relationshipSelectors) =>
        Sign(parts, certificate, relationshipSelectors, "id-" + Guid.NewGuid().ToString("N"));

    public PackageDigitalSignature Sign(
        IEnumerable<Uri> parts,
        X509Certificate certificate,
        IEnumerable<PackageRelationshipSelector>? relationshipSelectors,
        string signatureId) =>
        Sign(parts, certificate, relationshipSelectors, signatureId, signatureObjects: null, objectReferences: null);

    public PackageDigitalSignature Sign(
        IEnumerable<Uri> parts,
        X509Certificate certificate,
        IEnumerable<PackageRelationshipSelector>? relationshipSelectors,
        string signatureId,
        IEnumerable<DataObject>? signatureObjects,
        IEnumerable<Reference>? objectReferences)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrEmpty(signatureId);

        Uri[] partUris = parts.Select(ValidatePartUri).Distinct(UriComparer.Instance).ToArray();
        PackageRelationshipSelector[] selectors = relationshipSelectors?.ToArray() ?? [];
        foreach (Uri partUri in partUris)
        {
            if (!_package.PartExists(partUri))
                throw new ArgumentException($"The package part '{partUri}' does not exist.", nameof(parts));
        }

        X509Certificate2 certificate2 = certificate as X509Certificate2
            ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using RSA? privateKey = certificate2.GetRSAPrivateKey();
        if (privateKey is null)
            throw new CryptographicException("The signing certificate must contain an RSA private key.");

        string safeId = string.Concat(signatureId.Select(static character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        Uri signatureUri = PackUriHelper.CreatePartUri(new Uri(
            $"/package/services/digital-signature/xml-signature/{safeId}-{Guid.NewGuid():N}.psdsxs",
            UriKind.Relative));

        DateTime signingTime = DateTime.UtcNow;
        var root = new XElement("packageSignature",
            new XAttribute("Id", signatureId),
            new XAttribute("hashAlgorithm", HashAlgorithm),
            new XAttribute("certificateOption", CertificateOption),
            new XAttribute("signingTime", signingTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            new XAttribute("timeFormat", TimeFormat));

        var signedParts = new XElement("signedParts");
        foreach (Uri partUri in partUris)
        {
            PackagePart part = _package.GetPart(partUri);
            _transformMapping.TryGetValue(part.ContentType, out string? transform);
            signedParts.Add(new XElement("part",
                new XAttribute("uri", partUri.OriginalString),
                new XAttribute("digest", Convert.ToBase64String(ComputePartDigest(part, HashAlgorithm))),
                transform is null ? null : new XAttribute("transform", transform)));
        }
        root.Add(signedParts);

        var relationshipElements = new XElement("relationshipSelectors");
        foreach (PackageRelationshipSelector selector in selectors)
        {
            relationshipElements.Add(new XElement("selector",
                new XAttribute("sourceUri", selector.SourceUri.OriginalString),
                new XAttribute("selectorType", selector.SelectorType),
                new XAttribute("selectionCriteria", selector.SelectionCriteria),
                new XAttribute("digest", Convert.ToBase64String(
                    ComputeRelationshipDigest(selector, _package, HashAlgorithm)))));
        }
        root.Add(relationshipElements);

        Uri? certificatePartUri = null;
        switch (CertificateOption)
        {
            case CertificateEmbeddingOption.InSignaturePart:
                root.Add(new XElement("certificate", Convert.ToBase64String(certificate2.Export(X509ContentType.Cert))));
                break;
            case CertificateEmbeddingOption.InCertificatePart:
                certificatePartUri = PackUriHelper.CreatePartUri(new Uri(
                    $"/package/services/digital-signature/certificate/{Guid.NewGuid():N}.cer",
                    UriKind.Relative));
                PackagePart certificatePart = _package.CreatePart(certificatePartUri, CertificateContentType, CompressionOption.NotCompressed);
                using (Stream certificateStream = certificatePart.GetStream(FileMode.Create, FileAccess.Write))
                {
                    byte[] rawCertificate = certificate2.Export(X509ContentType.Cert);
                    certificateStream.Write(rawCertificate);
                }
                root.Add(new XElement("certificatePart", new XAttribute("uri", certificatePartUri.OriginalString)));
                break;
        }

        var xmlDocument = new XmlDocument { PreserveWhitespace = true };
        xmlDocument.LoadXml(new XDocument(root).ToString(SaveOptions.DisableFormatting));
        SignedXml signedXml = PackageSignedXmlFactory.Create(xmlDocument);
        signedXml.SigningKey = privateKey;
        SignedInfo signedInfo = signedXml.SignedInfo
            ?? throw new CryptographicException("The XML signature has no SignedInfo element.");
        signedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signedInfo.SignatureMethod = HashAlgorithm == SignedXml.XmlDsigSHA1Url
            ? SignedXml.XmlDsigRSASHA1Url
            : SignedXml.XmlDsigRSASHA256Url;
        var packageReference = new Reference(string.Empty)
        {
            DigestMethod = HashAlgorithm,
        };
        packageReference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        signedXml.AddReference(packageReference);

        foreach (DataObject dataObject in signatureObjects ?? [])
            signedXml.AddObject(dataObject);
        foreach (Reference reference in objectReferences ?? [])
            signedXml.AddReference(reference);

        if (CertificateOption == CertificateEmbeddingOption.InSignaturePart)
        {
            signedXml.KeyInfo = new KeyInfo();
            signedXml.KeyInfo.AddClause(new KeyInfoX509Data(certificate2));
        }
        signedXml.ComputeSignature();
        xmlDocument.DocumentElement!.AppendChild(xmlDocument.ImportNode(signedXml.GetXml(), deep: true));

        PackagePart signaturePart = _package.CreatePart(signatureUri, SignatureContentType, CompressionOption.Normal);
        using (Stream signatureStream = signaturePart.GetStream(FileMode.Create, FileAccess.Write))
            xmlDocument.Save(signatureStream);

        PackagePart originPart = EnsureOriginPart();
        originPart.CreateRelationship(signatureUri, TargetMode.Internal, SignatureRelationshipType);
        if (certificatePartUri is not null)
            signaturePart.CreateRelationship(certificatePartUri, TargetMode.Internal, CertificateRelationshipType);
        _package.Flush();
        return new PackageDigitalSignature(this, signaturePart);
    }

    public PackageDigitalSignature Countersign() => Countersign(SelectSigningCertificate());

    public PackageDigitalSignature Countersign(X509Certificate certificate) =>
        Countersign(certificate, Signatures.Select(static signature => signature.SignaturePart.Uri));

    public PackageDigitalSignature Countersign(X509Certificate certificate, IEnumerable<Uri> signatures)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(signatures);
        return Sign(signatures, certificate);
    }

    public VerifyResult VerifySignatures(bool exitOnFailure)
    {
        ReadOnlyCollection<PackageDigitalSignature> signatures = Signatures;
        if (signatures.Count == 0)
            return VerifyResult.NotSigned;

        VerifyResult aggregate = VerifyResult.Success;
        foreach (PackageDigitalSignature signature in signatures)
        {
            VerifyResult result = signature.Verify();
            if (result == VerifyResult.Success)
                continue;

            aggregate = result;
            InvalidSignatureEvent?.Invoke(this, new SignatureVerificationEventArgs(signature, result));
            if (exitOnFailure)
                break;
        }
        return aggregate;
    }

    public void RemoveSignature(Uri signatureUri)
    {
        ArgumentNullException.ThrowIfNull(signatureUri);
        PackageDigitalSignature signature = GetSignature(signatureUri);
        Uri? certificatePartUri = signature.GetCertificatePartUri();
        if (_package.PartExists(s_signatureOrigin))
        {
            PackagePart origin = _package.GetPart(s_signatureOrigin);
            foreach (PackageRelationship relationship in origin.GetRelationshipsByType(SignatureRelationshipType).ToArray())
            {
                Uri target = PackUriHelper.ResolvePartUri(s_signatureOrigin, relationship.TargetUri);
                if (UriComparer.Instance.Equals(target, signature.SignaturePart.Uri))
                    origin.DeleteRelationship(relationship.Id);
            }
        }

        _package.DeletePart(signature.SignaturePart.Uri);
        if (certificatePartUri is not null && _package.PartExists(certificatePartUri))
            _package.DeletePart(certificatePartUri);
        RemoveOriginIfEmpty();
        _package.Flush();
    }

    public void RemoveAllSignatures()
    {
        foreach (Uri uri in Signatures.Select(static signature => signature.SignaturePart.Uri).ToArray())
            RemoveSignature(uri);
    }

    public PackageDigitalSignature GetSignature(Uri signatureUri)
    {
        ArgumentNullException.ThrowIfNull(signatureUri);
        return Signatures.FirstOrDefault(signature => UriComparer.Instance.Equals(signature.SignaturePart.Uri, signatureUri))
            ?? throw new ArgumentException($"The signature '{signatureUri}' was not found.", nameof(signatureUri));
    }

    public static X509ChainStatusFlags VerifyCertificate(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        X509Certificate2 certificate2 = certificate as X509Certificate2
            ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        if (chain.Build(certificate2))
            return X509ChainStatusFlags.NoError;
        X509ChainStatusFlags result = X509ChainStatusFlags.NoError;
        foreach (X509ChainStatus status in chain.ChainStatus)
            result |= status.Status;
        return result;
    }

    internal static byte[] ComputePartDigest(PackagePart part, string hashAlgorithm)
    {
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using HashAlgorithm algorithm = CreateHashAlgorithm(hashAlgorithm);
        return algorithm.ComputeHash(stream);
    }

    internal static byte[] ComputeRelationshipDigest(
        PackageRelationshipSelector selector,
        Package package,
        string hashAlgorithm)
    {
        string canonical = string.Join("\n", selector.Select(package)
            .OrderBy(static relationship => relationship.Id, StringComparer.Ordinal)
            .Select(static relationship => string.Join("|",
                relationship.Id,
                relationship.RelationshipType,
                relationship.TargetMode,
                relationship.TargetUri.OriginalString)));
        using HashAlgorithm algorithm = CreateHashAlgorithm(hashAlgorithm);
        return algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonical));
    }

    private PackagePart EnsureOriginPart()
    {
        PackagePart origin;
        if (_package.PartExists(s_signatureOrigin))
        {
            origin = _package.GetPart(s_signatureOrigin);
        }
        else
        {
            origin = _package.CreatePart(s_signatureOrigin, OriginContentType, CompressionOption.NotCompressed);
            _package.CreateRelationship(s_signatureOrigin, TargetMode.Internal, SignatureOriginRelationshipType);
        }
        return origin;
    }

    private void RemoveOriginIfEmpty()
    {
        if (!_package.PartExists(s_signatureOrigin))
            return;
        PackagePart origin = _package.GetPart(s_signatureOrigin);
        if (origin.GetRelationshipsByType(SignatureRelationshipType).Any())
            return;

        foreach (PackageRelationship relationship in _package.GetRelationshipsByType(SignatureOriginRelationshipType).ToArray())
            _package.DeleteRelationship(relationship.Id);
        _package.DeletePart(s_signatureOrigin);
    }

    private static Uri ValidatePartUri(Uri partUri)
    {
        ArgumentNullException.ThrowIfNull(partUri);
        return PackUriHelper.CreatePartUri(partUri);
    }

    private static HashAlgorithm CreateHashAlgorithm(string algorithmUri) => algorithmUri switch
    {
        SignedXml.XmlDsigSHA1Url => SHA1.Create(),
        SignedXml.XmlDsigSHA256Url => SHA256.Create(),
        SignedXml.XmlDsigSHA384Url => SHA384.Create(),
        SignedXml.XmlDsigSHA512Url => SHA512.Create(),
        _ => throw new ArgumentException($"The hash algorithm '{algorithmUri}' is not supported.", nameof(algorithmUri)),
    };

    private static X509Certificate SelectSigningCertificate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2? certificate = store.Certificates
            .OfType<X509Certificate2>()
            .FirstOrDefault(static candidate => candidate.HasPrivateKey && candidate.GetRSAPrivateKey() is not null);
        return certificate ?? throw new InvalidOperationException(
            "No RSA signing certificate with a private key is available in the current-user certificate store.");
    }

    private sealed class UriComparer : IEqualityComparer<Uri>
    {
        public static UriComparer Instance { get; } = new();

        public bool Equals(Uri? x, Uri? y) => x is not null && y is not null && Uri.Compare(
            x,
            y,
            UriComponents.SerializationInfoString,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;

        public int GetHashCode(Uri obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.GetComponents(
                UriComponents.SerializationInfoString,
                UriFormat.UriEscaped));
    }
}

internal static class PackageSignedXmlFactory
{
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The document overload is required for same-document package references; this implementation statically references every built-in XMLDSIG transform it emits.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Package signatures are restricted to the statically referenced built-in XMLDSIG algorithms and transforms used by this implementation.")]
    internal static SignedXml Create(XmlDocument document) => new(document);
}
