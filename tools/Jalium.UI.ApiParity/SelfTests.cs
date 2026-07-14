using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Jalium.UI.ApiParity;

internal static class SelfTests
{
    public static void Run(ReferencePack pack, NamespaceMapper mapper)
    {
        const string wpfSource = """
            #nullable enable
            namespace System.Windows.Data;

            public class Box<T>
            {
                public static readonly int Count = 1;
                public T? Value { get; protected set; }
                public event System.EventHandler? Changed;

                protected Box(System.Collections.Generic.Dictionary<string, T[]> values) { }
                public void Add(T item) => Changed?.Invoke(this, System.EventArgs.Empty);
                public static T Echo(T value) => value;
            }

            public enum Mode { Zero = 0, One = 1 }

            public interface IRoot { }
            public interface IChild : IRoot { }
            public interface IExtra { }
            public class InterfaceSurface : IChild { }
            public class ExtraInterfaceSurface : IChild { }
            public class MissingInterfaceSurface : IChild { }
            public class ConcreteSurface { public ConcreteSurface() { } }

            public abstract class DrawingSurface
            {
                public void DrawText(string formattedText, int origin) { }
            }
            """;
        const string jaliumSource = """
            #nullable enable
            namespace Jalium.UI.Data;

            public class Box<T>
            {
                public static readonly int Count = 1;
                public T? Value { get; protected set; }
                public event System.EventHandler? Changed;

                protected Box(System.Collections.Generic.Dictionary<string, T[]> values) { }
                public void Add(T item) => Changed?.Invoke(this, System.EventArgs.Empty);
                public static T Echo(T value) => value;
            }

            public enum Mode { Zero = 0, One = 1 }

            public interface IRoot { }
            public interface IChild : IRoot { }
            public interface IExtra { }
            public class InterfaceSurface : IChild, IRoot { }
            public class ExtraInterfaceSurface : IChild, IExtra { }
            public class MissingInterfaceSurface : IChild { }
            public class ConcreteSurface { public ConcreteSurface() { } }

            public abstract class DrawingSurface
            {
                public void DrawText(string formattedText, int origin) { }
            }
            """;
        const string movedSource = """
            namespace Jalium.UI.Moved;

            public class Box<T>
            {
                protected Box(System.Collections.Generic.Dictionary<string, T[]> values) { }
            }
            """;
        const string mismatchedSource = """
            #nullable enable
            namespace Jalium.UI.Data;

            public class Box<T>
            {
                protected Box(System.Collections.Generic.Dictionary<string, T[]> values) { }
                public static object Echo(T value) => value!;
            }

            public enum Mode { Zero = 0, One = 2 }

            public interface IRoot { }
            public interface IChild : IRoot { }
            public class MissingInterfaceSurface : IRoot { }

            public abstract class DrawingSurface
            {
                public abstract void DrawText(string formattedText, int origin);
            }
            """;

        CSharpCompilation wpfCompilation = CompilationFactory.CreateSourceCompilation(
            "PresentationFramework.Synthetic",
            wpfSource,
            pack.CoreReferenceDirectory);
        CSharpCompilation jaliumCompilation = CompilationFactory.CreateSourceCompilation(
            "Jalium.UI.Synthetic",
            jaliumSource,
            pack.CoreReferenceDirectory);
        CSharpCompilation movedCompilation = CompilationFactory.CreateSourceCompilation(
            "Jalium.UI.MovedSynthetic",
            movedSource,
            pack.CoreReferenceDirectory);
        CSharpCompilation mismatchedCompilation = CompilationFactory.CreateSourceCompilation(
            "Jalium.UI.MismatchedSynthetic",
            mismatchedSource,
            pack.CoreReferenceDirectory);
        AssertNoErrors(wpfCompilation);
        AssertNoErrors(jaliumCompilation);
        AssertNoErrors(movedCompilation);
        AssertNoErrors(mismatchedCompilation);

        string metadataName = MetadataNames.FromLegacyTypeName("Box<T>");
        Assert(metadataName == "Box`1", "Generic legacy type names must retain metadata arity.");
        string wpfFullName = MetadataNames.Join("System.Windows.Data", metadataName);
        string jaliumFullName = MetadataNames.Join("Jalium.UI.Data", metadataName);

        var wpfIndex = ApiSymbolIndex.Build([wpfCompilation.Assembly]);
        var jaliumIndex = ApiSymbolIndex.Build([jaliumCompilation.Assembly]);
        var movedIndex = ApiSymbolIndex.Build([movedCompilation.Assembly]);
        Assert(wpfIndex.Entries.Any(entry => entry.Kind == ApiKind.Property), "API index must include properties.");
        Assert(wpfIndex.Entries.Any(entry => entry.Kind == ApiKind.Method), "API index must include methods.");
        Assert(wpfIndex.Entries.Any(entry => entry.Kind == ApiKind.Event), "API index must include events.");
        Assert(wpfIndex.Entries.Any(entry => entry.Kind == ApiKind.Field), "API index must include fields.");
        Assert(wpfIndex.Entries.Any(entry => entry.Kind == ApiKind.EnumValue), "API index must include enum values.");

        INamedTypeSymbol wpfType = Single(wpfIndex.FindTypes(wpfFullName));
        INamedTypeSymbol jaliumType = Single(jaliumIndex.FindTypes(jaliumFullName));
        IMethodSymbol wpfConstructor = Single(wpfType.InstanceConstructors.Where(ApiVisibility.IsApiMember));
        IMethodSymbol jaliumConstructor = Single(jaliumType.InstanceConstructors.Where(ApiVisibility.IsApiMember));
        string formatted = LegacySignatureFormatter.Constructor(wpfConstructor);
        Assert(
            formatted == "protected Box(System.Collections.Generic.Dictionary<string, T[]> values)",
            "Constructor formatter must preserve generic parameter structure and names. Actual: " + formatted);

        var mappedAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            wpfCompilation.Assembly.Name,
        };
        ConstructorContract expected = ConstructorContract.Create(wpfConstructor, mapper, mappedAssemblies);
        ConstructorContract actual = ConstructorContract.Create(
            jaliumConstructor,
            mapper,
            new HashSet<string>(StringComparer.Ordinal));
        Assert(expected.FullKey == actual.FullKey, "Mapped generic constructor contracts must compare equal.");

