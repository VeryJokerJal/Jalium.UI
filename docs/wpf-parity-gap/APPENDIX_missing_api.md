# 附录：缺失类型的完整待补公开 API（Tier1/Tier2）

## System.Windows.AttachedPropertyBrowsableAttribute  (class)  : System.Attribute
- ctor: `protected AttachedPropertyBrowsableAttribute()`
- meth: `internal abstract bool IsBrowsable(System.Windows.DependencyObject d, System.Windows.DependencyProperty dp)`

## System.Windows.AttachedPropertyBrowsableForChildrenAttribute  (class)  : System.Windows.AttachedPropertyBrowsableAttribute
- ctor: `public AttachedPropertyBrowsableForChildrenAttribute()`
- prop: `public bool IncludeDescendants`
- meth: `public override bool Equals(object obj)`
- meth: `public override int GetHashCode()`
- meth: `internal override bool IsBrowsable(System.Windows.DependencyObject d, System.Windows.DependencyProperty dp)`

## System.Windows.AttachedPropertyBrowsableForTypeAttribute  (class)  : System.Windows.AttachedPropertyBrowsableAttribute
- ctor: `public AttachedPropertyBrowsableForTypeAttribute(System.Type targetType)`
- prop: `public System.Type TargetType`
- prop: `public override object TypeId`
- meth: `public override bool Equals(object obj)`
- meth: `public override int GetHashCode()`
- meth: `internal override bool IsBrowsable(System.Windows.DependencyObject d, System.Windows.DependencyProperty dp)`

## System.Windows.AttachedPropertyBrowsableWhenAttributePresentAttribute  (class)  : System.Windows.AttachedPropertyBrowsableAttribute
- ctor: `public AttachedPropertyBrowsableWhenAttributePresentAttribute(System.Type attributeType)`
- prop: `public System.Type AttributeType`
- meth: `public override bool Equals(object obj)`
- meth: `public override int GetHashCode()`
- meth: `internal override bool IsBrowsable(System.Windows.DependencyObject d, System.Windows.DependencyProperty dp)`

## System.Windows.AutoResizedEventArgs  (class)  : System.EventArgs
- ctor: `public AutoResizedEventArgs(System.Windows.Size size)`
- prop: `public System.Windows.Size Size`

## System.Windows.BaseCompatibilityPreferences  (class)  :
- prop: `public static bool FlowDispatcherSynchronizationContextPriority`
- prop: `public static System.Windows.BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions HandleDispatcherRequestProcessingFailure`
- prop: `public static bool InlineDispatcherSynchronizationContextSend`
- prop: `public static bool ReuseDispatcherSynchronizationContextInstance`
- enum-value: `Continue = 0`
- enum-value: `Throw = 1`
- enum-value: `Reset = 2`

## System.Windows.ColumnSpaceDistribution  (enum)  :
- enum-value: `Left = 0`
- enum-value: `Right = 1`
- enum-value: `Between = 2`

## System.Windows.ConditionCollection  (class)  : System.Collections.ObjectModel.Collection<System.Windows.Condition>
- ctor: `public ConditionCollection()`
- prop: `public bool IsSealed`
- meth: `protected override void ClearItems()`
- meth: `protected override void InsertItem(int index, System.Windows.Condition item)`
- meth: `protected override void RemoveItem(int index)`
- meth: `protected override void SetItem(int index, System.Windows.Condition item)`

## System.Windows.ContentOperations  (class)  :
- meth: `public static System.Windows.DependencyObject GetParent(System.Windows.ContentElement reference)`
- meth: `public static void SetParent(System.Windows.ContentElement reference, System.Windows.DependencyObject parent)`

## System.Windows.CoreCompatibilityPreferences  (class)  :
- prop: `public static bool? EnableMultiMonitorDisplayClipping`
- prop: `public static bool IsAltKeyRequiredInAccessKeyDefaultScope`

## System.Windows.CultureInfoIetfLanguageTagConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public CultureInfoIetfLanguageTagConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.DataFormat  (class)  :
- ctor: `public DataFormat(string name, int id)`
- prop: `public int Id`
- prop: `public string Name`

## System.Windows.DataObjectCopyingEventArgs  (class)  : System.Windows.DataObjectEventArgs
- ctor: `public DataObjectCopyingEventArgs(System.Windows.IDataObject dataObject, bool isDragDrop)`
- prop: `public System.Windows.IDataObject DataObject`
- meth: `protected override void InvokeEventHandler(System.Delegate genericHandler, object genericTarget)`

## System.Windows.DataObjectEventArgs  (class)  : System.Windows.RoutedEventArgs
- ctor: `internal DataObjectEventArgs()`
- prop: `public bool CommandCancelled`
- prop: `public bool IsDragDrop`
- meth: `public void CancelCommand()`

## System.Windows.DataObjectExtensions  (class)  :
- meth: `public static bool TryGetData<T>(this IDataObject dataObject, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`
- meth: `public static bool TryGetData<T>(this IDataObject dataObject, string format, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`
- meth: `public static bool TryGetData<T>(this IDataObject dataObject, string format, bool autoConvert, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`
- meth: `public static bool TryGetData<T>(this IDataObject dataObject, string format, Func<Reflection.Metadata.TypeName, Type> resolver, bool autoConvert, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`

## System.Windows.DataObjectPastingEventArgs  (class)  : System.Windows.DataObjectEventArgs
- ctor: `public DataObjectPastingEventArgs(System.Windows.IDataObject dataObject, bool isDragDrop, string formatToApply)`
- prop: `public System.Windows.IDataObject DataObject`
- prop: `public string FormatToApply`
- prop: `public System.Windows.IDataObject SourceDataObject`
- meth: `protected override void InvokeEventHandler(System.Delegate genericHandler, object genericTarget)`

## System.Windows.DataObjectSettingDataEventArgs  (class)  : System.Windows.DataObjectEventArgs
- ctor: `public DataObjectSettingDataEventArgs(System.Windows.IDataObject dataObject, string format)`
- prop: `public System.Windows.IDataObject DataObject`
- prop: `public string Format`
- meth: `protected override void InvokeEventHandler(System.Delegate genericHandler, object genericTarget)`

## System.Windows.DeferrableContent  (class)  :
- ctor: `internal DeferrableContent()`

## System.Windows.DeferrableContentConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public DeferrableContentConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`

## System.Windows.DialogResultConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public DialogResultConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.DynamicResourceExtensionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public DynamicResourceExtensionConverter()`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.EventPrivateKey  (class)  :
- ctor: `public EventPrivateKey()`

## System.Windows.Expression  (class)  :
- ctor: `internal Expression()`

## System.Windows.ExpressionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public ExpressionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.FigureLengthConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public FigureLengthConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.FontEastAsianLanguage  (enum)  :
- enum-value: `Normal = 0`
- enum-value: `Jis78 = 1`
- enum-value: `Jis83 = 2`
- enum-value: `Jis90 = 3`
- enum-value: `Jis04 = 4`
- enum-value: `HojoKanji = 5`
- enum-value: `NlcKanji = 6`
- enum-value: `Simplified = 7`
- enum-value: `Traditional = 8`
- enum-value: `TraditionalNames = 9`

## System.Windows.FontEastAsianWidths  (enum)  :
- enum-value: `Normal = 0`
- enum-value: `Proportional = 1`
- enum-value: `Full = 2`
- enum-value: `Half = 3`
- enum-value: `Third = 4`
- enum-value: `Quarter = 5`

## System.Windows.FontFraction  (enum)  :
- enum-value: `Normal = 0`
- enum-value: `Slashed = 1`
- enum-value: `Stacked = 2`

## System.Windows.FontSizeConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public FontSizeConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.FontStretchConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public FontStretchConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext td, System.Type t)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext td, System.Globalization.CultureInfo ci, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.FontStyleConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public FontStyleConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext td, System.Type t)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext td, System.Globalization.CultureInfo ci, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.FontWeightConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public FontWeightConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext td, System.Type t)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext td, System.Globalization.CultureInfo ci, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.FrameworkCompatibilityPreferences  (class)  :
- prop: `public static bool AreInactiveSelectionHighlightBrushKeysSupported`
- prop: `public static bool KeepTextBoxDisplaySynchronizedWithTextProperty`
- prop: `public static bool ShouldThrowOnCopyOrCutFailure`

## System.Windows.FrameworkElementFactory  (class)  :
- ctor: `public FrameworkElementFactory()`
- ctor: `public FrameworkElementFactory(string text)`
- ctor: `public FrameworkElementFactory(System.Type type)`
- ctor: `public FrameworkElementFactory(System.Type type, string name)`
- prop: `public System.Windows.FrameworkElementFactory FirstChild`
- prop: `public bool IsSealed`
- prop: `public string Name`
- prop: `public System.Windows.FrameworkElementFactory NextSibling`
- prop: `public System.Windows.FrameworkElementFactory Parent`
- prop: `public string Text`
- prop: `public System.Type Type`
- meth: `public void AddHandler(System.Windows.RoutedEvent routedEvent, System.Delegate handler)`
- meth: `public void AddHandler(System.Windows.RoutedEvent routedEvent, System.Delegate handler, bool handledEventsToo)`
- meth: `public void AppendChild(System.Windows.FrameworkElementFactory child)`
- meth: `public void RemoveHandler(System.Windows.RoutedEvent routedEvent, System.Delegate handler)`
- meth: `public void SetBinding(System.Windows.DependencyProperty dp, System.Windows.Data.BindingBase binding)`
- meth: `public void SetResourceReference(System.Windows.DependencyProperty dp, object name)`
- meth: `public void SetValue(System.Windows.DependencyProperty dp, object value)`

## System.Windows.FrameworkTemplate  (class)  : System.Windows.Threading.DispatcherObject, System.Windows.Markup.INameScope, System.Windows.Markup.IQueryAmbient
- ctor: `protected FrameworkTemplate()`
- prop: `public bool HasContent`
- prop: `public bool IsSealed`
- prop: `public System.Windows.ResourceDictionary Resources`
- prop: `public System.Windows.TemplateContent Template`
- prop: `public System.Windows.FrameworkElementFactory VisualTree`
- meth: `public object FindName(string name, System.Windows.FrameworkElement templatedParent)`
- meth: `public System.Windows.DependencyObject LoadContent()`
- meth: `public void RegisterName(string name, object scopedElement)`
- meth: `public void Seal()`
- meth: `public bool ShouldSerializeResources(System.Windows.Markup.XamlDesignerSerializationManager manager)`
- meth: `public bool ShouldSerializeVisualTree()`
- meth: `object System.Windows.Markup.INameScope.FindName(string name)`
- meth: `bool System.Windows.Markup.IQueryAmbient.IsAmbientPropertyAvailable(string propertyName)`
- meth: `public void UnregisterName(string name)`
- meth: `protected virtual void ValidateTemplatedParent(System.Windows.FrameworkElement templatedParent)`

## System.Windows.HwndDpiChangedEventArgs  (class)  : System.ComponentModel.HandledEventArgs
- ctor: `internal HwndDpiChangedEventArgs()`
- prop: `public System.Windows.DpiScale NewDpi`
- prop: `public System.Windows.DpiScale OldDpi`
- prop: `public System.Windows.Rect SuggestedRect`

## System.Windows.IContentHost  (interface)  :
- prop: `System.Collections.Generic.IEnumerator<System.Windows.IInputElement> HostedElements`
- meth: `System.Collections.ObjectModel.ReadOnlyCollection<System.Windows.Rect> GetRectangles(System.Windows.ContentElement child)`
- meth: `System.Windows.IInputElement InputHitTest(System.Windows.Point point)`
- meth: `void OnChildDesiredSizeChanged(System.Windows.UIElement child)`

## System.Windows.IFrameworkInputElement  (interface)  : System.Windows.IInputElement
- prop: `string Name`

## System.Windows.ITypedDataObject  (interface)  : System.Windows.IDataObject
- meth: `bool TryGetData<T>([NotNullWhen(true), MaybeNullWhen(false)] out T data)`
- meth: `bool TryGetData<T>(string format, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`
- meth: `bool TryGetData<T>(string format, bool autoConvert, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`
- meth: `bool TryGetData<T>(string format, Func<Reflection.Metadata.TypeName, Type> resolver, bool autoConvert, [NotNullWhen(true), MaybeNullWhen(false)] out T data)`

## System.Windows.InheritanceBehavior  (enum)  :
- enum-value: `Default = 0`
- enum-value: `SkipToAppNow = 1`
- enum-value: `SkipToAppNext = 2`
- enum-value: `SkipToThemeNow = 3`
- enum-value: `SkipToThemeNext = 4`
- enum-value: `SkipAllNow = 5`
- enum-value: `SkipAllNext = 6`

## System.Windows.Int32RectConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public Int32RectConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.LengthConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public LengthConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.LineBreakCondition  (enum)  :
- enum-value: `BreakDesired = 0`
- enum-value: `BreakPossible = 1`
- enum-value: `BreakRestrained = 2`
- enum-value: `BreakAlways = 3`

## System.Windows.LineStackingStrategy  (enum)  :
- enum-value: `BlockLineHeight = 0`
- enum-value: `MaxHeight = 1`

## System.Windows.LostFocusEventManager  (class)  : System.Windows.WeakEventManager
- ctor: `internal LostFocusEventManager()`
- meth: `public static void AddHandler(System.Windows.DependencyObject source, System.EventHandler<System.Windows.RoutedEventArgs> handler)`
- meth: `public static void AddListener(System.Windows.DependencyObject source, System.Windows.IWeakEventListener listener)`
- meth: `protected override System.Windows.WeakEventManager.ListenerList NewListenerList()`
- meth: `public static void RemoveHandler(System.Windows.DependencyObject source, System.EventHandler<System.Windows.RoutedEventArgs> handler)`
- meth: `public static void RemoveListener(System.Windows.DependencyObject source, System.Windows.IWeakEventListener listener)`
- meth: `protected override void StartListening(object source)`
- meth: `protected override void StopListening(object source)`

## System.Windows.NullableBoolConverter  (class)  : System.ComponentModel.NullableConverter
- ctor: `public NullableBoolConverter() : base (default(System.Type))`
- meth: `public override System.ComponentModel.TypeConverter.StandardValuesCollection GetStandardValues(System.ComponentModel.ITypeDescriptorContext context)`
- meth: `public override bool GetStandardValuesExclusive(System.ComponentModel.ITypeDescriptorContext context)`
- meth: `public override bool GetStandardValuesSupported(System.ComponentModel.ITypeDescriptorContext context)`

## System.Windows.PowerLineStatus  (enum)  :
- enum-value: `Offline = 0`
- enum-value: `Online = 1`
- enum-value: `Unknown = 255`

## System.Windows.PresentationSource  (class)  : System.Windows.Threading.DispatcherObject
- ctor: `protected PresentationSource()`
- prop: `public System.Windows.Media.CompositionTarget CompositionTarget`
- prop: `public static System.Collections.IEnumerable CurrentSources`
- prop: `public abstract bool IsDisposed`
- prop: `public abstract System.Windows.Media.Visual RootVisual`
- meth: `protected void AddSource()`
- meth: `public static void AddSourceChangedHandler(System.Windows.IInputElement element, System.Windows.SourceChangedEventHandler handler)`
- meth: `protected void ClearContentRenderedListeners()`
- meth: `public static System.Windows.PresentationSource FromDependencyObject(System.Windows.DependencyObject dependencyObject)`
- meth: `public static System.Windows.PresentationSource FromVisual(System.Windows.Media.Visual visual)`
- meth: `protected abstract System.Windows.Media.CompositionTarget GetCompositionTargetCore()`
- meth: `protected void RemoveSource()`
- meth: `public static void RemoveSourceChangedHandler(System.Windows.IInputElement e, System.Windows.SourceChangedEventHandler handler)`
- meth: `protected void RootChanged(System.Windows.Media.Visual oldRoot, System.Windows.Media.Visual newRoot)`
- event: `public event System.EventHandler ContentRendered { add { } remove`

## System.Windows.RectConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public RectConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.ResourceDictionaryLocation  (enum)  :
- enum-value: `None = 0`
- enum-value: `SourceAssembly = 1`
- enum-value: `ExternalAssembly = 2`

## System.Windows.ResourceKey  (class)  : System.Windows.Markup.MarkupExtension
- ctor: `protected ResourceKey()`
- prop: `public abstract System.Reflection.Assembly Assembly`
- meth: `public override object ProvideValue(System.IServiceProvider serviceProvider)`

## System.Windows.ResourceReferenceKeyNotFoundException  (class)  : System.InvalidOperationException
- ctor: `public ResourceReferenceKeyNotFoundException()`
- ctor: `protected ResourceReferenceKeyNotFoundException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public ResourceReferenceKeyNotFoundException(string message, object resourceKey)`
- prop: `public object Key`
- meth: `public override void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`

## System.Windows.SetterBase  (class)  :
- ctor: `internal SetterBase()`
- prop: `public bool IsSealed`
- meth: `protected void CheckSealed()`

## System.Windows.SetterBaseCollection  (class)  : System.Collections.ObjectModel.Collection<System.Windows.SetterBase>
- ctor: `public SetterBaseCollection()`
- prop: `public bool IsSealed`
- meth: `protected override void ClearItems()`
- meth: `protected override void InsertItem(int index, System.Windows.SetterBase item)`
- meth: `protected override void RemoveItem(int index)`
- meth: `protected override void SetItem(int index, System.Windows.SetterBase item)`

## System.Windows.SourceChangedEventArgs  (class)  : System.Windows.RoutedEventArgs
- ctor: `public SourceChangedEventArgs(System.Windows.PresentationSource oldSource, System.Windows.PresentationSource newSource)`
- ctor: `public SourceChangedEventArgs(System.Windows.PresentationSource oldSource, System.Windows.PresentationSource newSource, System.Windows.IInputElement element, System.Windows.IInputElement oldParent)`
- prop: `public System.Windows.IInputElement Element`
- prop: `public System.Windows.PresentationSource NewSource`
- prop: `public System.Windows.IInputElement OldParent`
- prop: `public System.Windows.PresentationSource OldSource`
- meth: `protected override void InvokeEventHandler(System.Delegate genericHandler, object genericTarget)`

## System.Windows.StrokeCollectionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public StrokeCollectionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`
- meth: `public override bool GetStandardValuesSupported(System.ComponentModel.ITypeDescriptorContext context)`

## System.Windows.StyleTypedPropertyAttribute  (class)  : System.Attribute
- ctor: `public StyleTypedPropertyAttribute()`
- prop: `public string Property`
- prop: `public System.Type StyleTargetType`

## System.Windows.TemplateBindingExpressionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public TemplateBindingExpressionConverter()`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.TemplateBindingExtensionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public TemplateBindingExtensionConverter()`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.TemplateContent  (class)  :
- ctor: `internal TemplateContent()`

## System.Windows.TemplateContentLoader  (class)  : System.Xaml.XamlDeferringLoader
- ctor: `public TemplateContentLoader()`
- meth: `public override object Load(System.Xaml.XamlReader xamlReader, System.IServiceProvider serviceProvider)`
- meth: `public override System.Xaml.XamlReader Save(object value, System.IServiceProvider serviceProvider)`

## System.Windows.TextDataFormat  (enum)  :
- enum-value: `Text = 0`
- enum-value: `UnicodeText = 1`
- enum-value: `Rtf = 2`
- enum-value: `Html = 3`
- enum-value: `CommaSeparatedValue = 4`
- enum-value: `Xaml = 5`

