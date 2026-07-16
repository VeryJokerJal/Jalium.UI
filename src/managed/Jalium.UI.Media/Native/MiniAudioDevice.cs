using System.Runtime.InteropServices;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>
/// 由 miniaudio 后端支撑的 <see cref="INativeAudioDevice"/> 实现。所有控制
/// 函数对 pump 线程线程安全;<see cref="PlaybackEnded"/> 在 native 音频回调
/// 线程触发,实现端立即 marshal 到专用 dispatcher,但订阅者仍应避免长任务。
/// </summary>
public sealed class MiniAudioDevice : INativeAudioDevice
{
    /// <summary>
    /// <para>
    /// 静态字段持有 delegate,保证它不被 GC、函数指针长期有效——这是
    /// <see cref="Marshal.GetFunctionPointerForDelegate{TDelegate}(TDelegate)"/>
    /// 的硬性要求,本质等价于把 callback 钉成进程级单例。
    /// </para>
    /// <para>
    /// 真正的"这次回调指向哪个 device"由 native 端透传回来的
    /// <c>user_data</c>(<see cref="GCHandle.ToIntPtr"/>)还原,见
    /// <see cref="OnEndedFromNative"/>。
    /// </para>
    /// </summary>
    private static readonly NativeAudioInterop.PlaybackEndedNative s_endedCallback = OnEndedFromNative;
    private static readonly nint s_endedCallbackPtr = Marshal.GetFunctionPointerForDelegate(s_endedCallback);
    private static readonly System.Collections.Concurrent.ConcurrentQueue<MiniAudioDevice> s_endedQueue = new();
    private static readonly System.Threading.AutoResetEvent s_endedSignal = new(false);
    private static readonly System.Threading.Thread s_endedDispatcher = StartEndedDispatcher();

    private nint _handle;
    private int _sampleRate;
    private int _channels;
    private GCHandle _selfHandle;
    private int _disposed;  // 0 / 1, Interlocked

    /// <inheritdoc />
    public event System.EventHandler? PlaybackEnded;

    /// <summary>
    /// 以指定采样率 / 声道数打开默认输出设备(采样格式恒 F32 interleaved)。
    /// </summary>
    public MiniAudioDevice(int sampleRate, int channels)
    {
        if (sampleRate <= 0) throw new System.ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)   throw new System.ArgumentOutOfRangeException(nameof(channels));

        NativeMediaInitializer.EnsureInitialized();
        NativeAudioDecoder.EnsureAudioInitialized();

        _sampleRate = sampleRate;
        _channels = channels;

        // Weak 引用避免 native 回调长期持有 device 强引用导致析构受阻;
        // callback 仅排队,专用 dispatcher 跳出音频线程后才 raise 用户事件。
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);

        var config = new NativeAudioInterop.NativeAudioDeviceConfig
        {
            SampleRate = (uint)sampleRate,
            Channels = (uint)channels,
            SampleFormat = 0,                   // JALIUM_ASF_F32
            RingCapacityFrames = 0,             // 用 native 默认
            OnPlaybackEnded = s_endedCallbackPtr,
            EndedUserData = GCHandle.ToIntPtr(_selfHandle),
        };

        try
        {
            var st = NativeAudioInterop.jalium_audio_device_open(in config, out _handle);
            NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_open");
        }
        catch
        {
            if (_selfHandle.IsAllocated) _selfHandle.Free();
            throw;
        }
    }

    /// <inheritdoc />
    public System.TimeSpan Position
    {
        get
        {
            if (_handle == nint.Zero || _sampleRate <= 0) return System.TimeSpan.Zero;
            ulong frames = NativeAudioInterop.jalium_audio_device_played_frames(_handle);
            return System.TimeSpan.FromSeconds((double)frames / _sampleRate);
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        ThrowIfDisposed();
        var st = NativeAudioInterop.jalium_audio_device_start(_handle);
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_start");
    }

    /// <inheritdoc />
    public void Stop()
    {
        ThrowIfDisposed();
        var st = NativeAudioInterop.jalium_audio_device_stop(_handle);
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_stop");
    }

    /// <inheritdoc />
    public int Submit(System.ReadOnlySpan<float> pcm)
    {
        ThrowIfDisposed();
        if (pcm.IsEmpty || _channels <= 0) return 0;

        int frames = pcm.Length / _channels;
        if (frames <= 0) return 0;

        uint framesWritten;
        NativeMediaStatus st;
        unsafe
        {
            fixed (float* p = pcm)
            {
                st = NativeAudioInterop.jalium_audio_device_submit(
                    _handle, p, (uint)frames, out framesWritten);
            }
        }
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_submit");
        return (int)framesWritten;
    }

    /// <inheritdoc />
    public void SignalEndOfStream()
    {
        ThrowIfDisposed();
        var st = NativeAudioInterop.jalium_audio_device_signal_end_of_stream(_handle);
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_signal_end_of_stream");
    }

    /// <inheritdoc />
    public void Flush()
    {
        ThrowIfDisposed();
        var st = NativeAudioInterop.jalium_audio_device_flush(_handle);
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_flush");
    }

    /// <inheritdoc />
    public void SetVolume(float linear)
    {
        ThrowIfDisposed();
        var st = NativeAudioInterop.jalium_audio_device_set_volume(_handle, linear);
        NativeMediaException.ThrowIfFailed(st, "jalium_audio_device_set_volume");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if (_handle != nint.Zero)
        {
            var h = _handle;
            _handle = nint.Zero;
            NativeAudioInterop.jalium_audio_device_close(h);
        }

        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    private void ThrowIfDisposed()
    {
        if (System.Threading.Volatile.Read(ref _disposed) != 0)
            throw new System.ObjectDisposedException(nameof(MiniAudioDevice));
        if (_handle == nint.Zero)
            throw new System.InvalidOperationException("Audio device is not open.");
    }

    private static void OnEndedFromNative(nint userData)
    {
        // 永远第一时间跳离音频回调线程;之后才允许触发用户事件。
        if (userData == nint.Zero) return;
        GCHandle gch;
        try { gch = GCHandle.FromIntPtr(userData); }
        catch { return; }

        if (!gch.IsAllocated) return;
        if (gch.Target is not MiniAudioDevice self) return;

        self.QueuePlaybackEnded();
    }

    internal void QueuePlaybackEnded()
    {
        s_endedQueue.Enqueue(this);
        s_endedSignal.Set();
    }

    private static System.Threading.Thread StartEndedDispatcher()
    {
        var thread = new System.Threading.Thread(static () =>
        {
            while (true)
            {
                s_endedSignal.WaitOne();
                while (s_endedQueue.TryDequeue(out MiniAudioDevice? device))
                {
                    if (System.Threading.Volatile.Read(ref device._disposed) != 0) continue;
                    try { device.PlaybackEnded?.Invoke(device, System.EventArgs.Empty); } catch { /* swallow */ }
                }
            }
        })
        {
            IsBackground = true,
            Name = "Jalium audio event dispatcher",
        };
        thread.Start();
        return thread;
    }
}

/// <summary>
/// <see cref="MiniAudioDevice"/> 工厂。
/// </summary>
public sealed class MiniAudioDeviceFactory : INativeAudioDeviceFactory
{
    /// <inheritdoc />
    public INativeAudioDevice Create(int sampleRate, int channels)
        => new MiniAudioDevice(sampleRate, channels);
}
