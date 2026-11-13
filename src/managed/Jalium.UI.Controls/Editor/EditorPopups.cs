using Jalium.UI.Interop;
using Jalium.UI.Controls.Primitives;
using System.Collections;
using System.Windows.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Presentation data for a completion entry; the command owns acceptance behavior.</summary>
public sealed class EditorCompletionRow(string label, string? detail, string kindText, ICommand acceptCommand)
{
    public string Label { get; } = label;
    public string Detail { get; } = detail ?? string.Empty;
    public string KindText { get; } = kindText;
    public ICommand AcceptCommand { get; } = acceptCommand;
}

/// <summary>A virtualized, themeable completion list using standard selection and item templates.</summary>
public sealed class EditorCompletionList : ListBox
{
    static EditorCompletionList() => DefaultStyleKeyProperty.OverrideMetadata(typeof(EditorCompletionList), new PropertyMetadata(typeof(EditorCompletionList)));
    public EditorCompletionList() => Focusable = false;
}

/// <summary>One plain or emphasized section of a signature label.</summary>
public sealed record EditorSignatureSegment(string Text, bool IsActive)
{
    public Thickness BorderThickness => new(IsActive ? 1 : 0);
}

/// <summary>A source line rendered with the editor syntax palette.</summary>
public readonly record struct EditorPreviewLine(int DocumentLineNumber, string Prefix, string Text);

[TemplatePart(Name = "PART_Text", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_Preview", Type = typeof(EditorSyntaxPreviewView))]
[TemplatePart(Name = "PART_Segments", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_Counter", Type = typeof(TextBlock))]
[TemplateVisualState(Name = "Text", GroupName = "ContentStates")]
[TemplateVisualState(Name = "Signature", GroupName = "ContentStates")]
[TemplateVisualState(Name = "Preview", GroupName = "ContentStates")]
[TemplateVisualState(Name = "CounterVisible", GroupName = "CounterStates")]
[TemplateVisualState(Name = "CounterHidden", GroupName = "CounterStates")]
public sealed class EditorTextHint : Control
{
    private EditorSyntaxPreviewView? _previewView;
    public static readonly DependencyProperty PreviewLinesProperty = DependencyProperty.Register(nameof(PreviewLines), typeof(IReadOnlyList<EditorPreviewLine>), typeof(EditorTextHint), new PropertyMetadata(null));
    public static readonly DependencyProperty SyntaxOwnerProperty = DependencyProperty.Register(nameof(SyntaxOwner), typeof(EditControl), typeof(EditorTextHint), new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewLineHeightProperty = DependencyProperty.Register(nameof(PreviewLineHeight), typeof(double), typeof(EditorTextHint), new PropertyMetadata(0.0));
    public IReadOnlyList<EditorPreviewLine>? PreviewLines { get => (IReadOnlyList<EditorPreviewLine>?)GetValue(PreviewLinesProperty); set => SetValue(PreviewLinesProperty, value); }
    public EditControl? SyntaxOwner { get => (EditControl?)GetValue(SyntaxOwnerProperty); set => SetValue(SyntaxOwnerProperty, value); }
    public double PreviewLineHeight { get => (double)GetValue(PreviewLineHeightProperty)!; set => SetValue(PreviewLineHeightProperty, value); }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditorTextHint), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(IEnumerable), typeof(EditorTextHint), new PropertyMetadata(null));
    public static readonly DependencyProperty CounterProperty = DependencyProperty.Register(nameof(Counter), typeof(string), typeof(EditorTextHint), new PropertyMetadata(string.Empty));
    public string Text { get => (string?)GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }
    public IEnumerable? Segments { get => (IEnumerable?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public string Counter { get => (string?)GetValue(CounterProperty) ?? string.Empty; set => SetValue(CounterProperty, value); }
    static EditorTextHint() => DefaultStyleKeyProperty.OverrideMetadata(typeof(EditorTextHint), new PropertyMetadata(typeof(EditorTextHint)));
    public EditorTextHint() => Focusable = false;
    public override void OnApplyTemplate()
    {
        ReleasePreview(); base.OnApplyTemplate(); _previewView=GetTemplateChild("PART_Preview") as EditorSyntaxPreviewView;
        _previewView?.SetCurrentValue(EditorSyntaxPreviewView.OwnerProperty,this);UpdatePresentation();
    }
    protected override void OnTemplateChanging(){ReleasePreview();base.OnTemplateChanging();}
    private void ReleasePreview(){if(_previewView is { } view){view.ClearBinding(EditorSyntaxPreviewView.OwnerProperty);view.SetCurrentValue(EditorSyntaxPreviewView.OwnerProperty,null);}_previewView=null;}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SegmentsProperty || e.Property == CounterProperty || e.Property == PreviewLinesProperty) UpdatePresentation();
        _previewView?.InvalidateMeasure();_previewView?.InvalidateVisual();
    }
    private void UpdatePresentation()
    {
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, PreviewLines?.Count > 0 ? "Preview" : Segments?.Cast<object>().Any() == true ? "Signature" : "Text", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Counter) ? "CounterHidden" : "CounterVisible", false);
    }
}

/// <summary>Renders syntax glyphs; EditorTextHint templates supply the frame and padding.</summary>
public sealed class EditorSyntaxPreviewView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(EditorTextHint),typeof(EditorSyntaxPreviewView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    public EditorTextHint? Owner{get=>(EditorTextHint?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public EditorSyntaxPreviewView()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size availableSize)
    {
        if(Owner is not {PreviewLines:{Count:>0} lines} hint)return default;
        var text=new FormattedText(string.Join("\n",lines.Select(line=>line.Prefix+line.Text)),hint.FontFamily?.GetRenderingSource(hint)??"monospace",hint.FontSize);
        TextMeasurement.MeasureText(text);
        var height=hint.PreviewLineHeight>0?hint.PreviewLineHeight:Math.Max(1,text.LineHeight);
        return new Size(Math.Min(availableSize.Width,text.Width),Math.Min(availableSize.Height,height*lines.Count));
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(Owner is not {SyntaxOwner:{ } editor,PreviewLines:{Count:>0} lines} hint)return;
        editor.DrawSyntaxHighlightedPreview(dc,lines,new Rect(RenderSize),hint.FontFamily?.GetRenderingSource(hint)??"monospace",hint.FontSize,hint.Foreground??Brushes.Black,hint.PreviewLineHeight>0?hint.PreviewLineHeight:Math.Max(1,hint.FontSize*1.2));
    }
}

// IList keeps the existing ItemsControl/virtualizing collection-view fast path.
// Rows and commands are allocated only for entries that consumers actually read.
internal sealed class EditorCompletionRows(int count, Func<int, EditorCompletionRow> factory) : IList
{
    private readonly Dictionary<int, EditorCompletionRow> _realized = new();
    public int Count => count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public object this[int index]
    {
        get
        {
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!_realized.TryGetValue(index, out var row)) _realized[index] = row = factory(index);
            return row;
        }
        set => throw new NotSupportedException();
    }
    public int IndexOf(object? value)
    {
        foreach (var pair in _realized) if (ReferenceEquals(pair.Value, value)) return pair.Key;
        return -1;
    }
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (var i = 0; i < count; i++) yield return this[i]; }
    public void CopyTo(Array array, int index) { for (var i = 0; i < count; i++) array.SetValue(this[i], index + i); }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
