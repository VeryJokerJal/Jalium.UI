using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Displays a splash image through a replaceable control template.</summary>
[TemplatePart(Name = "PART_Image", Type = typeof(Image))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Empty", GroupName = "ImageStates")]
[TemplateVisualState(Name = "ImageAvailable", GroupName = "ImageStates")]
public class SplashScreenPresenter : Control
{
    /// <summary>Identifies the image source dependency property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(SplashScreenPresenter),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>Identifies the image scaling dependency property.</summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(SplashScreenPresenter),
            new PropertyMetadata(Stretch.None));

    /// <summary>Identifies the allowed scaling directions.</summary>
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(SplashScreenPresenter),
            new PropertyMetadata(StretchDirection.Both));

    private static readonly DependencyPropertyKey HasImagePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasImage), typeof(bool), typeof(SplashScreenPresenter),
            new PropertyMetadata(false));

    /// <summary>Identifies whether an image source is available.</summary>
    public static readonly DependencyProperty HasImageProperty = HasImagePropertyKey.DependencyProperty;

    /// <summary>Gets or sets the image source.</summary>
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets how the image fills its content box.</summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>Gets or sets the allowed scaling directions.</summary>
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    /// <summary>Gets whether an image source is available.</summary>
    public bool HasImage => (bool)GetValue(HasImageProperty)!;

    /// <summary>Initializes a reusable splash image presenter.</summary>
    public SplashScreenPresenter()
    {
        DefaultStyleKey = typeof(SplashScreenPresenter);
        Focusable = false;
        IsTabStop = false;
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdatePresentation();
    }

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdatePresentation();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (SplashScreenPresenter)d;
        presenter.UpdatePresentation();
        presenter.InvalidateMeasure();
    }

    private void UpdatePresentation()
    {
        SetValue(HasImagePropertyKey, Source is not null);
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, HasImage ? "ImageAvailable" : "Empty", false);
    }
}
