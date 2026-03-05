# Jalium.UI 完整功能实现计划

## 概述

基于对 Jalium.UI 项目的全面分析，与 WPF 对比后确定以下缺失功能需要实现。

## 当前状态

### 已完成的核心功能
- ✅ DependencyProperty 系统 (100%)
- ✅ Binding 系统 (100%)
- ✅ Style/Trigger 系统 (100%)
- ✅ ControlTemplate 系统 (100%)
- ✅ VisualStateManager (100%)
- ✅ RoutedEvent 系统 (100%)
- ✅ Animation 系统 (95%)
- ✅ AutomationPeer (100%)
- ✅ RichTextBox (100%)

### 已部分实现
- ⚡ Commands (有 ICommand/RelayCommand，缺 RoutedCommand)
- ⚡ Effects (有 BackdropEffect，缺 DropShadowEffect)
- ⚡ Shapes (有 Rectangle/Ellipse/Path，缺 Line/Polygon/Polyline)
- ⚡ DragDrop (有框架，缺完整 Windows 集成)
- ⚡ Clipboard (仅文本，缺图像)

### 完全缺失
- ❌ RoutedCommand / CommandBinding
- ❌ ICollectionView / CollectionViewSource
- ❌ Adorner 系统
- ❌ Behavior<T> 系统
- ❌ Freezable 模式
- ❌ PropertyPath 完整实现

---

## Phase 1: Commands 系统 (高优先级)

### 1.1 RoutedCommand

**文件**: `src/managed/Jalium.UI.Core/Input/RoutedCommand.cs`

```csharp
public class RoutedCommand : ICommand
{
    public string Name { get; }
    public Type OwnerType { get; }
    public InputGestureCollection InputGestures { get; }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter, IInputElement target);
    public void Execute(object? parameter, IInputElement target);
}
```

### 1.2 RoutedUICommand

**文件**: `src/managed/Jalium.UI.Core/Input/RoutedUICommand.cs`

```csharp
public class RoutedUICommand : RoutedCommand
{
    public string Text { get; set; }
}
```

### 1.3 CommandBinding

**文件**: `src/managed/Jalium.UI.Core/Input/CommandBinding.cs`

```csharp
public class CommandBinding
{
    public ICommand Command { get; set; }
    public event ExecutedRoutedEventHandler Executed;
    public event CanExecuteRoutedEventHandler CanExecute;
}
```

### 1.4 ApplicationCommands

**文件**: `src/managed/Jalium.UI.Core/Input/ApplicationCommands.cs`

```csharp
public static class ApplicationCommands
{
    public static RoutedUICommand Cut { get; }
    public static RoutedUICommand Copy { get; }
    public static RoutedUICommand Paste { get; }
    public static RoutedUICommand Delete { get; }
    public static RoutedUICommand Undo { get; }
    public static RoutedUICommand Redo { get; }
    public static RoutedUICommand SelectAll { get; }
    public static RoutedUICommand New { get; }
    public static RoutedUICommand Open { get; }
    public static RoutedUICommand Save { get; }
    public static RoutedUICommand SaveAs { get; }
    public static RoutedUICommand Close { get; }
    public static RoutedUICommand Print { get; }
    public static RoutedUICommand Find { get; }
    public static RoutedUICommand Replace { get; }
    public static RoutedUICommand Help { get; }
}
```

### 1.5 UIElement 扩展

修改 `UIElement.cs` 添加:
```csharp
public CommandBindingCollection CommandBindings { get; }
public InputBindingCollection InputBindings { get; }
```

---

## Phase 2: CollectionView 系统 (高优先级)

### 2.1 ICollectionView

**文件**: `src/managed/Jalium.UI.Core/Data/ICollectionView.cs`

