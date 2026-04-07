using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.Notifications;

/// <summary>
/// Windows notification backend using raw COM vtable calls to the WinRT
/// <c>Windows.UI.Notifications</c> APIs. No UWP/WinAppSDK/Toolkit dependency.
/// </summary>
internal sealed unsafe class WindowsNotificationBackend : INotificationBackend
{
    private string _appId = string.Empty;
    private nint _notifierPtr;
    private bool _disposed;
    private readonly Dictionary<uint, NotificationHandle> _activeNotifications = new();
    private uint _nextId;

    public bool IsSupported => OperatingSystem.IsWindows() && Environment.OSVersion.Version >= new Version(10, 0, 10240);

    #region INotificationBackend

    public void Initialize(string appId, string appName)
    {
        _appId = appId;

        // Ensure WinRT is initialized on this thread.
        WinRT.EnsureInitialized();

        // Create the AUMID shortcut for non-packaged apps if needed.
        EnsureShortcut(appId, appName);

        // Obtain IToastNotificationManagerStatics via RoGetActivationFactory.
        var managerIid = WinRT.IID_IToastNotificationManagerStatics;
        nint hClassName = WinRT.CreateHString("Windows.UI.Notifications.ToastNotificationManager");
        try
        {
            int hr = WinRT.RoGetActivationFactory(hClassName, ref managerIid, out nint managerPtr);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                // Call CreateToastNotifierWithId(appId)
                nint hAppId = WinRT.CreateHString(_appId);
                try
                {
                    hr = WinRT.IToastNotificationManagerStatics_CreateToastNotifierWithId(
                        managerPtr, hAppId, out _notifierPtr);
                    Marshal.ThrowExceptionForHR(hr);
                }
                finally
                {
                    WinRT.WindowsDeleteString(hAppId);
                }
            }
            finally
            {
                Marshal.Release(managerPtr);
            }
        }
        finally
        {
            WinRT.WindowsDeleteString(hClassName);
        }
    }

    public NotificationHandle Show(NotificationContent content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifierPtr == 0)
            throw new InvalidOperationException("NotificationBackend not initialized. Call Initialize first.");

        // Build toast XML
        string xml = BuildToastXml(content);

        // Create XmlDocument and load the XML
        nint xmlDocPtr = CreateXmlDocument(xml);

        try
        {
            // Create ToastNotification from XmlDocument
            nint toastPtr = CreateToastNotification(xmlDocPtr);

            try
            {
                uint id = ++_nextId;
                var handle = new NotificationHandle
                {
                    NativeHandle = toastPtr,
                    Tag = content.Tag,
                    Group = content.Group,
                    PlatformId = id
                };
                _activeNotifications[id] = handle;

                // Set Tag/Group on the toast if provided
                if (!string.IsNullOrEmpty(content.Tag))
                    SetToastTag(toastPtr, content.Tag);
                if (!string.IsNullOrEmpty(content.Group))
                    SetToastGroup(toastPtr, content.Group);

                // Show the toast via IToastNotifier::Show (vtable slot 6)
                int hr = WinRT.IToastNotifier_Show(_notifierPtr, toastPtr);
                Marshal.ThrowExceptionForHR(hr);

                return handle;
            }
            catch
            {
                Marshal.Release(toastPtr);
                throw;
            }
        }
        finally
        {
            Marshal.Release(xmlDocPtr);
        }
    }

    public void Hide(NotificationHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notifierPtr == 0 || handle.NativeHandle == 0) return;

        // IToastNotifier::Hide (vtable slot 7)
        WinRT.IToastNotifier_Hide(_notifierPtr, handle.NativeHandle);
        _activeNotifications.Remove(handle.PlatformId);
    }

    public void ClearAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nint hClassName = WinRT.CreateHString("Windows.UI.Notifications.ToastNotificationManager");
        try
        {
            var historyIid = WinRT.IID_IToastNotificationManagerStatics2;
            int hr = WinRT.RoGetActivationFactory(hClassName, ref historyIid, out nint manager2Ptr);
            if (hr < 0) return;

            try
            {
                hr = WinRT.IToastNotificationManagerStatics2_GetHistory(manager2Ptr, out nint historyPtr);
                if (hr >= 0 && historyPtr != 0)
                {
                    nint hAppId = WinRT.CreateHString(_appId);
                    try
                    {
                        WinRT.IToastNotificationHistory_Clear(historyPtr, hAppId);
                    }
                    finally
                    {
                        WinRT.WindowsDeleteString(hAppId);
                        Marshal.Release(historyPtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(manager2Ptr);
            }
        }
        finally
        {
            WinRT.WindowsDeleteString(hClassName);
        }

        _activeNotifications.Clear();
    }

    public void Remove(string tag, string? group = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        nint hClassName = WinRT.CreateHString("Windows.UI.Notifications.ToastNotificationManager");
        try
        {
            var historyIid = WinRT.IID_IToastNotificationManagerStatics2;
            int hr = WinRT.RoGetActivationFactory(hClassName, ref historyIid, out nint manager2Ptr);
            if (hr < 0) return;

            try
            {
                hr = WinRT.IToastNotificationManagerStatics2_GetHistory(manager2Ptr, out nint historyPtr);
                if (hr >= 0 && historyPtr != 0)
                {
                    try
                    {
                        nint hTag = WinRT.CreateHString(tag);
                        nint hGroup = group != null ? WinRT.CreateHString(group) : 0;
                        nint hAppId = WinRT.CreateHString(_appId);

                        try
                        {
                            if (hGroup != 0)
                                WinRT.IToastNotificationHistory_RemoveGroupedTagWithId(historyPtr, hTag, hGroup, hAppId);
                            else
                                WinRT.IToastNotificationHistory_RemoveTagWithId(historyPtr, hTag, hAppId);
                        }
                        finally
                        {
                            WinRT.WindowsDeleteString(hTag);
                            if (hGroup != 0) WinRT.WindowsDeleteString(hGroup);
                            WinRT.WindowsDeleteString(hAppId);
                        }
                    }
                    finally
                    {
                        Marshal.Release(historyPtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(manager2Ptr);
            }
        }
        finally
        {
            WinRT.WindowsDeleteString(hClassName);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kv in _activeNotifications)
        {
            if (kv.Value.NativeHandle != 0)
                Marshal.Release(kv.Value.NativeHandle);
        }
        _activeNotifications.Clear();

        if (_notifierPtr != 0)
        {
            Marshal.Release(_notifierPtr);
            _notifierPtr = 0;
        }
    }

    #endregion

    #region Toast XML Builder

    private static string BuildToastXml(NotificationContent content)
    {
        var sb = new StringBuilder(512);
        sb.Append("<toast");
        if (content.Arguments.Count > 0)
        {
            sb.Append(" launch=\"");
            bool first = true;
            foreach (var kv in content.Arguments)
            {
                if (!first) sb.Append('&');
                sb.Append(Escape(kv.Key)).Append('=').Append(Escape(kv.Value));
                first = false;
            }
            sb.Append('"');
        }
        sb.Append('>');

        // Visual
        sb.Append("<visual><binding template=\"ToastGeneric\">");
        sb.Append("<text>").Append(Escape(content.Title)).Append("</text>");
        if (!string.IsNullOrEmpty(content.Body))
            sb.Append("<text>").Append(Escape(content.Body)).Append("</text>");
        var iconPath = NotificationImageHelper.ResolveToPath(content.Icon);
        if (!string.IsNullOrEmpty(iconPath))
            sb.Append("<image placement=\"appLogoOverride\" src=\"").Append(Escape(iconPath)).Append("\"/>");
        var imagePath = NotificationImageHelper.ResolveToPath(content.Image);
        if (!string.IsNullOrEmpty(imagePath))
            sb.Append("<image placement=\"hero\" src=\"").Append(Escape(imagePath)).Append("\"/>");
        sb.Append("</binding></visual>");

        // Actions
        if (content.Actions.Count > 0)
        {
            sb.Append("<actions>");
            foreach (var action in content.Actions)
            {
                sb.Append("<action content=\"").Append(Escape(action.Label)).Append('"');
                sb.Append(" arguments=\"actionId=").Append(Escape(action.Id));
                if (action.Arguments != null)
                {
                    foreach (var kv in action.Arguments)
                        sb.Append('&').Append(Escape(kv.Key)).Append('=').Append(Escape(kv.Value));
                }
                sb.Append("\"/>");
            }
            sb.Append("</actions>");
        }

        // Audio
        if (content.Silent)
            sb.Append("<audio silent=\"true\"/>");

        sb.Append("</toast>");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    #endregion

    #region COM Factory Helpers

    private static nint CreateXmlDocument(string xml)
    {
        // Activate Windows.Data.Xml.Dom.XmlDocument
        nint hClassName = WinRT.CreateHString("Windows.Data.Xml.Dom.XmlDocument");
        int hr;
        nint xmlDocInspectable;
        try
        {
            hr = WinRT.RoActivateInstance(hClassName, out xmlDocInspectable);
        }
        finally
        {
            WinRT.WindowsDeleteString(hClassName);
        }
        Marshal.ThrowExceptionForHR(hr);

        // QI for IXmlDocumentIO to call LoadXml
        var xmlDocIoIid = WinRT.IID_IXmlDocumentIO;
        hr = Marshal.QueryInterface(xmlDocInspectable, ref xmlDocIoIid, out nint xmlDocIoPtr);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            nint hXml = WinRT.CreateHString(xml);
            try
            {
                hr = WinRT.IXmlDocumentIO_LoadXml(xmlDocIoPtr, hXml);
                Marshal.ThrowExceptionForHR(hr);
            }
            finally
            {
                WinRT.WindowsDeleteString(hXml);
            }
        }
        finally
        {
            Marshal.Release(xmlDocIoPtr);
        }

        return xmlDocInspectable;
    }

    private static nint CreateToastNotification(nint xmlDocPtr)
    {
        // Get IToastNotificationFactory via RoGetActivationFactory
        var factoryIid = WinRT.IID_IToastNotificationFactory;
        nint hClassName = WinRT.CreateHString("Windows.UI.Notifications.ToastNotification");
        int hr;
        nint factoryPtr;
        try
        {
            hr = WinRT.RoGetActivationFactory(hClassName, ref factoryIid, out factoryPtr);
        }
        finally
        {
            WinRT.WindowsDeleteString(hClassName);
        }
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            // IToastNotificationFactory::CreateToastNotification(XmlDocument) – vtable slot 6
            hr = WinRT.IToastNotificationFactory_CreateToastNotification(factoryPtr, xmlDocPtr, out nint toastPtr);
            Marshal.ThrowExceptionForHR(hr);
            return toastPtr;
        }
        finally
        {
            Marshal.Release(factoryPtr);
        }
    }

    private static void SetToastTag(nint toastPtr, string tag)
    {
        // QI for IToastNotification2 to set Tag
        var iid = WinRT.IID_IToastNotification2;
        int hr = Marshal.QueryInterface(toastPtr, ref iid, out nint toast2Ptr);
        if (hr < 0) return;

        try
        {
            nint hTag = WinRT.CreateHString(tag);
            try
            {
                WinRT.IToastNotification2_PutTag(toast2Ptr, hTag);
            }
            finally
            {
                WinRT.WindowsDeleteString(hTag);
            }
        }
        finally
        {
            Marshal.Release(toast2Ptr);
        }
    }

    private static void SetToastGroup(nint toastPtr, string group)
    {
        var iid = WinRT.IID_IToastNotification2;
        int hr = Marshal.QueryInterface(toastPtr, ref iid, out nint toast2Ptr);
        if (hr < 0) return;

        try
        {
            nint hGroup = WinRT.CreateHString(group);
            try
            {
                WinRT.IToastNotification2_PutGroup(toast2Ptr, hGroup);
            }
            finally
            {
                WinRT.WindowsDeleteString(hGroup);
            }
        }
        finally
        {
            Marshal.Release(toast2Ptr);
        }
    }

    #endregion

    #region Shortcut (non-packaged app AUMID registration)

    private static void EnsureShortcut(string appId, string appName)
    {
        // For non-packaged (Win32) apps, Windows requires a Start Menu shortcut
        // with System.AppUserModel.ID property set to receive toast notifications.
        try
        {
            string shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs",
                $"{appName}.lnk");

            if (File.Exists(shortcutPath))
                return;

            // Create shortcut via COM IShellLink
            var shellLinkClsid = new Guid("00021401-0000-0000-C000-000000000046");
            var shellLinkIid = new Guid("000214F9-0000-0000-C000-000000000046");

            int hr = Ole32.CoCreateInstance(ref shellLinkClsid, 0, 1 /* CLSCTX_INPROC_SERVER */,
                ref shellLinkIid, out nint shellLinkPtr);
            if (hr < 0) return;

            try
            {
                // IShellLinkW vtable: QI=0, AddRef=1, Release=2, GetPath=3, ...SetPath=20
                var vtbl = *(nint**)shellLinkPtr;

                // SetPath – slot 20
                string exePath = Environment.ProcessPath ?? string.Empty;
                nint hPath = Marshal.StringToHGlobalUni(exePath);
                try
                {
                    ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[20])(shellLinkPtr, hPath);
                }
                finally
                {
                    Marshal.FreeHGlobal(hPath);
                }

                // QI for IPropertyStore to set AppUserModelID
                var propStoreIid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                hr = Marshal.QueryInterface(shellLinkPtr, ref propStoreIid, out nint propStorePtr);
                if (hr >= 0)
                {
                    try
                    {
                        SetAppUserModelId(propStorePtr, appId);
                    }
                    finally
                    {
                        Marshal.Release(propStorePtr);
                    }
                }

                // QI for IPersistFile and save
                var persistFileIid = new Guid("0000010b-0000-0000-C000-000000000046");
                hr = Marshal.QueryInterface(shellLinkPtr, ref persistFileIid, out nint persistFilePtr);
                if (hr >= 0)
                {
                    try
                    {
                        // IPersistFile::Save (vtable slot 6 after IUnknown + IPersist)
                        var pvtbl = *(nint**)persistFilePtr;
                        nint hFile = Marshal.StringToHGlobalUni(shortcutPath);
                        try
                        {
                            ((delegate* unmanaged[Stdcall]<nint, nint, int, int>)pvtbl[6])(persistFilePtr, hFile, 1);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(hFile);
                        }
                    }
                    finally
                    {
                        Marshal.Release(persistFilePtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(shellLinkPtr);
            }
        }
        catch
        {
            // Best-effort – if shortcut creation fails, toasts may still work for packaged apps.
        }
    }

    private static void SetAppUserModelId(nint propStorePtr, string appId)
    {
        // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
        var pkey = new PropertyKey
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            pid = 5
        };

        // Create PROPVARIANT with VT_LPWSTR
        var propVar = new PropVariant();
        propVar.vt = 31; // VT_LPWSTR
        propVar.pwszVal = Marshal.StringToCoTaskMemUni(appId);

        try
        {
            var vtbl = *(nint**)propStorePtr;
            // IPropertyStore::SetValue – slot 6 (IUnknown 3 + GetCount + GetAt + GetValue + SetValue)
            ((delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)vtbl[6])(
                propStorePtr, &pkey, &propVar);
            // IPropertyStore::Commit – slot 7
            ((delegate* unmanaged[Stdcall]<nint, int>)vtbl[7])(propStorePtr);
        }
        finally
        {
            if (propVar.pwszVal != 0)
                Marshal.FreeCoTaskMem(propVar.pwszVal);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public nint pwszVal;
    }

    #endregion
}

#region WinRT COM Interop Helpers

/// <summary>
/// Raw WinRT COM vtable call helpers for toast notification APIs.
/// All methods are thin wrappers around unmanaged function-pointer calls.
/// </summary>
internal static unsafe class WinRT
{
    // ── WinRT Initialization ─────────────────────────────────────────
    private static bool s_initialized;

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int RoInitialize(int initType);

    /// <summary>
    /// Ensures the WinRT runtime is initialized on the current thread.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (s_initialized) return;
        // RO_INIT_SINGLETHREADED = 0, RO_INIT_MULTITHREADED = 1
        // Try single-threaded first (STA), then multithreaded.
        int hr = RoInitialize(0);
        if (hr < 0)
        {
            hr = RoInitialize(1);
            // RPC_E_CHANGED_MODE = 0x80010106 means already initialized in a different mode – that's OK.
            if (hr < 0 && hr != unchecked((int)0x80010106))
                Marshal.ThrowExceptionForHR(hr);
        }
        s_initialized = true;
    }

    // ── IIDs ──────────────────────────────────────────────────────────
    // IToastNotificationManagerStatics  {50AC103F-D235-4598-BBEF-98FE4D1A3AD4}
    public static Guid IID_IToastNotificationManagerStatics =
        new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");

    // IToastNotificationManagerStatics2 {7AB93C52-0E48-4750-BA9D-1A4113981847}
    public static Guid IID_IToastNotificationManagerStatics2 =
        new("7AB93C52-0E48-4750-BA9D-1A4113981847");

    // IToastNotificationFactory         {04124B20-82C6-4229-B109-FD9ED4662B53}
    public static Guid IID_IToastNotificationFactory =
        new("04124B20-82C6-4229-B109-FD9ED4662B53");

    // IToastNotification2               {9DFB9FD1-143A-490E-90BF-B9FBA7132DE7}
    public static Guid IID_IToastNotification2 =
        new("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7");

    // IToastNotificationHistory         {5caddc63-01d3-4c97-986f-0533483fee14}
    public static Guid IID_IToastNotificationHistory =
        new("5CADDC63-01D3-4C97-986F-0533483FEE14");

    // IXmlDocument                      {F7F3A506-1E87-42D6-BCFB-B8C809FA5494}
    public static Guid IID_IXmlDocument =
        new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");

    // IXmlDocumentIO                    {6CD0E74E-EE65-4489-9EBF-CA43E87BA637}
    public static Guid IID_IXmlDocumentIO =
        new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");

    // ── WindowsCreateString / WindowsDeleteString ─────────────────────
    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out nint hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int WindowsDeleteString(nint hstring);

    public static nint CreateHString(string s)
    {
        int hr = WindowsCreateString(s, s.Length, out nint h);
        Marshal.ThrowExceptionForHR(hr);
        return h;
    }

    // ── RoGetActivationFactory / RoActivateInstance ──────────────────
    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int RoGetActivationFactory(
        nint activatableClassId, ref Guid iid, out nint factory);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int RoActivateInstance(nint activatableClassId, out nint instance);

    // ── Vtable call wrappers ────────────────────────────────────────

    // IToastNotificationManagerStatics::CreateToastNotifierWithId
    // IUnknown(3) + IInspectable(3) = 6 base slots
    // CreateToastNotifier() = slot 6, CreateToastNotifierWithId(HSTRING) = slot 7
    public static int IToastNotificationManagerStatics_CreateToastNotifierWithId(
        nint @this, nint hAppId, out nint notifierPtr)
    {
        var vtbl = *(nint**)@this;
        nint result = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtbl[7])(
            @this, hAppId, &result);
        notifierPtr = result;
        return hr;
    }

    // IToastNotificationManagerStatics2::get_History – slot 6
    public static int IToastNotificationManagerStatics2_GetHistory(
        nint @this, out nint historyPtr)
    {
        var vtbl = *(nint**)@this;
        nint result = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)vtbl[6])(@this, &result);
        historyPtr = result;
        return hr;
    }

    // IToastNotifier::Show – IUnknown(3)+IInspectable(3)+Show=slot 6
    public static int IToastNotifier_Show(nint @this, nint toastNotification)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[6])(@this, toastNotification);
    }

    // IToastNotifier::Hide – slot 7
    public static int IToastNotifier_Hide(nint @this, nint toastNotification)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[7])(@this, toastNotification);
    }

    // IToastNotificationFactory::CreateToastNotification(XmlDocument) – slot 6
    public static int IToastNotificationFactory_CreateToastNotification(
        nint @this, nint content, out nint toastNotification)
    {
        var vtbl = *(nint**)@this;
        nint result = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtbl[6])(
            @this, content, &result);
        toastNotification = result;
        return hr;
    }

    // IToastNotification2::put_Tag – slot 8 (IUnknown3+IInspectable3+get_Tag=6+put_Tag=7 →
    //   wait, IToastNotification2 extends IInspectable: 3+3=6 base, get_Tag=6, put_Tag=7, get_Group=8, put_Group=9)
    public static int IToastNotification2_PutTag(nint @this, nint hTag)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[7])(@this, hTag);
    }

    public static int IToastNotification2_PutGroup(nint @this, nint hGroup)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[9])(@this, hGroup);
    }

    // IXmlDocumentIO::LoadXml – slot 6
    public static int IXmlDocumentIO_LoadXml(nint @this, nint hXml)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[6])(@this, hXml);
    }

    // IToastNotificationHistory::Clear(appId) – slot 10
    // Slots: IUnknown(3)+IInspectable(3) = 6 base
    // RemoveGroup=6, RemoveGroupWithId=7, Remove(tag,group,appId)=8, Remove(tag)=9, Clear=10
    public static int IToastNotificationHistory_Clear(nint @this, nint hAppId)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[10])(@this, hAppId);
    }

    // IToastNotificationHistory::Remove(tag, group, appId) – slot 8
    public static int IToastNotificationHistory_RemoveGroupedTagWithId(
        nint @this, nint hTag, nint hGroup, nint hAppId)
    {
        var vtbl = *(nint**)@this;
        return ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)vtbl[8])(
            @this, hTag, hGroup, hAppId);
    }

    // Simplified remove by tag (uses RemoveGroupedTagWithId with empty group as fallback)
    public static int IToastNotificationHistory_RemoveTagWithId(
        nint @this, nint hTag, nint hAppId)
    {
        // Use slot 7: Remove(tag, appId) – not all Windows versions have this,
        // fall back to RemoveGroupedTagWithId with empty group
        var vtbl = *(nint**)@this;
        nint hEmpty = CreateHString(string.Empty);
        try
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)vtbl[8])(
                @this, hTag, hEmpty, hAppId);
        }
        finally
        {
            WindowsDeleteString(hEmpty);
        }
    }
}

/// <summary>
/// Ole32 COM helpers.
/// </summary>
internal static class Ole32
{
    [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern int CoCreateInstance(
        ref Guid rclsid, nint pUnkOuter, uint dwClsContext,
        ref Guid riid, out nint ppv);
}

#endregion
