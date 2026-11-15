namespace Jalium.UI.Rendering;

// A visual cache, recorded parent, or queued frame owns a reference. Replaying
// may borrow one temporarily so reentrant invalidation cannot recycle buffers.
// Other render-cache hosts keep their existing GC-owned opaque handles.
internal interface IRetainedDrawingLifetime
{
    void Retain();
    void Release();
}

internal static class DrawingLifetime
{
    internal static void Retain(object? drawing)
    {
        if (drawing is IRetainedDrawingLifetime lifetime) lifetime.Retain();
    }

    internal static void Release(object? drawing)
    {
        if (drawing is IRetainedDrawingLifetime lifetime) lifetime.Release();
    }
}
