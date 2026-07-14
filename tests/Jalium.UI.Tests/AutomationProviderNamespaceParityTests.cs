using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Text;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class AutomationProviderNamespaceParityTests
{
    private static readonly string[] s_retiredRootProviderTypes =
    [
        "IInvokeProvider",
        "IToggleProvider",
        "IValueProvider",
        "IRangeValueProvider",
        "IExpandCollapseProvider",
        "ISelectionProvider",
        "ISelectionItemProvider",
        "IScrollProvider",
        "IScrollItemProvider",
        "ITextProvider",
    ];

    [Fact]
    public void ProviderContracts_HaveOneCanonicalPublicNamespace()
    {
        Assembly assembly = typeof(IInvokeProvider).Assembly;
        foreach (string name in s_retiredRootProviderTypes)
            Assert.Null(assembly.GetType($"Jalium.UI.Automation.{name}"));

        Assert.Equal("Jalium.UI.Automation.Provider", typeof(IInvokeProvider).Namespace);
        Assert.Equal("Jalium.UI.Automation.Provider", typeof(ITextProvider).Namespace);
        Assert.Empty(typeof(IInvokeProvider).GetInterfaces());
        Assert.Empty(typeof(IValueProvider).GetInterfaces());
        Assert.Empty(typeof(ITextProvider).GetInterfaces());
    }

    [Fact]
    public void RawProviderSurface_MatchesWpfTypeShape()
    {
        Type simple = typeof(IRawElementProviderSimple);
        Assert.Equal(typeof(ProviderOptions), simple.GetProperty(nameof(IRawElementProviderSimple.ProviderOptions))!.PropertyType);
        Assert.Equal(typeof(IRawElementProviderSimple), simple.GetProperty(nameof(IRawElementProviderSimple.HostRawElementProvider))!.PropertyType);
        Assert.Equal(typeof(object), simple.GetMethod(nameof(IRawElementProviderSimple.GetPatternProvider))!.ReturnType);
        Assert.Equal(typeof(object), simple.GetMethod(nameof(IRawElementProviderSimple.GetPropertyValue))!.ReturnType);

        Assert.Contains(typeof(IRawElementProviderSimple), typeof(IRawElementProviderFragment).GetInterfaces());
        Assert.Contains(typeof(IRawElementProviderFragment), typeof(IRawElementProviderFragmentRoot).GetInterfaces());

        Assert.Equal(0, (int)NavigateDirection.Parent);
        Assert.Equal(4, (int)NavigateDirection.LastChild);
        Assert.Equal(0x20, (int)ProviderOptions.UseComThreading);
    }

    [Fact]
    public void Win32ComAbi_IsNotPartOfThePublicManagedApi()
    {
        string abiNamespace = "Jalium.UI.Controls.Automation.Uia";
        Type[] leaked = typeof(Window).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == abiNamespace)
            .ToArray();

        Assert.Empty(leaked);
    }

    [Fact]
    public void TextBoxTextPattern_UsesCanonicalRangesAndPreservesSelection()
    {
        var textBox = new TextBox { Text = "hello world" };
        textBox.Select(1, 4);

        AutomationPeer peer = Assert.IsType<TextBoxAutomationPeer>(textBox.GetAutomationPeer());
        ITextProvider provider = Assert.IsAssignableFrom<ITextProvider>(peer.GetPattern(PatternInterface.Text));

        Assert.Equal("hello world", provider.DocumentRange.GetText(-1));
        ITextRangeProvider selection = Assert.Single(provider.GetSelection());
        Assert.Equal("ello", selection.GetText(-1));
        Assert.Equal(SupportedTextSelection.Single, provider.SupportedTextSelection);

        ITextRangeProvider? found = provider.DocumentRange.FindText("world", backward: false, ignoreCase: false);
        Assert.NotNull(found);
        Assert.Equal("world", found!.GetText(-1));

        ITextRangeProvider firstWord = provider.DocumentRange;
        firstWord.ExpandToEnclosingUnit(TextUnit.Word);
        Assert.Equal("hello", firstWord.GetText(-1));
    }
}