```csharp
public interface ICollectionView : IEnumerable, INotifyCollectionChanged
{
    bool CanFilter { get; }
    bool CanGroup { get; }
    bool CanSort { get; }

    CultureInfo Culture { get; set; }
    object CurrentItem { get; }
    int CurrentPosition { get; }
    Predicate<object>? Filter { get; set; }
    ObservableCollection<GroupDescription> GroupDescriptions { get; }
    ReadOnlyObservableCollection<object> Groups { get; }
    bool IsCurrentAfterLast { get; }
    bool IsCurrentBeforeFirst { get; }
    bool IsEmpty { get; }
    SortDescriptionCollection SortDescriptions { get; }
    IEnumerable SourceCollection { get; }

    event EventHandler CurrentChanged;
    event CurrentChangingEventHandler CurrentChanging;

    bool Contains(object item);
    IDisposable DeferRefresh();
    bool MoveCurrentTo(object item);
    bool MoveCurrentToFirst();
    bool MoveCurrentToLast();
    bool MoveCurrentToNext();
    bool MoveCurrentToPosition(int position);
    bool MoveCurrentToPrevious();
    void Refresh();
}
```

### 2.2 CollectionView

**文件**: `src/managed/Jalium.UI.Core/Data/CollectionView.cs`

### 2.3 ListCollectionView

**文件**: `src/managed/Jalium.UI.Core/Data/ListCollectionView.cs`

### 2.4 CollectionViewSource

**文件**: `src/managed/Jalium.UI.Core/Data/CollectionViewSource.cs`

```csharp
public class CollectionViewSource : DependencyObject, ISupportInitialize
{
    public static readonly DependencyProperty SourceProperty;
    public static readonly DependencyProperty ViewProperty;

    public object Source { get; set; }
    public ICollectionView View { get; }
    public CultureInfo Culture { get; set; }
    public ObservableCollection<GroupDescription> GroupDescriptions { get; }
    public SortDescriptionCollection SortDescriptions { get; }

    public event FilterEventHandler Filter;

    public static ICollectionView GetDefaultView(object source);
}
```

### 2.5 SortDescription

**文件**: `src/managed/Jalium.UI.Core/Data/SortDescription.cs`

```csharp
public struct SortDescription
{
    public string PropertyName { get; set; }
    public ListSortDirection Direction { get; set; }
}
```

### 2.6 GroupDescription

**文件**: `src/managed/Jalium.UI.Core/Data/GroupDescription.cs`

```csharp
public abstract class GroupDescription : INotifyPropertyChanged
{
    public abstract object GroupNameFromItem(object item, int level, CultureInfo culture);
}

public class PropertyGroupDescription : GroupDescription
{
    public string PropertyName { get; set; }
    public IValueConverter Converter { get; set; }
}
```

---

## Phase 3: Adorner 系统 (中等优先级)

### 3.1 Adorner

**文件**: `src/managed/Jalium.UI.Core/Documents/Adorner.cs`

```csharp
public abstract class Adorner : FrameworkElement
{
    protected Adorner(UIElement adornedElement);

    public UIElement AdornedElement { get; }
    public bool IsClipEnabled { get; set; }

    public virtual GeneralTransform GetDesiredTransform(GeneralTransform transform);
}
```

### 3.2 AdornerLayer

**文件**: `src/managed/Jalium.UI.Core/Documents/AdornerLayer.cs`

```csharp
public class AdornerLayer : FrameworkElement
{
    public void Add(Adorner adorner);
    public void Remove(Adorner adorner);
    public Adorner[] GetAdorners(UIElement element);
    public void Update();
    public void Update(UIElement element);

    public static AdornerLayer GetAdornerLayer(Visual visual);
}
```

### 3.3 AdornerDecorator

**文件**: `src/managed/Jalium.UI.Core/Documents/AdornerDecorator.cs`

```csharp
public class AdornerDecorator : Decorator
{
    public AdornerLayer AdornerLayer { get; }
}
```

---

## Phase 4: Behavior 系统 (中等优先级)

### 4.1 Behavior<T>

**文件**: `src/managed/Jalium.UI.Core/Interactivity/Behavior.cs`

