namespace Jalium.UI.Media.Native;

/// <summary>
/// <see cref="jalium.native.media"/> 音频子系统的 telemetry 快照,
/// 由原生层的 lock-free atomic counters 持续累计;调用 <see cref="Query"/>
/// 拿到当前值,调用 <see cref="Reset"/> 把所有 counter 归零。
/// </summary>
/// <remarks>
/// <para>对应 native ABI 中 <c>jalium_audio_stats_t</c>(见
/// <c>src/native/jalium.native.media.core/include/jalium_audio.h</c>)。</para>
/// <para><see cref="Version"/> 由 native 端写入(目前为
/// <c>JALIUM_AUDIO_STATS_VERSION</c>),用于将来加字段时检测 ABI 兼容性。
/// 现在不主动校验,但 DevTools / 调试面板可以借此识别老版本。</para>
/// </remarks>
public readonly record struct AudioStats(
    ulong Version,
    ulong DecodersOpened,
    ulong DecodersClosed,
    ulong DevicesOpened,
    ulong DevicesClosed,
    ulong FramesDecodedWav,
    ulong FramesDecodedFlac,
    ulong FramesDecodedMp3,
    ulong FramesDecodedOgg,
    ulong FramesDecodedAac,
    ulong FramesSubmitted,
    ulong FramesPlayed,
    ulong RingUnderruns,
    ulong RingOverflows,
    ulong CodecProbeFailures,
    ulong PlatformAacFallbacks)
{
    /// <summary>
    /// 拍一张当前 telemetry 快照。调用前确保 native 音频子系统已 init
    /// (本方法内部触发);如果 native dll 不可用会抛 <see cref="NativeMediaException"/>。
    /// </summary>
    public static AudioStats Query()
    {
        NativeMediaInitializer.EnsureInitialized();
        NativeAudioDecoder.EnsureAudioInitialized();

        NativeAudioInterop.jalium_query_audio_stats(out var n);
        return new AudioStats(
            Version:               n.Version,
            DecodersOpened:        n.DecodersOpened,
            DecodersClosed:        n.DecodersClosed,
            DevicesOpened:         n.DevicesOpened,
            DevicesClosed:         n.DevicesClosed,
            FramesDecodedWav:      n.FramesDecodedWav,
            FramesDecodedFlac:     n.FramesDecodedFlac,
            FramesDecodedMp3:      n.FramesDecodedMp3,
            FramesDecodedOgg:      n.FramesDecodedOgg,
            FramesDecodedAac:      n.FramesDecodedAac,
            FramesSubmitted:       n.FramesSubmitted,
            FramesPlayed:          n.FramesPlayed,
            RingUnderruns:         n.RingUnderruns,
            RingOverflows:         n.RingOverflows,
            CodecProbeFailures:    n.CodecProbeFailures,
            PlatformAacFallbacks:  n.PlatformAacFallbacks);
    }

    /// <summary>把所有 counter 归零。诊断面板在采样开始前调一次,得到的下次
    /// <see cref="Query"/> 就是 delta(无需自己减基线)。</summary>
    public static void Reset()
    {
        NativeMediaInitializer.EnsureInitialized();
        NativeAudioDecoder.EnsureAudioInitialized();
        NativeAudioInterop.jalium_reset_audio_stats();
    }
}
