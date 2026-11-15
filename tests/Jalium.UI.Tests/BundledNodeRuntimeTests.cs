using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Jalium.UI.Tooling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class BundledNodeRuntimeTests
{
    [Fact]
    public void ExistingRuntimeIsPreserved()
    {
        using var fixture = new RuntimeFixture();
        File.WriteAllBytes(fixture.Executable, fixture.Payload);
        Assert.Equal(fixture.Executable, fixture.Resolve());
    }

    [Fact]
    public void CompressedRuntimeIsVerifiedAndExtracted()
    {
        using var fixture = new RuntimeFixture();
        string cached = fixture.Resolve();
        Assert.NotEqual(fixture.Executable, cached);
        Assert.Equal(fixture.Payload, File.ReadAllBytes(cached));
        Assert.Equal(cached, fixture.Resolve());
    }

    [Fact]
    public void IncorrectChecksumRejectsRuntime()
    {
        using var fixture = new RuntimeFixture();
        File.WriteAllText(fixture.Executable + ".sha256", new string('0', 64));
        Assert.Throws<InvalidDataException>(() => fixture.Resolve());
        Assert.Empty(Directory.GetFiles(fixture.Cache, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void InvalidChecksumIsRejected()
    {
        using var fixture = new RuntimeFixture();
        File.WriteAllText(fixture.Executable + ".sha256", "invalid");
        Assert.Throws<InvalidDataException>(() => fixture.Resolve());
    }

    [Fact]
    public void DamagedArchiveIsRejected()
    {
        using var fixture = new RuntimeFixture();
        File.WriteAllBytes(fixture.Executable + ".br", [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => fixture.Resolve());
    }

    [Fact]
    public void DamagedCacheIsRebuiltFromVerifiedPayload()
    {
        using var fixture = new RuntimeFixture();
        string cached = fixture.Resolve();
        File.WriteAllText(cached, "damaged cached executable");
        Assert.Equal(cached, fixture.Resolve());
        Assert.Equal(fixture.Payload, File.ReadAllBytes(cached));
    }

    [Fact]
    public async Task ConcurrentBuildsShareVerifiedRuntime()
    {
        using var fixture = new RuntimeFixture();
        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(fixture.Resolve)));
        Assert.Single(paths.Distinct());
        Assert.Equal(fixture.Payload, File.ReadAllBytes(paths[0]));
        Assert.Empty(Directory.GetFiles(fixture.Cache, "*.tmp", SearchOption.AllDirectories));
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "jalium-bundled-node-" + Guid.NewGuid().ToString("N"));
        internal byte[] Payload { get; } = Encoding.UTF8.GetBytes("A verified offline Node runtime fixture.");
        internal string Executable => Path.Combine(_root, "node");
        internal string Cache => Path.Combine(_root, "cache");

        internal RuntimeFixture()
        {
            Directory.CreateDirectory(_root);
            using (var output = File.Create(Executable + ".br"))
            using (var compressed = new BrotliStream(output, CompressionLevel.SmallestSize))
                compressed.Write(Payload);
            File.WriteAllText(Executable + ".sha256", Convert.ToHexString(SHA256.HashData(Payload)));
        }

        internal string Resolve() => BundledNodeRuntime.Resolve(Executable, Cache);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
