using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Jalium.UI.Media;

/// <summary>
/// 独立的纯音频播放器,语义对齐 WPF <c>System.Windows.Media.MediaPlayer</c> 的音频面.
/// 基于 SoundFlow + MiniAudio + FFmpeg,跨平台支持 mp3 / wav / ogg / flac / aac / m4a.
/// </summary>
/// <remarks>
/// 与控件解耦:不需要挂在可视树中,适合从 ViewModel / Service / 后台线程直接驱动.
/// 内部使用进程内共享的 <see cref="MiniAudioEngine"/>(线程安全, 懒初始化),
/// 每个 <see cref="AudioPlayer"/> 实例独占一个 <see cref="AudioPlaybackDevice"/> + <see cref="SoundPlayer"/>.
/// </remarks>
public sealed class AudioPlayer : IDisposable
{
    // ── 进程内共享 SoundFlow engine ──────────────────────────────────────
    // SoundFlow 的 codec factory 注册在 engine 上, 不是在 SoundPlayer 上 ——
    // 多个 AudioPlayer 实例共用一个 engine 的开销几乎为 0(MiniAudio 内部用 ma_context).
    // 反向, 每个实例 new 一个 MiniAudioEngine 会让 codec factory 注册重复跑.
    private static readonly object s_engineLock = new();
    private static MiniAudioEngine? s_engine;
    private static bool s_codecsRegistered;

