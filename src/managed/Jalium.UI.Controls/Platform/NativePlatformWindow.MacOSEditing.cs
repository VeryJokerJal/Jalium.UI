using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform;

internal enum MacOSEditingCommand { Copy, Cut, Paste, SelectAll, Undo, Redo }

internal sealed partial class NativePlatformWindow
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EditingCommandQueryDelegate(int command, nint userData);

    private static readonly EditingCommandQueryDelegate EditingCommandQueryCallback = QueryEditingCommand;
    private Func<int, bool>? _editingCommandQuery;

    internal void SetMacOSEditingCommandQuery(Func<int, bool> query)
    {
        if (!OperatingSystem.IsMacOS() || _handle == 0 || _disposed) return;
        _editingCommandQuery = query;
        _ = AppleWindowSetEditingCommandQuery(_handle,
            Marshal.GetFunctionPointerForDelegate(EditingCommandQueryCallback), GCHandle.ToIntPtr(_selfHandle));
    }

    private static int QueryEditingCommand(int command, nint userData)
    {
        try
        {
            if (userData != 0 && GCHandle.FromIntPtr(userData).Target is NativePlatformWindow window &&
                !window._disposed && window._handle != 0 && window._editingCommandQuery is { } query)
                return query(command) ? 1 : 0;
        }
        catch
        {
            // User CanExecute handlers must not throw across an AppKit callback.
        }
        return 0;
    }

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_apple_window_set_editing_command_query")]
    private static partial int AppleWindowSetEditingCommandQuery(nint window, nint query, nint userData);
}
