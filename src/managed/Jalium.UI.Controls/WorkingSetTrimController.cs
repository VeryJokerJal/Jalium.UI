using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jalium.UI;

/// <summary>
/// Optional working-set pressure controller.
/// Keeps memory in check without fighting frame pacing.
/// </summary>
internal sealed class WorkingSetTrimController : IDisposable
{
    private const uint QuotaLimitHardWsMinEnable = 0x00000001;
    private const uint QuotaLimitHardWsMaxEnable = 0x00000004;

    private const int DefaultSoftTriggerMb = 512;
    private const int AutoGallerySoftTriggerMb = 384;
    private const int UltraSoftTriggerMb = 256;
    private const int HighPressureDeltaMb = 256;

    private const int TimerStartDelayMs = 3000;
    private const int TimerPeriodMs = 2000;

    private const int SoftTrimCooldownMs = 20_000;
    private const int HighTrimCooldownMs = 8000;
    private const int HardTrimCooldownMs = 3000;

    private const int SoftRequiredSamples = 3;
    private const int UltraRequiredSamples = 2;
    private const int HardRequiredSamples = 1;

    private const long MinHardWorkingSetFloorBytes = 16L * 1024L * 1024L;

    private readonly Timer _timer;
    private readonly nint _hardWorkingSetLimitBytes;
    private readonly long _softTrimTriggerBytes;
    private readonly long _highPressureBytes;
    private readonly int _requiredPressureSamples;

    private int _isTrimming;
    private int _consecutivePressureSamples;
    private long _lastTrimTickMs;
    private bool _disposed;

    private enum TrimProfile
    {
        Normal,
        Ultra
    }

    private WorkingSetTrimController(
        nint hardWorkingSetLimitBytes,
        long softTrimTriggerBytes,
        long highPressureBytes,
        int requiredPressureSamples)
    {
        _hardWorkingSetLimitBytes = hardWorkingSetLimitBytes;
        _softTrimTriggerBytes = softTrimTriggerBytes;
        _highPressureBytes = highPressureBytes;
        _requiredPressureSamples = requiredPressureSamples;

        _timer = new Timer(static state =>
        {
            if (state is WorkingSetTrimController controller)
            {
                controller.TrimOnce();
            }
        }, this, TimeSpan.FromMilliseconds(TimerStartDelayMs), TimeSpan.FromMilliseconds(TimerPeriodMs));
    }

    public static WorkingSetTrimController? TryCreateFromEnvironment()
    {
        var envValue = Environment.GetEnvironmentVariable("JALIUM_WORKING_SET_TRIM");
        var isGallery = IsGalleryProcess();

        if (string.IsNullOrWhiteSpace(envValue))
        {
            if (!isGallery)
            {
                return null;
            }

            // Gallery defaults to a soft auto mode.
            envValue = "auto";
        }

        if (IsDisableValue(envValue) || !IsEnableValue(envValue))
        {
            return null;
        }

        var profile = string.Equals(envValue, "ultra", StringComparison.OrdinalIgnoreCase)
            ? TrimProfile.Ultra
            : TrimProfile.Normal;

        nint hardLimitBytes = 0;
        if (TryParsePositiveIntEnvironmentVariable("JALIUM_WORKING_SET_LIMIT_MB", out var hardLimitMb))
        {
            hardLimitBytes = (nint)((long)hardLimitMb * 1024L * 1024L);
        }

        int softTriggerMb;
        if (TryParsePositiveIntEnvironmentVariable("JALIUM_WORKING_SET_TRIGGER_MB", out var configuredTriggerMb))
        {
            softTriggerMb = Math.Max(64, configuredTriggerMb);
        }
        else if (profile == TrimProfile.Ultra)
        {
            softTriggerMb = UltraSoftTriggerMb;
        }
        else if (isGallery && string.Equals(envValue, "auto", StringComparison.OrdinalIgnoreCase))
        {
            softTriggerMb = AutoGallerySoftTriggerMb;
        }
        else
        {
            softTriggerMb = DefaultSoftTriggerMb;
        }

        var softTriggerBytes = (long)softTriggerMb * 1024L * 1024L;
        var highPressureBytes = softTriggerBytes + (long)HighPressureDeltaMb * 1024L * 1024L;
        if (hardLimitBytes > 0)
        {
            // When hard cap is configured, treat cap+64MB as "must reclaim now".
            highPressureBytes = Math.Max(highPressureBytes, (long)hardLimitBytes + 64L * 1024L * 1024L);
        }

        var requiredSamples = hardLimitBytes > 0
            ? HardRequiredSamples
            : (profile == TrimProfile.Ultra ? UltraRequiredSamples : SoftRequiredSamples);

        return new WorkingSetTrimController(
            hardLimitBytes,
            softTriggerBytes,
            highPressureBytes,
            requiredSamples);
    }