        var facetVerifier = new LegacyFacetVerifier(
            wpfIndex,
            jaliumIndex,
            mapper,
            mappedAssemblies);
        INamedTypeSymbol wpfInterfaceSurface = wpfCompilation.GetTypeByMetadataName(
            "System.Windows.Data.InterfaceSurface")!;
        INamedTypeSymbol jaliumInterfaceSurface = jaliumCompilation.GetTypeByMetadataName(
            "Jalium.UI.Data.InterfaceSurface")!;
        Assert(
            facetVerifier.TypeShapeKeyForTest(wpfInterfaceSurface, mapWpf: true)
                == facetVerifier.TypeShapeKeyForTest(jaliumInterfaceSurface, mapWpf: false),
            "Type shape comparison must normalize redundant direct interface declarations to their public closure.");

        INamedTypeSymbol wpfExtraInterfaceSurface = wpfCompilation.GetTypeByMetadataName(
            "System.Windows.Data.ExtraInterfaceSurface")!;
        INamedTypeSymbol jaliumExtraInterfaceSurface = jaliumCompilation.GetTypeByMetadataName(
            "Jalium.UI.Data.ExtraInterfaceSurface")!;
        Assert(
            facetVerifier.TypeShapeKeyForTest(wpfExtraInterfaceSurface, mapWpf: true)
                != facetVerifier.TypeShapeKeyForTest(jaliumExtraInterfaceSurface, mapWpf: false),
            "The extra-interface fixture must have distinct exact type-shape keys.");
        Assert(
            facetVerifier.EvaluateFacetStatusForTest(
                "kind-mismatch",
                wpfExtraInterfaceSurface,
                jaliumExtraInterfaceSurface,
                "public class ExtraInterfaceSurface : IChild") == "resolved",
            "Type kind comparison must allow additional Jalium interfaces once the complete WPF interface closure is present.");

