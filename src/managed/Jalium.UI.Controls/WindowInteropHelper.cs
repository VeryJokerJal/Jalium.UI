namespace Jalium.UI.Interop
{
    /// <summary>Provides Win32 handle and ownership access for a Jalium window.</summary>
    public sealed class WindowInteropHelper
    {
        private readonly Jalium.UI.Window _window;
        private IntPtr _owner;

        /// <summary>Initializes a helper for <paramref name="window"/>.</summary>
        public WindowInteropHelper(Jalium.UI.Window window)
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
