namespace Jalium.UI;

/// <summary>
/// Exception raised when a render pipeline operation fails.
/// </summary>
public sealed class RenderPipelineException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderPipelineException"/> class.
    /// </summary>
    public RenderPipelineException(
        string stage,
        JaliumResult result,
        int resultCode,
        nint hwnd,
        int width,
        int height,
        float dpiX,
        float dpiY,
        string backend,
        string? details = null,
        Exception? innerException = null)
        : base(BuildMessage(stage, result, resultCode, hwnd, width, height, dpiX, dpiY, backend, details), innerException)
    {
        Stage = stage ?? "Unknown";
        Result = result;
        ResultCode = resultCode;
        Hwnd = hwnd;
        Width = width;
        Height = height;
        DpiX = dpiX;
        DpiY = dpiY;
        Backend = backend ?? "Unknown";
        Details = details;
    }

    /// <summary>
    /// Gets the failed pipeline stage (for example: Create, Resize, Begin, End).
    /// </summary>
    public string Stage { get; }

    /// <summary>
    /// Gets the mapped Jalium result.
    /// </summary>
    public JaliumResult Result { get; }

    /// <summary>
    /// Gets the raw native result code.
    /// </summary>
    public int ResultCode { get; }

    /// <summary>
    /// Gets the window handle associated with the failing render target.
    /// </summary>
    public nint Hwnd { get; }

    /// <summary>
    /// Gets the render target width in physical pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the render target height in physical pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the horizontal DPI.
    /// </summary>
    public float DpiX { get; }

    /// <summary>
    /// Gets the vertical DPI.
    /// </summary>
    public float DpiY { get; }

    /// <summary>
    /// Gets the rendering backend name.
    /// </summary>
    public string Backend { get; }

    /// <summary>
    /// Gets optional additional details.
    /// </summary>
    public string? Details { get; }

    private static string BuildMessage(
        string stage,
        JaliumResult result,
        int resultCode,
        nint hwnd,
        int width,
        int height,
        float dpiX,
        float dpiY,
        string backend,
        string? details)
    {
        string baseMessage =
            $"Render pipeline failure: stage={stage}, result={result}({resultCode}), hwnd=0x{hwnd.ToInt64():X}, " +
            $"size={width}x{height}, dpi={dpiX:F2}x{dpiY:F2}, backend={backend}.";

        if (string.IsNullOrWhiteSpace(details))
        {
            return baseMessage;
        }

        return $"{baseMessage} details={details}";
    }
}