        INamedTypeSymbol wpfMissingInterfaceSurface = wpfCompilation.GetTypeByMetadataName(
            "System.Windows.Data.MissingInterfaceSurface")!;
        INamedTypeSymbol jaliumMissingInterfaceSurface = mismatchedCompilation.GetTypeByMetadataName(
            "Jalium.UI.Data.MissingInterfaceSurface")!;
        Assert(
            facetVerifier.EvaluateFacetStatusForTest(
                "kind-mismatch",
                wpfMissingInterfaceSurface,
                jaliumMissingInterfaceSurface,
                "public class MissingInterfaceSurface : IChild") == "still-mismatched",
            "Type kind comparison must fail when a required WPF interface is absent from the Jalium interface closure.");
        string missingInterfaceDiagnostic = facetVerifier.EvaluateFacetDiagnosticForTest(
            "kind-mismatch",
            wpfMissingInterfaceSurface,
            jaliumMissingInterfaceSurface,
            "public class MissingInterfaceSurface : IChild");
        Assert(
            missingInterfaceDiagnostic.Contains("missing required interfaces=[Jalium.UI.Data.IChild]", StringComparison.Ordinal),
            "Type kind diagnostics must identify each required WPF interface that is missing.");
        Assert(
            missingInterfaceDiagnostic.Contains("Expected TypeShapeKey:", StringComparison.Ordinal)
                && missingInterfaceDiagnostic.Contains("Actual TypeShapeKey:", StringComparison.Ordinal)
                && missingInterfaceDiagnostic.Contains("base=", StringComparison.Ordinal)
                && missingInterfaceDiagnostic.Contains("abstract=", StringComparison.Ordinal)
                && missingInterfaceDiagnostic.Contains("sealed=", StringComparison.Ordinal)
                && missingInterfaceDiagnostic.Contains("interfaces=[", StringComparison.Ordinal),
            "Type kind diagnostics must expose readable expected and actual shape keys.");

        IMethodSymbol wpfEcho = Single(wpfType.GetMembers("Echo").OfType<IMethodSymbol>());
        IMethodSymbol jaliumEcho = Single(jaliumType.GetMembers("Echo").OfType<IMethodSymbol>());
        MethodContract expectedMethod = MethodContract.Create(wpfEcho, mapper, mappedAssemblies);
        MethodContract actualMethod = MethodContract.Create(
            jaliumEcho,
            mapper,
            new HashSet<string>(StringComparer.Ordinal));
        Assert(expectedMethod.FullKey == actualMethod.FullKey,
            "Static method parameter/return/accessibility contracts must compare equal.");

        IPropertySymbol wpfProperty = Single(wpfType.GetMembers("Value").OfType<IPropertySymbol>());
        IPropertySymbol jaliumProperty = Single(jaliumType.GetMembers("Value").OfType<IPropertySymbol>());
        Assert(
            PropertyContract.Create(wpfProperty, mapper, mappedAssemblies).FullKey
                == PropertyContract.Create(jaliumProperty, mapper, new HashSet<string>(StringComparer.Ordinal)).FullKey,
            "Property type and protected-setter accessibility must compare equal.");
        Assert(
            LegacyFacetVerifier.ValidateLegacyMetadataClaimStatusForTest(
                "accessibility",
                wpfProperty,
                "public T Value { get; set; }") == "legacy-input-invalid",
            "A legacy accessor claim that contradicts authoritative WPF metadata must be classified as invalid input.");
        INamedTypeSymbol wpfConcrete = wpfCompilation.GetTypeByMetadataName(
            "System.Windows.Data.ConcreteSurface")!;
        INamedTypeSymbol jaliumConcrete = jaliumCompilation.GetTypeByMetadataName(
            "Jalium.UI.Data.ConcreteSurface")!;
        Assert(
            facetVerifier.EvaluateFacetStatusForTest(
                "kind-mismatch",
                wpfConcrete,
                jaliumConcrete,
                "public class ConcreteSurface { public ConcreteSurface() { } } (concrete, instantiable)") == "resolved",
            "Concrete/instantiable legacy claims must resolve without requiring unrelated type-shape identity.");

        IMethodSymbol wpfDrawText = Single(wpfCompilation.GetTypeByMetadataName(
            "System.Windows.Data.DrawingSurface")!.GetMembers("DrawText").OfType<IMethodSymbol>());
        IMethodSymbol jaliumDrawText = Single(jaliumCompilation.GetTypeByMetadataName(
            "Jalium.UI.Data.DrawingSurface")!.GetMembers("DrawText").OfType<IMethodSymbol>());
        IMethodSymbol mismatchedDrawText = Single(mismatchedCompilation.GetTypeByMetadataName(
            "Jalium.UI.Data.DrawingSurface")!.GetMembers("DrawText").OfType<IMethodSymbol>());
        Assert(
            facetVerifier.EvaluateFacetStatusForTest(
                "kind-mismatch",
                wpfDrawText,
                jaliumDrawText,
                "public void DrawText(string formattedText, int origin)") == "resolved",
            "Concrete non-virtual methods must resolve matching legacy kind-mismatch rows.");
        Assert(
            facetVerifier.EvaluateFacetStatusForTest(
                "kind-mismatch",
                wpfDrawText,
                mismatchedDrawText,
                "public void DrawText(string formattedText, int origin)") == "still-mismatched",
            "Abstract method replacements must remain mismatched against concrete non-virtual WPF methods.");