```csharp
public abstract class Behavior : Animatable, IAttachedObject
{
    public DependencyObject AssociatedObject { get; }

    public void Attach(DependencyObject dependencyObject);
    public void Detach();

    protected virtual void OnAttached();
    protected virtual void OnDetaching();
}

public abstract class Behavior<T> : Behavior where T : DependencyObject
{
    public new T AssociatedObject { get; }
}
```

### 4.2 TriggerBase

**文件**: `src/managed/Jalium.UI.Core/Interactivity/TriggerBase.cs`

```csharp
public abstract class TriggerBase : Animatable, IAttachedObject
{
    public TriggerActionCollection Actions { get; }
    public DependencyObject AssociatedObject { get; }

    protected void InvokeActions(object parameter);
}

public abstract class TriggerBase<T> : TriggerBase where T : DependencyObject
{
    public new T AssociatedObject { get; }
}
```

### 4.3 EventTrigger

**文件**: `src/managed/Jalium.UI.Core/Interactivity/EventTrigger.cs`

```csharp
public class EventTrigger : TriggerBase<FrameworkElement>
{
    public static readonly DependencyProperty EventNameProperty;
    public string EventName { get; set; }
}
```

### 4.4 TriggerAction

**文件**: `src/managed/Jalium.UI.Core/Interactivity/TriggerAction.cs`

```csharp
public abstract class TriggerAction : Animatable, IAttachedObject
{
    public DependencyObject AssociatedObject { get; }
    protected abstract void Invoke(object parameter);
}

public abstract class TriggerAction<T> : TriggerAction where T : DependencyObject
{
    public new T AssociatedObject { get; }
}
```

### 4.5 InvokeCommandAction

**文件**: `src/managed/Jalium.UI.Core/Interactivity/InvokeCommandAction.cs`

```csharp
public class InvokeCommandAction : TriggerAction<DependencyObject>
{
    public static readonly DependencyProperty CommandProperty;
    public static readonly DependencyProperty CommandParameterProperty;

    public ICommand Command { get; set; }
    public object CommandParameter { get; set; }

    protected override void Invoke(object parameter);
}
```

### 4.6 Interaction

**文件**: `src/managed/Jalium.UI.Core/Interactivity/Interaction.cs`

```csharp
public static class Interaction
{
    public static readonly DependencyProperty BehaviorsProperty;
    public static readonly DependencyProperty TriggersProperty;

    public static BehaviorCollection GetBehaviors(DependencyObject obj);
    public static TriggerCollection GetTriggers(DependencyObject obj);
}
```

---

## Phase 5: Shapes 补全 (中等优先级)

### 5.1 Line

**文件**: `src/managed/Jalium.UI.Controls/Shapes/Line.cs`

```csharp
public class Line : Shape
{
    public static readonly DependencyProperty X1Property;
    public static readonly DependencyProperty Y1Property;
    public static readonly DependencyProperty X2Property;
    public static readonly DependencyProperty Y2Property;

    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
}
```

### 5.2 Polygon

**文件**: `src/managed/Jalium.UI.Controls/Shapes/Polygon.cs`

```csharp
public class Polygon : Shape
{
    public static readonly DependencyProperty PointsProperty;
    public static readonly DependencyProperty FillRuleProperty;

    public PointCollection Points { get; set; }
    public FillRule FillRule { get; set; }
}
```

### 5.3 Polyline

**文件**: `src/managed/Jalium.UI.Controls/Shapes/Polyline.cs`

```csharp
public class Polyline : Shape
{
    public static readonly DependencyProperty PointsProperty;
    public static readonly DependencyProperty FillRuleProperty;

    public PointCollection Points { get; set; }
    public FillRule FillRule { get; set; }
}
```

---

## Phase 6: Freezable 模式 (低优先级)

### 6.1 Freezable

**文件**: `src/managed/Jalium.UI.Core/Freezable.cs`

