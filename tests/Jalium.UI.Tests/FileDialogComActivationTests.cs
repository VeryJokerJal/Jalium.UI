using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for the NativeAOT-safe Windows Shell file-dialog interop
/// (<c>ShellComInterop</c>, consumed by <see cref="OpenFileDialog"/> / <see cref="SaveFileDialog"/>).
///
/// The file dialogs used to activate classic <c>[ComImport]</c> coclasses
/// (<c>new FileOpenDialogCom()</c>), which throws <see cref="NotSupportedException"/> under
/// NativeAOT ("Built-in COM has been disabled") — so every dialog button was silently inert once
/// the app was published with PublishAot. These tests drive the real production helpers through
/// <c>CoCreateInstance</c> + raw vtable dispatch WITHOUT ever calling Show(), so they run headless.
///
/// They execute under the JIT test host, so they cannot themselves prove the AOT compiler no longer
/// strips the path; what they DO prove is that the coclass-activation throw is gone, that the vtable
/// slot wiring / refcounting is self-consistent, and that a real modal call surface
/// (SetFileTypes / SetFileTypeIndex / GetFileTypeIndex, SHCreateItemFromParsingName + GetDisplayName)
/// round-trips. End-to-end AOT behaviour is smoke-tested manually against a PublishAot build.
/// </summary>
[Collection("Application")]
[SupportedOSPlatform("windows")]
public class FileDialogComActivationTests
{
    // Well-known Shell interface IIDs (ShObjIdl_core.h).
    private static readonly Guid IID_IFileDialog = new("42F85136-DB7E-439C-85F1-E4075D135FC8");
    private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    private static readonly Guid IID_IFileSaveDialog = new("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");

    [Fact]
    public void OpenDialog_Activates_AndExposesFileDialogInterfaces()
    {
        RunSta(() =>
        {
            // The whole point: this used to throw NotSupportedException under AOT. It must activate.
            nint dialog = ShellComInterop.CreateOpenDialog();
            Assert.NotEqual(0, dialog);
            try
            {
                // A freshly activated IFileOpenDialog must QI to its base IFileDialog and itself.
                AssertQuerySucceeds(dialog, IID_IFileDialog);
                AssertQuerySucceeds(dialog, IID_IFileOpenDialog);
            }
            finally
            {
                ShellComInterop.Release(dialog);
            }
        });
    }

    [Fact]
    public void SaveDialog_Activates_AndIsNotAnOpenDialog()
    {
        RunSta(() =>
        {
            nint dialog = ShellComInterop.CreateSaveDialog();
            Assert.NotEqual(0, dialog);
            try
            {
                AssertQuerySucceeds(dialog, IID_IFileDialog);
                AssertQuerySucceeds(dialog, IID_IFileSaveDialog);

                // Guards the slot-27 divergence: IFileSaveDialog is NOT an IFileOpenDialog, so the
                // save path must never call GetResults (slot 27 = SetSaveAsItem on a save dialog).
                Guid openIid = IID_IFileOpenDialog;
                int hr = Marshal.QueryInterface(dialog, in openIid, out nint unexpected);
                if (hr >= 0)
                {
                    Marshal.Release(unexpected);
                }
                Assert.True(hr < 0, $"IFileSaveDialog unexpectedly QI'd to IFileOpenDialog (hr=0x{hr:X8})");
            }
            finally
            {
                ShellComInterop.Release(dialog);
            }
        });
    }

    [Fact]
    public void SetFileTypes_RoundTripsFilterIndex_WithoutShowing()
    {
        RunSta(() =>
        {
            nint dialog = ShellComInterop.CreateOpenDialog();
            Assert.NotEqual(0, dialog);

            var filters = new (string Name, string Pattern)[]
            {
                ("Text files (*.txt)", "*.txt"),
                ("All files (*.*)", "*.*"),
            };

            nint block = ShellComInterop.AllocFilterSpecs(filters, out int count);
            try
            {
                Assert.Equal(2, count);
                Assert.NotEqual(0, block);

                // Exercises the blittable COMDLG_FILTERSPEC array through a real vtable call.
                ShellComInterop.SetFileTypes(dialog, (uint)count, block);
                ShellComInterop.SetFileTypeIndex(dialog, 2);
                Assert.Equal(2u, ShellComInterop.GetFileTypeIndex(dialog));
            }
            finally
            {
                ShellComInterop.FreeFilterSpecs(block, count);
                ShellComInterop.Release(dialog);
            }
        });
    }

    [Fact]
    public void CreateShellItem_ThenGetItemPath_ReturnsRealFileSystemPath()
    {
        RunSta(() =>
        {
            // Proves the rewritten SHCreateItemFromParsingName (PreserveSig + out nint, no
            // UnmanagedType.Interface) plus IShellItem::GetDisplayName produce a usable path.
            string seed = Environment.SystemDirectory;
            nint item = ShellComInterop.CreateShellItem(seed);
            Assert.NotEqual(0, item);
            try
            {
                string path = ShellComInterop.GetItemPath(item);
                Assert.False(string.IsNullOrWhiteSpace(path));
                Assert.True(Directory.Exists(path), $"GetItemPath returned a non-existent path: '{path}'");
            }
            finally
            {
                ShellComInterop.Release(item);
            }
        });
    }

    #region Helpers

    private static void AssertQuerySucceeds(nint pUnk, Guid iid)
    {
        int hr = Marshal.QueryInterface(pUnk, in iid, out nint p);
        Assert.True(hr >= 0, $"QueryInterface({iid}) failed: 0x{hr:X8}");
        Assert.NotEqual(0, p);
        Marshal.Release(p);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a dedicated STA thread with COM initialized, so
    /// CoCreateInstance of the shell dialog coclasses works and any assertion failure is
    /// re-thrown on the calling thread with its original stack.
    /// </summary>
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                if (hr >= 0)
                {
                    CoUninitialize();
                }
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private const uint COINIT_APARTMENTTHREADED = 0x2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    #endregion
}
