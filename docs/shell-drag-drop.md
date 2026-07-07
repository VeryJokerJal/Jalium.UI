# Jalium.UI Windows Shell 拖放增强

本文档描述 Jalium.UI 在 Windows 上的 Shell 拖放增强能力，对应实现：

- `src/managed/Jalium.UI.Core/Input/DragDrop.cs` —— 公共 API（`DoDragDrop` 重载、`DoShellDragDrop`、附加属性、`DropImageType`、`DragEventArgs.SetDropDescription`）
- `src/managed/Jalium.UI.Controls/DragDropPlatform.cs` —— 拖放源（应用内）：把调用方指定的拖拽图像渲染为跟随光标的覆盖层视觉
- `src/managed/Jalium.UI.Controls/OleDragSource.cs` —— 拖放源（跨进程）：手搓 AOT COM 桥（`IDataObject`/`IDropSource`/`IEnumFORMATETC`）+ `IDragSourceHelper` + 真 `DoDragDrop`
- `src/managed/Jalium.UI.Controls/OleDropTarget.cs` —— 拖放目标：接入 Shell `IDropTargetHelper` 与 `DropDescription`，并对提权进程放行拖放消息

对应需求：[issue #150](https://github.com/VeryJokerJal/Jalium.UI/issues/150)。

## 1. 能力概览

| 角色 | 能力 | 说明 |
|------|------|------|
| 拖放源（应用内） | 自定义拖拽图像 | 调用方可为一次拖拽指定 `ImageSource`（或任意未挂载的 `FrameworkElement`）作为跟随光标的视觉，并可设置光标热点偏移。 |
| 拖放源（跨进程） | 拖出到其他应用 | `DragDrop.DoShellDragDrop` 走真 OLE 拖放，可把文本/文件拖出到资源管理器等其他应用，并显示 Shell 系统拖拽图像。 |
| 拖放目标（Drop Target） | Shell 拖拽图像 | 默认支持：接收外部 OLE 拖放（如资源管理器文件）时，Shell 自动在窗口上渲染系统拖拽图像（“幽灵图”）。 |
| 拖放目标（Drop Target） | 拖拽描述 | 处理器可在 `DragEnter`/`DragOver`/`Drop` 中设置 Shell 描述文本（如“复制到 文档”），显示在光标旁。 |
| 拖放目标（Drop Target） | 提权进程接收 | 窗口注册拖放目标时对 UIPI 放行拖放消息，使**以管理员身份运行**时仍能接收来自资源管理器（中完整性）的拖入。 |

> 设计要点：源端有**两条独立路径**。默认的应用内拖拽（`DragDrop.DoDragDrop`）由 Jalium.UI 自身合成器（`OverlayLayer`）渲染覆盖层视觉，保留 Preview 事件与 GPU 合成，但不跨进程。需要拖出到其他应用时，改用 opt-in 的 `DragDrop.DoShellDragDrop`——它构建真 OLE `IDataObject`/`IDropSource`，并用 Shell `IDragSourceHelper` 渲染系统拖拽图像。目标端始终使用真正的 Shell `IDropTarget` + `IDropTargetHelper`，与资源管理器行为一致。

## 2. 拖放源：设置拖拽图像

在鼠标事件（通常是 `MouseMove`）中调用 `DragDrop.DoDragDrop` 的新重载，传入拖拽图像：

```csharp
// 用一张位图作为拖拽图像；光标默认位于图像左上角。
ImageSource image = /* 例如 BitmapImage / RenderTargetBitmap */;
DragDrop.DoDragDrop(sourceElement, data, DragDropEffects.Copy | DragDropEffects.Move, image);

// 指定热点：图像内 (offsetX, offsetY)（DIP，相对图像左上角）跟随光标。
DragDrop.DoDragDrop(sourceElement, data, DragDropEffects.Copy, image, new Point(16, 16));
```

也可以用附加属性在 JALXAML 或代码里“黏”在某个源元素上（每次拖拽生效，`DoDragDrop` 重载传入的图像优先级更高）：

```csharp
DragDrop.SetDragImage(sourceElement, image);
DragDrop.SetDragImageOffset(sourceElement, new Point(16, 16));
// 之后任意一次 DragDrop.DoDragDrop(sourceElement, data, effects) 都会使用该图像
```

说明：

- `dragImage` 接受 `ImageSource`（渲染为轻量 `Image`）或未挂载到可视树的 `FrameworkElement`（直接使用）。
- 未指定图像时，行为与之前一致：渲染源元素的半透明克隆，光标热点为点击处相对源元素的位置。
- `DragDrop.ShowDragVisual` 附加属性仍可整体关闭源端拖拽视觉。

## 3. 拖放目标：Shell 拖拽图像

拖放目标的 Shell 拖拽图像是**默认启用**的，无需额外代码：只要窗口按常规注册了拖放目标（框架在窗口创建时自动完成），当外部 OLE 源（如资源管理器）拖入时，Shell 会自动在窗口上渲染系统拖拽图像并随光标移动、随放置效果显示 +/链接 等角标。

若运行环境无法创建 Shell `IDropTargetHelper`（极少数情况），该能力静默降级为“无图像”，不影响事件路由与数据提取。

## 4. 拖放目标：设置拖拽描述

在 `DragEnter`/`DragOver`/`Drop` 处理器中调用 `DragEventArgs.SetDropDescription`，即可设置光标旁的 Shell 描述文本：

```csharp
private void OnDragOver(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        e.Effects = DragDropEffects.Copy;
        // %1 会被 insert 参数替换 → “复制到 文档”
        e.SetDropDescription(DropImageType.Copy, "复制到 %1", "文档");
    }
    else
    {
        e.Effects = DragDropEffects.None;
        e.SetDropDescription(DropImageType.None, "无法放置");
    }
    e.Handled = true;
}
```

`DropImageType` 与 Win32 `DROPIMAGETYPE` 对应：

| 值 | 含义 |
|----|------|
| `Invalid` (-1) | 清除自定义描述，恢复 Shell 默认文本 |
| `None` (0) | “禁止放置”角标 |
| `Copy` (1) / `Move` (2) / `Link` (4) | 复制 / 移动 / 链接角标 |
| `Label` (6) / `Warning` (7) | 标签 / 警告角标 |
| `NoImage` (8) | 仅文本、无角标 |

要点与限制：

- `message` 可含单个 `%1` 占位符，由 Shell 用 `insert` 替换；传 `null`/空则显示该类型的 Shell 默认文本。
- 描述文本写入的是**发起拖拽的源端数据对象**（由源端的拖拽图像窗口负责渲染），因此仅对来自外部 OLE 源（如资源管理器）的拖放有效；对纯应用内拖放是静默空操作。
- 框架会在 `DragLeave`/`Drop` 时自动清除本次设置的描述；`e.ClearDropDescription()` 可手动恢复默认。

## 5. 拖放源：拖出到其他应用（跨进程）

应用内拖拽（`DoDragDrop`）不会离开窗口。要把内容拖出到资源管理器或其他应用，使用 `DragDrop.DoShellDragDrop`——它发起真正的 OLE 拖放：

```csharp
private void OnMouseDown(object sender, MouseButtonEventArgs e)
{
    if (e.LeftButton != MouseButtonState.Pressed) return;

    var data = new DataObject();
    data.SetData(DataFormats.FileDrop, new[] { @"C:\path\to\file.txt" }); // 拖出文件
    // data.SetText("some text");                                          // 或纯文本

    // 可选：Shell 系统拖拽图像（像素热点，相对图像左上角）
    ImageSource image = /* BitmapImage 等 */;
    var effect = DragDrop.DoShellDragDrop(sourceElement, data, DragDropEffects.Copy, image, new Point(16, 24));
    // effect == Copy 表示已放置到目标（如文件被复制到某文件夹）；None 表示取消
}
```

实现要点与限制：

- 内部实现（`OleDragSource.cs`）用手搓的 AOT COM 桥构建真 `IDataObject`（承载 `CF_UNICODETEXT` / `CF_HDROP`）、`IDropSource` 与 `IEnumFORMATETC`，全部用托管 vtable + `[UnmanagedCallersOnly]` 回调，无运行时 COM interop。
- 拖拽图像交给 Shell `IDragSourceHelper::InitializeFromBitmap`（像素预乘的 32bpp 顶向下 DIB），由 Windows 渲染并跨进程可见。
- 拖出文件时，`DataFormats.FileDrop` 需为**磁盘上真实存在**的文件路径。
- `DoShellDragDrop` 会阻塞运行 OS 拖放循环，必须在 UI（已 `OleInitialize` 的 STA）线程、通常从鼠标事件中调用。放回本应用时仍会经窗口的 OLE 拖放目标触发常规拖放事件。
- 在无 Shell 源的平台（非 Windows），自动回退到应用内托管拖拽。

## 6. 拖放目标：提权（管理员）进程接收拖入

当应用以管理员身份运行（高完整性级别）时，UIPI 会拦截资源管理器（中完整性）搬运拖放数据所用的窗口消息，导致拖入静默失败。框架在注册拖放目标（`OleDropTarget.RegisterWindow`）成功后，会自动对目标窗口放行这三条消息：

| 消息 | 值 | 作用 |
|------|----|------|
| `WM_DROPFILES` | `0x0233` | 传统 HDROP 投放 |
| `WM_COPYDATA` | `0x004A` | 数据搬运 |
| `WM_COPYGLOBALDATA` | `0x0049` | 承载 HGLOBAL 载荷（关键） |

采用按窗口的 `ChangeWindowMessageFilterEx(hwnd, msg, MSGFLT_ALLOW)`。非提权进程本就无需放行，此调用为无害空操作；任何失败都被忽略（该能力仅在提权时才有意义）。无需调用方做任何事——常规注册的窗口自动获得该能力。

> 注意：这只影响**接收**（拖入）。解析文件仍走既有的 `IDataObject::GetData(CF_HDROP)` + `DragQueryFileW`，与是否提权无关。

## 7. 与 WPF 参考实现的差异

参考实现（WinCraft `ShellDragSource`/`ShellDropTarget`）需要调用方自行包装 `IDataObject` 桥接与 `IDropSource`/`IDragSourceHelper`。Jalium.UI 把这些都收进框架内部：

- 应用内源端拖拽图像 → 走框架 `OverlayLayer`，无需 HBITMAP/`IDragSourceHelper`；
- 跨进程源端（`DoShellDragDrop`）→ 框架内部构建真 OLE `IDataObject`/`IDropSource` + `IDragSourceHelper`，调用方只需传 `DataObject` 与可选图像；
- 目标端 → 复用既有真 OLE `IDropTarget`，仅追加 `IDropTargetHelper`、`DropDescription` 与提权消息放行；

因此无需额外的 `ShellDataObject` 包装即可使用。
