using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Options;

namespace Jalium.Extensions.Diagnostics.Metrics;

/// <summary>
/// Fluent builder for the Metrics subsystem. Mirrors MS <c>IMetricsBuilder</c> shape so
/// existing <c>builder.Metrics.AddListener&lt;X&gt;</c>-style code keeps compiling.
/// </summary>
public interface IMetricsBuilder
{
    IServiceCollection Services { get; }
}

internal sealed class MetricsBuilder : IMetricsBuilder
{
    public MetricsBuilder(IServiceCollection services) { Services = services; }
    public IServiceCollection Services { get; }
}

/// <summary>Sink that consumes <see cref="Meter"/> instrument samples.</summary>
public interface IMetricsListener
{
    string Name { get; }
    void Initialize(IObservableInstrumentsSource source);
    bool InstrumentPublished(Instrument instrument, out object? userState);
    void MeasurementsCompleted(Instrument instrument, object? userState);
    MeasurementHandlers GetMeasurementHandlers();
}

public sealed class MeasurementHandlers
{
    public MeasurementCallback<byte>? ByteHandler { get; set; }
    public MeasurementCallback<short>? ShortHandler { get; set; }
    public MeasurementCallback<int>? IntHandler { get; set; }
    public MeasurementCallback<long>? LongHandler { get; set; }
    public MeasurementCallback<float>? FloatHandler { get; set; }
    public MeasurementCallback<double>? DoubleHandler { get; set; }
    public MeasurementCallback<decimal>? DecimalHandler { get; set; }
}

public delegate void MeasurementCallback<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct;

public interface IObservableInstrumentsSource
{
    void RecordObservableInstruments();
}

/// <summary>Factory contract — analogous to MS <c>IMeterFactory</c> for keyed Meter creation.</summary>
public interface IMeterFactory : IDisposable
{
    Meter Create(MeterOptions options);
}

public static class MeterFactoryExtensions
{
    public static Meter Create(this IMeterFactory factory, string name)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Create(new MeterOptions(name));
    }
}

internal sealed class DefaultMeterFactory : IMeterFactory
{
    private readonly ConcurrentBag<Meter> _meters = new();
    private bool _disposed;

    public Meter Create(MeterOptions options)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DefaultMeterFactory));
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var m in _meters) { try { m.Dispose(); } catch { } }
    }
}

/// <summary>Wires the listener layer to a live <see cref="MeterListener"/>.</summary>
internal sealed class MetricsListenerRouter : IDisposable, IObservableInstrumentsSource
{
    private readonly IEnumerable<IMetricsListener> _listeners;
    private MeterListener? _listener;
    private bool _disposed;

    public MetricsListenerRouter(IEnumerable<IMetricsListener> listeners) { _listeners = listeners; }

    public void Start()
    {
        var listener = new MeterListener();
        var perListenerHandlers = new List<(IMetricsListener Listener, MeasurementHandlers Handlers)>();
        foreach (var l in _listeners)
        {
            l.Initialize(this);
            perListenerHandlers.Add((l, l.GetMeasurementHandlers()));
        }

        listener.InstrumentPublished = (inst, lst) =>
        {
            foreach (var (l, _) in perListenerHandlers)
            {
                if (l.InstrumentPublished(inst, out var userState))
                    lst.EnableMeasurementEvents(inst, userState);
            }
        };
        listener.MeasurementsCompleted = (inst, state) =>
        {
            foreach (var (l, _) in perListenerHandlers) l.MeasurementsCompleted(inst, state);
        };

        listener.SetMeasurementEventCallback<byte>((i, v, t, s) => DispatchByte(perListenerHandlers, i, v, t, s));
        listener.SetMeasurementEventCallback<short>((i, v, t, s) => DispatchShort(perListenerHandlers, i, v, t, s));
        listener.SetMeasurementEventCallback<int>((i, v, t, s) => DispatchInt(perListenerHandlers, i, v, t, s));
        listener.SetMeasurementEventCallback<long>((i, v, t, s) => DispatchLong(perListenerHandlers, i, v, t, s));
        listener.SetMeasurementEventCallback<float>((i, v, t, s) => DispatchFloat(perListenerHandlers, i, v, t, s));
        listener.SetMeasurementEventCallback<double>((i, v, t, s) => DispatchDouble(perListenerHandlers, i, v, t, s));
        listener.SetMeasurementEventCallback<decimal>((i, v, t, s) => DispatchDecimal(perListenerHandlers, i, v, t, s));

        listener.Start();
        _listener = listener;
    }

    private static void DispatchByte(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, byte v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.ByteHandler?.Invoke(inst, v, tags, state); }
    private static void DispatchShort(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, short v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.ShortHandler?.Invoke(inst, v, tags, state); }
    private static void DispatchInt(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, int v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.IntHandler?.Invoke(inst, v, tags, state); }
    private static void DispatchLong(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, long v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.LongHandler?.Invoke(inst, v, tags, state); }
    private static void DispatchFloat(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, float v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.FloatHandler?.Invoke(inst, v, tags, state); }
    private static void DispatchDouble(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, double v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.DoubleHandler?.Invoke(inst, v, tags, state); }
    private static void DispatchDecimal(List<(IMetricsListener, MeasurementHandlers)> handlers, Instrument inst, decimal v, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    { foreach (var (_, h) in handlers) h.DecimalHandler?.Invoke(inst, v, tags, state); }

    public void RecordObservableInstruments() => _listener?.RecordObservableInstruments();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _listener?.Dispose(); } catch { }
    }
}

public static class MetricsServiceExtensions
{
    public static IServiceCollection AddMetrics(this IServiceCollection services) => services.AddMetrics(_ => { });

    public static IServiceCollection AddMetrics(this IServiceCollection services, Action<IMetricsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMeterFactory, DefaultMeterFactory>();
        services.TryAddSingleton(sp =>
        {
            var router = new MetricsListenerRouter(sp.GetServices<IMetricsListener>());
            router.Start();
            return router;
        });
        configure?.Invoke(new MetricsBuilder(services));
        return services;
    }
}

public static class MetricsBuilderExtensions
{
    public static IMetricsBuilder AddListener<T>(this IMetricsBuilder builder) where T : class, IMetricsListener
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IMetricsListener, T>();
        return builder;
    }

    public static IMetricsBuilder AddListener(this IMetricsBuilder builder, IMetricsListener listener)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(listener);
        builder.Services.AddSingleton(listener);
        return builder;
    }

    public static IMetricsBuilder EnableMetrics(this IMetricsBuilder builder, string meterName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<MetricsOptions>(o => o.EnabledMeters.Add(meterName));
        return builder;
    }
}

public sealed class MetricsOptions
{
    public List<string> EnabledMeters { get; } = new();
}
