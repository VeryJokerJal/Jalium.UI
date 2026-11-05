using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Diagnostics;

namespace Jalium.UI.Controls.DevTools;

public enum DevToolsToolMode { None, Ruler, ColorPicker }
public enum DevToolsToolAction { ToggleRuler, ToggleColorPicker, ToggleOverdraw, ToggleDirtyRegions, ToggleFocus, CopyArgb }
public sealed record DevToolsToolRequest(DevToolsToolAction Action, object? Parameter = null);

/// <summary>Template-owned diagnostic tools; the consumer owns target input and sampling.</summary>
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "NoTool", GroupName = "ToolStates")]
[TemplateVisualState(Name = "RulerTool", GroupName = "ToolStates")]
[TemplateVisualState(Name = "ColorPickerTool", GroupName = "ToolStates")]
[TemplateVisualState(Name = "MeasurementAvailable", GroupName = "MeasurementStates")]
[TemplateVisualState(Name = "MeasurementEmpty", GroupName = "MeasurementStates")]
[TemplateVisualState(Name = "ColorAvailable", GroupName = "ColorStates")]
[TemplateVisualState(Name = "ColorEmpty", GroupName = "ColorStates")]
[TemplateVisualState(Name = "NoOverlay", GroupName = "OverlayStates")]
[TemplateVisualState(Name = "OverdrawOverlay", GroupName = "OverlayStates")]
[TemplateVisualState(Name = "DirtyRegionsOverlay", GroupName = "OverlayStates")]
[TemplateVisualState(Name = "FocusTracking", GroupName = "FocusStates")]
[TemplateVisualState(Name = "FocusIdle", GroupName = "FocusStates")]
[TemplateVisualState(Name = "RulerError", GroupName = "RulerStatusStates")]
[TemplateVisualState(Name = "RulerNormal", GroupName = "RulerStatusStates")]
[TemplateVisualState(Name = "PickerError", GroupName = "PickerStatusStates")]
[TemplateVisualState(Name = "PickerNormal", GroupName = "PickerStatusStates")]
[TemplateVisualState(Name = "Error", GroupName = "ErrorStates")]
[TemplateVisualState(Name = "NoError", GroupName = "ErrorStates")]
[TemplateVisualState(Name = "StatusAvailable", GroupName = "StatusStates")]
[TemplateVisualState(Name = "StatusEmpty", GroupName = "StatusStates")]
[TemplateVisualState(Name = "Busy", GroupName = "ExecutionStates")]
[TemplateVisualState(Name = "Ready", GroupName = "ExecutionStates")]
public class DevToolsToolsView : Control
{
    private double _layoutWidth;
    private ToolCommand[] _commands = [];
    private EventHandler? _commandChanged;
    private bool _updatingCommands;
    static DevToolsToolsView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsToolsView), new PropertyMetadata(typeof(DevToolsToolsView)));
    private static DependencyProperty State(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(DevToolsToolsView), new PropertyMetadata(value, OnStateChanged));
    private static DependencyPropertyKey ReadOnly(string name, Type type, object? value) => DependencyProperty.RegisterReadOnly(name, type, typeof(DevToolsToolsView), new PropertyMetadata(value));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(DevToolsToolsView), new PropertyMetadata(null, OnCommandChanged));
    public static readonly DependencyProperty ToolModeProperty = DependencyProperty.Register(nameof(ToolMode), typeof(DevToolsToolMode), typeof(DevToolsToolsView), new FrameworkPropertyMetadata(DevToolsToolMode.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged), value => value is DevToolsToolMode mode && Enum.IsDefined(mode));
    public static readonly DependencyProperty OverlayModeProperty = DependencyProperty.Register(nameof(OverlayMode), typeof(RenderDiagnostics.OverlayMode), typeof(DevToolsToolsView), new FrameworkPropertyMetadata(RenderDiagnostics.OverlayMode.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged), value => value is RenderDiagnostics.OverlayMode mode && Enum.IsDefined(mode));
    public static readonly DependencyProperty IsFocusTrackingProperty = DependencyProperty.Register(nameof(IsFocusTracking), typeof(bool), typeof(DevToolsToolsView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged));
    public static readonly DependencyProperty MeasurementProperty = State(nameof(Measurement), typeof(DevToolsRulerMeasurement), null);
    public static readonly DependencyProperty ColorSampleProperty = State(nameof(ColorSample), typeof(DevToolsColorSample), null);
    public static readonly DependencyProperty RulerStatusTextProperty = State(nameof(RulerStatusText), typeof(string), "Choose two points");
    public static readonly DependencyProperty PickerStatusTextProperty = State(nameof(PickerStatusText), typeof(string), "Choose a pixel in the target");
    public static readonly DependencyProperty FocusedElementTextProperty = State(nameof(FocusedElementText), typeof(string), "No focused element");
    public static readonly DependencyProperty HasRulerErrorProperty = State(nameof(HasRulerError), typeof(bool), false);
    public static readonly DependencyProperty HasPickerErrorProperty = State(nameof(HasPickerError), typeof(bool), false);
    public static readonly DependencyProperty StatusTextProperty = State(nameof(StatusText), typeof(string), "");
    public static readonly DependencyProperty LastErrorProperty = State(nameof(LastError), typeof(string), "");
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsToolsView), new PropertyMetadata(760d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    private static readonly DependencyPropertyKey HasMeasurementPropertyKey = ReadOnly(nameof(HasMeasurement), typeof(bool), false);
    public static readonly DependencyProperty HasMeasurementProperty = HasMeasurementPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasColorSamplePropertyKey = ReadOnly(nameof(HasColorSample), typeof(bool), false);
    public static readonly DependencyProperty HasColorSampleProperty = HasColorSamplePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsExecutingPropertyKey = ReadOnly(nameof(IsExecuting), typeof(bool), false);
    public static readonly DependencyProperty IsExecutingProperty = IsExecutingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey RulerCommandPropertyKey = ReadOnly(nameof(RulerCommand), typeof(ICommand), null);
    public static readonly DependencyProperty RulerCommandProperty = RulerCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ColorPickerCommandPropertyKey = ReadOnly(nameof(ColorPickerCommand), typeof(ICommand), null);
    public static readonly DependencyProperty ColorPickerCommandProperty = ColorPickerCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey OverdrawCommandPropertyKey = ReadOnly(nameof(OverdrawCommand), typeof(ICommand), null);
    public static readonly DependencyProperty OverdrawCommandProperty = OverdrawCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey DirtyRegionsCommandPropertyKey = ReadOnly(nameof(DirtyRegionsCommand), typeof(ICommand), null);
    public static readonly DependencyProperty DirtyRegionsCommandProperty = DirtyRegionsCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey FocusCommandPropertyKey = ReadOnly(nameof(FocusCommand), typeof(ICommand), null);
    public static readonly DependencyProperty FocusCommandProperty = FocusCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CopyArgbCommandPropertyKey = ReadOnly(nameof(CopyArgbCommand), typeof(ICommand), null);
    public static readonly DependencyProperty CopyArgbCommandProperty = CopyArgbCommandPropertyKey.DependencyProperty;

    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public DevToolsToolMode ToolMode { get => (DevToolsToolMode)GetValue(ToolModeProperty)!; set => SetValue(ToolModeProperty, value); }
    public RenderDiagnostics.OverlayMode OverlayMode { get => (RenderDiagnostics.OverlayMode)GetValue(OverlayModeProperty)!; set => SetValue(OverlayModeProperty, value); }
    public bool IsFocusTracking { get => (bool)GetValue(IsFocusTrackingProperty)!; set => SetValue(IsFocusTrackingProperty, value); }
    public DevToolsRulerMeasurement? Measurement { get => (DevToolsRulerMeasurement?)GetValue(MeasurementProperty); set => SetValue(MeasurementProperty, value); }
    public DevToolsColorSample? ColorSample { get => (DevToolsColorSample?)GetValue(ColorSampleProperty); set => SetValue(ColorSampleProperty, value); }
    public string RulerStatusText { get => (string?)GetValue(RulerStatusTextProperty) ?? ""; set => SetValue(RulerStatusTextProperty, value); }
    public string PickerStatusText { get => (string?)GetValue(PickerStatusTextProperty) ?? ""; set => SetValue(PickerStatusTextProperty, value); }
    public string FocusedElementText { get => (string?)GetValue(FocusedElementTextProperty) ?? ""; set => SetValue(FocusedElementTextProperty, value); }
    public bool HasRulerError { get => (bool)GetValue(HasRulerErrorProperty)!; set => SetValue(HasRulerErrorProperty, value); }
    public bool HasPickerError { get => (bool)GetValue(HasPickerErrorProperty)!; set => SetValue(HasPickerErrorProperty, value); }
    public string StatusText { get => (string?)GetValue(StatusTextProperty) ?? ""; set => SetValue(StatusTextProperty, value); }
    public string LastError { get => (string?)GetValue(LastErrorProperty) ?? ""; set => SetValue(LastErrorProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public bool HasMeasurement => (bool)GetValue(HasMeasurementProperty)!;
    public bool HasColorSample => (bool)GetValue(HasColorSampleProperty)!;
    public bool IsExecuting => (bool)GetValue(IsExecutingProperty)!;
    public ICommand RulerCommand => (ICommand)GetValue(RulerCommandProperty)!;
    public ICommand ColorPickerCommand => (ICommand)GetValue(ColorPickerCommandProperty)!;
    public ICommand OverdrawCommand => (ICommand)GetValue(OverdrawCommandProperty)!;
    public ICommand DirtyRegionsCommand => (ICommand)GetValue(DirtyRegionsCommandProperty)!;
    public ICommand FocusCommand => (ICommand)GetValue(FocusCommandProperty)!;
    public ICommand CopyArgbCommand => (ICommand)GetValue(CopyArgbCommandProperty)!;

    public DevToolsToolsView()
    {
        var actions = Enum.GetValues<DevToolsToolAction>();
        _commands = actions.Select(action => new ToolCommand(this, action)).ToArray();
        SetValue(RulerCommandPropertyKey, _commands[0]); SetValue(ColorPickerCommandPropertyKey, _commands[1]);
        SetValue(OverdrawCommandPropertyKey, _commands[2]); SetValue(DirtyRegionsCommandPropertyKey, _commands[3]);
        SetValue(FocusCommandPropertyKey, _commands[4]); SetValue(CopyArgbCommandPropertyKey, _commands[5]);
    }
    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsToolsView)d;
        if (e.OldValue is ICommand old && view._commandChanged != null) old.CanExecuteChanged -= view._commandChanged;
        view._commandChanged = null;
        if (e.NewValue is ICommand current)
        {
            var weak = new WeakReference<DevToolsToolsView>(view);
            EventHandler? changed = null;
            changed = (_, _) => { if (weak.TryGetTarget(out var owner)) owner.UpdateCommands(); else current.CanExecuteChanged -= changed; };
            current.CanExecuteChanged += changed; view._commandChanged = changed;
        }
        view.UpdateCommands();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsToolsView)d;
        view.SetValue(HasMeasurementPropertyKey, view.Measurement != null);
        view.SetValue(HasColorSamplePropertyKey, view.ColorSample != null);
        view.UpdateCommands();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateCommands();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth;
        UpdateStates(); return base.MeasureOverride(availableSize);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateCommands(); }
    private bool CanInvoke(DevToolsToolRequest request, ICommand? command)
    {
        if (!IsEnabled || IsExecuting || command == null || request.Action == DevToolsToolAction.CopyArgb && !HasColorSample) return false;
        try { return command.CanExecute(request) && ReferenceEquals(command, ActionCommand); }
        catch { return false; }
    }
    private void Invoke(DevToolsToolRequest request)
    {
        var command = ActionCommand;
        if (!CanInvoke(request, command)) return;
        SetValue(IsExecutingPropertyKey, true); SetCurrentValue(LastErrorProperty, ""); UpdateCommands();
        try { command!.Execute(request); }
        catch (Exception error) { SetCurrentValue(LastErrorProperty, error.Message); }
        finally { SetValue(IsExecutingPropertyKey, false); UpdateCommands(); }
    }
    private void UpdateCommands()
    {
        if (_updatingCommands) return;
        _updatingCommands = true;
        try { foreach (var command in _commands) command.RaiseCanExecuteChanged(); }
        finally { _updatingCommands = false; }
        UpdateStates();
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        double width = _layoutWidth > 0 ? _layoutWidth : ActualWidth;
        VisualStateManager.GoToState(this, width > 0 && width <= CompactWidth ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, ToolMode == DevToolsToolMode.Ruler ? "RulerTool" : ToolMode == DevToolsToolMode.ColorPicker ? "ColorPickerTool" : "NoTool", false);
        VisualStateManager.GoToState(this, HasMeasurement ? "MeasurementAvailable" : "MeasurementEmpty", false);
        VisualStateManager.GoToState(this, HasColorSample ? "ColorAvailable" : "ColorEmpty", false);
        VisualStateManager.GoToState(this, OverlayMode == RenderDiagnostics.OverlayMode.Overdraw ? "OverdrawOverlay" : OverlayMode == RenderDiagnostics.OverlayMode.DirtyRegions ? "DirtyRegionsOverlay" : "NoOverlay", false);
        VisualStateManager.GoToState(this, IsFocusTracking ? "FocusTracking" : "FocusIdle", false);
        VisualStateManager.GoToState(this, HasRulerError ? "RulerError" : "RulerNormal", false);
        VisualStateManager.GoToState(this, HasPickerError ? "PickerError" : "PickerNormal", false);
        VisualStateManager.GoToState(this, LastError.Length > 0 ? "Error" : "NoError", false);
        VisualStateManager.GoToState(this, StatusText.Length > 0 ? "StatusAvailable" : "StatusEmpty", false);
        VisualStateManager.GoToState(this, IsExecuting ? "Busy" : "Ready", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ToolsPeer(this);
    private sealed class ToolsPeer(DevToolsToolsView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsToolsView);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Target window tools";
    }
    private sealed class ToolCommand(DevToolsToolsView owner, DevToolsToolAction action) : ICommand
    {
        private readonly WeakReference<DevToolsToolsView> _owner = new(owner);
        private readonly DevToolsToolRequest _request = new(action);
        public event EventHandler? CanExecuteChanged;
        private DevToolsToolRequest Request(object? parameter) => parameter == null ? _request : new(action, parameter);
        public bool CanExecute(object? parameter) => _owner.TryGetTarget(out var view) && view.CanInvoke(Request(parameter), view.ActionCommand);
        public void Execute(object? parameter) { if (_owner.TryGetTarget(out var view)) view.Invoke(Request(parameter)); }
        internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
