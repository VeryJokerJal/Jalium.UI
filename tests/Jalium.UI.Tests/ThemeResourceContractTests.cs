using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeResourceContractTests
{
    private const string HighContrastThemeKey = "HighContrast";

    private const string GenericThemeResourceName =
        "Jalium.UI.Controls.Themes.Generic.jalxaml";

    private static readonly Regex s_markupReferencePattern = new(
        @"\{(?:ThemeResource|DynamicResource)\s+(?:ResourceKey\s*=\s*)?(?<key>[^\s,}""']+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_elementReferencePattern = new(
        @"<(?:ThemeResource|DynamicResource)\b[^>]*\bResourceKey\s*=\s*""(?<key>[^""]+)""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void GenericTheme_ShouldExposeSymmetricDarkAndLightKeySets()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();

        try
        {
            var genericTheme = BuildRegisteredThemeDictionary(GenericThemeResourceName);
            var darkKeys = CollectAvailableKeys(genericTheme, ThemeVariant.Dark.ToString());
            var lightKeys = CollectAvailableKeys(genericTheme, ThemeVariant.Light.ToString());

            Assert.NotEmpty(darkKeys);
            AssertKeySetsEqual(darkKeys, lightKeys);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Theory]
    [InlineData(nameof(ThemeVariant.Dark))]
    [InlineData(nameof(ThemeVariant.Light))]
    [InlineData(HighContrastThemeKey)]
    public void ControlThemeResourceReferences_ShouldResolve(string themeKey)
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            ApplyThemeKey(themeKey);

            var references = ReadControlThemeResourceReferences();
            Assert.NotEmpty(references);

            var unresolvedReferences = new List<string>();
            foreach (var reference in references)
            {
                if (!app.Resources.TryGetValue(reference.Key, out var value) || value is null)
                {
                    unresolvedReferences.Add($"'{reference.Key}' ({reference.Sources})");
                }
            }

            Assert.True(
                unresolvedReferences.Count == 0,
                $"Theme resources referenced by control dictionaries did not resolve in the " +
                $"{themeKey} theme: {string.Join(", ", unresolvedReferences)}.");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ControlThemes_ShouldUseOrdinaryRoundedRectangleShapes()
    {
        var themeDirectory = FindControlThemeDirectory();
        var superEllipseUsages = Directory
            .EnumerateFiles(themeDirectory, "*.jalxaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => File
                .ReadLines(path)
                .Select((line, index) => new
                {
                    File = Path.GetFileName(path),
                    Line = index + 1,
                    Text = line.Trim(),
                }))
            .Where(item => item.Text.Contains(
                "Shape=\"SuperEllipse\"",
                StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{item.File}:{item.Line}: {item.Text}")
            .ToArray();

        Assert.True(
            superEllipseUsages.Length == 0,
            "Built-in control themes must use ordinary rounded rectangles. " +
            $"Found explicit SuperEllipse shapes: {string.Join(", ", superEllipseUsages)}.");
    }

    [Fact]
    public void ControlTemplates_ShouldContainAtMostOneBorder()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var violations = new List<string>();

        foreach (var path in Directory
                     .EnumerateFiles(FindControlThemeDirectory(), "*.jalxaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var template in document.Descendants(presentation + "ControlTemplate"))
            {
                var borders = template
                    .Descendants(presentation + "Border")
                    .Where(border => ReferenceEquals(
                        border.Ancestors(presentation + "ControlTemplate").FirstOrDefault(),
                        template))
                    .ToArray();

                if (borders.Length <= 1)
                {
                    continue;
                }

                var targetType = (string?)template.Attribute("TargetType") ?? "(unknown)";
                violations.Add($"{Path.GetFileName(path)}:{targetType} has {borders.Length} Borders");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Each ControlTemplate may own at most one Border, with no nested Border chrome. " +
            string.Join("; ", violations));
    }

    [Fact]
    public void TemplatedVisualStates_ShouldTargetTemplateParts()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var violations = new List<string>();

        foreach (var path in Directory
                     .EnumerateFiles(FindControlThemeDirectory(), "*.jalxaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var style in document.Descendants(presentation + "Style"))
            {
                var ownsTemplate = style
                    .Elements(presentation + "Setter")
                    .Any(setter => string.Equals(
                        (string?)setter.Attribute("Property"),
                        "Template",
                        StringComparison.Ordinal));
                if (!ownsTemplate)
                {
                    continue;
                }

                var styleTriggers = style.Element(presentation + "Style.Triggers");
                if (styleTriggers is null)
                {
                    continue;
                }

                foreach (var setter in styleTriggers.Descendants(presentation + "Setter"))
                {
                    var targetName = (string?)setter.Attribute("TargetName");
                    var property = (string?)setter.Attribute("Property");
                    if (!string.IsNullOrEmpty(targetName) ||
                        string.Equals(property, "Background", StringComparison.Ordinal) ||
                        string.Equals(property, "BorderBrush", StringComparison.Ordinal))
                    {
                        var targetType = (string?)style.Attribute("TargetType") ?? "(unknown)";
                        violations.Add(
                            $"{Path.GetFileName(path)}:{targetType} Style.Trigger setter " +
                            $"TargetName='{targetName}' Property='{property}'");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Template-part visual states must be declared in ControlTemplate.Triggers so local " +
            "control values do not suppress hover/focus/pressed/disabled chrome. " +
            string.Join("; ", violations));
    }

    private static void ApplyThemeKey(string themeKey)
    {
        if (Enum.TryParse<ThemeVariant>(themeKey, ignoreCase: false, out var theme))
        {
            ThemeManager.ApplyTheme(theme);
            Assert.Equal(theme, ThemeManager.CurrentTheme);
        }
        else
        {
            Assert.Equal(HighContrastThemeKey, themeKey);
            ResourceDictionary.CurrentThemeKey = themeKey;
        }

        Assert.Equal(themeKey, ResourceDictionary.CurrentThemeKey as string);
    }

    private static ResourceDictionary BuildRegisteredThemeDictionary(string resourceName)
    {
        var assembly = typeof(Button).Assembly;

        Assert.True(
            XamlPrebuiltDictionaryRegistry.TryGet(resourceName, out var builder),
            $"No generated dictionary builder was registered for '{resourceName}'.");
        Assert.NotNull(builder);

        const string legacyControlsPrefix = "Jalium.UI.Controls.";
        var relativeName = resourceName.StartsWith(legacyControlsPrefix, StringComparison.Ordinal)
            ? resourceName[legacyControlsPrefix.Length..]
            : resourceName;

        const string jalxamlExtension = ".jalxaml";
        var relativePath = relativeName.EndsWith(jalxamlExtension, StringComparison.OrdinalIgnoreCase)
            ? relativeName[..^jalxamlExtension.Length].Replace('.', '/') + jalxamlExtension
            : relativeName.Replace('.', '/');

        var assemblyName = assembly.GetName().Name!;
        var sourceUri = new Uri(
            $"resource:///{assemblyName}/{relativePath}",
            UriKind.Absolute);
        var dictionary = new ResourceDictionary
        {
            Source = sourceUri,
            BaseUri = sourceUri,
            SourceAssembly = assembly
        };

        var context = XamlBuilder.BeginComponent(dictionary, sourceUri, assembly);
        builder!(dictionary, context);
        XamlBuilder.EndComponent(dictionary, context);
        return dictionary;
    }

    private static HashSet<object> CollectAvailableKeys(
        ResourceDictionary root,
        string themeKey)
    {
        var keys = new HashSet<object>();
        var visited = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);

        Visit(root);
        return keys;

        void Visit(ResourceDictionary dictionary)
        {
            if (!visited.Add(dictionary))
            {
                return;
            }

            foreach (var key in dictionary.Keys)
            {
                keys.Add(key);
            }

            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var themeDictionary))
            {
                Visit(themeDictionary);
            }

            foreach (var mergedDictionary in dictionary.MergedDictionaries)
            {
                Visit(mergedDictionary);
            }
        }
    }

    private static void AssertKeySetsEqual(
        IReadOnlySet<object> darkKeys,
        IReadOnlySet<object> lightKeys)
    {
        var missingInLight = darkKeys
            .Except(lightKeys)
            .Select(DescribeKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var missingInDark = lightKeys
            .Except(darkKeys)
            .Select(DescribeKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingInLight.Length == 0 && missingInDark.Length == 0,
            $"Dark/Light theme resource keys are asymmetric. " +
            $"Missing in Light: [{string.Join(", ", missingInLight)}]. " +
            $"Missing in Dark: [{string.Join(", ", missingInDark)}].");
    }

    private static string DescribeKey(object key)
        => key is Type type
            ? $"Type:{type.FullName}"
            : $"{key.GetType().FullName}:{key}";

    private static IReadOnlyList<ThemeResourceReference> ReadControlThemeResourceReferences()
    {
        var sourcesByKey = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var themeDirectory = FindControlThemeDirectory();

        foreach (var path in Directory
                     .EnumerateFiles(themeDirectory, "*.jalxaml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var sourceName = Path.GetFileName(path);
            var text = File.ReadAllText(path);

            AddMatches(s_markupReferencePattern.Matches(text), sourceName);
            AddMatches(s_elementReferencePattern.Matches(text), sourceName);
        }

        return sourcesByKey
            .Select(pair => new ThemeResourceReference(
                pair.Key,
                string.Join(", ", pair.Value.OrderBy(source => source, StringComparer.Ordinal))))
            .ToArray();

        void AddMatches(MatchCollection matches, string sourceName)
        {
            foreach (Match match in matches)
            {
                var key = match.Groups["key"].Value.Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                if (!sourcesByKey.TryGetValue(key, out var sources))
                {
                    sources = new HashSet<string>(StringComparer.Ordinal);
                    sourcesByKey.Add(key, sources);
                }

                sources.Add(sourceName);
            }
        }
    }

    private static string FindControlThemeDirectory(
        [CallerFilePath] string sourceFilePath = "")
    {
        var configuredRoot = Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT");
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);

        foreach (var seed in new[]
                 {
                     configuredRoot,
                     sourceDirectory,
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory,
                 })
        {
            if (string.IsNullOrWhiteSpace(seed) || !Directory.Exists(seed))
            {
                continue;
            }

            for (var current = new DirectoryInfo(seed); current != null; current = current.Parent)
            {
                var candidate = Path.Combine(
                    current.FullName,
                    "src",
                    "managed",
                    "Jalium.UI.Controls",
                    "Themes",
                    "Controls");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException(
            "Unable to locate src/managed/Jalium.UI.Controls/Themes/Controls.");
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField(
            "_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod(
            "Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private sealed record ThemeResourceReference(string Key, string Sources);
}
