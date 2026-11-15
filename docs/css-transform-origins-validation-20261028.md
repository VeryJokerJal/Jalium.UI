# CSS 与原生变换原点增量验证（R16）

历史快照说明：本文保留 R16 的计数与当时开放项。末节的 Motion Path 内容锚点缺陷已由后续 [R17 运动路径参考盒验证](css-motion-reference-boxes-validation-20261028.md) 接续处理；本页的“尚未修复”仅表示 R16 当时状态。

本轮接续 [R15 的默认原点命中缺陷](css-ruby-box-lines-validation-20261028.md)，统一绘制、坐标转换与逆向命中的原点，并补齐来源切换和 CSS 过渡的恢复路径。完整 CSS、T4、完整 Ruby、G0／E2 与跨平台验收仍未完成。

## 行为与实现边界

- 原生 `RenderTransform` 保留框架原点默认值 `(0, 0)`；绘制、二维矩阵和 CSS／3D 命中读取同一有效值，移除命中路径中无条件补中心的分支。
- 有效变换来自 CSS 基础层、状态层、关键帧或 CSS 过渡，并且没有作者原点贡献时，使用 CSS 布局盒的初始中心。CSS `transform:none` 声明仍有 CSS 来源；移除整项声明／样式才恢复原生默认语义。此互操作规则不把框架所有原生默认值改成 CSS 默认值。
- 计算有效原点时尊重本地值、`SetCurrentValue`、绑定、样式及原生／CSS 原点动画，不写入本地原点。即使变换对象完全相同，仅有效来源改变，也发出原点变化通知并安排绘制；重复的同值动画帧不重复报告变化。
- CSS 布局盒的 `content-box`／`fill-box` 中心包含真实 padding 和 border 的偏移；`border-box`／`stroke-box`／`view-box` 使用边框盒。内容盒跟随尺寸、内边距、边框及百分比内边距的包含块宽度变化。此处不声明完成所有 SVG 几何盒和表格包装盒语义。
- CSS 过渡复用原生动画值存储，但保留 CSS 来源。完成时解除动画层，取消或移出 `transition-property` 时丢弃中间帧，恢复当下基础值，不把中间帧提升到作者层；显式原生动画的 HoldEnd 行为继续保留。

