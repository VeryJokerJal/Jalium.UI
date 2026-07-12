namespace Jalium.UI.Interop
{
    /// <summary>Provides Win32 handle and ownership access for a Jalium window.</summary>
    public sealed class WindowInteropHelper
    {
        private readonly Jalium.UI.Controls.Window _window;
        private IntPtr _owner;

        /// <summary>Initializes a helper for <paramref name="window"/>.</summary>
        public WindowInteropHelper(Jalium.UI.Controls.Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            _window = window;
        }

        /// <summary>Gets the native handle, or zero before the window is created.</summary>
        public IntPtr Handle => _window.Handle;

        /// <summary>Gets or sets the native owner window.</summary>
        public IntPtr Owner
        {
            get => _owner;
            set
            {
                if (value == Handle && value != IntPtr.Zero)
                {
                    throw new ArgumentException("A window cannot own itself.", nameof(value));
                }

                _owner = value;
            }
        }

        /// <summary>Ensures the window's native handle has been created.</summary>
        public IntPtr EnsureHandle() => _window.Handle;
    }
}

namespace Jalium.UI.Controls
{
    /// <summary>
    /// Compatibility facade for the former Jalium namespace. New code should use
    /// <see cref="Jalium.UI.Interop.WindowInteropHelper"/>.
    /// </summary>
    [Obsolete("Use Jalium.UI.Interop.WindowInteropHelper.")]
    public sealed class WindowInteropHelper
    {
        private readonly Jalium.UI.Interop.WindowInteropHelper _inner;

        public WindowInteropHelper(Window window) =>
            _inner = new Jalium.UI.Interop.WindowInteropHelper(window);

        public IntPtr Handle => _inner.Handle;

        public IntPtr Owner
        {
            get => _inner.Owner;
            set => _inner.Owner = value;
        }

        public IntPtr EnsureHandle() => _inner.EnsureHandle();
    }
}