## System.Windows.TextDecorationCollectionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public TextDecorationCollectionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object input)`
- meth: `public static new System.Windows.TextDecorationCollection ConvertFromString(string text)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.ThemeInfoAttribute  (class)  : System.Attribute
- ctor: `public ThemeInfoAttribute(System.Windows.ResourceDictionaryLocation themeDictionaryLocation, System.Windows.ResourceDictionaryLocation genericDictionaryLocation)`
- prop: `public System.Windows.ResourceDictionaryLocation GenericDictionaryLocation`
- prop: `public System.Windows.ResourceDictionaryLocation ThemeDictionaryLocation`

## System.Windows.ThemeMode  (struct)  : System.IEquatable<System.Windows.ThemeMode>
- ctor: `public ThemeMode(string value)`
- prop: `public static System.Windows.ThemeMode Dark`
- prop: `public static System.Windows.ThemeMode Light`
- prop: `public static System.Windows.ThemeMode None`
- prop: `public static System.Windows.ThemeMode System`
- prop: `public string Value`
- meth: `public override bool Equals(object obj)`
- meth: `public bool Equals(System.Windows.ThemeMode other)`
- meth: `public override int GetHashCode()`
- meth: `public static bool operator ==(System.Windows.ThemeMode left, System.Windows.ThemeMode right)`
- meth: `public static bool operator !=(System.Windows.ThemeMode left, System.Windows.ThemeMode right)`
- meth: `public override string ToString()`

## System.Windows.ThemeModeConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public ThemeModeConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.VisualStateChangedEventArgs  (class)  : System.EventArgs
- ctor: `internal VisualStateChangedEventArgs()`
- prop: `public System.Windows.FrameworkElement Control`
- prop: `public System.Windows.VisualState NewState`
- prop: `public System.Windows.VisualState OldState`
- prop: `public System.Windows.FrameworkElement StateGroupsRoot`

## System.Windows.WindowCollection  (class)  : System.Collections.ICollection, System.Collections.IEnumerable
- ctor: `public WindowCollection()`
- prop: `public int Count`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Window this[int index]`
- prop: `public object SyncRoot`
- meth: `public void CopyTo(System.Windows.Window[] array, int index)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`

## System.Windows.Controls.DataGridCellsPanel  (class)  : System.Windows.Controls.VirtualizingPanel
- ctor: `public DataGridCellsPanel()`
- meth: `protected override System.Windows.Size ArrangeOverride(System.Windows.Size arrangeSize)`
- meth: `protected internal override void BringIndexIntoView(int index)`
- meth: `protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)`
- meth: `protected override void OnClearChildren()`
- meth: `protected override void OnIsItemsHostChanged(bool oldIsItemsHost, bool newIsItemsHost)`
- meth: `protected override void OnItemsChanged(object sender, System.Windows.Controls.Primitives.ItemsChangedEventArgs args)`

## System.Windows.Controls.DataGridLength  (struct)  : System.IEquatable<System.Windows.Controls.DataGridLength>
- ctor: `public DataGridLength(double pixels)`
- ctor: `public DataGridLength(double value, System.Windows.Controls.DataGridLengthUnitType type)`
- ctor: `public DataGridLength(double value, System.Windows.Controls.DataGridLengthUnitType type, double desiredValue, double displayValue)`
- prop: `public static System.Windows.Controls.DataGridLength Auto`
- prop: `public double DesiredValue`
- prop: `public double DisplayValue`
- prop: `public bool IsAbsolute`
- prop: `public bool IsAuto`
- prop: `public bool IsSizeToCells`
- prop: `public bool IsSizeToHeader`
- prop: `public bool IsStar`
- prop: `public static System.Windows.Controls.DataGridLength SizeToCells`
- prop: `public static System.Windows.Controls.DataGridLength SizeToHeader`
- prop: `public System.Windows.Controls.DataGridLengthUnitType UnitType`
- prop: `public double Value`
- meth: `public override bool Equals(object obj)`
- meth: `public bool Equals(System.Windows.Controls.DataGridLength other)`
- meth: `public override int GetHashCode()`
- meth: `public static bool operator ==(System.Windows.Controls.DataGridLength gl1, System.Windows.Controls.DataGridLength gl2)`
- meth: `public static implicit operator System.Windows.Controls.DataGridLength (double value)`
- meth: `public static bool operator !=(System.Windows.Controls.DataGridLength gl1, System.Windows.Controls.DataGridLength gl2)`
- meth: `public override string ToString()`

## System.Windows.Controls.DataGridLengthConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public DataGridLengthConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Controls.DataGridLengthUnitType  (enum)  :
- enum-value: `Auto = 0`
- enum-value: `Pixel = 1`
- enum-value: `SizeToCells = 2`
- enum-value: `SizeToHeader = 3`
- enum-value: `Star = 4`

## System.Windows.Controls.InkCanvasSelectionChangingEventArgs  (class)  : System.ComponentModel.CancelEventArgs
- ctor: `internal InkCanvasSelectionChangingEventArgs()`
- meth: `public System.Collections.ObjectModel.ReadOnlyCollection<System.Windows.UIElement> GetSelectedElements()`
- meth: `public System.Windows.Ink.StrokeCollection GetSelectedStrokes()`
- meth: `public void SetSelectedElements(System.Collections.Generic.IEnumerable<System.Windows.UIElement> selectedElements)`
- meth: `public void SetSelectedStrokes(System.Windows.Ink.StrokeCollection selectedStrokes)`

## System.Windows.Controls.InkCanvasStrokesReplacedEventArgs  (class)  : System.EventArgs
- ctor: `internal InkCanvasStrokesReplacedEventArgs()`
- prop: `public System.Windows.Ink.StrokeCollection NewStrokes`
- prop: `public System.Windows.Ink.StrokeCollection PreviousStrokes`

## System.Windows.Controls.StickyNoteControl  (class)  : System.Windows.Controls.Control
- ctor: `internal StickyNoteControl()`
- prop: `public System.Windows.Annotations.IAnchorInfo AnchorInfo`
- prop: `public string Author`
- prop: `public System.Windows.Media.FontFamily CaptionFontFamily`
- prop: `public double CaptionFontSize`
- prop: `public System.Windows.FontStretch CaptionFontStretch`
- prop: `public System.Windows.FontStyle CaptionFontStyle`
- prop: `public System.Windows.FontWeight CaptionFontWeight`
- prop: `public bool IsActive`
- prop: `public bool IsExpanded`
- prop: `public bool IsMouseOverAnchor`
- prop: `public double PenWidth`
- prop: `public System.Windows.Controls.StickyNoteType StickyNoteType`
- meth: `public override void OnApplyTemplate()`
- meth: `protected override void OnGotKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs args)`
- meth: `protected override void OnIsKeyboardFocusWithinChanged(System.Windows.DependencyPropertyChangedEventArgs args)`
- meth: `protected override void OnTemplateChanged(System.Windows.Controls.ControlTemplate oldTemplate, System.Windows.Controls.ControlTemplate newTemplate)`
- field: `public static readonly System.Windows.DependencyProperty AuthorProperty`
- field: `public static readonly System.Windows.DependencyProperty CaptionFontFamilyProperty`
- field: `public static readonly System.Windows.DependencyProperty CaptionFontSizeProperty`
- field: `public static readonly System.Windows.DependencyProperty CaptionFontStretchProperty`
- field: `public static readonly System.Windows.DependencyProperty CaptionFontStyleProperty`
- field: `public static readonly System.Windows.DependencyProperty CaptionFontWeightProperty`
- field: `public static readonly System.Windows.Input.RoutedCommand DeleteNoteCommand`
- field: `public static readonly System.Windows.Input.RoutedCommand InkCommand`
- field: `public static readonly System.Xml.XmlQualifiedName InkSchemaName`
- field: `public static readonly System.Windows.DependencyProperty IsActiveProperty`
- field: `public static readonly System.Windows.DependencyProperty IsExpandedProperty`
- field: `public static readonly System.Windows.DependencyProperty IsMouseOverAnchorProperty`
- field: `public static readonly System.Windows.DependencyProperty PenWidthProperty`
- field: `public static readonly System.Windows.DependencyProperty StickyNoteTypeProperty`
- field: `public static readonly System.Xml.XmlQualifiedName TextSchemaName`

## System.Windows.Controls.StickyNoteType  (enum)  :
- enum-value: `Text = 0`
- enum-value: `Ink = 1`

## System.ComponentModel.CurrentChangedEventManager  (class)  : System.Windows.WeakEventManager
- ctor: `internal CurrentChangedEventManager()`
- meth: `public static void AddHandler(System.ComponentModel.ICollectionView source, System.EventHandler<System.EventArgs> handler)`
- meth: `public static void AddListener(System.ComponentModel.ICollectionView source, System.Windows.IWeakEventListener listener)`
- meth: `protected override System.Windows.WeakEventManager.ListenerList NewListenerList()`
- meth: `public static void RemoveHandler(System.ComponentModel.ICollectionView source, System.EventHandler<System.EventArgs> handler)`
- meth: `public static void RemoveListener(System.ComponentModel.ICollectionView source, System.Windows.IWeakEventListener listener)`
- meth: `protected override void StartListening(object source)`
- meth: `protected override void StopListening(object source)`

## System.ComponentModel.CurrentChangingEventManager  (class)  : System.Windows.WeakEventManager
- ctor: `internal CurrentChangingEventManager()`
- meth: `public static void AddHandler(System.ComponentModel.ICollectionView source, System.EventHandler<System.ComponentModel.CurrentChangingEventArgs> handler)`
- meth: `public static void AddListener(System.ComponentModel.ICollectionView source, System.Windows.IWeakEventListener listener)`
- meth: `protected override System.Windows.WeakEventManager.ListenerList NewListenerList()`
- meth: `public static void RemoveHandler(System.ComponentModel.ICollectionView source, System.EventHandler<System.ComponentModel.CurrentChangingEventArgs> handler)`
- meth: `public static void RemoveListener(System.ComponentModel.ICollectionView source, System.Windows.IWeakEventListener listener)`
- meth: `protected override void StartListening(object source)`
- meth: `protected override void StopListening(object source)`

## System.ComponentModel.ErrorsChangedEventManager  (class)  : System.Windows.WeakEventManager
- ctor: `internal ErrorsChangedEventManager()`
- meth: `public static void AddHandler(System.ComponentModel.INotifyDataErrorInfo source, System.EventHandler<System.ComponentModel.DataErrorsChangedEventArgs> handler)`
- meth: `protected override System.Windows.WeakEventManager.ListenerList NewListenerList()`
- meth: `public static void RemoveHandler(System.ComponentModel.INotifyDataErrorInfo source, System.EventHandler<System.ComponentModel.DataErrorsChangedEventArgs> handler)`
- meth: `protected override void StartListening(object source)`
- meth: `protected override void StopListening(object source)`

## System.ComponentModel.ICollectionViewFactory  (interface)  :
- meth: `System.ComponentModel.ICollectionView CreateView()`

## System.ComponentModel.IItemProperties  (interface)  :
- prop: `System.Collections.ObjectModel.ReadOnlyCollection<System.ComponentModel.ItemPropertyInfo> ItemProperties`

## System.ComponentModel.ItemPropertyInfo  (class)  :
- ctor: `public ItemPropertyInfo(string name, System.Type type, object descriptor)`
- prop: `public object Descriptor`
- prop: `public string Name`
- prop: `public System.Type PropertyType`

## System.ComponentModel.PropertyFilterAttribute  (class)  : System.Attribute
- ctor: `public PropertyFilterAttribute(System.ComponentModel.PropertyFilterOptions filter)`
- prop: `public System.ComponentModel.PropertyFilterOptions Filter`
- meth: `public override bool Equals(object value)`
- meth: `public override int GetHashCode()`
- meth: `public override bool Match(object value)`
- field: `public static readonly System.ComponentModel.PropertyFilterAttribute Default`

## System.ComponentModel.PropertyFilterOptions  (enum)  :
- enum-value: `None = 0`
- enum-value: `Invalid = 1`
- enum-value: `SetValues = 2`
- enum-value: `UnsetValues = 4`
- enum-value: `Valid = 8`
- enum-value: `All = 15`

## System.Windows.Documents.ContentPosition  (class)  :
- ctor: `protected ContentPosition()`
- field: `public static readonly System.Windows.Documents.ContentPosition Missing`

## System.Windows.Documents.DynamicDocumentPaginator  (class)  : System.Windows.Documents.DocumentPaginator
- ctor: `protected DynamicDocumentPaginator()`
- prop: `public virtual bool IsBackgroundPaginationEnabled`
- meth: `public abstract System.Windows.Documents.ContentPosition GetObjectPosition(object value)`
- meth: `public abstract int GetPageNumber(System.Windows.Documents.ContentPosition contentPosition)`
- meth: `public virtual void GetPageNumberAsync(System.Windows.Documents.ContentPosition contentPosition)`
- meth: `public virtual void GetPageNumberAsync(System.Windows.Documents.ContentPosition contentPosition, object userState)`
- meth: `public abstract System.Windows.Documents.ContentPosition GetPagePosition(System.Windows.Documents.DocumentPage page)`
- meth: `protected virtual void OnGetPageNumberCompleted(System.Windows.Documents.GetPageNumberCompletedEventArgs e)`
- meth: `protected virtual void OnPaginationCompleted(System.EventArgs e)`
- meth: `protected virtual void OnPaginationProgress(System.Windows.Documents.PaginationProgressEventArgs e)`
- event: `public event System.Windows.Documents.GetPageNumberCompletedEventHandler GetPageNumberCompleted { add { } remove`
- event: `public event System.EventHandler PaginationCompleted { add { } remove`
- event: `public event System.Windows.Documents.PaginationProgressEventHandler PaginationProgress { add { } remove`

## System.Windows.Documents.FrameworkRichTextComposition  (class)  : System.Windows.Documents.FrameworkTextComposition
- ctor: `internal FrameworkRichTextComposition()`
- prop: `public System.Windows.Documents.TextPointer CompositionEnd`
- prop: `public System.Windows.Documents.TextPointer CompositionStart`
- prop: `public System.Windows.Documents.TextPointer ResultEnd`
- prop: `public System.Windows.Documents.TextPointer ResultStart`

## System.Windows.Documents.FrameworkTextComposition  (class)  : System.Windows.Input.TextComposition
- ctor: `internal FrameworkTextComposition() : base (default(System.Windows.Input.InputManager), default(System.Windows.IInputElement), default(string))`
- prop: `public int CompositionLength`
- prop: `public int CompositionOffset`
- prop: `public int ResultLength`
- prop: `public int ResultOffset`
- meth: `public override void Complete()`

## System.Windows.Documents.GetPageNumberCompletedEventArgs  (class)  : System.ComponentModel.AsyncCompletedEventArgs
- ctor: `public GetPageNumberCompletedEventArgs(System.Windows.Documents.ContentPosition contentPosition, int pageNumber, System.Exception error, bool cancelled, object userState) : base(default(System.Exception), default(bool), default(object))`
- prop: `public System.Windows.Documents.ContentPosition ContentPosition`
- prop: `public int PageNumber`

## System.Windows.Documents.GetPageRootCompletedEventArgs  (class)  : System.ComponentModel.AsyncCompletedEventArgs
- ctor: `internal GetPageRootCompletedEventArgs() : base (default(System.Exception), default(bool), default(object))`
- prop: `public System.Windows.Documents.FixedPage Result`

## System.Windows.Documents.LinkTarget  (class)  :
- ctor: `public LinkTarget()`
- prop: `public string Name`

## System.Windows.Documents.LinkTargetCollection  (class)  : System.Collections.CollectionBase
- ctor: `public LinkTargetCollection()`
- prop: `public System.Windows.Documents.LinkTarget this[int index]`
- meth: `public int Add(System.Windows.Documents.LinkTarget value)`
- meth: `public bool Contains(System.Windows.Documents.LinkTarget value)`
- meth: `public void CopyTo(System.Windows.Documents.LinkTarget[] array, int index)`
- meth: `public int IndexOf(System.Windows.Documents.LinkTarget value)`
- meth: `public void Insert(int index, System.Windows.Documents.LinkTarget value)`
- meth: `public void Remove(System.Windows.Documents.LinkTarget value)`

## System.Windows.Documents.PagesChangedEventArgs  (class)  : System.EventArgs
- ctor: `public PagesChangedEventArgs(int start, int count)`
- prop: `public int Count`
- prop: `public int Start`

## System.Windows.Documents.TextEffectResolver  (class)  :
- meth: `public static System.Windows.Documents.TextEffectTarget[] Resolve(System.Windows.Documents.TextPointer startPosition, System.Windows.Documents.TextPointer endPosition, System.Windows.Media.TextEffect effect)`

## System.Windows.Documents.TextEffectTarget  (class)  :
- ctor: `internal TextEffectTarget()`
- prop: `public System.Windows.DependencyObject Element`
- prop: `public bool IsEnabled`
- prop: `public System.Windows.Media.TextEffect TextEffect`
- meth: `public void Disable()`
- meth: `public void Enable()`

## System.Windows.Documents.TextElementEditingBehaviorAttribute  (class)  : System.Attribute
- ctor: `public TextElementEditingBehaviorAttribute()`
- prop: `public bool IsMergeable`
- prop: `public bool IsTypographicOnly`

## System.Windows.Documents.ZoomPercentageConverter  (class)  : System.Windows.Data.IValueConverter
- ctor: `public ZoomPercentageConverter()`
- meth: `public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)`
- meth: `public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)`

## System.Windows.Input.AccessKeyEventArgs  (class)  : System.EventArgs
- ctor: `internal AccessKeyEventArgs()`
- prop: `public bool IsMultiple`
- prop: `public string Key`

## System.Windows.Input.CanExecuteChangedEventManager  (class)  : System.Windows.WeakEventManager
- ctor: `internal CanExecuteChangedEventManager()`
- meth: `public static void AddHandler(System.Windows.Input.ICommand source, System.EventHandler<System.EventArgs> handler)`
- meth: `protected override bool Purge(object source, object data, bool purgeAll)`
- meth: `public static void RemoveHandler(System.Windows.Input.ICommand source, System.EventHandler<System.EventArgs> handler)`
- meth: `protected override void StartListening(object source)`
- meth: `protected override void StopListening(object source)`

## System.Windows.Input.CommandConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public CommandConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Input.FocusManager  (class)  :
- meth: `public static void AddGotFocusHandler(System.Windows.DependencyObject element, System.Windows.RoutedEventHandler handler)`
- meth: `public static void AddLostFocusHandler(System.Windows.DependencyObject element, System.Windows.RoutedEventHandler handler)`
- meth: `public static System.Windows.IInputElement GetFocusedElement(System.Windows.DependencyObject element)`
- meth: `public static System.Windows.DependencyObject GetFocusScope(System.Windows.DependencyObject element)`
- meth: `public static bool GetIsFocusScope(System.Windows.DependencyObject element)`
- meth: `public static void RemoveGotFocusHandler(System.Windows.DependencyObject element, System.Windows.RoutedEventHandler handler)`
- meth: `public static void RemoveLostFocusHandler(System.Windows.DependencyObject element, System.Windows.RoutedEventHandler handler)`
- meth: `public static void SetFocusedElement(System.Windows.DependencyObject element, System.Windows.IInputElement value)`
- meth: `public static void SetIsFocusScope(System.Windows.DependencyObject element, bool value)`
- field: `public static readonly System.Windows.DependencyProperty FocusedElementProperty`
- field: `public static readonly System.Windows.RoutedEvent GotFocusEvent`
- field: `public static readonly System.Windows.DependencyProperty IsFocusScopeProperty`
- field: `public static readonly System.Windows.RoutedEvent LostFocusEvent`