```csharp
public abstract class Freezable : DependencyObject
{
    public bool CanFreeze { get; }
    public bool IsFrozen { get; }

    public Freezable Clone();
    public Freezable CloneCurrentValue();
    public void Freeze();
    public static bool IsFreezeableCore { get; }

    protected virtual void CloneCore(Freezable sourceFreezable);
    protected virtual void CloneCurrentValueCore(Freezable sourceFreezable);
    protected virtual bool FreezeCore(bool isChecking);
    protected Freezable CreateInstance();
    protected abstract Freezable CreateInstanceCore();
    protected virtual void GetAsFrozenCore(Freezable sourceFreezable);
    protected virtual void GetCurrentValueAsFrozenCore(Freezable sourceFreezable);
    protected void OnChanged();
    protected virtual void OnFreezablePropertyChanged(DependencyObject oldValue, DependencyObject newValue);
    protected void WritePostscript();
    protected void WritePreamble();
}
```

---

## Phase 7: PropertyPath 完整实现 (低优先级)

### 7.1 PropertyPath

**文件**: `src/managed/Jalium.UI.Core/PropertyPath.cs`

```csharp
public sealed class PropertyPath
{
    public PropertyPath(string path);
    public PropertyPath(string path, params object[] pathParameters);
    public PropertyPath(object parameter);

    public string Path { get; }
    public IList PathParameters { get; }

    internal object? ResolveValue(object source);
    internal bool SetValue(object source, object value);
}
```

---

## Phase 8: Clipboard 增强 (低优先级)

### 8.1 Clipboard 增强

修改 `src/managed/Jalium.UI.Interop/Clipboard.cs`:

```csharp
public static class Clipboard
{
    // 现有
    public static string GetText();
    public static void SetText(string text);
    public static bool ContainsText();
    public static void Clear();

    // 新增
    public static ImageSource? GetImage();
    public static void SetImage(ImageSource image);
    public static bool ContainsImage();

    public static IDataObject GetDataObject();
    public static void SetDataObject(object data);
    public static void SetDataObject(object data, bool copy);
}
```

---

## 实现顺序

| 阶段 | 功能 | 优先级 | 预计文件数 |
|------|------|--------|-----------|
| 1 | Commands 系统 | 高 | 8 |
| 2 | CollectionView 系统 | 高 | 8 |
| 3 | Adorner 系统 | 中 | 3 |
| 4 | Behavior 系统 | 中 | 7 |
| 5 | Shapes 补全 | 中 | 3 |
| 6 | Freezable 模式 | 低 | 1 |
| 7 | PropertyPath | 低 | 1 |
| 8 | Clipboard 增强 | 低 | 1 |

**总计**: 约 32 个新文件

---

## 测试计划

每个阶段完成后:
1. 编写单元测试
2. 运行现有测试确保无回归
3. 在 Gallery 示例中添加演示

---

## 文件结构

```
src/managed/Jalium.UI.Core/
├── Data/
│   ├── ICollectionView.cs
│   ├── CollectionView.cs
│   ├── ListCollectionView.cs
│   ├── CollectionViewSource.cs
│   ├── SortDescription.cs
│   └── GroupDescription.cs
├── Documents/
│   ├── Adorner.cs
│   ├── AdornerLayer.cs
│   └── AdornerDecorator.cs
├── Input/
│   ├── RoutedCommand.cs
│   ├── RoutedUICommand.cs
│   ├── CommandBinding.cs
│   ├── CommandBindingCollection.cs
│   ├── InputBinding.cs
│   ├── InputBindingCollection.cs
│   ├── InputGestureCollection.cs
│   └── ApplicationCommands.cs
├── Interactivity/
│   ├── Behavior.cs
│   ├── BehaviorCollection.cs
│   ├── TriggerBase.cs
│   ├── TriggerAction.cs
│   ├── TriggerCollection.cs
│   ├── EventTrigger.cs
│   ├── InvokeCommandAction.cs
│   └── Interaction.cs
├── Freezable.cs
└── PropertyPath.cs

src/managed/Jalium.UI.Controls/Shapes/
├── Line.cs
├── Polygon.cs
└── Polyline.cs
```
