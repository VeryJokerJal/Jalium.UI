namespace Jalium.UI;

/// <summary>Defers template XAML so it can be instantiated for each template application.</summary>
public class TemplateContentLoader : Xaml.XamlDeferringLoader
{
    /// <inheritdoc />
    public override object Load(Xaml.XamlReader xamlReader, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(xamlReader);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return new DeferredTemplateContent(xamlReader, serviceProvider);
    }

    /// <inheritdoc />
    public override Xaml.XamlReader Save(object? value, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return value is DeferredTemplateContent deferred
            ? deferred.Reader
            : new Xaml.XamlObjectReader(value);
    }

    private sealed class DeferredTemplateContent
    {
        internal DeferredTemplateContent(Xaml.XamlReader reader, IServiceProvider serviceProvider)
        {
            Reader = reader;
            ServiceProvider = serviceProvider;
        }

        internal Xaml.XamlReader Reader { get; }

        internal IServiceProvider ServiceProvider { get; }
    }
}
