using System.IO.Pipes;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// HotReloadAgent 传输层端到端（进程内）：环境变量激活 → 组件注册惰性起 agent →
/// 客户端按协议发帧 → ApplyPatch 真实改到已注册实例 → 回包可解析；二连验证 accept 循环。
/// 测试线程即"UI 线程"（Dispatcher.MainDispatcher 为 null 时 agent 在管道线程内联 ApplyPatch）。
public class HotReloadAgentTests
{
    private const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void Agent_EndToEnd_PatchesRegisteredInstanceOverPipe()
    {
        var pipeName = "jalium.hotreload.test." + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(HotReloadAgent.PipeEnvironmentVariable, pipeName);
        // 测试进程的 MainDispatcher 被捕获但不泵浦，BeginInvoke 永不执行——切内联应用（生产不设）。
        HotReloadAgent.ApplyInlineForTests = true;
        try
        {
            // 本测试专属根类型（ScrollViewer），防全局注册表跨测试串扰。
            var target = new ScrollViewer { Width = 10 };
            HotReloadRuntime.RegisterComponent(target);   // 触发 EnsureStarted：环境变量在 → 起 agent

            // 帧 1：patch Width=222 → 实例真实变化
            var first = SendFrame(pipeName,
                typeof(ScrollViewer).FullName!, "f.jalxaml",
                $"""<ScrollViewer xmlns="{Pres}" Width="222" />""");
            Assert.NotNull(first);
            Assert.Equal(0, first!.Value.Failed);
            Assert.True(first.Value.Updated >= 1, $"expected ≥1 updated, got {first.Value.Updated}");
            Assert.Equal(222, target.Width);

            // 帧 2（新连接）：验证一帧一连接的 accept 循环还活着
            var second = SendFrame(pipeName,
                typeof(ScrollViewer).FullName!, "f.jalxaml",
                $"""<ScrollViewer xmlns="{Pres}" Width="333" />""");
            Assert.NotNull(second);
            Assert.Equal(0, second!.Value.Failed);
            Assert.Equal(333, target.Width);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HotReloadAgent.PipeEnvironmentVariable, null);
        }
    }

    /// 与 IDE 客户端同款协议：三个 length-prefixed 串 → 读 "updated|fallback|failed|message"。
    /// agent 线程刚起，连接带重试（总预算 ~5s）。
    private static (int Updated, int Fallback, int Failed, string Message)? SendFrame(
        string pipeName, string xClass, string filePath, string content)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                client.Connect(500);
                break;
            }
            catch (TimeoutException) when (DateTime.UtcNow < deadline)
            {
                // agent 线程尚未进入 WaitForConnection：稍后重试
            }
        }

        // Same shared protocol the production server and the file watcher use (no hand-rolled framing).
        HotReloadProtocol.WriteRequest(client, xClass, filePath, content);
        var result = HotReloadProtocol.ReadResult(client);
        return (result.UpdatedElements, result.FallbackReplacements, result.FailedElements, result.Message);
    }
}