## System.Windows.Input.ICommandSource  (interface)  :
- prop: `System.Windows.Input.ICommand Command`
- prop: `object CommandParameter`
- prop: `System.Windows.IInputElement CommandTarget`

## System.Windows.Input.IInputLanguageSource  (interface)  :
- prop: `System.Globalization.CultureInfo CurrentInputLanguage`
- prop: `System.Collections.IEnumerable InputLanguageList`
- meth: `void Initialize()`
- meth: `void Uninitialize()`

## System.Windows.Input.IManipulator  (interface)  :
- prop: `int Id`
- meth: `System.Windows.Point GetPosition(System.Windows.IInputElement relativeTo)`
- meth: `void ManipulationEnded(bool cancel)`
- event: `event System.EventHandler Updated;`

## System.Windows.Input.InputLanguageChangedEventArgs  (class)  : System.Windows.Input.InputLanguageEventArgs
- ctor: `public InputLanguageChangedEventArgs(System.Globalization.CultureInfo newLanguageId, System.Globalization.CultureInfo previousLanguageId) : base(default(System.Globalization.CultureInfo), default(System.Globalization.CultureInfo))`

## System.Windows.Input.InputLanguageChangingEventArgs  (class)  : System.Windows.Input.InputLanguageEventArgs
- ctor: `public InputLanguageChangingEventArgs(System.Globalization.CultureInfo newLanguageId, System.Globalization.CultureInfo previousLanguageId) : base(default(System.Globalization.CultureInfo), default(System.Globalization.CultureInfo))`
- prop: `public bool Rejected`

## System.Windows.Input.InputScopeNameConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public InputScopeNameConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Input.KeyGestureValueSerializer  (class)  : System.Windows.Markup.ValueSerializer
- ctor: `public KeyGestureValueSerializer()`
- meth: `public override bool CanConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override bool CanConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override object ConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override string ConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`

## System.Windows.Input.KeyValueSerializer  (class)  : System.Windows.Markup.ValueSerializer
- ctor: `public KeyValueSerializer()`
- meth: `public override bool CanConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override bool CanConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override object ConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override string ConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`

## System.Windows.Input.KeyboardEventArgs  (class)  : System.Windows.Input.InputEventArgs
- ctor: `public KeyboardEventArgs(System.Windows.Input.KeyboardDevice keyboard, int timestamp) : base(default(System.Windows.Input.InputDevice), default(int))`
- prop: `public System.Windows.Input.KeyboardDevice KeyboardDevice`
- meth: `protected override void InvokeEventHandler(System.Delegate genericHandler, object genericTarget)`

## System.Windows.Input.KeyboardInputProviderAcquireFocusEventArgs  (class)  : System.Windows.Input.KeyboardEventArgs
- ctor: `public KeyboardInputProviderAcquireFocusEventArgs(System.Windows.Input.KeyboardDevice keyboard, int timestamp, bool focusAcquired) : base(default(System.Windows.Input.KeyboardDevice), default(int))`
- prop: `public bool FocusAcquired`
- meth: `protected override void InvokeEventHandler(System.Delegate genericHandler, object genericTarget)`

## System.Windows.Input.Manipulation  (class)  :
- meth: `public static void AddManipulator(System.Windows.UIElement element, System.Windows.Input.IManipulator manipulator)`
- meth: `public static void CompleteManipulation(System.Windows.UIElement element)`
- meth: `public static System.Windows.IInputElement GetManipulationContainer(System.Windows.UIElement element)`
- meth: `public static System.Windows.Input.ManipulationModes GetManipulationMode(System.Windows.UIElement element)`
- meth: `public static System.Windows.Input.ManipulationPivot GetManipulationPivot(System.Windows.UIElement element)`
- meth: `public static bool IsManipulationActive(System.Windows.UIElement element)`
- meth: `public static void RemoveManipulator(System.Windows.UIElement element, System.Windows.Input.IManipulator manipulator)`
- meth: `public static void SetManipulationContainer(System.Windows.UIElement element, System.Windows.IInputElement container)`
- meth: `public static void SetManipulationMode(System.Windows.UIElement element, System.Windows.Input.ManipulationModes mode)`
- meth: `public static void SetManipulationParameter(System.Windows.UIElement element, System.Windows.Input.Manipulations.ManipulationParameters2D parameter)`
- meth: `public static void SetManipulationPivot(System.Windows.UIElement element, System.Windows.Input.ManipulationPivot pivot)`
- meth: `public static void StartInertia(System.Windows.UIElement element)`

## System.Windows.Input.ModifierKeysValueSerializer  (class)  : System.Windows.Markup.ValueSerializer
- ctor: `public ModifierKeysValueSerializer()`
- meth: `public override bool CanConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override bool CanConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override object ConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override string ConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`

## System.Windows.Input.MouseActionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public MouseActionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Input.MouseActionValueSerializer  (class)  : System.Windows.Markup.ValueSerializer
- ctor: `public MouseActionValueSerializer()`
- meth: `public override bool CanConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override bool CanConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override object ConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override string ConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`

## System.Windows.Input.MouseGestureValueSerializer  (class)  : System.Windows.Markup.ValueSerializer
- ctor: `public MouseGestureValueSerializer()`
- meth: `public override bool CanConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override bool CanConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override object ConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override string ConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`

## System.Windows.Input.SpeechMode  (enum)  :
- enum-value: `Dictation = 0`
- enum-value: `Command = 1`
- enum-value: `Indeterminate = 2`

## System.Windows.Input.StylusDeviceCollection  (class)  : System.Collections.ObjectModel.ReadOnlyCollection<System.Windows.Input.StylusDevice>
- ctor: `internal StylusDeviceCollection() : base(default(System.Collections.Generic.IList<System.Windows.Input.StylusDevice>))`

## System.Windows.Input.TraversalRequest  (class)  :
- ctor: `public TraversalRequest(System.Windows.Input.FocusNavigationDirection focusNavigationDirection)`
- prop: `public System.Windows.Input.FocusNavigationDirection FocusNavigationDirection`
- prop: `public bool Wrapped`

## System.Windows.Markup.AcceptedMarkupExtensionExpressionTypeAttribute  (class)  : System.Attribute
- ctor: `public AcceptedMarkupExtensionExpressionTypeAttribute(System.Type type)`
- prop: `public System.Type Type`

## System.Windows.Markup.AmbientAttribute  (class)  : System.Attribute
- ctor: `public AmbientAttribute()`

## System.Windows.Markup.ComponentResourceKeyConverter  (class)  : System.Windows.ExpressionConverter
- ctor: `public ComponentResourceKeyConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Markup.ConstructorArgumentAttribute  (class)  : System.Attribute
- ctor: `public ConstructorArgumentAttribute(string argumentName)`
- prop: `public string ArgumentName`

## System.Windows.Markup.ContentWrapperAttribute  (class)  : System.Attribute
- ctor: `public ContentWrapperAttribute(System.Type contentWrapper)`
- prop: `public System.Type ContentWrapper`
- prop: `public override object TypeId`
- meth: `public override bool Equals(object obj)`
- meth: `public override int GetHashCode()`

## System.Windows.Markup.DateTimeValueSerializer  (class)  : System.Windows.Markup.ValueSerializer
- ctor: `public DateTimeValueSerializer()`
- meth: `public override bool CanConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override bool CanConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override object ConvertFromString(string value, System.Windows.Markup.IValueSerializerContext context)`
- meth: `public override string ConvertToString(object value, System.Windows.Markup.IValueSerializerContext context)`

## System.Windows.Markup.DependencyPropertyConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public DependencyPropertyConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Markup.DependsOnAttribute  (class)  : System.Attribute
- ctor: `public DependsOnAttribute(string name)`
- prop: `public string Name`
- prop: `public override object TypeId`

## System.Windows.Markup.DictionaryKeyPropertyAttribute  (class)  : System.Attribute
- ctor: `public DictionaryKeyPropertyAttribute(string name)`
- prop: `public string Name`

## System.Windows.Markup.EventSetterHandlerConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public EventSetterHandlerConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.Markup.IAddChildInternal  (interface)  : IAddChild

## System.Windows.Markup.INameScopeDictionary  (interface)  : System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, object>>, System.Collections.Generic.IDictionary<string, object>, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>>, System.Collections.IEnumerable, System.Windows.Markup.INameScope

## System.Windows.Markup.IQueryAmbient  (interface)  :
- meth: `bool IsAmbientPropertyAvailable(string propertyName)`

## System.Windows.Markup.IReceiveMarkupExtension  (interface)  :
- meth: `void ReceiveMarkupExtension(string property, System.Windows.Markup.MarkupExtension markupExtension, System.IServiceProvider serviceProvider)`

## System.Windows.Markup.IXamlTypeResolver  (interface)  :
- meth: `System.Type Resolve(string qualifiedTypeName)`

## System.Windows.Markup.InternalTypeHelper  (class)  :
- ctor: `protected InternalTypeHelper()`
- meth: `protected internal abstract void AddEventHandler(System.Reflection.EventInfo eventInfo, object target, System.Delegate handler)`
- meth: `protected internal abstract System.Delegate CreateDelegate(System.Type delegateType, object target, string handler)`
- meth: `protected internal abstract object CreateInstance(System.Type type, System.Globalization.CultureInfo culture)`
- meth: `protected internal abstract object GetPropertyValue(System.Reflection.PropertyInfo propertyInfo, object target, System.Globalization.CultureInfo culture)`
- meth: `protected internal abstract void SetPropertyValue(System.Reflection.PropertyInfo propertyInfo, object target, object value, System.Globalization.CultureInfo culture)`

## System.Windows.Markup.MarkupExtensionBracketCharactersAttribute  (class)  : System.Attribute
- ctor: `public MarkupExtensionBracketCharactersAttribute(char openingBracket, char closingBracket)`
- prop: `public char ClosingBracket`
- prop: `public char OpeningBracket`

## System.Windows.Markup.MarkupExtensionReturnTypeAttribute  (class)  : System.Attribute
- ctor: `public MarkupExtensionReturnTypeAttribute()`
- ctor: `public MarkupExtensionReturnTypeAttribute(System.Type returnType)`
- ctor: `public MarkupExtensionReturnTypeAttribute(System.Type returnType, System.Type expressionType)`
- prop: `public System.Type ExpressionType`
- prop: `public System.Type ReturnType`

## System.Windows.Markup.MemberDefinition  (class)  :
- ctor: `protected MemberDefinition()`
- prop: `public abstract string Name`

## System.Windows.Markup.NameReferenceConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public NameReferenceConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Markup.NameScopePropertyAttribute  (class)  : System.Attribute
- ctor: `public NameScopePropertyAttribute(string name)`
- ctor: `public NameScopePropertyAttribute(string name, System.Type type)`
- prop: `public string Name`
- prop: `public System.Type Type`

## System.Windows.Markup.PropertyDefinition  (class)  : System.Windows.Markup.MemberDefinition
- ctor: `public PropertyDefinition()`
- prop: `public System.Collections.Generic.IList<System.Attribute> Attributes`
- prop: `public string Modifier`
- prop: `public override string Name`
- prop: `public System.Xaml.XamlType Type`

## System.Windows.Markup.Reference  (class)  : System.Windows.Markup.MarkupExtension
- ctor: `public Reference()`
- ctor: `public Reference(string name)`
- prop: `public string Name`
- meth: `public override object ProvideValue(System.IServiceProvider serviceProvider)`

## System.Windows.Markup.ResourceReferenceExpressionConverter  (class)  : System.Windows.ExpressionConverter
- ctor: `public ResourceReferenceExpressionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Markup.RootNamespaceAttribute  (class)  : System.Attribute
- ctor: `public RootNamespaceAttribute(string nameSpace)`
- prop: `public string Namespace`

## System.Windows.Markup.RoutedEventConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public RoutedEventConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.Markup.RuntimeNamePropertyAttribute  (class)  : System.Attribute
- ctor: `public RuntimeNamePropertyAttribute(string name)`
- prop: `public string Name`

## System.Windows.Markup.ServiceProviders  (class)  : System.IServiceProvider
- ctor: `public ServiceProviders()`
- meth: `public void AddService(System.Type serviceType, object service)`
- meth: `public object GetService(System.Type serviceType)`

## System.Windows.Markup.SetterTriggerConditionValueConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public SetterTriggerConditionValueConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Markup.TemplateKeyConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public TemplateKeyConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Markup.TrimSurroundingWhitespaceAttribute  (class)  : System.Attribute
- ctor: `public TrimSurroundingWhitespaceAttribute()`

## System.Windows.Markup.UidPropertyAttribute  (class)  : System.Attribute
- ctor: `public UidPropertyAttribute(string name)`
- prop: `public string Name`

## System.Windows.Markup.UsableDuringInitializationAttribute  (class)  : System.Attribute
- ctor: `public UsableDuringInitializationAttribute(bool usable)`
- prop: `public bool Usable`

## System.Windows.Markup.WhitespaceSignificantCollectionAttribute  (class)  : System.Attribute
- ctor: `public WhitespaceSignificantCollectionAttribute()`

## System.Windows.Markup.XData  (class)  :
- ctor: `public XData()`
- prop: `public string Text`
- prop: `public object XmlReader`

## System.Windows.Markup.XamlDeferLoadAttribute  (class)  : System.Attribute
- ctor: `public XamlDeferLoadAttribute(string loaderType, string contentType)`
- ctor: `public XamlDeferLoadAttribute(System.Type loaderType, System.Type contentType)`
- prop: `public System.Type ContentType`
- prop: `public string ContentTypeName`
- prop: `public System.Type LoaderType`
- prop: `public string LoaderTypeName`

## System.Windows.Markup.XamlInstanceCreator  (class)  :
- ctor: `protected XamlInstanceCreator()`
- meth: `public abstract object CreateObject()`

## System.Windows.Markup.XamlSetMarkupExtensionAttribute  (class)  : System.Attribute
- ctor: `public XamlSetMarkupExtensionAttribute(string xamlSetMarkupExtensionHandler)`
- prop: `public string XamlSetMarkupExtensionHandler`

## System.Windows.Markup.XamlSetMarkupExtensionEventArgs  (class)  : System.Windows.Markup.XamlSetValueEventArgs
- ctor: `public XamlSetMarkupExtensionEventArgs(System.Xaml.XamlMember member, System.Windows.Markup.MarkupExtension value, System.IServiceProvider serviceProvider) : base (default(System.Xaml.XamlMember), default(object))`
- prop: `public System.Windows.Markup.MarkupExtension MarkupExtension`
- prop: `public System.IServiceProvider ServiceProvider`
- meth: `public override void CallBase()`

## System.Windows.Markup.XamlSetTypeConverterAttribute  (class)  : System.Attribute
- ctor: `public XamlSetTypeConverterAttribute(string xamlSetTypeConverterHandler)`
- prop: `public string XamlSetTypeConverterHandler`

## System.Windows.Markup.XamlSetTypeConverterEventArgs  (class)  : System.Windows.Markup.XamlSetValueEventArgs
- ctor: `public XamlSetTypeConverterEventArgs(System.Xaml.XamlMember member, System.ComponentModel.TypeConverter typeConverter, object value, System.ComponentModel.ITypeDescriptorContext serviceProvider, System.Globalization.CultureInfo cultureInfo) : base (default(System.Xaml.XamlMember), default(object))`
- prop: `public System.Globalization.CultureInfo CultureInfo`
- prop: `public System.ComponentModel.ITypeDescriptorContext ServiceProvider`
- prop: `public System.ComponentModel.TypeConverter TypeConverter`
- meth: `public override void CallBase()`

## System.Windows.Markup.XamlSetValueEventArgs  (class)  : System.EventArgs
- ctor: `public XamlSetValueEventArgs(System.Xaml.XamlMember member, object value)`
- prop: `public bool Handled`
- prop: `public System.Xaml.XamlMember Member`
- prop: `public object Value`
- meth: `public virtual void CallBase()`

## System.Windows.Markup.XamlWriterState  (enum)  :
- enum-value: `Starting = 0`
- enum-value: `Finished = 1`

## System.Windows.Markup.XmlAttributeProperties  (class)  :
- ctor: `internal XmlAttributeProperties()`
- meth: `public static System.Collections.Hashtable GetXmlNamespaceMaps(System.Windows.DependencyObject dependencyObject)`
- meth: `public static string GetXmlnsDefinition(System.Windows.DependencyObject dependencyObject)`
- meth: `public static System.Windows.Markup.XmlnsDictionary GetXmlnsDictionary(System.Windows.DependencyObject dependencyObject)`
- meth: `public static string GetXmlSpace(System.Windows.DependencyObject dependencyObject)`
- meth: `public static void SetXmlNamespaceMaps(System.Windows.DependencyObject dependencyObject, System.Collections.Hashtable value)`
- meth: `public static void SetXmlnsDefinition(System.Windows.DependencyObject dependencyObject, string value)`
- meth: `public static void SetXmlnsDictionary(System.Windows.DependencyObject dependencyObject, System.Windows.Markup.XmlnsDictionary value)`
- meth: `public static void SetXmlSpace(System.Windows.DependencyObject dependencyObject, string value)`
- field: `public static readonly System.Windows.DependencyProperty XmlNamespaceMapsProperty`
- field: `public static readonly System.Windows.DependencyProperty XmlnsDefinitionProperty`
- field: `public static readonly System.Windows.DependencyProperty XmlnsDictionaryProperty`
- field: `public static readonly System.Windows.DependencyProperty XmlSpaceProperty`

## System.Windows.Markup.XmlLangPropertyAttribute  (class)  : System.Attribute
- ctor: `public XmlLangPropertyAttribute(string name)`
- prop: `public string Name`

## System.Windows.Markup.XmlLanguage  (class)  :
- ctor: `internal XmlLanguage()`
- prop: `public static System.Windows.Markup.XmlLanguage Empty`
- prop: `public string IetfLanguageTag`
- meth: `public System.Globalization.CultureInfo GetEquivalentCulture()`
- meth: `public static System.Windows.Markup.XmlLanguage GetLanguage(string ietfLanguageTag)`
- meth: `public System.Globalization.CultureInfo GetSpecificCulture()`
- meth: `public override string ToString()`

## System.Windows.Markup.XmlLanguageConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public XmlLanguageConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object source)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext typeDescriptorContext, System.Globalization.CultureInfo cultureInfo, object value, System.Type destinationType)`

## System.Windows.Media.CacheModeConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public CacheModeConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.ClearTypeHint  (enum)  :
- enum-value: `Auto = 0`
- enum-value: `Enabled = 1`

## System.Windows.Media.ColorInterpolationMode  (enum)  :
- enum-value: `ScRgbLinearInterpolation = 0`
- enum-value: `SRgbLinearInterpolation = 1`

## System.Windows.Media.DisableDpiAwarenessAttribute  (class)  : System.Attribute
- ctor: `public DisableDpiAwarenessAttribute()`

