using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Threading;

internal static class PlaybackSmoke
{
    private static readonly List<object> Evidence = [];
    private static readonly List<string> Failures = [];
    private static MediaElement _media = null!;
    private static Window _window = null!;
    private static Window? _fullscreen;
    private static DispatcherTimer _timer = null!;
    private static IEnumerator<Func<bool>> _script = null!;
    private static Func<bool>? _condition;
    private static string _output = "";
    private static int _opened;
    private static int _ended;
    private static bool _finished;
    private static bool _expectFailure;
    private static int _expectedFailures;
    private static Exception? _mediaFailure;

    internal static int Run(string path, string output, string backend)
    {
        _output = Path.GetFullPath(output);
        Directory.CreateDirectory(_output);
        Environment.SetEnvironmentVariable("JALIUM_RENDER_BACKEND", backend);
        var requested = backend.ToLowerInvariant() switch
        {
            "d3d12" => RenderBackend.D3D12,
            "vulkan" => RenderBackend.Vulkan,
            "software" => RenderBackend.Software,
            _ => throw new ArgumentException("Expected d3d12, vulkan or software.")
        };
        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromMinutes(2));
            Console.Error.WriteLine("PLAYBACK TIMEOUT: dispatcher or native media operation did not complete.");
            Environment.Exit(3);
        }) { IsBackground = true, Name = "Media smoke watchdog" }.Start();

        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(RenderTargetDrawingContext).TypeHandle);
        var context = RenderContext.GetOrCreateCurrent(requested);
        if (context.Backend != requested)
            throw new InvalidOperationException($"Requested {requested}, actually loaded {context.Backend}.");
        var app = new Application();
        _media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            ScrubbingEnabled = true,
            Stretch = Stretch.Uniform,
            Volume = 0.05,
        };
        _media.MediaOpened += (_, _) => _opened++;
        _media.MediaEnded += (_, _) => _ended++;
        _media.MediaFailed += (_, e) =>
        {
            if (_expectFailure) _expectedFailures++;
            else _mediaFailure = e.ErrorException;
        };
        _window = new Window
        {
            Title = "Jalium actual MediaElement playback verification",
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 720, Height = 460,
            Content = _media,
        };
        _script = Script(new Uri(Path.GetFullPath(path)));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick += Pump;
        _timer.Start();
        app.Run(_window);
        var stats = VideoSurfaceStats.Query();
        if (!_finished) Failures.Add("Window closed before the script finished.");
        if (stats.SurfacesCreated != stats.SurfacesDestroyed)
            Failures.Add($"Video surface imbalance: {stats.SurfacesCreated}/{stats.SurfacesDestroyed}.");
        File.WriteAllText(Path.Combine(_output, "playback.json"), JsonSerializer.Serialize(new
        {
            passed = Failures.Count == 0, failures = Failures,
            backend = context.Backend.ToString(),
            renderThreadRequested = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD"),
            runtime = typeof(MediaElement).Assembly.Location,
            runtimeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MediaElement).Assembly.Location))),
            opened = _opened, ended = _ended, expectedFailures = _expectedFailures,
            surfaces = stats, evidence = Evidence,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PLAYBACK_RESULT={(Failures.Count == 0 ? "PASS" : "FAIL")} evidence={Evidence.Count} surface={stats.SurfacesCreated}/{stats.SurfacesDestroyed}");
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Pump(object? sender, EventArgs e)
    {
        try
        {
            if (_mediaFailure is not null) throw new InvalidOperationException("Unexpected MediaFailed.", _mediaFailure);
            if (_condition is not null && !_condition()) return;
            if (!_script.MoveNext())
            {
                _finished = true;
                _timer.Stop();
                _media.Dispose();
                _window.Content = null;
                _window.Close();
                return;
            }
            _condition = _script.Current;
        }
        catch (Exception exception)
        {
            Failures.Add(exception.ToString());
            Console.Error.WriteLine(exception);
            _timer.Stop();
            try { _fullscreen?.Close(); } catch { }
            _media.Dispose();
            _window.Close();
        }
    }

    private static IEnumerator<Func<bool>> Script(Uri source)
    {
        _media.Source = source;
        yield return Until(() => _opened == 1, "open");
        Check(!_media.IsPlaying, "Manual source open must not auto-play.");
        _media.Play();
        yield return Until(() => _media.Position.TotalSeconds >= 0.5, "initial playback");
        var first = Capture(_window, "playing-1");
        yield return Delay(0.35);
        var next = Capture(_window, "playing-2");
        Check(first.Hash != next.Hash, "Playback must show different actual pixels, not a static cover.");
        var audio = (AudioPlayer?)typeof(MediaElement).GetField("_audioManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_media);
        Evidence.Add(new { stage = "audio-clock", _media.HasAudio, position = audio?.Position.TotalSeconds, state = audio?.State.ToString() });
        if (_media.HasAudio) Check(audio!.Position > TimeSpan.Zero, "The native audio device clock did not advance.");

        _media.Pause();
        yield return Delay(0.1);
        var pausedPosition = _media.Position;
        var paused = Capture(_window, "paused-1");
        yield return Delay(0.3);
        var stable = Capture(_window, "paused-2");
        Check(paused.Hash == stable.Hash && _media.Position == pausedPosition && !_media.IsPlaying,
            "Pause must retain identical frame and position.");

        var duration = _media.NaturalDuration.TimeSpan;
        _media.Position = TimeSpan.FromSeconds(duration.TotalSeconds * 0.6);
        yield return Delay(0.5);
        var scrubbed = Capture(_window, "paused-seek");
        Check(!_media.IsPlaying && scrubbed.Hash != stable.Hash, "Paused seek must update the frame without resuming.");
        _media.Play();
        yield return Delay(0.3);
        var resumed = Capture(_window, "seek-resumed");
        Check(resumed.Hash != scrubbed.Hash, "Seek resume must continue with moving pixels.");

        _media.Pause();
        yield return Delay(0.1);
        var beforeReparent = _media.Position;
        var openedBefore = _opened;
        _window.Content = null;
        _fullscreen = new Window
        {
            Title = "Jalium playback reparent verification",
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 960, Height = 620, Owner = _window, Content = _media,
        };
        _fullscreen.Show();
        _fullscreen.WindowState = WindowState.Maximized;
        yield return Delay(0.3);
        Capture(_fullscreen, "fullscreen-paused");
        _fullscreen.Content = null;
        _window.Content = _media;
        _fullscreen.Close();
        _fullscreen = null;
        yield return Delay(0.25);
        Check(_opened == openedBefore && !_media.IsPlaying && _media.Position == beforeReparent,
            "Reparent must not reopen the decoder or resume a paused source.");
        Capture(_window, "reparent-return");

        _window.Width = 920;
        _window.Height = 540;
        yield return Delay(0.25);
        Capture(_window, "resized-paused");
        _window.WindowState = WindowState.Minimized;
        yield return Delay(0.15);
        _window.WindowState = WindowState.Normal;
        yield return Delay(0.25);
        Capture(_window, "restored-paused");
        Check(!_media.IsPlaying, "Minimize and restore must retain pause.");

        _media.Stop();
        Check(_media.Position == TimeSpan.Zero && !_media.IsPlaying, "Stop must reset playback intent and position.");
        _media.Play();
        yield return Until(() => _media.Position.TotalSeconds > 0.4, "replay after stop");
        Capture(_window, "stop-replay");

        int endedBefore = _ended;
        _media.Position = TimeSpan.FromSeconds(Math.Max(0, duration.TotalSeconds - 0.7));
        yield return Until(() => _ended > endedBefore, "natural end", 15);
        Check(!_media.IsPlaying, "Ended video must not report playing.");
        _media.Play();
        yield return Until(() => _media.Position.TotalSeconds > 0.35 && _media.Position < duration - TimeSpan.FromSeconds(1), "replay after EOF");
        Capture(_window, "eof-replay");

        for (int i = 0; i < 5; i++)
        {
            _media.Pause();
            _media.Position = TimeSpan.FromSeconds((i % 3 + 1) * duration.TotalSeconds / 5);
            _media.Play();
            yield return Delay(0.08);
        }
        yield return Delay(0.25);
        Capture(_window, "rapid-seeks");

        _expectFailure = true;
        _media.Source = new Uri(Path.Combine(_output, "missing-invalid-source.mp4"));
        yield return Until(() => _expectedFailures == 1, "invalid source failure");
        Check(!_media.IsPlaying, "An invalid replacement must not keep reporting old playback.");
        _expectFailure = false;
        int openBefore = _opened;
        _media.Source = source;
        yield return Until(() => _opened == openBefore + 1, "reopen after failure");
        _media.Play();
        yield return Until(() => _media.Position.TotalSeconds > 0.35, "recovery playback");
        Capture(_window, "recovered-source");
        Task closed = _media.CloseAsync();
        yield return Until(() => closed.IsCompleted, "close drain");
        Check(closed.IsCompletedSuccessfully, "Native media cleanup failed.");
        Evidence.Add(new { stage = "close-completed", syncStats = _media.SyncStats });
    }

    private sealed record Frame(string Hash, int Width, int Height, double NonblackRatio);
    private static Frame Capture(Window window, string name)
    {
        var target = window.RenderTarget ?? throw new InvalidOperationException("Missing render target.");
        Check(target.RequestReadback() == JaliumResult.Ok, "Backend does not support verified pixel readback.");
        window.ForceRenderFrame();
        var bytes = new byte[checked(target.Width * target.Height * 4)];
        Check(target.FetchReadback(bytes, (uint)target.Width * 4, out int width, out int height) == JaliumResult.Ok,
            "Actual window readback failed.");
        long nonblack = 0;
        for (int i = 0; i < bytes.Length; i += 4)
            if (bytes[i] + bytes[i + 1] + bytes[i + 2] > 45) nonblack++;
        double ratio = nonblack / (double)(width * height);
        Check(ratio > 0.12, $"{name}: actual video area is black or only contains placeholder text ({ratio:F4}).");
        var frame = new Frame(Convert.ToHexString(SHA256.HashData(bytes)), width, height, ratio);
        File.WriteAllBytes(Path.Combine(_output, name + ".bgra"), bytes);
        Evidence.Add(new { stage = name, pixels = frame, position = _media.Position.TotalSeconds, playing = _media.IsPlaying, stats = _media.SyncStats });
        Console.WriteLine($"FRAME {name} {width}x{height} nonblack={ratio:F4} position={_media.Position.TotalSeconds:F3} hash={frame.Hash}");
        return frame;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static Func<bool> Delay(double seconds)
    {
        var timer = Stopwatch.StartNew();
        return () => timer.Elapsed.TotalSeconds >= seconds;
    }
    private static Func<bool> Until(Func<bool> condition, string stage, double seconds = 10)
    {
        var timer = Stopwatch.StartNew();
        return () => condition() || (timer.Elapsed.TotalSeconds < seconds ? false
            : throw new TimeoutException($"Timed out during {stage}: position={_media.Position}, playing={_media.IsPlaying}, {_media.SyncStats}."));
    }
}
