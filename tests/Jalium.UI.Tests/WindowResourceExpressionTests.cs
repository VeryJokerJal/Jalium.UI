using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests
{
    [Collection("macOS Window globals")]
    public sealed class WindowResourceExpressionTests : IDisposable
    {
        private readonly Func<object, object?>? _application = ResourceLookup.ApplicationResourceLookup;
        private readonly Func<object, (object? Value, ResourceDictionary? Dictionary)>? _source = ResourceLookup.ApplicationResourceLookupWithSource;
        private static readonly FieldInfo Motion = typeof(Window).Assembly.GetType("Jalium.UI.Controls.Platform.MacOSScrollBarSettings")!
            .GetField("s_prefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _motion = Motion.GetValue(null);
        private readonly ResourceDictionary _resources = new();

        public WindowResourceExpressionTests()
        {
            ResourceLookup.ApplicationResourceLookup = key => _resources.TryGetValue(key, out var value) ? value : null;
            ResourceLookup.ApplicationResourceLookupWithSource = null;
            ResourceLookup.InvalidateResourceCache();
        }

        public static IEnumerable<object[]> SystemKeyForms()
        {
            foreach (string kind in new[] { "StaticResource", "DynamicResource", "ThemeResource" })
                foreach (bool named in new[] { false, true })
                    foreach (bool wpf in new[] { false, true })
                        foreach (bool reduced in new[] { false, true }) yield return [kind, named, wpf, reduced];
        }

        [Theory]
        [MemberData(nameof(SystemKeyForms))]
        public void ActualXamlSystemKeysResolveAndDynamicFormsKeepTracking(string kind, bool named, bool wpf, bool reduced)
        {
            Motion.SetValue(null, reduced);
            string key = "{x:Static ui:SystemParameters.ClientAreaAnimationKey}";
            var child = Parse("{" + kind + " " + (named ? "ResourceKey=" : "") + key + "}", wpf);
            Assert.Equal(SystemParameters.ClientAreaAnimation, child.Tag);
            object? initial = child.Tag;
            Motion.SetValue(null, !reduced); SystemParameters.NotifyMacOSAnimationPreferencesChanged();
            Assert.Equal(kind == "StaticResource" ? initial : SystemParameters.ClientAreaAnimation, child.Tag);
        }

        [Theory]
        [InlineData("StaticResource", false)]
        [InlineData("StaticResource", true)]
        [InlineData("DynamicResource", false)]
        [InlineData("DynamicResource", true)]
        [InlineData("ThemeResource", false)]
        [InlineData("ThemeResource", true)]
        public void SystemXmlReadersUseTheSameTypedResourceKeys(string kind, bool reduced)
        {
            Motion.SetValue(null, reduced);
            string markup = "<Border xmlns='http://schemas.jalium.com/jalxaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:ui='clr-namespace:Jalium.UI;assembly=Jalium.UI.Controls' Tag='{"
                + kind + " {x:Static ui:SystemParameters.ClientAreaAnimationKey}}' />";
            using var text = new StringReader(markup); using var reader = System.Xml.XmlReader.Create(text);
            var child = Assert.IsType<Border>(XamlReader.Load(reader)); object initial = SystemParameters.ClientAreaAnimation;
            Assert.Equal(initial, child.Tag); Motion.SetValue(null, !reduced); SystemParameters.NotifyMacOSAnimationPreferencesChanged();
            Assert.Equal(kind == "StaticResource" ? initial : SystemParameters.ClientAreaAnimation, child.Tag);
        }

        [Fact]
        public void EscapedResourceKeyMarkupStaysALiteralString()
        {
            _resources["{x:Static left:Keys.Field}"] = "literal";
            Assert.Equal("literal", Parse("{StaticResource {}{x:Static left:Keys.Field}}", false).Tag);
        }

        [Theory]
        [InlineData("Field", false)]
        [InlineData("Property", false)]
        [InlineData("Field", true)]
        [InlineData("Property", true)]
        public void StaticResourceKeysRespectTheMappedNamespace(string member, bool named)
        {
            _resources[WindowKeysLeft.Keys.Field] = "left-field"; _resources[WindowKeysRight.Keys.Field] = "right-field";
            _resources[WindowKeysLeft.Keys.Property] = "left-property"; _resources[WindowKeysRight.Keys.Property] = "right-property";
            foreach (string prefix in new[] { "left", "right" })
            {
                string token = (named ? "Member=" : "") + prefix + ":Keys." + member;
                Assert.Equal(prefix + "-" + member.ToLowerInvariant(), Parse("{StaticResource {x:Static " + token + "}}", false).Tag);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ObjectElementStaticMembersRetainTheirOwnNamespace(bool wpf)
        {
            string ns = wpf ? "http://schemas.microsoft.com/winfx/2006/xaml/presentation" : "http://schemas.jalium.com/jalxaml";
            string markup = "<Border xmlns='" + ns + "' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Border.Tag>"
                + "<x:Static xmlns:key='clr-namespace:Jalium.UI.Tests.WindowKeysRight;assembly=Jalium.UI.Tests' Member='key:Keys.Text' />"
                + "</Border.Tag></Border>";
            Assert.Equal("right", Assert.IsType<Border>(XamlReader.Parse(markup)).Tag);
        }

        [Theory]
        [InlineData("StaticResource", false)]
        [InlineData("StaticResource", true)]
        [InlineData("DynamicResource", false)]
        [InlineData("DynamicResource", true)]
        [InlineData("ThemeResource", false)]
        [InlineData("ThemeResource", true)]
        public void PropertyElementResourcesRetainExpressionsInsteadOfBecomingLocalLiterals(string kind, bool reduced)
        {
            Motion.SetValue(null, reduced);
            string markup = "<Border xmlns='http://schemas.jalium.com/jalxaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:ui='clr-namespace:Jalium.UI;assembly=Jalium.UI.Controls'>"
                + "<Border.Tag><" + kind + " ResourceKey='{x:Static ui:SystemParameters.ClientAreaAnimationKey}' /></Border.Tag></Border>";
            var child = Assert.IsType<Border>(XamlReader.Parse(markup)); object initial = SystemParameters.ClientAreaAnimation;
            Assert.Equal(initial, child.Tag); Motion.SetValue(null, !reduced); SystemParameters.NotifyMacOSAnimationPreferencesChanged();
            Assert.Equal(kind == "StaticResource" ? initial : SystemParameters.ClientAreaAnimation, child.Tag);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AttachedResourceExpressionsSurviveParsingAndRespectLocalReplacement(bool setterMethod)
        {
            _resources[WindowKeysLeft.Keys.Field] = 1;
            var property = setterMethod ? Grid.RowProperty : WindowKeysLeft.Keys.SlotProperty;
            string attribute = setterMethod ? "Grid.Row" : "left:Keys.Slot";
            string markup = "<Border xmlns='http://schemas.jalium.com/jalxaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:left='clr-namespace:Jalium.UI.Tests.WindowKeysLeft;assembly=Jalium.UI.Tests' "
                + attribute + "='{DynamicResource {x:Static left:Keys.Field}}' />";
            var child = Assert.IsType<Border>(XamlReader.Parse(markup)); Assert.Equal(1, child.GetValue(property));
            _resources[WindowKeysLeft.Keys.Field] = 2; ResourceLookup.InvalidateResourceCache(); DynamicResourceBindingOperations.RefreshAll(); Assert.Equal(2, child.GetValue(property));
            child.SetValue(property, 3); _resources[WindowKeysLeft.Keys.Field] = 4; ResourceLookup.InvalidateResourceCache(); DynamicResourceBindingOperations.RefreshAll(); Assert.Equal(3, child.GetValue(property));
        }

        [Theory]
        [InlineData("{x:Static left:Keys.Text}", "left")]
        [InlineData("{x:Static Member=right:Keys.Text}", "right")]
        [InlineData("{x:Static left:Choice.First}", WindowKeysLeft.Choice.First)]
        public void DirectStaticMembersUseTheSameNamespaceAndNamedArgumentRules(string expression, object expected)
            => Assert.Equal(expected, Parse(expression, false).Tag);

        [Theory]
        [InlineData("StaticResource")]
        [InlineData("DynamicResource")]
        [InlineData("ThemeResource")]
        public void TypeKeysUseTheirMappedNamespace(string kind)
        {
            _resources[typeof(WindowKeysLeft.Keys)] = "type-left"; _resources[typeof(WindowKeysRight.Keys)] = "type-right";
            Assert.Equal("type-right", Parse("{" + kind + " {x:Type right:Keys}}", false).Tag);
        }

        [Theory]
        [InlineData("{StaticResource {x:Static missing:Keys.Field}}")]
        [InlineData("{StaticResource {x:Static left:Keys.Missing}}")]
        [InlineData("{StaticResource {x:Static left:Keys.Instance}}")]
        [InlineData("{StaticResource {x:Static left:Keys.Null}}")]
        [InlineData("{StaticResource {x:Type missing:Keys}}")]
        public void InvalidNestedKeysFailAtTheXamlBoundary(string expression)
            => Assert.Throws<XamlParseException>(() => Parse(expression, false));

        [Fact]
        public void PlainStringKeysAndLocalPrecedenceStayIntact()
        {
            _resources["ordinary"] = "application";
            Assert.Equal("application", Parse("{StaticResource ordinary}", false).Tag);
            string markup = "<StackPanel xmlns='http://schemas.jalium.com/jalxaml' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><StackPanel.Resources><x:String x:Key='ordinary'>local</x:String></StackPanel.Resources><Border Tag='{StaticResource ordinary}' /></StackPanel>";
            var panel = Assert.IsType<StackPanel>(XamlReader.Parse(markup));
            Assert.Equal("local", Assert.IsType<Border>(Assert.Single(panel.Children)).Tag);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LookupCallbacksCannotPublishAValueAfterTheirExpressionWasReplaced(bool resubscribe)
        {
            var key = new object(); var child = new Border(); child.Resources["new-key"] = "current";
            ResourceLookup.ApplicationResourceLookup = requested =>
            {
                if (!ReferenceEquals(requested, key)) return null;
                if (resubscribe) child.SetResourceReference(FrameworkElement.TagProperty, "new-key"); else child.Tag = "current";
                return "stale";
            };
            child.SetResourceReference(FrameworkElement.TagProperty, key);
            Assert.Equal("current", child.Tag);
            DynamicResourceBindingOperations.RefreshAll(); Assert.Equal("current", child.Tag);
            if (resubscribe) { child.Resources["new-key"] = "updated"; Assert.Equal("updated", child.Tag); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DocumentLookupRetiresAnOlderRequestEvenForTheSameResourceKey(bool resubscribe)
        {
            var key = new object(); var paragraph = new Paragraph(); bool first = true;
            ResourceLookup.ApplicationResourceLookup = requested =>
            {
                if (!ReferenceEquals(requested, key)) return null;
                if (!first) return "current";
                first = false;
                if (resubscribe) paragraph.SetResourceReference(FrameworkContentElement.TagProperty, key);
                else paragraph.Tag = "current";
                return "stale";
            };
            paragraph.SetResourceReference(FrameworkContentElement.TagProperty, key);
            Assert.Equal("current", paragraph.Tag);
            paragraph.Resources[key] = "updated"; Assert.Equal(resubscribe ? "updated" : "current", paragraph.Tag);
        }

        private static Border Parse(string expression, bool wpf)
        {
            string ns = wpf ? "http://schemas.microsoft.com/winfx/2006/xaml/presentation" : "http://schemas.jalium.com/jalxaml";
            string markup = "<Border xmlns='" + ns + "' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
                + "xmlns:ui='clr-namespace:Jalium.UI;assembly=Jalium.UI.Controls' "
                + "xmlns:left='clr-namespace:Jalium.UI.Tests.WindowKeysLeft;assembly=Jalium.UI.Tests' "
                + "xmlns:right='clr-namespace:Jalium.UI.Tests.WindowKeysRight;assembly=Jalium.UI.Tests' Tag=\"" + expression + "\" />";
            return Assert.IsType<Border>(XamlReader.Parse(markup));
        }

        public void Dispose()
        {
            ResourceLookup.ApplicationResourceLookup = _application; ResourceLookup.ApplicationResourceLookupWithSource = _source;
            Motion.SetValue(null, _motion); ResourceLookup.InvalidateResourceCache();
        }
    }

    [Collection("macOS Window globals")]
    public sealed class WindowDynamicResourceLifetimeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LocalSetValueRemovesOnlyItsExpressionEvenWhenTheValueIsEqual(bool equal)
        {
            var root = new Border(); root.Resources["value"] = "first"; root.SetResourceReference(FrameworkElement.TagProperty, "value");
            root.Tag = equal ? "first" : "author";
            root.Resources["value"] = "later"; DynamicResourceBindingOperations.RefreshAll();
            Assert.Equal(equal ? "first" : "author", root.Tag);
            Assert.False(DynamicResourceBindingOperations.TryGetLocalDynamicResourceKey(root, FrameworkElement.TagProperty, out _));
        }

        [Fact]
        public void ClearValueRetiresTheLocalExpressionAndCanRestoreItLater()
        {
            var root = new Border(); root.Resources["value"] = "first"; root.SetResourceReference(FrameworkElement.TagProperty, "value");
            root.ClearValue(FrameworkElement.TagProperty); root.Resources["value"] = "later"; DynamicResourceBindingOperations.RefreshAll();
            Assert.Null(root.Tag); Assert.False(DynamicResourceBindingOperations.TryGetLocalDynamicResourceKey(root, FrameworkElement.TagProperty, out _));
            root.SetResourceReference(FrameworkElement.TagProperty, "value"); Assert.Equal("later", root.Tag);
        }

        [Fact]
        public void SetCurrentValueAndMissingResourceRefreshKeepTheExpressionAlive()
        {
            var root = new Border(); root.SetResourceReference(FrameworkElement.TagProperty, "value");
            root.Resources["value"] = "first"; Assert.Equal("first", root.Tag);
            root.SetCurrentValue(FrameworkElement.TagProperty, "temporary");
            root.Resources["value"] = "second"; Assert.Equal("second", root.Tag);
            root.Resources.Remove("value"); Assert.Null(root.Tag);
            root.Resources["value"] = "third"; Assert.Equal("third", root.Tag);
        }

        [Fact]
        public void LocalReplacementKeepsLowerStyleResourcesLive()
        {
            var root = new Border(); root.Resources["lower"] = "style-one"; root.Resources["upper"] = "local";
            root.Style = new Style(typeof(Border)) { Setters = { new Setter(FrameworkElement.TagProperty, new DynamicResourceReference("lower")) } };
            root.SetResourceReference(FrameworkElement.TagProperty, "upper"); root.Tag = "author";
            root.Resources["lower"] = "style-two"; root.Resources["upper"] = "stale"; Assert.Equal("author", root.Tag);
            root.ClearValue(FrameworkElement.TagProperty); Assert.Equal("style-two", root.Tag);
            root.Resources["lower"] = "style-three"; Assert.Equal("style-three", root.Tag);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NonVisualAndDocumentConsumersRespectLocalReplacement(bool document)
        {
            var host = new Border(); var red = Color.FromRgb(10,20,30); var green = Color.FromRgb(30,40,50);
            if (document)
            {
                var paragraph = new Paragraph(); paragraph.Resources["value"] = "first";
                paragraph.SetResourceReference(FrameworkContentElement.TagProperty, "value"); paragraph.Tag = "author";
                paragraph.Resources["value"] = "later"; Assert.Equal("author", paragraph.Tag);
            }
            else
            {
                var brush = new SolidColorBrush(); host.Resources["color"] = red;
                DynamicResourceBindingOperations.SetDynamicResourceForNonVisual(host, brush, SolidColorBrush.ColorProperty, "color");
                brush.Color = green; host.Resources["color"] = Color.FromRgb(80,90,100); Assert.Equal(green, brush.Color);
            }
        }

        [Fact]
        public void InvalidLocalWritesDoNotRemoveAValidResourceExpression()
        {
            var root = new Border(); root.Resources["height"] = 20d; root.SetResourceReference(FrameworkElement.HeightProperty, "height");
            Assert.Throws<ArgumentException>(() => root.SetValue(FrameworkElement.HeightProperty, "invalid"));
            root.Resources["height"] = 30d; Assert.Equal(30d, root.Height);
        }

        [Fact]
        public void BindingAndResourceExpressionsCanReplaceEachOtherWithoutRevivingTheOldOwner()
        {
            var root = new Border(); root.Resources["value"] = "resource-one"; root.SetResourceReference(FrameworkElement.TagProperty, "value");
            var model = new ValueModel(); root.SetBinding(FrameworkElement.TagProperty, new Binding(nameof(ValueModel.Value)) { Source = model, Mode = BindingMode.OneWay });
            root.Resources["value"] = "resource-two"; Assert.Equal("model-one", root.Tag);
            model.Value = "model-two"; Assert.Equal("model-two", root.Tag);
            root.SetResourceReference(FrameworkElement.TagProperty, "value"); Assert.Equal("resource-two", root.Tag);
            model.Value = "model-three"; Assert.Equal("resource-two", root.Tag);
        }

        [Fact]
        public void APropertyCallbackCanReplaceTheResourceWithoutBeingResubscribed()
        {
            var root = new ReplacingBorder(); root.Resources["value"] = "first"; root.SetResourceReference(FrameworkElement.TagProperty, "value");
            root.Replace = true; root.Resources["value"] = "callback"; Assert.Equal("author", root.Tag);
            root.Resources["value"] = "later"; Assert.Equal("author", root.Tag);
        }

        private sealed class ReplacingBorder : Border
        {
            internal bool Replace;
            protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
            {
                base.OnPropertyChanged(e);
                if (Replace && e.Property == FrameworkElement.TagProperty && Equals(e.NewValue, "callback"))
                { Replace = false; Tag = "author"; }
            }
        }

        private sealed class ValueModel : INotifyPropertyChanged
        {
            private string _value = "model-one";
            public string Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}

namespace Jalium.UI.Tests.WindowKeysLeft
{
    public enum Choice { First }
    public class Keys
    {
        public static readonly DependencyProperty SlotProperty = DependencyProperty.RegisterAttached("Slot", typeof(int), typeof(Keys), new PropertyMetadata(0));
        public static readonly object Field = new();
        public static object Property { get; } = new();
        public static string Text => "left";
        public static object? Null => null;
        public object Instance => new();
    }
}
namespace Jalium.UI.Tests.WindowKeysRight
{
    public class Keys
    {
        public static readonly object Field = new();
        public static object Property { get; } = new();
        public static string Text => "right";
    }
}