## System.Windows.Media.DoubleCollectionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public DoubleCollectionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.FontEmbeddingManager  (class)  :
- ctor: `public FontEmbeddingManager()`
- prop: `public System.Collections.Generic.ICollection<System.Uri> GlyphTypefaceUris`
- meth: `public System.Collections.Generic.ICollection<ushort> GetUsedGlyphs(System.Uri glyphTypeface)`
- meth: `public void RecordUsage(System.Windows.Media.GlyphRun glyphRun)`

## System.Windows.Media.FontFamilyMap  (class)  :
- ctor: `public FontFamilyMap()`
- prop: `public System.Windows.Markup.XmlLanguage Language`
- prop: `public double Scale`
- prop: `public string Target`
- prop: `public string Unicode`

## System.Windows.Media.FontFamilyMapCollection  (class)  : System.Collections.Generic.ICollection<System.Windows.Media.FontFamilyMap>, System.Collections.Generic.IEnumerable<System.Windows.Media.FontFamilyMap>, System.Collections.Generic.IList<System.Windows.Media.FontFamilyMap>, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `internal FontFamilyMapCollection()`
- prop: `public int Count`
- prop: `public bool IsReadOnly`
- prop: `public System.Windows.Media.FontFamilyMap this[int index]`
- prop: `bool System.Collections.ICollection.IsSynchronized`
- prop: `object System.Collections.ICollection.SyncRoot`
- prop: `bool System.Collections.IList.IsFixedSize`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public void Add(System.Windows.Media.FontFamilyMap item)`
- meth: `public void Clear()`
- meth: `public bool Contains(System.Windows.Media.FontFamilyMap item)`
- meth: `public void CopyTo(System.Windows.Media.FontFamilyMap[] array, int index)`
- meth: `public System.Collections.Generic.IEnumerator<System.Windows.Media.FontFamilyMap> GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.FontFamilyMap item)`
- meth: `public void Insert(int index, System.Windows.Media.FontFamilyMap item)`
- meth: `public bool Remove(System.Windows.Media.FontFamilyMap item)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()`
- meth: `int System.Collections.IList.Add(object value)`
- meth: `bool System.Collections.IList.Contains(object value)`
- meth: `int System.Collections.IList.IndexOf(object value)`
- meth: `void System.Collections.IList.Insert(int index, object item)`
- meth: `void System.Collections.IList.Remove(object value)`

## System.Windows.Media.GeneralTransformCollection  (class)  : System.Windows.Media.Animation.Animatable, System.Collections.Generic.ICollection<System.Windows.Media.GeneralTransform>, System.Collections.Generic.IEnumerable<System.Windows.Media.GeneralTransform>, System.Collections.Generic.IList<System.Windows.Media.GeneralTransform>, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public GeneralTransformCollection()`
- ctor: `public GeneralTransformCollection(System.Collections.Generic.IEnumerable<System.Windows.Media.GeneralTransform> collection)`
- ctor: `public GeneralTransformCollection(int capacity)`
- prop: `public int Count`
- prop: `public System.Windows.Media.GeneralTransform this[int index]`
- prop: `bool System.Collections.Generic.ICollection<System.Windows.Media.GeneralTransform>.IsReadOnly`
- prop: `bool System.Collections.ICollection.IsSynchronized`
- prop: `object System.Collections.ICollection.SyncRoot`
- prop: `bool System.Collections.IList.IsFixedSize`
- prop: `bool System.Collections.IList.IsReadOnly`
- prop: `object System.Collections.IList.this[int index]`
- prop: `public System.Windows.Media.GeneralTransform Current`
- prop: `object System.Collections.IEnumerator.Current`
- meth: `public void Add(System.Windows.Media.GeneralTransform value)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.GeneralTransformCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable source)`
- meth: `public new System.Windows.Media.GeneralTransformCollection CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable source)`
- meth: `public bool Contains(System.Windows.Media.GeneralTransform value)`
- meth: `public void CopyTo(System.Windows.Media.GeneralTransform[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `public System.Windows.Media.GeneralTransformCollection.Enumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.GeneralTransform value)`
- meth: `public void Insert(int index, System.Windows.Media.GeneralTransform value)`
- meth: `public bool Remove(System.Windows.Media.GeneralTransform value)`
- meth: `public void RemoveAt(int index)`
- meth: `System.Collections.Generic.IEnumerator<System.Windows.Media.GeneralTransform> System.Collections.Generic.IEnumerable<System.Windows.Media.GeneralTransform>.GetEnumerator()`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()`
- meth: `int System.Collections.IList.Add(object value)`
- meth: `bool System.Collections.IList.Contains(object value)`
- meth: `int System.Collections.IList.IndexOf(object value)`
- meth: `void System.Collections.IList.Insert(int index, object value)`
- meth: `void System.Collections.IList.Remove(object value)`
- meth: `public bool MoveNext()`
- meth: `public void Reset()`
- meth: `void System.IDisposable.Dispose()`

## System.Windows.Media.Int32CollectionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public Int32CollectionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.InvalidWmpVersionException  (class)  : System.SystemException
- ctor: `public InvalidWmpVersionException()`
- ctor: `protected InvalidWmpVersionException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public InvalidWmpVersionException(string message)`
- ctor: `public InvalidWmpVersionException(string message, System.Exception innerException)`

