using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jalium.UI.HostingDemo.Services;

/// <summary>
/// 从 <see cref="GreetingOptions"/> 读取模板,通过 <see cref="ILogger{TCategoryName}"/>
/// 记录每次调用。展示:
/// <list type="bullet">
///   <item>配置 → 选项绑定(<c>builder.Services.Configure&lt;GreetingOptions&gt;(config.GetSection("Greeting"))</c>)</item>
///   <item>构造函数注入 <see cref="ILogger{TCategoryName}"/></item>
/// </list>
/// </summary>
public sealed class Greeter : IGreeter
{
    private readonly IOptions<GreetingOptions> _options;
    private readonly ILogger<Greeter> _logger;

    public Greeter(IOptions<GreetingOptions> options, ILogger<Greeter> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Hello(string name)
    {
        var opts = _options.Value;
        _logger.LogInformation("Greeter.Hello 被调用,name={Name}", name);
        return $"{opts.Message},{name}!  — {opts.Author}";
    }
}

/// <summary>
/// 绑定到 <c>appsettings.json</c> 的 <c>"Greeting"</c> 段。
/// </summary>
public sealed class GreetingOptions
{
    public string Message { get; set; } = "Hello";
    public string Author { get; set; } = "anon";
}