    private static bool IsEnableValue(string value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "ultra", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisableValue(string value)
    {
        return string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParsePositiveIntEnvironmentVariable(string name, out int value)
    {
        value = 0;
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out value) && value > 0;
    }

    private static bool IsGalleryProcess()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var processName = process.ProcessName;
            if (!string.IsNullOrWhiteSpace(processName) &&
                processName.Contains("gallery", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
            // Ignore and fall back to command line probe.
        }

        var commandLine = Environment.CommandLine;
        return !string.IsNullOrWhiteSpace(commandLine)
            && commandLine.Contains("gallery", StringComparison.OrdinalIgnoreCase);
    }

    private void TrimOnce()
    {
        if (_disposed || Interlocked.Exchange(ref _isTrimming, 1) != 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();

            // Never trim while animation frame loop is active.
            // This is the biggest protection against frame-time spikes.
            if (CompositionTarget.IsActive)
            {
                _consecutivePressureSamples = 0;
                return;
            }

            var workingSetBytes = process.WorkingSet64;
            var overHardLimit = _hardWorkingSetLimitBytes > 0 &&
                workingSetBytes > (long)_hardWorkingSetLimitBytes;
            var overSoftTrigger = workingSetBytes >= _softTrimTriggerBytes;
            var highPressure = workingSetBytes >= _highPressureBytes;

            if (!overHardLimit && !overSoftTrigger)
            {
                _consecutivePressureSamples = 0;
                return;
            }

            _consecutivePressureSamples = Math.Min(
                _requiredPressureSamples,
                _consecutivePressureSamples + 1);

            if (_consecutivePressureSamples < _requiredPressureSamples)
            {
                return;
            }

            var now = Environment.TickCount64;
            var lastTrim = Volatile.Read(ref _lastTrimTickMs);
            var cooldownMs = overHardLimit
                ? HardTrimCooldownMs
                : (highPressure ? HighTrimCooldownMs : SoftTrimCooldownMs);

            if (now - lastTrim < cooldownMs)
            {
                return;
            }

            // Keep managed GC pressure trim lightweight; avoid blocking compacting collections.
            var gcGeneration = (highPressure || overHardLimit) ? 1 : 0;
            GC.Collect(gcGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);

            // OS working-set trim is expensive; only do it under high pressure or hard-limit overflow.
            if (highPressure || overHardLimit)
            {
                _ = EmptyWorkingSet(process.Handle);
            }

            if (_hardWorkingSetLimitBytes > 0)
            {
                var minBytes = (nint)Math.Max(
                    MinHardWorkingSetFloorBytes,
                    (long)_hardWorkingSetLimitBytes / 2);

                _ = SetProcessWorkingSetSizeEx(
                    process.Handle,
                    minBytes,
                    _hardWorkingSetLimitBytes,
                    QuotaLimitHardWsMinEnable | QuotaLimitHardWsMaxEnable);
            }

            Volatile.Write(ref _lastTrimTickMs, now);
            _consecutivePressureSamples = 0;
        }
        catch
        {
            // Best-effort memory trim. Ignore failures.
        }
        finally
        {
            Volatile.Write(ref _isTrimming, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSizeEx(
        nint hProcess,
        nint dwMinimumWorkingSetSize,
        nint dwMaximumWorkingSetSize,
        uint flags);
}
