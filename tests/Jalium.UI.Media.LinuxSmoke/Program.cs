using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This smoke test must run on Linux.");
    return 2;
}

var png = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
var pngWithRecoverableIdatCrcMismatch = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+4u0AAAAASUVORK5CYII=");

var imageDecoder = new NativeImageDecoder();
DecodedImage image = imageDecoder.Decode(png, NativePixelFormat.Bgra8);
if (image.Width != 1 || image.Height != 1 || image.Pixels.Length != 4)
{
    throw new InvalidOperationException("Linux PNG decoder returned an invalid image.");
}
DecodedImage crcTolerantImage = imageDecoder.Decode(
    pngWithRecoverableIdatCrcMismatch, NativePixelFormat.Rgba8);
if (crcTolerantImage.Width != 1 || crcTolerantImage.Height != 1 ||
    crcTolerantImage.Pixels.Length != 4 ||
    crcTolerantImage.Pixels.Span[0] != 0xff ||
    crcTolerantImage.Pixels.Span[1] != 0xff ||
    crcTolerantImage.Pixels.Span[2] != 0xff ||
    crcTolerantImage.Pixels.Span[3] != 0xff)
{
    throw new InvalidOperationException(
        "Linux PNG decoder did not preserve WIC-compatible CRC tolerance.");
}

var missingVideo = Path.Combine(
    Path.GetTempPath(), $"jalium-linux-missing-{Guid.NewGuid():N}.webm");
using (var video = new NativeVideoDecoder())
{
    try
    {
        video.Open(new Uri(missingVideo));
        throw new InvalidOperationException("Missing video unexpectedly opened.");
    }
    catch (NativeMediaException exception)
        when (exception.Status == NativeMediaStatus.IoError)
    {
    }
}

var devices = new NativeCameraSourceFactory().EnumerateDevices();
var microphones = new NativeMicrophoneSourceFactory().EnumerateDevices();

var missingAac = Path.Combine(
    Path.GetTempPath(), $"jalium-linux-missing-{Guid.NewGuid():N}.m4a");
using (var audio = new NativeAudioDecoder())
{
    try
    {
        audio.Open(missingAac);
        throw new InvalidOperationException("Missing AAC file unexpectedly opened.");
    }
    catch (NativeMediaException exception)
        when (exception.Status == NativeMediaStatus.IoError)
    {
    }
}

if (args.Length > 0)
{
    var source = Uri.TryCreate(args[0], UriKind.Absolute, out var absolute) &&
                 !string.IsNullOrEmpty(absolute.Scheme)
        ? absolute
        : new Uri(Path.GetFullPath(args[0]));
    var capabilities = NativeLinuxMedia.GetCapabilities();
    var required = LinuxMediaCapability.GStreamerRuntime |
                   LinuxMediaCapability.VideoCpuFrames |
                   LinuxMediaCapability.AudioDecode |
                   LinuxMediaCapability.TrackDiscovery |
                   LinuxMediaCapability.SubtitleDecode;
    if ((capabilities & required) != required)
    {
        throw new InvalidOperationException(
            $"Linux media capabilities are incomplete: {capabilities}.");
    }

    var tracks = NativeLinuxMedia.DiscoverTracks(source);
    var audioTracks = tracks.Where(track => track.Kind == MediaTrackKind.Audio).ToArray();
    var subtitleTracks = tracks.Where(track => track.Kind == MediaTrackKind.Subtitle).ToArray();
    if (audioTracks.Length != 2 || subtitleTracks.Length != 1 ||
        !audioTracks.Any(track => track.Language is "en" or "eng") ||
        !audioTracks.Any(track => track.Language is "ja" or "jpn"))
    {
        throw new InvalidOperationException(
            "Managed track discovery metadata was incomplete: " +
            string.Join("; ", tracks.Select(track =>
                $"{track.Kind}[{track.Index}] lang={track.Language} codec={track.Codec}")));
    }

    using (var video = new NativeVideoDecoder())
    {
        video.Open(source);
        if (video.ActiveVideoCodec != SupportedCodec.H264 ||
            !video.TryReadFrame(out var firstFrame) || firstFrame is null)
        {
            throw new InvalidOperationException("Managed H.264 frame decode failed.");
        }
        firstFrame.Dispose();
        video.Seek(TimeSpan.FromMilliseconds(900));
        if (!video.TryReadFrame(out var seekFrame) || seekFrame is null ||
            seekFrame.PresentationTime < TimeSpan.FromMilliseconds(750))
        {
            seekFrame?.Dispose();
            throw new InvalidOperationException("Managed accurate video seek failed.");
        }
        seekFrame.Dispose();
    }

    using (var audio = new NativeAudioDecoder { AudioTrackIndex = 1 })
    {
        audio.Open(source.IsFile ? source.LocalPath : source.AbsoluteUri);
        var samples = new float[Math.Max(1, audio.Channels) * 1024];
        if (audio.ReadFrames(samples) <= 0)
        {
            throw new InvalidOperationException("Managed selected audio-track decode failed.");
        }
    }
}

Console.WriteLine(
    $"Linux managed media smoke passed: PNG=valid+CRC-tolerant, cameraDevices={devices.Count}, " +
    $"microphoneDevices={microphones.Count}, AAC bridge=registered, realFixture={args.Length > 0}.");
return 0;
