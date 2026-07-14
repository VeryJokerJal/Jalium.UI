using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Xaml;
using TypeConverter = System.ComponentModel.TypeConverter;

namespace Jalium.UI.Tests;

public sealed class StyleXamlReceiverParityTests
{
    private static readonly DependencyProperty FlagProperty = DependencyProperty.Register(
        "StyleXamlReceiverFlag",
        typeof(bool),
        typeof(StyleXamlReceiverParityTests),
        new PropertyMetadata(false));

    [Fact]
    public void ReceiverAttributesAndEventArgsExposeTheWpfContracts()
    {
        Assert.Equal("ReceiveMarkupExtension", GetMarkupReceiver(typeof(Condition)));
        Assert.Equal("ReceiveTypeConverter", GetConverterReceiver(typeof(Condition)));
        Assert.Equal("ReceiveMarkupExtension", GetMarkupReceiver(typeof(DataTrigger)));
        Assert.Equal("ReceiveMarkupExtension", GetMarkupReceiver(typeof(Setter)));
        Assert.Equal("ReceiveTypeConverter", GetConverterReceiver(typeof(Setter)));
        Assert.Equal("ReceiveTypeConverter", GetConverterReceiver(typeof(Trigger)));

        var member = new XamlMember("Value");
        var value = new object();
        var args = new XamlSetValueEventArgs(member, value);

        Assert.Same(member, args.Member);
        Assert.Same(value, args.Value);
        Assert.False(args.Handled);
        args.Handled = true;
        Assert.True(args.Handled);
        args.CallBase();
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The test exercises BindingBase, whose implementation does not reflect over user types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The test exercises BindingBase, whose implementation does not emit runtime code.")]
    public void BindingBaseProvideValueAttachesExactlyOneExpressionOrReturnsTheDeclaration()
    {
        var target = new FrameworkElement();
        var binding = new Binding(nameof(FrameworkElement.Tag)) { Source = new FrameworkElement { Tag = "bound" } };
        var targetProvider = new TargetServiceProvider(target, FrameworkElement.TagProperty);

        object supplied = binding.ProvideValue(targetProvider);

        Assert.Same(supplied, target.GetBindingExpression(FrameworkElement.TagProperty));
        Assert.Equal("bound", target.Tag);
        Assert.Same(binding, binding.ProvideValue(EmptyServiceProvider.Instance));
    }

    [Fact]
    public void MarkupReceiversDispatchBindingsResourcesAndCallBaseWhenUnhandled()
    {
        var binding = new Binding("Flag");

        var condition = new Condition();
        var conditionArgs = MarkupArgs(nameof(Condition.Binding), binding);
        Condition.ReceiveMarkupExtension(condition, conditionArgs);
        Assert.True(conditionArgs.Handled);
        Assert.Same(binding, condition.Binding);

        var dataTrigger = new DataTrigger();
        var dataArgs = MarkupArgs(nameof(DataTrigger.Binding), binding);
        DataTrigger.ReceiveMarkupExtension(dataTrigger, dataArgs);
        Assert.True(dataArgs.Handled);
        Assert.Same(binding, dataTrigger.Binding);

        var setter = new Setter();
        var bindingArgs = MarkupArgs(nameof(Setter.Value), binding);
        Setter.ReceiveMarkupExtension(setter, bindingArgs);
        Assert.True(bindingArgs.Handled);
        Assert.Same(binding, setter.Value);

        var ambient = new AmbientResourceProvider();
        ambient.AddResourceDictionary(new ResourceDictionary { ["accent"] = "resolved" });
        var staticResource = new StaticResourceExtension("accent");
        var resourceArgs = new XamlSetMarkupExtensionEventArgs(
            new XamlMember(nameof(Setter.Value)),
            staticResource,
            new ResourceServiceProvider(ambient));
        Setter.ReceiveMarkupExtension(setter, resourceArgs);
        Assert.True(resourceArgs.Handled);
        Assert.Equal("resolved", setter.Value);

        var unhandled = new TrackingMarkupExtensionEventArgs(
            new XamlMember(nameof(DataTrigger.Value)),
            new ProbeMarkupExtension(),
            EmptyServiceProvider.Instance);
        DataTrigger.ReceiveMarkupExtension(dataTrigger, unhandled);
        Assert.False(unhandled.Handled);
        Assert.True(unhandled.BaseCalled);
    }

    [Fact]
    public void TypeConverterReceiversDeferPropertyThenValueAndHonorHandledState()
    {
        var context = new ProbeTypeDescriptorContext();
        var propertyConverter = new MappingConverter(_ => FlagProperty);
        var valueConverter = new MappingConverter(_ => true);

        var setter = new Setter();
        InitializeWithReceivers(
            setter,
            Setter.ReceiveTypeConverter,
            propertyConverter,
            valueConverter,
            context);
        Assert.Same(FlagProperty, setter.Property);
        Assert.Equal(true, setter.Value);

        var condition = new Condition();
        InitializeWithReceivers(
            condition,
            Condition.ReceiveTypeConverter,
            propertyConverter,
            valueConverter,
            context);
        Assert.Same(FlagProperty, condition.Property);
        Assert.Equal(true, condition.Value);

        var trigger = new Trigger();
        InitializeWithReceivers(
            trigger,
            Trigger.ReceiveTypeConverter,
            propertyConverter,
            valueConverter,
            context);
        Assert.Same(FlagProperty, trigger.Property);
        Assert.Equal(true, trigger.Value);

        var ignored = ConverterArgs("Other", propertyConverter, "Flag", context);
        Trigger.ReceiveTypeConverter(new Trigger(), ignored);
        Assert.False(ignored.Handled);
    }

    [Fact]
    public void SealedReceiverTargetsRejectMutations()
    {
        var binding = new Binding("Flag");
        var setter = new Setter(FlagProperty, true);
        var condition = new Condition(FlagProperty, true);
        var trigger = new Trigger { Property = FlagProperty, Value = true };
        var dataTrigger = new DataTrigger { Binding = binding, Value = true };
        var multiTrigger = new MultiTrigger();
        multiTrigger.Conditions.Add(condition);

        var style = new Style();
        style.Setters.Add(setter);
        style.Triggers.Add(trigger);
        style.Triggers.Add(dataTrigger);
        style.Triggers.Add(multiTrigger);
        style.Seal();

        var context = new ProbeTypeDescriptorContext();
        var converterArgs = ConverterArgs(nameof(Setter.Value), new MappingConverter(_ => false), "False", context);
        Assert.Throws<InvalidOperationException>(() => Setter.ReceiveTypeConverter(setter, converterArgs));
        Assert.Throws<InvalidOperationException>(() => Condition.ReceiveMarkupExtension(condition, MarkupArgs(nameof(Condition.Binding), binding)));
        Assert.Throws<InvalidOperationException>(() => Trigger.ReceiveTypeConverter(
            trigger,
            ConverterArgs(nameof(Trigger.Value), new MappingConverter(_ => false), "False", context)));
        Assert.Throws<InvalidOperationException>(() => DataTrigger.ReceiveMarkupExtension(
            dataTrigger,
            MarkupArgs(nameof(DataTrigger.Binding), new Binding("Other"))));
    }

    [Fact]
    public void TriggerActionsTrackTheirOwnerSealWithTheTriggerAndRunOnTransitions()
    {
        var trigger = new Trigger { Property = FlagProperty, Value = true };
        var enter = new ProbeAction();
        var exit = new ProbeAction();
        trigger.EnterActions.Add(enter);
        trigger.ExitActions.Add(exit);

        Assert.Same(trigger, enter.ContainingTrigger);
        Assert.Throws<InvalidOperationException>(() => new Trigger().EnterActions.Add(enter));

        var detached = new ProbeAction();
        trigger.EnterActions.Add(detached);
        Assert.True(trigger.EnterActions.Remove(detached));
        Assert.Null(detached.ContainingTrigger);
        var newOwner = new Trigger();
        newOwner.EnterActions.Add(detached);
        Assert.Same(newOwner, detached.ContainingTrigger);

        var style = new Style();
        style.Triggers.Add(trigger);
        style.Seal();
        Assert.True(trigger.IsSealed);
        Assert.True(trigger.EnterActions.IsReadOnly);
        Assert.True(enter.IsSealed);
        Assert.Throws<InvalidOperationException>(() => trigger.EnterActions.Add(new ProbeAction()));

        var element = new FrameworkElement();
        style.Apply(element);
        element.SetValue(FlagProperty, true);
        element.SetValue(FlagProperty, false);

        Assert.Equal(1, enter.InvocationCount);
        Assert.Equal(1, exit.InvocationCount);
    }

    private static string? GetMarkupReceiver(Type type)
        => type.GetCustomAttribute<XamlSetMarkupExtensionAttribute>()?.XamlSetMarkupExtensionHandler;

    private static string? GetConverterReceiver(Type type)
        => type.GetCustomAttribute<XamlSetTypeConverterAttribute>()?.XamlSetTypeConverterHandler;

    private static XamlSetMarkupExtensionEventArgs MarkupArgs(string memberName, MarkupExtension extension)
        => new(new XamlMember(memberName), extension, EmptyServiceProvider.Instance);

    private static XamlSetTypeConverterEventArgs ConverterArgs(
        string memberName,
        TypeConverter converter,
        object value,
        ITypeDescriptorContext context)
        => new(new XamlMember(memberName), converter, value, context, CultureInfo.InvariantCulture);

    private static void InitializeWithReceivers(
        ISupportInitialize target,
        Action<object, XamlSetTypeConverterEventArgs> receiver,
        TypeConverter propertyConverter,
        TypeConverter valueConverter,
        ITypeDescriptorContext context)
    {
        target.BeginInit();
        var propertyArgs = ConverterArgs("Property", propertyConverter, "Flag", context);
        var valueArgs = ConverterArgs("Value", valueConverter, "True", context);
        receiver(target, propertyArgs);
        receiver(target, valueArgs);
        Assert.True(propertyArgs.Handled);
        Assert.True(valueArgs.Handled);
        target.EndInit();
    }

    private sealed class TargetServiceProvider : IServiceProvider, IProvideValueTarget
    {
        public TargetServiceProvider(object targetObject, object targetProperty)
        {
            TargetObject = targetObject;
            TargetProperty = targetProperty;
        }

        public object TargetObject { get; }

        public object TargetProperty { get; }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class ResourceServiceProvider : IServiceProvider
    {
        private readonly IAmbientResourceProvider _ambient;

        public ResourceServiceProvider(IAmbientResourceProvider ambient)
        {
            _ambient = ambient;
        }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IAmbientResourceProvider) ? _ambient : null;
    }

    private sealed class ProbeTypeDescriptorContext : ITypeDescriptorContext
    {
        public IContainer? Container => null;

        public object? Instance => null;

        public PropertyDescriptor? PropertyDescriptor => null;

        public object? GetService(Type serviceType) => null;

        public void OnComponentChanged()
        {
        }

        public bool OnComponentChanging() => true;
    }

    private sealed class MappingConverter : TypeConverter
    {
        private readonly Func<object?, object?> _map;

        public MappingConverter(Func<object?, object?> map)
        {
            _map = map;
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => _map(value);
    }

    private sealed class ProbeMarkupExtension : MarkupExtension
    {
        [RequiresUnreferencedCode("Matches the annotated MarkupExtension contract.")]
        [RequiresDynamicCode("Matches the annotated MarkupExtension contract.")]
        public override object? ProvideValue(IServiceProvider serviceProvider) => this;
    }

    private sealed class TrackingMarkupExtensionEventArgs : XamlSetMarkupExtensionEventArgs
    {
        public TrackingMarkupExtensionEventArgs(
            XamlMember member,
            MarkupExtension value,
            IServiceProvider serviceProvider)
            : base(member, value, serviceProvider)
        {
        }

        public bool BaseCalled { get; private set; }

        public override void CallBase()
        {
            BaseCalled = true;
        }
    }

    private sealed class ProbeAction : TriggerAction
    {
        public int InvocationCount { get; private set; }

        internal override void Invoke(FrameworkElement? element)
        {
            InvocationCount++;
        }
    }
}
