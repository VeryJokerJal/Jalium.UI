using Jalium.UI.Media;
namespace Jalium.UI.Controls;

/// <summary>Template-created editor surface. Text, layout, caret and editing state remain on the owner.</summary>
public sealed class EditControlView : FrameworkElement
{
    public EditControlView() => Focusable = false;
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner), typeof(EditControl), typeof(EditControlView), new PropertyMetadata(null, (d, _) =>
        {
            var view=(EditControlView)d;view.InvalidateMeasure();view.InvalidateVisual();
        }));
    public EditControl? Owner { get=>(EditControl?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value); }
    protected override Size MeasureOverride(Size availableSize)=>new(double.IsFinite(availableSize.Width)?availableSize.Width:0,double.IsFinite(availableSize.Height)?availableSize.Height:0);
    protected override void OnRender(DrawingContext drawingContext)=>Owner?.RenderEditorContent(drawingContext);
    protected override bool ParticipatesInRenderCache=>false;
}
