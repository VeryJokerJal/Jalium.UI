using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    public static IEnumerable<object[]> ProviderTransitions()
    {
        foreach (string kind in new[] { "owner", "overlay", "native" })
        foreach (bool duringActivation in new[] { false, true })
        foreach (string transition in new[] { "same", "custom-back", "null-back", "custom" })
            yield return [kind, duringActivation, transition];
    }

    [Theory]
    [MemberData(nameof(ProviderTransitions))]
    public void ProviderReplacementRetiresQueuedAccessibilityFocus(
        string kind, bool duringActivation, string transition)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        var original = FocusService.Provider;
        Assert.NotNull(original);
        void Change()
        {
            switch (transition)
            {
                case "same":
                    FocusService.Provider = original;
                    break;
                case "custom-back":
                    FocusService.Provider = new CustomFocusProvider { Current = h.Before };
                    FocusService.Provider = original;
                    break;
                case "null-back":
                    FocusService.Provider = null;
                    FocusService.Provider = original;
                    break;
                case "custom":
                    FocusService.Provider = new CustomFocusProvider { Current = h.Before };
                    break;
            }
        }
        try
        {
            if (duringActivation) h.SetActivation(() => { Change(); return true; });
            h.Accept();
            if (!duringActivation) Change();
            h.Process();
            bool current = transition == "same";
            Assert.Equal(current || duringActivation ? 1 : 0, h.ActivationCalls);
            Assert.Same(current ? h.Target : h.Before, FocusService.FocusedElement);
            if (transition != "custom") Assert.Same(original, FocusService.Provider);
        }
        finally
        {
            FocusService.Provider = original;
        }
    }

    [Theory]
    [InlineData("owner", false)] [InlineData("overlay", false)] [InlineData("native", false)]
    [InlineData("owner", true)] [InlineData("overlay", true)] [InlineData("native", true)]
    public void FocusTraversalAwayAndBackRetiresQueuedAccessibilityFocus(string kind, bool duringActivation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind);
        var original = FocusService.Provider;
        var provider = new TraversingProvider(h.Before, h.Other);
        FocusService.Provider = provider;
        void Change()
        {
            Assert.True(FocusService.MoveFocus(h.Before, FocusNavigationDirection.Next));
            Assert.Same(h.Other, FocusService.FocusedElement);
            Assert.True(FocusService.MoveFocus(h.Other, FocusNavigationDirection.Previous));
            Assert.Same(h.Before, FocusService.FocusedElement);
        }
        try
        {
            if (duringActivation) h.SetActivation(() => { Change(); return true; });
            h.Accept();
            if (!duringActivation) Change();
            h.Process();
            Assert.Equal(duringActivation ? 1 : 0, h.ActivationCalls);
            Assert.Same(h.Before, FocusService.FocusedElement);
            Assert.Same(provider, FocusService.Provider);
        }
        finally
        {
            FocusService.Provider = original;
        }
    }

    private sealed class TraversingProvider(IInputElement before, IInputElement other) : IFocusProvider
    {
        private IInputElement? _current = before;
        public IInputElement? FocusedElement => _current;
        public IInputElement? Focus(IInputElement? element) => _current = element;
        public void ClearFocus() => _current = null;
        public bool MoveFocus(UIElement element, FocusNavigationDirection direction)
        {
            _current = direction == FocusNavigationDirection.Next ? other : before;
            return true;
        }
    }
}
