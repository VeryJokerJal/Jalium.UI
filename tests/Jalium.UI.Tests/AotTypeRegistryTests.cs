using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jalium.UI.Tests;

public sealed class AotTypeRegistryTests
{
    [Fact]
    public void GeneratedCatalog_DrivesConventionBasedMvvmDiscovery()
    {
        AotTypeRegistry.Register<GeneratedCatalogPage>();
        AotTypeRegistry.Register<GeneratedCatalogViewModel>();

        var services = new ServiceCollection();
        services.AddViewsAndViewModels(typeof(GeneratedCatalogPage).Assembly);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ViewRegistry>();

        Assert.True(
            registry.TryGetViewModelType(
                typeof(GeneratedCatalogPage),
                out var viewModelType));
        Assert.Equal(typeof(GeneratedCatalogViewModel), viewModelType);
        Assert.NotNull(provider.GetService<GeneratedCatalogPage>());
        Assert.NotNull(provider.GetService<GeneratedCatalogViewModel>());
    }

    [Fact]
    public void RegisteredTypes_AreExposedByDeclaringAssembly()
    {
        AotTypeRegistry.Register<GeneratedCatalogViewModel>();

        Assert.True(
            AotTypeRegistry.TryGetTypes(
                typeof(GeneratedCatalogViewModel).Assembly,
                out var types));
        Assert.Contains(typeof(GeneratedCatalogViewModel), types);
    }

    [Fact]
    public void AssemblyProvider_IsMaterializedOnceOnFirstCatalogRead()
    {
        var assemblyName = new AssemblyName(
            $"Jalium.UI.Tests.AotCatalog.{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var generatedType = module
            .DefineType(
                "Generated.ProviderViewModel",
                TypeAttributes.Public | TypeAttributes.Class)
            .CreateType()!;
        var catalogAssembly = generatedType.Assembly;
        var providerCalls = 0;

        AotTypeRegistry.RegisterAssembly(catalogAssembly, () =>
        {
            Interlocked.Increment(ref providerCalls);
            AotTypeRegistry.Register(generatedType);
        });

        Assert.Equal(0, Volatile.Read(ref providerCalls));

        Assert.True(AotTypeRegistry.TryGetTypes(catalogAssembly, out var firstRead));
        Assert.Equal(1, Volatile.Read(ref providerCalls));
        Assert.Equal(new[] { generatedType }, firstRead);

        Assert.True(AotTypeRegistry.TryGetTypes(catalogAssembly, out var secondRead));
        Assert.Equal(1, Volatile.Read(ref providerCalls));
        Assert.Equal(firstRead, secondRead);
    }

    [Fact]
    public async Task AssemblyProvider_CanRegisterNestedProviderWithoutBlockingFirstRead()
    {
        var name = new AssemblyName($"Jalium.UI.Tests.NestedCatalog.{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(name.Name!);
        var firstType = module.DefineType("First", TypeAttributes.Public).CreateType()!;
        var secondType = module.DefineType("Second", TypeAttributes.Public).CreateType()!;
        var catalogAssembly = firstType.Assembly;
        var calls = 0;
        AotTypeRegistry.RegisterAssembly(catalogAssembly, () =>
        {
            AotTypeRegistry.Register(firstType);
            AotTypeRegistry.RegisterAssembly(catalogAssembly, () =>
            {
                calls++;
                AotTypeRegistry.Register(secondType);
            });
        });

        var read = Task.Run(() =>
        {
            Assert.True(AotTypeRegistry.TryGetTypes(catalogAssembly, out var types));
            return types;
        });
        var result = await read.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { firstType, secondType }, result);
        Assert.Equal(1, calls);
        Assert.True(AotTypeRegistry.TryGetTypes(catalogAssembly, out var repeated));
        Assert.Equal(result, repeated);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AssemblyProvider_RetriesFailureWithoutLosingRegisteredTypes()
    {
        var name = new AssemblyName($"Jalium.UI.Tests.RetryCatalog.{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(name.Name!);
        var firstType = module.DefineType("First", TypeAttributes.Public).CreateType()!;
        var secondType = module.DefineType("Second", TypeAttributes.Public).CreateType()!;
        var catalogAssembly = firstType.Assembly;
        var calls = 0;
        AotTypeRegistry.RegisterAssembly(catalogAssembly, () =>
        {
            AotTypeRegistry.Register(firstType);
            if (++calls == 1)
                throw new InvalidOperationException("A dependency is not ready yet.");
            AotTypeRegistry.Register(secondType);
        });

        Assert.Throws<InvalidOperationException>(() => AotTypeRegistry.TryGetTypes(catalogAssembly, out _));
        Assert.True(AotTypeRegistry.TryGetTypes(catalogAssembly, out var result));
        Assert.Equal(new[] { firstType, secondType }, result);
        Assert.Equal(2, calls);
    }

    public sealed class GeneratedCatalogPage : Page
    {
    }

    public sealed class GeneratedCatalogViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public string Title { get; set; } = "AOT";
    }
}
