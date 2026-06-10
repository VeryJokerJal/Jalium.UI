using System.Buffers.Binary;
using Jalium.UI.Media.Native;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// 端到端验证 telemetry:
/// 1. <see cref="AudioStats.Reset"/> 后所有 counter 归零(version 除外)。
/// 2. <see cref="AudioStats.Version"/> 与 native 端 JALIUM_AUDIO_STATS_VERSION (1) 对齐。
/// 3. 开/关 N 个 WAV decoder,delta 完全反映在 decoders_opened/closed + frames_decoded_wav。
/// </summary>
/// <remarks>
/// 这些 counter 是进程级共享 atomic counter,跨 test 累计。为隔离起见每个测试开头
/// 都先 <see cref="AudioStats.Reset"/>,但仍要假设其他 test 可能在并行 thread 中
/// 同时改 counter 因此用 >= 而非 == 断言开/关比例。Xunit collections 默认 sequential。
/// </remarks>
[Collection(AudioStatsExclusiveCollection.Name)]
public class AudioStatsTests
{
    private const int SampleRate = 44100;
    private const int Channels = 1;

    private static byte[] BuildSineWav(int sampleRate, int channels, double seconds, double freq)
    {
        const int bitsPerSample = 16;
        int totalFrames = (int)Math.Round(sampleRate * seconds);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int dataSize = totalFrames * blockAlign;
        int riffSize = 36 + dataSize;

        var buf = new byte[44 + dataSize];
        buf[0] = (byte)'R'; buf[1] = (byte)'I'; buf[2] = (byte)'F'; buf[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), riffSize);
        buf[8] = (byte)'W'; buf[9] = (byte)'A'; buf[10] = (byte)'V'; buf[11] = (byte)'E';
        buf[12] = (byte)'f'; buf[13] = (byte)'m'; buf[14] = (byte)'t'; buf[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(20, 2), (short)1);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(22, 2), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(34, 2), (short)bitsPerSample);
        buf[36] = (byte)'d'; buf[37] = (byte)'a'; buf[38] = (byte)'t'; buf[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(40, 4), dataSize);
        double twoPi = 2.0 * Math.PI;
        for (int f = 0; f < totalFrames; f++)
        {
            short s = (short)(Math.Sin(twoPi * 440.0 * f / sampleRate) * 16000);
            for (int ch = 0; ch < channels; ch++)
            {
                int o = 44 + (f * channels + ch) * 2;
                BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(o, 2), s);
            }
        }
        return buf;
    }

    [Fact]
    public void Reset_ZerosAllCountersExceptVersion()
    {
        AudioStats.Reset();
        var s = AudioStats.Query();

        Assert.True(s.Version > 0, "version should be set by native");
        Assert.Equal(0u, s.DecodersOpened);
        Assert.Equal(0u, s.DecodersClosed);
        Assert.Equal(0u, s.DevicesOpened);
        Assert.Equal(0u, s.DevicesClosed);
        Assert.Equal(0u, s.FramesDecodedWav);
        Assert.Equal(0u, s.FramesDecodedFlac);
        Assert.Equal(0u, s.FramesDecodedMp3);
        Assert.Equal(0u, s.FramesDecodedOgg);
        Assert.Equal(0u, s.FramesDecodedAac);
        Assert.Equal(0u, s.FramesSubmitted);
        Assert.Equal(0u, s.FramesPlayed);
        Assert.Equal(0u, s.RingUnderruns);
        Assert.Equal(0u, s.RingOverflows);
        Assert.Equal(0u, s.CodecProbeFailures);
        Assert.Equal(0u, s.PlatformAacFallbacks);
    }

    [Fact]
    public void Version_MatchesNativeContract()
    {
        AudioStats.Reset();
        var s = AudioStats.Query();
        // JALIUM_AUDIO_STATS_VERSION = 1u (jalium_audio.h)
        Assert.Equal(1u, s.Version);
    }

    [Fact]
    public void DecoderOpenClose_RegistersInCounters()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"jalium_stats_{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, BuildSineWav(SampleRate, Channels, 0.1, 440.0));
        try
        {
            AudioStats.Reset();
            const int N = 5;
            for (int i = 0; i < N; i++)
            {
                using var d = new NativeAudioDecoder();
                d.Open(path);
            }
            var s = AudioStats.Query();
            Assert.Equal((ulong)N, s.DecodersOpened);
            Assert.Equal((ulong)N, s.DecodersClosed);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void DecoderReadFrames_AccumulatesWavFrameCount()
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"jalium_stats_frames_{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, BuildSineWav(SampleRate, Channels, 1.0, 440.0));  // 1 second
        try
        {
            AudioStats.Reset();
            using var d = new NativeAudioDecoder();
            d.Open(path);
            var buf = new float[2048 * Channels];
            long totalRead = 0;
            int got;
            while ((got = d.ReadFrames(buf)) > 0) totalRead += got;

            var s = AudioStats.Query();
            // dr_wav 全程返回 SampleRate(44100) 帧,allow ±2 jitter
            Assert.InRange(s.FramesDecodedWav, (ulong)(SampleRate - 2), (ulong)(SampleRate + 2));
            Assert.Equal((ulong)totalRead, s.FramesDecodedWav);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
