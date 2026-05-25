using System.Buffers;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media;

/// <summary>
/// 独立的纯音频播放器,语义对齐 WPF <c>System.Windows.Media.MediaPlayer</c> 的音频面.
/// 跨平台支持 wav / flac / mp3 / ogg / aac / m4a(AAC 由平台原生 codec 桥接 ——
/// Windows Media Foundation / Android MediaCodec / Apple AudioToolbox / Linux GStreamer)。
/// </summary>
/// <remarks>
/// <para>
/// 与控件解耦:不需要挂在可视树中,适合从 ViewModel / Service / 后台线程直接驱动。
/// </para>
/// <para>
/// 内部架构:每个实例独占一对 <see cref="INativeAudioDecoder"/> +
/// <see cref="INativeAudioDevice"/>,中间夹一个可选 <see cref="WsolaSpeedProcessor"/>
/// 做时域伸缩(SpeedRatio != 1 时)。一个后台 pump 线程负责
/// <c>decoder.ReadFrames()  →  [WSOLA]  →  device.Submit()</c> 的搬运。
/// </para>
/// <para>
/// SoundFlow 已于 2026 年自研化:全部音频解码与播放走 <c>jalium.native.media</c>
/// 提供的 C ABI,无第三方运行时依赖。
/// </para>
/// </remarks>
public sealed class AudioPlayer : IDisposable
{
    // ── 输出参数 ──
    // 设备输出格式跟随源音频:device 在 Open 时按 decoder 报告的 sample_rate /
    // channels 创建,由 miniaudio(WASAPI shared 模式)做硬件重采样到设备原生
    // 速率。
    //
    // 否则:48 kHz 源音频被强行送 44.1 kHz device 会被当作慢速播放,人耳听到的
    // 音高低 ~8%、速度慢 ~8%(用户报告"视频音频跟不上视频速度"),反之 22.05 kHz
    // 源在 44.1 kHz device 上会快放并升高音高。
    //
    // 万一 decoder 没解出 sample_rate / channels(罕见),fallback 这俩常量保证
    // device 仍能开起来。
    private const int FALLBACK_SAMPLE_RATE = 44100;
    private const int FALLBACK_CHANNELS = 2;
    private const int PUMP_BLOCK_FRAMES = 2048;        // ~46 ms @ 44.1kHz

    // ── 默认工厂(可被 DI 替换的 hook 留到 Step 7 telemetry/cutover 配套)──
    private static readonly INativeAudioDecoderFactory s_decoderFactory = new NativeAudioDecoderFactory();
    private static readonly INativeAudioDeviceFactory  s_deviceFactory  = new MiniAudioDeviceFactory();

    /// <summary>
    /// 兼容旧版 API:历史上用来主动释放 SoundFlow 进程共享引擎。新原生栈无此概念
    /// (miniaudio 上下文随 dll 卸载自动收尾),保留为 no-op 让既有调用方不破。
    /// </summary>
    public static void ShutdownSharedEngine()
    {
        // No-op since the SoundFlow cutover. jalium.native.media tracks its own
        // refcount via jalium_audio_initialize / jalium_audio_shutdown which is
        // already driven by NativeAudioDecoder.EnsureAudioInitialized.
    }

    // ── 实例状态 ──
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _resumeGate = new(false);

    private INativeAudioDecoder?  _decoder;
    private INativeAudioDevice?   _device;
    private WsolaSpeedProcessor?  _speedProcessor;
    private Thread?               _pumpThread;
    private CancellationTokenSource? _pumpCts;

    private bool _disposed;
    private NativePlaybackState _state = NativePlaybackState.Stopped;
    private string? _currentFilePath;

    // 当前 device 实际配置的输出 rate / channels(== source 的 sample rate / channels)。
    // submit 的 ReadOnlySpan<float> 用 _outputChannels 切分 frame。
    private int _outputSampleRate = FALLBACK_SAMPLE_RATE;
    private int _outputChannels   = FALLBACK_CHANNELS;

