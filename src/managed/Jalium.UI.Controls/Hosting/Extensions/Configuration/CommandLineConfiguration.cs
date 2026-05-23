namespace Jalium.Extensions.Configuration;

/// <summary>
/// Reads command-line <paramref name="args"/> as configuration keys.
/// <para>
/// This is the <em>argument-parsing</em> source — it never writes to the console, so it
/// stays compatible with the "no console output" rule. Accepted forms (matches MS):
/// <list type="bullet">
///   <item><c>--key value</c> / <c>-k value</c></item>
///   <item><c>--key=value</c> / <c>-k=value</c></item>
///   <item><c>/key value</c> / <c>/key=value</c></item>
///   <item><c>--key</c> (boolean) → "true"</item>
/// </list>
/// </para>
/// Short-form keys can be mapped to long names via <see cref="SwitchMappings"/>:
/// <c>{ "-c", "Config" }</c> → <c>-c hi</c> binds to <c>Config=hi</c>.
/// </summary>
public sealed class CommandLineConfigurationSource : IConfigurationSource
{
    public IEnumerable<string> Args { get; set; } = Array.Empty<string>();
    public IDictionary<string, string>? SwitchMappings { get; set; }
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new CommandLineConfigurationProvider(Args, SwitchMappings);
}

public sealed class CommandLineConfigurationProvider : ConfigurationProvider
{
    private readonly IEnumerable<string> _args;
    private readonly Dictionary<string, string>? _switchMappings;

    public CommandLineConfigurationProvider(IEnumerable<string> args, IDictionary<string, string>? switchMappings = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        _args = args;
        if (switchMappings != null)
        {
            _switchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in switchMappings)
            {
                if (!kv.Key.StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException($"Switch mapping '{kv.Key}' must start with '-' or '--'.");
                _switchMappings[kv.Key] = kv.Value;
            }
        }
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? pendingKey = null;
        foreach (var arg in _args)
        {
            if (arg.Length == 0) continue;
            string? key = null, value = null;

            if (arg.StartsWith("--", StringComparison.Ordinal)) { Parse(arg.Substring(2), out key, out value); }
            else if (arg.StartsWith("-", StringComparison.Ordinal)) { Parse(arg.Substring(1), out key, out value); }
            else if (arg.StartsWith("/", StringComparison.Ordinal)) { Parse(arg.Substring(1), out key, out value); }
            else
            {
                // Bare token — value for a pending --key.
                if (pendingKey != null) { data[NormalizeKey(pendingKey)] = arg; pendingKey = null; }
                continue;
            }

            if (key == null) continue;

            if (value != null)
            {
                data[NormalizeKey(key)] = value;
                pendingKey = null;
            }
            else
            {
                pendingKey = key;
                // MS sets boolean switches to "true" once we see the next non-arg or end of input.
                // Pre-populate "true" so a stand-alone "--flag" still binds; a following bare arg overrides.
                data[NormalizeKey(key)] = "true";
            }
        }
        Data = data;
    }

    private string NormalizeKey(string keyOrSwitch)
    {
        if (_switchMappings != null)
        {
            // Try both "-x" and "--x" lookup forms.
            if (_switchMappings.TryGetValue("--" + keyOrSwitch, out var mapped)) return mapped;
            if (_switchMappings.TryGetValue("-" + keyOrSwitch, out mapped)) return mapped;
            if (_switchMappings.TryGetValue(keyOrSwitch, out mapped)) return mapped;
        }
        return keyOrSwitch;
    }

    private static void Parse(string token, out string? key, out string? value)
    {
        var eq = token.IndexOf('=');
        if (eq < 0) { key = token; value = null; }
        else { key = token.Substring(0, eq); value = token.Substring(eq + 1); }
    }
}

public static class CommandLineConfigurationExtensions
{
    public static IConfigurationBuilder AddCommandLine(this IConfigurationBuilder builder, string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(args);
        return builder.Add(new CommandLineConfigurationSource { Args = args });
    }

    public static IConfigurationBuilder AddCommandLine(this IConfigurationBuilder builder, string[] args, IDictionary<string, string>? switchMappings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(args);
        return builder.Add(new CommandLineConfigurationSource { Args = args, SwitchMappings = switchMappings });
    }

    public static IConfigurationBuilder AddCommandLine(this IConfigurationBuilder builder, Action<CommandLineConfigurationSource>? configureSource)
    {
        var src = new CommandLineConfigurationSource();
        configureSource?.Invoke(src);
        return builder.Add(src);
    }
}