        INamedTypeSymbol wpfMode = wpfCompilation.GetTypeByMetadataName("System.Windows.Data.Mode")!;
        INamedTypeSymbol jaliumMode = jaliumCompilation.GetTypeByMetadataName("Jalium.UI.Data.Mode")!;
        IFieldSymbol wpfEnumValue = Single(wpfMode.GetMembers("One").OfType<IFieldSymbol>());
        IFieldSymbol jaliumEnumValue = Single(jaliumMode.GetMembers("One").OfType<IFieldSymbol>());
        Assert(
            ContractKey.Constant(wpfEnumValue.ConstantValue) == ContractKey.Constant(jaliumEnumValue.ConstantValue),
            "Exact enum constant values must compare equal.");

        INamedTypeSymbol mismatchedBox = mismatchedCompilation.GetTypeByMetadataName(jaliumFullName)!;
        IMethodSymbol mismatchedEcho = Single(mismatchedBox.GetMembers("Echo").OfType<IMethodSymbol>());
        MethodContract mismatchedMethod = MethodContract.Create(
            mismatchedEcho,
            mapper,
            new HashSet<string>(StringComparer.Ordinal));
        Assert(expectedMethod.IdentityKey == mismatchedMethod.IdentityKey,
            "Return-type changes must retain overload identity.");
        Assert(expectedMethod.FullKey != mismatchedMethod.FullKey,
            "Return-type changes must fail the full method contract.");
        INamedTypeSymbol mismatchedMode = mismatchedCompilation.GetTypeByMetadataName("Jalium.UI.Data.Mode")!;
        IFieldSymbol mismatchedEnumValue = Single(mismatchedMode.GetMembers("One").OfType<IFieldSymbol>());
        Assert(
            ContractKey.Constant(wpfEnumValue.ConstantValue) != ContractKey.Constant(mismatchedEnumValue.ConstantValue),
            "Enum value mismatches must be detectable.");

        string apiId = StableIds.ApiId(wpfType);
        Assert(apiId.Contains("|T:System.Windows.Data.Box", StringComparison.Ordinal),
            "Stable API IDs must use Roslyn documentation IDs.");
        Assert(
            StableIds.GapId(apiId, "missing-type") == StableIds.GapId(apiId, "missing-type"),
            "Gap IDs must be deterministic.");

        Assert(
            mapper.MapNamespace("System.Windows.Controls.Primitives")
                == "Jalium.UI.Controls.Primitives",
            "Longest-prefix namespace mapping failed.");
        Assert(
            mapper.MapNamespace("System.Windows.Shapes")
                == "Jalium.UI.Shapes",
            "Canonical Shapes namespace mapping failed.");
        Assert(
            mapper.MapType("System.Windows.Media", "Visual") == "Jalium.UI.Media.Visual",
            "Canonical Visual namespace mapping failed.");
        var layeredTypeOverrides = new Dictionary<(string Namespace, string Type), string>
        {
            [("System.Windows.Controls.Primitives", "StatusBar")] = "Jalium.UI.Controls.Primitives.StatusBar",
        };
        foreach (((string namespaceName, string typeName), string target) in layeredTypeOverrides)
        {
            Assert(
                mapper.MapType(namespaceName, typeName) == target,
                $"Layered type override failed for {namespaceName}.{typeName}.");
        }
        Assert(movedIndex.FindTypes(jaliumFullName).Count == 0,
            "A same-name type in a different namespace must not be an exact match.");
        Assert(movedIndex.FindTypesBySimpleName(metadataName).Count == 1,
            "A moved type must remain discoverable for namespace-mismatch diagnostics.");
        var ambiguousIndex = ApiSymbolIndex.Build([jaliumCompilation.Assembly, mismatchedCompilation.Assembly]);
        Assert(ambiguousIndex.FindTypes(jaliumFullName).Count == 2,
            "Duplicate exact metadata names across Jalium assemblies must remain ambiguous.");

