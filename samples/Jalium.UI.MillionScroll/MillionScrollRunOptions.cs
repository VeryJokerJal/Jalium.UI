using System.Globalization;
using Jalium.UI.Interop;

namespace Jalium.UI.MillionScroll;

public sealed record MillionScrollRunOptions(
    int Rows,
    RenderBackend Backend,
    RenderingEngine Engine,
    double BenchmarkSeconds,
    double WarmupSeconds,
    double ScrollSpeed,
    string? OutputPath)
{
    public bool BenchmarkEnabled => BenchmarkSeconds > 0;

    public static MillionScrollRunOptions Parse(string[] args, int defaultRows)
    {
        var rows = ParseInt(ReadOption(args, "--rows"), defaultRows, 1, 10_000_000);
        var backend = ParseBackend(ReadOption(args, "--backend"));
        var engine = ParseEngine(ReadOption(args, "--engine"));
        var seconds = ParseDouble(ReadOption(args, "--benchmark-seconds"), 0, 0, 3_600);
        var warmup = ParseDouble(ReadOption(args, "--warmup-seconds"), 2, 0, 300);
        var speed = ParseDouble(ReadOption(args, "--speed"), 120, 1, 10_000);
        var output = ReadOption(args, "--output");
        if (seconds > 0 && string.IsNullOrWhiteSpace(output))
        {
            output = "million-scroll-benchmark.json";
        }

        return new MillionScrollRunOptions(rows, backend, engine, seconds, warmup, speed, output);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }

            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ParseInt(string? value, int fallback, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;

    private static double ParseDouble(string? value, double fallback, double min, double max) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;

    private static RenderBackend ParseBackend(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "d3d12" or "dx12" => RenderBackend.D3D12,
        "vulkan" or "vk" => RenderBackend.Vulkan,
        "software" or "cpu" => RenderBackend.Software,
        _ => RenderBackend.Auto,
    };

    private static RenderingEngine ParseEngine(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "vello" => RenderingEngine.Vello,
        "impeller" => RenderingEngine.Impeller,
        _ => RenderingEngine.Impeller,
    };
}

