using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class ManagedAssemblyFacadeCompatibilityTests
{
    private const string ImplementationAssemblyName = "Jalium.UI.Managed";

    [Theory]
    [InlineData("Jalium.UI.Core", "Jalium.UI.FrameworkElement")]
    [InlineData("Jalium.UI.Media", "Jalium.UI.Media.RenderTargetBitmap")]
    [InlineData("Jalium.UI.Input", "Jalium.UI.Input.Keyboard")]
    [InlineData("Jalium.UI.Interop", "Jalium.UI.Interop.HwndSource")]
    [InlineData("Jalium.UI.Controls", "Jalium.UI.Controls.Button")]
    public void LegacyAssemblyQualifiedNames_ResolveToUnifiedImplementation(
        string facadeAssemblyName,
        string typeName)
    {
        Type? implementationType =
            Type.GetType($"{typeName}, {ImplementationAssemblyName}", throwOnError: false);
        Type? legacyResolvedType =
            Type.GetType($"{typeName}, {facadeAssemblyName}", throwOnError: false);
        Assert.NotNull(implementationType);
        Assert.NotNull(legacyResolvedType);

        Assert.Same(implementationType, legacyResolvedType);
        Assert.Equal(ImplementationAssemblyName, legacyResolvedType!.Assembly.GetName().Name);
    }

    [Theory]
    [InlineData("Jalium.UI.Core", 1118, "Jalium.UI.FrameworkElement")]
    [InlineData("Jalium.UI.Media", 414, "Jalium.UI.Media.RenderTargetBitmap")]
    [InlineData("Jalium.UI.Input", 90, "Jalium.UI.Input.Keyboard")]
    [InlineData("Jalium.UI.Interop", 44, "Jalium.UI.Interop.HwndSource")]
    [InlineData("Jalium.UI.Controls", 1462, "Jalium.UI.Controls.Button")]
    public void LegacyAssembly_IsTypeDefFreeFacadeWithExpectedForwarders(
        string facadeAssemblyName,
        int expectedForwardedTypeCount,
        string keyTypeName)
    {
        Assembly facade = Assembly.Load(new AssemblyName(facadeAssemblyName));

        Assert.Empty(facade.DefinedTypes);

        Type[] forwardedTypes = facade.GetForwardedTypes();
        Assert.Equal(expectedForwardedTypeCount, forwardedTypes.Length);
        Assert.Contains(forwardedTypes, type => type.FullName == keyTypeName);
        Assert.All(
            forwardedTypes,
            type => Assert.Equal(ImplementationAssemblyName, type.Assembly.GetName().Name));
    }

    [Fact]
    public void CompileTimeReferences_AlsoBindToUnifiedImplementation()
    {
        Type[] representativeTypes =
        [
            typeof(FrameworkElement),
            typeof(RenderTargetBitmap),
            typeof(Keyboard),
            typeof(HwndSource),
            typeof(Button),
        ];

        Assert.All(
            representativeTypes,
            type => Assert.Equal(ImplementationAssemblyName, type.Assembly.GetName().Name));
    }
}
