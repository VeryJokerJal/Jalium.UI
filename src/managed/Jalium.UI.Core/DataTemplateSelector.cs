using Jalium.UI.Data;

namespace Jalium.UI.Controls
{
    /// <summary>
    /// Provides a way to choose a <see cref="DataTemplate"/> based on the data object
    /// and the data-bound element.
    /// </summary>
    public class DataTemplateSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataTemplateSelector"/> class.
        /// </summary>
        public DataTemplateSelector()
        {
        }

        /// <summary>
        /// When overridden in a derived class, returns a template based on custom logic.
        /// </summary>
        public virtual DataTemplate? SelectTemplate(object? item, DependencyObject container)
        {
            return null;
        }
    }

    /// <summary>
    /// Represents a template for the panel used by an items control.
    /// </summary>
    public class ItemsPanelTemplate : FrameworkTemplate
    {
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        private Type? _panelType;

        /// <summary>
        /// Gets or sets the callback used to parse deferred XAML content.
        /// </summary>
        public static Func<string, System.Reflection.Assembly?, FrameworkElement?>? XamlParser { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemsPanelTemplate"/> class.
        /// </summary>
        public ItemsPanelTemplate()
        {
        }

        /// <summary>Initializes a panel template from a legacy factory root.</summary>
#pragma warning disable CS0618 // WPF compatibility requires accepting the obsolete factory type.
        public ItemsPanelTemplate(FrameworkElementFactory root)
        {
            ArgumentNullException.ThrowIfNull(root);
            PanelType = root.Type;
        }
#pragma warning restore CS0618

        /// <summary>
        /// Gets or sets the panel type created by this template.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        public Type? PanelType
        {
            get => _panelType;
            set
            {
                CheckSealed();
                if (value != null && !typeof(FrameworkElement).IsAssignableFrom(value))
                {
                    throw new ArgumentException(
                        $"ItemsPanelTemplate panel type '{value}' must derive from FrameworkElement.",
                        nameof(value));
                }

                _panelType = value;
            }
        }

        /// <summary>
        /// Sets the visual-tree factory for this template.
        /// </summary>
        public void SetVisualTree(Func<FrameworkElement> visualTreeFactory)
        {
            ArgumentNullException.ThrowIfNull(visualTreeFactory);
            SetVisualTreeFactory(visualTreeFactory);
        }

        /// <summary>
        /// Creates the panel represented by this template.
        /// </summary>
        public FrameworkElement? CreatePanel()
        {
            if (_panelType != null)
            {
                return Activator.CreateInstance(_panelType) as FrameworkElement;
            }

            return LoadContent();
        }

        /// <summary>
        /// Creates the visual tree represented by this template.
        /// </summary>
        public new FrameworkElement? LoadContent() => base.LoadContent() as FrameworkElement;

        /// <inheritdoc />
        protected override Func<string, System.Reflection.Assembly?, FrameworkElement?>? DeferredXamlParser => XamlParser;

        /// <inheritdoc />
        protected override void ValidateTemplatedParent(FrameworkElement templatedParent)
        {
            ArgumentNullException.ThrowIfNull(templatedParent);
        }
    }
}

namespace Jalium.UI
{
    /// <summary>
    /// Compatibility name for the former Jalium namespace. New code should use
    /// <see cref="Controls.DataTemplateSelector"/>, matching WPF.
    /// </summary>
    public class DataTemplateSelector : Controls.DataTemplateSelector
    {
        public DataTemplateSelector()
        {
        }
    }

    /// <summary>
    /// Represents a DataTemplate that supports a hierarchy of generated items controls.
    /// </summary>
    public class HierarchicalDataTemplate : DataTemplate
    {
        /// <summary>
        /// Gets or sets the alternation cycle applied to generated child containers.
        /// </summary>
        public int AlternationCount { get; set; }

        public BindingBase? ItemsSource { get; set; }

        /// <summary>
        /// Gets or sets the binding group applied to generated child containers.
        /// </summary>
        public BindingGroup? ItemBindingGroup { get; set; }

        public DataTemplate? ItemTemplate { get; set; }

        public Controls.DataTemplateSelector? ItemTemplateSelector { get; set; }

        public Style? ItemContainerStyle { get; set; }

        /// <summary>
        /// Gets or sets the selector used to choose styles for generated child containers.
        /// </summary>
        public Controls.StyleSelector? ItemContainerStyleSelector { get; set; }

        /// <summary>
        /// Gets or sets the composite format string used for generated child item content.
        /// </summary>
        public string? ItemStringFormat { get; set; }

        public HierarchicalDataTemplate()
        {
        }

        public HierarchicalDataTemplate(object dataType) : base(dataType)
        {
        }

        public HierarchicalDataTemplate(Type dataType) : base(dataType)
        {
        }
    }

    /// <summary>
    /// Compatibility name for the former Jalium namespace. It derives from the
    /// WPF-compatible <see cref="Controls.ItemsPanelTemplate"/> type so existing source
    /// values remain assignable to control properties.
    /// </summary>
    public sealed class ItemsPanelTemplate : Controls.ItemsPanelTemplate
    {
        public ItemsPanelTemplate()
        {
        }
    }
}
