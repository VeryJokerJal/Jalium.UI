using Microsoft.Extensions.Hosting;

namespace Jalium.UI.Tests;

public sealed class HostedApplicationFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoryFailureReleasesTheHostAndPreservesTheOriginalError(bool disposeThrows)
    {
        var original = new InvalidOperationException("application factory");
        var previous = Application.Current;
        var host = new Host { DisposeThrows = disposeThrows };
        var app = new JaliumApp(host, [], []).UseApplication(() => throw original);
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => app.StartHosted()));
        Assert.Equal(1, host.DisposeCount);
        Assert.Equal(0, host.StartCount);
        Assert.Equal(0, host.StopCount);
        Assert.Same(previous, Application.Current);
        app.Dispose();
        Assert.Equal(1, host.DisposeCount);
    }

    [Fact]
    public void FactoryFailureIsAStoppedOneShotHost()
    {
        var original = new InvalidOperationException("application factory");
        var host = new Host();
        var app = new JaliumApp(host, [], []).UseApplication(() => throw original);
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => app.StartHosted()));
        Assert.Equal(1, app.StopHosted(99));
        Assert.Equal(1, app.StopHosted(42));
        Assert.Equal(0, host.StartCount);
        Assert.Equal(0, host.StopCount);
        Assert.Equal(1, host.DisposeCount);
    }

    [Fact]
    public void InvalidArgumentsDoNotStartOrDisposeAHost()
    {
        var host = new Host();
        using var app = new JaliumApp(host, [], []);
        Assert.Throws<ArgumentNullException>(() => app.StartHosted(null!));
        Assert.Equal(0, host.StartCount);
        Assert.Equal(0, host.StopCount);
        Assert.Equal(0, host.DisposeCount);
    }

    [Fact]
    public void AHostThatNeverStartedStillRejectsStop()
    {
        var host = new Host();
        using var app = new JaliumApp(host, [], []);
        Assert.Throws<InvalidOperationException>(() => app.StopHosted());
        Assert.Equal(0, host.StopCount);
        Assert.Equal(0, host.DisposeCount);
    }

    private sealed class Host : IHost, IServiceProvider
    {
        public bool DisposeThrows { get; init; }
        public int StartCount, StopCount, DisposeCount;
        public IServiceProvider Services => this;
        public object? GetService(Type type) => null;
        public Task StartAsync(CancellationToken token = default) { StartCount++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token = default) { StopCount++; return Task.CompletedTask; }
        public void Dispose()
        {
            DisposeCount++;
            if (DisposeThrows) throw new InvalidOperationException("host disposal");
        }
    }
}
