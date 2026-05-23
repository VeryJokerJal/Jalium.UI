using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Default <see cref="IServiceProvider"/> implementation. Holds resolved singleton instances,
/// composes scopes, and resolves enumerable / open-generic services via reflection.
/// </summary>
public sealed class ServiceProvider : IServiceProvider, IServiceScope, IServiceScopeFactory,
    IServiceProviderIsService, IServiceProviderIsKeyedService, IKeyedServiceProvider,
    IAsyncDisposable, IDisposable
{
    private readonly ServiceProviderEngine _engine;
    private readonly bool _isRoot;
    private readonly ServiceProviderScope _scope;
    private bool _disposed;

    internal ServiceProvider(IEnumerable<ServiceDescriptor> descriptors, ServiceProviderOptions options)
    {
        _engine = new ServiceProviderEngine(descriptors, options);
        _isRoot = true;
        _scope = new ServiceProviderScope(this, _engine, isRoot: true);

        // Self-registrations: container can be resolved as IServiceProvider / IServiceScopeFactory /
        // IServiceProviderIsService — mirrors MS.Extensions.DependencyInjection behavior.
        if (options.ValidateOnBuild)
        {
            ValidateAllServices();
        }
    }

    private ServiceProvider(ServiceProvider root, ServiceProviderScope scope)
    {
        _engine = root._engine;
        _isRoot = false;
        _scope = scope;
    }

    public object? GetService(Type serviceType) => _scope.GetService(serviceType);

    IServiceProvider IServiceScope.ServiceProvider => this;

    public IServiceScope CreateScope()
    {
        ThrowIfDisposed();
        var scope = new ServiceProviderScope(null!, _engine, isRoot: false);
        var provider = new ServiceProvider(_isRoot ? this : _scope.RootProvider!, scope);
        scope.AttachProvider(provider);
        return provider;
    }

    public bool IsService(Type serviceType) => _engine.IsService(serviceType);

    public bool IsKeyedService(Type serviceType, object? serviceKey) => _engine.IsKeyedService(serviceType, serviceKey);

    public object? GetKeyedService(Type serviceType, object? serviceKey) => _scope.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        var s = GetKeyedService(serviceType, serviceKey);
        if (s == null)
            throw new InvalidOperationException($"No keyed service for type '{serviceType.FullName}' with key '{serviceKey}' has been registered.");
        return s;
    }

    internal ServiceProviderScope Scope => _scope;
    internal ServiceProviderEngine Engine => _engine;
    internal bool IsRoot => _isRoot;

    private void ValidateAllServices()
    {
        var errors = new List<Exception>();
        foreach (var desc in _engine.Descriptors)
        {
            if (desc.ServiceType.ContainsGenericParameters) continue;
            try { _scope.GetService(desc.ServiceType); }
            catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0)
            throw new AggregateException("Some services are not able to be constructed.", errors);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ServiceProvider));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scope.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return _scope.DisposeAsync();
    }
}

/// <summary>
/// Per-scope state: disposables + scoped instance cache. The root <see cref="ServiceProvider"/>
/// also owns one of these (the "root scope") so singletons live there.
/// </summary>
internal sealed class ServiceProviderScope : IDisposable, IAsyncDisposable
{
    private readonly ServiceProviderEngine _engine;
    private readonly ConcurrentDictionary<ServiceCacheKey, object?> _resolved = new();
    private readonly List<object> _disposables = new();
    private readonly bool _isRoot;
    private ServiceProvider? _provider;
    private bool _disposed;

    public ServiceProviderScope(ServiceProvider? provider, ServiceProviderEngine engine, bool isRoot)
    {
        _provider = provider;
        _engine = engine;
        _isRoot = isRoot;
    }

    public void AttachProvider(ServiceProvider provider) => _provider = provider;

    public bool IsRoot => _isRoot;
    public ServiceProvider? RootProvider => _engine.RootProvider;

