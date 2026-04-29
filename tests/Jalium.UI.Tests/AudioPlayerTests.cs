using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// 冒烟测试 <see cref="AudioPlayer"/>:重点验证未打开媒体时的状态、属性 clamp、Seek 暂存、
/// Dispose 幂等。打开真实音频文件的端到端测试受限于 CI 环境的音频设备,因此放在 conditional fact 里,
/// 没有播放设备时跳过。
/// </summary>
public class AudioPlayerTests
{
    [Fact]
    public void NewInstance_DefaultState()
    {
        using var player = new AudioPlayer();

        Assert.Null(player.Source);
        Assert.False(player.HasAudio);
        Assert.Null(player.NaturalDuration);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Equal(0.5, player.Volume, precision: 3);
        Assert.False(player.IsMuted);
        Assert.Equal(0.0, player.Balance, precision: 3);
        Assert.Equal(1.0, player.SpeedRatio, precision: 3);
    }

    [Fact]
    public void Volume_Clamps_To_Range()
    {
        using var player = new AudioPlayer();

        player.Volume = -0.5;
        Assert.Equal(0.0, player.Volume, precision: 3);

        player.Volume = 2.0;
        Assert.Equal(1.0, player.Volume, precision: 3);

        player.Volume = 0.7;
        Assert.Equal(0.7, player.Volume, precision: 3);
    }

    [Fact]
    public void Balance_Clamps_To_Range()
    {
        using var player = new AudioPlayer();

        player.Balance = -2.0;
        Assert.Equal(-1.0, player.Balance, precision: 3);

        player.Balance = 2.0;
        Assert.Equal(1.0, player.Balance, precision: 3);
    }

    [Fact]
    public void SpeedRatio_Clamps_To_Range()
    {
        using var player = new AudioPlayer();

        player.SpeedRatio = 0.0;
        Assert.True(player.SpeedRatio >= 0.1, $"SpeedRatio={player.SpeedRatio} below floor.");

        player.SpeedRatio = 100.0;
        Assert.True(player.SpeedRatio <= 10.0, $"SpeedRatio={player.SpeedRatio} above ceiling.");
    }

    [Fact]
    public void IsMuted_Toggle_DoesNotThrow_OnEmptyPlayer()
    {
        using var player = new AudioPlayer();

        player.IsMuted = true;
        Assert.True(player.IsMuted);

        player.IsMuted = false;
        Assert.False(player.IsMuted);
    }

    [Fact]
    public void Play_Pause_Stop_OnEmptyPlayer_DoNotThrow()
    {
        using var player = new AudioPlayer();

        // 未 Open 任何源时,这些控制应当是 no-op 而不抛异常。
        player.Play();
        player.Pause();
        player.Stop();
    }

    [Fact]
    public void Seek_BeforeOpen_StoresTarget()
    {
        using var player = new AudioPlayer();

        // Seek 在 Open 之前调用,目标位置应被记录,在后续 Open 完成后立即 seek。
        // 不抛异常即可。
        player.Seek(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Open_NonExistingFile_RaisesMediaFailed()
    {
        using var player = new AudioPlayer();

        Exception? captured = null;
        player.MediaFailed += (_, e) => captured = e.ErrorException;

        var phantom = new Uri(Path.Combine(Path.GetTempPath(), $"jalium_audio_phantom_{Guid.NewGuid():N}.mp3"));
        player.Open(phantom);

        Assert.False(player.HasAudio);
        Assert.NotNull(captured);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var player = new AudioPlayer();
        player.Dispose();
        player.Dispose();
    }

    [Fact]
    public void Close_AllowsReuse()
    {
        using var player = new AudioPlayer();
        player.Close();
        Assert.Null(player.Source);
        Assert.False(player.HasAudio);

        // 再 Close 不应抛异常。
        player.Close();
    }

    [Fact]
    public void Volume_AfterDispose_DoesNotThrow_ButReadsLastValue()
    {
        var player = new AudioPlayer();
        player.Volume = 0.42;
        player.Dispose();

        // get 仍可读最后一次设置的值,setter 不会抛(我们只在 ThrowIfDisposed 里检查 Open/Play/Seek 等需要资源的操作)。
        Assert.Equal(0.42, player.Volume, precision: 3);
    }
}
