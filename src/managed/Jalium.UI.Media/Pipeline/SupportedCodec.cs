namespace Jalium.UI.Media.Pipeline;

/// <summary>
/// 平台原生硬件 / 软件解码器能力。原生层 <c>jalium_media_supported_video_codecs()</c>
/// 启动时探测，C# 侧映射为标志位。
/// </summary>
[Flags]
public enum SupportedCodec
{
    /// <summary>无任何解码器可用。</summary>
    None = 0,

    /// <summary>H.264 / AVC。</summary>
    H264 = 1 << 0,

    /// <summary>H.265 / HEVC。</summary>
    Hevc = 1 << 1,

    /// <summary>VP9。</summary>
    Vp9 = 1 << 2,

    /// <summary>AV1。</summary>
    Av1 = 1 << 3,

    /// <summary>AAC 音频。</summary>
    Aac = 1 << 8,

    /// <summary>Opus 音频。</summary>
    Opus = 1 << 9,

    /// <summary>MP3 音频。</summary>
    Mp3 = 1 << 10,
}
