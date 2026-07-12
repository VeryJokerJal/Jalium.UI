using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;

namespace Jalium.UI.Tests;

public class CompositionTargetInvalidationBatchTests
{
    [Fact]
    public void Rendering_ShouldBatchRepeatedVisualInvalidation_ForSameElement()
    {
        var host = new CountingWindowHost();
        var child = new Border();
        host.Child = child;
        host.ResetCounts();

        EventHandler handler = (_, _) =>
        {
            child.InvalidateVisual();
            child.InvalidateVisual();
        };

        CompositionTarget.Rendering += handler;

        try
        {
            var raiseRendering = typeof(CompositionTarget).GetMethod(
                "RaiseRendering",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(raiseRendering);
            raiseRendering!.Invoke(null, null);
        }
        finally
        {
            CompositionTarget.Rendering -= handler;
        }

        Assert.Equal(1, host.AddDirtyElementCount);
        Assert.Equal(1, host.InvalidateWindowCount);
    }

    private sealed class CountingWindowHost : Decorator, IWindowHost, ILayoutManagerHost
    {
        private readonly LayoutManager _layoutManager = new();
        private readonly HashSet<UIElement> _dirtyElements = [];
        private bool _windowInvalidated;

        public int AddDirtyElementCount { get; private set; }
        public int InvalidateWindowCount { get; private set; }

        LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

        public void AddDirtyElement(UIElement element)
        {
            if (_dirtyElements.Add(element))
                AddDirtyElementCount++;
        }

        public void InvalidateWindow()
        {
            if (!_windowInvalidated)
            {
                _windowInvalidated = true;
                InvalidateWindowCount++;
            }
        }

        public void RequestFullInvalidation()
        {
        }

        public void SetNativeCapture()
        {
        }

        public void ReleaseNativeCapture()
        {
        }

        public void ResetCounts()
        {
            AddDirtyElementCount = 0;
            InvalidateWindowCount = 0;
            _dirtyElements.Clear();
            _windowInvalidated = false;
        }
    }
}
