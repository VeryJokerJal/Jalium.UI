using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This smoke test must run on Linux.");
    return 2;
}

var png = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

var imageDecoder = new NativeImageDecoder();
DecodedImage image = imageDecoder.Decode(png, NativePixelFormat.Bgra8);
if (image.Width != 1 || image.Height != 1 || image.Pixels.Length != 4)
{
    throw new InvalidOperationException("Linux PNG decoder returned an invalid image.");
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

Console.WriteLine(
    $"Linux managed media smoke passed: PNG=1x1, cameraDevices={devices.Count}, AAC bridge=registered.");
return 0;