规范依据：[CSS Transforms 1 §5](https://www.w3.org/TR/css-transforms-1/#transform-origin-property)、[参考盒 §6](https://www.w3.org/TR/css-transforms-1/#transform-box)、[CSS Transitions 1 §3](https://www.w3.org/TR/css-transitions-1/#starting)。入口为 `DependencyObject.cs`、`UIElement.cs`、`CssTransform3D.cs`、`CssTransformReferenceBox.cs` 与 `CssTransitions.cs`，复用现有属性、动画、布局和绘制消费者。

## 验证口径

源码冻结在 `artifacts/css-transform-origins-20261028-fef6969a/source`，起始 Git 可见输入 4,581 个。工作区含大量已有修改，增量补丁只包含本轮登记的文件，不能当作完整 CSS 源码基线。

正式新增测试覆盖原生 Ruby 的五种方向、三种合并策略、LTR／RTL、缩小／放大，普通 TextBlock／Border 的默认及显式原点、四种仿射变换、非零位置、嵌套、奇异矩阵、3D、绑定、同一对象来源切换、关键帧、真实时钟过渡及取消恢复。像素参考使用没有 CSS 变换的普通控件，矩阵系数、物理偏移和原点来自独立常量，不读取实际控件几何；参考与实际采用相同底层偏移／变换栈精度，避免合并矩阵造成边缘浮点量化差异，不放宽像素比较。

修复前首批 117 项中 75 项失败；内容盒扩展的 136 项中 7 项失败。最终测试程序集换回过渡取消修复前运行库，五项隔离对照中三项取消场景失败，自然结束和原生接管两个控制项通过。早期把 CSS `transform:none` 仍读取中心误判成残留值，已按声明来源语义修正预期，不将该项记为产品缺陷。

最终候选定向回归 **2,474／2,474** 通过，0 失败／跳过／超时；补强自然结束检查后的几何、3D、动画边界 **272／272** 通过。新增正式用例 **360 项**，其中几何与来源恢复 144 项、三种渲染组合像素 216 项；来源往返用例各保存三组状态，合计新增 240 对图像。

macOS 完整聚合回归 **23,060／23,060** 通过，0 失败、跳过、超时，用时 1,393.1 秒（约 23 分 13 秒），新增 360 项全部执行。前轮 22,700 项的精确用例名及重复次数全部保留，见 `final-full/summary.json`、`legacy-case-retention.json`。

严格像素 **1,674 对／3,348 张 PNG** 的解码 RGBA 全部相同，每种渲染组合 558 对，其中本轮 80 对。既有 1,434 张实际帧与 R15 解码 RGBA 相同，24 张旧联系图 PNG 摘要相同；已逐张查看六张新增联系图。原始 400×320 图像保留完整位置，联系图仅为形状检查居中裁剪。见 `focused-pixel-identity.json`、`contact-visual-review.json`；全量版本另见 `final-pixel-identity.json` 与 `final-contact-view-identity.json`。

与 R15 相同精确用例名的时长比较中，没有出现增量至少 1 秒且达到八倍的新增异常。共享名称按重复记录中位数统计，合计约 1,416.0→1,347.8 秒，本轮新增约 40.3 秒；这不是受控性能基准，也不说明应用整体变快。构建无错误，没有新增警告类型。见 `duration-comparison.json`、`build-diagnostic-comparison.json`。

原生 CTest 选定 **65／65** 通过，注册 117 项、平台／媒体模式排除 52 项。复用实际 R11 原生构建，与 R15 及实际构建冻结源核对的 822 个 native／Apple 输入相同；未重新编译原生库。见 `native-repeat/` 与 `native-reuse-input-manifest.json`。

## 原生验收

中文原生 Metal 页使用三个并列卡片比较原生、CSS 和同一对象切换来源，初始为 75% 缩放且没有显式原点。复用既有蓝白验收页视觉体系、44px 按钮与原生标题栏，最终实例实际查看 50 张画面，记录 190 个事件／189 个快照，六个原始 Ruby TextBlock、三个普通文字标记的九条原父子关系全部保留。

- 五种书写方向均实际拖选两个注音字符，再用键盘全选六个字符；CSS 默认中心、同一对象改为原生来源、75%／150% 缩放和窄窗下也逐项检查实际选择。切换来源的三次事件均记录宿主和普通盒使用同一变换对象。
- 不对称内容盒得到 `(70/160, 44/100)`，原生来源继续 `(0,0)`；显式左上／中心及默认值往返后恢复。六对内容区域的解码 RGBA 精确相同。裁剪为 `(25,130)-(1015,550)`，覆盖全部文字与变换盒；较大区域底部含自动化指针光晕，不能用来比较应用绘制。
- 真实 CSS 过渡记录 90 帧，比例从约 0.753 到 1，CSS 宿主原点始终 `(0.5,0.5)` 且本地原点仍未设置；结束后原文可再次拖选。Tab 移到缩放按钮时出现绿色焦点环，空格实际把 75% 改为 100%。客户端检查 1040×740 和 860×680，截图含标题栏分别为 1040×768、860×708。
- 最终 app 的八个框架程序集与测试载荷 SHA 相同，八个原生动态库的 UUID 和全部文件后备节一致，使用自包含 .NET 运行库并验证签名。四个实际进程均已关闭，无残留进程、fatal 或 error 日志。使用旧闭包导致截图重新打开旧实例的数字前缀证据不计为最终视觉验收，只使用 `verified3-*`；字形左侧边界外的一次拖选尝试也不计为通过，移到实际字框内重试后成功。

R15 的原生缩放默认原点命中缺陷在本轮上述实际路径中已修复。见 `live-ui-summary.json`、`native-ui-review.json`、`live-packaging.json` 和 `live-output/verified3-*`。AX、键盘与截图检查不代替 VoiceOver 朗读；没有宣称检查全部窄窗与放大组合，也未验证 Windows／Linux 实机。

## 源码与交接

本轮增量包含 11 个路径：五个产品源文件、两个测试文件、四个文档文件。`implementation-delta.patch` 相对本轮起始字节生成，使用独立临时索引检查并重放；不修改用户的 HEAD、索引和暂存内容，不包含工作区原先全部 CSS 成果。源码与补丁验证见 `source-audit.json`、`patch-verification.json`、`final-validation-summary.json`，证据索引为本轮 artifact 目录的 `README.md`。团队集成时仍需按实施计划交接完整源码基线。

## 后续工作

下一优先项为 **Motion Path 锚点的参考盒**。额外四项诊断使用公开 `UpdateLayout()` 等待样式与布局稳定，再将同一诊断程序集分别装入 R16／R15 的八个精确框架程序集：两侧均有两项失败、两个控制项通过。160×100 盒的内容中心为 `(70,44)`，路径采样位置为 `(200,0)`；`offset-anchor:auto` 和 `offset-anchor:50% 50%` 的平移仍为 `(120,-50)`，正确参考盒预期为 `(130,-44)`。显式 `transform-origin:50% 50%` 与 border-box 控制项通过。这两项 **尚未修复**，不包含在正式 360 项及全量通过计数中。见 `motion-anchor-probe/verified-comparison.json`；未等待样式稳定或没有重新核对载荷的中间诊断不作最终依据。

后续应将上述四项转为正式回归，复用 `CssTransformReferenceBox` 统一 `CssMotionPath.ResolveAnchorOffset` 的 auto／百分比来源，覆盖变换与路径组合、来源切换、内容盒变化、3D、逆向命中、像素及原生动画；依据 [Motion Path 1 §2.4](https://www.w3.org/TR/motion-1/#offset-anchor-property)。本轮隐式变换原点的修复不代表 offset 属性的全部参考盒语义完成。

完整 Ruby 盒模型、全部基线、匿名／空白规则、复杂 leading／overhang、intrinsic／百分比组合、文档／编辑器与 Windows／Linux 实机保持开放。支持矩阵与规范映射继续由 G0／E2 收敛；本轮局部通过不能替代完整 WPT、VoiceOver 朗读或跨平台验收。
