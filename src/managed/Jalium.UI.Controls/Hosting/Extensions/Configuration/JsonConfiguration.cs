using System.Text.Json;

namespace Jalium.Extensions.Configuration;

/// <summary>Source describing a JSON file on disk.</summary>
public sealed class JsonConfigurationSource : FileConfigurationSource
{
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new JsonConfigurationProvider(this);
    }
}

/// <summary>
/// Reads a JSON document into a flat <c>key:subkey:array.index → value</c> map.
/// Mirrors the layout produced by <c>Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider</c>
/// so existing <c>appsettings.json</c> trees keep working unchanged.
/// </summary>
public sealed class JsonConfigurationProvider : FileConfigurationProvider
{
    public JsonConfigurationProvider(JsonConfigurationSource source) : base(source) { }

    public override void Load(Stream stream)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            VisitElement(data, doc.RootElement, parentPath: null);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Could not parse the JSON file.", ex);
        }
        Data = data;
    }

    internal static void VisitElement(IDictionary<string, string?> data, JsonElement element, string? parentPath)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var path = parentPath == null ? prop.Name : ConfigurationPath.Combine(parentPath, prop.Name);
                    VisitElement(data, prop.Value, path);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var path = parentPath == null ? index.ToString() : ConfigurationPath.Combine(parentPath, index.ToString());
                    VisitElement(data, item, path);
                    index++;
                }
                break;
            case JsonValueKind.String:
                if (parentPath != null) data[parentPath] = element.GetString();
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (parentPath != null) data[parentPath] = element.GetRawText();
                break;
            case JsonValueKind.Null:
                if (parentPath != null) data[parentPath] = null;
                break;
        }
    }
}

public static class JsonConfigurationExtensions
{
    public static IConfigurationBuilder AddJsonFile(this IConfigurationBuilder builder, string path)
        => builder.AddJsonFile(path, optional: false, reloadOnChange: false);

    public static IConfigurationBuilder AddJsonFile(this IConfigurationBuilder builder, string path, bool optional)
        => builder.AddJsonFile(path, optional, reloadOnChange: false);

    public static IConfigurationBuilder AddJsonFile(this IConfigurationBuilder builder, string path, bool optional, bool reloadOnChange)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return builder.Add(new JsonConfigurationSource { Path = path, Optional = optional, ReloadOnChange = reloadOnChange });
    }

    public static IConfigurationBuilder AddJsonFile(this IConfigurationBuilder builder, Action<JsonConfigurationSource>? configure)
    {
        var src = new JsonConfigurationSource();
        configure?.Invoke(src);
        return builder.Add(src);
    }

    public static IConfigurationBuilder AddJsonStream(this IConfigurationBuilder builder, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var source = new JsonStreamConfigurationSource { Stream = stream };
        return builder.Add(source);
    }
}

/// <summary>Source backed by a pre-opened stream — useful for tests or embedded resources.</summary>
public sealed class JsonStreamConfigurationSource : IConfigurationSource
{
    public Stream Stream { get; set; } = null!;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new JsonStreamConfigurationProvider(this);
}

internal sealed class JsonStreamConfigurationProvider : ConfigurationProvider
{
    private readonly JsonStreamConfigurationSource _source;
    public JsonStreamConfigurationProvider(JsonStreamConfigurationSource source) { _source = source; }
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(_source.Stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            JsonConfigurationProvider.VisitElement(data, doc.RootElement, null);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Could not parse the JSON stream.", ex);
        }
        Data = data;
        OnReload();
    }
}

