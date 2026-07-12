using System.Runtime.InteropServices;
using Jalium.UI.Input;

namespace Jalium.UI.Interop;

/// <summary>Provides keyboard navigation services for a Win32-hosted input sink.</summary>
public interface IKeyboardInputSink
{
    /// <summary>Gets or sets the site that owns this sink.</summary>
    IKeyboardInputSite? KeyboardInputSite { get; set; }

    /// <summary>Registers a child keyboard input sink.</summary>
    IKeyboardInputSite RegisterKeyboardInputSink(IKeyboardInputSink sink);

    /// <summary>Moves focus into this sink.</summary>
    bool TabInto(TraversalRequest request);

    /// <summary>Processes a keyboard accelerator.</summary>
    bool TranslateAccelerator(ref MSG msg, ModifierKeys modifiers);

    /// <summary>Processes a character message.</summary>
    bool TranslateChar(ref MSG msg, ModifierKeys modifiers);

    /// <summary>Processes a mnemonic.</summary>
    bool OnMnemonic(ref MSG msg, ModifierKeys modifiers);

    /// <summary>Gets whether keyboard focus is within this sink.</summary>
    bool HasFocusWithin();
}

/// <summary>Connects a child keyboard input sink to its owning sink.</summary>
public interface IKeyboardInputSite
{
    /// <summary>Gets the child sink registered at this site.</summary>
    IKeyboardInputSink Sink { get; }

    /// <summary>Continues navigation when the child has no additional tab stops.</summary>
    bool OnNoMoreTabStops(TraversalRequest request);

    /// <summary>Unregisters this site from its owner.</summary>
    void Unregister();
}

/// <summary>Exposes the native window handle owned by an interop object.</summary>
public interface IWin32Window
{
    /// <summary>Gets the native window handle.</summary>
    IntPtr Handle { get; }
}

/// <summary>Contains a Win32 message and its dispatch metadata.</summary>
[Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    private IntPtr _hwnd;
    private int _message;
    private IntPtr _wParam;
    private IntPtr _lParam;
    private int _time;
    private int _ptX;
    private int _ptY;

    internal MSG(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, int time, int ptX, int ptY)
    {
        _hwnd = hwnd;
        _message = message;
        _wParam = wParam;
        _lParam = lParam;
        _time = time;
        _ptX = ptX;
        _ptY = ptY;
    }

    /// <summary>Gets or sets the destination window.</summary>
    public IntPtr hwnd { readonly get => _hwnd; set => _hwnd = value; }

    /// <summary>Gets or sets the message identifier.</summary>
    public int message { readonly get => _message; set => _message = value; }

    /// <summary>Gets or sets the message's word parameter.</summary>
    public IntPtr wParam { readonly get => _wParam; set => _wParam = value; }

    /// <summary>Gets or sets the message's long parameter.</summary>
    public IntPtr lParam { readonly get => _lParam; set => _lParam = value; }

    /// <summary>Gets or sets the message timestamp.</summary>
    public int time { readonly get => _time; set => _time = value; }

    /// <summary>Gets or sets the message point's x-coordinate.</summary>
    public int pt_x { readonly get => _ptX; set => _ptX = value; }

    /// <summary>Gets or sets the message point's y-coordinate.</summary>
    public int pt_y { readonly get => _ptY; set => _ptY = value; }
}

/// <summary>Implements a registration between an owner and a child keyboard sink.</summary>
internal sealed class KeyboardInputSite : IKeyboardInputSite
{
    private readonly IKeyboardInputSink _owner;
    private readonly Action<KeyboardInputSite> _unregister;
    private bool _isRegistered = true;

    internal KeyboardInputSite(
        IKeyboardInputSink owner,
        IKeyboardInputSink sink,
        Action<KeyboardInputSite> unregister)
    {
        _owner = owner;
        Sink = sink;
        _unregister = unregister;
    }

    public IKeyboardInputSink Sink { get; }

    public bool OnNoMoreTabStops(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _isRegistered && _owner.TabInto(request);
    }

    public void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        _isRegistered = false;
        _unregister(this);
        if (ReferenceEquals(Sink.KeyboardInputSite, this))
        {
            Sink.KeyboardInputSite = null;
        }
    }
}