    private static MiniAudioEngine GetSharedEngine()
    {
        var engine = Volatile.Read(ref s_engine);
        if (engine != null) return engine;

        lock (s_engineLock)
        {
            if (s_engine != null) return s_engine;

            var newEngine = new MiniAudioEngine();
            try
            {
                if (!s_codecsRegistered)
                {
                    newEngine.RegisterCodecFactory(new FFmpegCodecFactory());
                    s_codecsRegistered = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer: FFmpeg codec registration failed — only formats supported by MiniAudio's built-in decoders will be available. {ex.Message}");
            }

            s_engine = newEngine;
            return newEngine;
        }
    }

    /// <summary>
    /// 释放进程内共享的 SoundFlow 引擎.通常只在测试或进程退出阶段调用.
    /// </summary>
    public static void ShutdownSharedEngine()
    {
        MiniAudioEngine? engine;
        lock (s_engineLock)
        {
            engine = s_engine;
            s_engine = null;
            s_codecsRegistered = false;
        }
        engine?.Dispose();
    }

    // ── 实例状态 ────────────────────────────────────────────────────────
    private readonly object _lock = new();
    private AudioPlaybackDevice? _playbackDevice;
    private SoundPlayer? _soundPlayer;
    private StreamDataProvider? _dataProvider;
    private FileStream? _fileStream;
    private string? _currentFilePath;
    private bool _disposed;

    private double _volume = 0.5;
    private bool _isMuted;
    private double _balance;
    private double _speedRatio = 1.0;
    private TimeSpan _naturalDuration;
    private TimeSpan _seekTarget;
    private bool _hasSeekTarget;
    private bool _hasMedia;

    private const int FORCED_SAMPLE_RATE = 44100;
    private const int FORCED_CHANNELS = 2;
    private const SampleFormat FORCED_FORMAT = SampleFormat.F32;

    /// <summary>当前媒体源(只读).设置请走 <see cref="Open"/>.</summary>
    public Uri? Source { get; private set; }

    /// <summary>是否打开了有效音频流.</summary>
    public bool HasAudio => _hasMedia;

    /// <summary>音量 0.0 ~ 1.0.</summary>
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

    /// <summary>静音开关.内部仍持有原 <see cref="Volume"/>,关闭静音后恢复.</summary>
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

    /// <summary>左右声道平衡 -1.0(全左) ~ 1.0(全右).当前未实际改变声像 — 留接口对齐 WPF.</summary>
    public double Balance
    {
        get { lock (_lock) return _balance; }
        set
        {
            var clamped = Math.Clamp(value, -1.0, 1.0);
            lock (_lock) _balance = clamped;
        }
    }

    /// <summary>播放倍速 0.1 ~ 10.0.SoundFlow 内置 WSOLA time-stretch,音高保持不变.</summary>
    public double SpeedRatio
    {
        get { lock (_lock) return _speedRatio; }
        set
        {
            var clamped = Math.Clamp(value, 0.1, 10.0);
            lock (_lock)
            {
                _speedRatio = clamped;
                if (_soundPlayer != null)
                {
                    _soundPlayer.PlaybackSpeed = (float)clamped;
                }
            }
        }
    }

    /// <summary>媒体总时长.未打开 / 流式不可知时为 null.</summary>
    public TimeSpan? NaturalDuration
    {
        get
        {
            lock (_lock)
            {
                return _naturalDuration > TimeSpan.Zero ? _naturalDuration : null;
            }
        }
    }

    /// <summary>当前播放位置.设置等价 <see cref="Seek"/>.</summary>
    public TimeSpan Position
    {
        get
        {
            lock (_lock)
            {
                return _soundPlayer?.Time != null
                    ? TimeSpan.FromSeconds(_soundPlayer.Time)
                    : TimeSpan.Zero;
            }
        }
        set => Seek(value);
    }

    /// <summary>当前底层播放状态.</summary>
    public PlaybackState State
    {
        get { lock (_lock) return _soundPlayer?.State ?? PlaybackState.Stopped; }
    }

    // ── 事件 ──────────────────────────────────────────────────────────
    /// <summary>媒体成功打开后触发(在 ThreadPool 上).</summary>
    public event EventHandler? MediaOpened;

    /// <summary>媒体播放到末尾时触发(在 SoundFlow 回调线程上).</summary>
    public event EventHandler? MediaEnded;

    /// <summary>媒体打开 / 播放失败时触发.</summary>
    public event EventHandler<AudioPlayerErrorEventArgs>? MediaFailed;

    // ── 公共方法 ──────────────────────────────────────────────────────

    /// <summary>同步打开指定 URI(目前只支持 <c>file://</c>).</summary>
    public void Open(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var path = source.IsFile ? source.LocalPath : source.ToString();
        bool ok;
        Exception? failure = null;

        try
        {
            ok = OpenInternal(path, source);
        }
        catch (Exception ex)
        {
            ok = false;
            failure = ex;
        }

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

    /// <summary>异步打开 — 把文件 IO + decoder 初始化放到 ThreadPool,UI 线程不阻塞.</summary>
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
                if (ok)
                {
                    MediaOpened?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(
                        new InvalidOperationException($"Failed to open audio source: {path}")));
                }
                return ok;
            }
            catch (Exception ex)
            {
                MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex));
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>开始或恢复播放.</summary>
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_soundPlayer == null || _playbackDevice == null) return;

            try
            {
                if (_soundPlayer.State == PlaybackState.Playing) return;

                if (_soundPlayer.State != PlaybackState.Paused)
                {
                    _playbackDevice.Start();
                }
                _soundPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Play failed: {ex.Message}");
                MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex));
            }
        }
    }

    /// <summary>暂停.再次 <see cref="Play"/> 从暂停位置继续.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_soundPlayer?.State == PlaybackState.Playing)
            {
                try { _soundPlayer.Pause(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AudioPlayer.Pause failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>停止播放并把位置重置到 0.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_soundPlayer != null)
            {
                try { _soundPlayer.Stop(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AudioPlayer.Stop failed: {ex.Message}"); }
            }
        }
    }

    /// <summary>跳转到指定位置.SoundFlow 的 <see cref="SoundPlayerBase.Seek(TimeSpan, SeekOrigin)"/> 不需要重建 stream.</summary>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (_naturalDuration > TimeSpan.Zero && position > _naturalDuration)
                position = _naturalDuration;

            if (_soundPlayer == null)
            {
                // 还没 Open: 把目标记下,Open 完成后立即 seek.
                _seekTarget = position;
                _hasSeekTarget = true;
                return;
            }

            try
            {
                _soundPlayer.Seek(position, SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioPlayer.Seek failed: {ex.Message}");
                MediaFailed?.Invoke(this, new AudioPlayerErrorEventArgs(ex));
            }
        }
    }

    /// <summary>关闭当前媒体释放设备 / 解码器,可后续 <see cref="Open"/> 新源.</summary>
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
    }

    // ── 内部实现 ──────────────────────────────────────────────────────

    private bool OpenInternal(string path, Uri source)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (source.IsFile && !File.Exists(path))
            return false;

        lock (_lock)
        {
            ThrowIfDisposed();
            CleanupLocked();

            var engine = GetSharedEngine();

            DeviceInfo? defaultDevice = engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
            if (defaultDevice?.Name == null)
            {
                defaultDevice = engine.PlaybackDevices.FirstOrDefault();
            }
            if (defaultDevice?.Name == null)
            {
                System.Diagnostics.Debug.WriteLine("AudioPlayer: no playback device available.");
                return false;
            }

            var format = new AudioFormat
            {
                SampleRate = FORCED_SAMPLE_RATE,
                Channels = FORCED_CHANNELS,
                Format = FORCED_FORMAT,
            };

            _playbackDevice = engine.InitializePlaybackDevice(defaultDevice, format);
            _fileStream = new FileStream(
                path,
                FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 262144, useAsync: true);
            _dataProvider = new StreamDataProvider(engine, format, _fileStream);
            _soundPlayer = new SoundPlayer(engine, format, _dataProvider);
            _soundPlayer.PlaybackSpeed = (float)_speedRatio;
            _soundPlayer.PlaybackEnded += OnSoundFlowPlaybackEnded;

            ApplyVolumeLocked();
            _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

            try
            {
                var dur = _soundPlayer.Duration;
                _naturalDuration = dur > 0 && !double.IsInfinity(dur)
                    ? TimeSpan.FromSeconds(dur)
                    : TimeSpan.Zero;
            }
            catch
            {
                _naturalDuration = TimeSpan.Zero;
            }

            Source = source;
            _currentFilePath = path;
            _hasMedia = true;

            if (_hasSeekTarget)
            {
                try { _soundPlayer.Seek(_seekTarget, SeekOrigin.Begin); }
                catch { /* ignore — best-effort */ }
                _hasSeekTarget = false;
            }
        }

        return true;
    }

    private void OnSoundFlowPlaybackEnded(object? sender, EventArgs e)
    {
        MediaEnded?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyVolumeLocked()
    {
        if (_soundPlayer == null) return;
        _soundPlayer.Volume = _isMuted ? 0f : (float)_volume;
    }

    private void CleanupLocked()
    {
        var sp = _soundPlayer;
        var dp = _dataProvider;
        var fs = _fileStream;
        var dev = _playbackDevice;

        _soundPlayer = null;
        _dataProvider = null;
        _fileStream = null;
        _playbackDevice = null;

        if (sp != null)
        {
            try { sp.PlaybackEnded -= OnSoundFlowPlaybackEnded; } catch { }
            try { dev?.MasterMixer.RemoveComponent(sp); } catch { }
            try { sp.Stop(); } catch { }
            try { sp.Dispose(); } catch { }
        }
        try { dp?.Dispose(); } catch { }
        try { fs?.Dispose(); } catch { }
        if (dev != null)
        {
            try { dev.Stop(); } catch { }
            try { dev.Dispose(); } catch { }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioPlayer));
    }
}

/// <summary>音频播放器错误事件参数.</summary>
public sealed class AudioPlayerErrorEventArgs : EventArgs
{
    /// <summary>导致失败的异常.</summary>
    public Exception ErrorException { get; }

    /// <summary>异常对应的简短描述.</summary>
    public string ErrorMessage => ErrorException.Message;

    /// <summary>初始化错误事件参数.</summary>
    public AudioPlayerErrorEventArgs(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorException = exception;
    }
}
