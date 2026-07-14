namespace Jalium.UI.Media.Pipeline;

/// <summary>
/// 平台原生音频解码器抽象。WAV/FLAC/MP3/OGG 由仓库内 vendored 单头库
/// （dr_wav / dr_flac / minimp3 / stb_vorbis）软解；AAC 由各平台原生 codec
/// 桥接（Windows Media Foundation / Android MediaCodec / Apple AudioToolbox
/// / Linux GStreamer）。
/// </summary>
/// <remarks>
/// <para>
/// <b>单线程契约</b>：同一实例只能由一根线程访问（典型为 <see cref="AudioPlayer"/>
/// 的 pump worker）。实现内部不上锁。
/// </para>
/// <para>
/// 输出格式恒为 interleaved 32-bit float，采样率 / 声道数沿用源原生值
/// （重采样在 <see cref="INativeAudioDevice"/> 一侧或 <see cref="AudioPlayer"/>
/// 的处理链里完成）。
/// </para>
/// </remarks>
public interface INativeAudioDecoder : IDisposable
{
    /// <summary>
    /// 打开本地音频文件。
    /// </summary>
    /// <param name="utf8Path">本地文件路径。</param>
    /// <exception cref="System.IO.FileNotFoundException">文件不存在或无法读取。</exception>
    /// <exception cref="System.NotSupportedException">本平台未实现对该格式的解码。</exception>
    void Open(string utf8Path);

    /// <summary>源原生采样率（Hz）。仅在 <c>Open</c> 成功后有效。</summary>
    int SampleRate { get; }

    /// <summary>源声道数（1..8）。仅在 <c>Open</c> 成功后有效。</summary>
    int Channels { get; }

    /// <summary>音频总时长；不可知时返回 <see cref="System.TimeSpan.Zero"/>。</summary>
    System.TimeSpan Duration { get; }

    /// <summary>实际探测/解码的编码格式。</summary>
    SupportedAudioCodec Codec { get; }

    /// <summary>
    /// 解码下一批 interleaved float 帧到 <paramref name="dst"/>。一帧 =
    /// <see cref="Channels"/> 个 float。
    /// </summary>
    /// <param name="dst">写入缓冲，长度须为 <see cref="Channels"/> 的整数倍。</param>
    /// <returns>实际写入的帧数。0 = EOS。可能小于 <c>dst.Length / Channels</c>，
    /// 调用方应循环到收到 0 为止。</returns>
    int ReadFrames(System.Span<float> dst);

    /// <summary>跳转到指定播放位置。超过 <see cref="Duration"/> 时夹紧到末尾。</summary>
    void Seek(System.TimeSpan position);
}

/// <summary>
/// <see cref="INativeAudioDecoder"/> 的具体实现枚举。与 <see cref="SupportedCodec"/>
/// （视频/平台硬件支持矩阵）有意分离——本枚举是 decoder 实例自报的单值，
/// 而 <see cref="SupportedCodec"/> 是平台能力集合的 Flags。
/// </summary>
public enum SupportedAudioCodec
{
    /// <summary>未识别或未打开。</summary>
    Unknown = 0,
    Wav = 1,
    Flac = 2,
    Mp3 = 3,
    Ogg = 4,
    Aac = 5,
}

/// <summary>
/// <see cref="INativeAudioDecoder"/> 工厂。注入到 <see cref="AudioPlayer"/>。
/// </summary>
public interface INativeAudioDecoderFactory
{
    /// <summary>创建一个未打开的解码器实例。</summary>
    INativeAudioDecoder Create();
}

/// <summary>
/// Optional decoder contract for containers with more than one audio stream.
/// Set the zero-based index before calling <see cref="INativeAudioDecoder.Open"/>.
/// </summary>
public interface INativeAudioTrackSelector
{
    int AudioTrackIndex { get; set; }
}
