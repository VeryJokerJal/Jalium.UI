using Jalium.Extensions.FileProviders;
using Jalium.Extensions.Primitives;

namespace Jalium.Extensions.Configuration;

/// <summary>
/// Source for any provider that reads from an <see cref="IFileProvider"/>. Concrete
/// providers (JSON, XML, INI, …) subclass <see cref="FileConfigurationProvider"/>
/// and override <see cref="FileConfigurationProvider.Load(Stream)"/>.
/// </summary>
public abstract class FileConfigurationSource : IConfigurationSource
{
    public IFileProvider? FileProvider { get; set; }
    public string? Path { get; set; }
    public bool Optional { get; set; }
    public bool ReloadOnChange { get; set; }
    public int ReloadDelay { get; set; } = 250;
    public Action<FileLoadExceptionContext>? OnLoadException { get; set; }

    public abstract IConfigurationProvider Build(IConfigurationBuilder builder);

    /// <summary>Resolve the file provider against the builder root path if none was set.</summary>
    public void EnsureDefaults(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (FileProvider == null && Path != null)
        {
            if (System.IO.Path.IsPathRooted(Path))
            {
                var dir = System.IO.Path.GetDirectoryName(Path)!;
                FileProvider = new PhysicalFileProvider(dir);
                Path = System.IO.Path.GetFileName(Path);
            }
            else
            {
                // Fall back to FileConfigurationExtensions.GetFileProvider (set by AddJsonFile/SetBasePath).
                if (builder.Properties.TryGetValue("FileProvider", out var existing) && existing is IFileProvider fp)
                    FileProvider = fp;
                else
                    FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory);
            }
        }
        if (FileProvider == null) FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}

public sealed class FileLoadExceptionContext
{
    public FileConfigurationProvider Provider { get; init; } = null!;
    public Exception Exception { get; init; } = null!;
    public bool Ignore { get; set; }
}

public abstract class FileConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly IDisposable? _changeTokenRegistration;
    private bool _disposed;

    protected FileConfigurationProvider(FileConfigurationSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.ReloadOnChange && source.FileProvider != null && source.Path != null)
        {
            _changeTokenRegistration = ChangeToken.OnChange(
                () => source.FileProvider.Watch(source.Path),
                () =>
                {
                    if (source.ReloadDelay > 0) Thread.Sleep(source.ReloadDelay);
                    Load(reload: true);
                });
        }
    }

    public FileConfigurationSource Source { get; }

    public override void Load() => Load(reload: false);

    private void Load(bool reload)
    {
        var file = Source.FileProvider?.GetFileInfo(Source.Path ?? string.Empty);
        if (file == null || !file.Exists)
        {
            if (Source.Optional || reload)
            {
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var error = new FileNotFoundException($"The configuration file '{Source.Path}' was not found and is not optional.");
                HandleException(error);
                return;
            }
        }
        else
        {
            try
            {
                using var stream = file.CreateReadStream();
                Load(stream);
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }
        OnReload();
    }

    public abstract void Load(Stream stream);

    private void HandleException(Exception ex)
    {
        bool ignore = false;
        if (Source.OnLoadException != null)
        {
            var ctx = new FileLoadExceptionContext { Provider = this, Exception = ex };
            Source.OnLoadException(ctx);
            ignore = ctx.Ignore;
        }
        if (!ignore) throw ex;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _changeTokenRegistration?.Dispose();
    }
}

public static class FileConfigurationExtensions
{
    public static IConfigurationBuilder SetBasePath(this IConfigurationBuilder builder, string basePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(basePath);
        builder.Properties["FileProvider"] = new PhysicalFileProvider(basePath);
        return builder;
    }

    public static IConfigurationBuilder SetFileProvider(this IConfigurationBuilder builder, IFileProvider fileProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(fileProvider);
        builder.Properties["FileProvider"] = fileProvider;
        return builder;
    }

    public static IFileProvider GetFileProvider(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Properties.TryGetValue("FileProvider", out var v) && v is IFileProvider fp) return fp;
        return new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
