using System.Runtime.InteropServices;

namespace Jalium.UI.Input;

/// <summary>
/// Represents a mouse cursor type.
/// </summary>
public enum CursorType
{
    None = 0,
    No = 1,
    Arrow = 2,
    AppStarting = 3,
    Cross = 4,
    Help = 5,
    IBeam = 6,
    SizeAll = 7,
    SizeNESW = 8,
    SizeNS = 9,
    SizeNWSE = 10,
    SizeWE = 11,
    UpArrow = 12,
    Wait = 13,
    Hand = 14,
    Pen = 15,
    ScrollNS = 16,
    ScrollWE = 17,
    ScrollAll = 18,
    ScrollN = 19,
    ScrollS = 20,
    ScrollW = 21,
    ScrollE = 22,
    ScrollNW = 23,
    ScrollNE = 24,
    ScrollSW = 25,
    ScrollSE = 26,
    ArrowCD = 27,
}

/// <summary>
/// Represents a cursor that can be displayed for a UI element.
/// </summary>
public sealed class Cursor : IDisposable
{
    private readonly CursorType _cursorType;
    private readonly byte[]? _cursorData;
    private readonly SafeHandle? _nativeHandle;
    private bool _nativeHandleReferenceAdded;
    private bool _disposed;

    /// <summary>
    /// Gets the cursor type.
    /// </summary>
    public CursorType CursorType => _cursorType;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cursor"/> class.
    /// </summary>
    /// <param name="cursorType">The cursor type.</param>
    public Cursor(CursorType cursorType)
    {
        if (!Enum.IsDefined(cursorType))
            throw new ArgumentException("Unknown cursor type.", nameof(cursorType));
        _cursorType = cursorType;
    }

    public Cursor(string cursorFile)
        : this(cursorFile, scaleWithDpi: false)
    {
    }

    public Cursor(string cursorFile, bool scaleWithDpi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursorFile);
        Source = cursorFile;
        ScaleWithDpi = scaleWithDpi;
        _cursorData = File.ReadAllBytes(cursorFile);
        if (_cursorData.Length == 0)
            throw new ArgumentException("The cursor file is empty.", nameof(cursorFile));
        _cursorType = CursorType.None;
    }

    public Cursor(Stream cursorStream)
        : this(cursorStream, scaleWithDpi: false)
    {
    }

    public Cursor(Stream cursorStream, bool scaleWithDpi)
    {
        ArgumentNullException.ThrowIfNull(cursorStream);
        if (!cursorStream.CanRead)
            throw new ArgumentException("The cursor stream must be readable.", nameof(cursorStream));
        using var copy = new MemoryStream();
        cursorStream.CopyTo(copy);
        _cursorData = copy.ToArray();
        if (_cursorData.Length == 0)
            throw new ArgumentException("The cursor stream is empty.", nameof(cursorStream));
        ScaleWithDpi = scaleWithDpi;
        _cursorType = CursorType.None;
    }

    /// <summary>
    /// Creates a cursor that keeps an existing native cursor handle alive without
    /// taking ownership of the caller's <see cref="SafeHandle"/> instance.
    /// </summary>
    internal Cursor(SafeHandle cursorHandle)
    {
        ArgumentNullException.ThrowIfNull(cursorHandle);

        bool referenceAdded = false;
        try
        {
            cursorHandle.DangerousAddRef(ref referenceAdded);
            if (cursorHandle.IsInvalid || cursorHandle.IsClosed)
            {
                throw new ArgumentException("The cursor handle is invalid or closed.", nameof(cursorHandle));
            }

            _nativeHandle = cursorHandle;
            _nativeHandleReferenceAdded = referenceAdded;
            _cursorType = CursorType.None;
        }
        catch
        {
            if (referenceAdded)
            {
                cursorHandle.DangerousRelease();
            }

            throw;
        }
    }

    internal ReadOnlyMemory<byte> CursorData => _cursorData ?? Array.Empty<byte>();
    internal string? Source { get; }
    internal bool ScaleWithDpi { get; }
    internal bool IsDisposed => _disposed;
    internal IntPtr NativeHandle =>
        _nativeHandle is { IsClosed: false, IsInvalid: false }
            ? _nativeHandle.DangerousGetHandle()
            : IntPtr.Zero;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_nativeHandleReferenceAdded)
        {
            _nativeHandleReferenceAdded = false;
            _nativeHandle!.DangerousRelease();
        }

        GC.SuppressFinalize(this);
    }

    ~Cursor() => Dispose();

    /// <inheritdoc />
    public override string ToString() => Source ?? _cursorType.ToString();

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is Cursor other && _cursorType == other._cursorType;

    /// <inheritdoc />
    public override int GetHashCode() => _cursorType.GetHashCode();

    /// <summary>
    /// Compares two Cursor instances for equality.
    /// </summary>
    public static bool operator ==(Cursor? left, Cursor? right)
    {
        if (left is null) return right is null;
        if (right is null) return false;
        return left._cursorType == right._cursorType;
    }

    /// <summary>
    /// Compares two Cursor instances for inequality.
    /// </summary>
    public static bool operator !=(Cursor? left, Cursor? right) => !(left == right);
}
