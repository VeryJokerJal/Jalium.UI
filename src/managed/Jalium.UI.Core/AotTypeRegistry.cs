using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Stores the source-generated type catalog used by Jalium's reflection-shaped
/// features when an application is trimmed or compiled with NativeAOT.
/// </summary>
/// <remarks>
/// Application code does not need to populate this registry. The JALXAML source
/// generator emits a module initializer that registers one lazy provider for
/// every consuming assembly. The annotations on
/// <see cref="Register(Type)"/> preserve the constructors and public members needed
/// by MVVM discovery and string-path data binding.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AotTypeRegistry
{
    private static readonly ConcurrentDictionary<Assembly, AssemblyCatalog> Catalogs = new();

    /// <summary>
    /// Marks an assembly as source-generator aware, including assemblies that
    /// do not declare any registrable types.
    /// </summary>
    public static void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Catalogs.GetOrAdd(assembly, static _ => new AssemblyCatalog());
    }

    /// <summary>
    /// Registers a source-generated provider whose type entries are materialized
    /// the first time the assembly catalog is requested.
    /// </summary>
    public static void RegisterAssembly(Assembly assembly, Action typeProvider)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(typeProvider);

        Catalogs
            .GetOrAdd(assembly, static _ => new AssemblyCatalog())
            .RegisterProvider(typeProvider);
    }

    /// <summary>
    /// Registers and preserves a type declared in a consuming assembly.
    /// </summary>
    public static void Register(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicFields)]
        Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Catalogs
            .GetOrAdd(type.Assembly, static _ => new AssemblyCatalog())
            .Types.TryAdd(type, 0);
    }

    /// <summary>
    /// Strongly typed convenience overload for explicit registrations.
    /// </summary>
    public static void Register<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicFields)] T>()
    {
        Register(typeof(T));
    }

    /// <summary>
    /// Returns the generated catalog for <paramref name="assembly"/>.
    /// </summary>
    public static bool TryGetTypes(
        Assembly assembly,
        [NotNullWhen(true)] out IReadOnlyList<Type>? types)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!Catalogs.TryGetValue(assembly, out var catalog))
        {
            types = null;
            return false;
        }

        catalog.EnsureMaterialized();
        types = catalog.Types.Keys
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private sealed class AssemblyCatalog
    {
        private readonly object _providerGate = new();
        private Action? _typeProviders;
        private bool _isMaterializing;
        private bool _isMaterialized;
        private int _materializingThreadId;

        public ConcurrentDictionary<Type, byte> Types { get; } = new();

        public void RegisterProvider(Action typeProvider)
        {
            var invokeImmediately = false;
            var currentThreadId = Environment.CurrentManagedThreadId;

            lock (_providerGate)
            {
                while (_isMaterializing && _materializingThreadId != currentThreadId)
                {
                    Monitor.Wait(_providerGate);
                }

                // A provider can trigger another module initializer for this
                // assembly. Complete that nested registration on the owning
                // thread instead of waiting for our own materialization to end.
                if (_isMaterialized || _isMaterializing)
                {
                    invokeImmediately = true;
                }
                else
                {
                    _typeProviders += typeProvider;
                }
            }

            if (invokeImmediately)
            {
                typeProvider();
            }
        }

        public void EnsureMaterialized()
        {
            Action? providers;
            var currentThreadId = Environment.CurrentManagedThreadId;

            lock (_providerGate)
            {
                while (_isMaterializing)
                {
                    // A provider is allowed to query its own catalog. Returning the
                    // entries materialized so far avoids a same-thread deadlock.
                    if (_materializingThreadId == currentThreadId)
                    {
                        return;
                    }

                    Monitor.Wait(_providerGate);
                }

                if (_isMaterialized)
                {
                    return;
                }

                _isMaterializing = true;
                _materializingThreadId = currentThreadId;
                providers = _typeProviders;
                _typeProviders = null;
            }

            try
            {
                providers?.Invoke();
            }
            catch
            {
                lock (_providerGate)
                {
                    // Registration is idempotent, so retrying the complete provider
                    // list after a transient failure is safe.
                    _typeProviders = (Action?)Delegate.Combine(providers, _typeProviders);
                    _isMaterializing = false;
                    _materializingThreadId = 0;
                    Monitor.PulseAll(_providerGate);
                }

                throw;
            }

            lock (_providerGate)
            {
                _isMaterialized = true;
                _isMaterializing = false;
                _materializingThreadId = 0;
                Monitor.PulseAll(_providerGate);
            }
        }
    }
}
