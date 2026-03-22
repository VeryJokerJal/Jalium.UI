using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;
using OpenCvSharp;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Jalium.UI.Controls;

#region 媒体状态与事件参数

public enum MediaState
{
    Manual,
    Play,
    Pause,
    Stop,
    Close
}

public sealed class MediaFailedEventArgs : RoutedEventArgs
{
    public Exception Exception { get; }
    public string ErrorMessage => Exception.Message;

    public MediaFailedEventArgs(RoutedEvent routedEvent, object source, Exception exception)
        : base(routedEvent, source)
    {
        Exception = exception;
    }
}

#endregion

#region 媒体信息结构

public sealed class MediaInfo
{
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public double VideoFps { get; set; }
    public double Duration { get; set; }
    public int AudioSampleRate { get; set; } = 44100;
    public int AudioChannels { get; set; } = 2;
}

#endregion

#region 文件日志记录器

public static class FileLogger
{
    private static readonly object _lock = new();
    private static string _logPath = Path.Combine(AppContext.BaseDirectory, "mediaplayer.log");

    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, logMessage + Environment.NewLine);
            }
            catch { }
        }

        Debug.WriteLine(logMessage);
    }

    public static void LogError(string message, Exception? ex = null)
    {
        var logMessage = $"[ERROR] {message}";
        if (ex != null)
        {
            logMessage += $" - Exception: {ex.GetType().Name}: {ex.Message}";
        }
        Log(logMessage);
    }
}

#endregion

#region 视频帧缓冲队列

public sealed class VideoFrame : IDisposable
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public long TimestampMs { get; }
    public int FrameIndex { get; }

    public VideoFrame(byte[] data, int width, int height, long timestampMs, int frameIndex)
    {
        Data = data;
        Width = width;
        Height = height;
        TimestampMs = timestampMs;
        FrameIndex = frameIndex;
    }

    public void Dispose() { }
}

public sealed class VideoFrameBuffer : IDisposable
{
    private readonly BlockingCollection<VideoFrame> _frameQueue;
    private readonly int _maxSize;
    private int _frameIndex;

    public VideoFrameBuffer(int maxSize = 3)
    {
        _maxSize = maxSize;
        _frameQueue = new BlockingCollection<VideoFrame>(maxSize);
        _frameIndex = 0;
    }

