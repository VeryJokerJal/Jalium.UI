using Jalium.UI.Controls.Platform;

namespace Jalium.UI;

public partial class Application
{
    private IDisposable? _platformAppearanceMonitor;
    private bool _startingAppearanceMonitor;

    internal void EnsurePlatformAppearanceMonitoring()
    {
        if (!OperatingSystem.IsMacOS() || IsHostedStopping || _platformAppearanceMonitor != null || _startingAppearanceMonitor) return;
        VerifyAccess();
        _startingAppearanceMonitor = true;
        try
        {
            var monitor = PlatformThemeIntegration.MonitorAppearance?.Invoke();
            if (IsHostedStopping || !ReferenceEquals(Current, this)) monitor?.Dispose();
            else _platformAppearanceMonitor = monitor;
        }
        finally { _startingAppearanceMonitor = false; }
    }

    private void StopPlatformAppearanceMonitoring() =>
        Interlocked.Exchange(ref _platformAppearanceMonitor, null)?.Dispose();
}
