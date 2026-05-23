namespace Jalium.Extensions.Configuration;

/// <summary>Source describing an INI file.</summary>
public sealed class IniConfigurationSource : FileConfigurationSource
{
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new IniConfigurationProvider(this);
    }
}

/// <summary>
/// Reads an INI file. Format mirrors <c>Microsoft.Extensions.Configuration.Ini</c>:
/// <list type="bullet">
///   <item><c>[Section]</c> — section header. Nested sections allowed (<c>[A:B]</c>) for hierarchical paths.</item>
///   <item><c>key=value</c> — key/value pair under the current section.</item>
///   <item><c># comment</c> or <c>; comment</c> — line comment.</item>
///   <item>Inline whitespace around the <c>=</c> sign is ignored. Values may be quoted with <c>"…"</c>; quotes are stripped.</item>
/// </list>
/// </summary>
public sealed class IniConfigurationProvider : FileConfigurationProvider
{
    public IniConfigurationProvider(IniConfigurationSource source) : base(source) { }

    internal IDictionary<string, string?> InternalData => Data;

    public override void Load(Stream stream)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(stream);
        string sectionPrefix = string.Empty;
        string? line;
        int lineNum = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNum++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed[0] == ';' || trimmed[0] == '#') continue;

            if (trimmed[0] == '[')
            {
                if (trimmed[trimmed.Length - 1] != ']')
                    throw new FormatException($"Invalid INI section header at line {lineNum}: '{trimmed}'");
                sectionPrefix = trimmed.Substring(1, trimmed.Length - 2).Trim();
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq < 0) throw new FormatException($"Invalid INI line {lineNum}: '{trimmed}'");
            var key = trimmed.Substring(0, eq).Trim();
            var value = trimmed.Substring(eq + 1).Trim();

            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);

            var fullKey = string.IsNullOrEmpty(sectionPrefix) ? key : ConfigurationPath.Combine(sectionPrefix, key);
            if (data.ContainsKey(fullKey))
                throw new FormatException($"Duplicate INI key '{fullKey}' at line {lineNum}.");
            data[fullKey] = value;
        }
        Data = data;
    }
}

public static class IniConfigurationExtensions
{
    public static IConfigurationBuilder AddIniFile(this IConfigurationBuilder builder, string path)
        => builder.AddIniFile(path, optional: false, reloadOnChange: false);

    public static IConfigurationBuilder AddIniFile(this IConfigurationBuilder builder, string path, bool optional)
        => builder.AddIniFile(path, optional, reloadOnChange: false);

    public static IConfigurationBuilder AddIniFile(this IConfigurationBuilder builder, string path, bool optional, bool reloadOnChange)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return builder.Add(new IniConfigurationSource { Path = path, Optional = optional, ReloadOnChange = reloadOnChange });
    }

    public static IConfigurationBuilder AddIniStream(this IConfigurationBuilder builder, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return builder.Add(new IniStreamConfigurationSource { Stream = stream });
    }
}

public sealed class IniStreamConfigurationSource : IConfigurationSource
{
    public Stream Stream { get; set; } = null!;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new IniStreamConfigurationProvider(this);
}

internal sealed class IniStreamConfigurationProvider : ConfigurationProvider
{
    private readonly IniStreamConfigurationSource _source;
    public IniStreamConfigurationProvider(IniStreamConfigurationSource source) { _source = source; }
    public override void Load()
    {
        var tmpSrc = new IniConfigurationSource();
        var provider = new IniConfigurationProvider(tmpSrc);
        provider.Load(_source.Stream);
        Data = provider.InternalData;
        OnReload();
    }
}

internal static class IniConfigurationProviderInternals
{
    // Reserved for future helpers.
}