## System.Windows.Media.LanguageSpecificStringDictionary  (class)  : System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string>>, System.Collections.Generic.IDictionary<System.Windows.Markup.XmlLanguage, string>, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string>>, System.Collections.ICollection, System.Collections.IDictionary, System.Collections.IEnumerable
- ctor: `internal LanguageSpecificStringDictionary()`
- prop: `public int Count`
- prop: `public bool IsReadOnly`
- prop: `public string this[System.Windows.Markup.XmlLanguage key]`
- prop: `public System.Collections.Generic.ICollection<System.Windows.Markup.XmlLanguage> Keys`
- prop: `bool System.Collections.ICollection.IsSynchronized`
- prop: `object System.Collections.ICollection.SyncRoot`
- prop: `bool System.Collections.IDictionary.IsFixedSize`
- prop: `object System.Collections.IDictionary.this[object key]`
- prop: `System.Collections.ICollection System.Collections.IDictionary.Keys`
- prop: `System.Collections.ICollection System.Collections.IDictionary.Values`
- prop: `public System.Collections.Generic.ICollection<string> Values`
- meth: `public void Add(System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string> item)`
- meth: `public void Add(System.Windows.Markup.XmlLanguage key, string value)`
- meth: `public void Clear()`
- meth: `public bool Contains(System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string> item)`
- meth: `public bool ContainsKey(System.Windows.Markup.XmlLanguage key)`
- meth: `public void CopyTo(System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string>[] array, int index)`
- meth: `public System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string>> GetEnumerator()`
- meth: `public bool Remove(System.Collections.Generic.KeyValuePair<System.Windows.Markup.XmlLanguage, string> item)`
- meth: `public bool Remove(System.Windows.Markup.XmlLanguage key)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `void System.Collections.IDictionary.Add(object key, object value)`
- meth: `bool System.Collections.IDictionary.Contains(object key)`
- meth: `System.Collections.IDictionaryEnumerator System.Collections.IDictionary.GetEnumerator()`
- meth: `void System.Collections.IDictionary.Remove(object key)`
- meth: `System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()`
- meth: `public bool TryGetValue(System.Windows.Markup.XmlLanguage key, out string value)`

## System.Windows.Media.MatrixConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public MatrixConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.MediaClock  (class)  : System.Windows.Media.Animation.Clock
- prop: `public new System.Windows.Media.MediaTimeline Timeline`
- meth: `protected internal MediaClock(System.Windows.Media.MediaTimeline media) : base(default(System.Windows.Media.Animation.Timeline))`
- meth: `protected override void DiscontinuousTimeMovement()`
- meth: `protected override bool GetCanSlip()`
- meth: `protected override System.TimeSpan GetCurrentTimeCore()`
- meth: `protected override void SpeedChanged()`
- meth: `protected override void Stopped()`

## System.Windows.Media.PathFigureCollectionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public PathFigureCollectionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.PenDashCap  (enum)  :
- enum-value: `Flat = 0`
- enum-value: `Round = 2`
- enum-value: `Triangle = 3`

## System.Windows.Media.PixelFormatChannelMask  (struct)  :
- prop: `public System.Collections.Generic.IList<byte> Mask`
- meth: `public override bool Equals(object obj)`
- meth: `public static bool Equals(System.Windows.Media.PixelFormatChannelMask left, System.Windows.Media.PixelFormatChannelMask right)`
- meth: `public override int GetHashCode()`
- meth: `public static bool operator ==(System.Windows.Media.PixelFormatChannelMask left, System.Windows.Media.PixelFormatChannelMask right)`
- meth: `public static bool operator !=(System.Windows.Media.PixelFormatChannelMask left, System.Windows.Media.PixelFormatChannelMask right)`

## System.Windows.Media.TransformConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public TransformConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.VectorCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList, System.IFormattable, System.Collections.Generic.IList<System.Windows.Vector>
- ctor: `public VectorCollection()`
- ctor: `public VectorCollection(System.Collections.Generic.IEnumerable<System.Windows.Vector> collection)`
- ctor: `public VectorCollection(int capacity)`
- prop: `public int Count`
- prop: `public System.Windows.Vector this[int index]`
- prop: `bool System.Collections.ICollection.IsSynchronized`
- prop: `object System.Collections.ICollection.SyncRoot`
- prop: `bool System.Collections.IList.IsFixedSize`
- prop: `bool System.Collections.IList.IsReadOnly`
- prop: `bool System.Collections.Generic.ICollection<System.Windows.Vector>.IsReadOnly`
- prop: `object System.Collections.IList.this[int index]`
- prop: `public System.Windows.Vector Current`
- prop: `object System.Collections.IEnumerator.Current`
- meth: `public void Add(System.Windows.Vector value)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.VectorCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable source)`
- meth: `public new System.Windows.Media.VectorCollection CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable source)`
- meth: `public bool Contains(System.Windows.Vector value)`
- meth: `public void CopyTo(System.Windows.Vector[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `public System.Windows.Media.VectorCollection.Enumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Vector value)`
- meth: `public void Insert(int index, System.Windows.Vector value)`
- meth: `public static System.Windows.Media.VectorCollection Parse(string source)`
- meth: `public bool Remove(System.Windows.Vector value)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()`
- meth: `System.Collections.Generic.IEnumerator<System.Windows.Vector> System.Collections.Generic.IEnumerable<System.Windows.Vector>.GetEnumerator()`
- meth: `int System.Collections.IList.Add(object value)`
- meth: `bool System.Collections.IList.Contains(object value)`
- meth: `int System.Collections.IList.IndexOf(object value)`
- meth: `void System.Collections.IList.Insert(int index, object value)`
- meth: `void System.Collections.IList.Remove(object value)`
- meth: `string System.IFormattable.ToString(string format, System.IFormatProvider provider)`
- meth: `public override string ToString()`
- meth: `public string ToString(System.IFormatProvider provider)`
- meth: `public bool MoveNext()`
- meth: `public void Reset()`
- meth: `void System.IDisposable.Dispose()`

## System.Windows.Media.VectorCollectionConverter  (class)  : System.ComponentModel.TypeConverter
- ctor: `public VectorCollectionConverter()`
- meth: `public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type sourceType)`
- meth: `public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType)`
- meth: `public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)`
- meth: `public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType)`

## System.Windows.Media.Animation.BooleanAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected BooleanAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.BooleanAnimationBase Clone()`
- meth: `public bool GetCurrentValue(bool defaultOriginValue, bool defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract bool GetCurrentValueCore(bool defaultOriginValue, bool defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.BooleanKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected BooleanKeyFrame()`
- ctor: `protected BooleanKeyFrame(bool value)`
- ctor: `protected BooleanKeyFrame(bool value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public bool Value`
- meth: `public bool InterpolateValue(bool baseValue, double keyFrameProgress)`
- meth: `protected abstract bool InterpolateValueCore(bool baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.BooleanKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public BooleanKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.BooleanKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.BooleanKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.BooleanKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.BooleanKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.BooleanKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.BooleanKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.BooleanKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.BooleanKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.BooleanKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.ByteAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected ByteAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.ByteAnimationBase Clone()`
- meth: `public byte GetCurrentValue(byte defaultOriginValue, byte defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract byte GetCurrentValueCore(byte defaultOriginValue, byte defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.ByteAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.ByteAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public ByteAnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.ByteKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.ByteAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.ByteAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override byte GetCurrentValueCore(byte defaultOriginValue, byte defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.ByteKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected ByteKeyFrame()`
- ctor: `protected ByteKeyFrame(byte value)`
- ctor: `protected ByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public byte Value`
- meth: `public byte InterpolateValue(byte baseValue, double keyFrameProgress)`
- meth: `protected abstract byte InterpolateValueCore(byte baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.ByteKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public ByteKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.ByteKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.ByteKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.ByteKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.ByteKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.ByteKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.ByteKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.ByteKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.ByteKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.ByteKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.CharAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected CharAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.CharAnimationBase Clone()`
- meth: `public char GetCurrentValue(char defaultOriginValue, char defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract char GetCurrentValueCore(char defaultOriginValue, char defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.CharKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected CharKeyFrame()`
- ctor: `protected CharKeyFrame(char value)`
- ctor: `protected CharKeyFrame(char value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public char Value`
- meth: `public char InterpolateValue(char baseValue, double keyFrameProgress)`
- meth: `protected abstract char InterpolateValueCore(char baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.CharKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public CharKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.CharKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.CharKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.CharKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.CharKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.CharKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.CharKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.CharKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.CharKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.CharKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.ColorAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected ColorAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.ColorAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Media.Color GetCurrentValue(System.Windows.Media.Color defaultOriginValue, System.Windows.Media.Color defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Media.Color GetCurrentValueCore(System.Windows.Media.Color defaultOriginValue, System.Windows.Media.Color defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.ColorKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected ColorKeyFrame()`
- ctor: `protected ColorKeyFrame(System.Windows.Media.Color value)`
- ctor: `protected ColorKeyFrame(System.Windows.Media.Color value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Media.Color Value`
- meth: `public System.Windows.Media.Color InterpolateValue(System.Windows.Media.Color baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Media.Color InterpolateValueCore(System.Windows.Media.Color baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.DecimalAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected DecimalAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.DecimalAnimationBase Clone()`
- meth: `public decimal GetCurrentValue(decimal defaultOriginValue, decimal defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract decimal GetCurrentValueCore(decimal defaultOriginValue, decimal defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.DecimalAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.DecimalAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public DecimalAnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.DecimalKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.DecimalAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.DecimalAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override decimal GetCurrentValueCore(decimal defaultOriginValue, decimal defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.DecimalKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected DecimalKeyFrame()`
- ctor: `protected DecimalKeyFrame(decimal value)`
- ctor: `protected DecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public decimal Value`
- meth: `public decimal InterpolateValue(decimal baseValue, double keyFrameProgress)`
- meth: `protected abstract decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.DecimalKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public DecimalKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.DecimalKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.DecimalKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.DecimalKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.DecimalKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.DecimalKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.DecimalKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.DecimalKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.DecimalKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.DecimalKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.DiscreteByteKeyFrame  (class)  : System.Windows.Media.Animation.ByteKeyFrame
- ctor: `public DiscreteByteKeyFrame()`
- ctor: `public DiscreteByteKeyFrame(byte value)`
- ctor: `public DiscreteByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteDecimalKeyFrame  (class)  : System.Windows.Media.Animation.DecimalKeyFrame
- ctor: `public DiscreteDecimalKeyFrame()`
- ctor: `public DiscreteDecimalKeyFrame(decimal value)`
- ctor: `public DiscreteDecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteInt16KeyFrame  (class)  : System.Windows.Media.Animation.Int16KeyFrame
- ctor: `public DiscreteInt16KeyFrame()`
- ctor: `public DiscreteInt16KeyFrame(short value)`
- ctor: `public DiscreteInt16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override short InterpolateValueCore(short baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteInt32KeyFrame  (class)  : System.Windows.Media.Animation.Int32KeyFrame
- ctor: `public DiscreteInt32KeyFrame()`
- ctor: `public DiscreteInt32KeyFrame(int value)`
- ctor: `public DiscreteInt32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override int InterpolateValueCore(int baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteInt64KeyFrame  (class)  : System.Windows.Media.Animation.Int64KeyFrame
- ctor: `public DiscreteInt64KeyFrame()`
- ctor: `public DiscreteInt64KeyFrame(long value)`
- ctor: `public DiscreteInt64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override long InterpolateValueCore(long baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteMatrixKeyFrame  (class)  : System.Windows.Media.Animation.MatrixKeyFrame
- ctor: `public DiscreteMatrixKeyFrame()`
- ctor: `public DiscreteMatrixKeyFrame(System.Windows.Media.Matrix value)`
- ctor: `public DiscreteMatrixKeyFrame(System.Windows.Media.Matrix value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Media.Matrix InterpolateValueCore(System.Windows.Media.Matrix baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteRectKeyFrame  (class)  : System.Windows.Media.Animation.RectKeyFrame
- ctor: `public DiscreteRectKeyFrame()`
- ctor: `public DiscreteRectKeyFrame(System.Windows.Rect value)`
- ctor: `public DiscreteRectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Rect InterpolateValueCore(System.Windows.Rect baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteSingleKeyFrame  (class)  : System.Windows.Media.Animation.SingleKeyFrame
- ctor: `public DiscreteSingleKeyFrame()`
- ctor: `public DiscreteSingleKeyFrame(float value)`
- ctor: `public DiscreteSingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override float InterpolateValueCore(float baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteSizeKeyFrame  (class)  : System.Windows.Media.Animation.SizeKeyFrame
- ctor: `public DiscreteSizeKeyFrame()`
- ctor: `public DiscreteSizeKeyFrame(System.Windows.Size value)`
- ctor: `public DiscreteSizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Size InterpolateValueCore(System.Windows.Size baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DiscreteVectorKeyFrame  (class)  : System.Windows.Media.Animation.VectorKeyFrame
- ctor: `public DiscreteVectorKeyFrame()`
- ctor: `public DiscreteVectorKeyFrame(System.Windows.Vector value)`
- ctor: `public DiscreteVectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Vector InterpolateValueCore(System.Windows.Vector baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.DoubleAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected DoubleAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.DoubleAnimationBase Clone()`
- meth: `public double GetCurrentValue(double defaultOriginValue, double defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.DoubleKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected DoubleKeyFrame()`
- ctor: `protected DoubleKeyFrame(double value)`
- ctor: `protected DoubleKeyFrame(double value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public double Value`
- meth: `public double InterpolateValue(double baseValue, double keyFrameProgress)`
- meth: `protected abstract double InterpolateValueCore(double baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.EasingByteKeyFrame  (class)  : System.Windows.Media.Animation.ByteKeyFrame
- ctor: `public EasingByteKeyFrame()`
- ctor: `public EasingByteKeyFrame(byte value)`
- ctor: `public EasingByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingDecimalKeyFrame  (class)  : System.Windows.Media.Animation.DecimalKeyFrame
- ctor: `public EasingDecimalKeyFrame()`
- ctor: `public EasingDecimalKeyFrame(decimal value)`
- ctor: `public EasingDecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingDecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingInt16KeyFrame  (class)  : System.Windows.Media.Animation.Int16KeyFrame
- ctor: `public EasingInt16KeyFrame()`
- ctor: `public EasingInt16KeyFrame(short value)`
- ctor: `public EasingInt16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingInt16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override short InterpolateValueCore(short baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingInt32KeyFrame  (class)  : System.Windows.Media.Animation.Int32KeyFrame
- ctor: `public EasingInt32KeyFrame()`
- ctor: `public EasingInt32KeyFrame(int value)`
- ctor: `public EasingInt32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingInt32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override int InterpolateValueCore(int baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingInt64KeyFrame  (class)  : System.Windows.Media.Animation.Int64KeyFrame
- ctor: `public EasingInt64KeyFrame()`
- ctor: `public EasingInt64KeyFrame(long value)`
- ctor: `public EasingInt64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingInt64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override long InterpolateValueCore(long baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingRectKeyFrame  (class)  : System.Windows.Media.Animation.RectKeyFrame
- ctor: `public EasingRectKeyFrame()`
- ctor: `public EasingRectKeyFrame(System.Windows.Rect value)`
- ctor: `public EasingRectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingRectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Rect InterpolateValueCore(System.Windows.Rect baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingSingleKeyFrame  (class)  : System.Windows.Media.Animation.SingleKeyFrame
- ctor: `public EasingSingleKeyFrame()`
- ctor: `public EasingSingleKeyFrame(float value)`
- ctor: `public EasingSingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingSingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override float InterpolateValueCore(float baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingSizeKeyFrame  (class)  : System.Windows.Media.Animation.SizeKeyFrame
- ctor: `public EasingSizeKeyFrame()`
- ctor: `public EasingSizeKeyFrame(System.Windows.Size value)`
- ctor: `public EasingSizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingSizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Size InterpolateValueCore(System.Windows.Size baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingThicknessKeyFrame  (class)  : System.Windows.Media.Animation.ThicknessKeyFrame
- ctor: `public EasingThicknessKeyFrame()`
- ctor: `public EasingThicknessKeyFrame(System.Windows.Thickness value)`
- ctor: `public EasingThicknessKeyFrame(System.Windows.Thickness value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingThicknessKeyFrame(System.Windows.Thickness value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Thickness InterpolateValueCore(System.Windows.Thickness baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.EasingVectorKeyFrame  (class)  : System.Windows.Media.Animation.VectorKeyFrame
- ctor: `public EasingVectorKeyFrame()`
- ctor: `public EasingVectorKeyFrame(System.Windows.Vector value)`
- ctor: `public EasingVectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public EasingVectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.IEasingFunction easingFunction)`
- prop: `public System.Windows.Media.Animation.IEasingFunction EasingFunction`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Vector InterpolateValueCore(System.Windows.Vector baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty EasingFunctionProperty`

## System.Windows.Media.Animation.Int16AnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected Int16AnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.Int16AnimationBase Clone()`
- meth: `public short GetCurrentValue(short defaultOriginValue, short defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract short GetCurrentValueCore(short defaultOriginValue, short defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.Int16AnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.Int16AnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public Int16AnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.Int16KeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.Int16AnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.Int16AnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override short GetCurrentValueCore(short defaultOriginValue, short defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.Int16KeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected Int16KeyFrame()`
- ctor: `protected Int16KeyFrame(short value)`
- ctor: `protected Int16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public short Value`
- meth: `public short InterpolateValue(short baseValue, double keyFrameProgress)`
- meth: `protected abstract short InterpolateValueCore(short baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.Int16KeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public Int16KeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.Int16KeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.Int16KeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.Int16KeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.Int16KeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.Int16KeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.Int16KeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.Int16KeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.Int16KeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.Int16KeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.Int32AnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected Int32AnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.Int32AnimationBase Clone()`
- meth: `public int GetCurrentValue(int defaultOriginValue, int defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract int GetCurrentValueCore(int defaultOriginValue, int defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.Int32AnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.Int32AnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public Int32AnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.Int32KeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.Int32AnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.Int32AnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override int GetCurrentValueCore(int defaultOriginValue, int defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.Int32KeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected Int32KeyFrame()`
- ctor: `protected Int32KeyFrame(int value)`
- ctor: `protected Int32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public int Value`
- meth: `public int InterpolateValue(int baseValue, double keyFrameProgress)`
- meth: `protected abstract int InterpolateValueCore(int baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.Int32KeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public Int32KeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.Int32KeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.Int32KeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.Int32KeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.Int32KeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.Int32KeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.Int32KeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.Int32KeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.Int32KeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.Int32KeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.Int64AnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected Int64AnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.Int64AnimationBase Clone()`
- meth: `public long GetCurrentValue(long defaultOriginValue, long defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract long GetCurrentValueCore(long defaultOriginValue, long defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.Int64AnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.Int64AnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public Int64AnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.Int64KeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.Int64AnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.Int64AnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override long GetCurrentValueCore(long defaultOriginValue, long defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.Int64KeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected Int64KeyFrame()`
- ctor: `protected Int64KeyFrame(long value)`
- ctor: `protected Int64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public long Value`
- meth: `public long InterpolateValue(long baseValue, double keyFrameProgress)`
- meth: `protected abstract long InterpolateValueCore(long baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.Int64KeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public Int64KeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.Int64KeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.Int64KeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.Int64KeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.Int64KeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.Int64KeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.Int64KeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.Int64KeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.Int64KeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.Int64KeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.LinearByteKeyFrame  (class)  : System.Windows.Media.Animation.ByteKeyFrame
- ctor: `public LinearByteKeyFrame()`
- ctor: `public LinearByteKeyFrame(byte value)`
- ctor: `public LinearByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearDecimalKeyFrame  (class)  : System.Windows.Media.Animation.DecimalKeyFrame
- ctor: `public LinearDecimalKeyFrame()`
- ctor: `public LinearDecimalKeyFrame(decimal value)`
- ctor: `public LinearDecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearInt16KeyFrame  (class)  : System.Windows.Media.Animation.Int16KeyFrame
- ctor: `public LinearInt16KeyFrame()`
- ctor: `public LinearInt16KeyFrame(short value)`
- ctor: `public LinearInt16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override short InterpolateValueCore(short baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearInt32KeyFrame  (class)  : System.Windows.Media.Animation.Int32KeyFrame
- ctor: `public LinearInt32KeyFrame()`
- ctor: `public LinearInt32KeyFrame(int value)`
- ctor: `public LinearInt32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override int InterpolateValueCore(int baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearInt64KeyFrame  (class)  : System.Windows.Media.Animation.Int64KeyFrame
- ctor: `public LinearInt64KeyFrame()`
- ctor: `public LinearInt64KeyFrame(long value)`
- ctor: `public LinearInt64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override long InterpolateValueCore(long baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearRectKeyFrame  (class)  : System.Windows.Media.Animation.RectKeyFrame
- ctor: `public LinearRectKeyFrame()`
- ctor: `public LinearRectKeyFrame(System.Windows.Rect value)`
- ctor: `public LinearRectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Rect InterpolateValueCore(System.Windows.Rect baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearSingleKeyFrame  (class)  : System.Windows.Media.Animation.SingleKeyFrame
- ctor: `public LinearSingleKeyFrame()`
- ctor: `public LinearSingleKeyFrame(float value)`
- ctor: `public LinearSingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override float InterpolateValueCore(float baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearSizeKeyFrame  (class)  : System.Windows.Media.Animation.SizeKeyFrame
- ctor: `public LinearSizeKeyFrame()`
- ctor: `public LinearSizeKeyFrame(System.Windows.Size value)`
- ctor: `public LinearSizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Size InterpolateValueCore(System.Windows.Size baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.LinearVectorKeyFrame  (class)  : System.Windows.Media.Animation.VectorKeyFrame
- ctor: `public LinearVectorKeyFrame()`
- ctor: `public LinearVectorKeyFrame(System.Windows.Vector value)`
- ctor: `public LinearVectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Vector InterpolateValueCore(System.Windows.Vector baseValue, double keyFrameProgress)`

## System.Windows.Media.Animation.MatrixAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected MatrixAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.MatrixAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Media.Matrix GetCurrentValue(System.Windows.Media.Matrix defaultOriginValue, System.Windows.Media.Matrix defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Media.Matrix GetCurrentValueCore(System.Windows.Media.Matrix defaultOriginValue, System.Windows.Media.Matrix defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.MatrixAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.MatrixAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public MatrixAnimationUsingKeyFrames()`
- prop: `public System.Windows.Media.Animation.MatrixKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.MatrixAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.MatrixAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override System.Windows.Media.Matrix GetCurrentValueCore(System.Windows.Media.Matrix defaultOriginValue, System.Windows.Media.Matrix defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.MatrixKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected MatrixKeyFrame()`
- ctor: `protected MatrixKeyFrame(System.Windows.Media.Matrix value)`
- ctor: `protected MatrixKeyFrame(System.Windows.Media.Matrix value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Media.Matrix Value`
- meth: `public System.Windows.Media.Matrix InterpolateValue(System.Windows.Media.Matrix baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Media.Matrix InterpolateValueCore(System.Windows.Media.Matrix baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.MatrixKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public MatrixKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.MatrixKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.MatrixKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.MatrixKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.MatrixKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.MatrixKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.MatrixKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.MatrixKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.MatrixKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.MatrixKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.ObjectAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected ObjectAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.ObjectAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract object GetCurrentValueCore(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.ObjectKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected ObjectKeyFrame()`
- ctor: `protected ObjectKeyFrame(object value)`
- ctor: `protected ObjectKeyFrame(object value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public object Value`
- meth: `public object InterpolateValue(object baseValue, double keyFrameProgress)`
- meth: `protected abstract object InterpolateValueCore(object baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.Point3DAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected Point3DAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.Point3DAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Media.Media3D.Point3D GetCurrentValue(System.Windows.Media.Media3D.Point3D defaultOriginValue, System.Windows.Media.Media3D.Point3D defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Media.Media3D.Point3D GetCurrentValueCore(System.Windows.Media.Media3D.Point3D defaultOriginValue, System.Windows.Media.Media3D.Point3D defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.Point3DKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected Point3DKeyFrame()`
- ctor: `protected Point3DKeyFrame(System.Windows.Media.Media3D.Point3D value)`
- ctor: `protected Point3DKeyFrame(System.Windows.Media.Media3D.Point3D value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Media.Media3D.Point3D Value`
- meth: `public System.Windows.Media.Media3D.Point3D InterpolateValue(System.Windows.Media.Media3D.Point3D baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Media.Media3D.Point3D InterpolateValueCore(System.Windows.Media.Media3D.Point3D baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.PointAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected PointAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.PointAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Point GetCurrentValue(System.Windows.Point defaultOriginValue, System.Windows.Point defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Point GetCurrentValueCore(System.Windows.Point defaultOriginValue, System.Windows.Point defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.PointKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected PointKeyFrame()`
- ctor: `protected PointKeyFrame(System.Windows.Point value)`
- ctor: `protected PointKeyFrame(System.Windows.Point value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Point Value`
- meth: `public System.Windows.Point InterpolateValue(System.Windows.Point baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Point InterpolateValueCore(System.Windows.Point baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.QuaternionAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected QuaternionAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.QuaternionAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Media.Media3D.Quaternion GetCurrentValue(System.Windows.Media.Media3D.Quaternion defaultOriginValue, System.Windows.Media.Media3D.Quaternion defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Media.Media3D.Quaternion GetCurrentValueCore(System.Windows.Media.Media3D.Quaternion defaultOriginValue, System.Windows.Media.Media3D.Quaternion defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.QuaternionKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected QuaternionKeyFrame()`
- ctor: `protected QuaternionKeyFrame(System.Windows.Media.Media3D.Quaternion value)`
- ctor: `protected QuaternionKeyFrame(System.Windows.Media.Media3D.Quaternion value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Media.Media3D.Quaternion Value`
- meth: `public System.Windows.Media.Media3D.Quaternion InterpolateValue(System.Windows.Media.Media3D.Quaternion baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Media.Media3D.Quaternion InterpolateValueCore(System.Windows.Media.Media3D.Quaternion baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.RectAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected RectAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.RectAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Rect GetCurrentValue(System.Windows.Rect defaultOriginValue, System.Windows.Rect defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Rect GetCurrentValueCore(System.Windows.Rect defaultOriginValue, System.Windows.Rect defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.RectAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.RectAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public RectAnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.RectKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.RectAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.RectAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override System.Windows.Rect GetCurrentValueCore(System.Windows.Rect defaultOriginValue, System.Windows.Rect defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.RectKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected RectKeyFrame()`
- ctor: `protected RectKeyFrame(System.Windows.Rect value)`
- ctor: `protected RectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Rect Value`
- meth: `public System.Windows.Rect InterpolateValue(System.Windows.Rect baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Rect InterpolateValueCore(System.Windows.Rect baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.RectKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public RectKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.RectKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.RectKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.RectKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.RectKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.RectKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.RectKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.RectKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.RectKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.RectKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.Rotation3DAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected Rotation3DAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.Rotation3DAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Media.Media3D.Rotation3D GetCurrentValue(System.Windows.Media.Media3D.Rotation3D defaultOriginValue, System.Windows.Media.Media3D.Rotation3D defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Media.Media3D.Rotation3D GetCurrentValueCore(System.Windows.Media.Media3D.Rotation3D defaultOriginValue, System.Windows.Media.Media3D.Rotation3D defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.Rotation3DKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected Rotation3DKeyFrame()`
- ctor: `protected Rotation3DKeyFrame(System.Windows.Media.Media3D.Rotation3D value)`
- ctor: `protected Rotation3DKeyFrame(System.Windows.Media.Media3D.Rotation3D value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Media.Media3D.Rotation3D Value`
- meth: `public System.Windows.Media.Media3D.Rotation3D InterpolateValue(System.Windows.Media.Media3D.Rotation3D baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Media.Media3D.Rotation3D InterpolateValueCore(System.Windows.Media.Media3D.Rotation3D baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.SingleAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected SingleAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.SingleAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public float GetCurrentValue(float defaultOriginValue, float defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract float GetCurrentValueCore(float defaultOriginValue, float defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.SingleAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.SingleAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public SingleAnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.SingleKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.SingleAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.SingleAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override float GetCurrentValueCore(float defaultOriginValue, float defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.SingleKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected SingleKeyFrame()`
- ctor: `protected SingleKeyFrame(float value)`
- ctor: `protected SingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public float Value`
- meth: `public float InterpolateValue(float baseValue, double keyFrameProgress)`
- meth: `protected abstract float InterpolateValueCore(float baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.SingleKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public SingleKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.SingleKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.SingleKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.SingleKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.SingleKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.SingleKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.SingleKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.SingleKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.SingleKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.SingleKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.SizeAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected SizeAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.SizeAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Size GetCurrentValue(System.Windows.Size defaultOriginValue, System.Windows.Size defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Size GetCurrentValueCore(System.Windows.Size defaultOriginValue, System.Windows.Size defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.SizeAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.SizeAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public SizeAnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.SizeKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.SizeAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.SizeAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override System.Windows.Size GetCurrentValueCore(System.Windows.Size defaultOriginValue, System.Windows.Size defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.SizeKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected SizeKeyFrame()`
- ctor: `protected SizeKeyFrame(System.Windows.Size value)`
- ctor: `protected SizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Size Value`
- meth: `public System.Windows.Size InterpolateValue(System.Windows.Size baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Size InterpolateValueCore(System.Windows.Size baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.SizeKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public SizeKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.SizeKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.SizeKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.SizeKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.SizeKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.SizeKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.SizeKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.SizeKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.SizeKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.SizeKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.SplineByteKeyFrame  (class)  : System.Windows.Media.Animation.ByteKeyFrame
- ctor: `public SplineByteKeyFrame()`
- ctor: `public SplineByteKeyFrame(byte value)`
- ctor: `public SplineByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineByteKeyFrame(byte value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineDecimalKeyFrame  (class)  : System.Windows.Media.Animation.DecimalKeyFrame
- ctor: `public SplineDecimalKeyFrame()`
- ctor: `public SplineDecimalKeyFrame(decimal value)`
- ctor: `public SplineDecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineDecimalKeyFrame(decimal value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineInt16KeyFrame  (class)  : System.Windows.Media.Animation.Int16KeyFrame
- ctor: `public SplineInt16KeyFrame()`
- ctor: `public SplineInt16KeyFrame(short value)`
- ctor: `public SplineInt16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineInt16KeyFrame(short value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override short InterpolateValueCore(short baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineInt32KeyFrame  (class)  : System.Windows.Media.Animation.Int32KeyFrame
- ctor: `public SplineInt32KeyFrame()`
- ctor: `public SplineInt32KeyFrame(int value)`
- ctor: `public SplineInt32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineInt32KeyFrame(int value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override int InterpolateValueCore(int baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineInt64KeyFrame  (class)  : System.Windows.Media.Animation.Int64KeyFrame
- ctor: `public SplineInt64KeyFrame()`
- ctor: `public SplineInt64KeyFrame(long value)`
- ctor: `public SplineInt64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineInt64KeyFrame(long value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override long InterpolateValueCore(long baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineRectKeyFrame  (class)  : System.Windows.Media.Animation.RectKeyFrame
- ctor: `public SplineRectKeyFrame()`
- ctor: `public SplineRectKeyFrame(System.Windows.Rect value)`
- ctor: `public SplineRectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineRectKeyFrame(System.Windows.Rect value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Rect InterpolateValueCore(System.Windows.Rect baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineSingleKeyFrame  (class)  : System.Windows.Media.Animation.SingleKeyFrame
- ctor: `public SplineSingleKeyFrame()`
- ctor: `public SplineSingleKeyFrame(float value)`
- ctor: `public SplineSingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineSingleKeyFrame(float value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override float InterpolateValueCore(float baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineSizeKeyFrame  (class)  : System.Windows.Media.Animation.SizeKeyFrame
- ctor: `public SplineSizeKeyFrame()`
- ctor: `public SplineSizeKeyFrame(System.Windows.Size value)`
- ctor: `public SplineSizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineSizeKeyFrame(System.Windows.Size value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Size InterpolateValueCore(System.Windows.Size baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.SplineVectorKeyFrame  (class)  : System.Windows.Media.Animation.VectorKeyFrame
- ctor: `public SplineVectorKeyFrame()`
- ctor: `public SplineVectorKeyFrame(System.Windows.Vector value)`
- ctor: `public SplineVectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime)`
- ctor: `public SplineVectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime, System.Windows.Media.Animation.KeySpline keySpline)`
- prop: `public System.Windows.Media.Animation.KeySpline KeySpline`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override System.Windows.Vector InterpolateValueCore(System.Windows.Vector baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeySplineProperty`

## System.Windows.Media.Animation.StringAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected StringAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.StringAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public string GetCurrentValue(string defaultOriginValue, string defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract string GetCurrentValueCore(string defaultOriginValue, string defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.StringKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected StringKeyFrame()`
- ctor: `protected StringKeyFrame(string value)`
- ctor: `protected StringKeyFrame(string value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public string Value`
- meth: `public string InterpolateValue(string baseValue, double keyFrameProgress)`
- meth: `protected abstract string InterpolateValueCore(string baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.StringKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public StringKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.StringKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.StringKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.StringKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.StringKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.StringKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.StringKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.StringKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.StringKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.StringKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Animation.ThicknessAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected ThicknessAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.ThicknessAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Thickness GetCurrentValue(System.Windows.Thickness defaultOriginValue, System.Windows.Thickness defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Thickness GetCurrentValueCore(System.Windows.Thickness defaultOriginValue, System.Windows.Thickness defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.ThicknessKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected ThicknessKeyFrame()`
- ctor: `protected ThicknessKeyFrame(System.Windows.Thickness value)`
- ctor: `protected ThicknessKeyFrame(System.Windows.Thickness value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Thickness Value`
- meth: `public System.Windows.Thickness InterpolateValue(System.Windows.Thickness baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Thickness InterpolateValueCore(System.Windows.Thickness baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.Vector3DAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected Vector3DAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.Vector3DAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Media.Media3D.Vector3D GetCurrentValue(System.Windows.Media.Media3D.Vector3D defaultOriginValue, System.Windows.Media.Media3D.Vector3D defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Media.Media3D.Vector3D GetCurrentValueCore(System.Windows.Media.Media3D.Vector3D defaultOriginValue, System.Windows.Media.Media3D.Vector3D defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.Vector3DKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected Vector3DKeyFrame()`
- ctor: `protected Vector3DKeyFrame(System.Windows.Media.Media3D.Vector3D value)`
- ctor: `protected Vector3DKeyFrame(System.Windows.Media.Media3D.Vector3D value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Media.Media3D.Vector3D Value`
- meth: `public System.Windows.Media.Media3D.Vector3D InterpolateValue(System.Windows.Media.Media3D.Vector3D baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Media.Media3D.Vector3D InterpolateValueCore(System.Windows.Media.Media3D.Vector3D baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.VectorAnimationBase  (class)  : System.Windows.Media.Animation.AnimationTimeline
- ctor: `protected VectorAnimationBase()`
- prop: `public sealed override System.Type TargetPropertyType`
- meth: `public new System.Windows.Media.Animation.VectorAnimationBase Clone()`
- meth: `public sealed override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `public System.Windows.Vector GetCurrentValue(System.Windows.Vector defaultOriginValue, System.Windows.Vector defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected abstract System.Windows.Vector GetCurrentValueCore(System.Windows.Vector defaultOriginValue, System.Windows.Vector defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`

## System.Windows.Media.Animation.VectorAnimationUsingKeyFrames  (class)  : System.Windows.Media.Animation.VectorAnimationBase, System.Windows.Markup.IAddChild, System.Windows.Media.Animation.IKeyFrameAnimation
- ctor: `public VectorAnimationUsingKeyFrames()`
- prop: `public bool IsAdditive`
- prop: `public bool IsCumulative`
- prop: `public System.Windows.Media.Animation.VectorKeyFrameCollection KeyFrames`
- prop: `System.Collections.IList System.Windows.Media.Animation.IKeyFrameAnimation.KeyFrames`
- meth: `protected virtual void AddChild(object child)`
- meth: `protected virtual void AddText(string childText)`
- meth: `public new System.Windows.Media.Animation.VectorAnimationUsingKeyFrames Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `public new System.Windows.Media.Animation.VectorAnimationUsingKeyFrames CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected sealed override System.Windows.Vector GetCurrentValueCore(System.Windows.Vector defaultOriginValue, System.Windows.Vector defaultDestinationValue, System.Windows.Media.Animation.AnimationClock animationClock)`
- meth: `protected sealed override System.Windows.Duration GetNaturalDurationCore(System.Windows.Media.Animation.Clock clock)`
- meth: `protected override void OnChanged()`
- meth: `public bool ShouldSerializeKeyFrames()`
- meth: `void System.Windows.Markup.IAddChild.AddChild(object child)`
- meth: `void System.Windows.Markup.IAddChild.AddText(string childText)`

## System.Windows.Media.Animation.VectorKeyFrame  (class)  : System.Windows.Freezable, System.Windows.Media.Animation.IKeyFrame
- ctor: `protected VectorKeyFrame()`
- ctor: `protected VectorKeyFrame(System.Windows.Vector value)`
- ctor: `protected VectorKeyFrame(System.Windows.Vector value, System.Windows.Media.Animation.KeyTime keyTime)`
- prop: `public System.Windows.Media.Animation.KeyTime KeyTime`
- prop: `object System.Windows.Media.Animation.IKeyFrame.Value`
- prop: `public System.Windows.Vector Value`
- meth: `public System.Windows.Vector InterpolateValue(System.Windows.Vector baseValue, double keyFrameProgress)`
- meth: `protected abstract System.Windows.Vector InterpolateValueCore(System.Windows.Vector baseValue, double keyFrameProgress)`
- field: `public static readonly System.Windows.DependencyProperty KeyTimeProperty`
- field: `public static readonly System.Windows.DependencyProperty ValueProperty`

## System.Windows.Media.Animation.VectorKeyFrameCollection  (class)  : System.Windows.Freezable, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public VectorKeyFrameCollection()`
- prop: `public int Count`
- prop: `public static System.Windows.Media.Animation.VectorKeyFrameCollection Empty`
- prop: `public bool IsFixedSize`
- prop: `public bool IsReadOnly`
- prop: `public bool IsSynchronized`
- prop: `public System.Windows.Media.Animation.VectorKeyFrame this[int index]`
- prop: `public object SyncRoot`
- prop: `object System.Collections.IList.this[int index]`
- meth: `public int Add(System.Windows.Media.Animation.VectorKeyFrame keyFrame)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Animation.VectorKeyFrameCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable sourceFreezable)`
- meth: `public bool Contains(System.Windows.Media.Animation.VectorKeyFrame keyFrame)`
- meth: `public void CopyTo(System.Windows.Media.Animation.VectorKeyFrame[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable sourceFreezable)`
- meth: `public System.Collections.IEnumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Animation.VectorKeyFrame keyFrame)`
- meth: `public void Insert(int index, System.Windows.Media.Animation.VectorKeyFrame keyFrame)`
- meth: `public void Remove(System.Windows.Media.Animation.VectorKeyFrame keyFrame)`
- meth: `public void RemoveAt(int index)`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `int System.Collections.IList.Add(object keyFrame)`
- meth: `bool System.Collections.IList.Contains(object keyFrame)`
- meth: `int System.Collections.IList.IndexOf(object keyFrame)`
- meth: `void System.Collections.IList.Insert(int index, object keyFrame)`
- meth: `void System.Collections.IList.Remove(object keyFrame)`

## System.Windows.Media.Effects.BitmapEffectCollection  (class)  : System.Windows.Media.Animation.Animatable, System.Collections.Generic.ICollection<System.Windows.Media.Effects.BitmapEffect>, System.Collections.Generic.IEnumerable<System.Windows.Media.Effects.BitmapEffect>, System.Collections.Generic.IList<System.Windows.Media.Effects.BitmapEffect>, System.Collections.ICollection, System.Collections.IEnumerable, System.Collections.IList
- ctor: `public BitmapEffectCollection()`
- ctor: `public BitmapEffectCollection(System.Collections.Generic.IEnumerable<System.Windows.Media.Effects.BitmapEffect> collection)`
- ctor: `public BitmapEffectCollection(int capacity)`
- prop: `public int Count`
- prop: `public System.Windows.Media.Effects.BitmapEffect this[int index]`
- prop: `bool System.Collections.Generic.ICollection<System.Windows.Media.Effects.BitmapEffect>.IsReadOnly`
- prop: `bool System.Collections.ICollection.IsSynchronized`
- prop: `object System.Collections.ICollection.SyncRoot`
- prop: `bool System.Collections.IList.IsFixedSize`
- prop: `bool System.Collections.IList.IsReadOnly`
- prop: `object System.Collections.IList.this[int index]`
- prop: `public System.Windows.Media.Effects.BitmapEffect Current`
- prop: `object System.Collections.IEnumerator.Current`
- meth: `public void Add(System.Windows.Media.Effects.BitmapEffect value)`
- meth: `public void Clear()`
- meth: `public new System.Windows.Media.Effects.BitmapEffectCollection Clone()`
- meth: `protected override void CloneCore(System.Windows.Freezable source)`
- meth: `public new System.Windows.Media.Effects.BitmapEffectCollection CloneCurrentValue()`
- meth: `protected override void CloneCurrentValueCore(System.Windows.Freezable source)`
- meth: `public bool Contains(System.Windows.Media.Effects.BitmapEffect value)`
- meth: `public void CopyTo(System.Windows.Media.Effects.BitmapEffect[] array, int index)`
- meth: `protected override System.Windows.Freezable CreateInstanceCore()`
- meth: `protected override bool FreezeCore(bool isChecking)`
- meth: `protected override void GetAsFrozenCore(System.Windows.Freezable source)`
- meth: `protected override void GetCurrentValueAsFrozenCore(System.Windows.Freezable source)`
- meth: `public System.Windows.Media.Effects.BitmapEffectCollection.Enumerator GetEnumerator()`
- meth: `public int IndexOf(System.Windows.Media.Effects.BitmapEffect value)`
- meth: `public void Insert(int index, System.Windows.Media.Effects.BitmapEffect value)`
- meth: `public bool Remove(System.Windows.Media.Effects.BitmapEffect value)`
- meth: `public void RemoveAt(int index)`
- meth: `System.Collections.Generic.IEnumerator<System.Windows.Media.Effects.BitmapEffect> System.Collections.Generic.IEnumerable<System.Windows.Media.Effects.BitmapEffect>.GetEnumerator()`
- meth: `void System.Collections.ICollection.CopyTo(System.Array array, int index)`
- meth: `System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()`
- meth: `int System.Collections.IList.Add(object value)`
- meth: `bool System.Collections.IList.Contains(object value)`
- meth: `int System.Collections.IList.IndexOf(object value)`
- meth: `void System.Collections.IList.Insert(int index, object value)`
- meth: `void System.Collections.IList.Remove(object value)`
- meth: `public bool MoveNext()`
- meth: `public void Reset()`
- meth: `void System.IDisposable.Dispose()`

## System.Windows.Media.Imaging.LateBoundBitmapDecoder  (class)  : System.Windows.Media.Imaging.BitmapDecoder
- ctor: `internal LateBoundBitmapDecoder()`
- prop: `public override System.Windows.Media.Imaging.BitmapCodecInfo CodecInfo`
- prop: `public override System.Collections.ObjectModel.ReadOnlyCollection<System.Windows.Media.ColorContext> ColorContexts`
- prop: `public System.Windows.Media.Imaging.BitmapDecoder Decoder`
- prop: `public override System.Collections.ObjectModel.ReadOnlyCollection<System.Windows.Media.Imaging.BitmapFrame> Frames`
- prop: `public override bool IsDownloading`
- prop: `public override System.Windows.Media.Imaging.BitmapPalette Palette`
- prop: `public override System.Windows.Media.Imaging.BitmapSource Preview`
- prop: `public override System.Windows.Media.Imaging.BitmapSource Thumbnail`

## System.Windows.Media.Imaging.WmpBitmapDecoder  (class)  : System.Windows.Media.Imaging.BitmapDecoder
- ctor: `public WmpBitmapDecoder(System.IO.Stream bitmapStream, System.Windows.Media.Imaging.BitmapCreateOptions createOptions, System.Windows.Media.Imaging.BitmapCacheOption cacheOption)`
- ctor: `public WmpBitmapDecoder(System.Uri bitmapUri, System.Windows.Media.Imaging.BitmapCreateOptions createOptions, System.Windows.Media.Imaging.BitmapCacheOption cacheOption)`

## System.Windows.Media.TextFormatting.CharacterBufferRange  (struct)  : System.IEquatable<System.Windows.Media.TextFormatting.CharacterBufferRange>
- ctor: `public CharacterBufferRange(char[] characterArray, int offsetToFirstChar, int characterLength)`
- ctor: `public CharacterBufferRange(string characterString, int offsetToFirstChar, int characterLength)`
- prop: `public System.Windows.Media.TextFormatting.CharacterBufferReference CharacterBufferReference`
- prop: `public static System.Windows.Media.TextFormatting.CharacterBufferRange Empty`
- prop: `public int Length`
- meth: `public unsafe CharacterBufferRange(char* unsafeCharacterString, int characterLength)`
- meth: `public override bool Equals(object obj)`
- meth: `public bool Equals(System.Windows.Media.TextFormatting.CharacterBufferRange value)`
- meth: `public override int GetHashCode()`
- meth: `public static bool operator ==(System.Windows.Media.TextFormatting.CharacterBufferRange left, System.Windows.Media.TextFormatting.CharacterBufferRange right)`
- meth: `public static bool operator !=(System.Windows.Media.TextFormatting.CharacterBufferRange left, System.Windows.Media.TextFormatting.CharacterBufferRange right)`

## System.Windows.Media.TextFormatting.IndexedGlyphRun  (class)  :
- ctor: `internal IndexedGlyphRun()`
- prop: `public System.Windows.Media.GlyphRun GlyphRun`
- prop: `public int TextSourceCharacterIndex`
- prop: `public int TextSourceLength`

## System.Windows.Media.TextFormatting.TextCollapsedRange  (class)  :
- ctor: `internal TextCollapsedRange()`
- prop: `public int Length`
- prop: `public int TextSourceCharacterIndex`
- prop: `public double Width`

## System.Windows.Media.TextFormatting.TextEndOfSegment  (class)  : System.Windows.Media.TextFormatting.TextRun
- ctor: `public TextEndOfSegment(int length)`
- prop: `public sealed override System.Windows.Media.TextFormatting.CharacterBufferReference CharacterBufferReference`
- prop: `public sealed override int Length`
- prop: `public sealed override System.Windows.Media.TextFormatting.TextRunProperties Properties`

## System.Windows.Media.TextFormatting.TextModifier  (class)  : System.Windows.Media.TextFormatting.TextRun
- ctor: `protected TextModifier()`
- prop: `public sealed override System.Windows.Media.TextFormatting.CharacterBufferReference CharacterBufferReference`
- prop: `public abstract System.Windows.FlowDirection FlowDirection`
- prop: `public abstract bool HasDirectionalEmbedding`
- meth: `public abstract System.Windows.Media.TextFormatting.TextRunProperties ModifyProperties(System.Windows.Media.TextFormatting.TextRunProperties properties)`

## System.Windows.Media.TextFormatting.TextRunCache  (class)  :
- ctor: `public TextRunCache()`
- meth: `public void Change(int textSourceCharacterIndex, int addition, int removal)`
- meth: `public void Invalidate()`

## System.Windows.Media.TextFormatting.TextRunTypographyProperties  (class)  :
- ctor: `protected TextRunTypographyProperties()`
- prop: `public abstract int AnnotationAlternates`
- prop: `public abstract System.Windows.FontCapitals Capitals`
- prop: `public abstract bool CapitalSpacing`
- prop: `public abstract bool CaseSensitiveForms`
- prop: `public abstract bool ContextualAlternates`
- prop: `public abstract bool ContextualLigatures`
- prop: `public abstract int ContextualSwashes`
- prop: `public abstract bool DiscretionaryLigatures`
- prop: `public abstract bool EastAsianExpertForms`
- prop: `public abstract System.Windows.FontEastAsianLanguage EastAsianLanguage`
- prop: `public abstract System.Windows.FontEastAsianWidths EastAsianWidths`
- prop: `public abstract System.Windows.FontFraction Fraction`
- prop: `public abstract bool HistoricalForms`
- prop: `public abstract bool HistoricalLigatures`
- prop: `public abstract bool Kerning`
- prop: `public abstract bool MathematicalGreek`
- prop: `public abstract System.Windows.FontNumeralAlignment NumeralAlignment`
- prop: `public abstract System.Windows.FontNumeralStyle NumeralStyle`
- prop: `public abstract bool SlashedZero`
- prop: `public abstract bool StandardLigatures`
- prop: `public abstract int StandardSwashes`
- prop: `public abstract int StylisticAlternates`
- prop: `public abstract bool StylisticSet1`
- prop: `public abstract bool StylisticSet10`
- prop: `public abstract bool StylisticSet11`
- prop: `public abstract bool StylisticSet12`
- prop: `public abstract bool StylisticSet13`
- prop: `public abstract bool StylisticSet14`
- prop: `public abstract bool StylisticSet15`
- prop: `public abstract bool StylisticSet16`
- prop: `public abstract bool StylisticSet17`
- prop: `public abstract bool StylisticSet18`
- prop: `public abstract bool StylisticSet19`
- prop: `public abstract bool StylisticSet2`
- prop: `public abstract bool StylisticSet20`
- prop: `public abstract bool StylisticSet3`
- prop: `public abstract bool StylisticSet4`
- prop: `public abstract bool StylisticSet5`
- prop: `public abstract bool StylisticSet6`
- prop: `public abstract bool StylisticSet7`
- prop: `public abstract bool StylisticSet8`
- prop: `public abstract bool StylisticSet9`
- prop: `public abstract System.Windows.FontVariants Variants`
- meth: `protected void OnPropertiesChanged()`

## System.Windows.Media.TextFormatting.TextSimpleMarkerProperties  (class)  : System.Windows.Media.TextFormatting.TextMarkerProperties
- ctor: `public TextSimpleMarkerProperties(System.Windows.TextMarkerStyle style, double offset, int autoNumberingIndex, System.Windows.Media.TextFormatting.TextParagraphProperties textParagraphProperties)`
- prop: `public sealed override double Offset`
- prop: `public sealed override System.Windows.Media.TextFormatting.TextSource TextSource`

## System.Windows.Threading.DispatcherEventArgs  (class)  : System.EventArgs
- ctor: `internal DispatcherEventArgs()`
- prop: `public System.Windows.Threading.Dispatcher Dispatcher`

## System.Windows.Threading.DispatcherHookEventArgs  (class)  : System.EventArgs
- ctor: `public DispatcherHookEventArgs(System.Windows.Threading.DispatcherOperation operation)`
- prop: `public System.Windows.Threading.Dispatcher Dispatcher`
- prop: `public System.Windows.Threading.DispatcherOperation Operation`

## System.Xaml.AmbientPropertyValue  (class)  :
- ctor: `public AmbientPropertyValue(System.Xaml.XamlMember property, object value)`
- prop: `public System.Xaml.XamlMember RetrievedProperty`
- prop: `public object Value`

## System.Xaml.AttachableMemberIdentifier  (class)  : System.IEquatable<System.Xaml.AttachableMemberIdentifier>
- ctor: `public AttachableMemberIdentifier(System.Type declaringType, string memberName)`
- prop: `public System.Type DeclaringType`
- prop: `public string MemberName`
- meth: `public override bool Equals(object obj)`
- meth: `public bool Equals(System.Xaml.AttachableMemberIdentifier other)`
- meth: `public override int GetHashCode()`
- meth: `public static bool operator ==(System.Xaml.AttachableMemberIdentifier left, System.Xaml.AttachableMemberIdentifier right)`
- meth: `public static bool operator !=(System.Xaml.AttachableMemberIdentifier left, System.Xaml.AttachableMemberIdentifier right)`
- meth: `public override string ToString()`

## System.Xaml.AttachablePropertyServices  (class)  :
- meth: `public static void CopyPropertiesTo(object instance, System.Collections.Generic.KeyValuePair<System.Xaml.AttachableMemberIdentifier, object>[] array, int index)`
- meth: `public static int GetAttachedPropertyCount(object instance)`
- meth: `public static bool RemoveProperty(object instance, System.Xaml.AttachableMemberIdentifier name)`
- meth: `public static void SetProperty(object instance, System.Xaml.AttachableMemberIdentifier name, object value)`
- meth: `public static bool TryGetProperty(object instance, System.Xaml.AttachableMemberIdentifier name, out object value)`
- meth: `public static bool TryGetProperty<T>(object instance, System.Xaml.AttachableMemberIdentifier name, out T value)`

## System.Xaml.IAmbientProvider  (interface)  :
- meth: `System.Collections.Generic.IEnumerable<System.Xaml.AmbientPropertyValue> GetAllAmbientValues(System.Collections.Generic.IEnumerable<System.Xaml.XamlType> ceilingTypes, bool searchLiveStackOnly, System.Collections.Generic.IEnumerable<System.Xaml.XamlType> types, params System.Xaml.XamlMember[] properties)`
- meth: `System.Collections.Generic.IEnumerable<System.Xaml.AmbientPropertyValue> GetAllAmbientValues(System.Collections.Generic.IEnumerable<System.Xaml.XamlType> ceilingTypes, params System.Xaml.XamlMember[] properties)`
- meth: `System.Collections.Generic.IEnumerable<object> GetAllAmbientValues(params System.Xaml.XamlType[] types)`
- meth: `System.Xaml.AmbientPropertyValue GetFirstAmbientValue(System.Collections.Generic.IEnumerable<System.Xaml.XamlType> ceilingTypes, params System.Xaml.XamlMember[] properties)`
- meth: `object GetFirstAmbientValue(params System.Xaml.XamlType[] types)`

## System.Xaml.IAttachedPropertyStore  (interface)  :
- prop: `int PropertyCount`
- meth: `void CopyPropertiesTo(System.Collections.Generic.KeyValuePair<System.Xaml.AttachableMemberIdentifier, object>[] array, int index)`
- meth: `bool RemoveProperty(System.Xaml.AttachableMemberIdentifier attachableMemberIdentifier)`
- meth: `void SetProperty(System.Xaml.AttachableMemberIdentifier attachableMemberIdentifier, object value)`
- meth: `bool TryGetProperty(System.Xaml.AttachableMemberIdentifier attachableMemberIdentifier, out object value)`

## System.Xaml.IDestinationTypeProvider  (interface)  :
- meth: `System.Type GetDestinationType()`

## System.Xaml.INamespacePrefixLookup  (interface)  :
- meth: `string LookupPrefix(string ns)`

## System.Xaml.IRootObjectProvider  (interface)  :
- prop: `object RootObject`

## System.Xaml.IXamlIndexingReader  (interface)  :
- prop: `int Count`
- prop: `int CurrentIndex`

## System.Xaml.IXamlLineInfo  (interface)  :
- prop: `bool HasLineInfo`
- prop: `int LineNumber`
- prop: `int LinePosition`

## System.Xaml.IXamlLineInfoConsumer  (interface)  :
- prop: `bool ShouldProvideLineInfo`
- meth: `void SetLineInfo(int lineNumber, int linePosition)`

## System.Xaml.IXamlNameProvider  (interface)  :
- meth: `string GetName(object value)`

## System.Xaml.IXamlNameResolver  (interface)  :
- prop: `bool IsFixupTokenAvailable`
- meth: `System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> GetAllNamesAndValuesInScope()`
- meth: `object GetFixupToken(System.Collections.Generic.IEnumerable<string> names)`
- meth: `object GetFixupToken(System.Collections.Generic.IEnumerable<string> names, bool canAssignDirectly)`
- meth: `object Resolve(string name)`
- meth: `object Resolve(string name, out bool isFullyInitialized)`
- event: `event System.EventHandler OnNameScopeInitializationComplete;`

## System.Xaml.IXamlNamespaceResolver  (interface)  :
- meth: `string GetNamespace(string prefix)`
- meth: `System.Collections.Generic.IEnumerable<System.Xaml.NamespaceDeclaration> GetNamespacePrefixes()`

## System.Xaml.IXamlObjectWriterFactory  (interface)  :
- meth: `System.Xaml.XamlObjectWriterSettings GetParentSettings()`
- meth: `System.Xaml.XamlObjectWriter GetXamlObjectWriter(System.Xaml.XamlObjectWriterSettings settings)`

## System.Xaml.IXamlSchemaContextProvider  (interface)  :
- prop: `System.Xaml.XamlSchemaContext SchemaContext`

## System.Xaml.NamespaceDeclaration  (class)  :
- ctor: `public NamespaceDeclaration(string ns, string prefix)`
- prop: `public string Namespace`
- prop: `public string Prefix`

## System.Xaml.XamlBackgroundReader  (class)  : System.Xaml.XamlReader, System.Xaml.IXamlLineInfo
- ctor: `public XamlBackgroundReader(System.Xaml.XamlReader wrappedReader)`
- prop: `public bool HasLineInfo`
- prop: `public override bool IsEof`
- prop: `public int LineNumber`
- prop: `public int LinePosition`
- prop: `public override System.Xaml.XamlMember Member`
- prop: `public override System.Xaml.NamespaceDeclaration Namespace`
- prop: `public override System.Xaml.XamlNodeType NodeType`
- prop: `public override System.Xaml.XamlSchemaContext SchemaContext`
- prop: `public override System.Xaml.XamlType Type`
- prop: `public override object Value`
- meth: `protected override void Dispose(bool disposing)`
- meth: `public override bool Read()`
- meth: `public void StartThread()`
- meth: `public void StartThread(string threadName)`

## System.Xaml.XamlDeferringLoader  (class)  :
- ctor: `protected XamlDeferringLoader()`
- meth: `public abstract object Load(System.Xaml.XamlReader xamlReader, System.IServiceProvider serviceProvider)`
- meth: `public abstract System.Xaml.XamlReader Save(object value, System.IServiceProvider serviceProvider)`

## System.Xaml.XamlDirective  (class)  : System.Xaml.XamlMember
- ctor: `public XamlDirective(System.Collections.Generic.IEnumerable<string> xamlNamespaces, string name, System.Xaml.XamlType xamlType, System.Xaml.Schema.XamlValueConverter<System.ComponentModel.TypeConverter> typeConverter, System.Xaml.Schema.AllowedMemberLocations allowedLocation) : base (default(string), default(System.Xaml.XamlType), default(bool))`
- ctor: `public XamlDirective(string xamlNamespace, string name) : base (default(string), default(System.Xaml.XamlType), default(bool))`
- prop: `public System.Xaml.Schema.AllowedMemberLocations AllowedLocation`
- meth: `public override int GetHashCode()`
- meth: `public override System.Collections.Generic.IList<string> GetXamlNamespaces()`
- meth: `protected sealed override System.Reflection.ICustomAttributeProvider LookupCustomAttributeProvider()`
- meth: `protected sealed override System.Xaml.Schema.XamlValueConverter<System.Xaml.XamlDeferringLoader> LookupDeferringLoader()`
- meth: `protected sealed override System.Collections.Generic.IList<System.Xaml.XamlMember> LookupDependsOn()`
- meth: `protected sealed override System.Xaml.Schema.XamlMemberInvoker LookupInvoker()`
- meth: `protected sealed override bool LookupIsAmbient()`
- meth: `protected sealed override bool LookupIsEvent()`
- meth: `protected sealed override bool LookupIsReadOnly()`
- meth: `protected sealed override bool LookupIsReadPublic()`
- meth: `protected sealed override bool LookupIsUnknown()`
- meth: `protected sealed override bool LookupIsWriteOnly()`
- meth: `protected sealed override bool LookupIsWritePublic()`
- meth: `protected sealed override System.Xaml.XamlType LookupTargetType()`
- meth: `protected sealed override System.Xaml.XamlType LookupType()`
- meth: `protected sealed override System.Xaml.Schema.XamlValueConverter<System.ComponentModel.TypeConverter> LookupTypeConverter()`
- meth: `protected sealed override System.Reflection.MethodInfo LookupUnderlyingGetter()`
- meth: `protected sealed override System.Reflection.MemberInfo LookupUnderlyingMember()`
- meth: `protected sealed override System.Reflection.MethodInfo LookupUnderlyingSetter()`
- meth: `public override string ToString()`

## System.Xaml.XamlDuplicateMemberException  (class)  : System.Xaml.XamlException
- ctor: `public XamlDuplicateMemberException()`
- ctor: `protected XamlDuplicateMemberException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlDuplicateMemberException(string message)`
- ctor: `public XamlDuplicateMemberException(string message, System.Exception innerException)`
- ctor: `public XamlDuplicateMemberException(System.Xaml.XamlMember member, System.Xaml.XamlType type)`
- prop: `public System.Xaml.XamlMember DuplicateMember`
- prop: `public System.Xaml.XamlType ParentType`
- meth: `public override void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`

## System.Xaml.XamlException  (class)  : System.Exception
- ctor: `public XamlException()`
- ctor: `protected XamlException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlException(string message)`
- ctor: `public XamlException(string message, System.Exception innerException)`
- ctor: `public XamlException(string message, System.Exception innerException, int lineNumber, int linePosition)`
- prop: `public int LineNumber`
- prop: `public int LinePosition`
- prop: `public override string Message`
- meth: `public override void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`

## System.Xaml.XamlInternalException  (class)  : System.Xaml.XamlException
- ctor: `public XamlInternalException()`
- ctor: `protected XamlInternalException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlInternalException(string message)`
- ctor: `public XamlInternalException(string message, System.Exception innerException)`

## System.Xaml.XamlLanguage  (class)  :
- prop: `public static System.Collections.ObjectModel.ReadOnlyCollection<System.Xaml.XamlDirective> AllDirectives`
- prop: `public static System.Collections.ObjectModel.ReadOnlyCollection<System.Xaml.XamlType> AllTypes`
- prop: `public static System.Xaml.XamlDirective Arguments`
- prop: `public static System.Xaml.XamlType Array`
- prop: `public static System.Xaml.XamlDirective AsyncRecords`
- prop: `public static System.Xaml.XamlDirective Base`
- prop: `public static System.Xaml.XamlType Boolean`
- prop: `public static System.Xaml.XamlType Byte`
- prop: `public static System.Xaml.XamlType Char`
- prop: `public static System.Xaml.XamlDirective Class`
- prop: `public static System.Xaml.XamlDirective ClassAttributes`
- prop: `public static System.Xaml.XamlDirective ClassModifier`
- prop: `public static System.Xaml.XamlDirective Code`
- prop: `public static System.Xaml.XamlDirective ConnectionId`
- prop: `public static System.Xaml.XamlType Decimal`
- prop: `public static System.Xaml.XamlType Double`
- prop: `public static System.Xaml.XamlDirective FactoryMethod`
- prop: `public static System.Xaml.XamlDirective FieldModifier`
- prop: `public static System.Xaml.XamlDirective Initialization`
- prop: `public static System.Xaml.XamlType Int16`
- prop: `public static System.Xaml.XamlType Int32`
- prop: `public static System.Xaml.XamlType Int64`
- prop: `public static System.Xaml.XamlDirective Items`
- prop: `public static System.Xaml.XamlDirective Key`
- prop: `public static System.Xaml.XamlDirective Lang`
- prop: `public static System.Xaml.XamlType Member`
- prop: `public static System.Xaml.XamlDirective Members`
- prop: `public static System.Xaml.XamlDirective Name`
- prop: `public static System.Xaml.XamlType Null`
- prop: `public static System.Xaml.XamlType Object`
- prop: `public static System.Xaml.XamlDirective PositionalParameters`
- prop: `public static System.Xaml.XamlType Property`
- prop: `public static System.Xaml.XamlType Reference`
- prop: `public static System.Xaml.XamlDirective Shared`
- prop: `public static System.Xaml.XamlType Single`
- prop: `public static System.Xaml.XamlDirective Space`
- prop: `public static System.Xaml.XamlType Static`
- prop: `public static System.Xaml.XamlType String`
- prop: `public static System.Xaml.XamlDirective Subclass`
- prop: `public static System.Xaml.XamlDirective SynchronousMode`
- prop: `public static System.Xaml.XamlType TimeSpan`
- prop: `public static System.Xaml.XamlType Type`
- prop: `public static System.Xaml.XamlDirective TypeArguments`
- prop: `public static System.Xaml.XamlDirective Uid`
- prop: `public static System.Xaml.XamlDirective UnknownContent`
- prop: `public static System.Xaml.XamlType Uri`
- prop: `public static System.Collections.Generic.IList<string> XamlNamespaces`
- prop: `public static System.Xaml.XamlType XData`
- prop: `public static System.Collections.Generic.IList<string> XmlNamespaces`
- field: `public const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml"`
- field: `public const string Xml1998Namespace = "http://www.w3.org/XML/1998/namespace"`

## System.Xaml.XamlMember  (class)  : System.IEquatable<System.Xaml.XamlMember>
- ctor: `public XamlMember(System.Reflection.EventInfo eventInfo, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlMember(System.Reflection.EventInfo eventInfo, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.Schema.XamlMemberInvoker invoker)`
- ctor: `public XamlMember(System.Reflection.PropertyInfo propertyInfo, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlMember(System.Reflection.PropertyInfo propertyInfo, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.Schema.XamlMemberInvoker invoker)`
- ctor: `public XamlMember(string attachablePropertyName, System.Reflection.MethodInfo getter, System.Reflection.MethodInfo setter, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlMember(string attachablePropertyName, System.Reflection.MethodInfo getter, System.Reflection.MethodInfo setter, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.Schema.XamlMemberInvoker invoker)`
- ctor: `public XamlMember(string attachableEventName, System.Reflection.MethodInfo adder, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlMember(string attachableEventName, System.Reflection.MethodInfo adder, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.Schema.XamlMemberInvoker invoker)`
- ctor: `public XamlMember(string name, System.Xaml.XamlType declaringType, bool isAttachable)`
- prop: `public System.Xaml.XamlType DeclaringType`
- prop: `public System.Xaml.Schema.XamlValueConverter<System.Xaml.XamlDeferringLoader> DeferringLoader`
- prop: `public System.Collections.Generic.IList<System.Xaml.XamlMember> DependsOn`
- prop: `public System.Xaml.Schema.XamlMemberInvoker Invoker`
- prop: `public bool IsAmbient`
- prop: `public bool IsAttachable`
- prop: `public bool IsDirective`
- prop: `public bool IsEvent`
- prop: `public bool IsNameValid`
- prop: `public bool IsReadOnly`
- prop: `public bool IsReadPublic`
- prop: `public bool IsUnknown`
- prop: `public bool IsWriteOnly`
- prop: `public bool IsWritePublic`
- prop: `public System.Collections.Generic.IReadOnlyDictionary<char, char> MarkupExtensionBracketCharacters`
- prop: `public string Name`
- prop: `public string PreferredXamlNamespace`
- prop: `public System.ComponentModel.DesignerSerializationVisibility SerializationVisibility`
- prop: `public System.Xaml.XamlType TargetType`
- prop: `public System.Xaml.XamlType Type`
- prop: `public System.Xaml.Schema.XamlValueConverter<System.ComponentModel.TypeConverter> TypeConverter`
- prop: `public System.Reflection.MemberInfo UnderlyingMember`
- prop: `public System.Xaml.Schema.XamlValueConverter<System.Windows.Markup.ValueSerializer> ValueSerializer`
- meth: `public override bool Equals(object obj)`
- meth: `public bool Equals(System.Xaml.XamlMember other)`
- meth: `public override int GetHashCode()`
- meth: `public virtual System.Collections.Generic.IList<string> GetXamlNamespaces()`
- meth: `protected virtual System.Reflection.ICustomAttributeProvider LookupCustomAttributeProvider()`
- meth: `protected virtual System.Xaml.Schema.XamlValueConverter<System.Xaml.XamlDeferringLoader> LookupDeferringLoader()`
- meth: `protected virtual System.Collections.Generic.IList<System.Xaml.XamlMember> LookupDependsOn()`
- meth: `protected virtual System.Xaml.Schema.XamlMemberInvoker LookupInvoker()`
- meth: `protected virtual bool LookupIsAmbient()`
- meth: `protected virtual bool LookupIsEvent()`
- meth: `protected virtual bool LookupIsReadOnly()`
- meth: `protected virtual bool LookupIsReadPublic()`
- meth: `protected virtual bool LookupIsUnknown()`
- meth: `protected virtual bool LookupIsWriteOnly()`
- meth: `protected virtual bool LookupIsWritePublic()`
- meth: `protected virtual System.Collections.Generic.IReadOnlyDictionary<char, char> LookupMarkupExtensionBracketCharacters()`
- meth: `protected virtual System.Xaml.XamlType LookupTargetType()`
- meth: `protected virtual System.Xaml.XamlType LookupType()`
- meth: `protected virtual System.Xaml.Schema.XamlValueConverter<System.ComponentModel.TypeConverter> LookupTypeConverter()`
- meth: `protected virtual System.Reflection.MethodInfo LookupUnderlyingGetter()`
- meth: `protected virtual System.Reflection.MemberInfo LookupUnderlyingMember()`
- meth: `protected virtual System.Reflection.MethodInfo LookupUnderlyingSetter()`
- meth: `protected virtual System.Xaml.Schema.XamlValueConverter<System.Windows.Markup.ValueSerializer> LookupValueSerializer()`
- meth: `public static bool operator ==(System.Xaml.XamlMember xamlMember1, System.Xaml.XamlMember xamlMember2)`
- meth: `public static bool operator !=(System.Xaml.XamlMember xamlMember1, System.Xaml.XamlMember xamlMember2)`
- meth: `public override string ToString()`

## System.Xaml.XamlNodeList  (class)  :
- ctor: `public XamlNodeList(System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlNodeList(System.Xaml.XamlSchemaContext schemaContext, int size)`
- prop: `public int Count`
- prop: `public System.Xaml.XamlWriter Writer`
- meth: `public void Clear()`
- meth: `public System.Xaml.XamlReader GetReader()`

## System.Xaml.XamlNodeQueue  (class)  :
- ctor: `public XamlNodeQueue(System.Xaml.XamlSchemaContext schemaContext)`
- prop: `public int Count`
- prop: `public bool IsEmpty`
- prop: `public System.Xaml.XamlReader Reader`
- prop: `public System.Xaml.XamlWriter Writer`

## System.Xaml.XamlNodeType  (enum)  : byte
- meth: `None = (byte)0,`
- meth: `StartObject = (byte)1,`
- meth: `GetObject = (byte)2,`
- meth: `EndObject = (byte)3,`
- meth: `StartMember = (byte)4,`
- meth: `EndMember = (byte)5,`
- meth: `Value = (byte)6,`
- meth: `NamespaceDeclaration = (byte)7,`

## System.Xaml.XamlObjectEventArgs  (class)  : System.EventArgs
- ctor: `public XamlObjectEventArgs(object instance)`
- prop: `public int ElementLineNumber`
- prop: `public int ElementLinePosition`
- prop: `public object Instance`
- prop: `public System.Uri SourceBamlUri`

## System.Xaml.XamlObjectReader  (class)  : System.Xaml.XamlReader
- ctor: `public XamlObjectReader(object instance)`
- ctor: `public XamlObjectReader(object instance, System.Xaml.XamlObjectReaderSettings settings)`
- ctor: `public XamlObjectReader(object instance, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlObjectReader(object instance, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlObjectReaderSettings settings)`
- prop: `public virtual object Instance`
- prop: `public override bool IsEof`
- prop: `public override System.Xaml.XamlMember Member`
- prop: `public override System.Xaml.NamespaceDeclaration Namespace`
- prop: `public override System.Xaml.XamlNodeType NodeType`
- prop: `public override System.Xaml.XamlSchemaContext SchemaContext`
- prop: `public override System.Xaml.XamlType Type`
- prop: `public override object Value`
- meth: `public override bool Read()`

## System.Xaml.XamlObjectReaderException  (class)  : System.Xaml.XamlException
- ctor: `public XamlObjectReaderException()`
- ctor: `protected XamlObjectReaderException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlObjectReaderException(string message)`
- ctor: `public XamlObjectReaderException(string message, System.Exception innerException)`

## System.Xaml.XamlObjectReaderSettings  (class)  : System.Xaml.XamlReaderSettings
- ctor: `public XamlObjectReaderSettings()`
- prop: `public bool RequireExplicitContentVisibility`

## System.Xaml.XamlObjectWriter  (class)  : System.Xaml.XamlWriter, System.Xaml.IXamlLineInfoConsumer
- ctor: `public XamlObjectWriter(System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlObjectWriter(System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlObjectWriterSettings settings)`
- prop: `public virtual object Result`
- prop: `public System.Windows.Markup.INameScope RootNameScope`
- prop: `public override System.Xaml.XamlSchemaContext SchemaContext`
- prop: `public bool ShouldProvideLineInfo`
- meth: `public void Clear()`
- meth: `protected override void Dispose(bool disposing)`
- meth: `protected virtual void OnAfterBeginInit(object value)`
- meth: `protected virtual void OnAfterEndInit(object value)`
- meth: `protected virtual void OnAfterProperties(object value)`
- meth: `protected virtual void OnBeforeProperties(object value)`
- meth: `protected virtual bool OnSetValue(object eventSender, System.Xaml.XamlMember member, object value)`
- meth: `public void SetLineInfo(int lineNumber, int linePosition)`
- meth: `public override void WriteEndMember()`
- meth: `public override void WriteEndObject()`
- meth: `public override void WriteGetObject()`
- meth: `public override void WriteNamespace(System.Xaml.NamespaceDeclaration namespaceDeclaration)`
- meth: `public override void WriteStartMember(System.Xaml.XamlMember property)`
- meth: `public override void WriteStartObject(System.Xaml.XamlType xamlType)`
- meth: `public override void WriteValue(object value)`

## System.Xaml.XamlObjectWriterException  (class)  : System.Xaml.XamlException
- ctor: `public XamlObjectWriterException()`
- ctor: `protected XamlObjectWriterException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlObjectWriterException(string message)`
- ctor: `public XamlObjectWriterException(string message, System.Exception innerException)`

## System.Xaml.XamlObjectWriterSettings  (class)  : System.Xaml.XamlWriterSettings
- ctor: `public XamlObjectWriterSettings()`
- ctor: `public XamlObjectWriterSettings(System.Xaml.XamlObjectWriterSettings settings)`
- prop: `public System.Xaml.Permissions.XamlAccessLevel AccessLevel`
- prop: `public System.EventHandler<System.Xaml.XamlObjectEventArgs> AfterBeginInitHandler`
- prop: `public System.EventHandler<System.Xaml.XamlObjectEventArgs> AfterEndInitHandler`
- prop: `public System.EventHandler<System.Xaml.XamlObjectEventArgs> AfterPropertiesHandler`
- prop: `public System.EventHandler<System.Xaml.XamlObjectEventArgs> BeforePropertiesHandler`
- prop: `public System.Windows.Markup.INameScope ExternalNameScope`
- prop: `public bool IgnoreCanConvert`
- prop: `public bool PreferUnconvertedDictionaryKeys`
- prop: `public bool RegisterNamesOnExternalNamescope`
- prop: `public object RootObjectInstance`
- prop: `public bool SkipDuplicatePropertyCheck`
- prop: `public bool SkipProvideValueOnRoot`
- prop: `public System.Uri SourceBamlUri`
- prop: `public System.EventHandler<System.Windows.Markup.XamlSetValueEventArgs> XamlSetValueHandler`

## System.Xaml.XamlReaderSettings  (class)  :
- ctor: `public XamlReaderSettings()`
- ctor: `public XamlReaderSettings(System.Xaml.XamlReaderSettings settings)`
- prop: `public bool AllowProtectedMembersOnRoot`
- prop: `public System.Uri BaseUri`
- prop: `public bool IgnoreUidsOnPropertyElements`
- prop: `public System.Reflection.Assembly LocalAssembly`
- prop: `public bool ProvideLineInfo`
- prop: `public bool ValuesMustBeString`

## System.Xaml.XamlSchemaContext  (class)  :
- ctor: `public XamlSchemaContext()`
- ctor: `public XamlSchemaContext(System.Collections.Generic.IEnumerable<System.Reflection.Assembly> referenceAssemblies)`
- ctor: `public XamlSchemaContext(System.Collections.Generic.IEnumerable<System.Reflection.Assembly> referenceAssemblies, System.Xaml.XamlSchemaContextSettings settings)`
- ctor: `public XamlSchemaContext(System.Xaml.XamlSchemaContextSettings settings)`
- prop: `public bool FullyQualifyAssemblyNamesInClrNamespaces`
- prop: `public System.Collections.Generic.IList<System.Reflection.Assembly> ReferenceAssemblies`
- prop: `public bool SupportMarkupExtensionsWithDuplicateArity`
- meth: `~XamlSchemaContext()`
- meth: `public virtual System.Collections.Generic.IEnumerable<string> GetAllXamlNamespaces()`
- meth: `public virtual System.Collections.Generic.ICollection<System.Xaml.XamlType> GetAllXamlTypes(string xamlNamespace)`
- meth: `public virtual string GetPreferredPrefix(string xmlns)`
- meth: `protected internal System.Xaml.Schema.XamlValueConverter<TConverterBase> GetValueConverter<TConverterBase>(System.Type converterType, System.Xaml.XamlType targetType) where TConverterBase : class`
- meth: `public virtual System.Xaml.XamlDirective GetXamlDirective(string xamlNamespace, string name)`
- meth: `protected internal virtual System.Xaml.XamlType GetXamlType(string xamlNamespace, string name, params System.Xaml.XamlType[] typeArguments)`
- meth: `public virtual System.Xaml.XamlType GetXamlType(System.Type type)`
- meth: `public System.Xaml.XamlType GetXamlType(System.Xaml.Schema.XamlTypeName xamlTypeName)`
- meth: `protected internal virtual System.Reflection.Assembly OnAssemblyResolve(string assemblyName)`
- meth: `public virtual bool TryGetCompatibleXamlNamespace(string xamlNamespace, out string compatibleNamespace)`

## System.Xaml.XamlSchemaContextSettings  (class)  :
- ctor: `public XamlSchemaContextSettings()`
- ctor: `public XamlSchemaContextSettings(System.Xaml.XamlSchemaContextSettings settings)`
- prop: `public bool FullyQualifyAssemblyNamesInClrNamespaces`
- prop: `public bool SupportMarkupExtensionsWithDuplicateArity`

## System.Xaml.XamlSchemaException  (class)  : System.Xaml.XamlException
- ctor: `public XamlSchemaException()`
- ctor: `protected XamlSchemaException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlSchemaException(string message)`
- ctor: `public XamlSchemaException(string message, System.Exception innerException)`

## System.Xaml.XamlServices  (class)  :
- meth: `public static object Load(System.IO.Stream stream)`
- meth: `public static object Load(System.IO.TextReader textReader)`
- meth: `public static object Load(string fileName)`
- meth: `public static object Load(System.Xaml.XamlReader xamlReader)`
- meth: `public static object Load(System.Xml.XmlReader xmlReader)`
- meth: `public static object Parse(string xaml)`
- meth: `public static void Save(System.IO.Stream stream, object instance)`
- meth: `public static void Save(System.IO.TextWriter writer, object instance)`
- meth: `public static string Save(object instance)`
- meth: `public static void Save(string fileName, object instance)`
- meth: `public static void Save(System.Xaml.XamlWriter writer, object instance)`
- meth: `public static void Save(System.Xml.XmlWriter writer, object instance)`
- meth: `public static void Transform(System.Xaml.XamlReader xamlReader, System.Xaml.XamlWriter xamlWriter)`
- meth: `public static void Transform(System.Xaml.XamlReader xamlReader, System.Xaml.XamlWriter xamlWriter, bool closeWriter)`

## System.Xaml.XamlType  (class)  : System.IEquatable<System.Xaml.XamlType>
- ctor: `protected XamlType(string typeName, System.Collections.Generic.IList<System.Xaml.XamlType> typeArguments, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlType(string unknownTypeNamespace, string unknownTypeName, System.Collections.Generic.IList<System.Xaml.XamlType> typeArguments, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlType(System.Type underlyingType, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlType(System.Type underlyingType, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.Schema.XamlTypeInvoker invoker)`
- prop: `public System.Collections.Generic.IList<System.Xaml.XamlType> AllowedContentTypes`
- prop: `public System.Xaml.XamlType BaseType`
- prop: `public bool ConstructionRequiresArguments`
- prop: `public System.Xaml.XamlMember ContentProperty`
- prop: `public System.Collections.Generic.IList<System.Xaml.XamlType> ContentWrappers`
- prop: `public System.Xaml.Schema.XamlValueConverter<System.Xaml.XamlDeferringLoader> DeferringLoader`
- prop: `public System.Xaml.Schema.XamlTypeInvoker Invoker`
- prop: `public bool IsAmbient`
- prop: `public bool IsArray`
- prop: `public bool IsCollection`
- prop: `public bool IsConstructible`
- prop: `public bool IsDictionary`
- prop: `public bool IsGeneric`
- prop: `public bool IsMarkupExtension`
- prop: `public bool IsNameScope`
- prop: `public bool IsNameValid`
- prop: `public bool IsNullable`
- prop: `public bool IsPublic`
- prop: `public bool IsUnknown`
- prop: `public bool IsUsableDuringInitialization`
- prop: `public bool IsWhitespaceSignificantCollection`
- prop: `public bool IsXData`
- prop: `public System.Xaml.XamlType ItemType`
- prop: `public System.Xaml.XamlType KeyType`
- prop: `public System.Xaml.XamlType MarkupExtensionReturnType`
- prop: `public string Name`
- prop: `public string PreferredXamlNamespace`
- prop: `public System.Xaml.XamlSchemaContext SchemaContext`
- prop: `public bool TrimSurroundingWhitespace`
- prop: `public System.Collections.Generic.IList<System.Xaml.XamlType> TypeArguments`
- prop: `public System.Xaml.Schema.XamlValueConverter<System.ComponentModel.TypeConverter> TypeConverter`
- prop: `public System.Type UnderlyingType`
- prop: `public System.Xaml.Schema.XamlValueConverter<System.Windows.Markup.ValueSerializer> ValueSerializer`
- meth: `public virtual bool CanAssignTo(System.Xaml.XamlType xamlType)`
- meth: `public override bool Equals(object obj)`
- meth: `public bool Equals(System.Xaml.XamlType other)`
- meth: `public System.Xaml.XamlMember GetAliasedProperty(System.Xaml.XamlDirective directive)`
- meth: `public System.Collections.Generic.ICollection<System.Xaml.XamlMember> GetAllAttachableMembers()`
- meth: `public System.Collections.Generic.ICollection<System.Xaml.XamlMember> GetAllMembers()`
- meth: `public System.Xaml.XamlMember GetAttachableMember(string name)`
- meth: `public override int GetHashCode()`
- meth: `public System.Xaml.XamlMember GetMember(string name)`
- meth: `public System.Collections.Generic.IList<System.Xaml.XamlType> GetPositionalParameters(int parameterCount)`
- meth: `public virtual System.Collections.Generic.IList<string> GetXamlNamespaces()`
- meth: `protected virtual System.Xaml.XamlMember LookupAliasedProperty(System.Xaml.XamlDirective directive)`
- meth: `protected virtual System.Collections.Generic.IEnumerable<System.Xaml.XamlMember> LookupAllAttachableMembers()`
- meth: `protected virtual System.Collections.Generic.IEnumerable<System.Xaml.XamlMember> LookupAllMembers()`
- meth: `protected virtual System.Collections.Generic.IList<System.Xaml.XamlType> LookupAllowedContentTypes()`
- meth: `protected virtual System.Xaml.XamlMember LookupAttachableMember(string name)`
- meth: `protected virtual System.Xaml.XamlType LookupBaseType()`
- meth: `protected virtual System.Xaml.Schema.XamlCollectionKind LookupCollectionKind()`
- meth: `protected virtual bool LookupConstructionRequiresArguments()`
- meth: `protected virtual System.Xaml.XamlMember LookupContentProperty()`
- meth: `protected virtual System.Collections.Generic.IList<System.Xaml.XamlType> LookupContentWrappers()`
- meth: `protected virtual System.Reflection.ICustomAttributeProvider LookupCustomAttributeProvider()`
- meth: `protected virtual System.Xaml.Schema.XamlValueConverter<System.Xaml.XamlDeferringLoader> LookupDeferringLoader()`
- meth: `protected virtual System.Xaml.Schema.XamlTypeInvoker LookupInvoker()`
- meth: `protected virtual bool LookupIsAmbient()`
- meth: `protected virtual bool LookupIsConstructible()`
- meth: `protected virtual bool LookupIsMarkupExtension()`
- meth: `protected virtual bool LookupIsNameScope()`
- meth: `protected virtual bool LookupIsNullable()`
- meth: `protected virtual bool LookupIsPublic()`
- meth: `protected virtual bool LookupIsUnknown()`
- meth: `protected virtual bool LookupIsWhitespaceSignificantCollection()`
- meth: `protected virtual bool LookupIsXData()`
- meth: `protected virtual System.Xaml.XamlType LookupItemType()`
- meth: `protected virtual System.Xaml.XamlType LookupKeyType()`
- meth: `protected virtual System.Xaml.XamlType LookupMarkupExtensionReturnType()`
- meth: `protected virtual System.Xaml.XamlMember LookupMember(string name, bool skipReadOnlyCheck)`
- meth: `protected virtual System.Collections.Generic.IList<System.Xaml.XamlType> LookupPositionalParameters(int parameterCount)`
- meth: `protected virtual System.EventHandler<System.Windows.Markup.XamlSetMarkupExtensionEventArgs> LookupSetMarkupExtensionHandler()`
- meth: `protected virtual System.EventHandler<System.Windows.Markup.XamlSetTypeConverterEventArgs> LookupSetTypeConverterHandler()`
- meth: `protected virtual bool LookupTrimSurroundingWhitespace()`
- meth: `protected virtual System.Xaml.Schema.XamlValueConverter<System.ComponentModel.TypeConverter> LookupTypeConverter()`
- meth: `protected virtual System.Type LookupUnderlyingType()`
- meth: `protected virtual bool LookupUsableDuringInitialization()`
- meth: `protected virtual System.Xaml.Schema.XamlValueConverter<System.Windows.Markup.ValueSerializer> LookupValueSerializer()`
- meth: `public static bool operator ==(System.Xaml.XamlType xamlType1, System.Xaml.XamlType xamlType2)`
- meth: `public static bool operator !=(System.Xaml.XamlType xamlType1, System.Xaml.XamlType xamlType2)`
- meth: `public override string ToString()`

## System.Xaml.XamlWriterSettings  (class)  :
- ctor: `public XamlWriterSettings()`
- ctor: `public XamlWriterSettings(System.Xaml.XamlWriterSettings settings)`

## System.Xaml.XamlXmlReader  (class)  : System.Xaml.XamlReader, System.Xaml.IXamlLineInfo
- ctor: `public XamlXmlReader(System.IO.Stream stream)`
- ctor: `public XamlXmlReader(System.IO.Stream stream, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlReader(System.IO.Stream stream, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(System.IO.Stream stream, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(System.IO.TextReader textReader)`
- ctor: `public XamlXmlReader(System.IO.TextReader textReader, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlReader(System.IO.TextReader textReader, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(System.IO.TextReader textReader, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(string fileName)`
- ctor: `public XamlXmlReader(string fileName, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlReader(string fileName, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(string fileName, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(System.Xml.XmlReader xmlReader)`
- ctor: `public XamlXmlReader(System.Xml.XmlReader xmlReader, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlReader(System.Xml.XmlReader xmlReader, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlReaderSettings settings)`
- ctor: `public XamlXmlReader(System.Xml.XmlReader xmlReader, System.Xaml.XamlXmlReaderSettings settings)`
- prop: `public bool HasLineInfo`
- prop: `public override bool IsEof`
- prop: `public int LineNumber`
- prop: `public int LinePosition`
- prop: `public override System.Xaml.XamlMember Member`
- prop: `public override System.Xaml.NamespaceDeclaration Namespace`
- prop: `public override System.Xaml.XamlNodeType NodeType`
- prop: `public override System.Xaml.XamlSchemaContext SchemaContext`
- prop: `public override System.Xaml.XamlType Type`
- prop: `public override object Value`
- meth: `public override bool Read()`

## System.Xaml.XamlXmlReaderSettings  (class)  : System.Xaml.XamlReaderSettings
- ctor: `public XamlXmlReaderSettings()`
- ctor: `public XamlXmlReaderSettings(System.Xaml.XamlXmlReaderSettings settings)`
- prop: `public bool CloseInput`
- prop: `public bool SkipXmlCompatibilityProcessing`
- prop: `public string XmlLang`
- prop: `public bool XmlSpacePreserve`

## System.Xaml.XamlXmlWriter  (class)  : System.Xaml.XamlWriter
- ctor: `public XamlXmlWriter(System.IO.Stream stream, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlWriter(System.IO.Stream stream, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlWriterSettings settings)`
- ctor: `public XamlXmlWriter(System.IO.TextWriter textWriter, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlWriter(System.IO.TextWriter textWriter, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlWriterSettings settings)`
- ctor: `public XamlXmlWriter(System.Xml.XmlWriter xmlWriter, System.Xaml.XamlSchemaContext schemaContext)`
- ctor: `public XamlXmlWriter(System.Xml.XmlWriter xmlWriter, System.Xaml.XamlSchemaContext schemaContext, System.Xaml.XamlXmlWriterSettings settings)`
- prop: `public override System.Xaml.XamlSchemaContext SchemaContext`
- prop: `public System.Xaml.XamlXmlWriterSettings Settings`
- meth: `protected override void Dispose(bool disposing)`
- meth: `public void Flush()`
- meth: `public override void WriteEndMember()`
- meth: `public override void WriteEndObject()`
- meth: `public override void WriteGetObject()`
- meth: `public override void WriteNamespace(System.Xaml.NamespaceDeclaration namespaceDeclaration)`
- meth: `public override void WriteStartMember(System.Xaml.XamlMember property)`
- meth: `public override void WriteStartObject(System.Xaml.XamlType type)`
- meth: `public override void WriteValue(object value)`

## System.Xaml.XamlXmlWriterException  (class)  : System.Xaml.XamlException
- ctor: `public XamlXmlWriterException()`
- ctor: `protected XamlXmlWriterException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)`
- ctor: `public XamlXmlWriterException(string message)`
- ctor: `public XamlXmlWriterException(string message, System.Exception innerException)`

## System.Xaml.XamlXmlWriterSettings  (class)  : System.Xaml.XamlWriterSettings
- ctor: `public XamlXmlWriterSettings()`
- prop: `public bool AssumeValidInput`
- prop: `public bool CloseOutput`
- meth: `public System.Xaml.XamlXmlWriterSettings Copy()`