    public bool TryAdd(VideoFrame frame, int timeoutMs = 0)
    {
        if (timeoutMs > 0)
        {
            return _frameQueue.TryAdd(frame, timeoutMs);
        }
        try
        {
            _frameQueue.Add(frame);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public VideoFrame? Take(CancellationToken token)
    {
        try
        {
            return _frameQueue.Take(token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool TryTake(out VideoFrame? frame)
    {
        return _frameQueue.TryTake(out frame);
    }

    public void Clear()
    {
        while (_frameQueue.TryTake(out var frame))
        {
            frame?.Dispose();
        }
    }

    public int Count => _frameQueue.Count;

    public int GetNextFrameIndex() => Interlocked.Increment(ref _frameIndex) - 1;

    public void Dispose()
    {
        _frameQueue.CompleteAdding();
        Clear();
        _frameQueue.Dispose();
    }
}

#endregion

#region 音频管理器 (SoundFlow + FFmpeg)

/// <summary>
/// 音频流信息结构 - 用于存储探测到的真实格式
/// </summary>
public sealed class AudioStreamInfo
{
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
}

/// <summary>
/// 使用 SoundFlow + FFmpeg 编解码器管理音频播放
/// </summary>
public sealed class AudioPlaybackManager : IDisposable
{
    private MiniAudioEngine? _audioEngine;
    private AudioPlaybackDevice? _playbackDevice;
    private SoundPlayer? _soundPlayer;
    private StreamDataProvider? _dataProvider;
    private FileStream? _fileStream;
    private readonly object _lock = new();
    private bool _disposed;
    private double _currentVolume = 1.0;
    private bool _isMuted;
    private bool _isInitialized;
    private string? _currentFilePath;

    private DateTime _playbackStartTime;
    private TimeSpan _pausePosition;
    private bool _isPlaying;

    // 存储探测到的真实音频格式
    private AudioStreamInfo _detectedFormat = new() { SampleRate = 48000, Channels = 2, BitsPerSample = 16 };

    public TimeSpan CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                if (_soundPlayer == null) return TimeSpan.Zero;
                if (_isPlaying)
                    return _pausePosition + (DateTime.Now - _playbackStartTime);
                return _pausePosition;
            }
        }
    }

    public TimeSpan Duration { get; private set; }

    public AudioPlaybackManager()
    {
        InitializeEngineAndCodecs();
    }

    private void InitializeEngineAndCodecs()
    {
        try
        {
            _audioEngine = new MiniAudioEngine();
            var factory = new FFmpegCodecFactory();
            TryRegisterFactory(factory);
            FileLogger.Log("AudioPlaybackManager: Engine initialized and FFmpeg codecs registered");
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Failed to initialize engine and codecs in constructor", ex);
        }
    }

    private void TryRegisterFactory(FFmpegCodecFactory factory)
    {
        try
        {
            var engineType = typeof(AudioEngine);
            var registerMethod = engineType.GetMethod("RegisterCodecFactory",
                BindingFlags.Public | BindingFlags.Instance);

            if (registerMethod != null && _audioEngine != null)
            {
                registerMethod.Invoke(_audioEngine, new object[] { factory });
                FileLogger.Log("FFmpeg codec factory registered via RegisterCodecFactory");
                return;
            }

            var codecManagerProp = engineType.GetProperty("CodecManager",
                BindingFlags.Public | BindingFlags.Instance);

            if (codecManagerProp != null && _audioEngine != null)
            {
                var codecManager = codecManagerProp.GetValue(_audioEngine);
                if (codecManager != null)
                {
                    var addMethod = codecManager.GetType().GetMethod("AddFactory",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (addMethod != null)
                    {
                        addMethod.Invoke(codecManager, new object[] { factory });
                        FileLogger.Log("FFmpeg codec factory registered via CodecManager.AddFactory");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Error in TryRegisterFactory", ex);
        }
    }

    private bool EnsureEngineInitialized()
    {
        try
        {
            if (_audioEngine == null)
            {
                InitializeEngineAndCodecs();
            }
            return _audioEngine != null;
        }
        catch (Exception ex)
        {
            FileLogger.LogError("Failed to ensure engine initialization", ex);
            return false;
        }
    }

    #region 音频格式探测

    /// <summary>
    /// 使用 AudioFormat.GetFormatFromStream 或手动解析文件头
    /// </summary>
    private AudioStreamInfo DetectAudioFormat(string filePath)
    {
        try
        {
            //尝试使用 SoundFlow 的 AudioFormat.GetFormatFromStream（如果可用）
            var formatMethod = typeof(AudioFormat).GetMethod("GetFormatFromStream",
                BindingFlags.Public | BindingFlags.Static);

            if (formatMethod != null && _audioEngine != null)
            {
                try
                {
                    using var testStream = File.OpenRead(filePath);
                    var detectedFormat = formatMethod.Invoke(null, new object[] { testStream });
                    if (detectedFormat is AudioFormat af)
                    {
                        FileLogger.Log($"[AutoDetect] AudioFormat.GetFormatFromStream: {af.SampleRate}Hz, {af.Channels}ch");
                        return new AudioStreamInfo
                        {
                            SampleRate = af.SampleRate,
                            Channels = af.Channels,
                            BitsPerSample = 16
                        };
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.LogError("GetFormatFromStream failed, fallback to manual detection", ex);
                }
            }

            // 手动解析文件头
            using var fs = File.OpenRead(filePath);
            var header = new byte[16384];
            var read = fs.Read(header, 0, header.Length);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (extension == ".wav" || extension == ".wave")
                return ParseWavHeader(header);
            if (extension == ".mp3")
                return ParseMp3Header(header, read);
            if (extension == ".mp4" || extension == ".m4a" || extension == ".mov")
                return ParseMp4Header(header);
            if (extension == ".flac")
                return ParseFlacHeader(header);
            if (extension == ".ogg")
                return ParseOggHeader(header);
        }
        catch (Exception ex)
        {
            FileLogger.LogError($"Format detection failed for {filePath}", ex);
        }

        return GuessFormatFromExtension(filePath);
    }

    private AudioStreamInfo ParseWavHeader(byte[] header)
    {
        if (header.Length < 44) return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        string riff = System.Text.Encoding.ASCII.GetString(header, 0, 4);
        string wave = System.Text.Encoding.ASCII.GetString(header, 8, 4);

        if (riff != "RIFF" || wave != "WAVE")
            return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        int channels = header[22] | (header[23] << 8);
        int sampleRate = header[24] | (header[25] << 8) | (header[26] << 16) | (header[27] << 24);
        int bitsPerSample = header[34] | (header[35] << 8);

        FileLogger.Log($"[WAV Detected] {sampleRate}Hz, {channels}ch, {bitsPerSample}bit");

        return new AudioStreamInfo
        {
            SampleRate = sampleRate > 0 ? sampleRate : 44100,
            Channels = channels > 0 ? channels : 2,
            BitsPerSample = bitsPerSample > 0 ? bitsPerSample : 16
        };
    }

    private AudioStreamInfo ParseMp3Header(byte[] header, int length)
    {
        for (int i = 0; i < length - 4; i++)
        {
            if (header[i] == 0xFF && (header[i + 1] & 0xE0) == 0xE0)
            {
                int byte1 = header[i + 1];
                int byte2 = header[i + 2];

                int mpegVersion = (byte1 >> 3) & 0x3;
                int sampleRateIndex = (byte2 >> 2) & 0x3;
                int channelMode = (byte2 >> 6) & 0x3;

                int[,] sampleRates = {
                    { 44100, 48000, 32000, 0 },
                    { 22050, 24000, 16000, 0 },
                    { 11025, 12000, 8000, 0 }
                };

                int versionIndex = mpegVersion == 3 ? 0 : (mpegVersion == 2 ? 1 : 2);
                int sampleRate = sampleRates[versionIndex, sampleRateIndex];
                int channels = channelMode == 3 ? 1 : 2;

                if (sampleRate > 0)
                {
                    FileLogger.Log($"[MP3 Detected] {sampleRate}Hz, {channels}ch");
                    return new AudioStreamInfo
                    {
                        SampleRate = sampleRate,
                        Channels = channels,
                        BitsPerSample = 16
                    };
                }
            }
        }
        return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };
    }

    private AudioStreamInfo ParseMp4Header(byte[] header)
    {
        if (header.Length > 8)
        {
            string sig = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            if (sig == "ftyp" || sig == "moov")
            {
                for (int i = 0; i < header.Length - 20; i++)
                {
                    if (header[i] == 'm' && header[i + 1] == 'd' && header[i + 2] == 'h' && header[i + 3] == 'd')
                    {
                        int version = header[i + 4];
                        int offset = version == 1 ? 28 : 16;

                        if (i + offset + 8 < header.Length)
                        {
                            uint timescale = (uint)((header[i + offset] << 24) |
                                                   (header[i + offset + 1] << 16) |
                                                   (header[i + offset + 2] << 8) |
                                                    header[i + offset + 3]);

                            if (timescale >= 8000 && timescale <= 192000)
                            {
                                FileLogger.Log($"[MP4 Detected] {timescale}Hz");
                                return new AudioStreamInfo
                                {
                                    SampleRate = (int)timescale,
                                    Channels = 2,
                                    BitsPerSample = 16
                                };
                            }
                        }
                    }
                }
            }
        }
        return new AudioStreamInfo { SampleRate = 48000, Channels = 2, BitsPerSample = 16 };
    }

    private AudioStreamInfo ParseFlacHeader(byte[] header)
    {
        if (header.Length < 38) return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        string marker = System.Text.Encoding.ASCII.GetString(header, 0, 4);
        if (marker != "fLaC") return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        int pos = 4;
        while (pos < header.Length - 34)
        {
            int blockHeader = (header[pos] << 24) | (header[pos + 1] << 16) |
                             (header[pos + 2] << 8) | header[pos + 3];
            int blockType = (blockHeader >> 31) & 0x1;
            int actualType = (blockHeader >> 24) & 0x7F;
            int blockSize = blockHeader & 0xFFFFFF;

            if (actualType == 0)
            {
                int sampleRate = ((header[pos + 10] << 16) | (header[pos + 11] << 8) |
                                  (header[pos + 12] & 0xF0)) >> 4;
                int channels = ((header[pos + 12] & 0x0F) >> 1) + 1;

                if (sampleRate > 0)
                {
                    FileLogger.Log($"[FLAC Detected] {sampleRate}Hz, {channels}ch");
                    return new AudioStreamInfo
                    {
                        SampleRate = sampleRate,
                        Channels = channels,
                        BitsPerSample = 16
                    };
                }
            }

            if (blockType == 1) break;
            pos += 4 + blockSize;
        }

        return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };
    }

    private AudioStreamInfo ParseOggHeader(byte[] header)
    {
        if (header.Length < 30) return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        string capture = System.Text.Encoding.ASCII.GetString(header, 0, 4);
        if (capture != "OggS") return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };

        for (int i = 0; i < header.Length - 30; i++)
        {
            if (header[i] == 1 && System.Text.Encoding.ASCII.GetString(header, i + 1, 6) == "vorbis")
            {
                int sampleRate = header[i + 15] | (header[i + 16] << 8) |
                                (header[i + 17] << 16) | (header[i + 18] << 24);
                int channels = header[i + 19];

                if (sampleRate > 0)
                {
                    FileLogger.Log($"[OGG Vorbis Detected] {sampleRate}Hz, {channels}ch");
                    return new AudioStreamInfo
                    {
                        SampleRate = sampleRate,
                        Channels = channels > 0 ? channels : 2,
                        BitsPerSample = 16
                    };
                }
            }

            if (System.Text.Encoding.ASCII.GetString(header, i, 8) == "OpusHead")
            {
                int channels = header[i + 9];
                FileLogger.Log($"[OGG Opus Detected] 48000Hz, {channels}ch");
                return new AudioStreamInfo
                {
                    SampleRate = 48000,
                    Channels = channels > 0 ? channels : 2,
                    BitsPerSample = 16
                };
            }
        }

        return new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 };
    }

    private AudioStreamInfo GuessFormatFromExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var info = ext switch
        {
            ".mp3" => new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 },
            ".wav" => new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 },
            ".flac" => new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 },
            ".ogg" => new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 },
            ".m4a" or ".mp4" or ".mov" => new AudioStreamInfo { SampleRate = 48000, Channels = 2, BitsPerSample = 16 },
            ".aac" => new AudioStreamInfo { SampleRate = 48000, Channels = 2, BitsPerSample = 16 },
            ".wma" => new AudioStreamInfo { SampleRate = 44100, Channels = 2, BitsPerSample = 16 },
            _ => new AudioStreamInfo { SampleRate = 48000, Channels = 2, BitsPerSample = 16 }
        };

        FileLogger.Log($"[GuessFormat] {ext} -> {info.SampleRate}Hz");
        return info;
    }

    /// <summary>
    /// 创建与探测到的格式匹配的 AudioFormat
    /// </summary>
    private AudioFormat CreateMatchingFormat(AudioStreamInfo info)
    {
        try
        {
            // 将 bitsPerSample 转换为 SampleFormat 枚举
            var sampleFormat = info.BitsPerSample switch
            {
                8 => SampleFormat.U8,
                16 => SampleFormat.S16,
                24 => SampleFormat.S24,
                32 => SampleFormat.S32,
                _ => SampleFormat.S16
            };

            // 使用对象初始化器创建 AudioFormat
            var format = new AudioFormat
            {
                SampleRate = info.SampleRate,
                Channels = info.Channels,
                Format = sampleFormat
            };

            FileLogger.Log($"[CreateFormat] Created format: {info.SampleRate}Hz, {info.BitsPerSample}bit ({sampleFormat}), {info.Channels}ch");
            return format;
        }
        catch (Exception ex)
        {
            FileLogger.LogError($"Failed to create format {info.SampleRate}Hz, fallback to preset", ex);

            return info.SampleRate switch
            {
                44100 => AudioFormat.Cd,
                48000 => AudioFormat.Dvd,
                96000 => AudioFormat.StudioHq,
                192000 => new AudioFormat { SampleRate = 192000, Channels = 2, Format = SampleFormat.S24 },
                _ => AudioFormat.Dvd
            };
        }
    }

    #endregion

    #region 核心播放控制

    public bool OpenAndPlay(string filePath)
    {
        lock (_lock)
        {
            try
            {
                Close();

                if (!EnsureEngineInitialized())
                    return false;

                _currentFilePath = filePath;
                Duration = GetAudioDuration(filePath);

                // 探测文件真实格式
                _detectedFormat = DetectAudioFormat(filePath);
                FileLogger.Log($"[OpenAndPlay] Detected: {_detectedFormat.SampleRate}Hz, {_detectedFormat.Channels}ch, {_detectedFormat.BitsPerSample}bit");

                // 创建匹配的 AudioFormat
                var targetFormat = CreateMatchingFormat(_detectedFormat);

                // 使用文件格式初始化 PlaybackDevice
                var defaultDevice = _audioEngine!.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
                _playbackDevice = _audioEngine.InitializePlaybackDevice(defaultDevice, targetFormat);

                FileLogger.Log($"[Device] Initialized with format: {targetFormat.SampleRate}Hz");

                // 使用标准 FileStream 构造函数（修复 bufferSize 参数错误）
                _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 262144, useAsync: true);

                // StreamDataProvider 使用相同的 targetFormat
                _dataProvider = new StreamDataProvider(_audioEngine, targetFormat, _fileStream);

                // SoundPlayer 使用相同的 targetFormat
                _soundPlayer = new SoundPlayer(_audioEngine, targetFormat, _dataProvider);

                UpdateVolume();
                _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

                _isInitialized = true;
                _pausePosition = TimeSpan.Zero;

                FileLogger.Log($"[Success] Audio opened: {filePath} @ {targetFormat.SampleRate}Hz (all components matched)");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError($"Failed to open audio: {filePath}", ex);
                Cleanup();
                return false;
            }
        }
    }

    private TimeSpan GetAudioDuration(string filePath)
    {
        return TimeSpan.Zero;
    }

    public bool Play()
    {
        lock (_lock)
        {
            if (_soundPlayer == null || _playbackDevice == null) return false;

            try
            {
                if (_soundPlayer.State != PlaybackState.Playing)
                {
                    Thread.Sleep(300);

                    _playbackDevice.Start();
                    _soundPlayer.Play();
                    _playbackStartTime = DateTime.Now;
                    _isPlaying = true;

                    FileLogger.Log($"[Play] Started at {_detectedFormat.SampleRate}Hz (1x speed, no pitch shift)");
                }
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("SoundFlow audio play failed", ex);
                return false;
            }
        }
    }

    public bool Pause()
    {
        lock (_lock)
        {
            if (_soundPlayer == null) return false;

            try
            {
                _soundPlayer.Pause();
                if (_isPlaying)
                {
                    _pausePosition += DateTime.Now - _playbackStartTime;
                    _isPlaying = false;
                }
                FileLogger.Log("[Pause] Audio playback paused");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("SoundFlow audio pause failed", ex);
                return false;
            }
        }
    }

    public bool Stop()
    {
        lock (_lock)
        {
            if (_soundPlayer == null) return false;

            try
            {
                _soundPlayer.Stop();
                _pausePosition = TimeSpan.Zero;
                _isPlaying = false;
                _playbackDevice?.Stop();
                FileLogger.Log("[Stop] Audio playback stopped");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("SoundFlow audio stop failed", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Seek 操作需要重新初始化流，确保格式一致
    /// </summary>
    public bool Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_audioEngine == null || _playbackDevice == null || string.IsNullOrEmpty(_currentFilePath))
                return false;

            try
            {
                FileLogger.Log($"[Seek] Seeking to {position.TotalSeconds:F2}s");

                _soundPlayer?.Stop();
                _playbackDevice.MasterMixer.RemoveComponent(_soundPlayer!);
                _dataProvider?.Dispose();
                _fileStream?.Dispose();

                //重新打开文件流时使用相同的格式
                _fileStream = new FileStream(_currentFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 262144, useAsync: true);

                if (_fileStream.CanSeek)
                {
                    // 估算字节位置（基于探测到的格式）
                    var bytesPerSecond = _detectedFormat.SampleRate * _detectedFormat.Channels * (_detectedFormat.BitsPerSample / 8);
                    var bytePosition = (long)(position.TotalSeconds * bytesPerSecond);
                    _fileStream.Position = Math.Min(bytePosition, _fileStream.Length);
                    FileLogger.Log($"[Seek] File position set to {_fileStream.Position} bytes");
                }

                // 使用探测到的格式重新创建 Provider 和 Player
                var targetFormat = CreateMatchingFormat(_detectedFormat);
                _dataProvider = new StreamDataProvider(_audioEngine, targetFormat, _fileStream);
                _soundPlayer = new SoundPlayer(_audioEngine, targetFormat, _dataProvider);
                UpdateVolume();
                _playbackDevice.MasterMixer.AddComponent(_soundPlayer);

                _pausePosition = position;
                if (_isPlaying)
                {
                    _playbackStartTime = DateTime.Now;
                    _soundPlayer.Play();
                }

                FileLogger.Log($"[Seek] Seek completed to {position}");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError($"[Seek] Failed to seek to {position}", ex);
                return false;
            }
        }
    }

    public void SetVolume(double volume)
    {
        _currentVolume = Math.Clamp(volume, 0.0, 1.0);
        UpdateVolume();
    }

    public void SetMuted(bool muted)
    {
        _isMuted = muted;
        UpdateVolume();
    }

    private void UpdateVolume()
    {
        lock (_lock)
        {
            if (_soundPlayer != null)
            {
                _soundPlayer.Volume = _isMuted ? 0.0f : (float)_currentVolume;
            }
        }
    }

    public PlaybackState PlaybackState => _soundPlayer?.State ?? PlaybackState.Stopped;

    private void Cleanup()
    {
        if (_soundPlayer != null)
        {
            try
            {
                _playbackDevice?.MasterMixer.RemoveComponent(_soundPlayer);
                _soundPlayer.Stop();
                _soundPlayer.Dispose();
            }
            catch { }
            _soundPlayer = null;
        }

        _dataProvider?.Dispose();
        _dataProvider = null;
        _fileStream?.Dispose();
        _fileStream = null;
        _playbackDevice?.Stop();
        _playbackDevice?.Dispose();
        _playbackDevice = null;
        _isInitialized = false;
        _isPlaying = false;
    }

    public void Close()
    {
        lock (_lock)
        {
            FileLogger.Log("[Close] Closing audio playback");
            Cleanup();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _audioEngine?.Dispose();
        _audioEngine = null;
        FileLogger.Log("[Dispose] AudioPlaybackManager disposed");
    }

    #endregion
}

#endregion

#region 同步时钟

/// <summary>
/// 音视频同步时钟 - 使用音频位置作为主时钟
/// </summary>
public sealed class AVSyncClock : IDisposable
{
    private double _audioPositionSeconds;
    private readonly Stopwatch _systemClock;
    private TimeSpan _baseTime;
    private double _speedRatio = 1.0;
    private readonly object _lock = new();

    public const double MaxAllowedDriftMs = 50;
    public const double VideoCatchUpThresholdMs = 100;

    public AVSyncClock()
    {
        _systemClock = new Stopwatch();
    }

    public void UpdateAudioPosition(double positionSeconds)
    {
        lock (_lock)
        {
            _audioPositionSeconds = positionSeconds;
        }
    }

    public TimeSpan GetMediaTime()
    {
        lock (_lock)
        {
            if (_audioPositionSeconds > 0)
            {
                return TimeSpan.FromSeconds(_audioPositionSeconds / _speedRatio);
            }

            if (!_systemClock.IsRunning)
                return _baseTime;

            return _baseTime + TimeSpan.FromTicks((long)(_systemClock.Elapsed.Ticks / _speedRatio));
        }
    }

    public void Start(TimeSpan startPosition)
    {
        lock (_lock)
        {
            _baseTime = startPosition;
            _audioPositionSeconds = startPosition.TotalSeconds;
            _systemClock.Restart();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _systemClock.Stop();
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _systemClock.Start();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _systemClock.Stop();
            _systemClock.Reset();
            _audioPositionSeconds = 0;
        }
    }

    public double SpeedRatio
    {
        get => _speedRatio;
        set => _speedRatio = Math.Clamp(value, 0.1, 10.0);
    }

    public TimeSpan CalculateVideoDelay(double framePresentationTimeMs)
    {
        var currentTime = GetMediaTime().TotalMilliseconds;
        var delay = framePresentationTimeMs - currentTime;
        return TimeSpan.FromMilliseconds(delay);
    }

    public bool ShouldDropFrame(double framePresentationTimeMs)
    {
        var currentTime = GetMediaTime().TotalMilliseconds;
        var drift = currentTime - framePresentationTimeMs;
        return drift > VideoCatchUpThresholdMs;
    }

    public void Dispose()
    {
        Stop();
    }
}

#endregion

#region 媒体播放器控件

public sealed class MediaPlayer : FrameworkElement, IDisposable
{
    #region 私有字段

    private VideoCapture? _videoCapture;
    private AudioPlaybackManager? _audioManager;
    private VideoFrameBuffer? _frameBuffer;
    private string? _mediaPath;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _hasVideo;
    private bool _hasAudio;
    private CancellationTokenSource? _playbackCts;
    private Task? _videoDecodeTask;
    private Task? _videoRenderTask;
    private Task? _audioSyncTask;
    private readonly object _lock = new();

    private WriteableBitmap? _frameBitmap;
    private ImageSource? _frameImage;
    private readonly SolidColorBrush _backgroundBrush = new(Color.FromRgb(0, 0, 0));
    private int _videoWidth;
    private int _videoHeight;
    private double _videoFps;
    private double _frameDelayMs;

    private double _currentVolume = 0.5;
    private bool _isMuted;

    private AVSyncClock? _syncClock;

    private double _displayScale = 1.0;

    private Size _arrangedSize;
    private bool _isArranged;
    private bool _pendingPlay;

    private int _framesRendered;
    private int _framesDropped;
    private int _framesLate;

    private int _targetWidth;
    private int _targetHeight;

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(string), typeof(MediaPlayer),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register(nameof(Volume), typeof(double), typeof(MediaPlayer),
            new PropertyMetadata(0.5, OnVolumeChanged, CoerceVolume));

    public static readonly DependencyProperty IsMutedProperty =
        DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(MediaPlayer),
            new PropertyMetadata(false, OnIsMutedChanged));

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(MediaPlayer),
            new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(MediaPlayer),
            new PropertyMetadata(StretchDirection.Both, OnStretchChanged));

    public static readonly DependencyProperty LoadedBehaviorProperty =
        DependencyProperty.Register(nameof(LoadedBehavior), typeof(MediaState), typeof(MediaPlayer),
            new PropertyMetadata(MediaState.Play, OnLoadedBehaviorChanged));

    public static readonly DependencyProperty UnloadedBehaviorProperty =
        DependencyProperty.Register(nameof(UnloadedBehavior), typeof(MediaState), typeof(MediaPlayer),
            new PropertyMetadata(MediaState.Close));

    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register(nameof(SpeedRatio), typeof(double), typeof(MediaPlayer),
            new PropertyMetadata(1.0, OnSpeedRatioChanged, CoerceSpeedRatio));

    #endregion

    #region 路由事件

    public static readonly RoutedEvent MediaOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(MediaOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MediaPlayer));

    public static readonly RoutedEvent MediaEndedEvent =
        EventManager.RegisterRoutedEvent(nameof(MediaEnded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(MediaPlayer));

    public static readonly RoutedEvent MediaFailedEvent =
        EventManager.RegisterRoutedEvent(nameof(MediaFailed), RoutingStrategy.Bubble,
            typeof(EventHandler<MediaFailedEventArgs>), typeof(MediaPlayer));

    public event RoutedEventHandler? MediaOpened
    {
        add => AddHandler(MediaOpenedEvent, value);
        remove => RemoveHandler(MediaOpenedEvent, value);
    }

    public event RoutedEventHandler? MediaEnded
    {
        add => AddHandler(MediaEndedEvent, value);
        remove => RemoveHandler(MediaEndedEvent, value);
    }

    public event EventHandler<MediaFailedEventArgs>? MediaFailed
    {
        add => AddHandler(MediaFailedEvent, value);
        remove => RemoveHandler(MediaFailedEvent, value);
    }

    #endregion

    #region CLR 属性

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double Volume
    {
        get => (double)GetValue(VolumeProperty)!;
        set => SetValue(VolumeProperty, value);
    }

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty)!;
        set => SetValue(IsMutedProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    public MediaState LoadedBehavior
    {
        get => (MediaState)GetValue(LoadedBehaviorProperty)!;
        set => SetValue(LoadedBehaviorProperty, value);
    }

    public MediaState UnloadedBehavior
    {
        get => (MediaState)GetValue(UnloadedBehaviorProperty)!;
        set => SetValue(UnloadedBehaviorProperty, value);
    }

    public double SpeedRatio
    {
        get => (double)GetValue(SpeedRatioProperty)!;
        set => SetValue(SpeedRatioProperty, value);
    }

    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                SeekToPosition(value);
            }
        }
    }

    public Duration NaturalDuration => _duration == TimeSpan.Zero
        ? Duration.Automatic
        : new Duration(_duration);

    public int NaturalVideoWidth => _videoWidth;
    public int NaturalVideoHeight => _videoHeight;
    public bool HasAudio => _hasAudio;
    public bool HasVideo => _hasVideo;
    public bool CanPause { get; private set; } = true;
    public bool IsPlaying => _isPlaying;
    public string SyncStats => $"Frames: {_framesRendered} Dropped: {_framesDropped} Late: {_framesLate}";

    #endregion

    #region 构造函数与析构函数

    public MediaPlayer()
    {
        Unloaded += OnUnloaded;
        _audioManager = new AudioPlaybackManager();
        FileLogger.Log("MediaPlayer created with OpenCV and SoundFlow + FFmpeg (cross-platform)");
    }

    ~MediaPlayer()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        FileLogger.Log("MediaPlayer disposing");
        StopPlayback();
        _syncClock?.Dispose();
        _videoCapture?.Dispose();
        _frameBuffer?.Dispose();
        _audioManager?.Dispose();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ApplyUnloadedBehavior();
    }

    #endregion

    #region 公共方法

    public void Play()
    {
        if (_isPlaying) return;

        FileLogger.Log("Play() called");
        _isPlaying = true;
        StartPlaybackInternal();
    }

    public void Pause()
    {
        if (!_isPlaying) return;

        FileLogger.Log("Pause() called");
        _isPlaying = false;
        PausePlayback();
    }

    public void Stop()
    {
        FileLogger.Log("Stop() called");
        _isPlaying = false;
        _pendingPlay = false;
        StopPlayback();
        _position = TimeSpan.Zero;
        InvalidateVisual();
    }

    public void Close()
    {
        FileLogger.Log("Close() called");
        _isPlaying = false;
        _pendingPlay = false;
        CloseMedia();
    }

    #endregion

    #region 布局与渲染

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_hasVideo || _videoWidth <= 0 || _videoHeight <= 0)
        {
            return new Size(0, 0);
        }

        var naturalSize = new Size(_videoWidth, _videoHeight);
        var result = ComputeScaledSize(availableSize, naturalSize);
        return result;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arrangedSize = finalSize;
        _isArranged = finalSize.Width > 0 && finalSize.Height > 0;

        UpdateDisplayScale(finalSize);

        if (_isArranged && _pendingPlay)
        {
            _pendingPlay = false;
            Dispatcher.MainDispatcher?.BeginInvoke(() =>
            {
                if (_isPlaying)
                {
                    StartPlaybackWithSize(_arrangedSize);
                }
            });
        }

        return finalSize;
    }

    private Size ComputeScaledSize(Size availableSize, Size contentSize)
    {
        if (contentSize.Width <= 0 || contentSize.Height <= 0)
            return new Size(0, 0);

        var scaleX = 1.0;
        var scaleY = 1.0;

        var isWidthInfinite = double.IsInfinity(availableSize.Width);
        var isHeightInfinite = double.IsInfinity(availableSize.Height);

        if (Stretch != Stretch.None && (!isWidthInfinite || !isHeightInfinite))
        {
            scaleX = availableSize.Width / contentSize.Width;
            scaleY = availableSize.Height / contentSize.Height;

            if (isWidthInfinite) scaleX = scaleY;
            else if (isHeightInfinite) scaleY = scaleX;
            else
            {
                switch (Stretch)
                {
                    case Stretch.Uniform:
                        scaleX = scaleY = Math.Min(scaleX, scaleY);
                        break;
                    case Stretch.UniformToFill:
                        scaleX = scaleY = Math.Max(scaleX, scaleY);
                        break;
                }
            }

            switch (StretchDirection)
            {
                case StretchDirection.UpOnly:
                    scaleX = Math.Max(1.0, scaleX);
                    scaleY = Math.Max(1.0, scaleY);
                    break;
                case StretchDirection.DownOnly:
                    scaleX = Math.Min(1.0, scaleX);
                    scaleY = Math.Min(1.0, scaleY);
                    break;
            }
        }

        scaleX = Math.Max(0.01, scaleX);
        scaleY = Math.Max(0.01, scaleY);

        return new Size(contentSize.Width * scaleX, contentSize.Height * scaleY);
    }

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        if (!_hasVideo && _hasAudio)
        {
            dc.DrawRectangle(_backgroundBrush, null, rect);

            var text = new FormattedText("Audio Only", "Segoe UI", 14)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            var textWidth = text.Width;
            var textHeight = text.Height;
            dc.DrawText(text, new Point((rect.Width - textWidth) / 2, (rect.Height - textHeight) / 2));
            return;
        }

        dc.DrawRectangle(_backgroundBrush, null, rect);

        var imageSourceToDraw = _frameImage as ImageSource ?? _frameBitmap as ImageSource;
        if (imageSourceToDraw != null && _hasVideo)
        {
            var frameSize = new Size(imageSourceToDraw.Width, imageSourceToDraw.Height);
            var scaledSize = ComputeScaledSize(rect.Size, frameSize);

            if (scaledSize.Width > 0 && scaledSize.Height > 0)
            {
                var x = (rect.Width - scaledSize.Width) / 2;
                var y = (rect.Height - scaledSize.Height) / 2;

                dc.DrawImage(imageSourceToDraw, new Rect(x, y, scaledSize.Width, scaledSize.Height));
            }
        }
        else if (_hasVideo)
        {
            var text = new FormattedText("Loading...", "Segoe UI", 14)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            var textWidth = text.Width;
            var textHeight = text.Height;
            dc.DrawText(text, new Point((rect.Width - textWidth) / 2, (rect.Height - textHeight) / 2));
        }
    }

    #endregion

    #region 属性变更处理程序

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaPlayer media)
        {
            media.OnSourceChanged((string?)e.OldValue, (string?)e.NewValue);
        }
    }

    private void OnSourceChanged(string? oldPath, string? newPath)
    {
        FileLogger.Log($"OnSourceChanged: old={oldPath}, new={newPath}");
        _mediaPath = newPath;
        if (!string.IsNullOrEmpty(newPath))
        {
            OpenMedia(newPath);
        }
        else
        {
            CloseMedia();
        }
    }

    private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaPlayer media)
        {
            media.SetVolumeInternal((double)e.NewValue!);
        }
    }

    private static object CoerceVolume(DependencyObject d, object? value)
    {
        var volume = (double)(value ?? 0.5);
        return Math.Clamp(volume, 0.0, 1.0);
    }

    private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaPlayer media)
        {
            media.SetMutedInternal((bool)e.NewValue!);
        }
    }

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaPlayer media)
        {
            media.InvalidateMeasure();
            media.InvalidateVisual();
        }
    }

    private static void OnLoadedBehaviorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaPlayer media)
        {
            media.ApplyLoadedBehavior((MediaState)e.NewValue!);
        }
    }

    private static void OnSpeedRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaPlayer media)
        {
            var ratio = (double)e.NewValue!;
            if (media._syncClock != null)
            {
                media._syncClock.SpeedRatio = ratio;
            }
        }
    }

    private static object CoerceSpeedRatio(DependencyObject d, object? value)
    {
        var ratio = (double)(value ?? 1.0);
        return Math.Clamp(ratio, 0.1, 10.0);
    }

    #endregion

    #region 媒体打开与初始化

    private void OpenMedia(string source)
    {
        FileLogger.Log($"OpenMedia: {source}");

        try
        {
            StopPlayback();

            var hasVideo = false;
            var hasAudio = false;
            var videoWidth = 0;
            var videoHeight = 0;
            var videoFps = 30.0;
            var duration = 0.0;
            var audioDuration = 0.0;

            //尝试打开视频
            _videoCapture?.Dispose();
            _videoCapture = new VideoCapture(source);

            if (_videoCapture.IsOpened())
            {
                var videoInfo = GetVideoInfo(_videoCapture);
                hasVideo = videoInfo.HasVideo;
                videoWidth = videoInfo.VideoWidth;
                videoHeight = videoInfo.VideoHeight;
                videoFps = videoInfo.VideoFps;
                duration = videoInfo.Duration;

                FileLogger.Log($"Video detected: {videoWidth}x{videoHeight}@{videoFps}fps, Duration: {duration}s");
            }
            else
            {
                _videoCapture.Dispose();
                _videoCapture = null;
                FileLogger.Log("No video stream found");
            }

            // 使用 SoundFlow + FFmpeg 检测音频
            if (_audioManager != null)
            {
                _audioManager.Close();

                if (_audioManager.OpenAndPlay(source))
                {
                    hasAudio = true;
                    audioDuration = _audioManager.Duration.TotalSeconds;

                    if (duration <= 0 && audioDuration > 0)
                        duration = audioDuration;

                    FileLogger.Log($"Audio detected with SoundFlow: Duration={audioDuration}s");

                    _audioManager.Stop();
                }
                else
                {
                    FileLogger.Log("No audio stream found with SoundFlow");
                }
            }

            _hasVideo = hasVideo;
            _hasAudio = hasAudio;
            _videoWidth = videoWidth;
            _videoHeight = videoHeight;
            _videoFps = videoFps > 0 ? videoFps : 30.0;
            _duration = TimeSpan.FromSeconds(duration);
            _frameDelayMs = 1000.0 / _videoFps;

            // 创建同步时钟
            _syncClock?.Dispose();
            _syncClock = new AVSyncClock();
            _syncClock.SpeedRatio = SpeedRatio;

            // 创建帧缓冲区
            _frameBuffer?.Dispose();
            _frameBuffer = new VideoFrameBuffer(5);

            FileLogger.Log($"OpenMedia complete: Video={_hasVideo}, Audio={_hasAudio}, Duration={_duration}");

            if (_hasVideo)
            {
                UpdateDisplayScale(RenderSize);
                InvalidateMeasure();
            }

            RaiseMediaOpened();
            ApplyLoadedBehavior(LoadedBehavior);
        }
        catch (Exception ex)
        {
            FileLogger.LogError("OpenMedia failed", ex);
            RaiseMediaFailed(ex);
        }
    }

    private MediaInfo GetVideoInfo(VideoCapture capture)
    {
        var info = new MediaInfo();

        info.VideoWidth = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        info.VideoHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        info.VideoFps = capture.Get(VideoCaptureProperties.Fps);
        info.HasVideo = info.VideoWidth > 0 && info.VideoHeight > 0;

        var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
        if (info.VideoFps > 0 && frameCount > 0)
        {
            info.Duration = frameCount / info.VideoFps;
        }

        if (info.VideoFps <= 0)
            info.VideoFps = 30.0;

        return info;
    }

    #endregion

    #region 播放控制

    private void StartPlaybackInternal()
    {
        FileLogger.Log($"StartPlaybackInternal: mediaPath={_mediaPath}, hasVideo={_hasVideo}, hasAudio={_hasAudio}, isArranged={_isArranged}");

        if (string.IsNullOrEmpty(_mediaPath))
        {
            FileLogger.Log("StartPlaybackInternal: aborted - no media path");
            return;
        }

        if (!_isArranged || _arrangedSize.Width <= 0 || _arrangedSize.Height <= 0)
        {
            FileLogger.Log("StartPlaybackInternal: Layout not ready, deferring...");
            _pendingPlay = true;
            return;
        }

        StartPlaybackWithSize(_arrangedSize);
    }

    private void StartPlaybackWithSize(Size targetSize)
    {
        FileLogger.Log($"StartPlaybackWithSize: targetSize={targetSize.Width}x{targetSize.Height}");

        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        // 重置帧统计
        _framesRendered = 0;
        _framesDropped = 0;
        _framesLate = 0;

        // 计算视频渲染尺寸
        var (videoWidth, videoHeight) = CalculateVideoRenderSize(targetSize);
        _targetWidth = videoWidth;
        _targetHeight = videoHeight;

        FileLogger.Log($"Video render size: {videoWidth}x{videoHeight}");

        // 先启动音频，再启动同步时钟，最后启动视频
        if (_hasAudio && _audioManager != null)
        {
            _audioManager.SetVolume(_currentVolume);
            _audioManager.SetMuted(_isMuted);

            if (_position > TimeSpan.Zero)
            {
                _audioManager.Seek(_position);
            }

            _audioManager.Play();

            Thread.Sleep(50); // 等待音频启动

            FileLogger.Log("SoundFlow audio playback started");
        }

        // 启动同步时钟
        _syncClock?.Start(_position);

        // 启动音频同步任务
        if (_hasAudio)
        {
            _audioSyncTask = Task.Run(() => AudioSyncLoop(token), token);
        }

        // 启动视频播放
        if (_hasVideo && videoWidth > 0 && videoHeight > 0)
        {
            _videoCapture?.Dispose();
            _videoCapture = new VideoCapture(_mediaPath!);

            if (!_videoCapture.IsOpened())
            {
                FileLogger.LogError("Failed to reopen video stream");
                RaiseMediaFailed(new InvalidOperationException("Failed to open video"));
                return;
            }

            if (_position > TimeSpan.Zero)
            {
                var framePos = _position.TotalSeconds * _videoFps;
                _videoCapture.Set(VideoCaptureProperties.PosFrames, framePos);
            }

            _videoDecodeTask = Task.Run(() => VideoDecodeLoop(token, videoWidth, videoHeight), token);
            _videoRenderTask = Task.Run(() => VideoRenderLoop(token), token);

            FileLogger.Log("Video playback started");
        }

        // 纯音频模式
        if (!_hasVideo && _hasAudio)
        {
            _videoRenderTask = Task.Run(() => AudioOnlyPositionLoop(token), token);
        }
    }

    private (int width, int height) CalculateVideoRenderSize(Size availableSize)
    {
        if (_videoWidth <= 0 || _videoHeight <= 0)
            return (0, 0);

        var naturalSize = new Size(_videoWidth, _videoHeight);
        var scaledSize = ComputeScaledSize(availableSize, naturalSize);

        var targetWidth = (int)(scaledSize.Width + 0.5);
        var targetHeight = (int)(scaledSize.Height + 0.5);

        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        targetWidth = (targetWidth / 2) * 2;
        targetHeight = (targetHeight / 2) * 2;

        return (targetWidth, targetHeight);
    }

    private void PausePlayback()
    {
        FileLogger.Log("PausePlayback");
        _syncClock?.Pause();
        _audioManager?.Pause();
    }

    private void StopPlayback()
    {
        FileLogger.Log("StopPlayback called");

        _playbackCts?.Cancel();

        try
        {
            _videoDecodeTask?.Wait(TimeSpan.FromSeconds(2));
            _videoRenderTask?.Wait(TimeSpan.FromSeconds(2));
            _audioSyncTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        _playbackCts?.Dispose();
        _playbackCts = null;
        _videoDecodeTask = null;
        _videoRenderTask = null;
        _audioSyncTask = null;
        _isPlaying = false;
        _pendingPlay = false;

        _syncClock?.Stop();
        _frameBuffer?.Clear();

        _audioManager?.Stop();
        _videoCapture?.Dispose();
        _videoCapture = null;

        FileLogger.Log($"StopPlayback completed. Stats: {SyncStats}");
    }

    private void CloseMedia()
    {
        FileLogger.Log("CloseMedia");
        StopPlayback();

        _videoCapture?.Dispose();
        _videoCapture = null;
        _audioManager?.Close();
        _frameBuffer?.Dispose();
        _frameBuffer = null;

        _frameBitmap = null;
        _frameImage = null;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        _hasVideo = false;
        _hasAudio = false;
        _videoWidth = 0;
        _videoHeight = 0;
        _isArranged = false;
        _arrangedSize = Size.Empty;
        _pendingPlay = false;

        InvalidateMeasure();
        InvalidateVisual();
    }

    #endregion

    #region 播放循环

    private void VideoDecodeLoop(CancellationToken token, int targetWidth, int targetHeight)
    {
        FileLogger.Log($"VideoDecodeLoop started: {targetWidth}x{targetHeight}, FPS={_videoFps:F2}");

        try
        {
            var frameIndex = 0;

            using var mat = new Mat();

            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_videoCapture == null || !_videoCapture.Read(mat) || mat.Empty())
                {
                    FileLogger.Log("VideoDecodeLoop: End of stream");
                    break;
                }

                var framePts = frameIndex * _frameDelayMs;

                Mat processedMat;
                if (mat.Width != targetWidth || mat.Height != targetHeight)
                {
                    using var resizedMat = mat.Resize(new OpenCvSharp.Size(targetWidth, targetHeight));
                    processedMat = resizedMat.Clone();
                }
                else
                {
                    processedMat = mat.Clone();
                }

                var frameData = MatToBgraBytes(processedMat, targetWidth, targetHeight);
                processedMat.Dispose();

                var frame = new VideoFrame(frameData, targetWidth, targetHeight, (long)framePts, frameIndex);

                if (!_frameBuffer!.TryAdd(frame, 100))
                {
                    if (_frameBuffer.TryTake(out var oldFrame))
                    {
                        oldFrame?.Dispose();
                    }
                    _frameBuffer.TryAdd(frame);
                }

                frameIndex++;
            }

            FileLogger.Log($"VideoDecodeLoop ended. Total frames decoded: {frameIndex}");
        }
        catch (OperationCanceledException)
        {
            FileLogger.Log("VideoDecodeLoop cancelled");
        }
        catch (Exception ex)
        {
            FileLogger.LogError("VideoDecodeLoop error", ex);
        }
    }

    private async Task VideoRenderLoop(CancellationToken token)
    {
        FileLogger.Log("VideoRenderLoop started");

        try
        {
            var firstFrame = true;
            var lastLogTime = DateTime.Now;

            while (!token.IsCancellationRequested && _isPlaying)
            {
                var frame = _frameBuffer?.Take(token);
                if (frame == null) continue;

                var delay = _syncClock!.CalculateVideoDelay(frame.TimestampMs);

                if (delay > TimeSpan.FromMilliseconds(1))
                {
                    await PreciseDelay(delay, token);
                }
                else if (delay < TimeSpan.FromMilliseconds(-AVSyncClock.VideoCatchUpThresholdMs))
                {
                    _framesDropped++;
                    if (frame.FrameIndex % 30 == 0)
                    {
                        FileLogger.Log($"Dropping frame {frame.FrameIndex} (lag: {-delay.TotalMilliseconds:F1}ms)");
                    }
                    frame.Dispose();
                    continue;
                }
                else if (delay < TimeSpan.Zero)
                {
                    _framesLate++;
                }

                _position = _syncClock.GetMediaTime();
                _framesRendered++;

                var frameData = frame.Data;
                var width = frame.Width;
                var height = frame.Height;

                Dispatcher.MainDispatcher?.BeginInvoke(() =>
                {
                    UpdateFrameBitmap(frameData, width, height);
                });

                if (firstFrame)
                {
                    FileLogger.Log($"First frame rendered at PTS={frame.TimestampMs:F1}ms");
                    firstFrame = false;
                }
                else if ((DateTime.Now - lastLogTime).TotalSeconds >= 5)
                {
                    FileLogger.Log($"Render stats: {SyncStats}, Buffer: {_frameBuffer?.Count}, Drift: {delay.TotalMilliseconds:F1}ms");
                    lastLogTime = DateTime.Now;
                }

                frame.Dispose();
            }

            FileLogger.Log($"VideoRenderLoop ended. Rendered: {_framesRendered}, Dropped: {_framesDropped}");
        }
        catch (OperationCanceledException)
        {
            FileLogger.Log("VideoRenderLoop cancelled");
        }
        catch (Exception ex)
        {
            FileLogger.LogError("VideoRenderLoop error", ex);
        }
    }

    /// <summary>
    /// 音频同步循环 - 更新同步时钟的音频位置
    /// </summary>
    private void AudioSyncLoop(CancellationToken token)
    {
        FileLogger.Log("AudioSyncLoop started");

        try
        {
            // 等待音频启动
            Thread.Sleep(100);

            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_audioManager != null && _syncClock != null)
                {
                    var audioPos = _audioManager.CurrentPosition.TotalSeconds;
                    _syncClock.UpdateAudioPosition(audioPos);
                    _position = _audioManager.CurrentPosition;
                }

                Thread.Sleep(10);
            }
        }
        catch (OperationCanceledException)
        {
            FileLogger.Log("AudioSyncLoop cancelled");
        }
        catch (Exception ex)
        {
            FileLogger.LogError("AudioSyncLoop error", ex);
        }
    }

    private async Task AudioOnlyPositionLoop(CancellationToken token)
    {
        FileLogger.Log("AudioOnlyPositionLoop started");

        try
        {
            while (!token.IsCancellationRequested && _isPlaying)
            {
                if (_audioManager != null)
                {
                    _position = _audioManager.CurrentPosition;
                }

                if (_duration > TimeSpan.Zero && _position >= _duration)
                {
                    Dispatcher.MainDispatcher?.BeginInvoke(() => RaiseMediaEnded());
                    break;
                }

                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            FileLogger.Log("AudioOnlyPositionLoop cancelled");
        }
    }

    #endregion

    #region 辅助方法

    private byte[] MatToBgraBytes(Mat mat, int width, int height)
    {
        var pixelCount = width * height;
        var bgraData = new byte[pixelCount * 4];

        using var continuousMat = mat.IsContinuous() ? mat : mat.Clone();

        using var bgrMat = continuousMat.Type() == MatType.CV_8UC3
            ? continuousMat
            : continuousMat.CvtColor(ColorConversionCodes.RGBA2BGR);

        unsafe
        {
            byte* srcPtr = bgrMat.DataPointer;

            for (int i = 0; i < pixelCount; i++)
            {
                int srcIdx = i * 3;
                int dstIdx = i * 4;

                bgraData[dstIdx + 0] = srcPtr[srcIdx + 0];
                bgraData[dstIdx + 1] = srcPtr[srcIdx + 1];
                bgraData[dstIdx + 2] = srcPtr[srcIdx + 2];
                bgraData[dstIdx + 3] = 255;
            }
        }

        return bgraData;
    }

    private async Task PreciseDelay(TimeSpan delay, CancellationToken token)
    {
        if (delay <= TimeSpan.Zero) return;

        const int taskDelayThresholdMs = 20;

        if (delay.TotalMilliseconds > taskDelayThresholdMs)
        {
            var remaining = delay - TimeSpan.FromMilliseconds(taskDelayThresholdMs);
            try
            {
                await Task.Delay(remaining, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < delay - remaining && !token.IsCancellationRequested)
            {
                Thread.SpinWait(1);
            }
        }
        else
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < delay && !token.IsCancellationRequested)
            {
                Thread.SpinWait(1);
            }
        }
    }

    #endregion

    #region 帧渲染

    private void UpdateFrameBitmap(byte[] pixelData, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixelData == null || pixelData.Length == 0)
            return;

        var expectedLength = width * height * 4;
        if (pixelData.Length < expectedLength)
            return;

        var needsNewBitmap = _frameBitmap == null ||
                            _frameBitmap.Width != width ||
                            _frameBitmap.Height != height;

        if (needsNewBitmap)
        {
            if (width > 8192 || height > 8192)
                return;

            try
            {
                _frameBitmap = new WriteableBitmap(
                    width, height,
                    96, 96,
                    PixelFormats.Bgra32, null);
            }
            catch
            {
                _frameBitmap = null;
                return;
            }
        }

        try
        {
            var stride = width * 4;
            _frameBitmap.WritePixels(
                new Int32Rect(0, 0, width, height),
                pixelData,
                stride,
                0);

            try
            {
                var bmp = EncodeBmp32TopDown(pixelData, width, height);
                _frameImage = BitmapImage.FromBytes(bmp);
            }
            catch { }

            InvalidateVisual();
        }
        catch { }
    }

    private static byte[] EncodeBmp32TopDown(byte[] bgraData, int width, int height)
    {
        if (bgraData == null) throw new ArgumentNullException(nameof(bgraData));

        var stride = width * 4;
        var pixelDataSize = (uint)(stride * height);
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var bfOffBits = fileHeaderSize + infoHeaderSize;
        var bfSize = bfOffBits + pixelDataSize;

        using var ms = new MemoryStream((int)bfSize);
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0x4D42);
        bw.Write((uint)bfSize);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((uint)bfOffBits);

        bw.Write((uint)infoHeaderSize);
        bw.Write((int)width);
        bw.Write((int)(-height));
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)0);
        bw.Write((uint)pixelDataSize);
        bw.Write((int)3780);
        bw.Write((int)3780);
        bw.Write((uint)0);
        bw.Write((uint)0);

        bw.Write(bgraData, 0, bgraData.Length);
        bw.Flush();
        return ms.ToArray();
    }

    private void UpdateDisplayScale(Size availableSize)
    {
        if (!_hasVideo || _videoWidth <= 0 || _videoHeight <= 0) return;

        var displayWidth = availableSize.Width > 0 ? availableSize.Width : _videoWidth;
        var displayHeight = availableSize.Height > 0 ? availableSize.Height : _videoHeight;

        var scaleX = displayWidth / _videoWidth;
        var scaleY = displayHeight / _videoHeight;

        switch (Stretch)
        {
            case Stretch.None:
                _displayScale = 1.0;
                break;
            case Stretch.Fill:
                _displayScale = Math.Max(scaleX, scaleY);
                break;
            case Stretch.Uniform:
                _displayScale = Math.Min(scaleX, scaleY);
                break;
            case Stretch.UniformToFill:
                _displayScale = Math.Max(scaleX, scaleY);
                break;
        }

        switch (StretchDirection)
        {
            case StretchDirection.UpOnly:
                _displayScale = Math.Max(1.0, _displayScale);
                break;
            case StretchDirection.DownOnly:
                _displayScale = Math.Min(1.0, _displayScale);
                break;
        }

        _displayScale = Math.Clamp(_displayScale, 0.1, 4.0);
    }

    #endregion

    #region 跳转

    private void SeekToPosition(TimeSpan position)
    {
        FileLogger.Log($"SeekToPosition: {position}");

        var wasPlaying = _isPlaying;
        StopPlayback();

        _position = position;

        if (_audioManager != null)
        {
            _audioManager.Seek(position);
        }

        if (wasPlaying)
        {
            _isPlaying = true;
            StartPlaybackInternal();
        }
    }

    #endregion

    #region 音量与静音

    private void SetVolumeInternal(double volume)
    {
        _currentVolume = volume;
        _audioManager?.SetVolume(volume);
    }

    private void SetMutedInternal(bool isMuted)
    {
        _isMuted = isMuted;
        _audioManager?.SetMuted(isMuted);
    }

    #endregion

    #region 行为应用

    private void ApplyLoadedBehavior(MediaState state)
    {
        FileLogger.Log($"ApplyLoadedBehavior: {state}, isArranged={_isArranged}");

        switch (state)
        {
            case MediaState.Play:
                _isPlaying = true;
                StartPlaybackInternal();
                break;
            case MediaState.Pause:
                _isPlaying = false;
                PausePlayback();
                break;
            case MediaState.Stop:
                _isPlaying = false;
                StopPlayback();
                break;
            case MediaState.Close:
                CloseMedia();
                break;
        }
    }

    private void ApplyUnloadedBehavior()
    {
        FileLogger.Log($"ApplyUnloadedBehavior: {UnloadedBehavior}");

        switch (UnloadedBehavior)
        {
            case MediaState.Close:
                CloseMedia();
                break;
            case MediaState.Stop:
                Stop();
                break;
            case MediaState.Pause:
                Pause();
                break;
        }
    }

    #endregion

    #region 事件触发

    private void RaiseMediaOpened()
    {
        FileLogger.Log("RaiseMediaOpened");
        RaiseEvent(new RoutedEventArgs(MediaOpenedEvent, this));
    }

    private void RaiseMediaEnded()
    {
        FileLogger.Log("RaiseMediaEnded");
        _isPlaying = false;
        RaiseEvent(new RoutedEventArgs(MediaEndedEvent, this));
    }

    private void RaiseMediaFailed(Exception exception)
    {
        FileLogger.LogError("RaiseMediaFailed", exception);
        _isPlaying = false;
        RaiseEvent(new MediaFailedEventArgs(MediaFailedEvent, this, exception));
    }

    #endregion
}

#endregion