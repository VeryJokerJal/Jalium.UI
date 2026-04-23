using System.Diagnostics;

namespace Jalium.UI.Markup;

/// <summary>
/// 统一的 JALXAML 加载诊断通道,默认静默。打开后会把关键环节(XmlnsDefinition 扫描、
/// force-load、XAML 资源查找、ResourceDictionary 解析、.uic 加载与 fallback)以 <c>[Jalium.UI.Xaml]</c>
/// 前缀写入 <see cref="Debug.WriteLine(string)"/>,可在 Visual Studio 的 Output / Debug 窗口里过滤查看。
/// </summary>
/// <remarks>
/// <para>启用方式(任选其一,以先到者为准):</para>
/// <list type="bullet">
///   <item>环境变量 <c>JALIUM_XAML_TRACE=1</c></item>
///   <item>程序启动早期设置 <see cref="Enabled"/> <c>= true</c></item>
/// </list>
/// <para>
/// 设计原则:<see cref="Enabled"/> 关闭时所有 <c>Log</c> 调用零开销(先布尔短路,不做插值)。
/// 因此调用点都使用 <see cref="string.Format(string, object?[])"/> 重载或手动 <c>if (Enabled)</c> 守卫。
/// </para>
/// </remarks>
public static class JalxamlDiagnostics
{
    private static int _enabled = InitEnabledFromEnv();

    private static int InitEnabledFromEnv()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable("JALIUM_XAML_TRACE");
            if (!string.IsNullOrEmpty(value)
                && (value.Equals("1", StringComparison.Ordinal)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("on", StringComparison.OrdinalIgnoreCase)))
            {
                return 1;
            }
        }
        catch
        {
            // 环境变量访问被沙箱拒绝等情况,按 disabled 处理。
        }
        return 0;
    }

    /// <summary>
    /// 启用/关闭诊断日志。线程安全。
    /// </summary>
    public static bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    /// <summary>
    /// 写一条无格式化参数的日志。开关关闭时零开销。
    /// </summary>
    public static void Log(string message)
    {
        if (Enabled)
        {
            Debug.WriteLine($"[Jalium.UI.Xaml] {message}");
        }
    }

    /// <summary>
    /// 写一条带格式化参数的日志。开关关闭时不做字符串插值,保持零开销。
    /// </summary>
    public static void Log(string format, object? arg0)
    {
        if (Enabled)
        {
            Debug.WriteLine($"[Jalium.UI.Xaml] {string.Format(format, arg0)}");
        }
    }

    /// <summary>
    /// 写一条带格式化参数的日志。开关关闭时不做字符串插值,保持零开销。
    /// </summary>
    public static void Log(string format, object? arg0, object? arg1)
    {
        if (Enabled)
        {
            Debug.WriteLine($"[Jalium.UI.Xaml] {string.Format(format, arg0, arg1)}");
        }
    }

    /// <summary>
    /// 写一条带格式化参数的日志。开关关闭时不做字符串插值,保持零开销。
    /// </summary>
    public static void Log(string format, object? arg0, object? arg1, object? arg2)
    {
        if (Enabled)
        {
            Debug.WriteLine($"[Jalium.UI.Xaml] {string.Format(format, arg0, arg1, arg2)}");
        }
    }

    /// <summary>
    /// 写一条带格式化参数的日志。开关关闭时不做字符串插值,保持零开销。
    /// </summary>
    public static void Log(string format, params object?[] args)
    {
        if (Enabled)
        {
            Debug.WriteLine($"[Jalium.UI.Xaml] {string.Format(format, args)}");
        }
    }
}
