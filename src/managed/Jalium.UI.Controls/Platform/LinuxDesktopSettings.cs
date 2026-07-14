using System.Diagnostics;
using System.Globalization;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Reads Linux desktop preferences from standardized portals first and then
/// from the native GNOME/KDE settings providers. Values are cached because
/// SystemParameters properties are frequently queried during layout.
/// </summary>
internal static class LinuxDesktopSettings
{
    private static readonly object s_gate = new();
    private static readonly List<Process> s_monitorProcesses = [];
    private static readonly List<FileSystemWatcher> s_fileWatchers = [];
    private static Timer? s_xsettingsMonitor;
    private static int? s_xsettingsFingerprint;
    private static int s_xsettingsPollActive;
    private static Snapshot s_snapshot = ReadSnapshot();
    private static int s_monitoringStarted;
    private static int s_refreshQueued;

    internal static int DoubleClickTime => Current.Input.DoubleClickTime.Value;
    internal static int DoubleClickDistance => Current.Input.DoubleClickDistance.Value;
    internal static int DragThreshold => Current.Input.DragThreshold.Value;
    internal static int KeyboardDelay => Current.Input.KeyboardDelay.Value;
    internal static int KeyboardSpeed => Current.Input.KeyboardSpeed.Value;
    internal static int WheelScrollLines => Current.Input.WheelScrollLines.Value;
    internal static bool SwapButtons => Current.Input.SwapButtons.Value;
    internal static int MenuShowDelay => Current.Input.MenuShowDelay.Value;
    internal static LinuxSystemFontSettings SystemFontSettings => Current.Fonts;
    internal static int CaretBlinkTime => Current.CaretBlinkTime;
    internal static bool HighContrast => Current.HighContrast;
    internal static bool AnimationsEnabled => Current.AnimationsEnabled;
    internal static int CursorSize => Current.CursorSize;
    internal static string ThemeName => Current.ThemeName;

    internal static event EventHandler? SettingsChanged;

    private static Snapshot Current
    {
        get
        {
            lock (s_gate)
                return s_snapshot;
        }
    }

    /// <summary>
    /// Starts change monitors once per process. Portal appearance changes are
    /// signal-driven; GNOME and KDE native settings are monitored at their
    /// authoritative stores and only re-read after a change notification.
    /// </summary>
    internal static void EnsureMonitoring()
    {
        if (!OperatingSystem.IsLinux() ||
            Interlocked.Exchange(ref s_monitoringStarted, 1) != 0)
        {
            return;
        }

        _ = LinuxDesktopPortal.TrySubscribeSettingChanged(
            static (settingsNamespace, key, _) =>
            {
                if (settingsNamespace == "org.freedesktop.appearance" &&
                    key is "contrast" or "reduced-motion" or "color-scheme")
                {
                    QueueRefresh();
                }
            });

        StartGSettingsMonitor("org.gnome.desktop.interface");
        StartGSettingsMonitor("org.gnome.desktop.wm.preferences");
        StartGSettingsMonitor("org.gnome.desktop.peripherals.mouse");
        StartGSettingsMonitor("org.gnome.desktop.peripherals.keyboard");
        StartKdeConfigMonitor();
        StartXSettingsMonitor();
    }

    internal static void RefreshForTesting() => RefreshNow();

    private static void QueueRefresh()
    {
        if (Interlocked.Exchange(ref s_refreshQueued, 1) != 0)
            return;
        ThreadPool.QueueUserWorkItem(static _ =>
        {
            try { RefreshNow(); }
            finally { Volatile.Write(ref s_refreshQueued, 0); }
        });
    }

    private static void RefreshNow()
    {
        Snapshot updated = ReadSnapshot();
        bool changed;
        lock (s_gate)
        {
            changed = updated != s_snapshot;
            if (changed)
                s_snapshot = updated;
        }

        if (!changed)
            return;
        PushNativeDoubleClickSettings(updated);
        try { SettingsChanged?.Invoke(null, EventArgs.Empty); }
        catch
        {
            // A settings monitor must remain alive even if an application
            // subscriber throws from its notification callback.
        }
    }

    internal static void PushNativeDoubleClickSettings()
    {
        if (!OperatingSystem.IsLinux())
            return;
        PushNativeDoubleClickSettings(Current);
    }

    private static void PushNativeDoubleClickSettings(Snapshot snapshot)
    {
        try
        {
            _ = Jalium.UI.Interop.NativeMethods.PlatformSetDoubleClickSettings(
                (uint)Math.Max(snapshot.Input.DoubleClickTime.Value, 1),
                Math.Max(snapshot.Input.DoubleClickDistance.Value, 0));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older or intentionally absent native payloads keep their native
            // defaults; managed input still uses the resolved desktop values.
        }
    }

