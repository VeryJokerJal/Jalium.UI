using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

/// <summary>Maps native media playback and sound state to CSS resource selectors.</summary>
internal static class CssMediaPlaybackState
{
    internal static bool IsPlaying(CssNode element) =>
        element.Target is MediaElement { IsPlaying: true };

    internal static bool IsPaused(CssNode element) =>
        element.Target is MediaElement { IsPlaying: false };

    internal static bool IsBuffering(CssNode element) =>
        element.Target is MediaElement { IsPlaying: true, IsBuffering: true };

    internal static bool IsMuted(CssNode element) =>
        element.Target is MediaElement { IsMuted: true };

    internal static void NotifyStateChanged(MediaElement target, string property)
    {
        CssSelectorDependencies.NativeValueChanged(target, property);
        if (CssNode.Existing(target) is { } node)
            CssEvaluationScheduler.InvalidateSubtree(node);
    }
}
