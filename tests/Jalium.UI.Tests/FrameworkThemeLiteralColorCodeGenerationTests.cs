extern alias sg;

using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using sg::Jalium.UI.Xaml.SourceGenerator;

namespace Jalium.UI.Tests;

public sealed class FrameworkThemeLiteralColorCodeGenerationTests
{
    private const string PresentationXmlns =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlXmlns =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void BulkLiteralColorsStayEagerWhileLiteralBrushesRegisterDeferredDescriptors()
    {
        var code = Generate(CreateBulkPaletteBody(includeBrushes: true), optimizeFrameworkTheme: true);

        Assert.Equal(17, Count(code, "__AddLiteralColor("));
        Assert.Equal(2, Count(code, ".AddDeferredLiteralSolidColorBrush("));
        Assert.Equal(1, Count(code, "new global::Jalium.UI.Media.Color()"));
        Assert.DoesNotContain("new global::Jalium.UI.Media.SolidColorBrush()", code);
        Assert.DoesNotContain("SetContentText(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDeferredResource", code, StringComparison.Ordinal);
        Assert.Equal(1, Count(code, "MethodImplOptions.NoInlining"));
        Assert.Contains(
            "__AddLiteralColor(__target, __ctx, \"Color00\", 0xFF112233u);",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "__AddLiteralColor(__target, __ctx, \"Color15\", 0x80112233u);",
            code,
            StringComparison.Ordinal);

        var color07 = code.IndexOf(
            "__AddLiteralColor(__target, __ctx, \"Color07\"",
            StringComparison.Ordinal);
        var brushA = code.IndexOf(
            ".AddDeferredLiteralSolidColorBrush(\"BrushA\", 0xFF445566u)",
            StringComparison.Ordinal);
        var color08 = code.IndexOf(
            "__AddLiteralColor(__target, __ctx, \"Color08\"",
            StringComparison.Ordinal);
        Assert.True(color07 >= 0 && brushA > color07 && color08 > brushA);
    }