    private static void StartGSettingsMonitor(string schema)
    {
        try
        {
            var startInfo = new ProcessStartInfo("gsettings")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("monitor");
            startInfo.ArgumentList.Add(schema);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += static (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    QueueRefresh();
            };
            process.ErrorDataReceived += static (_, _) => { };
            process.Exited += static (sender, _) =>
            {
                if (sender is not Process exited)
                    return;
                lock (s_gate)
                    s_monitorProcesses.Remove(exited);
                exited.Dispose();
            };
            if (!process.Start())
            {
                process.Dispose();
                return;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (s_gate)
                s_monitorProcesses.Add(process);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // gsettings is optional (KDE/minimal sessions); portal/KDE monitors
            // continue to provide the settings that exist there.
        }
    }

    private static void StartKdeConfigMonitor()
    {
        try
        {
            string configDirectory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
                                     Path.Combine(
                                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                         ".config");
            if (!Directory.Exists(configDirectory))
                return;

            foreach (string file in new[] { "kdeglobals", "kcminputrc" })
            {
                var watcher = new FileSystemWatcher(configDirectory, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                                   NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += static (_, _) => QueueRefresh();
                watcher.Created += static (_, _) => QueueRefresh();
                watcher.Deleted += static (_, _) => QueueRefresh();
                watcher.Renamed += static (_, _) => QueueRefresh();
                lock (s_gate)
                    s_fileWatchers.Add(watcher);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only or sandboxed config directory simply means KDE
            // settings cannot be watched; the initial snapshot remains valid.
        }
    }

    private static void StartXSettingsMonitor()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return;

        s_xsettingsFingerprint = ReadXSettingsFingerprint();
        s_xsettingsMonitor = new Timer(
            static _ => PollXSettings(),
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
    }

    private static void PollXSettings()
    {
        if (Interlocked.Exchange(ref s_xsettingsPollActive, 1) != 0)
            return;
        try
        {
            int? updated = ReadXSettingsFingerprint();
            bool changed;
            lock (s_gate)
            {
                changed = updated != s_xsettingsFingerprint;
                s_xsettingsFingerprint = updated;
            }
            if (changed)
                QueueRefresh();
        }
        finally
        {
            Volatile.Write(ref s_xsettingsPollActive, 0);
        }
    }

    private static int? ReadXSettingsFingerprint()
    {
        if (!LinuxXSettings.TryRead(out IReadOnlyDictionary<string, LinuxXSettingValue> settings))
            return null;

        var hash = new HashCode();
        foreach (string key in new[]
                 {
                     "Net/DoubleClickTime",
                     "Net/DoubleClickDistance",
                     "Net/DndDragThreshold",
                     "Gtk/MenuPopupDelay",
                     "Gtk/FontName",
                 })
        {
            if (!settings.TryGetValue(key, out LinuxXSettingValue value))
                continue;
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value.Kind);
            hash.Add(value.Integer);
            hash.Add(value.String, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }

    internal static PowerLineStatus ReadPowerLineStatus()
    {
        if (!OperatingSystem.IsLinux())
            return global::Jalium.UI.PowerLineStatus.Unknown;

        try
        {
            const string powerSupplyRoot = "/sys/class/power_supply";
            if (!Directory.Exists(powerSupplyRoot))
                return global::Jalium.UI.PowerLineStatus.Unknown;

            var foundExternalPower = false;
            foreach (var directory in Directory.EnumerateDirectories(powerSupplyRoot))
            {
                var typePath = Path.Combine(directory, "type");
                if (!File.Exists(typePath))
                    continue;
                var type = File.ReadAllText(typePath).Trim();
                if (type is not ("Mains" or "USB" or "USB_C" or "USB_PD" or "Wireless"))
                    continue;

                foundExternalPower = true;
                var onlinePath = Path.Combine(directory, "online");
                if (File.Exists(onlinePath) && File.ReadAllText(onlinePath).Trim() == "1")
                    return global::Jalium.UI.PowerLineStatus.Online;
            }

            return foundExternalPower
                ? global::Jalium.UI.PowerLineStatus.Offline
                : global::Jalium.UI.PowerLineStatus.Unknown;
        }
        catch
        {
            return global::Jalium.UI.PowerLineStatus.Unknown;
        }
    }

    internal static bool TryParseGSettingsInteger(string? output, out int value)
    {
        output = output?.Trim();
        if (string.IsNullOrEmpty(output))
        {
            value = 0;
            return false;
        }

        var lastSpace = output.LastIndexOf(' ');
        if (lastSpace >= 0)
            output = output[(lastSpace + 1)..];
        return int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    internal static string UnquoteGSettingsString(string? output)
    {
        output = output?.Trim() ?? string.Empty;
        if (output.Length >= 2 &&
            ((output[0] == '\'' && output[^1] == '\'') ||
             (output[0] == '"' && output[^1] == '"')))
        {
            output = output[1..^1];
        }
        return output.Replace("\\'", "'", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static Snapshot ReadSnapshot()
    {
        if (!OperatingSystem.IsLinux())
            return Snapshot.Default;

        LinuxResolvedInputSettings input = ResolveInputSettings(ReadInputSettings());
        LinuxSystemFontSettings fonts = ReadSystemFontSettings(
            global::Jalium.UI.FrameworkElement.DefaultFontFamilyName);

        var caretBlinkEnabled = ReadGSettingsBoolean("org.gnome.desktop.interface", "cursor-blink");
        var caretCycle = ReadGSettingsInteger("org.gnome.desktop.interface", "cursor-blink-time");
        var kdeCaret = ReadKdeInteger("kdeglobals", "KDE", "CursorBlinkRate");
        var caretBlink = caretBlinkEnabled == false
            ? -1
            : caretCycle is > 0
                ? Math.Clamp(caretCycle.Value / 2, 100, 5000)
                : kdeCaret is > 0
                    ? Math.Clamp(kdeCaret.Value, 100, 5000)
                    : 530;

        var theme = ReadGSettingsString("org.gnome.desktop.interface", "gtk-theme");
        if (string.IsNullOrWhiteSpace(theme))
            theme = Environment.GetEnvironmentVariable("GTK_THEME")?.Split(':')[0] ?? "Default";

        var highContrast = LinuxDesktopPortal.TryReadContrast() == 1 ||
                           theme.Contains("highcontrast", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(
                               Environment.GetEnvironmentVariable("JALIUM_HIGH_CONTRAST"),
                               "1",
                               StringComparison.Ordinal);

        var reducedMotion = LinuxDesktopPortal.TryReadReducedMotion() == 1;
        var toolkitAnimations = ReadGSettingsBoolean("org.gnome.desktop.interface", "enable-animations");
        var animationsEnabled = !reducedMotion && toolkitAnimations != false;

        var cursorSize = ReadPositiveIntegerEnvironment("XCURSOR_SIZE") ??
                         ReadGSettingsInteger("org.gnome.desktop.interface", "cursor-size") ??
                         24;
        cursorSize = Math.Clamp(cursorSize, 8, 256);

        return new Snapshot(
            input,
            fonts,
            caretBlink,
            highContrast,
            animationsEnabled,
            cursorSize,
            theme);
    }

    private static LinuxInputSettings ReadInputSettings()
    {
        _ = LinuxXSettings.TryRead(out IReadOnlyDictionary<string, LinuxXSettingValue> xsettings);
        LinuxDesktopEnvironmentKind desktop = DetectDesktopEnvironment(
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
            Environment.GetEnvironmentVariable("DESKTOP_SESSION"));
        bool useGnomeSettings = desktop == LinuxDesktopEnvironmentKind.Gnome;
        bool useKdeSettings = desktop == LinuxDesktopEnvironmentKind.Kde;

        return new LinuxInputSettings
        {
            EnvironmentDoubleClickTime = ReadPositiveIntegerEnvironment("JALIUM_DOUBLE_CLICK_TIME"),
            EnvironmentDoubleClickDistance = ReadNonNegativeIntegerEnvironment("JALIUM_DOUBLE_CLICK_DISTANCE"),
            EnvironmentDragThreshold = ReadPositiveIntegerEnvironment("JALIUM_DRAG_THRESHOLD"),
            EnvironmentKeyboardDelayMilliseconds = ReadPositiveIntegerEnvironment("JALIUM_KEYBOARD_DELAY_MS"),
            EnvironmentKeyboardRepeatRate = ReadPositiveDoubleEnvironment("JALIUM_KEYBOARD_REPEAT_RATE"),
            EnvironmentWheelScrollLines = ReadNonNegativeIntegerEnvironment("JALIUM_WHEEL_SCROLL_LINES"),
            EnvironmentSwapButtons = ReadBooleanEnvironment("JALIUM_SWAP_MOUSE_BUTTONS"),
            EnvironmentMenuShowDelay = ReadNonNegativeIntegerEnvironment("JALIUM_MENU_SHOW_DELAY"),

            XSettingsDoubleClickTime = ReadXSettingInteger(xsettings, "Net/DoubleClickTime"),
            XSettingsDoubleClickDistance = ReadXSettingInteger(xsettings, "Net/DoubleClickDistance"),
            XSettingsDragThreshold = ReadXSettingInteger(xsettings, "Net/DndDragThreshold"),
            XSettingsMenuShowDelay = ReadXSettingInteger(xsettings, "Gtk/MenuPopupDelay"),

            // GNOME publishes click distance/time and DnD threshold through
            // XSETTINGS. The GSettings mouse schema still supplies button
            // handedness, while the keyboard schema uses milliseconds.
            GnomeDoubleClickTime = useGnomeSettings
                ? ReadGSettingsInteger(
                      "org.gnome.settings-daemon.peripherals.mouse", "double-click") ??
                  ReadGSettingsInteger("org.gnome.desktop.peripherals.mouse", "double-click")
                : null,
            GnomeDragThreshold = useGnomeSettings
                ? ReadGSettingsInteger(
                      "org.gnome.settings-daemon.peripherals.mouse", "drag-threshold") ??
                  ReadGSettingsInteger("org.gnome.desktop.peripherals.mouse", "drag-threshold")
                : null,
            GnomeKeyboardDelayMilliseconds = useGnomeSettings
                ? ReadGSettingsInteger("org.gnome.desktop.peripherals.keyboard", "delay")
                : null,
            GnomeKeyboardRepeatIntervalMilliseconds = useGnomeSettings
                ? ReadGSettingsInteger("org.gnome.desktop.peripherals.keyboard", "repeat-interval")
                : null,
            GnomeSwapButtons = useGnomeSettings
                ? ReadGSettingsBoolean("org.gnome.desktop.peripherals.mouse", "left-handed")
                : null,

            // Qt/KDE exposes application-wide input hints in kdeglobals. Keep
            // the older Mouse group fallbacks for Plasma 4/early Plasma 5.
            KdeDoubleClickTime = useKdeSettings
                ? ReadKdeInteger("kdeglobals", "KDE", "DoubleClickInterval") ??
                  ReadKdeInteger("kcminputrc", "Mouse", "DoubleClickInterval")
                : null,
            KdeDragThreshold = useKdeSettings
                ? ReadKdeInteger("kdeglobals", "KDE", "StartDragDist") ??
                  ReadKdeInteger("kcminputrc", "Mouse", "StartDragDist")
                : null,
            KdeKeyboardDelayMilliseconds = useKdeSettings
                ? ReadKdeInteger("kcminputrc", "Keyboard", "RepeatDelay")
                : null,
            KdeKeyboardRepeatRate = useKdeSettings
                ? ReadKdeDouble("kcminputrc", "Keyboard", "RepeatRate")
                : null,
            KdeWheelScrollLines = useKdeSettings
                ? ReadKdeInteger("kdeglobals", "KDE", "WheelScrollLines")
                : null,
            KdeSwapButtons = useKdeSettings ? ReadKdeMouseButtonSwap() : null,
            KdeMenuShowDelay = useKdeSettings
                ? ReadKdeInteger("kdeglobals", "KDE", "MenuShowDelay")
                : null,
        };
    }

    internal static LinuxDesktopEnvironmentKind DetectDesktopEnvironment(
        string? currentDesktop,
        string? sessionDesktop,
        string? desktopSession)
    {
        string[] values = [currentDesktop ?? string.Empty,
                           sessionDesktop ?? string.Empty,
                           desktopSession ?? string.Empty];
        foreach (string value in values)
        {
            foreach (string token in value.Split([':', ';'], StringSplitOptions.RemoveEmptyEntries |
                                                              StringSplitOptions.TrimEntries))
            {
                if (token.Equals("KDE", StringComparison.OrdinalIgnoreCase) ||
                    token.Contains("PLASMA", StringComparison.OrdinalIgnoreCase))
                {
                    return LinuxDesktopEnvironmentKind.Kde;
                }
            }
        }

        foreach (string value in values)
        {
            foreach (string token in value.Split([':', ';'], StringSplitOptions.RemoveEmptyEntries |
                                                              StringSplitOptions.TrimEntries))
            {
                if (token.Equals("GNOME", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("UBUNTU", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("UNITY", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("BUDGIE", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("PANTHEON", StringComparison.OrdinalIgnoreCase) ||
                    token.Contains("COSMIC", StringComparison.OrdinalIgnoreCase))
                {
                    return LinuxDesktopEnvironmentKind.Gnome;
                }
            }
        }
        return LinuxDesktopEnvironmentKind.Unknown;
    }

    /// <summary>
    /// Resolves Linux UI fonts. JALIUM_SYSTEM_FONT accepts a Pango description
    /// and applies to every role; JALIUM_SYSTEM_{CAPTION,SMALL_CAPTION,MENU,
    /// STATUS,MESSAGE,ICON}_FONT can override an individual role. Point sizes
    /// are converted to 96-DPI units; an explicit px suffix stays absolute.
    /// </summary>
    internal static LinuxSystemFontSettings ReadSystemFontSettings(string fallbackFamilyName)
    {
        if (!OperatingSystem.IsLinux())
            return ResolveSystemFontSettings(new LinuxSystemFontCandidates(), fallbackFamilyName);

        LinuxDesktopEnvironmentKind desktop = DetectDesktopEnvironment(
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
            Environment.GetEnvironmentVariable("DESKTOP_SESSION"));
        _ = LinuxXSettings.TryRead(out IReadOnlyDictionary<string, LinuxXSettingValue> xsettings);

        LinuxSystemFontDescription? xsettingsFont = LinuxXSettings.TryGetString(
                xsettings, "Gtk/FontName", out string gtkFontName)
            ? ParsePangoFontDescription(gtkFontName)
            : null;
        bool useGnomeSettings = desktop == LinuxDesktopEnvironmentKind.Gnome;
        bool useKdeSettings = desktop == LinuxDesktopEnvironmentKind.Kde;

        return ResolveSystemFontSettings(
            new LinuxSystemFontCandidates
            {
                EnvironmentGeneral = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_FONT")),
                EnvironmentCaption = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_CAPTION_FONT")),
                EnvironmentSmallCaption = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_SMALL_CAPTION_FONT")),
                EnvironmentMenu = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_MENU_FONT")),
                EnvironmentStatus = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_STATUS_FONT")),
                EnvironmentMessage = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_MESSAGE_FONT")),
                EnvironmentIcon = ParsePangoFontDescription(
                    Environment.GetEnvironmentVariable("JALIUM_SYSTEM_ICON_FONT")),
                XSettingsGeneral = xsettingsFont,
                GnomeGeneral = useGnomeSettings
                    ? ParsePangoFontDescription(
                        ReadGSettingsString("org.gnome.desktop.interface", "font-name"))
                    : null,
                GnomeTitlebarUsesSystemFont = useGnomeSettings
                    ? ReadGSettingsBoolean(
                        "org.gnome.desktop.wm.preferences", "titlebar-uses-system-font")
                    : null,
                GnomeTitlebar = useGnomeSettings
                    ? ParsePangoFontDescription(
                        ReadGSettingsString("org.gnome.desktop.wm.preferences", "titlebar-font"))
                    : null,
                KdeGeneral = useKdeSettings
                    ? ParseKdeFontDescription(ReadKdeValue("kdeglobals", "General", "font"))
                    : null,
                KdeCaption = useKdeSettings
                    ? ParseKdeFontDescription(ReadKdeValue("kdeglobals", "WM", "activeFont"))
                    : null,
                KdeSmallCaption = useKdeSettings
                    ? ParseKdeFontDescription(
                        ReadKdeValue("kdeglobals", "General", "smallestReadableFont"))
                    : null,
                KdeMenu = useKdeSettings
                    ? ParseKdeFontDescription(ReadKdeValue("kdeglobals", "General", "menuFont"))
                    : null,
                KdeStatus = useKdeSettings
                    ? ParseKdeFontDescription(ReadKdeValue("kdeglobals", "General", "taskbarFont"))
                    : null,
                KdeIcon = useKdeSettings
                    ? ParseKdeFontDescription(ReadKdeValue("kdeglobals", "General", "desktopFont"))
                    : null,
            },
            fallbackFamilyName);
    }

    internal static LinuxSystemFontSettings ResolveSystemFontSettings(
        LinuxSystemFontCandidates input,
        string fallbackFamilyName)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(fallbackFamilyName))
            fallbackFamilyName = "Sans";

        LinuxDesktopSetting<LinuxSystemFontDescription> fallbackGeneral = FallbackFont(
            fallbackFamilyName, 14);
        LinuxDesktopSetting<LinuxSystemFontDescription> fallbackSmallCaption = FallbackFont(
            fallbackFamilyName, 11);
        LinuxDesktopSetting<LinuxSystemFontDescription> fallbackIcon = FallbackFont(
            fallbackFamilyName, 9);

        LinuxDesktopSetting<LinuxSystemFontDescription> general = FirstFont(
            fallbackGeneral,
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment),
            (input.XSettingsGeneral, LinuxDesktopSettingSource.XSettings),
            (input.GnomeGeneral, LinuxDesktopSettingSource.GSettings),
            (input.KdeGeneral, LinuxDesktopSettingSource.Kde));

        LinuxSystemFontDescription? gnomeTitlebar = input.GnomeTitlebarUsesSystemFont == false
            ? input.GnomeTitlebar
            : null;
        LinuxDesktopSetting<LinuxSystemFontDescription> caption = FirstFont(
            general.Source == LinuxDesktopSettingSource.Fallback ? fallbackGeneral : general,
            (input.EnvironmentCaption, LinuxDesktopSettingSource.Environment),
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment),
            (gnomeTitlebar, LinuxDesktopSettingSource.GSettings),
            (input.KdeCaption, LinuxDesktopSettingSource.Kde),
            (input.XSettingsGeneral, LinuxDesktopSettingSource.XSettings),
            (input.GnomeGeneral, LinuxDesktopSettingSource.GSettings),
            (input.KdeGeneral, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<LinuxSystemFontDescription> smallCaption = FirstFont(
            caption.Source == LinuxDesktopSettingSource.Fallback ? fallbackSmallCaption : caption,
            (input.EnvironmentSmallCaption, LinuxDesktopSettingSource.Environment),
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment),
            (input.KdeSmallCaption, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<LinuxSystemFontDescription> menu = FirstFont(
            general,
            (input.EnvironmentMenu, LinuxDesktopSettingSource.Environment),
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment),
            (input.KdeMenu, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<LinuxSystemFontDescription> status = FirstFont(
            general,
            (input.EnvironmentStatus, LinuxDesktopSettingSource.Environment),
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment),
            (input.KdeStatus, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<LinuxSystemFontDescription> message = FirstFont(
            general,
            (input.EnvironmentMessage, LinuxDesktopSettingSource.Environment),
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment));
        LinuxDesktopSetting<LinuxSystemFontDescription> icon = FirstFont(
            general.Source == LinuxDesktopSettingSource.Fallback ? fallbackIcon : general,
            (input.EnvironmentIcon, LinuxDesktopSettingSource.Environment),
            (input.EnvironmentGeneral, LinuxDesktopSettingSource.Environment),
            (input.KdeIcon, LinuxDesktopSettingSource.Kde));

        return new LinuxSystemFontSettings(caption, smallCaption, menu, status, message, icon);
    }

    private static LinuxDesktopSetting<LinuxSystemFontDescription> FallbackFont(
        string family,
        double size) =>
        new(new LinuxSystemFontDescription(family.Trim(), size, 400, false),
            LinuxDesktopSettingSource.Fallback);

    private static LinuxDesktopSetting<LinuxSystemFontDescription> FirstFont(
        LinuxDesktopSetting<LinuxSystemFontDescription> fallback,
        params (LinuxSystemFontDescription? Value, LinuxDesktopSettingSource Source)[] candidates)
    {
        foreach ((LinuxSystemFontDescription? candidate, LinuxDesktopSettingSource source) in candidates)
        {
            if (candidate.HasValue)
                return new LinuxDesktopSetting<LinuxSystemFontDescription>(candidate.Value, source);
        }
        return fallback;
    }

    private static LinuxSystemFontDescription? ParsePangoFontDescription(string? value) =>
        TryParsePangoFontDescription(value, out LinuxSystemFontDescription description)
            ? description
            : null;

    internal static bool TryParsePangoFontDescription(
        string? value,
        out LinuxSystemFontDescription description)
    {
        description = default;
        string text = value?.Trim().Trim('"', '\'') ?? string.Empty;
        if (text.Length == 0)
            return false;

        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !TryParseFontSize(tokens[^1], out double size))
            return false;

        int weight = 400;
        bool weightFound = false;
        bool italic = false;
        int familyEnd = tokens.Length - 1;
        while (familyEnd > 0)
        {
            string token = NormalizeFontStyleToken(tokens[familyEnd - 1]);
            if (token is "italic" or "oblique")
            {
                italic = true;
                familyEnd--;
                continue;
            }

            if (familyEnd > 1 &&
                TryMapPangoWeight(
                    NormalizeFontStyleToken(tokens[familyEnd - 2] + tokens[familyEnd - 1]),
                    out int combinedWeight))
            {
                if (!weightFound)
                    weight = combinedWeight;
                weightFound = true;
                familyEnd -= 2;
                continue;
            }

            if (TryMapPangoWeight(token, out int singleWeight))
            {
                if (!weightFound)
                    weight = singleWeight;
                weightFound = true;
                familyEnd--;
                continue;
            }
            break;
        }

        string family = string.Join(' ', tokens.AsSpan(0, familyEnd).ToArray()).Trim();
        if (family.Length == 0)
            return false;
        description = new LinuxSystemFontDescription(family, size, weight, italic);
        return true;
    }

    private static bool TryParseFontSize(string token, out double size)
    {
        token = token.Trim();
        bool pixels = token.EndsWith("px", StringComparison.OrdinalIgnoreCase);
        if (pixels || token.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^2];
        }
        if (!double.TryParse(
                token, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            !double.IsFinite(parsed) || parsed is < 4 or > 256)
        {
            size = 0;
            return false;
        }

        // Pango descriptions use points unless the value has the absolute
        // "px" suffix. Jalium font sizes are DIPs, matching WPF's 96-DPI unit.
        size = pixels ? parsed : parsed * 96.0 / 72.0;
        return true;
    }

    private static string NormalizeFontStyleToken(string token) =>
        token.Replace("-", string.Empty, StringComparison.Ordinal)
             .Replace("_", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static bool TryMapPangoWeight(string token, out int weight)
    {
        weight = token switch
        {
            "thin" => 100,
            "ultralight" or "extralight" => 200,
            "light" or "semilight" => 300,
            "book" or "normal" or "regular" => 400,
            "medium" => 500,
            "semibold" or "demibold" => 600,
            "bold" => 700,
            "ultrabold" or "extrabold" => 800,
            "heavy" or "black" or "ultrablack" => 900,
            _ => 0,
        };
        return weight != 0;
    }

    private static LinuxSystemFontDescription? ParseKdeFontDescription(string? value) =>
        TryParseKdeFontDescription(value, out LinuxSystemFontDescription description)
            ? description
            : null;

    internal static bool TryParseKdeFontDescription(
        string? value,
        out LinuxSystemFontDescription description)
    {
        description = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string[] fields = value.Split(',');
        if (fields.Length < 6)
            return false;
        string family = fields[0].Trim();
        if (family.Length == 0)
            return false;

        double size;
        if (double.TryParse(
                fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pointSize) &&
            double.IsFinite(pointSize) && pointSize > 0)
        {
            size = pointSize * 96.0 / 72.0;
        }
        else
        {
            if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out size) ||
                !double.IsFinite(size) || size <= 0)
            {
                return false;
            }
        }
        if (size is < 4 or > 256 ||
            !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int qtWeight))
        {
            return false;
        }

        int weight = ConvertKdeFontWeight(qtWeight);
        if (weight == 0)
            return false;
        bool italic = int.TryParse(
            fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int style)
            ? style != 0
            : TryParseBoolean(fields[5], out bool parsedItalic) && parsedItalic;
        description = new LinuxSystemFontDescription(family, size, weight, italic);
        return true;
    }

    internal static int ConvertKdeFontWeight(int weight)
    {
        if (weight is >= 0 and <= 99)
        {
            return weight switch
            {
                <= 6 => 100,
                <= 18 => 200,
                <= 37 => 300,
                <= 53 => 400,
                <= 60 => 500,
                <= 69 => 600,
                <= 78 => 700,
                <= 84 => 800,
                _ => 900,
            };
        }
        return weight is >= 100 and <= 1000
            ? Math.Clamp((int)Math.Round(weight / 100.0) * 100, 100, 900)
            : 0;
    }

    internal static LinuxResolvedInputSettings ResolveInputSettings(LinuxInputSettings input)
    {
        ArgumentNullException.ThrowIfNull(input);

        LinuxDesktopSetting<int> doubleClickTime = FirstInteger(
            fallback: 500,
            minimum: 1,
            maximum: 60_000,
            (input.EnvironmentDoubleClickTime, LinuxDesktopSettingSource.Environment),
            (input.XSettingsDoubleClickTime, LinuxDesktopSettingSource.XSettings),
            (input.GnomeDoubleClickTime, LinuxDesktopSettingSource.GSettings),
            (input.KdeDoubleClickTime, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<int> doubleClickDistance = FirstInteger(
            fallback: 5,
            minimum: 0,
            maximum: 16_384,
            (input.EnvironmentDoubleClickDistance, LinuxDesktopSettingSource.Environment),
            (input.XSettingsDoubleClickDistance, LinuxDesktopSettingSource.XSettings));
        LinuxDesktopSetting<int> dragThreshold = FirstInteger(
            fallback: 8,
            minimum: 1,
            maximum: 16_384,
            (input.EnvironmentDragThreshold, LinuxDesktopSettingSource.Environment),
            (input.XSettingsDragThreshold, LinuxDesktopSettingSource.XSettings),
            (input.GnomeDragThreshold, LinuxDesktopSettingSource.GSettings),
            (input.KdeDragThreshold, LinuxDesktopSettingSource.Kde));

        LinuxDesktopSetting<int> keyboardDelay = ResolveKeyboardDelay(input);
        LinuxDesktopSetting<int> keyboardSpeed = ResolveKeyboardSpeed(input);
        LinuxDesktopSetting<int> wheelScrollLines = FirstInteger(
            fallback: 3,
            minimum: 0,
            maximum: 100,
            (input.EnvironmentWheelScrollLines, LinuxDesktopSettingSource.Environment),
            (input.KdeWheelScrollLines, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<bool> swapButtons = FirstBoolean(
            fallback: false,
            (input.EnvironmentSwapButtons, LinuxDesktopSettingSource.Environment),
            (input.GnomeSwapButtons, LinuxDesktopSettingSource.GSettings),
            (input.KdeSwapButtons, LinuxDesktopSettingSource.Kde));
        LinuxDesktopSetting<int> menuShowDelay = FirstInteger(
            fallback: 400,
            minimum: 0,
            maximum: 60_000,
            (input.EnvironmentMenuShowDelay, LinuxDesktopSettingSource.Environment),
            (input.XSettingsMenuShowDelay, LinuxDesktopSettingSource.XSettings),
            (input.KdeMenuShowDelay, LinuxDesktopSettingSource.Kde));

        return new LinuxResolvedInputSettings(
            doubleClickTime,
            doubleClickDistance,
            dragThreshold,
            keyboardDelay,
            keyboardSpeed,
            wheelScrollLines,
            swapButtons,
            menuShowDelay);
    }

    private static int? ReadPositiveIntegerEnvironment(string name) =>
        int.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) && value > 0
            ? value
            : null;

    private static int? ReadNonNegativeIntegerEnvironment(string name) =>
        int.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) && value >= 0
            ? value
            : null;

    private static double? ReadPositiveDoubleEnvironment(string name) =>
        double.TryParse(
            Environment.GetEnvironmentVariable(name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value) && double.IsFinite(value) && value > 0
            ? value
            : null;

    private static bool? ReadBooleanEnvironment(string name) =>
        TryParseBoolean(Environment.GetEnvironmentVariable(name), out bool value)
            ? value
            : null;

    private static int? ReadXSettingInteger(
        IReadOnlyDictionary<string, LinuxXSettingValue>? settings,
        string name) =>
        LinuxXSettings.TryGetInteger(settings, name, out int value) ? value : null;

    private static LinuxDesktopSetting<int> FirstInteger(
        int fallback,
        int minimum,
        int maximum,
        params (int? Value, LinuxDesktopSettingSource Source)[] candidates)
    {
        foreach ((int? candidate, LinuxDesktopSettingSource source) in candidates)
        {
            if (candidate.HasValue && candidate.Value >= minimum && candidate.Value <= maximum)
                return new LinuxDesktopSetting<int>(candidate.Value, source);
        }
        return new LinuxDesktopSetting<int>(fallback, LinuxDesktopSettingSource.Fallback);
    }

    private static LinuxDesktopSetting<bool> FirstBoolean(
        bool fallback,
        params (bool? Value, LinuxDesktopSettingSource Source)[] candidates)
    {
        foreach ((bool? candidate, LinuxDesktopSettingSource source) in candidates)
        {
            if (candidate.HasValue)
                return new LinuxDesktopSetting<bool>(candidate.Value, source);
        }
        return new LinuxDesktopSetting<bool>(fallback, LinuxDesktopSettingSource.Fallback);
    }

    private static LinuxDesktopSetting<int> ResolveKeyboardDelay(LinuxInputSettings input)
    {
        foreach ((int? milliseconds, LinuxDesktopSettingSource source) in new[]
                 {
                     (input.EnvironmentKeyboardDelayMilliseconds, LinuxDesktopSettingSource.Environment),
                     (input.GnomeKeyboardDelayMilliseconds, LinuxDesktopSettingSource.GSettings),
                     (input.KdeKeyboardDelayMilliseconds, LinuxDesktopSettingSource.Kde),
                 })
        {
            if (milliseconds is >= 1 and <= 60_000)
                return new LinuxDesktopSetting<int>(KeyboardDelayFromMilliseconds(milliseconds.Value), source);
        }
        return new LinuxDesktopSetting<int>(1, LinuxDesktopSettingSource.Fallback);
    }

    private static LinuxDesktopSetting<int> ResolveKeyboardSpeed(LinuxInputSettings input)
    {
        if (input.EnvironmentKeyboardRepeatRate is >= 0.2 and <= 200)
        {
            return new LinuxDesktopSetting<int>(
                KeyboardSpeedFromRepeatsPerSecond(input.EnvironmentKeyboardRepeatRate.Value),
                LinuxDesktopSettingSource.Environment);
        }
        if (input.GnomeKeyboardRepeatIntervalMilliseconds is >= 5 and <= 5000)
        {
            return new LinuxDesktopSetting<int>(
                KeyboardSpeedFromRepeatsPerSecond(
                    1000.0 / input.GnomeKeyboardRepeatIntervalMilliseconds.Value),
                LinuxDesktopSettingSource.GSettings);
        }
        if (input.KdeKeyboardRepeatRate is >= 0.2 and <= 200)
        {
            return new LinuxDesktopSetting<int>(
                KeyboardSpeedFromRepeatsPerSecond(input.KdeKeyboardRepeatRate.Value),
                LinuxDesktopSettingSource.Kde);
        }
        return new LinuxDesktopSetting<int>(31, LinuxDesktopSettingSource.Fallback);
    }

    internal static int KeyboardDelayFromMilliseconds(int milliseconds)
    {
        double setting = (milliseconds - 250.0) / 250.0;
        return Math.Clamp((int)Math.Round(setting, MidpointRounding.AwayFromZero), 0, 3);
    }

    internal static int KeyboardSpeedFromRepeatsPerSecond(double repeatsPerSecond)
    {
        double setting = (repeatsPerSecond - 2.5) * 31.0 / 27.5;
        return Math.Clamp((int)Math.Round(setting, MidpointRounding.AwayFromZero), 0, 31);
    }

    private static int? ReadGSettingsInteger(string schema, string key) =>
        TryRun("gsettings", ["get", schema, key], out var output) &&
        TryParseGSettingsInteger(output, out var value)
            ? value
            : null;

    private static bool? ReadGSettingsBoolean(string schema, string key)
    {
        if (!TryRun("gsettings", ["get", schema, key], out var output))
            return null;
        return bool.TryParse(output.Trim(), out var value) ? value : null;
    }

    private static string ReadGSettingsString(string schema, string key) =>
        TryRun("gsettings", ["get", schema, key], out var output)
            ? UnquoteGSettingsString(output)
            : string.Empty;

    private static int? ReadKdeInteger(string file, string group, string key) =>
        int.TryParse(
            ReadKdeValue(file, group, key),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : null;

    private static double? ReadKdeDouble(string file, string group, string key) =>
        double.TryParse(
            ReadKdeValue(file, group, key),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value) && double.IsFinite(value)
            ? value
            : null;

    private static bool? ReadKdeMouseButtonSwap()
    {
        string? mapping = ReadKdeValue("kcminputrc", "Mouse", "MouseButtonMapping");
        if (string.Equals(mapping, "LeftHanded", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(mapping, "RightHanded", StringComparison.OrdinalIgnoreCase))
            return false;

        string? leftHanded = ReadKdeValue("kcminputrc", "Mouse", "XLbInptLeftHanded") ??
                             ReadKdeValue("kcminputrc", "Mouse", "LeftHanded");
        return TryParseBoolean(leftHanded, out bool value) ? value : null;
    }

    private static string? ReadKdeValue(string file, string group, string key)
    {
        foreach (var command in new[] { "kreadconfig6", "kreadconfig5" })
        {
            if (TryRun(command, ["--file", file, "--group", group, "--key", key], out var output) &&
                !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }
        }

        try
        {
            string path = Path.Combine(GetConfigDirectory(), file);
            if (File.Exists(path) &&
                TryReadKdeConfigValue(File.ReadAllText(path), group, key, out string value))
            {
                return value;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    internal static bool TryReadKdeConfigValue(
        string? content,
        string group,
        string key,
        out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(content))
            return false;

        bool inRequestedGroup = false;
        bool found = false;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                inRequestedGroup = string.Equals(
                    line[1..^1].Trim(), group, StringComparison.Ordinal);
                continue;
            }
            if (!inRequestedGroup)
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0 || !string.Equals(line[..equals].Trim(), key, StringComparison.Ordinal))
                continue;
            value = line[(equals + 1)..].Trim();
            found = true;
        }
        return found;
    }

    private static string GetConfigDirectory() =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config");

    internal static bool TryParseBoolean(string? text, out bool value)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "1" or "true" or "yes" or "on" or "lefthanded":
                value = true;
                return true;
            case "0" or "false" or "no" or "off" or "righthanded":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryRun(string fileName, IReadOnlyList<string> arguments, out string output)
    {
        output = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    private sealed record Snapshot(
        LinuxResolvedInputSettings Input,
        LinuxSystemFontSettings Fonts,
        int CaretBlinkTime,
        bool HighContrast,
        bool AnimationsEnabled,
        int CursorSize,
        string ThemeName)
    {
        internal static Snapshot Default { get; } = new(
            LinuxResolvedInputSettings.Default,
            ResolveSystemFontSettings(new LinuxSystemFontCandidates(), "Sans"),
            530,
            false,
            true,
            32,
            "Default");
    }
}

internal enum LinuxDesktopSettingSource
{
    Fallback,
    Environment,
    XSettings,
    GSettings,
    Kde,
}

internal enum LinuxDesktopEnvironmentKind
{
    Unknown,
    Gnome,
    Kde,
}

internal readonly record struct LinuxDesktopSetting<T>(
    T Value,
    LinuxDesktopSettingSource Source);

internal sealed record LinuxResolvedInputSettings(
    LinuxDesktopSetting<int> DoubleClickTime,
    LinuxDesktopSetting<int> DoubleClickDistance,
    LinuxDesktopSetting<int> DragThreshold,
    LinuxDesktopSetting<int> KeyboardDelay,
    LinuxDesktopSetting<int> KeyboardSpeed,
    LinuxDesktopSetting<int> WheelScrollLines,
    LinuxDesktopSetting<bool> SwapButtons,
    LinuxDesktopSetting<int> MenuShowDelay)
{
    internal static LinuxResolvedInputSettings Default { get; } = new(
        new(500, LinuxDesktopSettingSource.Fallback),
        new(5, LinuxDesktopSettingSource.Fallback),
        new(8, LinuxDesktopSettingSource.Fallback),
        new(1, LinuxDesktopSettingSource.Fallback),
        new(31, LinuxDesktopSettingSource.Fallback),
        new(3, LinuxDesktopSettingSource.Fallback),
        new(false, LinuxDesktopSettingSource.Fallback),
        new(400, LinuxDesktopSettingSource.Fallback));
}

internal sealed record LinuxInputSettings
{
    internal int? EnvironmentDoubleClickTime { get; init; }
    internal int? EnvironmentDoubleClickDistance { get; init; }
    internal int? EnvironmentDragThreshold { get; init; }
    internal int? EnvironmentKeyboardDelayMilliseconds { get; init; }
    internal double? EnvironmentKeyboardRepeatRate { get; init; }
    internal int? EnvironmentWheelScrollLines { get; init; }
    internal bool? EnvironmentSwapButtons { get; init; }
    internal int? EnvironmentMenuShowDelay { get; init; }
    internal int? XSettingsDoubleClickTime { get; init; }
    internal int? XSettingsDoubleClickDistance { get; init; }
    internal int? XSettingsDragThreshold { get; init; }
    internal int? XSettingsMenuShowDelay { get; init; }
    internal int? GnomeDoubleClickTime { get; init; }
    internal int? GnomeDragThreshold { get; init; }
    internal int? GnomeKeyboardDelayMilliseconds { get; init; }
    internal int? GnomeKeyboardRepeatIntervalMilliseconds { get; init; }
    internal bool? GnomeSwapButtons { get; init; }
    internal int? KdeDoubleClickTime { get; init; }
    internal int? KdeDragThreshold { get; init; }
    internal int? KdeKeyboardDelayMilliseconds { get; init; }
    internal double? KdeKeyboardRepeatRate { get; init; }
    internal int? KdeWheelScrollLines { get; init; }
    internal bool? KdeSwapButtons { get; init; }
    internal int? KdeMenuShowDelay { get; init; }
}

internal readonly record struct LinuxSystemFontDescription(
    string Family,
    double Size,
    int Weight,
    bool Italic);

internal sealed record LinuxSystemFontSettings(
    LinuxDesktopSetting<LinuxSystemFontDescription> Caption,
    LinuxDesktopSetting<LinuxSystemFontDescription> SmallCaption,
    LinuxDesktopSetting<LinuxSystemFontDescription> Menu,
    LinuxDesktopSetting<LinuxSystemFontDescription> Status,
    LinuxDesktopSetting<LinuxSystemFontDescription> Message,
    LinuxDesktopSetting<LinuxSystemFontDescription> Icon);

internal sealed record LinuxSystemFontCandidates
{
    internal LinuxSystemFontDescription? EnvironmentGeneral { get; init; }
    internal LinuxSystemFontDescription? EnvironmentCaption { get; init; }
    internal LinuxSystemFontDescription? EnvironmentSmallCaption { get; init; }
    internal LinuxSystemFontDescription? EnvironmentMenu { get; init; }
    internal LinuxSystemFontDescription? EnvironmentStatus { get; init; }
    internal LinuxSystemFontDescription? EnvironmentMessage { get; init; }
    internal LinuxSystemFontDescription? EnvironmentIcon { get; init; }
    internal LinuxSystemFontDescription? XSettingsGeneral { get; init; }
    internal LinuxSystemFontDescription? GnomeGeneral { get; init; }
    internal bool? GnomeTitlebarUsesSystemFont { get; init; }
    internal LinuxSystemFontDescription? GnomeTitlebar { get; init; }
    internal LinuxSystemFontDescription? KdeGeneral { get; init; }
    internal LinuxSystemFontDescription? KdeCaption { get; init; }
    internal LinuxSystemFontDescription? KdeSmallCaption { get; init; }
    internal LinuxSystemFontDescription? KdeMenu { get; init; }
    internal LinuxSystemFontDescription? KdeStatus { get; init; }
    internal LinuxSystemFontDescription? KdeIcon { get; init; }
}