    public object? GetService(Type serviceType)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ServiceProviderScope));

        // Container self-resolution
        if (serviceType == typeof(IServiceProvider)) return _provider!;
        if (serviceType == typeof(IServiceScopeFactory)) return _engine.RootProvider!;
        if (serviceType == typeof(IServiceProviderIsService)) return _engine.RootProvider!;
        if (serviceType == typeof(IServiceProviderIsKeyedService)) return _engine.RootProvider!;
        if (serviceType == typeof(IKeyedServiceProvider)) return _engine.RootProvider!;

        return _engine.Resolve(serviceType, this);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ServiceProviderScope));
        return _engine.ResolveKeyed(serviceType, serviceKey, this);
    }

    /// <summary>Track an instance for disposal at scope teardown.</summary>
    public void CaptureDisposable(object? instance)
    {
        if (instance == null) return;
        if (ReferenceEquals(instance, _provider)) return;
        if (instance is IDisposable || instance is IAsyncDisposable)
        {
            lock (_disposables) _disposables.Add(instance);
        }
    }

    public object? GetOrAddResolved(ServiceCacheKey key, Func<ServiceProviderScope, object?> factory)
    {
        // Two-phase: avoid running factory under lock, but ensure single-creation per key.
        if (_resolved.TryGetValue(key, out var v)) return v;
        lock (_resolved)
        {
            if (_resolved.TryGetValue(key, out v)) return v;
            var created = factory(this);
            _resolved[key] = created;
            return created;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Reverse-order disposal (LIFO).
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            switch (_disposables[i])
            {
                case IDisposable d:
                    try { d.Dispose(); } catch { /* swallow on shutdown */ }
                    break;
                case IAsyncDisposable ad:
                    try { ad.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* swallow */ }
                    break;
            }
        }
        _disposables.Clear();
        _resolved.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            switch (_disposables[i])
            {
                case IAsyncDisposable ad:
                    try { await ad.DisposeAsync().ConfigureAwait(false); } catch { }
                    break;
                case IDisposable d:
                    try { d.Dispose(); } catch { }
                    break;
            }
        }
        _disposables.Clear();
        _resolved.Clear();
    }
}

internal readonly record struct ServiceCacheKey(Type ServiceType, int DescriptorIndex);

/// <summary>
/// Shared resolution engine — maps service type → list of registrations and instantiates them.
/// One per root <see cref="ServiceProvider"/>; shared with every child scope.
/// </summary>
internal sealed class ServiceProviderEngine
{
    // Descriptors[i] is registration #i. Per-type lookup yields ordered list of indices.
    public ServiceDescriptor[] Descriptors { get; }
    // Non-keyed registrations.
    private readonly Dictionary<Type, List<int>> _byType = new();
    private readonly Dictionary<Type, List<int>> _byOpenGeneric = new();
    // Keyed: keyed by (serviceType, key) → descriptor indices (order preserved).
    private readonly Dictionary<(Type, object), List<int>> _byKeyed = new();
    private readonly Dictionary<(Type, object), List<int>> _byOpenGenericKeyed = new();
    // Tracks every key seen per service type — used by GetKeyedServices(type, AnyKey).
    private readonly Dictionary<Type, List<object?>> _allKeysByType = new();
    private readonly Dictionary<Type, List<object?>> _allKeysByOpenGeneric = new();
    private readonly ServiceProviderOptions _options;
    public ServiceProvider? RootProvider { get; private set; }

    public ServiceProviderEngine(IEnumerable<ServiceDescriptor> descriptors, ServiceProviderOptions options)
    {
        _options = options;
        Descriptors = descriptors.ToArray();
        for (int i = 0; i < Descriptors.Length; i++)
        {
            var d = Descriptors[i];
            var t = d.ServiceType;

            if (d.IsKeyedService)
            {
                var key = d.ServiceKey ?? NullKeySentinel;
                var keyedBucket = t.IsGenericTypeDefinition ? _byOpenGenericKeyed : _byKeyed;
                var allKeys = t.IsGenericTypeDefinition ? _allKeysByOpenGeneric : _allKeysByType;
                if (!keyedBucket.TryGetValue((t, key), out var klist))
                {
                    klist = new List<int>(1);
                    keyedBucket[(t, key)] = klist;
                }
                klist.Add(i);
                if (!allKeys.TryGetValue(t, out var akl))
                {
                    akl = new List<object?>(1);
                    allKeys[t] = akl;
                }
                if (!akl.Contains(d.ServiceKey)) akl.Add(d.ServiceKey);
            }
            else
            {
                var bucket = t.IsGenericTypeDefinition ? _byOpenGeneric : _byType;
                if (!bucket.TryGetValue(t, out var list))
                {
                    list = new List<int>(1);
                    bucket[t] = list;
                }
                list.Add(i);
            }
        }
    }

    /// <summary>Sentinel used to represent a <c>null</c> key in the keyed dictionary.</summary>
    internal static readonly object NullKeySentinel = new();

    public void AttachRoot(ServiceProvider root) => RootProvider = root;

    public bool IsService(Type serviceType)
    {
        if (_byType.ContainsKey(serviceType)) return true;
        if (serviceType.IsGenericType)
        {
            var def = serviceType.GetGenericTypeDefinition();
            if (_byOpenGeneric.ContainsKey(def)) return true;
            // IEnumerable<T>
            if (def == typeof(IEnumerable<>)) return true;
        }
        return false;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? NullKeySentinel;
        if (_byKeyed.ContainsKey((serviceType, key))) return true;
        if (serviceType.IsGenericType)
        {
            var def = serviceType.GetGenericTypeDefinition();
            if (_byOpenGenericKeyed.ContainsKey((def, key))) return true;
            if (def == typeof(IEnumerable<>)) return true;
        }
        return false;
    }

