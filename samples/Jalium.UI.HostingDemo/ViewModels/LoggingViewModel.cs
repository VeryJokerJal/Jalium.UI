using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 展示 <c>builder.Logging</c> 的终点:VM 构造函数注入 <see cref="ILogger{TCategoryName}"/>,
/// 提供几个按钮让用户按不同等级输出日志,顺便把最近日志回填到 UI。
/// </summary>
public sealed class LoggingViewModel : ViewModelBase
{
    private readonly ILogger<LoggingViewModel> _logger;
    private string _lastEntry = "（尚无日志）";

    public LoggingViewModel(ILogger<LoggingViewModel> logger)
    {
        _logger = logger;
    }

    public string LastEntry
    {
        get => _lastEntry;
        private set => SetProperty(ref _lastEntry, value);
    }

    public void LogTrace()
    {
        _logger.LogTrace("Trace 级别 @ {At}", DateTime.Now);
        LastEntry = "[Trace] 写入控制台,通常在生产被过滤";
    }

    public void LogDebug()
    {
        _logger.LogDebug("Debug 级别 @ {At}", DateTime.Now);
        LastEntry = "[Debug] 写入控制台";
    }

    public void LogInformation()
    {
        _logger.LogInformation("Information 级别 @ {At}", DateTime.Now);
        LastEntry = "[Info] 写入控制台";
    }

    public void LogWarning()
    {
        _logger.LogWarning("Warning 级别 @ {At}", DateTime.Now);
        LastEntry = "[Warn] 写入控制台";
    }

    public void LogError()
    {
        try
        {
            throw new InvalidOperationException("模拟异常");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "捕获到异常 @ {At}", DateTime.Now);
            LastEntry = $"[Error] {ex.Message}(异常已写入控制台)";
        }
    }
}
