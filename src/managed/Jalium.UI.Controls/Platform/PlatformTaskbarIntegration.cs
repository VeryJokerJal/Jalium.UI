using Jalium.UI.Shell;

namespace Jalium.UI.Controls.Platform;

// The SDK package supplies the system surface; Window owns its lifetime.
internal interface IPlatformTaskbarIntegration : IDisposable
{
    void Refresh();
}

internal static class PlatformTaskbarIntegration
{
    internal static Func<Window, IPlatformTaskbarIntegration?>? Attach { get; set; }
}

/// <summary>A generation-bound subscription shared by native taskbar surfaces.</summary>
internal sealed class TaskbarItemInfoLease : IDisposable
{
    private Func<TaskbarItemInfo?>? _read;
    private Func<bool>? _available;
    private Action? _changed;
    private TaskbarItemInfo? _info;
    private ThumbButtonInfoCollection? _buttons;
    private long _modelEpoch;
    private readonly Dictionary<ThumbButtonInfo, long> _membership = new(ReferenceEqualityComparer.Instance);
    private long _membershipEpoch;
    private bool _disposed;

    internal TaskbarItemInfoLease(Func<TaskbarItemInfo?> read, Func<bool> available, Action changed)
    {
        _read = read;
        _available = available;
        _changed = changed;
    }

    internal long Revision { get; private set; }
    internal TaskbarItemInfo? Info => _info;

    internal void Refresh()
    {
        if (_disposed) return;
        var info = _read!();
        if (!ReferenceEquals(info, _info))
        {
            if (_info != null) _info.Changed -= OnChanged;
            _info = info;
            ++_modelEpoch;
            if (info != null) info.Changed += OnChanged;
        }
        if (!ReferenceEquals(_buttons, info?.ThumbButtonInfos))
        {
            _buttons = info?.ThumbButtonInfos;
            ++_modelEpoch;
        }
        var buttons = _buttons?.ToArray() ?? [];
        foreach (var previous in _membership.Keys.ToArray())
            if (!buttons.Any(button => ReferenceEquals(button, previous))) _membership.Remove(previous);
        foreach (var button in buttons)
            if (!_membership.ContainsKey(button)) _membership.Add(button, ++_membershipEpoch);
        ++Revision;
        _changed?.Invoke();
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    internal bool IsCurrent(long revision) => !_disposed && revision == Revision &&
        ReferenceEquals(_info, _read!()) && _available!();

    internal ButtonLease CaptureButton(ThumbButtonInfo button) => new(this, button);

    internal sealed class ButtonLease
    {
        private readonly TaskbarItemInfoLease _owner;
        private readonly WeakReference<ThumbButtonInfo> _button;
        private readonly long _epoch;
        private readonly long _membership;
        private readonly WeakReference<object>? _command, _parameter, _target;

        internal ButtonLease(TaskbarItemInfoLease owner, ThumbButtonInfo button)
        {
            _owner = owner; _button = new(button); _epoch = owner._modelEpoch;
            _membership = owner._membership.GetValueOrDefault(button);
            _command = Weak(button.Command); _parameter = Weak(button.CommandParameter); _target = Weak(button.CommandTarget);
        }

        private static WeakReference<object>? Weak(object? value) => value == null ? null : new(value);
        private static bool Matches(WeakReference<object>? expected, object? value) => expected == null
            ? value == null : expected.TryGetTarget(out var target) && ReferenceEquals(target, value);

        internal bool CanInvoke => !_owner._disposed && _button.TryGetTarget(out var button) &&
            _epoch == _owner._modelEpoch && ReferenceEquals(_owner._info, _owner._read!()) && _owner._available!() &&
            _membership != 0 && _owner._membership.GetValueOrDefault(button) == _membership &&
            ReferenceEquals(_owner._buttons, _owner._info?.ThumbButtonInfos) &&
            _owner._buttons?.Any(candidate => ReferenceEquals(candidate, button)) == true &&
            Matches(_command, button.Command) && Matches(_parameter, button.CommandParameter) &&
            Matches(_target, button.CommandTarget) && button.Visibility == Visibility.Visible &&
            button.IsEnabled && button.IsInteractive;

        internal void Invoke(Func<bool>? available = null)
        {
            if (available?.Invoke() != false && CanInvoke && _button.TryGetTarget(out var button))
                button.RaiseClickIfCurrent(() => available?.Invoke() != false && CanInvoke);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++Revision;
        if (_info != null) _info.Changed -= OnChanged;
        _info = null;
        _buttons = null;
        _membership.Clear();
        _read = null; _available = null; _changed = null;
    }
}
