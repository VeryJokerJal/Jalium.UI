namespace Jalium.UI.Media;

/// <summary>
/// Specifies the method used to determine number substitution.
/// </summary>
public enum NumberCultureSource
{
    /// <summary>
    /// Number culture is derived from the text element.
    /// </summary>
    Text = 0,

    /// <summary>
    /// Number culture is derived from the user's profile.
    /// </summary>
    User = 1,

    /// <summary>
    /// Number culture is derived from the system override.
    /// </summary>
    Override = 2
}

/// <summary>
/// Specifies how numbers in text are displayed in a class derived from NumberSubstitution.
/// </summary>
public enum NumberSubstitutionMethod
{
    /// <summary>
    /// The substitution method is determined based on the locale and culture.
    /// </summary>
    AsCulture = 0,

    /// <summary>
    /// If the number culture is Arabic or Farsi, numbers are context-dependent.
    /// </summary>
    Context = 1,

    /// <summary>
    /// Numbers are always rendered as European digits (0-9).
    /// </summary>
    European = 2,

    /// <summary>
    /// Numbers are rendered using the national digits for the number culture.
    /// </summary>
    NativeNational = 3,

    /// <summary>
    /// Numbers are rendered using traditional digits for the number culture.
    /// </summary>
    Traditional = 4
}

/// <summary>
/// Specifies how numbers in text are displayed.
/// </summary>
public sealed class NumberSubstitution
{
    /// <summary>
    /// Identifies the CultureSource attached property.
    /// </summary>
    public static readonly DependencyProperty CultureSourceProperty =
        DependencyProperty.RegisterAttached("CultureSource", typeof(NumberCultureSource), typeof(NumberSubstitution),
            new PropertyMetadata(NumberCultureSource.Text));

    /// <summary>
    /// Identifies the Substitution attached property.
    /// </summary>
    public static readonly DependencyProperty SubstitutionProperty =
        DependencyProperty.RegisterAttached("Substitution", typeof(NumberSubstitutionMethod), typeof(NumberSubstitution),
            new PropertyMetadata(NumberSubstitutionMethod.AsCulture));

    /// <summary>
    /// Gets or sets the culture source for number substitution.
    /// </summary>
    public NumberCultureSource CultureSource { get; set; } = NumberCultureSource.Text;

    /// <summary>
    /// Gets or sets the culture override for number substitution.
    /// </summary>
    public System.Globalization.CultureInfo? CultureOverride { get; set; }

    /// <summary>
    /// Gets or sets the substitution method for number substitution.
    /// </summary>
    public NumberSubstitutionMethod Substitution { get; set; } = NumberSubstitutionMethod.AsCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumberSubstitution"/> class.
    /// </summary>
    public NumberSubstitution()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NumberSubstitution"/> class.
    /// </summary>
    public NumberSubstitution(NumberCultureSource source, System.Globalization.CultureInfo? cultureOverride, NumberSubstitutionMethod substitution)
    {
        CultureSource = source;
        CultureOverride = cultureOverride;
        Substitution = substitution;
    }

    /// <summary>
    /// Gets the CultureSource for the specified element.
    /// </summary>
    public static NumberCultureSource GetCultureSource(DependencyObject element)
    {
        return (NumberCultureSource)(element.GetValue(CultureSourceProperty) ?? NumberCultureSource.Text);
    }

    /// <summary>
    /// Sets the CultureSource for the specified element.
    /// </summary>
    public static void SetCultureSource(DependencyObject element, NumberCultureSource value)
    {
        element.SetValue(CultureSourceProperty, value);
    }

    /// <summary>
    /// Gets the Substitution for the specified element.
    /// </summary>
    public static NumberSubstitutionMethod GetSubstitution(DependencyObject element)
    {
        return (NumberSubstitutionMethod)(element.GetValue(SubstitutionProperty) ?? NumberSubstitutionMethod.AsCulture);
    }

    /// <summary>
    /// Sets the Substitution for the specified element.
    /// </summary>
    public static void SetSubstitution(DependencyObject element, NumberSubstitutionMethod value)
    {
        element.SetValue(SubstitutionProperty, value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not NumberSubstitution other)
            return false;

        return CultureSource == other.CultureSource
            && Substitution == other.Substitution
            && Equals(CultureOverride, other.CultureOverride);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(CultureSource, Substitution, CultureOverride);
    }
}
