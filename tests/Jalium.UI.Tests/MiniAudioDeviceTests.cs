using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// 验证 miniaudio backend 通过 P/Invoke 接通:device 能 open/close、能 submit/flush、
/// 能在没真启动播放的情况下生效 SetVolume,以及 SignalEndOfStream + 微量 submit
/// 在 Start 之后能让 PlaybackEnded 事件 fire 一次。
/// </summary>
/// <remarks>
/// 所有"需要真音频驱动"的检查都被包成 <see cref="TryOpenDevice"/> 模式:CI 上跑测试时
/// 如果系统返回 NoDevice/PlatformError 我们就视作"环境不支持",测试自然通过(不能假装失败)。
/// </remarks>
public class MiniAudioDeviceTests
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private static MiniAudioDevice? TryOpenDevice()
    {
        try
        {
            return new MiniAudioDevice(SampleRate, Channels);
        }
        catch (NativeMediaException ex) when (ex.Status is NativeMediaStatus.NoDevice
                                                      or NativeMediaStatus.PlatformError)
        {
            return null;
        }
    }

    [Fact]
    public void OpenClose_NoCrashEvenWithoutStart()
    {
        var device = TryOpenDevice();
        if (device is null) return; // CI 无音频设备,跳过
        device.Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var device = TryOpenDevice();
        if (device is null) return;
        device.Dispose();
        device.Dispose();
    }

    [Fact]
    public void SetVolume_Accepts_BeforeStart()
    {
        using var device = TryOpenDevice();
        if (device is null) return;
        device.SetVolume(0.0f);
        device.SetVolume(0.5f);
        device.SetVolume(1.0f);
    }

    [Fact]
    public void Submit_AcceptsSilence()
    {
        using var device = TryOpenDevice();
        if (device is null) return;
        var silence = new float[1024 * Channels];
        int written = device.Submit(silence);
        Assert.InRange(written, 1, silence.Length / Channels);
    }

    [Fact]
    public void Flush_AfterSubmit_DoesNotThrow()
    {
        using var device = TryOpenDevice();
        if (device is null) return;
        device.Submit(new float[256 * Channels]);
        device.Flush();
    }

    [Fact]
    public void Position_StartsAtZero()
    {
        using var device = TryOpenDevice();
        if (device is null) return;
        Assert.Equal(TimeSpan.Zero, device.Position);
    }

    [Fact]
    public void Factory_CreatesDistinctDevices()
    {
        var factory = new MiniAudioDeviceFactory();
        var d1 = factory.Create(SampleRate, Channels);
        if (d1 is null) return;
        var d2 = factory.Create(SampleRate, Channels);
        try
        {
            Assert.NotSame(d1, d2);
        }
        finally
        {
            d1.Dispose();
            d2?.Dispose();
        }
    }

    [Fact]
    public async Task PlaybackEnded_FiresAfterSubmitAndSignal()
    {
        using var device = TryOpenDevice();
        if (device is null) return;

        // 启动设备需要操作系统级音频驱动正常工作。如果失败(headless CI 等),静默跳过。
        try { device.Start(); }
        catch (NativeMediaException) { return; }

        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => fired.TrySetResult();
        device.PlaybackEnded += handler;

        try
        {
            // 提交一小段(~50 ms)PCM,然后告诉设备没后续数据。等 ring 排空后 ended 事件 fire。
            const int frames = SampleRate / 20; // 50 ms
            var silence = new float[frames * Channels];
            int written = device.Submit(silence);
            Assert.True(written > 0);

            device.SignalEndOfStream();

            // Do not block the xUnit worker while the managed callback is queued to the
            // ThreadPool. The full Linux suite can otherwise turn this into a worker
            // starvation check instead of an audio-ring drain check.
            await fired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            device.PlaybackEnded -= handler;
        }
    }
}