    private double _volume = 0.5;
    private bool   _isMuted;
    private double _balance;
    private double _speedRatio = 1.0;
    private TimeSpan _naturalDuration;
    private TimeSpan _seekTarget;
    private bool _hasSeekTarget;
    private bool _hasMedia;

    // Position 计算的 anchor:Open / Seek / SpeedRatio 改变时更新,避免 device 累计帧数
    // 漏到流位置。
    private TimeSpan _streamPosAtAnchor;
    private TimeSpan _devicePosAtAnchor;

    // ── 公共属性(API 表面与 SoundFlow 版本保持一致)──

    /// <summary>当前媒体源(只读)。设置请走 <see cref="Open"/>。</summary>
    public Uri? Source { get; private set; }

    /// <summary>是否打开了有效音频流。</summary>
    public bool HasAudio { get { lock (_lock) return _hasMedia; } }

    /// <summary>音量 0.0 ~ 1.0。</summary>
    public double Volume
    {
        get { lock (_lock) return _volume; }
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            lock (_lock)
            {
                _volume = clamped;
                ApplyVolumeLocked();
            }
        }
    }

    /// <summary>静音开关。内部仍持有原 <see cref="Volume"/>,关闭静音后恢复。</summary>
    public bool IsMuted
    {
        get { lock (_lock) return _isMuted; }
        set
        {
            lock (_lock)
            {
                if (_isMuted == value) return;
                _isMuted = value;
                ApplyVolumeLocked();
            }
        }
    }

    /// <summary>左右声道平衡 -1.0(全左) ~ 1.0(全右)。当前未实际改变声像 — 留接口对齐 WPF。</summary>
    public double Balance
    {
        get { lock (_lock) return _balance; }
        set
        {
            var clamped = Math.Clamp(value, -1.0, 1.0);
            lock (_lock) _balance = clamped;
        }
    }

    /// <summary>播放倍速 0.1 ~ 10.0。<see cref="WsolaSpeedProcessor"/> 做 time-stretch,音高保持不变。</summary>
    public double SpeedRatio
    {
        get { lock (_lock) return _speedRatio; }
        set
        {
            var clamped = Math.Clamp(value, 0.1, 10.0);
            lock (_lock)
            {
                if (Math.Abs(_speedRatio - clamped) < 1e-9) return;
                // 切速率前先用旧速率计算 Position,作为新 anchor —— 让 Position 在切换瞬间不跳。
                if (_device != null)
                {
                    _streamPosAtAnchor = ComputePositionLocked();
                    _devicePosAtAnchor = _device.Position;
                }
                _speedRatio = clamped;
                if (_speedProcessor != null) _speedProcessor.SpeedRatio = clamped;
            }
        }
    }

    /// <summary>媒体总时长。未打开 / 流式不可知时为 null。</summary>
    public TimeSpan? NaturalDuration
    {
        get
        {
            lock (_lock) return _naturalDuration > TimeSpan.Zero ? _naturalDuration : null;
        }
    }

    /// <summary>当前播放位置。设置等价 <see cref="Seek"/>。</summary>
    public TimeSpan Position
    {
        get { lock (_lock) return ComputePositionLocked(); }
        set => Seek(value);
    }

    /// <summary>当前底层播放状态。</summary>
    public NativePlaybackState State
    {
        get { lock (_lock) return _state; }
    }

    // ── 事件 ──

    /// <summary>媒体成功打开后触发。<see cref="Open"/> 上在调用方线程,<see cref="OpenAsync"/> 上在 ThreadPool。</summary>
    public event EventHandler? MediaOpened;

    /// <summary>
    /// 播放真正流到末尾(ring buffer 排空)时触发。回调由 native 音频线程触发后再
    /// hop 到 ThreadPool,因此事件订阅者已经不在音频回调线程上,但仍应避免长任务。
    /// </summary>
    public event EventHandler? MediaEnded;

    /// <summary>媒体打开 / 播放失败时触发。</summary>
    public event EventHandler<AudioPlayerErrorEventArgs>? MediaFailed;

    // ── 公共方法 ──

    /// <summary>同步打开指定 URI(目前只支持 <c>file://</c>)。</summary>
    public void Open(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var path = source.IsFile ? source.LocalPath : source.ToString();
        bool ok;
        Exception? failure = null;
        try { ok = OpenInternal(path, source); }
        catch (Exception ex) { ok = false; failure = ex; }

        if (ok)
        {
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(
                failure ?? new InvalidOperationException($"Failed to open audio source: {path}")));
        }
    }

    /// <summary>异步打开 — 把文件 IO + decoder 初始化放到 ThreadPool,UI 线程不阻塞。</summary>
    public Task<bool> OpenAsync(Uri source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var path = source.IsFile ? source.LocalPath : source.ToString();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ok = OpenInternal(path, source);
                if (ok) MediaOpened?.Invoke(this, EventArgs.Empty);
                else MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(
                    new InvalidOperationException($"Failed to open audio source: {path}")));
                return ok;
            }
            catch (Exception ex)
            {
                MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex));
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>开始或恢复播放。</summary>
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_device == null || _decoder == null) return;
            try
            {
                _device.Start();
                _state = NativePlaybackState.Playing;
                _resumeGate.Set();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Play failed: {ex.Message}");
                MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex));
            }
        }
    }

    /// <summary>暂停。再次 <see cref="Play"/> 从暂停位置继续。</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_device == null || _state != NativePlaybackState.Playing) return;
            try
            {
                _resumeGate.Reset();
                _device.Stop();
                _state = NativePlaybackState.Paused;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Pause failed: {ex.Message}");
            }
        }
    }

    /// <summary>停止播放并把位置重置到 0。</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_device == null) return;
            try
            {
                _resumeGate.Reset();
                _device.Stop();
                _device.Flush();
                _decoder?.Seek(TimeSpan.Zero);
                _speedProcessor?.Reset();
                _streamPosAtAnchor = TimeSpan.Zero;
                _devicePosAtAnchor = _device.Position;
                _state = NativePlaybackState.Stopped;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Stop failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 跳转到指定位置。decoder 内部 seek 不重建 stream;
    /// device ring 会被 Flush 掉,WSOLA 状态也会 Reset。
    /// </summary>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (_naturalDuration > TimeSpan.Zero && position > _naturalDuration)
                position = _naturalDuration;

            if (_decoder == null)
            {
                // 还没 Open: 把目标记下,Open 完成后立即 seek。
                _seekTarget = position;
                _hasSeekTarget = true;
                return;
            }

            try
            {
                bool wasPlaying = _state == NativePlaybackState.Playing;
                _resumeGate.Reset();
                _device?.Flush();
                _decoder.Seek(position);
                _speedProcessor?.Reset();
                _streamPosAtAnchor = position;
                _devicePosAtAnchor = _device?.Position ?? TimeSpan.Zero;
                if (wasPlaying) _resumeGate.Set();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Seek failed: {ex.Message}");
                MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex));
            }
        }
    }

    /// <summary>关闭当前媒体释放设备 / 解码器,可后续 <see cref="Open"/> 新源。</summary>
    public void Close()
    {
        lock (_lock)
        {
            CleanupLocked();
            Source = null;
            _currentFilePath = null;
            _hasMedia = false;
            _naturalDuration = TimeSpan.Zero;
            _hasSeekTarget = false;
            _state = NativePlaybackState.Stopped;
            _streamPosAtAnchor = TimeSpan.Zero;
            _devicePosAtAnchor = TimeSpan.Zero;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            CleanupLocked();
        }
        _resumeGate.Dispose();
    }

    // ── 内部实现 ──

    private bool OpenInternal(string path, Uri source)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (source.IsFile && !File.Exists(path)) return false;

        lock (_lock)
        {
            ThrowIfDisposed();
            CleanupLocked();

            INativeAudioDecoder? decoder = null;
            INativeAudioDevice?  device  = null;
            try
            {
                decoder = s_decoderFactory.Create();
                decoder.Open(path);

                // 按源音频的 sample rate / channels 建 device 和 WSOLA,避免
                // 强制重采样到固定 44.1k 导致音高 / 速度偏移。WASAPI 共享模式
                // 会硬件重采样到操作系统的设备速率,人耳感知是原速原音高。
                int rate = decoder.SampleRate > 0 ? decoder.SampleRate : FALLBACK_SAMPLE_RATE;
                int channels = decoder.Channels > 0 ? decoder.Channels : FALLBACK_CHANNELS;
                _outputSampleRate = rate;
                _outputChannels = channels;

                device = s_deviceFactory.Create(rate, channels);
                device.PlaybackEnded += OnDevicePlaybackEnded;

                _decoder = decoder;
                _device  = device;
                _speedProcessor = new WsolaSpeedProcessor(rate, channels)
                {
                    SpeedRatio = _speedRatio,
                };

                ApplyVolumeLocked();

                _naturalDuration = decoder.Duration;
                Source = source;
                _currentFilePath = path;
                _hasMedia = true;
                _state = NativePlaybackState.Stopped;
                _streamPosAtAnchor = TimeSpan.Zero;
                _devicePosAtAnchor = device.Position;

                if (_hasSeekTarget)
                {
                    try
                    {
                        decoder.Seek(_seekTarget);
                        _streamPosAtAnchor = _seekTarget;
                    }
                    catch { /* best-effort */ }
                    _hasSeekTarget = false;
                }

                // 启 pump 线程,等 Play() 通过 _resumeGate 放行。
                _pumpCts = new CancellationTokenSource();
                _pumpThread = new Thread(PumpLoop)
                {
                    IsBackground = true,
                    Name = "Jalium.AudioPump",
                };
                _pumpThread.Start(_pumpCts.Token);
            }
            catch
            {
                // 任何步骤出错都把已分配资源释放,并把状态恢复到"未打开"。
                if (device != null)
                {
                    try { device.PlaybackEnded -= OnDevicePlaybackEnded; } catch { }
                    try { device.Dispose(); } catch { }
                }
                try { decoder?.Dispose(); } catch { }
                _decoder = null;
                _device = null;
                _speedProcessor = null;
                _hasMedia = false;
                _naturalDuration = TimeSpan.Zero;
                throw;
            }
        }
        return true;
    }

    private void PumpLoop(object? boxedToken)
    {
        var ct = (CancellationToken)boxedToken!;
        int outChannels = _outputChannels > 0 ? _outputChannels : FALLBACK_CHANNELS;
        // WSOLA 在 0.1x 下输出可能膨胀 10x;给 dst 留充足空间。
        var src = ArrayPool<float>.Shared.Rent(PUMP_BLOCK_FRAMES * outChannels);
        var dst = ArrayPool<float>.Shared.Rent(PUMP_BLOCK_FRAMES * outChannels * 10);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { _resumeGate.Wait(ct); }
                catch (OperationCanceledException) { break; }

                INativeAudioDecoder? decoder;
                INativeAudioDevice?  device;
                WsolaSpeedProcessor? speed;
                lock (_lock)
                {
                    decoder = _decoder;
                    device  = _device;
                    speed   = _speedProcessor;
                }
                if (decoder == null || device == null) break;

                int frames = decoder.ReadFrames(src.AsSpan(0, PUMP_BLOCK_FRAMES * outChannels));
                if (frames == 0)
                {
                    // EOS — drain WSOLA tail, signal device, exit loop.
                    int drained = speed?.Drain(dst.AsSpan()) ?? 0;
                    if (drained > 0) SubmitAll(device, dst.AsSpan(0, drained), outChannels, ct);
                    try { device.SignalEndOfStream(); } catch { }
                    break;
                }

                var inputSpan = src.AsSpan(0, frames * outChannels);
                int produced;
                if (speed != null)
                {
                    produced = speed.Process(inputSpan, dst.AsSpan());
                }
                else
                {
                    int n = Math.Min(inputSpan.Length, dst.Length);
                    inputSpan[..n].CopyTo(dst);
                    produced = n;
                }

                if (produced > 0) SubmitAll(device, dst.AsSpan(0, produced), outChannels, ct);
            }
        }
        catch (Exception ex)
        {
            try { MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex)); }
            catch { /* event-subscriber-induced exception swallowed */ }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(src);
            ArrayPool<float>.Shared.Return(dst);
        }
    }

    private static void SubmitAll(INativeAudioDevice device, ReadOnlySpan<float> samples, int channels, CancellationToken ct)
    {
        int writtenSamples = 0;
        while (writtenSamples < samples.Length && !ct.IsCancellationRequested)
        {
            int wFrames = device.Submit(samples[writtenSamples..]);
            if (wFrames == 0)
            {
                // Ring 满,等 callback 消费几毫秒再重试。
                Thread.Sleep(2);
                continue;
            }
            writtenSamples += wFrames * channels;
        }
    }

    private void OnDevicePlaybackEnded(object? sender, EventArgs e)
    {
        // PlaybackEnded 已在 MiniAudioDevice 内部 hop 到 ThreadPool,这里直接 fire。
        lock (_lock) _state = NativePlaybackState.Ended;
        try { MediaEnded?.Invoke(this, EventArgs.Empty); }
        catch { /* subscriber exception isolated */ }
    }

    private TimeSpan ComputePositionLocked()
    {
        if (_device == null) return TimeSpan.Zero;
        var delta = _device.Position - _devicePosAtAnchor;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        // SpeedRatio 把"墙钟" * speed 映射回流位置。
        var streamDelta = TimeSpan.FromSeconds(delta.TotalSeconds * _speedRatio);
        var pos = _streamPosAtAnchor + streamDelta;
        if (_naturalDuration > TimeSpan.Zero && pos > _naturalDuration)
            pos = _naturalDuration;
        return pos;
    }

    private void ApplyVolumeLocked()
    {
        if (_device == null) return;
        try { _device.SetVolume((float)(_isMuted ? 0.0 : _volume)); }
        catch { /* ignore — Volume getter/setter must not throw */ }
    }

    private void CleanupLocked()
    {
        // 收割 pump:cancel + 释放 gate 让 Wait 跳出 + Join。
        var cts    = _pumpCts;
        var thread = _pumpThread;
        var decoder = _decoder;
        var device  = _device;

        _pumpCts = null;
        _pumpThread = null;
        _decoder = null;
        _device = null;
        _speedProcessor = null;

        try { cts?.Cancel(); } catch { }
        _resumeGate.Set();
        if (thread != null && thread.IsAlive)
        {
            try { thread.Join(TimeSpan.FromSeconds(2)); } catch { }
        }
        try { cts?.Dispose(); } catch { }

        if (device != null)
        {
            try { device.PlaybackEnded -= OnDevicePlaybackEnded; } catch { }
            try { device.Stop(); } catch { }
            try { device.Dispose(); } catch { }
        }
        try { decoder?.Dispose(); } catch { }

        _resumeGate.Reset();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioPlayer));
    }
}

/// <summary>音频播放器错误事件参数。</summary>
public sealed class AudioPlayerErrorEventArgs : EventArgs
{
    /// <summary>导致失败的异常。</summary>
    public Exception ErrorException { get; }

    /// <summary>异常对应的简短描述。</summary>
    public string ErrorMessage => ErrorException.Message;

    /// <summary>初始化错误事件参数。</summary>
    public AudioPlayerErrorEventArgs(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorException = exception;
    }
}
