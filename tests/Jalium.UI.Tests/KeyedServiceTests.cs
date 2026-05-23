using Jalium.Extensions.DependencyInjection;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// End-to-end coverage for the keyed-services surface we mirror from MS DI 8.0+.
/// Smoke-checks every <c>AddKeyed*</c> overload + <c>TryAddKeyed*</c> dedup + resolution + scope semantics.
/// </summary>
public sealed class KeyedServiceTests
{
    private interface ISvc { string Tag { get; } }
    private sealed class Svc : ISvc { public string Tag { get; init; } = "default"; }
    private sealed class SvcA : ISvc { public string Tag => "A"; }
    private sealed class SvcB : ISvc { public string Tag => "B"; }
    private sealed class WithKeyedParam
    {
        public ISvc Resolved { get; }
        public WithKeyedParam([FromKeyedServices("a")] ISvc svc) { Resolved = svc; }
    }

    [Fact]
    public void AddKeyedSingleton_TypeImpl_ResolvesByKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("a");
        services.AddKeyedSingleton<ISvc, SvcB>("b");
        using var sp = services.BuildServiceProvider();

        Assert.Equal("A", sp.GetRequiredKeyedService<ISvc>("a").Tag);
        Assert.Equal("B", sp.GetRequiredKeyedService<ISvc>("b").Tag);
    }

    [Fact]
    public void AddKeyedSingleton_Instance_ReturnsExactReference()
    {
        var inst = new Svc { Tag = "X" };
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc>("x", inst);
        using var sp = services.BuildServiceProvider();

        Assert.Same(inst, sp.GetRequiredKeyedService<ISvc>("x"));
    }

    [Fact]
    public void AddKeyedSingleton_Factory_ReceivesKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc>("z", (sp, key) => new Svc { Tag = $"key={key}" });
        using var root = services.BuildServiceProvider();

        Assert.Equal("key=z", root.GetRequiredKeyedService<ISvc>("z").Tag);
    }

    [Fact]
    public void AddKeyedSingleton_Type_Type_NonGeneric()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(typeof(ISvc), "u", typeof(SvcA));
        using var sp = services.BuildServiceProvider();

        Assert.Equal("A", sp.GetRequiredKeyedService<ISvc>("u").Tag);
    }

    [Fact]
    public void AddKeyedSingleton_Type_Instance_NonGeneric()
    {
        var inst = new Svc { Tag = "Q" };
        var services = new ServiceCollection();
        services.AddKeyedSingleton(typeof(ISvc), "q", inst);
        using var sp = services.BuildServiceProvider();

        Assert.Same(inst, sp.GetRequiredKeyedService<ISvc>("q"));
    }

    [Fact]
    public void AddKeyedScoped_HasPerScopeInstance()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<ISvc, SvcA>("s");
        using var root = services.BuildServiceProvider();

        using var scope1 = root.CreateScope();
        using var scope2 = root.CreateScope();
        var a1 = scope1.ServiceProvider.GetRequiredKeyedService<ISvc>("s");
        var a2 = scope1.ServiceProvider.GetRequiredKeyedService<ISvc>("s");
        var b1 = scope2.ServiceProvider.GetRequiredKeyedService<ISvc>("s");

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b1);
    }

    [Fact]
    public void AddKeyedTransient_NewInstanceEachResolution()
    {
        var services = new ServiceCollection();
        services.AddKeyedTransient<ISvc, SvcA>("t");
        using var sp = services.BuildServiceProvider();

        var first = sp.GetRequiredKeyedService<ISvc>("t");
        var second = sp.GetRequiredKeyedService<ISvc>("t");
        Assert.NotSame(first, second);
    }

    [Fact]
    public void TryAddKeyedSingleton_DoesNotDuplicate_OnSameKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("k");
        services.TryAddKeyedSingleton<ISvc, SvcB>("k");
        using var sp = services.BuildServiceProvider();

        // First registration wins — TryAdd does not overwrite.
        Assert.Equal("A", sp.GetRequiredKeyedService<ISvc>("k").Tag);
    }

    [Fact]
    public void TryAddKeyedSingleton_DifferentKey_AddsBoth()
    {
        var services = new ServiceCollection();
        services.TryAddKeyedSingleton<ISvc, SvcA>("a");
        services.TryAddKeyedSingleton<ISvc, SvcB>("b");
        using var sp = services.BuildServiceProvider();

        Assert.Equal("A", sp.GetRequiredKeyedService<ISvc>("a").Tag);
        Assert.Equal("B", sp.GetRequiredKeyedService<ISvc>("b").Tag);
    }

    [Fact]
    public void GetKeyedServices_AnyKey_ReturnsAllKeysForType()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("a");
        services.AddKeyedSingleton<ISvc, SvcB>("b");
        using var sp = services.BuildServiceProvider();

        var all = sp.GetKeyedServices<ISvc>(KeyedService.AnyKey).ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.Tag == "A");
        Assert.Contains(all, x => x.Tag == "B");
    }

    [Fact]
    public void FromKeyedServices_Attribute_PullsByKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("a");
        services.AddKeyedSingleton<ISvc, SvcB>("b");
        services.AddTransient<WithKeyedParam>();
        using var sp = services.BuildServiceProvider();

        var obj = sp.GetRequiredService<WithKeyedParam>();
        Assert.Equal("A", obj.Resolved.Tag);
    }

    [Fact]
    public void RemoveAllKeyed_DropsMatchingKeyedDescriptors()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("a");
        services.AddKeyedSingleton<ISvc, SvcB>("a"); // duplicate-by-key on purpose
        services.AddKeyedSingleton<ISvc, SvcA>("other");
        Assert.Equal(3, services.Count);

        services.RemoveAllKeyed<ISvc>("a");
        Assert.Single(services);
        Assert.Equal("other", services[0].ServiceKey);
    }

    [Fact]
    public void ReplaceKeyed_SwapsFirstMatch()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("a");
        services.ReplaceKeyed(ServiceDescriptor.KeyedSingleton<ISvc, SvcB>("a"));
        using var sp = services.BuildServiceProvider();

        Assert.Equal("B", sp.GetRequiredKeyedService<ISvc>("a").Tag);
    }

    [Fact]
    public void IsKeyedService_ReflectsRegistration()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ISvc, SvcA>("a");
        using var sp = services.BuildServiceProvider();

        var isks = sp.GetRequiredService<IServiceProviderIsKeyedService>();
        Assert.True(isks.IsKeyedService(typeof(ISvc), "a"));
        Assert.False(isks.IsKeyedService(typeof(ISvc), "missing"));
    }

    [Fact]
    public void NullKey_DistinctFromKeyedAndNonKeyed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISvc, SvcA>();                      // non-keyed
        services.AddKeyedSingleton<ISvc, SvcB>(serviceKey: null); // keyed with null key
        using var sp = services.BuildServiceProvider();

        Assert.Equal("A", sp.GetRequiredService<ISvc>().Tag);
        Assert.Equal("B", sp.GetRequiredKeyedService<ISvc>(null).Tag);
    }
}