    public object? Resolve(Type serviceType, ServiceProviderScope scope)
    {
        // IEnumerable<T> — return every registration in order.
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return ResolveEnumerable(serviceType, scope);
        }

        // Try closed registrations first (the more specific match wins).
        if (_byType.TryGetValue(serviceType, out var list))
        {
            // Last registration wins for single-resolve (MS DI semantics).
            var idx = list[list.Count - 1];
            return ResolveByDescriptor(idx, serviceType, scope, expectedKey: null);
        }

        // Open generic fallback.
        if (serviceType.IsGenericType)
        {
            var def = serviceType.GetGenericTypeDefinition();
            if (_byOpenGeneric.TryGetValue(def, out var openList))
            {
                var idx = openList[openList.Count - 1];
                return ResolveByDescriptor(idx, serviceType, scope, expectedKey: null);
            }
        }

        return null;
    }

    public object? ResolveKeyed(Type serviceType, object? serviceKey, ServiceProviderScope scope)
    {
        // IEnumerable<T> — Microsoft DI returns every same-key registration in order.
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return ResolveKeyedEnumerable(serviceType, serviceKey, scope);
        }

        var keyForLookup = serviceKey ?? NullKeySentinel;
        if (_byKeyed.TryGetValue((serviceType, keyForLookup), out var list))
        {
            var idx = list[list.Count - 1];
            return ResolveByDescriptor(idx, serviceType, scope, expectedKey: serviceKey);
        }

        if (serviceType.IsGenericType)
        {
            var def = serviceType.GetGenericTypeDefinition();
            if (_byOpenGenericKeyed.TryGetValue((def, keyForLookup), out var openList))
            {
                var idx = openList[openList.Count - 1];
                return ResolveByDescriptor(idx, serviceType, scope, expectedKey: serviceKey);
            }
        }

        return null;
    }

    private object ResolveKeyedEnumerable(Type enumerableType, object? serviceKey, ServiceProviderScope scope)
    {
        var elementType = enumerableType.GenericTypeArguments[0];
        var items = new List<object?>();

        // KeyedService.AnyKey → walk every key for the type.
        if (ReferenceEquals(serviceKey, KeyedService.AnyKey))
        {
            if (_allKeysByType.TryGetValue(elementType, out var allKeys))
            {
                foreach (var k in allKeys)
                {
                    var keyForLookup = k ?? NullKeySentinel;
                    if (_byKeyed.TryGetValue((elementType, keyForLookup), out var idxList))
                        foreach (var idx in idxList)
                            items.Add(ResolveByDescriptor(idx, elementType, scope, expectedKey: k));
                }
            }
            if (elementType.IsGenericType)
            {
                var def = elementType.GetGenericTypeDefinition();
                if (_allKeysByOpenGeneric.TryGetValue(def, out var allOpenKeys))
                {
                    foreach (var k in allOpenKeys)
                    {
                        var keyForLookup = k ?? NullKeySentinel;
                        if (_byOpenGenericKeyed.TryGetValue((def, keyForLookup), out var idxList))
                            foreach (var idx in idxList)
                                items.Add(ResolveByDescriptor(idx, elementType, scope, expectedKey: k));
                    }
                }
            }
        }
        else
        {
            var keyForLookup = serviceKey ?? NullKeySentinel;
            if (_byKeyed.TryGetValue((elementType, keyForLookup), out var idxList))
                foreach (var idx in idxList)
                    items.Add(ResolveByDescriptor(idx, elementType, scope, expectedKey: serviceKey));
            if (elementType.IsGenericType)
            {
                var def = elementType.GetGenericTypeDefinition();
                if (_byOpenGenericKeyed.TryGetValue((def, keyForLookup), out var openList))
                    foreach (var idx in openList)
                        items.Add(ResolveByDescriptor(idx, elementType, scope, expectedKey: serviceKey));
            }
        }

        var array = Array.CreateInstance(elementType, items.Count);
        for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
        return array;
    }

    private object ResolveEnumerable(Type enumerableType, ServiceProviderScope scope)
    {
        var elementType = enumerableType.GenericTypeArguments[0];
        var items = new List<object?>();

        if (_byType.TryGetValue(elementType, out var list))
        {
            foreach (var idx in list)
            {
                items.Add(ResolveByDescriptor(idx, elementType, scope, expectedKey: null));
            }
        }

        if (elementType.IsGenericType)
        {
            var def = elementType.GetGenericTypeDefinition();
            if (_byOpenGeneric.TryGetValue(def, out var openList))
            {
                foreach (var idx in openList)
                {
                    items.Add(ResolveByDescriptor(idx, elementType, scope, expectedKey: null));
                }
            }
        }

        var array = Array.CreateInstance(elementType, items.Count);
        for (int i = 0; i < items.Count; i++)
            array.SetValue(items[i], i);
        return array;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "Open-generic implementation types are user-supplied; trimming preservation is the caller's responsibility.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "MakeGenericType usage mirrors MS DI semantics; AOT scenarios must register closed types or supply factories.")]
    private object? ResolveByDescriptor(int descriptorIndex, Type requestedType, ServiceProviderScope scope, object? expectedKey)
    {
        var desc = Descriptors[descriptorIndex];

        // Decide which scope owns the instance.
        ServiceProviderScope ownerScope = desc.Lifetime switch
        {
            ServiceLifetime.Singleton => RootProvider!.Scope,
            ServiceLifetime.Scoped => scope,
            _ => scope,
        };

        if (desc.Lifetime == ServiceLifetime.Transient)
        {
            var instance = Create(desc, requestedType, scope, expectedKey);
            scope.CaptureDisposable(instance);
            return instance;
        }

        // Cache by (type, descriptor index) — different keyed registrations get distinct cache slots
        // automatically because each lives in a different descriptor index.
        var cacheKey = new ServiceCacheKey(requestedType, descriptorIndex);
        return ownerScope.GetOrAddResolved(cacheKey, s =>
        {
            var instance = Create(desc, requestedType, s, expectedKey);
            s.CaptureDisposable(instance);
            return instance;
        });
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "MakeGenericType is required for open-generic DI.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection-based activation is the documented fallback for non-AOT scenarios.")]
    private object Create(ServiceDescriptor desc, Type requestedType, ServiceProviderScope scope, object? resolveKey)
    {
        if (desc.ImplementationInstance != null) return desc.ImplementationInstance;
        if (desc.ImplementationFactory != null)
            return desc.ImplementationFactory(new ScopedProvider(scope));
        if (desc.KeyedImplementationFactory != null)
            return desc.KeyedImplementationFactory(new ScopedProvider(scope), resolveKey);

        var implType = desc.ImplementationType!;
        if (implType.IsGenericTypeDefinition)
        {
            // Close on the request's generic args.
            implType = implType.MakeGenericType(requestedType.GenericTypeArguments);
        }
        return ActivatorUtilitiesCore.CreateInstance(new ScopedProvider(scope), implType, Array.Empty<object>());
    }
}

