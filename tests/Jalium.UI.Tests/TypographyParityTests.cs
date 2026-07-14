using System.Reflection;
using Jalium.UI.Controls;
using DocumentTypography = Jalium.UI.Documents.Typography;

namespace Jalium.UI.Tests;

public sealed class TypographyParityTests
{
    private static readonly (string Name, Type ValueType, object DefaultValue, object AlternateValue)[] Contracts =
    [
        (nameof(DocumentTypography.StandardLigatures), typeof(bool), true, false),
        (nameof(DocumentTypography.ContextualLigatures), typeof(bool), true, false),
        (nameof(DocumentTypography.DiscretionaryLigatures), typeof(bool), false, true),
        (nameof(DocumentTypography.HistoricalLigatures), typeof(bool), false, true),
        (nameof(DocumentTypography.AnnotationAlternates), typeof(int), 0, 4),
        (nameof(DocumentTypography.ContextualAlternates), typeof(bool), true, false),
        (nameof(DocumentTypography.HistoricalForms), typeof(bool), false, true),
        (nameof(DocumentTypography.Kerning), typeof(bool), true, false),
        (nameof(DocumentTypography.CapitalSpacing), typeof(bool), false, true),
        (nameof(DocumentTypography.CaseSensitiveForms), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet1), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet2), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet3), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet4), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet5), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet6), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet7), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet8), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet9), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet10), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet11), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet12), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet13), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet14), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet15), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet16), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet17), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet18), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet19), typeof(bool), false, true),
        (nameof(DocumentTypography.StylisticSet20), typeof(bool), false, true),
        (nameof(DocumentTypography.Fraction), typeof(FontFraction), FontFraction.Normal, FontFraction.Stacked),
        (nameof(DocumentTypography.SlashedZero), typeof(bool), false, true),
        (nameof(DocumentTypography.MathematicalGreek), typeof(bool), false, true),
        (nameof(DocumentTypography.EastAsianExpertForms), typeof(bool), false, true),
        (nameof(DocumentTypography.Variants), typeof(FontVariants), FontVariants.Normal, FontVariants.Ruby),
        (nameof(DocumentTypography.Capitals), typeof(FontCapitals), FontCapitals.Normal, FontCapitals.SmallCaps),
        (nameof(DocumentTypography.NumeralStyle), typeof(FontNumeralStyle), FontNumeralStyle.Normal, FontNumeralStyle.OldStyle),
        (nameof(DocumentTypography.NumeralAlignment), typeof(FontNumeralAlignment), FontNumeralAlignment.Normal, FontNumeralAlignment.Tabular),
        (nameof(DocumentTypography.EastAsianWidths), typeof(FontEastAsianWidths), FontEastAsianWidths.Normal, FontEastAsianWidths.Full),
        (nameof(DocumentTypography.EastAsianLanguage), typeof(FontEastAsianLanguage), FontEastAsianLanguage.Normal, FontEastAsianLanguage.Jis04),
        (nameof(DocumentTypography.StandardSwashes), typeof(int), 0, 2),
        (nameof(DocumentTypography.ContextualSwashes), typeof(int), 0, 3),
        (nameof(DocumentTypography.StylisticAlternates), typeof(int), 0, 5),
    ];

    [Fact]
    public void DeclaredSurfaceContainsEveryWpfTypographyContract()
    {
        var type = typeof(DocumentTypography);

        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(Contracts.Length, type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length);
        Assert.Equal(Contracts.Length, type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Length);
        Assert.Equal(Contracts.Length * 2, type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Length);

        foreach (var contract in Contracts)
        {
            var property = type.GetProperty(contract.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.NotNull(property);
            Assert.Equal(contract.ValueType, property!.PropertyType);
            Assert.True(property.CanRead);
            Assert.True(property.CanWrite);

            var field = type.GetField($"{contract.Name}Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.NotNull(field);
            Assert.True(field!.IsInitOnly);
            Assert.Equal(typeof(DependencyProperty), field.FieldType);

            var dependencyProperty = Assert.IsType<DependencyProperty>(field.GetValue(null));
            Assert.Equal(contract.Name, dependencyProperty.Name);
            Assert.Equal(contract.ValueType, dependencyProperty.PropertyType);
            Assert.Equal(type, dependencyProperty.OwnerType);

            var getter = type.GetMethod(
                $"Get{contract.Name}",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null,
                [typeof(DependencyObject)],
                null);
            Assert.NotNull(getter);
            Assert.Equal(contract.ValueType, getter!.ReturnType);
            Assert.Equal("element", Assert.Single(getter.GetParameters()).Name);
            var browsable = getter.GetCustomAttribute<AttachedPropertyBrowsableForTypeAttribute>();
            Assert.NotNull(browsable);
            Assert.Equal(typeof(DependencyObject), browsable!.TargetType);

            var setter = type.GetMethod(
                $"Set{contract.Name}",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null,
                [typeof(DependencyObject), contract.ValueType],
                null);
            Assert.NotNull(setter);
            Assert.Equal(typeof(void), setter!.ReturnType);
            Assert.Equal(
                new[] { "element", "value" },
                setter.GetParameters().Select(parameter => parameter.Name).ToArray());
        }
    }

    [Fact]
    public void DefaultsAndMetadataMatchWpfOpenTypePropertyDefinitions()
    {
        var owner = new TextBlock();
        var typography = owner.Typography;

        foreach (var contract in Contracts)
        {
            var property = typeof(DocumentTypography).GetProperty(contract.Name)!;
            var dependencyProperty = GetDependencyProperty(contract.Name);
            var getter = GetAccessor($"Get{contract.Name}", typeof(DependencyObject));

            Assert.Equal(contract.DefaultValue, property.GetValue(typography));
            Assert.Equal(contract.DefaultValue, getter.Invoke(null, [owner]));
            Assert.Equal(contract.DefaultValue, dependencyProperty.DefaultMetadata.DefaultValue);
            Assert.False(owner.HasLocalValue(dependencyProperty));

            var metadata = Assert.IsType<FrameworkPropertyMetadata>(dependencyProperty.DefaultMetadata);
            Assert.True(metadata.AffectsMeasure);
            Assert.True(metadata.AffectsRender);
            Assert.True(metadata.Inherits);
        }
    }

    [Fact]
    public void InstanceAndAttachedAccessorsShareTheDependencyPropertyStore()
    {
        var owner = new TextBlock();
        var typography = owner.Typography;

        foreach (var contract in Contracts)
        {
            var property = typeof(DocumentTypography).GetProperty(contract.Name)!;
            var dependencyProperty = GetDependencyProperty(contract.Name);
            var getter = GetAccessor($"Get{contract.Name}", typeof(DependencyObject));
            var setter = GetAccessor($"Set{contract.Name}", typeof(DependencyObject), contract.ValueType);

            property.SetValue(typography, contract.AlternateValue);
            Assert.Equal(contract.AlternateValue, owner.GetValue(dependencyProperty));
            Assert.Equal(contract.AlternateValue, getter.Invoke(null, [owner]));

            setter.Invoke(null, [owner, contract.DefaultValue]);
            Assert.Equal(contract.DefaultValue, property.GetValue(owner.Typography));
            Assert.True(owner.HasLocalValue(dependencyProperty));
        }
    }

    [Fact]
    public void ValuesInheritAndUseWpfTypeValidationSemantics()
    {
        var parent = new StackPanel();
        var child = new TextBlock();
        parent.Children.Add(child);

        DocumentTypography.SetStylisticSet8(parent, true);
        DocumentTypography.SetAnnotationAlternates(parent, -1);

        Assert.True(DocumentTypography.GetStylisticSet8(child));
        Assert.Equal(-1, DocumentTypography.GetAnnotationAlternates(child));

        Assert.False(DocumentTypography.FractionProperty.IsValidValue(null));
        Assert.False(DocumentTypography.FractionProperty.IsValidValue("Stacked"));
        Assert.Throws<ArgumentException>(() => child.SetValue(DocumentTypography.FractionProperty, "Stacked"));

        var undefinedFraction = (FontFraction)42;
        Assert.True(DocumentTypography.FractionProperty.IsValidValue(undefinedFraction));
        DocumentTypography.SetFraction(child, undefinedFraction);
        Assert.Equal(undefinedFraction, DocumentTypography.GetFraction(child));
    }

    [Fact]
    public void AttachedAccessorsRejectNullOwners()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentTypography.GetAnnotationAlternates(null!));
        Assert.Throws<ArgumentNullException>(() => DocumentTypography.SetAnnotationAlternates(null!, 1));
        Assert.Throws<ArgumentNullException>(() => DocumentTypography.GetEastAsianLanguage(null!));
        Assert.Throws<ArgumentNullException>(() => DocumentTypography.SetEastAsianLanguage(null!, FontEastAsianLanguage.Jis04));
    }

    private static DependencyProperty GetDependencyProperty(string name)
    {
        return Assert.IsType<DependencyProperty>(typeof(DocumentTypography).GetField($"{name}Property")!.GetValue(null));
    }

    private static MethodInfo GetAccessor(string name, params Type[] parameterTypes)
    {
        var method = typeof(DocumentTypography).GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        return method!;
    }
}
