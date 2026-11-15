using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Describes a console command submitted through the editor.</summary>
public sealed class DevToolsReplCommandEventArgs(RoutedEvent routedEvent, object source, string commandText) : RoutedEventArgs(routedEvent, source)
{
    public string CommandText { get; } = commandText;
}

/// <summary>A template-driven console with native text editing and bounded command history.</summary>
[TemplatePart(Name = "PART_Terminal", Type = typeof(Terminal))]
[TemplatePart(Name = "PART_InputTextBox", Type = typeof(TextBox))]
[TemplateVisualState(Name = "OutputAvailable", GroupName = "OutputStates")]
[TemplateVisualState(Name = "OutputEmpty", GroupName = "OutputStates")]
[TemplateVisualState(Name = "Editable", GroupName = "InputStates")]
[TemplateVisualState(Name = "ReadOnly", GroupName = "InputStates")]
[TemplateVisualState(Name = "Ready", GroupName = "ExecutionStates")]
[TemplateVisualState(Name = "Executing", GroupName = "ExecutionStates")]
[TemplateVisualState(Name = "NoError", GroupName = "ErrorStates")]
[TemplateVisualState(Name = "Error", GroupName = "ErrorStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsReplView : Control
{
    private Terminal? _terminal;
    private TextBox? _inputBox;
    private readonly List<string> _history = [];
    private readonly ReadOnlyCollection<string> _historyView;
    private readonly Queue<string> _transcript = new();
    private int _historyCursor = -1;
    private string _historyDraft = "";
    private bool _settingHistory;
    private EventHandler? _executeChanged;
    private DispatcherOperation? _reflowOperation;
    private readonly RelayCommand _submitCommand, _clearCommand;

    static DevToolsReplView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsReplView), new PropertyMetadata(typeof(DevToolsReplView)));
    private static DependencyProperty Property(string name, Type type, object? value, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsReplView), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty InputTextProperty = DependencyProperty.Register(nameof(InputText), typeof(string), typeof(DevToolsReplView),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnInputChanged, (_, value) => SingleLine(value as string)));
    public static readonly DependencyProperty PromptProperty = Property(nameof(Prompt), typeof(string), "devtools>");
    public static readonly DependencyProperty ContextTextProperty = Property(nameof(ContextText), typeof(string), "$ · window · app · root");
    public static readonly DependencyProperty IsReadOnlyProperty = Property(nameof(IsReadOnly), typeof(bool), false, OnStateChanged);
    public static readonly DependencyProperty ExecuteCommandProperty = Property(nameof(ExecuteCommand), typeof(ICommand), null, OnExecuteChanged);
    public static readonly DependencyProperty PromptBrushProperty = Property(nameof(PromptBrush), typeof(Brush), null);
    public static readonly DependencyProperty ResultBrushProperty = Property(nameof(ResultBrush), typeof(Brush), null);
    public static readonly DependencyProperty ErrorBrushProperty = Property(nameof(ErrorBrush), typeof(Brush), null);
    public static readonly DependencyProperty HistoryCapacityProperty = DependencyProperty.Register(nameof(HistoryCapacity), typeof(int), typeof(DevToolsReplView),
        new PropertyMetadata(200, (d, _) => ((DevToolsReplView)d).TrimHistory()), value => value is int capacity && capacity >= 0);
    /// <summary>Limits the number of complete output writes replayed after replacing the template.</summary>
    public static readonly DependencyProperty TranscriptCapacityProperty = DependencyProperty.Register(nameof(TranscriptCapacity), typeof(int), typeof(DevToolsReplView),
        new PropertyMetadata(512, (d, _) => ((DevToolsReplView)d).TrimTranscript()), value => value is int capacity && capacity >= 0);
    public static readonly DependencyProperty MaxScrollbackLinesProperty = DependencyProperty.Register(nameof(MaxScrollbackLines), typeof(int), typeof(DevToolsReplView),
        new PropertyMetadata(2000), value => value is int lines && lines >= 0);
    private static readonly DependencyPropertyKey HistoryCountPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HistoryCount), typeof(int), typeof(DevToolsReplView), new PropertyMetadata(0));
    public static readonly DependencyProperty HistoryCountProperty = HistoryCountPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasOutputPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasOutput), typeof(bool), typeof(DevToolsReplView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasOutputProperty = HasOutputPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsExecutingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsExecuting), typeof(bool), typeof(DevToolsReplView), new PropertyMetadata(false));
    public static readonly DependencyProperty IsExecutingProperty = IsExecutingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanSubmitPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanSubmit), typeof(bool), typeof(DevToolsReplView), new PropertyMetadata(false));
    public static readonly DependencyProperty CanSubmitProperty = CanSubmitPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey LastErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(LastError), typeof(string), typeof(DevToolsReplView), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty LastErrorProperty = LastErrorPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey SubmitCommandPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubmitCommand), typeof(ICommand), typeof(DevToolsReplView), new PropertyMetadata(null));
    public static readonly DependencyProperty SubmitCommandProperty = SubmitCommandPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ClearCommandPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ClearCommand), typeof(ICommand), typeof(DevToolsReplView), new PropertyMetadata(null));
    public static readonly DependencyProperty ClearCommandProperty = ClearCommandPropertyKey.DependencyProperty;
    public static readonly RoutedEvent CommandSubmittedEvent = EventManager.RegisterRoutedEvent(nameof(CommandSubmitted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsReplView));

    public string InputText { get => (string?)GetValue(InputTextProperty) ?? ""; set => SetValue(InputTextProperty, value); }
    public string Prompt { get => (string?)GetValue(PromptProperty) ?? ""; set => SetValue(PromptProperty, value); }
    public string ContextText { get => (string?)GetValue(ContextTextProperty) ?? ""; set => SetValue(ContextTextProperty, value); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty)!; set => SetValue(IsReadOnlyProperty, value); }
    public ICommand? ExecuteCommand { get => (ICommand?)GetValue(ExecuteCommandProperty); set => SetValue(ExecuteCommandProperty, value); }
    public Brush? PromptBrush { get => (Brush?)GetValue(PromptBrushProperty); set => SetValue(PromptBrushProperty, value); }
    public Brush? ResultBrush { get => (Brush?)GetValue(ResultBrushProperty); set => SetValue(ResultBrushProperty, value); }
    public Brush? ErrorBrush { get => (Brush?)GetValue(ErrorBrushProperty); set => SetValue(ErrorBrushProperty, value); }
    public int HistoryCapacity { get => (int)GetValue(HistoryCapacityProperty)!; set => SetValue(HistoryCapacityProperty, value); }
    public int TranscriptCapacity { get => (int)GetValue(TranscriptCapacityProperty)!; set => SetValue(TranscriptCapacityProperty, value); }
    public int MaxScrollbackLines { get => (int)GetValue(MaxScrollbackLinesProperty)!; set => SetValue(MaxScrollbackLinesProperty, value); }
    public int HistoryCount => (int)GetValue(HistoryCountProperty)!;
    public bool HasOutput => (bool)GetValue(HasOutputProperty)!;
    public bool IsExecuting => (bool)GetValue(IsExecutingProperty)!;
    public bool CanSubmit => (bool)GetValue(CanSubmitProperty)!;
    public string LastError => (string)GetValue(LastErrorProperty)!;
    public ICommand SubmitCommand => (ICommand)GetValue(SubmitCommandProperty)!;
    public ICommand ClearCommand => (ICommand)GetValue(ClearCommandProperty)!;
    public IReadOnlyList<string> History => _historyView;
    public event RoutedEventHandler CommandSubmitted { add => AddHandler(CommandSubmittedEvent, value); remove => RemoveHandler(CommandSubmittedEvent, value); }

    public DevToolsReplView()
    {
        _historyView = _history.AsReadOnly();
        _submitCommand = new RelayCommand(() => Submit(), () => CanSubmit);
        _clearCommand = new RelayCommand(Clear, () => IsEnabled && HasOutput);
        SetValue(SubmitCommandPropertyKey, _submitCommand); SetValue(ClearCommandPropertyKey, _clearCommand);
    }
    public override void OnApplyTemplate()
    {
        DetachParts(); base.OnApplyTemplate();
        _terminal = GetTemplateChild("PART_Terminal") as Terminal;
        _inputBox = GetTemplateChild("PART_InputTextBox") as TextBox;
        if (_inputBox != null) _inputBox.PreviewKeyDown += OnInputKeyDown;
        if (_terminal != null) { _terminal.PropertyChangedInternal += OnTerminalPropertyChanged; ReplayOutput(); }
        SetValue(HasOutputPropertyKey, _transcript.Count > 0); UpdateCommands();
    }
    protected override void OnTemplateChanging() { DetachParts(); base.OnTemplateChanging(); }
    private void DetachParts()
    {
        _reflowOperation?.Abort(); _reflowOperation = null;
        if (_inputBox != null) _inputBox.PreviewKeyDown -= OnInputKeyDown;
        if (_terminal != null) _terminal.PropertyChangedInternal -= OnTerminalPropertyChanged;
        _inputBox = null; _terminal = null;
    }
    private void ReplayOutput() { if (_terminal != null) { _terminal.Clear(); foreach (var text in _transcript) _terminal.Write(text); } }
    private void OnTerminalPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        if (property != Terminal.TerminalColumnsProperty && property != Terminal.TerminalRowsProperty || _reflowOperation != null) return;
        // A console owns complete output writes. Reparse them at the final cell size
        // after layout, since resizing a terminal's screen buffer clips its old cells.
        var terminal = _terminal;
        _reflowOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _reflowOperation = null;
            if (ReferenceEquals(terminal, _terminal)) ReplayOutput();
        });
    }
    public bool FocusInput() { ApplyTemplate(); return _inputBox?.Focus() == true; }

    /// <summary>Appends terminal output, retaining complete writes for a future template instance.</summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _transcript.Enqueue(text); TrimTranscript(); _terminal?.Write(text);
        SetValue(HasOutputPropertyKey, true); UpdateCommands();
    }
    public void WriteResult(string value) => Write(Colorize("=> " + value, ResultBrush) + "\r\n");
    public void WriteError(string message) { SetValue(LastErrorPropertyKey, message); Write(Colorize("E: " + message, ErrorBrush) + "\r\n"); }
    private static string Colorize(string text, Brush? brush) => brush is SolidColorBrush solid
        ? $"\x1b[38;2;{solid.Color.R};{solid.Color.G};{solid.Color.B}m{text}\x1b[0m" : text;

    public bool Submit()
    {
        UpdateCommands(); if (!CanSubmit) return false;
        string code = InputText;
        SetValue(IsExecutingPropertyKey, true); SetValue(LastErrorPropertyKey, string.Empty);
        if (HistoryCapacity > 0) { _history.Add(code); TrimHistory(); }
        _historyCursor = -1; _historyDraft = "";
        Write(Colorize(Prompt + " " + code, PromptBrush) + "\r\n");
        SetCurrentValue(InputTextProperty, string.Empty); UpdateCommands();
        try
        {
            var args = new DevToolsReplCommandEventArgs(CommandSubmittedEvent, this, code); RaiseEvent(args);
            if (!args.Handled) ExecuteCommand?.Execute(code);
        }
        catch (Exception ex) { SetValue(LastErrorPropertyKey, ex.Message); WriteError(ex.Message); }
        finally { SetValue(IsExecutingPropertyKey, false); UpdateCommands(); }
        return true;
    }
    public void Clear()
    {
        _transcript.Clear(); _terminal?.Clear(); SetValue(HasOutputPropertyKey, false);
        SetValue(LastErrorPropertyKey, string.Empty); UpdateCommands();
    }
    public void CancelInput()
    {
        if (IsReadOnly || !IsEnabled || IsExecuting) return;
        if (InputText.Length > 0) Write(Colorize(Prompt + " " + InputText + " ^C", PromptBrush) + "\r\n");
        SetCurrentValue(InputTextProperty, string.Empty); _historyCursor = -1; _historyDraft = "";
    }
    public bool RecallHistory(int direction)
    {
        if (IsReadOnly || !IsEnabled || IsExecuting || _history.Count == 0 || direction == 0) return false;
        if (direction < 0)
        {
            if (_historyCursor < 0) { _historyDraft = InputText; _historyCursor = _history.Count - 1; }
            else _historyCursor = Math.Max(0, _historyCursor - 1);
        }
        else
        {
            if (_historyCursor < 0) return false;
            if (++_historyCursor >= _history.Count) _historyCursor = -1;
        }
        _settingHistory = true;
        try { SetCurrentValue(InputTextProperty, _historyCursor >= 0 ? _history[_historyCursor] : _historyDraft); }
        finally { _settingHistory = false; }
        if (_inputBox != null) { _inputBox.CaretIndex = InputText.Length; _inputBox.SelectionLength = 0; }
        return true;
    }
    private void TrimHistory()
    {
        if (_history.Count > HistoryCapacity) _history.RemoveRange(0, _history.Count - HistoryCapacity);
        _historyCursor = -1; _historyDraft = ""; SetValue(HistoryCountPropertyKey, _history.Count);
    }
    private void TrimTranscript() { while (_transcript.Count > TranscriptCapacity) _transcript.Dequeue(); }
    private static string SingleLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = new StringBuilder(text.Length);
        foreach (char value in text) if (!char.IsControl(value)) result.Append(value); else if (value is '\r' or '\n' or '\t') result.Append(' ');
        return result.ToString();
    }
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(sender, _inputBox) || !IsEnabled || _inputBox.IsImeComposing) return;
        bool primary = PointerSelectionModifiers.IsToggle(e.KeyboardModifiers);
        if (e.Key == Key.Enter) { Submit(); e.Handled = true; }
        else if (e.Key == Key.Escape || primary && e.Key == Key.C && _inputBox?.SelectionLength == 0) { CancelInput(); e.Handled = true; }
        else if (primary && e.Key == Key.L) { Clear(); e.Handled = true; }
        else if (e.KeyboardModifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down) { RecallHistory(e.Key == Key.Up ? -1 : 1); e.Handled = true; }
    }
    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsReplView)d;
        if (!view._settingHistory) { view._historyCursor = -1; view._historyDraft = ""; }
        view.SetValue(LastErrorPropertyKey, string.Empty); view.UpdateCommands();
    }
    private static void OnExecuteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsReplView)d;
        if (e.OldValue is ICommand old && view._executeChanged != null) old.CanExecuteChanged -= view._executeChanged;
        view._executeChanged = null;
        if (e.NewValue is ICommand command)
        {
            var weak = new WeakReference<DevToolsReplView>(view);
            EventHandler? handler = null;
            handler = (sender, _) => { if (weak.TryGetTarget(out var owner)) owner.UpdateCommands(); else if (sender is ICommand retired) retired.CanExecuteChanged -= handler; };
            command.CanExecuteChanged += handler; view._executeChanged = handler;
        }
        view.UpdateCommands();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsReplView)d).UpdateCommands();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == IsEnabledProperty) UpdateCommands(); }
    private void UpdateCommands()
    {
        bool available = IsEnabled && !IsReadOnly && !IsExecuting && !string.IsNullOrWhiteSpace(InputText) && (ExecuteCommand?.CanExecute(InputText) ?? true);
        SetValue(CanSubmitPropertyKey, available); _submitCommand?.RaiseCanExecuteChanged(); _clearCommand?.RaiseCanExecuteChanged(); UpdateStates();
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, HasOutput ? "OutputAvailable" : "OutputEmpty", false);
        VisualStateManager.GoToState(this, IsReadOnly ? "ReadOnly" : "Editable", false);
        VisualStateManager.GoToState(this, IsExecuting ? "Executing" : "Ready", false);
        VisualStateManager.GoToState(this, LastError.Length > 0 ? "Error" : "NoError", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ReplPeer(this);
    private sealed class ReplPeer(DevToolsReplView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsReplView);
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Expression console";
    }
}