    [Fact]
    public void LiteralColorOptimizationRequiresBulkExactConverterInput()
    {
        var belowThreshold = Generate(
            CreateLiteralColorElements(15),
            optimizeFrameworkTheme: true);

        Assert.DoesNotContain("__AddLiteralColor", belowThreshold, StringComparison.Ordinal);
        Assert.Equal(15, Count(belowThreshold, "new global::Jalium.UI.Media.Color()"));
        Assert.Equal(15, Count(belowThreshold, "SetContentText("));

        var ordinaryDictionary = Generate(
            CreateLiteralColorElements(16),
            optimizeFrameworkTheme: false);

        Assert.DoesNotContain("__AddLiteralColor", ordinaryDictionary, StringComparison.Ordinal);
        Assert.Equal(16, Count(ordinaryDictionary, "SetContentText("));

        var exactPaletteWithFallbacks = Generate(
            CreateLiteralColorElements(16)
            + "<Color x:Key=\"Named\">Red</Color>\n"
            + "<Color x:Key=\"ShortHex\">#123</Color>\n",
            optimizeFrameworkTheme: true);

        Assert.Equal(17, Count(exactPaletteWithFallbacks, "__AddLiteralColor("));
        Assert.Equal(2, Count(exactPaletteWithFallbacks, "SetContentText("));
        Assert.Equal(3, Count(
            exactPaletteWithFallbacks,
            "new global::Jalium.UI.Media.Color()"));
        Assert.Contains("\"Red\", __ctx", exactPaletteWithFallbacks, StringComparison.Ordinal);
        Assert.Contains("\"#123\", __ctx", exactPaletteWithFallbacks, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedBulkPaletteCompilesAndDefersOnlyLiteralBrushInstances()
    {
        var code = Generate(CreateBulkPaletteBody(includeBrushes: true), optimizeFrameworkTheme: true);
        var snapshot = CompileAndRun(code);

        var expectedKeys = new List<string>();
        for (var index = 0; index < 16; index++)
        {
            expectedKeys.Add($"Color{index:D2}");
            if (index == 7)
            {
                expectedKeys.Add("BrushA");
            }
            else if (index == 15)
            {
                expectedKeys.Add("BrushB");
            }
        }

        Assert.Equal(expectedKeys, Assert.IsType<string[]>(snapshot[0]));
        Assert.Equal(18, Assert.IsType<int>(snapshot[1]));
        Assert.Equal(0xFF112233u, Assert.IsType<uint>(snapshot[2]));
        Assert.Equal(0x80112233u, Assert.IsType<uint>(snapshot[3]));
        Assert.Equal(0xFF445566u, Assert.IsType<uint>(snapshot[4]));
        Assert.False(Assert.IsType<bool>(snapshot[5]));
        Assert.False(Assert.IsType<bool>(snapshot[6]));
        Assert.Equal(0xFF112233u, Assert.IsType<uint>(snapshot[7]));
        Assert.True(Assert.IsType<bool>(snapshot[8]));
        Assert.Equal(0, Assert.IsType<int>(snapshot[9]));
        Assert.Equal(0, Assert.IsType<int>(snapshot[10]));
        Assert.Equal(17, Assert.IsType<int>(snapshot[11]));
        Assert.Equal(16, Assert.IsType<int>(snapshot[12]));
        Assert.Equal(0, Assert.IsType<int>(snapshot[13]));
        Assert.Equal(2, Assert.IsType<int>(snapshot[14]));
        Assert.True(Assert.IsType<bool>(snapshot[15]));
    }

    private static string CreateBulkPaletteBody(bool includeBrushes)
    {
        var body = new StringBuilder();
        for (var index = 0; index < 16; index++)
        {
            var color = index < 8 ? "#112233" : "#80112233";
            body.AppendLine($"<Color x:Key=\"Color{index:D2}\">{color}</Color>");
            if (!includeBrushes)
            {
                continue;
            }

            if (index == 7)
            {
                body.AppendLine(
                    "<SolidColorBrush x:Key=\"BrushA\" Color=\"#445566\" />");
            }
            else if (index == 15)
            {
                body.AppendLine(
                    "<SolidColorBrush x:Key=\"BrushB\" Color=\"#445566\" />");
            }
        }

        return body.ToString();
    }

    private static string CreateLiteralColorElements(int count)
    {
        var body = new StringBuilder();
        for (var index = 0; index < count; index++)
        {
            body.AppendLine(
                $"<Color x:Key=\"Color{index:D2}\">#FF{index:X2}2233</Color>");
        }

        return body.ToString();
    }

    private static string Generate(string body, bool optimizeFrameworkTheme)
    {
        var result = JalxamlParser.Parse(
            $$"""
            <ResourceDictionary xmlns="{{PresentationXmlns}}"
                                xmlns:x="{{XamlXmlns}}">
            {{body}}
            </ResourceDictionary>
            """,
            "FrameworkTheme.jalxaml")!;
        ResolveColorNodes(result.Root!);

        var code = JalxamlCodeGenerator.TryEmitDictionaryBuildBody(
            result,
            symbols: null,
            xmlnsResolver: null,
            deferFrameworkThemeStyles: optimizeFrameworkTheme);
        return Assert.IsType<string>(code);
    }

    private static void ResolveColorNodes(JalxamlAstNode node)
    {
        if (string.Equals(node.LocalName, "Color", StringComparison.Ordinal)
            && string.IsNullOrEmpty(node.ResolvedClrTypeName))
        {
            node.ResolvedClrTypeName = "Jalium.UI.Media.Color";
            node.FallbackClrTypeName = "Jalium.UI.Media.Color";
        }

        foreach (var child in node.Children)
        {
            ResolveColorNodes(child);
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var child in propertyElement.Children)
            {
                ResolveColorNodes(child);
            }
        }
    }

    private static object[] CompileAndRun(string generatedBody)
    {
        var source = CreateRuntimeHarnessSource(generatedBody);
        var compilation = CSharpCompilation.Create(
            assemblyName: $"GeneratedPalette_{Guid.NewGuid():N}",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)),
            ],
            references: GetPlatformReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        Assert.True(
            emitResult.Success,
            string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Where(
                    static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        peStream.Position = 0;
        var loadContext = new AssemblyLoadContext(
            $"GeneratedPalette_{Guid.NewGuid():N}",
            isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(peStream);
            var harnessType = assembly.GetType(
                "Generated.ThemeHarness",
                throwOnError: true)!;
            var run = harnessType.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(harnessType.FullName, "Run");
            return Assert.IsType<object[]>(run.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static string CreateRuntimeHarnessSource(string generatedBody)
    {
        return """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;

            namespace Jalium.UI.Media
            {
                public struct Color
                {
                    public byte A { get; private set; }
                    public byte R { get; private set; }
                    public byte G { get; private set; }
                    public byte B { get; private set; }

                    public static Color FromArgb(byte a, byte r, byte g, byte b)
                        => new Color { A = a, R = r, G = g, B = b };

                    public uint ToArgb()
                        => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
                }

                public sealed class SolidColorBrush
                {
                    public SolidColorBrush(Color color) => Color = color;

                    public Color Color { get; set; }
                }
            }

            namespace Jalium.UI
            {
                public sealed class ResourceDictionary
                {
                    private sealed class DeferredLiteralBrush
                    {
                        private readonly ResourceDictionary _owner;
                        private readonly uint _argb;
                        private global::Jalium.UI.Media.SolidColorBrush? _value;

                        public DeferredLiteralBrush(ResourceDictionary owner, uint argb)
                        {
                            _owner = owner;
                            _argb = argb;
                        }

                        public global::Jalium.UI.Media.SolidColorBrush GetValue()
                        {
                            if (_value is null)
                            {
                                _value = new global::Jalium.UI.Media.SolidColorBrush(
                                    global::Jalium.UI.Media.Color.FromArgb(
                                    (byte)(_argb >> 24),
                                    (byte)(_argb >> 16),
                                    (byte)(_argb >> 8),
                                    (byte)_argb));
                                _owner.RealizedBrushCount++;
                            }

                            return _value;
                        }
                    }

                    private readonly Dictionary<string, object> _values =
                        new Dictionary<string, object>(StringComparer.Ordinal);
                    private readonly List<string> _addedKeys = new List<string>();

                    public int Count => _values.Count;
                    public int RealizedBrushCount { get; private set; }
                    public IReadOnlyList<string> AddedKeys => _addedKeys;
                    public object this[string key]
                    {
                        get
                        {
                            var value = _values[key];
                            return value is DeferredLiteralBrush deferred
                                ? deferred.GetValue()
                                : value;
                        }
                    }

                    public void Set(string key, object value)
                    {
                        if (!_values.ContainsKey(key))
                            _addedKeys.Add(key);
                        _values[key] = value;
                    }

                    internal void AddDeferredLiteralSolidColorBrush(string key, uint argb)
                        => Set(key, new DeferredLiteralBrush(this, argb));
                }
            }

            namespace Jalium.UI.Markup
            {
                public sealed class XamlBuildContext
                {
                    public XamlBuildContext(object sourceToken) => SourceToken = sourceToken;

                    public object SourceToken { get; }
                    public List<string> AddedKeys { get; } = new List<string>();
                    public List<object> SourceTokens { get; } = new List<object>();
                    public int Depth { get; set; }
                    public int SetContentTextCalls { get; set; }
                }

                public static class XamlBuilder
                {
                    public static int XmlIdentityCalls { get; private set; }
                    public static int RecordedAttributeCalls { get; private set; }

                    public static void Reset()
                    {
                        XmlIdentityCalls = 0;
                        RecordedAttributeCalls = 0;
                    }

                    public static void SetXmlIdentity(
                        object instance,
                        string namespaceUri,
                        string localName,
                        bool resetAttributes = false)
                    {
                        XmlIdentityCalls++;
                    }

                    public static void RecordXmlAttribute(
                        object instance,
                        string namespaceUri,
                        string localName,
                        string value,
                        Type? ownerType = null)
                    {
                        RecordedAttributeCalls++;
                    }

                    public static void PushParent(object parent, XamlBuildContext context)
                    {
                        context.Depth++;
                    }

                    public static void PopParent(XamlBuildContext context)
                    {
                        context.Depth--;
                    }

                    public static void ApplyXDirective(
                        object instance,
                        string directive,
                        string value,
                        XamlBuildContext context)
                    {
                    }

                    public static object SetContentText(
                        object instance,
                        string text,
                        XamlBuildContext context)
                    {
                        context.SetContentTextCalls++;
                        return instance;
                    }

                    public static void AddChild(
                        object parent,
                        object child,
                        XamlBuildContext context,
                        string? resourceKey = null)
                    {
                        if (resourceKey is null)
                            throw new InvalidOperationException("Resource key is required.");

                        ((global::Jalium.UI.ResourceDictionary)parent).Set(resourceKey, child);
                        context.AddedKeys.Add(resourceKey);
                        context.SourceTokens.Add(context.SourceToken);
                    }
                }
            }

            namespace Generated
            {
                public static class ThemeHarness
                {
                    public static object[] Run()
                    {
                        global::Jalium.UI.Markup.XamlBuilder.Reset();
                        var sourceToken = new object();
                        var context = new global::Jalium.UI.Markup.XamlBuildContext(sourceToken);
                        var dictionary = new global::Jalium.UI.ResourceDictionary();
                        Build(dictionary, context);

                        var realizedBeforeBrushRead = dictionary.RealizedBrushCount;

                        var backgroundArgb = Task.Run(
                                () => ((global::Jalium.UI.Media.Color)dictionary["Color00"]).ToArgb())
                            .GetAwaiter()
                            .GetResult();
                        var brushA = (global::Jalium.UI.Media.SolidColorBrush)dictionary["BrushA"];
                        var brushB = (global::Jalium.UI.Media.SolidColorBrush)dictionary["BrushB"];
                        var brushASecond = (global::Jalium.UI.Media.SolidColorBrush)dictionary["BrushA"];

                        return new object[]
                        {
                            dictionary.AddedKeys.ToArray(),
                            dictionary.Count,
                            ((global::Jalium.UI.Media.Color)dictionary["Color00"]).ToArgb(),
                            ((global::Jalium.UI.Media.Color)dictionary["Color15"]).ToArgb(),
                            brushA.Color.ToArgb(),
                            ReferenceEquals(brushA, brushB),
                            ReferenceEquals(dictionary["Color00"], dictionary["Color01"]),
                            backgroundArgb,
                            context.SourceTokens.All(token => ReferenceEquals(token, sourceToken)),
                            context.Depth,
                            context.SetContentTextCalls,
                            global::Jalium.UI.Markup.XamlBuilder.XmlIdentityCalls,
                            global::Jalium.UI.Markup.XamlBuilder.RecordedAttributeCalls,
                            realizedBeforeBrushRead,
                            dictionary.RealizedBrushCount,
                            ReferenceEquals(brushA, brushASecond),
                        };
                    }

                    private static void Build(
                        global::Jalium.UI.ResourceDictionary __target,
                        global::Jalium.UI.Markup.XamlBuildContext __ctx)
                    {
            """
            + generatedBody
            + """
                    }
                }
            }
            """;
    }

    private static IEnumerable<MetadataReference> GetPlatformReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException(
                "The runtime did not expose trusted platform assemblies.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    private static int Count(string value, string needle)
        => value.Split(new[] { needle }, StringSplitOptions.None).Length - 1;
}