        VerifyExternalWindowsDesktopContractMapping(pack, mapper);
    }

    private static void VerifyExternalWindowsDesktopContractMapping(ReferencePack pack, NamespaceMapper mapper)
    {
        const string automationTypesSource = """
            namespace System.Windows.Automation
            {
                public sealed class AsyncContentLoadedEventArgs : System.EventArgs { }
            }
            """;
        const string automationProviderSource = """
            namespace System.Windows.Automation.Provider
            {
                public interface IRawElementProviderSimple { }
                public interface IGridProvider
                {
                    int RowCount { get; }
                    IRawElementProviderSimple GetItem(int row, int column);
                }
            }
            """;
        const string wpfPeerSource = """
            namespace System.Windows.Automation.Peers
            {
                public class AutomationPeer
                {
                    protected AutomationPeer PeerFromProvider(
                        System.Windows.Automation.Provider.IRawElementProviderSimple provider) => this;

                    public void RaiseAsyncContentLoadedEvent(
                        System.Windows.Automation.AsyncContentLoadedEventArgs args) { }
                }

                public sealed class GridPeer : AutomationPeer,
                    System.Windows.Automation.Provider.IGridProvider
                {
                    int System.Windows.Automation.Provider.IGridProvider.RowCount => 0;

                    System.Windows.Automation.Provider.IRawElementProviderSimple
                        System.Windows.Automation.Provider.IGridProvider.GetItem(int row, int column) => null!;
                }
            }
            """;
        const string jaliumSource = """
            namespace Jalium.UI.Automation
            {
                public sealed class AsyncContentLoadedEventArgs : System.EventArgs { }
            }

            namespace Jalium.UI.Automation.Provider
            {
                public interface IRawElementProviderSimple { }
                public interface IGridProvider
                {
                    int RowCount { get; }
                    IRawElementProviderSimple GetItem(int row, int column);
                }
            }

            namespace Jalium.UI.Automation.Peers
            {
                public class AutomationPeer
                {
                    protected AutomationPeer PeerFromProvider(
                        Jalium.UI.Automation.Provider.IRawElementProviderSimple provider) => this;

                    public void RaiseAsyncContentLoadedEvent(
                        Jalium.UI.Automation.AsyncContentLoadedEventArgs args) { }
                }

                public sealed class GridPeer : AutomationPeer,
                    Jalium.UI.Automation.Provider.IGridProvider
                {
                    int Jalium.UI.Automation.Provider.IGridProvider.RowCount => 0;

                    Jalium.UI.Automation.Provider.IRawElementProviderSimple
                        Jalium.UI.Automation.Provider.IGridProvider.GetItem(int row, int column) => null!;
                }
            }
            """;

        CSharpCompilation automationTypes = CompilationFactory.CreateSourceCompilation(
            "UIAutomationTypes",
            automationTypesSource,
            pack.CoreReferenceDirectory);
        CSharpCompilation automationProvider = CompilationFactory.CreateSourceCompilation(
                "UIAutomationProvider",
                automationProviderSource,
                pack.CoreReferenceDirectory)
            .AddReferences(automationTypes.ToMetadataReference());
        CSharpCompilation wpfPeers = CompilationFactory.CreateSourceCompilation(
                "PresentationCore.ExternalContractSynthetic",
                wpfPeerSource,
                pack.CoreReferenceDirectory)
            .AddReferences(
                automationTypes.ToMetadataReference(),
                automationProvider.ToMetadataReference());
        CSharpCompilation jalium = CompilationFactory.CreateSourceCompilation(
            "Jalium.UI.ExternalContractSynthetic",
            jaliumSource,
            pack.CoreReferenceDirectory);

        AssertNoErrors(automationTypes);
        AssertNoErrors(automationProvider);
        AssertNoErrors(wpfPeers);
        AssertNoErrors(jalium);

        HashSet<string> mappedAssemblies = LegacyVerifier.CreateMappedAssemblyNames(
            [wpfPeers.Assembly],
            pack.DesktopReferenceAssemblyNames);
        var noMappedAssemblies = new HashSet<string>(StringComparer.Ordinal);
        Assert(mappedAssemblies.Contains("UIAutomationProvider"),
            "UIAutomationProvider must participate in WPF contract namespace mapping.");
        Assert(mappedAssemblies.Contains("UIAutomationTypes"),
            "UIAutomationTypes must participate in WPF contract namespace mapping.");
        Assert(mappedAssemblies.Contains("System.Windows.Input.Manipulations"),
            "External WindowsDesktop input contract assemblies must participate in mapping.");
        Assert(!mappedAssemblies.Contains("System.ObjectModel"),
            "Core/BCL assemblies such as System.ObjectModel must not be WPF namespace-mapped.");

        CSharpCompilation bclProbe = CompilationFactory.CreateSourceCompilation(
            "BclIdentityProbe",
            "public sealed class Probe { public System.Windows.Input.ICommand? Command { get; set; } }",
            pack.CoreReferenceDirectory);
        AssertNoErrors(bclProbe);
        IPropertySymbol commandProperty = Single(
            bclProbe.GetTypeByMetadataName("Probe")!.GetMembers("Command").OfType<IPropertySymbol>());
        Assert(
            TypeContractKey.Create(commandProperty.Type, mapper, mappedAssemblies)
                == "System.Windows.Input.ICommand",
            "System.ObjectModel's ICommand must retain its BCL identity instead of mapping to Jalium.UI.Input.");

        INamedTypeSymbol wpfRaw = wpfPeers.GetTypeByMetadataName(
            "System.Windows.Automation.Provider.IRawElementProviderSimple")!;
        INamedTypeSymbol jaliumRaw = jalium.GetTypeByMetadataName(
            "Jalium.UI.Automation.Provider.IRawElementProviderSimple")!;
        Assert(
            TypeContractKey.Create(wpfRaw, mapper, mappedAssemblies)
                == TypeContractKey.Create(jaliumRaw, mapper, noMappedAssemblies),
            "Provider types owned by UIAutomationProvider must map to canonical Jalium provider identities.");

        INamedTypeSymbol wpfAutomationPeer = wpfPeers.GetTypeByMetadataName(
            "System.Windows.Automation.Peers.AutomationPeer")!;
        INamedTypeSymbol jaliumAutomationPeer = jalium.GetTypeByMetadataName(
            "Jalium.UI.Automation.Peers.AutomationPeer")!;
        AssertMethodsEqual("PeerFromProvider", wpfAutomationPeer, jaliumAutomationPeer, mapper, mappedAssemblies, noMappedAssemblies);
        AssertMethodsEqual("RaiseAsyncContentLoadedEvent", wpfAutomationPeer, jaliumAutomationPeer, mapper, mappedAssemblies, noMappedAssemblies);

        INamedTypeSymbol wpfGridPeer = wpfPeers.GetTypeByMetadataName(
            "System.Windows.Automation.Peers.GridPeer")!;
        INamedTypeSymbol jaliumGridPeer = jalium.GetTypeByMetadataName(
            "Jalium.UI.Automation.Peers.GridPeer")!;
        IPropertySymbol wpfRowCount = Single(wpfGridPeer.GetMembers().OfType<IPropertySymbol>());
        IPropertySymbol jaliumRowCount = Single(jaliumGridPeer.GetMembers().OfType<IPropertySymbol>());
        Assert(
            PropertyContract.Create(wpfRowCount, mapper, mappedAssemblies).FullKey
                == PropertyContract.Create(jaliumRowCount, mapper, noMappedAssemblies).FullKey,
            "Explicit provider property contracts must map across external WPF assemblies.");
        IMethodSymbol wpfGetItem = Single(wpfGridPeer.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.ExplicitInterfaceImplementation));
        IMethodSymbol jaliumGetItem = Single(jaliumGridPeer.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.ExplicitInterfaceImplementation));
        Assert(
            MethodContract.Create(wpfGetItem, mapper, mappedAssemblies).FullKey
                == MethodContract.Create(jaliumGetItem, mapper, noMappedAssemblies).FullKey,
            "Explicit provider method contracts must map across external WPF assemblies.");
    }

    private static void AssertMethodsEqual(
        string name,
        INamedTypeSymbol wpfType,
        INamedTypeSymbol jaliumType,
        NamespaceMapper mapper,
        ISet<string> mappedAssemblies,
        ISet<string> noMappedAssemblies)
    {
        IMethodSymbol expected = Single(wpfType.GetMembers(name).OfType<IMethodSymbol>());
        IMethodSymbol actual = Single(jaliumType.GetMembers(name).OfType<IMethodSymbol>());
        Assert(
            MethodContract.Create(expected, mapper, mappedAssemblies).FullKey
                == MethodContract.Create(actual, mapper, noMappedAssemblies).FullKey,
            $"External WPF contract method '{name}' must map to the exact Jalium method contract.");
    }

    private static void AssertNoErrors(CSharpCompilation compilation)
    {
        Diagnostic[] errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"Synthetic compilation '{compilation.AssemblyName}' failed:\n"
                + string.Join("\n", errors.Select(static error => error.ToString())));
        }
    }

    private static T Single<T>(IEnumerable<T> values)
    {
        T[] array = values.ToArray();
        Assert(array.Length == 1, $"Expected one value, found {array.Length}.");
        return array[0];
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-test failed: " + message);
        }
    }
}
