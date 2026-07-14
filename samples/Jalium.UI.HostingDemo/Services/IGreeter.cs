namespace Jalium.UI.HostingDemo.Services;

/// <summary>
/// 一个极简的问候服务 —— 演示通过 <c>builder.Services.AddSingleton&lt;IGreeter, Greeter&gt;()</c>
/// 注册,然后在 <see cref="ViewModels.HomeViewModel"/> 构造函数里作为依赖被解析。
/// </summary>
public interface IGreeter
{
    /// <summary>
    /// 基于配置的问候语生成一句带作者署名的文字。
    /// </summary>
    string Hello(string name);
}
