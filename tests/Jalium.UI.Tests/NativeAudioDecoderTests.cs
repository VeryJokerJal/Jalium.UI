using System.Buffers.Binary;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// 端到端验证 native WAV 解码器:把代码合成的 sine 440Hz/44.1kHz/mono/16-bit PCM
/// 写成临时 .wav 文件,通过 jalium.native.media -> dr_wav 解出来,
/// 校验 sample_rate / channels / duration / EOS / Seek 行为。
/// </summary>
public class NativeAudioDecoderTests : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 1;
    private const double DurationSeconds = 1.0;
    private const double FrequencyHz = 440.0;

    private readonly string _wavPath;

    public NativeAudioDecoderTests()
    {
        _wavPath = Path.Combine(Path.GetTempPath(),
            $"jalium_audio_sine_{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(_wavPath, BuildSineWav(SampleRate, Channels, DurationSeconds, FrequencyHz));
    }

    public void Dispose()
    {
        try { File.Delete(_wavPath); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Open_PopulatesMetadata()
    {
        using var decoder = new NativeAudioDecoder();
        decoder.Open(_wavPath);

        Assert.Equal(SampleRate, decoder.SampleRate);
        Assert.Equal(Channels, decoder.Channels);
        Assert.Equal(SupportedAudioCodec.Wav, decoder.Codec);
        Assert.InRange(decoder.Duration.TotalSeconds, 0.99, 1.01);
    }

    [Fact]
    public void ReadFrames_ReadsExpectedTotalAndReturnsZeroAtEos()
    {
        using var decoder = new NativeAudioDecoder();
        decoder.Open(_wavPath);

        var buffer = new float[2048 * Channels];
        long total = 0;
        int read;
        do
        {
            read = decoder.ReadFrames(buffer);
            total += read;
        } while (read > 0 && total < SampleRate * 4);

        // dr_wav 报 totalPCMFrameCount 包含尾部静音填充,允许 ±2 帧抖动。
        Assert.InRange(total, SampleRate - 2, SampleRate + 2);

        // 已到 EOS,再读应为 0。
        Assert.Equal(0, decoder.ReadFrames(buffer));
    }

    [Fact]
    public void Seek_BackToStart_AllowsReread()
    {
        using var decoder = new NativeAudioDecoder();
        decoder.Open(_wavPath);

        var buffer = new float[2048 * Channels];

        // 先把流读到 EOS。
        while (decoder.ReadFrames(buffer) > 0) { }

        decoder.Seek(TimeSpan.Zero);
        int afterSeek = decoder.ReadFrames(buffer);

        Assert.True(afterSeek > 0, "Seek(0) 后第一次 ReadFrames 必须有数据。");
    }

    [Fact]
    public void Open_NonExistingFile_Throws()
    {
        using var decoder = new NativeAudioDecoder();
        var phantom = Path.Combine(Path.GetTempPath(), $"jalium_phantom_{Guid.NewGuid():N}.wav");

        var ex = Assert.Throws<NativeMediaException>(() => decoder.Open(phantom));
        Assert.Equal(NativeMediaStatus.IoError, ex.Status);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var decoder = new NativeAudioDecoder();
        decoder.Open(_wavPath);
        decoder.Dispose();
        decoder.Dispose();  // 不应抛
    }

    [Fact]
    public void Factory_CreatesIndependentInstances()
    {
        var factory = new NativeAudioDecoderFactory();
        using var d1 = factory.Create();
        using var d2 = factory.Create();
        Assert.NotSame(d1, d2);
    }

    // -----------------------------------------------------------------------
    // Codec dispatch routing — 给 .flac/.mp3/.ogg 扩展名但塞 WAV 内容,验证 codec
    // 路由真的把这些 hint 路由到对应 decoder(返回 DecodeFailed,说明 dr_flac/
    // minimp3/stb_vorbis 真被调过),而不是返回 NotImplemented(未接 dispatch)
    // 或 UnsupportedCodec(probe 失败)。AAC 因为还没接 platform 桥,期望保持
    // NotImplemented。
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(".flac")]
    [InlineData(".mp3")]
    [InlineData(".ogg")]
    public void Codec_DispatchByExtension_ReachesCodec(string ext)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"jalium_dispatch_{Guid.NewGuid():N}{ext}");
        // 用 WAV bytes 但伪装成其他扩展名,让 codec dispatch 选错 codec。
        File.WriteAllBytes(path, BuildSineWav(SampleRate, Channels, 0.1, FrequencyHz));
        try
        {
            using var decoder = new NativeAudioDecoder();
            // 关键断言: codec dispatch 必须真路由到对应 codec 而非走 NotImplemented
            // 或 UnsupportedCodec 分支。具体 codec 接受 WAV 内容与否(dr_flac/
            // stb_vorbis 严格拒绝,minimp3 宽容)对这一证明无影响。
            try
            {
                decoder.Open(path);
            }
            catch (NativeMediaException ex)
            {
                Assert.NotEqual(NativeMediaStatus.NotImplemented, ex.Status);
                Assert.NotEqual(NativeMediaStatus.UnsupportedCodec, ex.Status);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Open_UnicodePath_DecodesNormally()
    {
        // Gallery 报告:中文文件名加载后无声。根因是 dr_wav / dr_flac / minimp3 /
        // stb_vorbis 通过 libc fopen() 打开文件,Windows 上 fopen 用 ANSI codepage
        // 解 UTF-8 path 字节,任何非 ASCII 字符(CJK / 重音 / emoji 都算)都会让
        // 文件名解错、找不到文件 → DecodeFailed。修复在 audio_decoder.cpp 入口
        // 统一通过 MultiByteToWideChar + _wfopen_s 读全文件再走 OpenMemory。
        // 这个测试用真实中文 + 空格 + 特殊符号路径确认修复成立。
        var dir = Path.Combine(Path.GetTempPath(), $"jalium_unicode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "团子在线混音 - Two Steps (1).wav");
        File.WriteAllBytes(path, BuildSineWav(SampleRate, Channels, 0.5, FrequencyHz));
        try
        {
            using var decoder = new NativeAudioDecoder();
            decoder.Open(path);

            Assert.Equal(SupportedAudioCodec.Wav, decoder.Codec);
            Assert.Equal(SampleRate, decoder.SampleRate);
            Assert.InRange(decoder.Duration.TotalSeconds, 0.49, 0.51);

            // 真读出帧确认 codec 拿到的指针有效(我们 move 了源 vector,data() 必须仍可用)。
            var buf = new float[2048 * Channels];
            int read = decoder.ReadFrames(buf);
            Assert.True(read > 0, "Read should not return 0 immediately after open on a non-empty WAV.");
        }
        finally
        {
            try { File.Delete(path); Directory.Delete(dir); } catch { /* best-effort */ }
        }
    }

    [Theory]
    [InlineData(".m4a")]
    [InlineData(".aac")]
    public void Codec_AacHint_DispatchesToPlatformBridge(string ext)
    {
        // Step 4a 之后:Windows 上 AAC 路由到 win_mf_aac_decoder。喂一个不存在的
        // .m4a/.aac,期望抛 NativeMediaException 但 status 不是 NotImplemented
        // —— 这证明 hook 已注册、dispatch 真路由到 MF 桥(MF 找不到文件返回 IoError)。
        // Linux/macOS/Android 暂未做 AAC 桥,这测试在那些平台上仍会期望 NotImplemented;
        // 用 RuntimeInformation 区分。
        var phantom = Path.Combine(Path.GetTempPath(),
            $"jalium_aac_phantom_{Guid.NewGuid():N}{ext}");

        using var decoder = new NativeAudioDecoder();
        var ex = Assert.Throws<NativeMediaException>(() => decoder.Open(phantom));

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            Assert.NotEqual(NativeMediaStatus.NotImplemented, ex.Status);
            Assert.NotEqual(NativeMediaStatus.UnsupportedCodec, ex.Status);
        }
        else
        {
            // 其他平台 AAC 桥还没接(plan Step 4b/c/d),仍是 NotImplemented。
            Assert.Equal(NativeMediaStatus.NotImplemented, ex.Status);
        }
    }

    // -----------------------------------------------------------------------
    // 工具:用代码合成 RIFF/WAVE/PCM16 mono sine
    // -----------------------------------------------------------------------

    private static byte[] BuildSineWav(int sampleRate, int channels, double seconds, double freq)
    {
        const int bitsPerSample = 16;
        int totalFrames = (int)Math.Round(sampleRate * seconds);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int dataSize = totalFrames * blockAlign;
        int riffSize = 36 + dataSize;

        var buf = new byte[44 + dataSize];
        // RIFF header
        buf[0] = (byte)'R'; buf[1] = (byte)'I'; buf[2] = (byte)'F'; buf[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), riffSize);
        buf[8] = (byte)'W'; buf[9] = (byte)'A'; buf[10] = (byte)'V'; buf[11] = (byte)'E';
        // fmt chunk
        buf[12] = (byte)'f'; buf[13] = (byte)'m'; buf[14] = (byte)'t'; buf[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(16, 4), 16);                // subchunk1 size
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(20, 2), (short)1);          // PCM
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(22, 2), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(32, 2), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(34, 2), (short)bitsPerSample);
        // data chunk
        buf[36] = (byte)'d'; buf[37] = (byte)'a'; buf[38] = (byte)'t'; buf[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(40, 4), dataSize);

        // PCM16 samples (interleaved if channels > 1; here mono).
        double twoPi = 2.0 * Math.PI;
        for (int frame = 0; frame < totalFrames; frame++)
        {
            double t = (double)frame / sampleRate;
            short s = (short)(Math.Sin(twoPi * freq * t) * 16000);  // 容量 0.488 of full-scale
            for (int ch = 0; ch < channels; ch++)
            {
                int o = 44 + (frame * channels + ch) * 2;
                BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(o, 2), s);
            }
        }
        return buf;
    }
}
