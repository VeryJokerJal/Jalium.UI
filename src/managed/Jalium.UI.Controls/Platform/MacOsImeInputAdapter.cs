using System;
using System.Runtime.InteropServices;
using Jalium.UI.Input;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Platform
{
    internal sealed class MacOsImeInputAdapter : IDisposable
    {
        private readonly Window _owner;
        private readonly InsertTextDelegate _insertTextDelegate;
        private readonly SetMarkedTextDelegate _setMarkedTextDelegate;
        private readonly UnmarkTextDelegate _unmarkTextDelegate;
        private readonly CommandDelegate _commandDelegate;
        private GCHandle _selfHandle;
        private nint _bridgeHandle;
        private bool _attached;
        private bool _isComposing;

        public MacOsImeInputAdapter(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _insertTextDelegate = OnInsertText;
            _setMarkedTextDelegate = OnSetMarkedText;
            _unmarkTextDelegate = OnUnmarkText;
            _commandDelegate = OnCommand;
        }

        public bool Attach(nint windowHandle)
        {
            if (windowHandle == nint.Zero)
                return false;

            if (_attached)
                return true;

            _selfHandle = GCHandle.Alloc(this);

            try
            {
                nint managedContext = GCHandle.ToIntPtr(_selfHandle);
                _bridgeHandle = NativeMethods.MacOsImeCreate(
                    windowHandle,
                    managedContext,
                    Marshal.GetFunctionPointerForDelegate(_insertTextDelegate),
                    Marshal.GetFunctionPointerForDelegate(_setMarkedTextDelegate),
                    Marshal.GetFunctionPointerForDelegate(_unmarkTextDelegate),
                    Marshal.GetFunctionPointerForDelegate(_commandDelegate));

                if (_bridgeHandle == nint.Zero)
                {
                    Dispose();
                    return false;
                }

                _attached = true;
                return true;
            }
            catch (DllNotFoundException)
            {
                Dispose();
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                Dispose();
                return false;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        public void NotifyCaretRectChanged(double x, double y, double width, double height, double scale)
        {
            if (_bridgeHandle == nint.Zero)
                return;

            try
            {
                ulong eventId = (ulong)Environment.TickCount;
                NativeMethods.MacOsImeUpdateCaretRect(_bridgeHandle, eventId, x, y, width, height);
            }
            catch
            {
                // Ignore failures on IME update.
            }
        }

        public void NotifyFocusEnter()
        {
            if (_bridgeHandle == nint.Zero)
                return;

            try
            {
                NativeMethods.MacOsImeFocus(_bridgeHandle, true);
            }
            catch
            {
            }
        }

        public void NotifyFocusLeave()
        {
            if (_bridgeHandle == nint.Zero)
                return;

            try
            {
                NativeMethods.MacOsImeFocus(_bridgeHandle, false);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_bridgeHandle != nint.Zero)
            {
                try
                {
                    NativeMethods.MacOsImeDestroy(_bridgeHandle);
                }
                catch
                {
                }

                _bridgeHandle = nint.Zero;
            }

            if (_selfHandle.IsAllocated)
            {
                _selfHandle.Free();
            }

            _attached = false;
        }

        private static void OnInsertText(nint context, nint utf8Text)
        {
            if (GCHandle.FromIntPtr(context).Target is not MacOsImeInputAdapter adapter)
                return;

            string? text = Marshal.PtrToStringUTF8(utf8Text);
            if (string.IsNullOrEmpty(text))
                return;

            adapter._owner.MacOsImeCommitText(text);
        }

        private static void OnSetMarkedText(
            nint context,
            nint utf8Text,
            int selectedStart,
            int selectedLength,
            int replacementStart,
            int replacementLength)
        {
            if (GCHandle.FromIntPtr(context).Target is not MacOsImeInputAdapter adapter)
                return;

            string text = Marshal.PtrToStringUTF8(utf8Text) ?? string.Empty;
            if (!adapter._isComposing)
            {
                adapter._owner.MacOsImeStartComposition();
                adapter._isComposing = true;
            }

            int cursor = selectedStart >= 0
                ? Math.Clamp(selectedStart + Math.Max(0, selectedLength), 0, text.Length)
                : text.Length;

            InputMethod.UpdateComposition(text, cursor);
        }

        private static void OnUnmarkText(nint context)
        {
            if (GCHandle.FromIntPtr(context).Target is not MacOsImeInputAdapter adapter)
                return;

            if (!adapter._isComposing)
                return;

            adapter._isComposing = false;
            adapter._owner.MacOsImeEndComposition();
        }

        private static void OnCommand(nint context, nint utf8Command)
        {
            if (GCHandle.FromIntPtr(context).Target is not MacOsImeInputAdapter adapter)
                return;

            string? command = Marshal.PtrToStringUTF8(utf8Command);
            if (string.IsNullOrEmpty(command))
                return;

            if (string.Equals(command, "cancelOperation:", StringComparison.Ordinal))
            {
                if (adapter._isComposing)
                {
                    adapter._isComposing = false;
                    adapter._owner.MacOsImeCancelComposition();
                }
            }
            else if (string.Equals(command, "insertNewline:", StringComparison.Ordinal))
            {
                if (adapter._isComposing)
                {
                    adapter._isComposing = false;
                    adapter._owner.MacOsImeEndComposition();
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InsertTextDelegate(nint context, nint utf8Text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetMarkedTextDelegate(
            nint context,
            nint utf8Text,
            int selectedStart,
            int selectedLength,
            int replacementStart,
            int replacementLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UnmarkTextDelegate(nint context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CommandDelegate(nint context, nint utf8Command);
    }
}
