using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.RightsManagement;
using System.Text;
using System.IO.Packaging;

namespace Jalium.UI.Tests;

public sealed class PackagingRightsManagementWpfParityTests
{
    [Fact]
    public void MissingTypesUseCanonicalNamespacesAndWpfShapes()
    {
        Type[] packagingTypes =
        [
            typeof(CertificateEmbeddingOption),
            typeof(EncryptedPackageEnvelope),
            typeof(PackWebRequest),
            typeof(PackWebRequestFactory),
            typeof(PackWebResponse),
            typeof(PackageDigitalSignature),
            typeof(PackageDigitalSignatureManager),
            typeof(PackageStore),
            typeof(RightsManagementInformation),
            typeof(SignatureVerificationEventArgs),
            typeof(StorageInfo),
            typeof(StreamInfo),
            typeof(VerifyResult),
            typeof(InvalidSignatureEventHandler),
        ];
        Type[] rightsTypes =
        [
            typeof(AuthenticationType),
            typeof(ContentGrant),
            typeof(ContentRight),
            typeof(ContentUser),
            typeof(CryptoProvider),
            typeof(LocalizedNameDescriptionPair),
            typeof(PublishLicense),
            typeof(RightsManagementException),
            typeof(RightsManagementFailureCode),
            typeof(SecureEnvironment),
            typeof(UnsignedPublishLicense),
            typeof(UseLicense),
            typeof(UserActivationMode),
        ];

        Assert.All(packagingTypes, type => Assert.Equal("System.IO.Packaging", type.Namespace));
        Assert.All(rightsTypes, type => Assert.Equal("System.Security.RightsManagement", type.Namespace));
        Assert.True(typeof(PackWebRequest).IsSealed);
        Assert.True(typeof(PackWebRequestFactory).IsSealed);
        Assert.True(typeof(PackWebResponse).IsSealed);
        Assert.True(typeof(PackageDigitalSignatureManager).IsSealed);
        Assert.True(typeof(PackageStore).IsAbstract && typeof(PackageStore).IsSealed);
        Assert.Contains(typeof(IDisposable), typeof(EncryptedPackageEnvelope).GetInterfaces());
        Assert.Contains(typeof(IDisposable), typeof(CryptoProvider).GetInterfaces());
        Assert.Contains(typeof(IDisposable), typeof(SecureEnvironment).GetInterfaces());
        Assert.Equal(typeof(System.Net.WebRequest), typeof(PackWebRequest).BaseType);
        Assert.Equal(typeof(System.Net.WebResponse), typeof(PackWebResponse).BaseType);
    }

    [Fact]
    public void PublicSignaturesRetainWpfParameterNames()
    {
        AssertParameterNames(
            typeof(ContentGrant).GetConstructor([typeof(ContentUser), typeof(ContentRight), typeof(DateTime), typeof(DateTime)])!,
            "user", "right", "validFrom", "validUntil");
        AssertParameterNames(
            typeof(UnsignedPublishLicense).GetMethod(nameof(UnsignedPublishLicense.Sign))!,
            "secureEnvironment", "authorUseLicense");
        AssertParameterNames(
            typeof(PackageDigitalSignatureManager).GetMethod(
                nameof(PackageDigitalSignatureManager.Sign),
                [
                    typeof(IEnumerable<Uri>),
                    typeof(X509Certificate),
                    typeof(IEnumerable<PackageRelationshipSelector>),
                    typeof(string),
                    typeof(IEnumerable<System.Security.Cryptography.Xml.DataObject>),
                    typeof(IEnumerable<System.Security.Cryptography.Xml.Reference>),
                ])!,
            "parts", "certificate", "relationshipSelectors", "signatureId", "signatureObjects", "objectReferences");
        AssertParameterNames(
            typeof(EncryptedPackageEnvelope).GetMethod(
                nameof(EncryptedPackageEnvelope.CreateFromPackage),
                [typeof(Stream), typeof(Stream), typeof(PublishLicense), typeof(CryptoProvider)])!,
            "envelopeStream", "packageStream", "publishLicense", "cryptoProvider");
        AssertParameterNames(
            typeof(StorageInfo).GetMethod(
                nameof(StorageInfo.CreateStream),
                [typeof(string), typeof(CompressionOption), typeof(EncryptionOption)])!,
            "name", "compressionOption", "encryptionOption");
    }

