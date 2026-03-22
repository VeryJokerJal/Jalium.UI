using System.Runtime.CompilerServices;

// Expose a minimal set of internals only to the Interop assembly (and tests)
// Interop needs access to some internal setters and helper APIs for native
// text/geometry interop. Avoid exposing internals to many assemblies to
// prevent type name collisions (e.g. RenderTargetDrawingContext).
[assembly: InternalsVisibleTo("Jalium.UI.Interop")]
[assembly: InternalsVisibleTo("Jalium.UI.Tests")]
