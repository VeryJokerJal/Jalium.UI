using System.Text.Json;

namespace Jalium.UI.ApiParity;

internal sealed class NamespaceMapper
{
    private readonly PrefixRule[] _prefixes;
    private readonly IReadOnlyDictionary<string, string> _typeOverrides;

    private NamespaceMapper(NamespaceMapConfiguration configuration)
    {
        _prefixes = configuration.Prefixes
            .OrderByDescending(static rule => rule.From.Length)
            .ToArray();
        _typeOverrides = configuration.TypeOverrides;
    }

    public static NamespaceMapper Load(string path)
    {
        if (!File.Exists(path))
        {
            return CreateDefault();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        NamespaceMapConfiguration? configuration =
            JsonSerializer.Deserialize<NamespaceMapConfiguration>(File.ReadAllText(path), options);
        if (configuration is null || configuration.Prefixes.Count == 0)
        {
            throw new InvalidDataException($"Namespace map '{path}' contains no prefix rules.");
        }

        return new NamespaceMapper(configuration);
    }

    public static NamespaceMapper CreateDefault()
    {
        return new NamespaceMapper(new NamespaceMapConfiguration
        {
            Prefixes =
            [
                new PrefixRule("System.Windows.Shapes", "Jalium.UI.Controls.Shapes"),
                new PrefixRule("System.Windows", "Jalium.UI"),
                new PrefixRule("System.Xaml", "Jalium.UI.Xaml"),
            ],
        });
    }

    public string MapNamespace(string sourceNamespace)
    {
        foreach (PrefixRule rule in _prefixes)
        {
            if (sourceNamespace.Equals(rule.From, StringComparison.Ordinal))
            {
                return rule.To;
            }

            if (sourceNamespace.StartsWith(rule.From + ".", StringComparison.Ordinal))
            {
                return rule.To + sourceNamespace[rule.From.Length..];
            }
        }

        return sourceNamespace;
    }

    public string MapType(string sourceNamespace, string metadataTypeName)
    {
        string sourceFullName = MetadataNames.Join(sourceNamespace, metadataTypeName);
        if (_typeOverrides.TryGetValue(sourceFullName, out string? targetFullName))
        {
            return targetFullName;
        }

        return MetadataNames.Join(MapNamespace(sourceNamespace), metadataTypeName);
    }
}

internal sealed class NamespaceMapConfiguration
{
    public List<PrefixRule> Prefixes { get; init; } = [];

    public Dictionary<string, string> TypeOverrides { get; init; } =
        new(StringComparer.Ordinal);
}

internal sealed record PrefixRule(string From, string To);
