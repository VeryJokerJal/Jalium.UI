using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Automation.MacOS;

internal enum MacOSAXOperation { Info, Child, String, Focus, HitTest, Action, SetValue, TextSelection, SetTextSelection, TextBounds, Attached, WindowButton, TextNavigation, TextStyles, TextStyleRange, BeginChildren, ReadChildren, ReleaseChildren }
internal enum MacOSAXTextNavigation { LineForIndex, RangeForIndex, RangeForLine, RangeForPosition, VisibleTextRange, InsertionLine, SetInsertionLine, ReplaceSelection }
internal enum MacOSAXWindowButton { Default, Cancel, Close, Minimize, Zoom }
internal enum MacOSAXString { Name, Help, Identifier, Value, Placeholder }
internal enum MacOSAXAction { Press, SetFocus, Increment, Decrement, Expand, Collapse, Deselect }
internal enum MacOSAXNotification { Focus, Value, Selection, Layout, Title }
[Flags]
internal enum MacOSAXFlags : uint
{
    Enabled = 1, Focusable = 2, Focused = 4, Password = 8, Pressable = 16,
    Writable = 32, Range = 64, Toggle = 128, Selected = 256,
    Expandable = 512, Expanded = 1024, Text = 2048, Multiline = 4096,
    Dialog = 8192, Modal = 16384, Selectable = 32768, Selection = 65536,
    Value = 131072, CloseButton = 1 << 18, MinimizeButton = 1 << 19, ZoomButton = 1 << 20,
    NavigableText = 1 << 21, EditableText = 1 << 22, Offscreen = 1 << 23, StyledText = 1 << 24
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MacOSAXRequest
{
    internal ulong NodeId;
    internal MacOSAXOperation Operation;
    internal int Index;
    internal ulong ResultId, ParentId;
    internal int Role;
    internal MacOSAXFlags Flags;
    internal int ChildCount, TextStart, TextLength;
    internal double X, Y, Width, Height, Value, Minimum, Maximum, Step;
    internal char* Text;
    internal int TextCapacity, TextCount;
}

internal static partial class MacOSAccessibilityNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int Callback(MacOSAXRequest* request, nint context);

    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_apple_window_set_accessibility")]
    internal static partial void SetCallback(nint platformWindow, nint callback, nint context);
    [LibraryImport(JaliumNativeLibraryNames.Platform, EntryPoint = "jalium_apple_window_notify_accessibility")]
    internal static partial void Notify(nint platformWindow, ulong nodeId, MacOSAXNotification notification);
}
