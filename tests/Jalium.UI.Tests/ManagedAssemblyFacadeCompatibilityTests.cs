using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

public sealed class ManagedAssemblyFacadeCompatibilityTests
{
    private const string ImplementationAssemblyName = "Jalium.UI.Managed";

    [Theory]
    [InlineData("Jalium.UI.Core", "Jalium.UI.FrameworkElement")]
    [InlineData("Jalium.UI.Media", "Jalium.UI.Media.Imaging.RenderTargetBitmap")]
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
    [InlineData("Jalium.UI.Core", "Jalium.UI.FrameworkElement")]
    [InlineData("Jalium.UI.Media", "Jalium.UI.Media.Imaging.RenderTargetBitmap")]
    [InlineData("Jalium.UI.Input", "Jalium.UI.Input.Keyboard")]
    [InlineData("Jalium.UI.Interop", "Jalium.UI.Interop.HwndSource")]
    [InlineData("Jalium.UI.Controls", "Jalium.UI.Controls.Button")]
    public void LegacyAssembly_IsTypeDefFreeFacadeWithCanonicalForwarders(
        string facadeAssemblyName,
        string keyTypeName)
    {
        Assembly facade = Assembly.Load(new AssemblyName(facadeAssemblyName));

        Assert.Empty(facade.DefinedTypes);

        Type[] forwardedTypes = facade.GetForwardedTypes();
        Assert.NotEmpty(forwardedTypes);
        Assert.Equal(
            forwardedTypes.Length,
            forwardedTypes.Select(type => type.FullName).Distinct(StringComparer.Ordinal).Count());
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