    [Fact]
    public void EnumValuesMatchWpfContracts()
    {
        Assert.Equal(2, (int)CertificateEmbeddingOption.NotEmbedded);
        Assert.Equal(5, (int)VerifyResult.NotSigned);
        Assert.Equal(3, (int)AuthenticationType.Internal);
        Assert.Equal(12, (int)ContentRight.Export);
        Assert.Equal(1, (int)UserActivationMode.Temporary);
        Assert.Equal(-2147168512, (int)RightsManagementFailureCode.InvalidLicense);
        Assert.Equal(-2147168395, (int)RightsManagementFailureCode.OwnerLicenseNotFound);
    }

    [Fact]
    public void ManagedRightsLicensesSignBindEncryptAndRoundTrip()
    {
        var user = new ContentUser("alice@example.test", AuthenticationType.Windows);
        using SecureEnvironment environment = SecureEnvironment.Create("<manifest />", user);
        var unsignedLicense = new UnsignedPublishLicense
        {
            Owner = user,
            ReferralInfoName = "Help desk",
            ReferralInfoUri = new Uri("https://example.test/rights"),
        };
        unsignedLicense.Grants.Add(new ContentGrant(user, ContentRight.Edit));

        PublishLicense publishLicense = unsignedLicense.Sign(environment, out UseLicense authorUseLicense);
        using CryptoProvider provider = authorUseLicense.Bind(environment);
        byte[] clearText = Encoding.UTF8.GetBytes("managed rights content");
        byte[] encrypted = provider.Encrypt(clearText);

        Assert.True(provider.CanEncrypt);
        Assert.True(provider.CanDecrypt);
        Assert.Equal(clearText, provider.Decrypt(encrypted));
        Assert.Equal(unsignedLicense.ContentId, publishLicense.ContentId);
        Assert.Equal(authorUseLicense, new UseLicense(authorUseLicense.ToString()));
        Assert.Equal(user, new ContentUser("ALICE@EXAMPLE.TEST", AuthenticationType.Windows));
    }

    [Fact]
    public void EncryptedEnvelopePersistsPackageStorageAndEmbeddedLicense()
    {
        var user = new ContentUser("envelope-user", AuthenticationType.Windows);
        using SecureEnvironment environment = SecureEnvironment.Create("<manifest />", user);
        var unsignedLicense = new UnsignedPublishLicense { Owner = user };
        unsignedLicense.Grants.Add(new ContentGrant(user, ContentRight.Edit));
        PublishLicense publishLicense = unsignedLicense.Sign(environment, out UseLicense authorUseLicense);
        using CryptoProvider provider = authorUseLicense.Bind(environment);
        using var envelopeStream = new MemoryStream();

        using (EncryptedPackageEnvelope envelope = EncryptedPackageEnvelope.Create(
            envelopeStream,
            publishLicense,
            provider))
        {
            Uri partUri = PackUriHelper.CreatePartUri(new Uri("/document.txt", UriKind.Relative));
            PackagePart part = envelope.GetPackage().CreatePart(partUri, "text/plain");
            using (var writer = new StreamWriter(part.GetStream(FileMode.Create, FileAccess.Write), Encoding.UTF8, leaveOpen: false))
                writer.Write("inside package");

            StreamInfo streamInfo = envelope.StorageInfo.CreateStream("metadata");
            using (var writer = new StreamWriter(streamInfo.GetStream(FileMode.Create, FileAccess.Write), Encoding.UTF8, leaveOpen: false))
                writer.Write("custom storage");
            envelope.RightsManagementInformation.SaveUseLicense(user, authorUseLicense);
            envelope.Flush();
        }

        envelopeStream.Position = 0;
        Assert.True(EncryptedPackageEnvelope.IsEncryptedPackageEnvelope(envelopeStream));
        envelopeStream.Position = 0;
        using EncryptedPackageEnvelope reopened = EncryptedPackageEnvelope.Open(envelopeStream);
        Assert.Equal(publishLicense.ContentId, reopened.RightsManagementInformation.LoadPublishLicense().ContentId);
        reopened.RightsManagementInformation.CryptoProvider = provider;
        Uri reopenedPartUri = PackUriHelper.CreatePartUri(new Uri("/document.txt", UriKind.Relative));
        using var reader = new StreamReader(
            reopened.GetPackage().GetPart(reopenedPartUri).GetStream(FileMode.Open, FileAccess.Read),
            Encoding.UTF8);
        Assert.Equal("inside package", reader.ReadToEnd());
        Assert.True(reopened.StorageInfo.StreamExists("metadata"));
        Assert.True(reopened.RightsManagementInformation.GetEmbeddedUseLicenses().ContainsKey(user));
    }

