using Jalium.UI.Styling;

namespace Jalium.UI.Media;

/// <summary>Transactional capture shared by element and background compositing.</summary>
internal static class CssBlendGroup
{
    internal sealed class Frame(DrawingContext drawing, IBlendDrawingContext context, Rect bounds, CssBlendMode mode) : IDisposable
    {
        private bool _open = true;
        internal void Complete(double opacity = 1)
        {
            _open = false;
            context.EndBlendCapture();
            if (opacity != 1) drawing.PushOpacity(opacity);
            try { context.CompositeBlendCapture(mode, bounds); }
            finally { if (opacity != 1) drawing.Pop(); }
        }
        public void Dispose() { if (!_open) return; _open = false; context.EndBlendCapture(); }
    }
    internal static Frame? Begin(DrawingContext context, Rect bounds, CssBlendMode mode, string property = "mix-blend-mode")
    {
        if (context is not IBlendDrawingContext { SupportsBlendModes: true } target)
        {
            CssDiagnostics.Report(property, CssDiagnosticReason.LossyConversion, null, "Drawing context does not support isolated blending groups");
            return null;
        }
        target.BeginBlendCapture(bounds); return new(context, target, bounds, mode);
    }
}
