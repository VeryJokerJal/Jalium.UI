# Jalium.UI vs WPF API 差异报告

以 `wpf-main` 的 **ref 存根**（`WindowsBase` / `PresentationCore` / `PresentationFramework` / `System.Xaml`，共 **1931 个公开类型**）为权威基准，对 Jalium.UI 做逐类型、逐成员、逐重载、逐参数的成员级差异分析。

## 方法

1. 解析 4 个 WPF ref 存根 → 结构化权威签名（每个 public/protected 成员的完整签名、可访问性、重载）。
2. 索引 Jalium 全部托管源码（3101 个类型名 → 文件/命名空间）。
3. 自动名称级匹配（去泛型元数）→ 匹配 / 命名空间错位 / 完全缺失 三类。
4. 按优先级分 180 个任务包并行成员级对比（每包内嵌 WPF 权威存根，agent 读 Jalium 源码逐成员/重载/参数比对）。
5. 对 11 个关键类型（UIElement、FrameworkElement、DependencyProperty、Binding、Point、Storyboard、Timeline 等）做跨源码**对抗式验证**，全部 CONFIRMED，无假阳性。

## 汇总（覆盖 1901 个 WPF 顶层类型 / 180 个任务包，成员级）

| 类别 | 数量 |
|---|---|
| 缺失类型 | 602 |
| 缺失构造函数（逐重载） | 225 |
| 缺失属性（含 DependencyProperty） | 1604 |
| 缺失方法（逐重载） | 3155 |
| 缺失事件 | 379 |
| 缺失字段（含静态 DP/RoutedEvent） | 1065 |
| 缺失枚举值 | 128 |
| 参数/签名/可访问性不一致 | 1204 |
| 命名空间错位（类型存在但位置不同） | 291 |

## 文件

| 文件 | 内容 |
|---|---|
| `REPORT_FINAL.md` | 完整报告：全部 8 类差异表格（8600+ 行） |
| `APPENDIX_missing_api.md` | 351 个 Tier1/Tier2 缺失类型的**完整待补公开 API** 清单（逐 ctor/属性/方法/事件/字段） |
| `csv/missing_types.csv` | 缺失类型（命名空间/种类/基类/成员数） |
| `csv/missing_ctors.csv` | 缺失构造函数（逐重载完整签名） |
| `csv/missing_properties.csv` | 缺失属性（含 DP，逐条 WPF 签名） |
| `csv/missing_methods.csv` | 缺失方法（逐重载） |
| `csv/missing_events.csv` | 缺失事件 |
| `csv/missing_fields.csv` | 缺失字段 |
| `csv/missing_enum_values.csv` | 缺失枚举值 |
| `csv/inconsistencies.csv` | 参数/返回/可访问性不一致（Jalium 签名 vs WPF 签名 vs 差异类型） |
| `csv/ns_mismatch.csv` | 命名空间错位 |

CSV 为 UTF-8-sig，可直接导入生成 API 补齐任务清单。