/// <summary>
/// Thin <see cref="IServiceProvider"/> wrapper that routes through a specific scope. Passed
/// to user factories (<see cref="ServiceDescriptor.ImplementationFactory"/>) so transient
/// resolutions inside the factory honour scope boundaries.
/// </summary>
internal sealed class ScopedProvider : IServiceProvider, IServiceScopeFactory, IKeyedServiceProvider, IServiceProviderIsService, IServiceProviderIsKeyedService
{
    private readonly ServiceProviderScope _scope;
    public ScopedProvider(ServiceProviderScope scope) { _scope = scope; }
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider)) return this;
        if (serviceType == typeof(IServiceScopeFactory)) return this;
        if (serviceType == typeof(IKeyedServiceProvider)) return this;
        if (serviceType == typeof(IServiceProviderIsService)) return this;
        if (serviceType == typeof(IServiceProviderIsKeyedService)) return this;
        return _scope.GetService(serviceType);
    }
    public IServiceScope CreateScope() => _scope.RootProvider!.CreateScope();

    public object? GetKeyedService(Type serviceType, object? serviceKey) => _scope.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        var s = GetKeyedService(serviceType, serviceKey);
        if (s == null)
            throw new InvalidOperationException($"No keyed service for type '{serviceType.FullName}' with key '{serviceKey}' has been registered.");
        return s;
    }

    public bool IsService(Type serviceType) => _scope.RootProvider!.IsService(serviceType);
    public bool IsKeyedService(Type serviceType, object? serviceKey) => _scope.RootProvider!.IsKeyedService(serviceType, serviceKey);
}

/// <summary>
/// <see cref="IServiceCollection"/> → <see cref="ServiceProvider"/> bridge.
/// </summary>
public static class ServiceCollectionContainerBuilderExtensions
{
    public static ServiceProvider BuildServiceProvider(this IServiceCollection services)
        => services.BuildServiceProvider(ServiceProviderOptions.Default);

    public static ServiceProvider BuildServiceProvider(this IServiceCollection services, bool validateScopes)
        => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes });

    public static ServiceProvider BuildServiceProvider(this IServiceCollection services, ServiceProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = new ServiceProvider(services, options);
        provider.Engine.AttachRoot(provider);
        return provider;
    }
}
