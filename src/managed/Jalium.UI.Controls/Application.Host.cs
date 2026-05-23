using Jalium.Extensions.Configuration;
using Jalium.Extensions.DependencyInjection;
using Jalium.Extensions.Hosting;

namespace Jalium.UI;

public partial class Application
{
    private IServiceProvider? _services;
    private IConfiguration? _configuration;
    private IHostEnvironment? _hostEnvironment;

    /// <summary>
    /// Gets the root <see cref="IServiceProvider"/> produced by the <see cref="AppBuilder"/>.
    /// Returns <see langword="null"/> when the application was not created via
    /// <see cref="AppBuilder"/>. Resolve application-scoped services through this provider
    /// (per-window or per-operation scopes should be created with <see cref="IServiceScopeFactory"/>).
    /// </summary>
    public IServiceProvider? Services => _services;

    /// <summary>
    /// Gets the configuration built by the <see cref="AppBuilder"/>. Exposes the same
    /// <see cref="IConfiguration"/> tree registered in <see cref="Services"/>.
    /// </summary>
    public IConfiguration? Configuration => _configuration;

    /// <summary>
    /// Gets the hosting environment (<c>Development</c>/<c>Staging</c>/<c>Production</c>,
    /// application name, content root) produced by the <see cref="AppBuilder"/>.
    /// </summary>
    public IHostEnvironment? HostEnvironment => _hostEnvironment;

    internal void AttachHost(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        _services = services;
        _configuration = configuration;
        _hostEnvironment = environment;
    }

    internal void DetachHost()
    {
        _services = null;
        _configuration = null;
        _hostEnvironment = null;
    }
}
