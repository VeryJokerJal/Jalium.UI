using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class WindowTaskbarRelaunchTests
{
    [Fact]
    public void BuildTaskbarRelaunchInfo_ShouldReuseExecutable_WhenRunningViaAppHost()
    {
        var info = Window.BuildTaskbarRelaunchInfo(
            @"C:\Program Files\Jalium\Jalium.exe",
            [
                @"C:\Program Files\Jalium\Jalium.exe",
                "--profile",
                "Daily Build"
            ],
            "Ignored Window Title");

        Assert.Equal("App.Jalium", info.AppUserModelId);
        Assert.Equal("\"C:\\Program Files\\Jalium\\Jalium.exe\" --profile \"Daily Build\"", info.Command);
        Assert.Equal("Jalium", info.DisplayName);
        Assert.Equal(@"C:\Program Files\Jalium\Jalium.exe,0", info.IconResource);
    }

    [Fact]
    public void BuildTaskbarRelaunchInfo_ShouldIncludeManagedEntryPoint_WhenRunningViaDotnetHost()
    {
        var info = Window.BuildTaskbarRelaunchInfo(
            @"C:\Program Files\dotnet\dotnet.exe",
            [
                @"D:\Apps\Demo App\Demo.dll",
                "--workspace",
                @"C:\Temp Folder\Session\"
            ],
            "Demo Window");

        Assert.Equal("App.Demo", info.AppUserModelId);
        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"D:\\Apps\\Demo App\\Demo.dll\" --workspace \"C:\\Temp Folder\\Session\\\\\"",
            info.Command);
        Assert.Equal("Demo", info.DisplayName);
        Assert.Null(info.IconResource);
    }

    [Fact]
    public void QuoteCommandLineArgument_ShouldEscapeTrailingBackslashesInsideQuotedValues()
    {
        var quoted = Window.QuoteCommandLineArgument(@"C:\Temp Folder\Session\");

        Assert.Equal("\"C:\\Temp Folder\\Session\\\\\"", quoted);
    }

    [RequiresWindowsFact]
    [SupportedOSPlatform("windows")]
    public void Show_ShouldPublishCompleteTaskbarRelaunchPropertiesToWindowPropertyStore()
    {
        RunOnSta(() =>
        {
            const string title = "Jalium Taskbar Relaunch Property Test";
            const string executable = @"C:\Jalium Test\Taskbar Host.exe";
            var expected = Window.BuildTaskbarRelaunchInfo(
                executable,
                [executable, "--profile", "Taskbar Test"],
                title);

            Assert.False(string.IsNullOrWhiteSpace(expected.AppUserModelId));
            Assert.False(string.IsNullOrWhiteSpace(expected.Command));
            Assert.False(string.IsNullOrWhiteSpace(expected.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(expected.IconResource));

            var window = new Window
            {
                Title = title,
            };

            try
            {
                window.Show();
                Assert.NotEqual(nint.Zero, window.Handle);
                window.ApplyTaskbarRelaunchProperties(expected);

                var actual = ReadTaskbarRelaunchProperties(window.Handle);
                Assert.Equal(expected.Command, actual.Command);
                Assert.Equal(expected.DisplayName, actual.DisplayName);
                Assert.Equal(expected.AppUserModelId, actual.AppUserModelId);
                Assert.Equal(expected.IconResource, actual.IconResource);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [RequiresWindowsFact]
    [SupportedOSPlatform("windows")]
    public void LongRelaunchCommand_ShouldStillPublishWindowGroupingIdentity()
    {
        RunOnSta(() =>
        {
            const string executable = @"C:\Jalium Test\Long Command Host.exe";
            var expected = Window.BuildTaskbarRelaunchInfo(
                executable, [executable, new string('x', 4096)], "Long command property test");
            var window = new Window { Title = "Jalium Long Taskbar Command Test" };
            try
            {
                window.Show();
                window.ApplyTaskbarRelaunchProperties(expected);

                // A rejected relaunch string must not skip the final AppID write.
                // The original Show used the test host's different grouping ID.
                var actual = ReadTaskbarRelaunchProperties(window.Handle);
                Assert.Equal(expected.AppUserModelId, actual.AppUserModelId);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("windows")]
    public void WindowDestruction_ClearsPublishedPropertiesBeforeTheHwndDisappears(bool nativeDestroy)
    {
        if (!OperatingSystem.IsWindows()) return;

        RunOnSta(() =>
        {
            const string executable = @"C:\Jalium\Taskbar.exe";
            var expected = Window.BuildTaskbarRelaunchInfo(executable, [executable], "Cleanup");
            var window = new Window { Title = "Taskbar property lifetime", Width = 320, Height = 200 };
            try
            {
                window.Show();
                window.ApplyTaskbarRelaunchProperties(expected);
                Assert.Equal(expected.AppUserModelId, ReadTaskbarRelaunchProperties(window.Handle).AppUserModelId);

                using var observer = new NativeDestroyObserver(window.Handle);
                if (nativeDestroy)
                    Assert.True(DestroyWindow(window.Handle));
                else
                    window.Close();

                observer.Failure?.Throw();
                Assert.True(observer.CapturedWhileWindowValid);
                Assert.Equal(new TaskbarRelaunchProperties(null, null, null, null), observer.Properties);
                Assert.Equal(nint.Zero, window.Handle);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [RequiresWindowsFact]
    [SupportedOSPlatform("windows")]
    public void CanceledClose_PreservesTaskbarProperties_AndExplicitCleanupIsIdempotent()
    {
        RunOnSta(() =>
        {
            const string executable = @"C:\Jalium\Taskbar.exe";
            var expected = Window.BuildTaskbarRelaunchInfo(executable, [executable], "Cancel cleanup");
            var window = new Window { Title = "Taskbar canceled close", Width = 320, Height = 200 };
            EventHandler<System.ComponentModel.CancelEventArgs> cancel = (_, e) => e.Cancel = true;
            try
            {
                window.Show();
                window.ApplyTaskbarRelaunchProperties(expected);
                var before = ReadTaskbarRelaunchProperties(window.Handle);
                window.Closing += cancel;
                window.Close();
                Assert.NotEqual(nint.Zero, window.Handle);
                Assert.Equal(before, ReadTaskbarRelaunchProperties(window.Handle));

                window.ReleaseTaskbarRelaunchProperties(window.Handle);
                window.ReleaseTaskbarRelaunchProperties(window.Handle);
                Assert.Equal(new TaskbarRelaunchProperties(null, null, null, null),
                    ReadTaskbarRelaunchProperties(window.Handle));

                // Cleanup must not poison a still-live window's later publication.
                window.ApplyTaskbarRelaunchProperties(expected);
                Assert.Equal(before, ReadTaskbarRelaunchProperties(window.Handle));
            }
            finally
            {
                window.Closing -= cancel;
                window.Close();
            }
        });
    }

    private sealed class NativeDestroyObserver : IDisposable
    {
        private readonly nint _window;
        private readonly nint _previous;
        private readonly WindowProcedure _callback;

        public NativeDestroyObserver(nint window)
        {
            _window = window;
            _callback = OnMessage;
            _previous = SetWindowProcedure(window, Marshal.GetFunctionPointerForDelegate(_callback));
            Assert.NotEqual(nint.Zero, _previous);
        }

        public bool CapturedWhileWindowValid { get; private set; }
        public TaskbarRelaunchProperties Properties { get; private set; }
        public ExceptionDispatchInfo? Failure { get; private set; }

        private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam)
        {
            nint result = CallWindowProcW(_previous, hwnd, message, wParam, lParam);
            if (message == 0x0002) // WM_DESTROY, before USER32 removes the HWND.
            {
                try
                {
                    CapturedWhileWindowValid = IsWindow(hwnd);
                    Properties = ReadTaskbarRelaunchProperties(hwnd);
                }
                catch (Exception exception)
                {
                    Failure = ExceptionDispatchInfo.Capture(exception);
                }
            }
            return result;
        }

        public void Dispose()
        {
            if (IsWindow(_window)) _ = SetWindowProcedure(_window, _previous);
            GC.KeepAlive(_callback);
        }

        private static nint SetWindowProcedure(nint window, nint procedure) =>
            nint.Size == 8 ? SetWindowLongPtrW(window, -4, procedure) : SetWindowLongW(window, -4, procedure);
    }

    private static TaskbarRelaunchProperties ReadTaskbarRelaunchProperties(nint hwnd)
    {
        var interfaceId = IPropertyStoreGuid;
        nint propertyStore = nint.Zero;
        int hr = SHGetPropertyStoreForWindow(hwnd, ref interfaceId, out propertyStore);
        try
        {
            Assert.True(
                hr >= 0,
                $"SHGetPropertyStoreForWindow failed with HRESULT 0x{hr:X8}.");
            Assert.NotEqual(nint.Zero, propertyStore);

            return new TaskbarRelaunchProperties(
                ReadPropertyStoreString(propertyStore, RelaunchCommandPropertyName),
                ReadPropertyStoreString(propertyStore, RelaunchDisplayNamePropertyName),
                ReadPropertyStoreString(propertyStore, AppUserModelIdPropertyName),
                ReadPropertyStoreString(propertyStore, RelaunchIconPropertyName));
        }
        finally
        {
            if (propertyStore != nint.Zero)
            {
                _ = Marshal.Release(propertyStore);
            }
        }
    }

    private static unsafe string? ReadPropertyStoreString(nint propertyStore, string canonicalName)
    {
        int hr = PSGetPropertyKeyFromName(canonicalName, out var propertyKey);
        Assert.True(
            hr >= 0,
            $"PSGetPropertyKeyFromName('{canonicalName}') failed with HRESULT 0x{hr:X8}.");

        PROPVARIANT value = default;
        try
        {
            // IPropertyStore::GetValue is vtable slot 5 after IUnknown, GetCount, and GetAt.
            var vtable = *(nint**)propertyStore;
            var getValue =
                (delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)vtable[5];
            hr = getValue(propertyStore, &propertyKey, &value);

            Assert.True(
                hr >= 0,
                $"IPropertyStore::GetValue('{canonicalName}') failed with HRESULT 0x{hr:X8}.");

            if (value.vt == VT_EMPTY)
            {
                Assert.Equal(nint.Zero, value.pszVal);
                return null;
            }

            Assert.Equal(VT_LPWSTR, value.vt);
            Assert.NotEqual(nint.Zero, value.pszVal);

            var text = Marshal.PtrToStringUni(value.pszVal);
            Assert.NotNull(text);
            return text!;
        }
        finally
        {
            int clearHr = PropVariantClear(ref value);
            Assert.True(
                clearHr >= 0,
                $"PropVariantClear('{canonicalName}') failed with HRESULT 0x{clearHr:X8}.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RunOnSta(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                captured = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true,
            Name = nameof(WindowTaskbarRelaunchTests),
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(30)),
            "The STA window-property test thread did not exit within 30 seconds.");
        captured?.Throw();
    }

    private const ushort VT_EMPTY = 0;
    private const ushort VT_LPWSTR = 31;
    private const string AppUserModelIdPropertyName = "System.AppUserModel.ID";
    private const string RelaunchCommandPropertyName = "System.AppUserModel.RelaunchCommand";
    private const string RelaunchDisplayNamePropertyName = "System.AppUserModel.RelaunchDisplayNameResource";
    private const string RelaunchIconPropertyName = "System.AppUserModel.RelaunchIconResource";
    private static readonly Guid IPropertyStoreGuid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWindowLongW(nint hwnd, int index, nint value);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CallWindowProcW(nint procedure, nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid riid, out nint ppv);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PSGetPropertyKeyFromName(string pszCanonicalName, out PROPERTYKEY propkey);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(2)]
        public ushort wReserved1;

        [FieldOffset(4)]
        public ushort wReserved2;

        [FieldOffset(6)]
        public ushort wReserved3;

        [FieldOffset(8)]
        public nint pszVal;

        // The native union is two pointers wide: 16 bytes on x64 and 8 on x86.
        [FieldOffset(8)]
        private PropVariantUnion _union;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantUnion
    {
        public nint First;
        public nint Second;
    }

    private readonly record struct TaskbarRelaunchProperties(
        string? Command,
        string? DisplayName,
        string? AppUserModelId,
        string? IconResource);
}
