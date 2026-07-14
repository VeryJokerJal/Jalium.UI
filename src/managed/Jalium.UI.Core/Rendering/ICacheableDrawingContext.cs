namespace Jalium.UI.Rendering;

/// <summary>
/// Marker implemented by drawing contexts that are safe to drive through
/// the retained-mode <see cref="IRenderCacheHost"/> cache. The cache only
/// activates when the live drawing context carries this marker; any other
/// context — including test probe contexts that intercept specific call
/// sites, or one-off custom contexts — falls through to the legacy
/// immediate-mode <c>OnRender</c> path.
/// </summary>
/// <remarks>
/// <para>
/// The marker exists because <c>OnRender(DrawingContext)</c> accepts any
/// concrete subclass and user code is free to pattern-match the argument
/// for context-specific logic (<c>if (drawingContext is not MyProbe) return;</c>).
/// Replacing such a context with a recorder on cached frames would cause
/// the pattern match to fail, silently dropping the intended draws on the
/// first frame and replaying nothing afterwards.
/// </para>
/// <para>
/// The live rendering path — <c>RenderTargetDrawingContext</c> — implements
/// this marker. New context types that want to benefit from caching
/// should implement it too; contexts that rely on being observed directly
/// from <c>OnRender</c> should not.
/// </para>
/// </remarks>
internal interface ICacheableDrawingContext
{
}