    [Fact]
    public void PackageStoreAndPackWebRequestReturnRegisteredPart()
    {
        using var packageStream = new MemoryStream();
        using Package package = Package.Open(packageStream, FileMode.Create, FileAccess.ReadWrite);
        Uri partUri = PackUriHelper.CreatePartUri(new Uri("/asset.txt", UriKind.Relative));
        PackagePart part = package.CreatePart(partUri, "text/plain");
        using (var writer = new StreamWriter(part.GetStream(FileMode.Create, FileAccess.Write), Encoding.UTF8, leaveOpen: false))
            writer.Write("pack response");
        package.Flush();

        var packageUri = new Uri("file:///virtual/pack-store.zip");
        PackageStore.AddPackage(packageUri, package);
        try
        {
            Uri requestUri = PackUriHelper.Create(packageUri, partUri);
            var request = Assert.IsType<PackWebRequest>(((System.Net.IWebRequestCreate)new PackWebRequestFactory()).Create(requestUri));
            using System.Net.WebResponse response = request.GetResponse();
            using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
            Assert.Equal("pack response", reader.ReadToEnd());
            Assert.Equal("text/plain", response.ContentType);
        }
        finally
        {
            PackageStore.RemovePackage(packageUri);
        }
    }

    [Fact]
    public void PackageSignatureDetectsPartMutationAndCanBeRemoved()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Jalium parity", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        using var packageStream = new MemoryStream();
        using Package package = Package.Open(packageStream, FileMode.Create, FileAccess.ReadWrite);
        Uri partUri = PackUriHelper.CreatePartUri(new Uri("/signed.txt", UriKind.Relative));
        PackagePart part = package.CreatePart(partUri, "text/plain");
        using (var writer = new StreamWriter(part.GetStream(FileMode.Create, FileAccess.Write), Encoding.UTF8, leaveOpen: false))
            writer.Write("original");

        var manager = new PackageDigitalSignatureManager(package)
        {
            CertificateOption = CertificateEmbeddingOption.InSignaturePart,
            HashAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256",
        };
        PackageDigitalSignature signature = manager.Sign([partUri], certificate);
        Assert.Equal(VerifyResult.Success, signature.Verify());
        Assert.True(manager.IsSigned);

        using (var writer = new StreamWriter(part.GetStream(FileMode.Create, FileAccess.Write), Encoding.UTF8, leaveOpen: false))
            writer.Write("changed");
        Assert.Equal(VerifyResult.InvalidSignature, signature.Verify());

        SignatureVerificationEventArgs? invalid = null;
        manager.InvalidSignatureEvent += (_, e) => invalid = e;
        Assert.Equal(VerifyResult.InvalidSignature, manager.VerifySignatures(exitOnFailure: true));
        Assert.Same(signature.SignaturePart.Uri, invalid?.Signature.SignaturePart.Uri);
        manager.RemoveSignature(signature.SignaturePart.Uri);
        Assert.False(manager.IsSigned);
    }

    private static void AssertParameterNames(MethodBase method, params string[] expected) =>
        Assert.Equal(expected, method.GetParameters().Select(static parameter => parameter.Name));
}
