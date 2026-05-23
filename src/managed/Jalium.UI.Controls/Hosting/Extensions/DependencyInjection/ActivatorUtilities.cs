using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Jalium.Extensions.DependencyInjection;

/// <summary>
/// Reflection-based helper that instantiates types whose constructor parameters are
/// supplied through a mix of DI resolutions and caller-supplied positional arguments —
/// the moral equivalent of MS.Extensions.DependencyInjection.ActivatorUtilities.
/// </summary>
/// <remarks>
/// Constructor selection algorithm mirrors MS DI:
/// <list type="number">
///   <item>If any constructor is decorated with <see cref="ActivatorUtilitiesConstructorAttribute"/>, use it.</item>
///   <item>Otherwise, choose the constructor that can be satisfied with the most caller-supplied arguments combined with services from <paramref name="provider"/>. Ties broken by parameter count (more = preferred).</item>
/// </list>
/// </remarks>
public static class ActivatorUtilities
{
    [RequiresUnreferencedCode("Reflection-based activation; preserve target type constructors when trimming.")]
    [RequiresDynamicCode("Reflection-based activation requires dynamic code.")]
    public static object CreateInstance(IServiceProvider provider, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type instanceType, params object[] parameters)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(instanceType);
        parameters ??= Array.Empty<object>();
        return ActivatorUtilitiesCore.CreateInstance(provider, instanceType, parameters);
    }

    [RequiresUnreferencedCode("Reflection-based activation; preserve target type constructors when trimming.")]
    [RequiresDynamicCode("Reflection-based activation requires dynamic code.")]
    public static T CreateInstance<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(IServiceProvider provider, params object[] parameters)
        => (T)CreateInstance(provider, typeof(T), parameters);

    [RequiresUnreferencedCode("Reflection-based activation; preserve target type constructors when trimming.")]
    [RequiresDynamicCode("Reflection-based activation requires dynamic code.")]
    public static object GetServiceOrCreateInstance(IServiceProvider provider, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        => provider.GetService(type) ?? CreateInstance(provider, type);

    [RequiresUnreferencedCode("Reflection-based activation; preserve target type constructors when trimming.")]
    [RequiresDynamicCode("Reflection-based activation requires dynamic code.")]
    public static T GetServiceOrCreateInstance<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(IServiceProvider provider)
        => (T)GetServiceOrCreateInstance(provider, typeof(T));

    [RequiresUnreferencedCode("Reflection-based activation; preserve target type constructors when trimming.")]
    [RequiresDynamicCode("Reflection-based activation requires dynamic code.")]
    public static ObjectFactory CreateFactory([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type instanceType, Type[] argumentTypes)
    {
        ArgumentNullException.ThrowIfNull(instanceType);
        ArgumentNullException.ThrowIfNull(argumentTypes);
        return (sp, args) => ActivatorUtilitiesCore.CreateInstance(sp, instanceType, args ?? Array.Empty<object>());
    }
}

public delegate object ObjectFactory(IServiceProvider serviceProvider, object?[]? arguments);

/// <summary>Marks the preferred constructor for <see cref="ActivatorUtilities"/>.</summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class ActivatorUtilitiesConstructorAttribute : Attribute { }

internal static class ActivatorUtilitiesCore
{
    [RequiresUnreferencedCode("Reflection-based activation.")]
    [RequiresDynamicCode("Reflection-based activation.")]
    public static object CreateInstance(IServiceProvider provider, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type instanceType, object?[] suppliedArgs)
    {
        var ctors = instanceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
            throw new InvalidOperationException($"No public constructor on type '{instanceType.FullName}'.");

        // Explicit marker wins.
        ConstructorInfo? markedCtor = null;
        foreach (var c in ctors)
        {
            if (c.IsDefined(typeof(ActivatorUtilitiesConstructorAttribute), inherit: false))
            {
                if (markedCtor != null)
                    throw new InvalidOperationException($"Multiple constructors on '{instanceType.FullName}' carry [ActivatorUtilitiesConstructor].");
                markedCtor = c;
            }
        }
        if (markedCtor != null)
        {
            return Invoke(markedCtor, provider, suppliedArgs);
        }

        // Pick the longest constructor we can satisfy. Each supplied argument is consumed by the
        // first non-resolved parameter of compatible type (left-to-right). Remaining parameters
        // must resolve from DI.
        Array.Sort(ctors, (a, b) => b.GetParameters().Length - a.GetParameters().Length);
        Exception? lastError = null;
        foreach (var c in ctors)
        {
            try { return Invoke(c, provider, suppliedArgs); }
            catch (Exception ex) { lastError = ex; }
        }
        throw new InvalidOperationException(
            $"No constructor on '{instanceType.FullName}' could be satisfied by the service provider and the supplied arguments.",
            lastError);
    }

    private static object Invoke(ConstructorInfo ctor, IServiceProvider provider, object?[] suppliedArgs)
    {
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        var consumed = new bool[suppliedArgs.Length];

        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            object? value = null;
            bool found = false;

            // Match the next unused supplied argument that fits this parameter.
            for (int j = 0; j < suppliedArgs.Length; j++)
            {
                if (consumed[j]) continue;
                var s = suppliedArgs[j];
                if (s == null)
                {
                    if (!p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null)
                    {
                        value = null; found = true; consumed[j] = true; break;
                    }
                    continue;
                }
                if (p.ParameterType.IsInstanceOfType(s))
                {
                    value = s; found = true; consumed[j] = true; break;
                }
            }

            if (!found)
            {
                // Honor [FromKeyedServices(key)] on the parameter.
                var keyedAttr = p.GetCustomAttribute<FromKeyedServicesAttribute>(inherit: false);
                if (keyedAttr != null && provider is IKeyedServiceProvider ksp)
                {
                    value = ksp.GetKeyedService(p.ParameterType, keyedAttr.Key);
                }
                else
                {
                    value = provider.GetService(p.ParameterType);
                }
                if (value == null)
                {
                    if (p.HasDefaultValue) value = p.DefaultValue;
                    else throw new InvalidOperationException(
                        $"Unable to resolve service for type '{p.ParameterType.FullName}'{(keyedAttr != null ? $" with key '{keyedAttr.Key}'" : string.Empty)} while attempting to activate '{ctor.DeclaringType?.FullName}'.");
                }
            }
            args[i] = value;
        }
        return ctor.Invoke(args);
    }
}
