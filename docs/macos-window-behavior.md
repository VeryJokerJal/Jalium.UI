# macOS Window 行为与验收

本阶段补齐 AppKit 窗口与托管 `Window` 的状态、尺寸、焦点、关闭和输入衔接。
Gallery 保留现有视觉风格。本文区分实现、自动检查和实际界面验收；它不表示所有
macOS 平台能力已经完成。

2026-10-07 完成 v58 桌面验收后，按用户要求清理两个仓库中的 `artifacts` 输出；
v61 验收完成后再次清理，移除 6,788 个文件、约 6.10 GB 输出及 9 份本轮临时日志。
清理后全量扫描两个仓库，`artifacts` 目录数量为 0，相关源码和测试的清理前后哈希一致。
2026-10-08 v62 测试后再次清理，移除 2,702 个文件、约 2.33 GB 及 3 份临时日志。
同日 v63–v64 回归后再次清理，移除 3,027 个文件、约 2.65 GB 及 3 份本轮临时日志；
清理后两个仓库中的 `artifacts` 目录数量为 0，相关工作树文件与 Lab 源码哈希一致。
同日 v65–v66 验证后清理 3,001 个文件、约 2.65 GB 输出及 3 份本轮临时日志；
关闭本轮 Lab 进程，两个仓库的 `artifacts` 目录、本轮测试进程与临时日志均为 0，
清理前后相关工作树文件和 Lab 源码的哈希及文件集合一致。
同日 v67 验证后再次清理 2,771 个文件、约 2.38 GB 输出；本轮没有生成 Lab 临时
日志。清理后两个仓库的 `artifacts` 目录及本轮测试进程均为 0，相关源码哈希一致。
同日 v68 验证后清理 3,114 个文件、约 2.74 GB 输出及 3 份本轮临时日志；
关闭本轮 Lab，两个仓库的 `artifacts` 目录及本轮测试进程均为 0，相关工作树与
Lab 源码的清理前后哈希及文件集合一致。
同日 v69 验证后清理 86 个文件、约 10.49 MB 输出；未产生 Lab 临时日志。
清理后两个仓库的 `artifacts` 目录与本轮测试进程均为 0，相关工作树与 Lab 源码
的清理前后哈希及文件集合一致。
同日 v70 验证后清理 3,108 个文件、约 2.74 GB 输出及 4 份本轮临时日志；
关闭本轮 Lab，两个仓库的 `artifacts` 目录、本轮测试进程与临时日志均为 0，
清理前后相关工作树文件和 Lab 源码的哈希及文件集合一致。
同日 v71 验证后清理 3,122 个 artifacts 文件、约 2.79 GB 输出及 4 份本轮临时
日志；另清理 `.tools` 中早期 Window 验证留下的 155 份日志、结果文件和探针
二进制（784,575 字节），保留探针源码。两个仓库的 artifacts、本轮测试进程与
临时日志均为 0，相关工作树、Lab 与工具链文件的清理前后哈希及文件集合一致。
同日 v72 收尾清理 5,636 个 artifacts 文件、4,960,074,746 字节（约 4.96 GB），
以及 2 份本轮临时文件和 1 份夹具 SIGTRAP 崩溃诊断；两个仓库中不区分大小写的
artifacts 路径、本轮测试进程和临时文件均为 0，相关工作树、Lab 与工具链文件的
清理前后文件集合及哈希一致。
同日 v73 验证后清理 3,148 个 artifacts 文件、2,789,588,613 字节（约 2.79 GB），
以及 3 份本轮临时日志；两个仓库中不区分大小写的 artifacts 路径、本轮验证
进程与临时日志均为 0，清理前后工作树、Lab 源码与工具链的文件集合及哈希一致。
同日 v74 验证后清理 3,135 个 artifacts 文件、2,745,882,526 字节（约 2.75 GB），
以及 2 份本轮临时日志；两个仓库中不区分大小写的 artifacts 路径、本轮验证
进程与临时日志均为 0，清理前后工作树、Lab 源码与工具链的文件集合及哈希一致。
同日 v75 自动回归结束、桌面锁定阻止剩余验收后，停止自有 Lab，清理 3,134 个
artifacts 文件、2,747,588,811 字节（约 2.75 GB）及 3 份本轮临时日志。两个仓库中
不区分大小写的 artifacts 路径、本轮进程与临时日志均为 0，清理前后工作树、Lab
源码与工具链的文件集合及哈希一致。实际全屏输入、截图与键盘验收尚未完成，
解锁后需要重新构建继续检查。
同日 v76 目标暂停后收尾清理 143 个 artifacts 文件、7,152,162 字节；
本轮未产生临时日志，未发现带本轮产物路径的崩溃诊断。两个仓库中不区分大小写的
artifacts 路径和本轮进程均为 0，清理前后工作树、Lab 源码与工具链的文件集合及
哈希一致；保留已通过 32 项检查的源码与验收结论。
同日 v77–v79 验证后清理 6,764 个 artifacts 文件、6,103,094,851 字节，
以及 10 份本轮临时日志和 0 份含本轮产物路径的夹具崩溃诊断。
两个仓库中不区分大小写的 artifacts 路径、本轮测试进程与临时日志均为 0；
清理前后受保护工作树文件、Lab 源码与工具链的文件集合、模式和哈希一致。
同日 v80–v84 验证后清理 8,250 个 artifacts 文件、7,475,478,035 字节，
以及 17 份本轮临时日志/进程记录；未发现含本轮负载路径的崩溃诊断。
两个仓库中不区分大小写的 artifacts 路径、本轮进程与临时文件均为 0；
3,844 个受保护工作树、Lab 和工具链文件在清理前后的集合、模式和哈希一致。
最后的 v84 测试进程在锁屏期间未响应 SIGTERM；重新核对自有可执行路径后
以 SIGKILL 结束并确认退出 137，没有将这次收尾算作原生正常退出验收。
同日 v85–v86 验证后，两个窗口通过关闭按钮结束，Lab 正常退出 0；清理 1,939 个
artifacts 文件、1,680,853,366 字节（约 1.68 GB）及 3 份本轮临时文件，未发现含
本轮产物路径的崩溃诊断。两个仓库中不区分大小写的 artifacts 路径、本轮测试
进程与临时文件均为 0；3,844 个受保护工作树、Lab 和工具链文件在清理前后的
集合、模式与哈希一致。保留本节已记录的失败、自动通过和实际桌面验收范围。
同日 v87 验证及筛选核对后，两种标题栏均通过实际关闭按钮结束，两个 PID 正常
退出 0；清理 1,940 个 artifacts 文件、1,682,966,176 字节（约 1.68 GB）、
3 份本轮临时文件及 0 份含本轮产物路径的崩溃诊断。两个仓库中不区分大小写
的 artifacts 路径、本轮测试进程与临时文件均为 0；3,847 个受保护工作树、Lab
和工具链文件在清理前后的集合、模式与哈希一致。仅在核对完成后追加本清理记录。
同日 v88–v94 前台与相关自动回归完成后，清理 408 个 artifacts 文件、
228,387,662 字节（约 0.23 GB）、7 份本轮临时日志及 0 份含本轮产物路径的
崩溃诊断。两个仓库中不区分大小写的 artifacts 路径、本轮验证进程和临时日志
均为 0；3,850 个受保护工作树、Lab、包装脚本和工具链文件在清理前后的集合、
模式与哈希一致，五份原生源码仍匹配 v94 已验证构建；之后仅追加本清理记录。
同日 v95–v98 回归和实际 ⌘Q 验收完成后，清理 5,041 个 artifacts 文件（含符号
链接）、4,519,628,994 字节（约 4.52 GB）及 6 份本轮临时日志；未发现含本轮
负载路径的崩溃诊断。两个仓库中不区分大小写的 artifacts 路径、本轮临时文件和
验证进程均为 0；3,949 个受保护工作树、Lab、包装脚本和工具链文件在清理前后的
集合、模式与哈希一致，12 份源码仍匹配已验证构建；之后仅追加本清理记录。
2026-10-09 v99–v113 回归及实际 Lab/Gallery 验收完成后，清理 15,410 个 artifacts
文件（含符号链接）、13,865,192,995 字节（约 13.87 GB）、10 份本轮临时日志
及 23 份匹配本轮应用名/路径或已核对进程与 Mach-O UUID 的崩溃诊断。两个仓库
中不区分大小写的 artifacts 路径、本轮进程与临时日志均为 0；
18,055 个受保护工作树、完整 `.tools`、Lab、包装脚本与工具链文件在清理前后的
集合、模式及哈希一致，五份最终构建源码清单仍全部匹配；之后仅追加本清理记录。
同日 v114–v116 菜单回归与实际窗口验收完成后，清理 4,941 个 artifacts 文件
（含符号链接）、4,437,650,602 字节（约 4.44 GB）、4 份本轮临时日志及 11 份
已按进程、时间、bundle ID 和 Mach-O UUID 核对的冷启动崩溃诊断。两个仓库中
不区分大小写的 artifacts 路径、本轮验证进程与临时文件均为 0；18,060 个受保护
工作树和完整 `.tools` 文件在清理前后的集合、模式与哈希一致，四份最终构建源码
清单全部匹配；清理后仅更新本文的验收记录。
同日 v117 EditControl 无障碍回归与两种标题栏实际验证完成后，清理 4,835 个
artifacts 文件（含符号链接）、4,218,088,872 字节（约 4.22 GB）及 1 份本轮
临时日志；未找到可归属本轮的崩溃诊断。两个仓库中不区分大小写的 artifacts
路径、本轮验证进程和临时日志均为 0；18,064 个受保护工作树和完整 `.tools`
文件的集合、模式与哈希在清理前后一致，四份最终构建源码清单全部匹配；
清理后仅追加本清理记录。
本文中的旧应用、冻结负载、JSON、日志和截图路径仅说明当时的验证来源，不再是
当前可打开的交付文件；保留源码、测试代码和下述验收结论。清理后重新运行需要
重新构建原生与托管负载，不能以历史结果代替新负载的验证。

同日 v118 文本导航自动回归及原生 TextBox 已完成界面检查后，按用户要求清理
6,173 个 artifacts 文件/链接、5,378,445,123 逻辑字节（约 5.38 GB），
以及 1 份本轮临时日志；本轮匹配的崩溃报告 0 份。清理前后 **18,069** 个
工作树和完整 .tools 文件/链接的集合、内容、模式一致；两个仓库字面 artifacts 路径
与本轮进程均为 **0**。Mac 锁定后的富文本、自定义标题栏及最小尺寸验证仍待
手动解锁并重新构建；原生窗口按清理路径退出 137，未称为实际 ⌘Q 通过。

同日 v119 祖先裁剪自动回归及两种标题栏实际界面验收完成后，清理 **4,497** 个
artifacts 文件/链接、**3,751,071,922** 逻辑字节（约 **3.75 GB**），以及 **1** 份本轮
临时日志；匹配本轮应用路径或 bundle ID 的崩溃诊断 **0** 份。清理前后 **18,072** 个
受保护工作树和完整 `.tools` 文件/链接的集合、模式与内容哈希一致；两个仓库不区分
大小写的字面 artifacts 路径与本轮验证进程均为 **0**。两个最终桌面进程通过实际
⌘Q 退出，CLI 均为 **0**；之后只追加本文的清理记录，不保留历史构建包和测试日志。

同日 v120 文本样式、RTF、字素按键与两种标题栏实际验收完成后，清理 **4,464** 个
artifacts 文件/链接、**3,710,423,821** 逻辑字节（约 **3.71 GB**），以及 **1** 份本轮
临时日志；按本轮包路径或 bundle ID 匹配的崩溃诊断 **0** 份。清理前后 **18,079** 个
受保护工作树和完整 `.tools` 文件/链接的集合、模式与内容哈希一致；两个仓库不区分
大小写的字面 artifacts 路径、本轮验证进程和本轮临时日志均为 **0**。两个最终桌面
PID **4625/4794** 通过实际 ⌘Q 退出，原 CLI 均为 **0**；四份最终源码清单在删除前
仍匹配。验收结论留在本文，源码与回归测试保留，应用包、冻结负载和日志不保留。

同日 v121 主字体匹配回归及两种标题栏实际验收完成后，清理 **2,879** 个 artifacts
文件/链接、**2,383,261,950** 逻辑字节（约 **2.38 GB**）及 **1** 份本轮临时日志；
匹配本轮包路径或 bundle ID 的崩溃诊断 **0** 份。清理前后 **18,084** 个受保护
工作树和完整 `.tools` 文件/链接的集合、模式与内容哈希一致；两个仓库不区分大小写
的字面 artifacts 路径、本轮验证进程与临时日志均为 **0**。PID **8886/9197** 分别
通过实际 ⌘Q，原 CLI 均退出 **0**；四份实际依赖源码清单在删除前匹配已验证构建。
源码、测试、打包器修复和验收结论保留；清理后仅追加本文的本段记录。

2026-10-09 v122 合成斜体、RTF、富文本选区回归及两种标题栏实际验收完成后，清理
**2,918** 个 artifacts 文件/链接、**2,419,517,989** 逻辑字节（约 **2.42 GB**），以及
**1** 份本轮临时日志；匹配本轮路径或 bundle ID 的崩溃诊断 **0** 份。清理前后
**18,086** 个受保护工作树和完整 `.tools` 文件/链接的集合、模式与内容哈希一致。
两个仓库不区分大小写的字面 artifacts 路径、本轮验证进程、临时日志和诊断均为
**0**。实际 PID **14676/14999** 经 ⌘Q，原 CLI 均退出 **0**；删除前再次核对五份
源码清单、冻结原生负载与两包签名。源码、测试与验收结论保留，清理后仅追加本段。

2026-10-09 v123–v125 焦点修复及本轮检查收尾后，清理 **4,967** 个 artifacts 文件/链接、
**4,160,388,840** 逻辑字节（约 **4.16 GB**），以及 **7** 份本轮临时日志和 **2** 份
匹配本轮包路径或 bundle ID 的崩溃诊断。清理前后 **18,087** 个受保护工作树和完整
`.tools` 文件/链接及 **1,212** 个目录的集合、模式与内容 SHA-256 一致；两个仓库
不区分大小写的字面 artifacts 路径、本轮进程、临时日志和诊断均为 **0**。原生
标题栏 PID **24343** 经实际 ⌘Q、原 CLI **退出 0**；自定义 PID **24734** 的最后
验收被锁屏阻止，SIGTERM 未退出，收尾只终止本轮 PID，原 CLI **-9/SIGKILL**，
未记为正常退出或最终完整界面通过。源码、测试、观察工具与验收结论保留，目标继续
进行；清理后仅追加本段。

2026-10-09 v126–v127 验证和清理完成：移除 **9,314** 个 artifacts 文件/链接、
**5,562,811,610** 逻辑字节（约 **5.56 GB**），以及 **5** 份本轮临时日志和 **4** 份
修复前基线产生的崩溃诊断。诊断路径被 macOS 脱敏，使用准确的测试程序名、启动时间、
基线日志时间段及两份夹具 UUID 归属核对；其他诊断保留。清理前后 **18,087** 个
现存工作树/完整 `.tools` 文件与链接的模式和 SHA-256、**14** 个既有缺失路径的
缺失状态，以及 **1,540** 个目录的集合和模式均一致。首轮保护检查遇到 Gallery
已删除的受版本控制文件，在删除输出前停止；将既有删除状态纳入检查后才完成清理。
两个仓库不区分大小写的字面 artifacts 路径、本轮验证进程、临时日志和诊断均为
**0**。删除前七份 **2,814** 文件源码清单、**302** 份原生源码及两轮冻结负载仍匹配；
最终两个桌面 PID **41371/42641** 经实际 ⌘Q、原 CLI 均 **退出 0**。源码、回归测试、
工具链和本文验收结论保留，清理后仅追加本段。

2026-10-09 v128 验证和清理完成：移除两个 artifacts 根下 **3,190** 个文件/链接，
**2,620,366,231** 逻辑字节（约 **2.62 GB**），以及 **5** 份本轮临时日志和 **1**
份可按进程名、PID、启动时间及基线区间归属的崩溃诊断。v75 固定路径的可见性日志
本轮运行前不存在，其全部记录时间在 v128 桌面进程运行区间内；其他临时日志保留。
清理前后 **17,875** 个现存保护文件/链接的模式、链接目标或 SHA-256、**14** 个
既有缺失路径的状态，以及完整 `.tools`/文档的 **1,214** 个目录集合和模式均一致。
**2,814** 份源码/配置清单及冻结负载在删除前匹配，删除后源码仍匹配。首次清理
预检因 Python 不接受四位小数秒的时间戳而在删除前停止；规范化微秒格式后完成。
两个仓库不区分大小写的字面 artifacts 路径、本轮应用进程、已归属临时日志和诊断
均为 **0**。四份已验收应用已删除；源码、测试、工具链和验收结论保留，清理后
仅追加本段，完整 macOS 目标保持进行中。

2026-10-09 v130 最终回归、两种标题栏键盘与尺寸验收后清理 **12,510** 个 artifacts
文件/链接、**8,464,055,075** 逻辑字节（约 8.46 GB），以及 **5** 份本轮 Lab 临时
日志。清理前后 **18,093** 个受保护工作树和完整 `.tools` 文件/链接的模式、内容哈希
或链接目标一致；**14** 个既有缺失路径和 **1,544** 个目录状态一致。删除前核对
**3,194** 个独立 PR 源码文件、**301** 个原生源码/配置指纹及 **11** 份冻结文件；
删除后四个实现/夹具覆盖文件和原生源码仍匹配。两个仓库不区分大小写的字面
artifacts 路径及本轮临时日志均为 **0**，最终两个 Lab 进程已通过实际 ⌘Q 退出 0。
源代码、测试、工具链、并行 CSS/ToolTip 工作与本节结论保留；清理后仅追加本文记录。

2026-10-09 v131–v135 外部 AX 通知验收后，清理 **23,229** 个 artifacts 文件/链接、
**10,562,648,739** 逻辑字节（约 **10.56 GB**），以及 **15** 份本轮版本号匹配的
临时日志和 **1** 份基线能力探测诊断。诊断通过测试程序名、PID **9102**、启动及
异常时间归属核对，其他历史诊断保留。清理前后 **18,094** 个受保护文件/链接的
模式、内容哈希或链接目标一致，**14** 个既有缺失路径、**1,540** 个目录状态一致。
删除前核对四份导出的 **12,779** 个源码文件、五轮 **55** 份冻结负载及真实完成
标记；最终 **511** 个原生源码/配置指纹和四个覆盖文件在删除前后仍匹配。
两个仓库不区分大小写的字面 artifacts 路径、本轮应用/观察器、已归属日志和诊断
均为 **0**。最后 Lab PID **13128** 经实际 ⌘Q **退出 0**；最终窗口 AX 宿主 PID
**13888** 完成 **14/14**、退出 0。源码、完整 `.tools`、并行 CSS/ToolTip 工作和
既有删除状态保留。本文在清理保护核对后更新；历史应用路径不作为当前交付文件。

2026-10-09 v136 合并现有 PR 的文件过滤与屏幕坐标改动后，再清理 **6,290** 个
artifacts 文件/链接、**2,958,439,919** 逻辑字节（约 **2.96 GB**），以及 **4** 份
本轮版本号匹配的临时日志；本轮没有新增可归属的崩溃诊断。清理前后 **18,098**
个受保护文件/链接的模式、SHA-256 或链接目标一致，**14** 个既有缺失路径和
**1,540** 个目录状态一致。删除前核对独立 Git 导出的 **3,199** 个源码文件、
**11** 份冻结负载及真实完成标记；**513** 个原生源码/配置指纹在删除前后仍匹配。
两仓库不区分大小写的字面 artifacts 路径、本轮应用/观察器和已归属临时日志均为
**0**。最后 Lab PID **18709** 经实际 ⌘Q **退出 0**，外部观察器 PID **18718**
正常停止、退出 0，窗口 AX 宿主 PID **19405** 完成 **14/14**、退出 0。
源码、完整 `.tools`、并行 CSS/ToolTip 工作及 Gallery 既有删除状态保留；本文在
清理保护核对后追加。v131–v136 两次清理共移除 **29,519** 个文件/链接、
**13,521,088,658** 逻辑字节、**19** 份临时日志和 **1** 份已归属的基线诊断。

2026-10-10 v137–v138 两轮重开验收后，清理 **11,894** 个 artifacts 文件/链接、
**5,586,443,451** 逻辑字节（约 **5.59 GB**）。每轮清理前后 **18,099** 个受保护
文件/链接的模式、SHA-256 或链接目标一致；**14** 个既有缺失路径、**1,538** 个
目录状态及完整 `.tools` 的 **1,212** 个目录集合一致。每轮均核对 **3,200** 个
独立源码文件、三份夹具覆盖、**11** 份冻结原生负载与真实完成标记，以及 **516**
个原生源码/配置指纹。两仓库不区分大小写的字面 artifacts 路径、本轮宿主与
caffeinate 进程、已归属临时日志最终均为 **0**。最终契约宿主 PID **28445**
完成 **22/22**，前台宿主 PID **28104** 完成 **8/8**，均退出 0。源码、工具链、
并行 CSS/ToolTip 工作与 Gallery 既有删除状态保留；本文在清理核对后追加。

2026-10-10 v139 回归结束后，清理 **6,477** 个 artifacts 文件/链接、
**3,238,654,584** 逻辑字节（约 **3.24 GB**）。清理前后 **18,101** 个受保护
文件/链接的内容、模式及链接目标、**14** 个既有缺失路径和相关目录集合一致；
完整 `.tools` 的 **1,212** 个目录保持一致。删除前核对 **3,202** 个独立源码
文件、四份夹具/产品覆盖、**12** 份冻结文件（含真实完成标记）及 **516** 个原生
源码/配置指纹。两仓库不区分大小写的字面 artifacts 路径和本轮宿主进程最终为
**0**；并行 CSS/ToolTip 修改、工具链与 Gallery 既有删除状态保留。本文在清理
核对后追加；该清理记录不表示未完成的跨应用切换已通过。

2026-10-10 v140 绑定与几何回归后，清理 **5,070** 个 artifacts 文件/链接、
**1,963,445,541** 逻辑字节（约 **1.96 GB**）。清理前后 **18,102** 个受保护
文件/链接的内容、模式和链接目标、**14** 个既有缺失路径及相关目录集合一致；
完整 `.tools` 的 **1,212** 个目录保持一致。删除前核对 **3,203** 个独立源码
文件、四份覆盖、**12** 份冻结文件及本轮 **515** 个原生源码/配置指纹。两仓库
不区分大小写的字面 artifacts 路径、本轮宿主和 caffeinate 进程最终为 **0**。
并行 CSS/ToolTip 修改、工具链与 Gallery 既有删除状态保留；本文在清理核对后更新。

2026-10-10 v141 输入边界验收结束后，清理两个仓库全部不区分大小写的字面
artifacts 路径及已归属的本轮诊断。清理前后的受保护文件与目录等值断言完成，
最终核对 **18,119** 个文件/链接、**14** 个既有缺失路径、**1,538** 个相关目录及
完整 `.tools` 的 **1,212** 个目录；内容、模式与链接目标保持一致。删除前核对
**3,206** 个独立源码文件、**10** 份代码覆盖、**12** 份冻结文件和 **515** 个原生
源码/配置指纹。清理脚本末尾的文本式进程检查触发断言，总删除数量/字节未写出；
随后按明确可执行列独立核对，本轮宿主与 keeper 进程均为 **0**，两仓库 artifacts
路径为 **0**。四份并行 CSS/ToolTip 修改的内容与模式、工具链和 Gallery 既有删除
状态保留。本文在清理保护检查后追加本记录。

2026-10-10 v142 初始定位回归与实际窗口检查完成后，清理 **4,829** 个 artifacts
文件/链接、**1,777,665,598** 逻辑字节（约 **1.78 GB**），以及 **3** 份自有 CTest
临时记录；匹配本轮路径或 bundle ID 的崩溃诊断为 **0**。两个仓库不区分大小写
的字面 artifacts 路径、本轮宿主与 keeper 进程均为 **0**。清理前后 **18,100** 个
受保护文件/链接、**14** 个既有缺失路径、**1,538** 个相关目录和完整 `.tools` 的
**1,212** 个目录保持一致；内容、模式与链接目标未变。删除前核对 **3,206** 个
独立源码文件、**24** 份修改前/最终冻结文件及 **505** 个原生源码/配置指纹；
删除后原生输入指纹仍一致。并行 CSS/ToolTip 修改、工具链和 Gallery 原有删除
状态保留。本段在保护与清理核对完成后追加。

## 行为范围

| 行为 | 实现与自动检查 | 界面验收范围 |
| --- | --- | --- |
| 显示、隐藏、激活 | `ShowActivated=false` 保留当前 key window；禁用窗口拒绝输入与激活；Show/Activate 回调中关闭或隐藏后停止后续显示操作；保留初始化、定位和原生显示回调中最新的应用状态请求 | v21 完整原生 Window 检查及真实 AppKit 生命周期 18 项通过；Lab 实际模态焦点与恢复已检查，隐藏重开保留 v14 历史证据 |
| 动态窗口可见性 | macOS 未显示的 Window 初始为 Collapsed；绑定、样式、SetValue、SetCurrentValue、清值和解绑同步原生可见性；首次属性显示通过调度创建；Show/Hide 保留绑定，隐藏保留句柄与内容，隐藏模态窗口结束模态循环 | v55 同一原生检查修复前 2/40、修复后 40/40；实际桌面隐藏恢复保留中文、emoji 和编辑焦点，清值及样式回退已实测 |
| 关闭后的失败请求 | 已关闭窗口继续拒绝 Visible 请求；强制回调拒绝写入时恢复原属性层及动画值，后续读取不会再次抛出；既有绑定可继续更新合法值 | v56 同一 16 项夹具由旧负载的 1/16 提升到 16/16；原生两种标题栏各检查三种写入的三次重复失败；桌面显示、关闭及再显示请求已操作，状态可读且无新句柄 |
| 最大化、还原 | AppKit Zoom 使用标准缩放尺寸；Fill 和托管最大化使用工作区；状态在尺寸事件前同步，保留普通窗口还原尺寸；首次显示的 AppKit 框架约束保留预先最大化状态与正常尺寸；还原回调中的原生 Zoom 排队后仍使用 AppKit 最佳尺寸，显式状态请求可以替代它，两个排队 Zoom 相互抵消 | v7 Zoom 及还原已实测；v10 系统 Fill、靠左、返回原尺寸与托管状态及 SizeToContent 混用已实测；v64 新增原生与托管重入回归通过，尚未实测新负载的物理 Zoom |
| RestoreBounds | 普通状态读取当前原生客户区几何；最大化、最小化、全屏及还原回调保留普通窗口几何；未显示或已关闭时返回 Empty | 原生及托管检查通过；v10 普通、自动尺寸、平铺、还原和关闭前后已实测 |
| 最小化、再激活 | 动画开始前保存普通尺寸，完成后重放最后状态与选择请求；Activate 和 AX Main 等待实际去最小化完成，Main 保留键盘选择；隐藏、禁用、新最小化或销毁取消旧选择，隐藏后的新激活仍可重开；从最大化最小化后恢复最大化 | v74 激活时序核心基线 **0/16**，扩展原生 **32/32**、托管最小化 **24/24**；两种标题栏、普通和最大化共 **4** 条激活路径均实际恢复焦点并继续输入，最终面板负载又复查 **2** 条。还原客户区保留 **800×760 DIP**；后台前台激活首轮失败与单组复查通过分开记录，未声明消除其不稳定根因 |
| AppKit 全屏 | 原生 Spaces 转换；完成或失败后保留更新状态与选择请求；按请求代次拒绝重复及失效回调；保存 RestoreBounds；恢复转换前已有的输入视图焦点，尊重隐藏、禁用及更晚原生输入目标 | v75 受控失败 **64/64**、焦点 **40/40**；v79 完整 Window AX **232/232**、托管 **135/135**。v78 和最终 v79 均实际完成两种标题栏的全屏进出、无需重点击编辑框的中文与 emoji 输入、**520×580 DIP** 最小布局和 Tab/Shift+Tab；原生 Control+Command+F 循环及还原尺寸通过。修复前失焦证据保留 |
| 自定义窗口框架 | 标题栏拖动；双击在释放时读取系统 Zoom/Fill/Minimize/No Action 偏好；八个边缘与角的缩放 | 较早拖动、右边缘、右上角和右下角缩放已实测；v28 当前 Zoom 偏好下的标题栏双击最大化/还原与 RestoreBounds 已实测，其余偏好待实测 |
| 调整尺寸中断 | 有界读取原生鼠标事件，空闲时继续等待按住的手势；隐藏、禁用、取消缩放能力、状态请求、最小化开始或销毁后终止旧目标的追踪 | v70 完整原生拖动组 138/138，保留七类中断、八个边角及固定锚点、静止按住和同一 view 连续输入；当前 Lab 实际连续缩放两次通过。失活时外部边缘按下未进入托管 view，激活后的工具路径与真实设备仍需分别验收 |
| 拖放与窗口销毁 | macOS 公共 DragDrop 源/目标入口；Preview/Bubble、反馈与查询；每次进入独立 token，回调后重验窗口和目标；隐藏、禁用、结束及旧会话清理；同进程保留原始 IDataObject，其他来源按 pasteboard 懒读并在 Drop 前保存全部格式；多文件、中文、HTML/RTF、CSV/Xaml、PNG/TIFF、音频及自定义格式；启动前安装查询监听，旧无查询入口保留 AppKit 终止事件；启动拒绝与异常释放资源并保留独立目标访问；结束及清理回调抛错后仍清理所属状态，并保留另一窗口的捕获 | v143 原生拖放 **152/152**、真实 SDK URI 路由 **12/12**、完整托管 **1406/1406**；新增 URI 实际内容分类、256 MiB 读取上限及数据访问重入检查。v85–v86 当轮原生拖放 **122/122**、相关原生组 **8/8**、托管相关组 **351 通过、2 跳过**。v84 两种标题栏实际拖入、取消返回 None 且没有 Drop、复读样例及取消后重试通过；实际自定义→原生跨窗口 Enter/Drop 与源 Move 已确认。反方向及跨应用、实际修饰键和物理 Escape 仍待验收。完整原生 v84 **9/10**，独立后台激活复查仍失败；v86 专项结果不替代这项缺口或实际界面验收 |
| 尺寸与布局 | 程序尺寸同步；原生尺寸回写不形成反馈；用户缩放及 AppKit 系统布局退出 SizeToContent；程序尺寸、约束、样式和 DPI 更新不误判为用户操作；交互调整尺寸逐次使用最新 Min/Max 约束；拖动期间程序修改位置、尺寸或标题栏后，从最新 frame 和上一次指针继续并保留固定锚点 | v70 新增 72 项几何回归，基线 0/72、修复后 72/72，完整拖动组 138/138；v69 动态约束 32 项保留。Lab 当前负载的程序尺寸与两次外部连续缩放通过；拖动中更新属性的物理输入仍待验收 |
| DPI 与位置 | 客户区物理像素与 DIP 转换；左上角屏幕坐标；DpiChanged 路由事件；更新尺寸约束；macOS PointToScreen/PointFromScreen 使用实际 flipped 客户区原点，排除原生标题栏装饰 | v71 两种标题栏、移动和运行时切换后的宿主往返检查通过；实际菜单锚点随原生标题栏偏移正确；混合 DPI 多屏待验收 |
| 初始定位 | macOS 首次显示使用 AppKit 点坐标和完整外框定位；CenterScreen 选择父窗口所在屏幕，否则选择鼠标屏幕；CenterOwner 按普通父窗口外框居中，非普通父窗口使用其屏幕工作区，无父窗口保留手动位置；限制到工作区；隐藏重开保留移动后的位置；初始化最大化保留已居中的还原外框 | v142 原生 **72/72**、真实 SDK **20/20**，按独立普通 AppKit 窗口校准像素取整后仍严格比较完整外框；实际 1× 单屏的两种奇数尺寸标题栏截图、焦点、字符输入和撤销已检查。v72 的最大化还原、程序移动和重开记录保留；v141 首轮失败不改写为通过。混合 DPI 多屏仍无硬件验收 |
| 运行时窗口样式 | 标题栏、可缩放和按钮能力保留客户区大小；透明属性同时同步 NSWindow 和现有渲染层；置顶级别可还原 | 原生属性断言通过；v55 透明窗口背景合成与编辑焦点已实测，更广样式组合仍待验收 |
| 样式切换的输入焦点 | AppKit 替换窗口框架后保留原有内容 firstResponder；未聚焦或禁用的窗口不抢焦点 | v61 原生两种初始标题栏的 10 次样式转换及 4 项负向检查通过；实际 Native/Custom 双向切换后无需重新点击即可继续输入 |
| 编辑框字形与行高 | CoreText 绘制框至少容纳首行自然行高，保留绘制原点、基线及调用方裁剪 | v60 复现 PingFang 16 自然行高 22.3999 DIP 被取整为 22 DIP 后完全没有字形；24 组配置、48 次 GPU 捕获与参考逐通道误差不超过 1；v60/v61 实际中文、emoji 和追加输入已检查 |
| Owner | 拒绝自身、循环所有权及可见模态窗口更换 Owner；显示、Visibility 与 Activate 重新绑定当前 parent；初始化回调的新显式 Owner 优先；owner 关闭后 owned windows 完成关闭 | v21 托管及真实宿主检查通过；独立窗口、内外层模态菜单状态与退出保留所有窗口已实际检查；v10 第二窗口输入与所有权关闭历史证据保留 |
| 模态会话 | DialogResult 触发关闭；Closing 取消后清空结果并保留模态循环；隐藏返回 False 且保留窗口；禁用应用内其他窗口，恢复先前状态与活动窗口 | v58 沿内层默认/取消关系实际外部 Press 分别返回 True/False，只恢复外层；两次返回后均可继续编辑，主窗口保持禁用直到外层结束。v57 隐藏返回 False、重开后外部 Press 返回 True 的实测保留 |
| 关闭与销毁 | Command+W 与原生关闭请求可取消；无边框窗口的 performClose 进入同一请求路径；禁用或没有关闭能力时不发出请求；外部 AppKit 销毁也释放托管句柄；Closed 只触发一次 | v7/v10 Command+W、v10 主窗口关闭及进程退出已实测；v14 无边框 Window 菜单 Close 已实测通过 |
| 回调关闭后新建窗口 | 原生操作跨回调保留原 NSWindow，并同时核对注册状态及对象身份；旧请求不能沿复用的内存地址继续改动新窗口 | v67 状态回调实际关闭并新建窗口，基线 1/4、修复后 4/4；三项基线失败和四项修复后独立用例均实际复用了平台窗口地址。v68 原生 Window AX 完整组 70/70、完整原生前台 Window 分组、托管非激活宿主 46/46 及独立前台组 2/2 通过；外部属性写入仍待验收 |
| 应用退出 | 空应用菜单补齐 Command+Q；已有应用菜单动作保留；先协商最深层 owned/modal 窗口；保留公开 ShutdownMode 及回调的新策略；等待正在执行的 Closing 作出决定；主应用类阻止递归 terminate；完成原生资源销毁后才答复，取消后保留先前接受窗口的延迟销毁保护 | v99 重入 Closing 专项由旧负载 0/8 到 8/8；最终 v113 实际 AppKit terminate **32/32**，只换回默认主应用类的对照 **20/32**；相关真实宿主合计 **79/79**。v110 两种标题栏实际关闭与 ⌘Q 取消、编辑和焦点检查后接受退出，原 PID 均退出 0；Gallery v111 实际 ⌘Q 退出 0 |
| Dock 重开 | 没有可用的显示窗口或全部最小化时恢复有效主窗口或其他窗口；排除禁用、关闭中及已关闭目标，回调关闭后尝试其他窗口；保留模态 owner 禁用状态；阻止嵌套与已停止宿主重开，自行处理后阻止 AppKit 默认流程 | v31 真实 AppKit 宿主 11/11，v95 同一分组复查 11/11；包括可见禁用 owner 上的最小化模态对话框。v96 Dock 工具读取超时，实际 Dock 点击及前台焦点仍待验收 |
| Window 菜单 | 宿主注册默认窗口菜单，复用已有菜单；平台显式维护无边框窗口的列表项；动作按窗口能力与禁用状态验证；跟踪期间的 Escape 优先取消原生菜单并消费该次重复/释放事件，保留编辑器组合文本 | v51 全屏菜单状态及退出已实测；v52 实际绘出的 Window 菜单弹出路径确认一次 Escape 仅关闭菜单、保留首次关闭保护及编辑焦点，后续 Escape 正常取消模态。菜单栏的物理键盘路径仍待验收；CUA 菜单栏 AX 路径不产生跟踪通知，不能作为该项证据 |
| 窗口上下文菜单 | SystemCommands.ShowSystemMenu 与自定义标题栏右键接入 AppKit；四项标准动作显式指向请求窗口；复制宿主标签和快捷键；隐藏、禁用、销毁时取消追踪，拒绝重入及迟到动作 | v71 原生 26/26、宿主 4/4；实际 Escape、取消及接受关闭、能力禁用、Zoom、全屏进出与最小化已操作。标题栏坐标右键被桌面工具拒绝，保留外部输入缺项 |
| 键盘焦点 | 每个 macOS Window 保存逻辑焦点；激活时恢复有效可见目标，恢复失败则清理旧窗口焦点；原生键盘与文本输入即时排除隐藏或退出中的目标，覆盖延迟重验焦点前的间隙；Tab/Shift+Tab 在窗口内循环；按下和释放均阻止输入进入其他窗口 | v33 托管回归覆盖其他窗口焦点的按下/释放拒绝；v28 常规及最小窗口六种模式、重复切换的双向 Tab、嵌套模态焦点恢复、Gallery 编辑与 Command-K 保留实际证据 |
| 原生文本导航与修饰键 | 原生事件携带独立 PhysicalModifiers；Command 兼容现有 Control 手势，Control+Command 保留额外修饰位；三类编辑器支持 Command 行/文档边界、Option 按词移动/删除、Shift 扩展选区及物理 Control 编辑键；密码框采用安全的字段边界；视觉行边界与按词方向使用 CoreText 排版，鼠标采用 AppKit 双击词范围；保留光标亲和性与声明的富文档段落方向 | v50 托管 802/802、AppKit 宿主 142/142，富文本/逻辑树 56/56；富文本范围替换、跨段删除/合并、嵌套内联换段、多行粘贴、撤销/重做保留节点、格式、绑定与选区归属；实际多行粘贴与撤销/重做由外部 AX 确认；默认 100% 字宽下的静态族和私有集合匹配及缺失字重方向修复，字重、有序回退、按字形加载与阻塞、FontStretch、CSS 精确字宽、光标及 ch 缓存回归通过；42,100 次 NSTextView 对照及 400 次线程查询，1×/2× 本进程 GPU 与参考一致，32 组字宽/倾斜/变换段落像素与独立 CTLineDraw 一致；完整 CSS 字体匹配、合成控制、内联方向、语言及物理键盘等仍待补齐或验收 |
| AppKit 编辑菜单动作与可用状态 | Copy、Cut、Paste、Select All、Undo 与 Redo 发送 Command 标志及按下/释放事件；查询当前有效控件的选区、只读、撤销历史和剪贴板状态；密码框不提供 Copy/Cut；自定义控件复用标准路由命令；禁用、隐藏、最小化或销毁窗口拒绝菜单查询 | v114–v116 真实 NSMenu 新专项由 0/14 到 14/14，最终相关宿主合计 72/72；两种标题栏的实际菜单 Undo/Redo、只读与密码框状态、按钮焦点及最小尺寸布局已检查。菜单栏 AX 命令执行与实际绘制弹出菜单分别记录，物理键盘菜单栏路径仍待验收 |
| Gallery 快捷键提示 | 搜索、编辑、Markdown、菜单及命令示例按平台显示快捷键；四种语言共用命名标记，语言切换保持同一资源字典；文本导航提示采用 Option 与 Command 边界组合；显式 DisplayString 与 KeyGesture 解析、序列化及匹配规则保持不变 | v35 重建并执行相关回归 22/22，已签名 Gallery 已准备；实际符号渲染、语言切换和键盘菜单交互仍待解锁验收 |
| 按键与鼠标按钮 | 标点使用 OEM 虚拟键；移除 Shift 后按当前布局翻译；F1–F24、数字小键盘、左右修饰键及中键/侧键保留身份 | 原生事件检查通过；真实布局、功能键和侧键设备待验收 |
| 滚动时鼠标按钮状态 | AppKit 每次滚动及惯性事件携带当前五个按钮的系统快照，托管预览、冒泡、指针提升与全局 Mouse 状态保持一致；旧事件没有快照时保留已知按钮状态 | v141 新增轮询/解码/路由回归通过；两种标题栏工具定向滚动已检查，真实设备按住按钮滚动仍待验收 |
| 焦点轮廓与滚动条留白 | 轮廓跟随滚动并应用每一层祖先对子内容的裁剪，排除普通滚动条和双侧留白；覆盖式滚动条保留完整视口，滚动条自身的焦点轮廓使用外框裁剪 | v141 内容视口、滚动前后、双侧留白、RTL、覆盖模式及滚动条自身回归通过；两种标题栏的截图、Tab/Shift+Tab、离开视口和滚动返回已检查 |
| 输入法 | 焦点、只读状态、光标和布局变化推送上下文；候选矩形处理 flipped view 与 Retina；切换编辑器丢弃旧组合；AppKit cancelOperation 取消组合，组合期间仍分发 Command 快捷键 | 原生取消、只触发一次结束、保留原文选区及 Command 按键检查通过；真实中文候选窗待验收 |
| AppKit 文本存储与替换 | 周边文本、UTF-16 文档选区、组合文本虚拟快照和任意替换范围；范围遵守字素边界并经过 PreviewTextInput；五种编辑器接入 | 原生协议和托管编辑/撤销检查通过；系统输入法与文本服务待实测 |
| AppKit 文本几何 | 任意范围的首个视觉行矩形与实际 UTF-16 范围；屏幕点映射到编辑器；组合索引映射与密码保护；完整控件变换和 Retina 转换；TextBox 使用单次 CoreText 布局范围查询 | 原生协议、托管桥接与 20 项真实 CoreText 检查通过；8192 字符查询性能回归通过，系统候选窗待验收 |
| AppKit 控件无障碍树 | 通过 NSAccessibilityElement 暴露 AutomationPeer 的中文名称、角色、状态、动作、值、文本选区及屏幕矩形；能力和写入方法发现按实际 provider 判断；Invoke 请求先返回再执行用户代码，排队后重新检查目标；排除隐藏及退出中的控件，拒绝旧 AX 对象动作；重新显示保留身份；已实现数据项采用实际容器 Peer | v57 同一原生方法发现探针由 3/8 提升至 8/8；外部 Inspector 确认静态文字 Value 只读、TextBox 可写；实际隐藏/关闭后旧按钮引用不可用且 Press 无效，同一窗口重开后旧引用恢复。v28 Gallery 隔离证据保留；其余属性、通知接收与 VoiceOver 待验收 |
| EditControl 原生文本无障碍 | 专用 EditControlAutomationPeer 提供 Value/Text、UTF-16 选区、可见文本矩形与滚动；空文档和单行也保持 AXTextArea；AX 写入沿用编辑事务、撤销记录及 Text 绑定，禁用/只读/隐藏/移除/关闭状态拒绝写入；值和选区通知先于用户回调 | v117 修复前真实宿主 0/16，修复后两种标题栏 16/16；相关宿主 118/118，相关托管 573 通过、2 跳过；外部 AX 读写中文、emoji、多行、组合字符选区、只读能力及全屏往返已检查；外部通知接收与 VoiceOver 仍待验收 |
| 三类文本控件的原生导航 | 视觉行、UTF-16 字形与屏幕点查询、可见范围、caret 行、单选数组、可撤销的选区替换；只读与禁用能力发现；公开 provider 的真实屏幕/DPI 范围 | v120 导航专项 42/42，相关真实宿主 267/267，托管全项目 897 通过、2 跳过，原生 C++ 2/2；两种标题栏的富文本格式/选区、撤销重做、只读、全屏往返与最小尺寸已实测；任意外部参数化 AX 调用、通知与 VoiceOver 仍开放 |
| 公共文本样式与 RTF | 三类文本控件的字体、字号、字重、倾斜、纯色前景、下划线/删除线、对齐和声明的语言/方向；AX 带属性字符串、RTF 和样式范围；公开混合属性、FindAttribute 与 Format 单位；密码/失效目标保护 | v120 样式真实 AppKit 36/36、新增托管 21/21；实际混合字号、颜色、下划线与斜体已检查。v121 接通共享字体匹配，下行另列范围；RTF 的 NFC/换行规范化按实际 NSTextView 比对，AX 子串仍保留原始 UTF-16；完整字体轴、逐字形回退、其他格式及任意外部参数化调用仍有缺项 |
| 绘制、AX 与 RTF 主字体一致 | Metal 和 AppKit 共用 CoreText 私有字节、集合 face、数值字重、倾斜和字宽匹配；托管样式快照解开带引号的有序族列表、CSS 字宽封装及主字体计划，传递实际主字体参数 | v121 原生字体专项基线 3/9、最终 9/9；新托管 13/13、真实宿主 12/12；完整 Metal 字体 29,920 项通过。两种标题栏、三类编辑器实际窄粗体与外部 AX 格式通过，字宽改变后 AX/RTF face 在宿主中同步更新；v122 字体专项扩展到 65/65、新倾斜 Host 24/24，补齐被测变量实例和纯水平剪切的 RTF/现代 AX 表达；逐字形回退、其他字体轴/矩阵及字体嵌入仍未全部完成 |
| 字体设置后的富文本选区 | 相同有效字体值新增本地属性但没有内容通知时，原文未变仍保存最新 caret/选区，后续格式改变及撤销/重做保持位置 | v122 新托管同一基线 0/8、最终 8/8；富文本编辑回归 132/132，真实字体切换 Host 24/24；整个默认项目 918 通过、2 跳过 |
| EditControl 字素左右键 | 物理左右键复用公开字素移动；无 Shift 按既有选区起止收起，Shift 按整组字符扩缩；CRLF 的导航、删除和程序 caret 采用完整边界 | v120 托管基线 31/31 失败、真实 NSEvent 0/8，最终 31/31 与 8/8；两种标题栏实际 Shift+Left/Right 均可收回并选回完整 é，普通 Left/Right 的选区起止收起已实测，文字不变 |
| 祖先裁剪与可见无障碍范围 | 文本矩形、可见范围、命中、点击点和默认 Offscreen 判断遵循自身/祖先布局裁剪、每子项裁剪、变换、滚动和原生客户区；几何不可见的后代仍保留在 AXChildren，AXVisibleChildren 排除其 Offscreen 标志，滚动显露后身份不变 | v119 同一基线托管 6/6 失败、真实 AppKit 0/36，最终新增托管 20/20、AppKit 36/36；包括复合路径孔洞、填充规则、凹形、薄片、旋转投影、动态几何、隐藏与奇异变换。曲线使用元素空间 1/8 DIP 展平，未声明像素级一致或所有派生 peer 自动采用通用 Offscreen 计算 |
| 自定义元素的无障碍焦点 | macOS 缓存缺省 Peer，以 Focusable 或显式名称、标识、标签暴露 Group；匿名布局展平，动态语义与父关系保留身份，标准控件 Peer 和动作能力保持真实 | v87 新增专项 **13/13**、相关托管 **470 通过、2 跳过**；两种标题栏的最小客户区 **520×580 DIP** 实测独立来源/接收区节点、Tab 与 Shift+Tab 源 AX Focused 及可见绿色轮廓、取消框 Space 与复查按钮 Enter。实际拖入、取消与重试通过；动态通知订阅、VoiceOver 与物理边缘缩放仍待验收 |
| AppKit Window 无障碍状态 | 原生 NSWindow 根据托管状态报告 AXEnabled、AXModal 与对话框子角色；普通非 popup 窗口禁用后仍为 AXStandardWindow；取消关闭保留模态，隐藏后普通显示清除模态；嵌套恢复保留外层禁用关系 | v57 宿主 14/14 及原生协议通过；外部 Inspector 实测主窗口启用/禁用均为 AXStandardWindow、AXModal=false，内外层为 AXDialog、AXModal=true，外层禁用及返回恢复正确；Window 其他属性与 VoiceOver 待验收 |
| Window 无障碍几何与选择写入 | 两种标题栏的现代及旧版 AX 入口修改真实窗口，遵守客户区约束与固定尺寸能力；旧版 Size 夹紧后保持上边缘；回调关闭或产生新几何后停止旧操作；Focused/Main/Minimized 到达真实原生动作 | v94 完整前台 Window AX **244/244**，含新增 **12** 项边界，完整前台 Window 生命周期通过；外部 Inspector 数值写入、VoiceOver 及后台激活仍开放 |
| 默认、取消按钮关系 | NSWindow 的 AXDefaultButton 与 AXCancelButton 引用实际托管按钮，复用控件树身份；与 Enter/Escape 共用有效可见性及退出状态查找；CSS 隐藏后代不遮蔽可见按钮，显式显示后代仍可用；原生折叠、display:none、折叠 flex 项及退出动画中的子树排除后代；禁用时保留引用并拒绝动作 | v58 六种 CSS 模式 × 两种尺寸的外部关系目标、Press 和实际 Return/Escape 通过，共 24 次确认、24 次取消、隐藏调用 0；两次折叠后的旧引用 Press 无效；内层默认/取消目标及返回结果已实测。v57 外层禁用关系与宿主 12/12 保留；VoiceOver 待验收 |
| 标题栏按钮关系与动作 | AXCloseButton、AXMinimizeButton、AXZoomButton 引用安装的实际标题栏控件；原生标题栏优先保留 AppKit 控件；标准子角色、可本地化名称及 Invoke；最大化/还原复用缩放按钮身份，隐藏、禁用、换模板及销毁后重验动作 | v61 标题栏宿主 10/10、名称/通知/Invoke 单元测试 9/9；外部 Inspector 读到三项窗口引用及三个控件的中文名称、PART 标识、AXButton 和标准子角色；外部 Press 缩放/还原、最小化及系统菜单恢复已实测；完整通知接收与 VoiceOver 待验收 |
| 图标、透明度与窗口参与 | BGRA 图标转为 AppKit miniwindowImage；整窗 alpha；ShowInTaskbar 控制窗口菜单和轮换参与，保留普通窗口 FullScreenPrimary 与 popup FullScreenAuxiliary 角色；切换不修改尺寸 | v29 隐藏菜单条目后进入全屏、全屏中双向切换菜单参与及退出还原尺寸已实测；v55 已实测透明合成，窗口图标仍待实测 |
| SystemBackdrop | 使用 AppKit 原生材质，位于渲染视图下方；随激活、系统外观与减少透明度设置变化；运行时切换保留几何和焦点 | 原生层级/属性及托管检查通过；v55 已查看 Auto/Mica/Mica Alt/Acrylic 的桌面外观与切换后继续编辑；系统外观、减少透明度和失活合成仍待验收 |
| 半透明窗口背景 | Metal 自行预乘输入颜色，Window 的整窗清屏与局部背景填充传入原始 RGB，避免重复乘 alpha | v55 重跑 Impeller/Vello、1×/2×、五种 alpha 共 20 项 GPU 检查，重复局部绘制保留区域外像素；实际桌面透明与半透明背景合成及继续编辑已检查 |
| 窗口位图与默认深度状态 | 物理像素读回保留捕获尺寸，拒绝短 stride，单次消费并处理并发调用；离屏视图不申请呈现 drawable；无模板裁剪时使用显式默认深度状态 | v161 普通 Debug Metal 18/18、隔离 Release Metal/图片/效果/clipboard 22/22、SDK 位图 12/12；Native/Custom 的实际截图、F6、编辑、焦点、最小尺寸及关闭已检查；固定远端虚拟 GPU 读回对照 13/13，图片 CPU/GPU 作业通过。实体多屏、VoiceOver、最低系统和完整流水线待验收 |
| 动态整窗透明度 | macOS 原生 alpha 跟随 Opacity 的有效依赖属性值，覆盖绑定、样式、SetValue、SetCurrentValue 与动画；动画期间修改基值不覆盖当前动画值；解绑、清值及移除 HoldEnd 动画恢复有效基值 | v55 真实 NSWindow 28/28 通过；实际桌面 35%、绑定 80%、样式 50% 的合成、内容及编辑焦点已检查；动画中改基值后仍使用约 31% 的动画值，结束后托管与原生都恢复 65% |

macOS 的 Dock 图标属于应用，不能按每个 Window 隐藏。窗口图标和 ShowInTaskbar
采用上述 AppKit 语义。普通窗口关闭窗口菜单参与后仍可独立进入全屏；popup 保留
辅助窗口角色，参与位切换也更新全屏转换期间的窗口菜单。
Owner 关闭时不再单独触发 owned window 的可取消 Closing，
与 [Window 的所有权关闭约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.closing?view=windowsdesktop-10.0)
一致。显式 `Application.Shutdown()` 和系统注销/关机策略属于后续 Application 阶段。

macOS 未指定显式 Owner 的 ShowDialog 先选择当前活动、启用、可见且未关闭的
窗口，再回退到主窗口。AppKit 会在隐藏子窗口时解除其 parent 关系；Show、
Visibility.Visible 和 Activate 都重新绑定当前显式或模态 owner，保持原生句柄。
因此同一隐藏对话框可以在新的推断 owner 下重开；转为无显式 Owner 的普通显示时
保持独立窗口。显示前初始化回调的新显式 Owner 优先于本次模态入口保存的 owner，
原生 parent、居中定位、OwnedWindows 与 Quit 协商顺序采用该显式关系。
已显示的模态窗口更换或清除 Owner 会拒绝，原有托管与原生关系保持不变；参考
[Window.Owner 的模态约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.owner?view=windowsdesktop-10.0)。

应用 Quit 会先调用最深层子窗口的 `Close()`，让每个窗口有机会通过 Closing 取消。
这里包含 ShowDialog 推断的原生父窗口；否则先关闭父窗口会直接带走 owned window，
绕过子窗口的取消处理。普通 owner.Close() 仍遵守上述所有权关闭约定。

macOS 的 ShowDialog 禁用调用时应用内其他有效且已启用的窗口，记录本次改动；
返回或抛异常时恢复仍存在的窗口。已经禁用的窗口保持禁用，嵌套对话框只恢复上一层。
优先恢复进入模态前的活动窗口，其已关闭时再尝试恢复 owner。启用状态回调抛异常时
也会继续恢复其他窗口，再向调用者传播异常。参考
[ShowDialog 的应用模态与激活约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.showdialog?view=windowsdesktop-10.0)。
Hide、Visibility.Hidden 或 Collapsed 会结束模态循环、把未指定结果设为 False，并
保留原生句柄供再次显示；接受关闭但未设置结果同样返回 False。Closing 取消则保留
模态循环且清空 DialogResult。重复模态、已经可见或已经关闭的窗口会在改变 owner
状态前拒绝 ShowDialog。参考
[Window.Hide](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.hide?view=windowsdesktop-10.0)
和 [DialogResult](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.dialogresult?view=windowsdesktop-10.0)。

自定义标题栏每次双击都读取系统“连按窗口标题栏”偏好，动作在第二次按键释放时
执行；释放到内容区、窗口外或失去激活时取消。Zoom 使用 AppKit 的标准缩放尺寸，
Fill 填充工作区，Minimize 遵守最小化能力；No Action、未知值和无效值保持窗口。
未设置偏好时采用 Zoom。读取不会修改用户设置。缩放过程先回写窗口状态，再通知
尺寸变化，避免把缩放尺寸保存为普通窗口的 RestoreBounds；回调中的还原请求在
缩放完成后处理。参考 [Apple 的桌面与程序坞设置](https://support.apple.com/en-gb/guide/mac-help/mchlp1119/mac)
和 [NSWindow.zoom](https://developer.apple.com/documentation/appkit/nswindow/zoom%28_%3A%29)。

AppKit 的系统 Fill/平铺不一定进入 inLiveResize。外部尺寸变化按用户调整回写，
退出 SizeToContent；框架设置尺寸、约束和样式使用独立标记，保留自动尺寸模式。
从最大化平铺到其他尺寸时，先同步 Normal，再回写新的普通窗口尺寸。还原回调中
读取 RestoreBounds 仍得到保存的普通尺寸，回调重入的最大化/还原请求在当前转换后执行。
macOS 的 RestoreBounds 在普通状态查询当前客户区，未显示及已关闭时为 Empty；
Closing 期间仍可读取几何。参考
[Window.RestoreBounds](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.restorebounds?view=windowsdesktop-10.0)。

原生 StateChanged 对 WindowState 的回写只跳过当前属性变化。StateChanged 处理器、
内部属性监听器以及派生 OnPropertyChanged 在基础回调之后发出的新请求都进入
AppKit 状态队列。Show 记录应用请求的版本，在初始化、定位及原生显示回调结束后
采用最新请求；原生去最小化反馈不会丢弃显示前请求的 Minimized 状态。
最小化动画开始前保存普通窗口尺寸，动画期间的显式状态请求进入队列，完成后
重放最新请求。过渡期间无障碍几何写入及重复的原生窗口菜单状态动作暂不可用；
没有发出开始通知的原生拒绝请求释放等待标记。完成回调关闭窗口后，旧请求不再
操作替代窗口。真实宿主检查持续处理 AppKit 事件并等待原生完成结果。参考
[windowDidMiniaturize](https://developer.apple.com/documentation/appkit/nswindowdelegate/windowdidminiaturize%28_%3A%29)。

最小化开始和完成通知中的 Activate 或 AX Main 请求等待实际去最小化完成后再选择
窗口。AX Main 只选择文档窗口，不切换键盘窗口；隐藏、禁用、再次最小化和关闭会
取消旧选择，旧异步 AX 请求不能在取消之后重新选中窗口。隐藏的最小化窗口保留
还原请求，重新显示后再执行，避免等待 AppKit 未发出的去最小化通知。
macOS 的 `Window.Activate()` 返回 true 表示平台接受请求；实际前台结果以
`IsActive`、`Activated` 和原生 key window 为准。实现使用公开的 NSApplication
activate；[Apple 的协作激活约定](https://developer.apple.com/documentation/appkit/passing-control-from-one-app-to-another-with-cooperative-activation)
说明请求不保证应用最终取得前台，因此不能用返回值代替焦点验收。

宿主默认 Window 菜单提供 Minimize（Command+M）、Zoom、全屏（Control+Command+F）、
Close（Command+W）和 Bring All to Front，并注册为 NSApplication.WindowsMenu。
默认应用菜单在主菜单为空或其应用子菜单为空时补齐 Quit（Command+Q）；已经注册
Window 菜单也不会跳过此步骤。已有应用菜单动作保持原样，反复配置不重复添加动作。
只有 Window 菜单的主菜单会先补齐独立应用菜单，避免把 Quit 插入 Window 菜单。
自定义无边框窗口通过公开的 addWindowsItem、changeWindowsItem、updateWindowsItem
和 removeWindowsItem 维护窗口列表，随标题、显示/隐藏、ShowInTaskbar 和销毁更新。
已注册的窗口菜单保留；已有名为 Window 的菜单只注册，
不改写其内容。菜单动作复用窗口的状态和可取消关闭路径。参考
[NSApplication.windowsMenu](https://developer.apple.com/documentation/appkit/nsapplication/windowsmenu)。
菜单识别使用现有主菜单，参考
[NSApplication.mainMenu](https://developer.apple.com/documentation/appkit/nsapplication/mainmenu)。

AppKit 的 performClose 默认要求窗口具有关闭按钮，无边框窗口通过覆盖此动作
发出可取消关闭请求。回归检查覆盖有框和无框窗口、取消后保留窗口、禁用、缺少关闭
能力、接受请求时重入销毁，以及销毁后的迟到请求。参考
[NSWindow.performClose](https://developer.apple.com/documentation/appkit/nswindow/performclose%28_%3A%29)。

macOS 的 SystemBackdrop 使用语义对应的 AppKit 材质：Auto/Mica 为
WindowBackground，Acrylic 为 Popover，MicaAlt 为 UnderWindowBackground。
它们呈现 macOS 的原生效果。设置不透明的 Window.Background 会覆盖材质；
半透明背景可作为材质上的色调。材质随窗口激活状态变化，并使用系统外观。
减少透明度时隐藏材质，以不透明系统背景替代，同时保留渲染内容的 alpha。
参考 [AppKit 材质](https://developer.apple.com/documentation/appkit/nsvisualeffectview/material-swift.enum/underwindowbackground)
和 [减少透明度设置](https://developer.apple.com/documentation/appkit/nsworkspace/accessibilitydisplayshouldreducetransparency)。

`NSTextInputClient` 的文本查询使用文档绝对 UTF-16 范围。macOS 保留完整周边文本，
不会因其他平台的 4000 字节上下文上限移动文档原点。原生虚拟快照将组合文本放在
原选区上，确认前保留托管原文；查询、选区、整段或局部组合文本替换都保持同一
坐标约定。参考 [NSTextInputClient 协议](https://developer.apple.com/documentation/appkit/nstextinputclient)。

TextBox、RichTextBox、EditControl、AutoCompleteBox 和 NumberBox 接入文档范围请求。
替换先经过 PreviewTextInput，焦点变化后重新检查目标，避免提交到其他编辑器。
TextBox 保留大小写与长度约束，AutoCompleteBox 保留 Tab 完成和建议过滤，NumberBox
按替换后留下的文本过滤数字输入。取消组合不删除原选区；密码框也保留原文与选区，
同时继续拒绝周边文本查询。各编辑器的候选矩形跟随组合文本内部的光标。

AppKit 的 cancelOperation 在组合期间清除原生 marked text，再丢弃输入上下文并发出
一次组合结束事件，不提交或删除文档。组合期间仍向托管层分发 Command 按键，
使 Command+K 等应用快捷键能够改变焦点并取消旧编辑器的组合。其他组合编辑键
继续交给 AppKit。协议测试覆盖原文、选区、结束事件次数和原生 Meta 修饰位；
真实中文候选窗中的取消与快捷键行为尚未验收。

RichTextBox 的范围替换在编辑时保留范围外的格式。v47 的 Undo/Redo 快照恢复原有
文档节点、格式继承、绑定与选区，不再重建纯文本；如果历史节点已经归属其他
编辑器，撤销保留当前内容与历史。列表、表格、嵌入内容及重复空文档撤销有回归
覆盖。v48 的嵌套编辑分组形成一个文档撤销单元，保留最外层起始选区；禁用撤销
或修改上限会清空两份历史。v49 的 Run 文字、节点与格式修改在文档和选区稳定后
发送 TextChanged；分组、Undo/Redo、Document 赋值及普通 TextBox 替换也遵守此边界。
RichTextBox 暴露 Text provider 的正文、选区、只读状态和渲染几何；隐藏后的缓存 AX
节点不返回文字，重新显示后恢复。相邻 Run 边界按前向/后向亲和性选择正确节点，
同 Run 的完整替换保留原 Run。v50 通过共享范围编辑保留未选中的节点、跨段尾部
继承格式以及 Section/ListItem 的归属；嵌套 Span 换段和多行粘贴不再扁平化文档。
部分 Run 格式化、Table/锚定/嵌入内容的完整范围编辑、输入合并、可变格式对象和
完整文档元素通知仍需补齐。

`firstRectForCharacterRange:actualRange:` 同步查询当前编辑器的实际布局，返回首个
视觉行片段和对应的 UTF-16 范围；空范围返回零宽插入矩形。查询可跨逻辑换行、
字素和组合文本边界，调用者可依据 actualRange 继续查询后续片段。
`characterIndexForPoint:` 将 AppKit 屏幕坐标转到窗口物理像素，再逆变换到控件坐标；
编辑器视口内返回最近的字符边界，视口外返回 NSNotFound。组合文本查询使用临时
字符串的局部索引，原生端映射回虚拟文本存储；原选区中被组合文本覆盖的字符不再
作为虚拟文本命中。查询不改变选区和内容，异常、失去焦点或关闭后的结果被拒绝。
密码框只开放组合文本几何，继续拒绝密码文档查询。

TextBox 的候选矩形和组合文本锚点使用视觉行位置、垂直对齐、模板内容原点及滚动
偏移；Window 的几何转换包含控件变换和 RenderOffset。CoreText 的字符末尾查询
前进到整个字素末尾，软换行边界落到正确视觉行；RTL 命中不因次要光标位置重复
前进。Metal 的换行模式已按共享 ABI 修正为 0=Wrap、1=NoWrap、2=Character。
参考 [范围矩形查询](https://developer.apple.com/documentation/appkit/nstextinputclient/firstrect%28forcharacterrange%3Aactualrange%3A%29)
和 [位置查询](https://developer.apple.com/documentation/appkit/nstextinputclient/characterindex%28for%3A%29)。
这些接口的自动检查不等同于系统文本服务、听写或 Writing Tools 已完成验收。

macOS 内容视图现在保留稳定的 `NSAccessibilityElement` 对象，由现有 AutomationPeer
动态提供内容。AppKit 继续暴露原生窗口、标题栏和菜单；托管 Window 的 Peer 作为
内容组，避免增加第二个 AXWindow。名称、帮助、AutomationId、占位文字和启用状态
从 Peer 读取；按钮 Invoke、Toggle、SelectionItem、RangeValue 与 ExpandCollapse
接入各自的实际动作。数据项代理统一映射到已实现容器的 Peer，保持父子关系与
展开、选择操作一致；TreeView 提供可见的 outline rows、展开与选中行。
参考 [NSAccessibilityElement](https://developer.apple.com/documentation/appkit/nsaccessibilityelement-swift.class)
和 [自定义控件无障碍](https://developer.apple.com/documentation/accessibility/integrating-accessibility-into-your-app)。

macOS AX Press 遇到 `IInvokeProvider` 时先把操作排入窗口 Dispatcher，再返回已接受。
调度时重新检查窗口句柄、目标是否仍属于窗口且有效可见、退出状态及启用状态，防止
排队期间隐藏、禁用、移除或关闭后调用旧目标；排队操作中的用户异常继续隔离。
返回值表示请求已接受，不表示用户操作已经完成。Toggle、SelectionItem、值和其他
动作保留原有同步语义。这样通过 AX 打开 ShowDialog 不会把整个模态会话留在原生
AX 请求回调内；参考 [Invoke 的异步调用约定](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-iinvokeprovider-invoke)。

原生 NSWindow 的 AXEnabled 现在对应托管 IsEnabled；ShowDialog 通过窗口根 Peer
报告 AXModal 与 AXDialog 子角色，并使用 AppKit 的本地化角色描述。Closing 取消
保留这些状态，隐藏后普通 Show 恢复普通窗口语义；嵌套对话框报告自身模态并使外层
AXEnabled 为 False，内层结束后只恢复外层。AutomationProperties.IsDialog 可标记
非模态对话框的子角色，不会将其变为模态。托管内容仍为原生窗口下的 AXGroup，
所有权保留 AppKit parent/child window 关系，不增加重复 AXWindow。
v57 明确普通窗口的 AXStandardWindow 子角色，避免本机 AppKit 因 owner 被禁用、
不能成为 main window 而将它报告为 AXDialog；popup 或没有托管查询时保留 AppKit
回退。窗口角色判断所需的 popup 状态在查询回调前保存，避免回调销毁窗口后的读取。
参考 [accessibilityModal](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilitymodal)
与 [accessibilityEnabled](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilityenabled)。

原生窗口的 AXDefaultButton 与 AXCancelButton 动态指向 Button.IsDefault / IsCancel
对应的同一 AX 对象。查找优先使用活动弹层或焦点所在的内嵌 ContentDialog，与
Enter/Escape 的按钮作用域一致；隐藏按钮和隐藏祖先不成为目标。禁用按钮保留
关系并报告不可用，AX Press 由实际按钮的启用检查拒绝；这个保留引用的约定已在
原生 AppKit 默认按钮上实测。隐藏窗口不开放这些引用，重开保留身份；移除或关闭
后旧动作对象失效。原生回调只保留桥接对象，避免动作关闭窗口后再次读取已释放 owner。
参考 [accessibilityDefaultButton](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilitydefaultbutton)
与 [accessibilityCancelButton](https://developer.apple.com/documentation/appkit/nsaccessibility-c.protocol/accessibilitycancelbutton)。

桥接使用内容视图左上角的点坐标，由 AppKit 完成屏幕空间转换，保留 Retina 逻辑点。
文本值、选区和子串使用 UTF-16；文本范围矩形包含控件到根视图的变换。
密码框开放安全输入，但不返回密码文本、长度或选区。只读值不开放写入，禁用窗口
和控件拒绝动作。隐藏窗口不暴露内容且拒绝动作，重开保留原有 AX 身份；移除控件
或关闭窗口后，旧 AX 对象不再进入托管回调。动作中的用户异常被拦截在原生边界。
v57 同时限制 `respondsToSelector:` 对写入方法和动作的发现，避免 AppKit 根据继承的
setter 将静态文字 Value 报告为可写。只读文本仍可选择并获得焦点，可写文本仍可
修改值；禁用时不公布这些写入方法。外部 Inspector 已确认静态 Value 和可写编辑器，
其他属性的外部可写元数据尚未全面确认。参考
[AppKit 自定义控件的读写属性与选择器约定](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/ImplementingAccessibilityforCustomControls.html)。
查询只在 AppKit 主线程进入 Peer；外部线程请求直接拒绝，避免跨线程访问和同步
调度死锁。框架焦点、值、选区和结构事件已连接到 AppKit 通知，通知的外部接收
尚未实测。文本行导航、复杂表格、虚拟化列表及完整控件语义仍需专门补齐和验收。

新增的可选范围查询接口返回 24 字节几何结果，通过独立的 TextRangeMetricsProvider
接入后端，保留已有 TextFormat 虚表。旧版原生库缺少导出时，托管层回退到原有查询。
Metal 使用一次 CoreText frame 布局及 CTLineEnumerateCaretOffsets 获取首个视觉行，
处理字素、RTL、软换行和 CRLF；TextBox 使用此结果，按每个视觉行绘制组合文本背景
与下划线。8192 字符的 TextBox 范围查询在本机由约 34.2 秒降到约 2.7 毫秒；
回归检查要求在 5 秒内完成。该结果只对应此用例，其余编辑器的长范围性能仍待扩展。

## 可复现检查

托管测试不创建真实 AppKit 窗口，因此可在测试宿主线程运行。它引用仓库的统一
托管实现，覆盖 macOS Window，并保留平台样式、窗口约束、置顶和光标回归检查。

```bash
dotnet test tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj \
  -c Debug -p:JaliumBuildRoot="$PWD/artifacts/macos-window-tests"
```

真实字体几何检查使用 Metal 后端的 CoreText，不打开窗口。原生负载已构建后可运行：

```bash
DYLD_LIBRARY_PATH="$PWD/src/native/bin/native/osx-arm64/Debug" \
JALIUM_METALLIB_DIR="$PWD/src/native/artifacts/apple/slices/osx-arm64/Debug" \
dotnet test tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj -c Debug \
  -p:JaliumBuildRoot="$PWD/artifacts/macos-window-font-tests" \
  -p:JaliumMacNativeGeometryTests=true \
  --filter 'FullyQualifiedName~MacOSTextGeometryIntegrationTests'
```

真实 AppKit 检查在原生测试可执行文件的主线程运行。先按
[macOS 开发说明](macos-development.md) 准备原生构建，再运行：

```bash
cmake --build src/native/out/build/apple-macos-dev-arm64 --config Debug \
  --target jalium.native.platform.window.tests jalium.native.platform.window.resize.tests \
    jalium.native.package.complete
ctest --test-dir src/native/out/build/apple-macos-dev-arm64 -C Debug \
  -R '^jalium.native.platform.macos.window' --output-on-failure
```

完整 `macos.window` 检查需要解锁且能够激活测试应用的桌面会话，包含实际激活、最小化和 Spaces
全屏转换。`macos.window.properties` 检查样式、尺寸、所有权、输入转换、输入法
坐标、周边文本/选区/组合与显式范围替换、标题栏双击偏好、菜单动作能力，以及
缩放事件顺序、转换前的还原几何、外部系统布局、重入状态请求、无边框菜单关闭和
窗口参与角色。窗口菜单生命周期检查会显示、隐藏测试窗口，
其他属性检查主要使用隐藏窗口；该目标不依赖前台激活。
`macos.window.resize-drag` 使用属于测试进程的原生按下事件、tracking-mode 定时器
及排队的拖动/释放事件；核对实际 AppKit 追踪循环的中断、几何与重复调用。
它不证明物理鼠标、托管窗口 chrome 路由或外部工具的连续拖动已通过。
几何协议检查还覆盖屏幕坐标、首行 actualRange、零宽插入矩形、组合前后索引映射、
拒绝结果和无效浮点数。

宿主菜单检查使用真实 AppKit，并在进程主线程运行。各场景启动独立测试进程，
避免 AppKit 在应用生命周期内保留的 WindowsMenu 干扰其他场景；测试不显示窗口，
并禁止应用激活，因此不替代实际菜单和键盘操作验收。

```bash
dotnet build tests/Jalium.UI.MacOS.HostSmoke/Jalium.UI.MacOS.HostSmoke.csproj \
  -c Debug -p:JaliumBuildRoot="$PWD/artifacts/macos-window-host-tests" \
  -p:ValidateXcodeVersion=false
"$PWD/artifacts/macos-window-host-tests/bin/Jalium.UI.MacOS.HostSmoke/Debug/net10.0-macos/osx-arm64/Jalium.UI.MacOS.HostSmoke.app/Contents/MacOS/Jalium.UI.MacOS.HostSmoke"
```

同一个宿主的 `--accessibility` 在独立主线程进程中检查完整的
AutomationPeer → C ABI → AppKit 协议链路：

```bash
"$PWD/artifacts/macos-window-host-tests/bin/Jalium.UI.MacOS.HostSmoke/Debug/net10.0-macos/osx-arm64/Jalium.UI.MacOS.HostSmoke.app/Contents/MacOS/Jalium.UI.MacOS.HostSmoke" --accessibility
```

应用激活策略为 Prohibited。焦点用例检查托管目标接收到请求；这些用例不等同于
外部 AX 客户端读取、前台焦点、系统通知接收或 VoiceOver 朗读验证。

同一个宿主的 `--window-accessibility` 检查实际 ShowDialog、原生 AX 关闭按钮、
关闭取消、隐藏转普通显示、嵌套模态和显式非模态对话框标记。断言真实 NSWindow
的启用、模态、角色与所属关系，也保留内容组到原生窗口的无障碍父子关系：

```bash
"$PWD/artifacts/macos-window-host-tests/bin/Jalium.UI.MacOS.HostSmoke/Debug/net10.0-macos/osx-arm64/Jalium.UI.MacOS.HostSmoke.app/Contents/MacOS/Jalium.UI.MacOS.HostSmoke" --window-accessibility
```

这些检查同样禁止应用激活，不证明前台焦点或 VoiceOver 接收到状态变化。

`--window-buttons-accessibility` 检查实际默认/取消按钮引用和操作、动态切换、禁用、
隐藏、移除重插、窗口重用与销毁、模态关闭取消和结果，以及弹层/内嵌 ContentDialog
作用域。隐藏祖先用例还直接调用 Enter/Escape 使用的按钮解析器，核对其与 AX
引用一致；它不是物理键盘事件或前台焦点验收：

```bash
"$PWD/artifacts/macos-window-host-tests/bin/Jalium.UI.MacOS.HostSmoke/Debug/net10.0-macos/osx-arm64/Jalium.UI.MacOS.HostSmoke.app/Contents/MacOS/Jalium.UI.MacOS.HostSmoke" --window-buttons-accessibility
```

`--window-caption-accessibility` 在同一宿主中运行 10 个独立标题栏场景；
`--window-caption-accessibility-case=0` 至 `=9` 可单独重跑。构建与默认/取消检查相同，
必须使用本轮重新完成的原生负载。使用新版 `--artifacts-path` 时，以构建日志中的
实际测试 DLL 路径为准，将原生库置于其同目录；不要沿用旧的 `Debug/net10.0` 路径。
启用 `JALIUM_METAL_BUILD_TESTS=ON` 后，原生 `jalium.native.metal.regression`
支持 `--text-line-height`，可单独执行取整行高的 GPU 像素回归。

本机 Xcode 27 与当前 .NET macOS workload 的版本校验不匹配，验证构建沿用 Gallery
的 `ValidateXcodeVersion=false`；这没有替代最低版本或发行工具链验收。

真实宿主生命周期检查显示测试窗口但禁止激活，使用 Metal 渲染，在独立主线程
进程中检查 SourceInitialized、Loaded、ContentRendered、Shown 中的关闭，以及
SourceInitialized 隐藏/重开、最大化/还原请求，以及原生 StateChanged 和派生属性通知
中的还原/最小化请求与 RestoreBounds，以及模态和所有权重用。
模态结束时会调用恢复激活路径，但 NSApplication 策略禁止应用激活；这些检查
不能证明前台恢复或实际活动窗口选择已通过：

```bash
"$PWD/artifacts/macos-window-host-tests/bin/Jalium.UI.MacOS.HostSmoke/Debug/net10.0-macos/osx-arm64/Jalium.UI.MacOS.HostSmoke.app/Contents/MacOS/Jalium.UI.MacOS.HostSmoke" --window-lifecycle
```

这些断言不能代替实际截图、键盘菜单操作或前台激活验收。

Gallery 的常规构建与运行入口：

```bash
bash eng/apple/run-gallery.sh Debug
```

多项本地工作共用原生输出时，应为 Gallery 构建指定独立的 `JaliumBuildRoot`，并
使用已完成且保持不变的 `JaliumMacNativeRoot`。原生构建的完成标记必须存在，
否则打包项目不会包含该原生负载。

2026-10-09 v129 本轮验证及 PR 源码复查后，清理 **2** 个 artifacts 根、**8,060** 个文件/链接、
**4,401,066,211** 逻辑字节，以及 **21** 份已归属临时日志；匹配本轮包的崩溃诊断
**0** 份。清理前后 **18,091** 个受保护文件/链接及 **1,214** 个目录的内容、
模式或链接目标一致，Gallery 的 **14** 个既有缺失路径保持缺失；完整 `.tools`、
源码、测试和其他工作树改动保留。两个仓库不区分大小写的字面 artifacts 路径、
本轮应用进程及已归属日志均为 **0**。最终 Lab 两个 PID 经 ⌘Q 均退出 0，PR 源码
托管 **918 通过 / 2 跳过**，其独立宿主回归 **109/109**。本段在保护核对完成后追加；
已删除的包与日志路径不作为当前交付文件。

## 本次验证记录

验证环境：Apple Silicon、macOS 27.0.1、Xcode 27.0、.NET SDK 10.0.300。
这不等同于在声明的 macOS 15.0 最低版本、Intel Mac 或签名发布环境验收。

最近实际验证的 Gallery 应用仍为 **v111**，Window Lab 为 **v136**，重开恢复专项宿主为 **v138**、重开契约及绑定/属性宿主为 **v140**、输入边界宿主为 **v141**、初始定位宿主为 **v142**、应用切换观察宿主为 **v139**。字体匹配、合成倾斜及 RTF 保真专项保留 **v122** 记录；
v126 修复 AX 读取生命周期，v127 修正 TextBox 选区测量，v128 重新构建原生负载并修复布局通知中的节点生命周期；
控件/窗口 AX、文本导航、生命周期、Tab 与选区像素宿主已在 **v128** 复查，编辑菜单的最近专项仍为 **v127**。
完整原生前台 Window AX 为 **v128、280/280**，独立原生前台 Window 生命周期组为 **v128**。最近完成全部
外部标题栏按钮动作验收的负载仍为 **v61**；v63 的最小尺寸截图保留。v65 发现的
Main=False 选择问题已经有 v66 修复与 v68/v70 前台回归，v70 又完成两种标题栏的
外部 Main=False / Focused=False 回读、Main=True 激活及 Lab 两次连续缩放。
Position/Size 的外部数值编辑仍受桌面工具限制，拖动中动态几何与真实设备输入
保留独立验收缺项。v71 已实际验证新窗口上下文菜单和最小布局；标题栏坐标右键
仍受工具限制。v72 补查初始定位、还原后的托管坐标、混合标题栏、编辑和最小布局。
v73 补查最小化动画中的状态排队、实际重开与最小布局；后续外部键盘输入仍未验收。
v74 补查动画中的激活选择、取消后的重新激活及两种标题栏继续输入；最终验证面板
的实际焦点文字、键盘导航与最小布局已复查。
v78 补查独立全屏窗口的两种标题栏连续输入、最小布局及键盘；v79 又在最终拖放
修复负载上完成同一范围的实测，并核对实际加载的原生库。
v58 复用了 v57，v59–v84 均重新构建各轮实际使用的负载。
本轮构建输出已按用户要求清理，历史构建路径不作为当前交付文件。

### v161：虚拟 GPU 的默认深度状态与 Window 再验收

固定 macos-26 / Xcode 26.6 作业中的 Apple Paravirtual device 能执行
单采样、4× MSAA、Stencil8、解析、BGRA 拷贝和 GPU shared-event signal。
本轮继续分拆框架与原生 Metal 的差异；调整尺寸不是必要条件，未调整尺寸的
框架清屏也失败。仅绑定裁剪纹理或设置裁剪区域时通过，单独调用
setDepthStencilState:nil 就复现 GPU Hang。

[Apple 的深度状态接口文档](https://developer.apple.com/documentation/metal/mtlrendercommandencoder/setdepthstencilstate(_:))
说明 nil 恢复 MTLDepthStencilDescriptor 的默认值。现在创建并复用显式默认状态，
用于无模板裁剪的内容及 backdrop 效果绘制；模板裁剪继续使用已有比较状态。
默认状态创建失败会使初始化失败。未更改纹理用途、MSAA、裁剪范围或呈现条件。

原生对照覆盖纹理用途、1×1 R8 绑定、scissor、深度状态、AppKit、无窗口 layer
及 GPU 拷贝待执行时替换纹理。默认对照使用与生产实现一致的显式默认状态；
旧 nil 行为保留为手动 --gpu-nil-depth-repro 入口，不属于正常基线。
框架的 resize/并发消费/窗口附着与脱离及独立的无 resize 读回均保持严格检查。

本轮证据：

- 修复前的远端 6b9def3d 作业为 **7/13**，单独的 nil 深度状态失败，
  独立纹理绑定和 scissor 对照通过；两项框架读回均失败。
- 修复后的远端 40fc1ba3 作业 **13/13、2.93 秒**，
  [Apple run 38091188777](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38091188777)
  的独立图片方向作业也通过 CPU 像素及真实 GPU 绘制两个阶段。
- 本机普通 Debug Metal 集合 **18/18、17.75 秒**。
  隔离的完整 Release Metal/图片/效果/clipboard 集合 **22/22、16.93 秒**，
  链接已准备的固定 DXC/SPIRV-Cross 静态编译器，原生导出检查通过。
  七项 shader 输入与现有编译资产来源一致，两份 metallib 的大小和 SHA256
  均与 manifest 匹配后复用。
- 当前 PR 源码的隔离 SDK Release 构建成功，**31 个警告、0 个错误**；
  --window-bitmap-dpi **12/12**。使用应用 MonoBundle 中的新原生库，无 DYLD 覆盖；
  本机 Xcode 27 构建继续使用 ValidateXcodeVersion=false。
- CUA 实际查看 Native/Custom 各 **3 张截图**：正常、旋转及最小尺寸。
  两类窗口均从托管 **680 × 680** 调整到 **520 × 620 DIP**，
  三列 96/144/192 DPI 图形与孔洞一致，按钮仍完整可达。
  F6 各完成一次旋转/还原；中文 emoji 粘贴及 Command-A 的完整选区、
  Tab 到旋转按钮和 Shift-Tab 回编辑框均由实际 AX 与截图确认。
  Native 通过结束按钮、Custom 通过 Command-W 关闭，原 PID 74301/74386
  均消失，没有匹配本轮身份及 PID 的崩溃报告。关闭后读取目标窗口的 CUA
  调用返回 timeout，关闭结论依据原进程退出，未重新启动目标。

上述软件 DPI 位图不代表实体混合 DPI 多屏验收；中文粘贴不代表原生候选窗输入。
VoiceOver、最低系统、Intel 与签名发布仍不在本轮结论内。
该源码头的完整 Apple native-and-packages 与 Linux 作业尚未结束；
两个专项成功不代表整个 CI 或全部 Window/macOS 行为完成。
临时产物的清理范围仍为两仓库名称恰为 artifacts 的路径及本轮自有隔离输出，
保留正常工具链/原生负载、用户修改、源码测试和现有 PR 分支。

### v160：离屏视图、窗口呈现与独立 MSAA 对照

macOS 的离屏读回目标可以使用尚未挂入 NSWindow 的 NSView。旧实现仍向其
CAMetalLayer 请求 drawable 并尝试呈现。本轮仅在视图有所属窗口时进入呈现路径，
每帧重新读取所属关系；同一视图挂入窗口后恢复呈现，移出后继续场景绘制、
读回与 GPU retirement。已附着窗口保留原有呈现选择，其他 Apple 平台保持原有路径。
这符合 AppKit 的 [NSView.window](https://developer.apple.com/documentation/appkit/nsview/window)
关系以及 Metal 的 [屏幕呈现](https://developer.apple.com/documentation/metal/onscreen-presentation) 模型。

新增回归先链接修改前的普通 Debug 快照，在真实 Apple M6 上失败：
“A detached view must not acquire an onscreen drawable for readback”。
修复后读回检查继续覆盖尺寸查询、短 stride、行尾 padding、resize 和两线程
竞争的八帧单次消费。三轮挂入／移出同一视图均验证 drawable 请求数与实际 BGRA
像素；该连接回归让测试 layer 返回 nil，仅观察呈现选择，实际窗口呈现另由 SDK
界面验证。独立原生 GPU 对照新增 4×MSAA、Stencil8、
StoreAndMultisampleResolve，再复制到共享缓冲区并核对全部红色像素和事件值。

当前主机结果：

- 普通 Debug 的 readback、单采样 GPU 与 4×MSAA GPU 对照 **3/3**。
- 精确 PR 源码叠加本轮修复的隔离 Release：Metal 与 clipboard 回归 **12/12**，
  无跳过；诊断模式开启。两个 shader 输入与普通负载逐字相同，复用其既有
  预编译 metallib，并核对 manifest 的大小与 SHA256。未用 embedded fallback
  代替预编译负载。运行时 HLSL 编译器链接既有固定版本 Debug 静态依赖。
- Debug/Release 均通过实际 CMake completion 与入口导出检查。
- SDK 冒烟构建发现 MediaPlayer 被解析为 macOS SDK 的同名命名空间。
  注册媒体解码器时现明确引用 global::Jalium.UI.Media.MediaPlayer；
  修复后同一 SDK Release 构建成功，位图用例的 **12** 个组合均报告通过。
- 两种标题栏的当前 SDK 窗口均查看实际截图：96/144/192 DPI 的三列图形大小、
  圆角及内部孔洞一致；F6 往返旋转后继续编辑，中文及 emoji 保留。
  设置最小尺寸后内容和按钮均可达，正反 Tab 返回编辑框，Command+A 的选区已查看。
  Native 的结束按钮和 Custom 的 Command+W 均关闭原进程。
  本机 SDK 构建使用 ValidateXcodeVersion=false，最低系统、固定工具链和签名发布仍待验收。

[093065f3 Apple CI](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38088071142)
的独立读回任务为 **2/3**：Apple Paravirtual device 的单采样与 4×MSAA 原生
对照均通过，框架在 Fetch after resize 仍返回 MTLCommandBufferErrorDomain、
code=2、GPU Hang。日志已只包含 scene/effect 和 readback copy，两者均为
Completed（state=1）；移除离屏呈现没有解决该失败。
同一头的 CPU 图像检查通过，Metal 图像仍失败。根因尚待继续隔离，完整 Apple CI
和完整 Window/macOS 验收保持未完成。

本轮临时源树、构建、SDK 应用及注册项、进程均已清理，两仓库 literal artifacts
路径为 0。保护快照复核 **19,008** 个文件、**1,690** 个目录、**507** 个未改动
原生输入，普通原生负载 **28** 项及 Gallery 既有的 **14** 个缺失路径。
用户的 CSS、Tooltip、输入分发、测试项目和 Gallery 工作保留；
普通 bin/out 与 .tools 保留。最终临时审计记录用于提交后的复查，随后删除。

### v159：Metal 读回的并发消费与失败诊断

两个 FetchReadback 调用等待同一帧 GPU 命令时，旧实现会在等待期间释放锁。
先返回的调用取走并清空捕获后，后一个仍返回成功和旧尺寸，却没有复制像素。
本轮在真实 GPU 的 MTLSharedEvent 门控下复现：两个调用均返回
JALIUM_OK、7×5，单次捕获被报告成功两次。修复在 fence 等待结束并重新取得锁后
核对命令身份；捕获已经消费或换成新帧时，返回 INVALID_STATE、尺寸 0×0，
保持调用方缓冲区及新的待取捕获不变。等待期间仍释放锁，保留非阻塞尺寸查询。

新增原生 readback 用例验证空状态、重复请求、重复尺寸查询、短 stride、
带行尾 padding 的真实 BGRA 像素、捕获后 resize 和单次消费。两线程竞争重复
8 帧，每帧仅一个调用取得完整红色像素，另一个保持目的缓冲区不变并返回
INVALID_STATE；随后重新请求继续成功。GPU 命令失败现记录 NSError 的 domain、
code 与 description，避免仅凭验收程序的泛化“尺寸／读回失败”判定原因。

验证记录：

- 最新 PR 头 f57b044b 的隔离 Release 基线：Metal smoke、CPU 图像、Metal 图像
  和私有剪贴板均通过；剪贴板 **47** 项，包括中文与 emoji URL。
  这不能消除 macOS 26 CI 上相同用例的失败。
- 加入修复后，全部 **9** 项 Metal CTest 与 **1** 项 clipboard CTest 最终通过。
  首次扩大运行时为 9/10，路径检查明确拒绝缺少预编译 shader 的临时负载；
  使用同一源码的生产 shader 生成脚本补齐两个 metallib 和 manifest 后，
  路径检查通过预编译 ABI v5 及 **1158** 项真实 GPU 几何、画刷、捕获断言。
  未关闭 shader 断言，也未把 embedded fallback 当成 bundle 验证。
- Release 动态负载完成真实 CMake completion 和入口导出检查。
  使用本机 Xcode 27／macOS 27、arm64、macOS 15 部署目标；运行时 HLSL 编译器
  实际链接现有固定版本的 Debug DXC/SPIRV-Cross 静态依赖。
  这属于当前主机 Release 验收，不能代替固定 Xcode 26.6、最低系统或其他 Apple RID。
- [f57b044b Apple CI](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38084265610)
  的独立图像任务确认 CPU 解码通过、Metal 读回失败；在原日志中没有具体 GPU
  错误，暂未确定其与本轮并发缺陷有因果关系。
  此轮未改动 Window AX、resize-drag 或真实前台激活失败的验收要求。

随后 cdb42993 的独立图像 CI 实际记录
MTLCommandBufferErrorDomain、code=2、
Caused GPU Hang Error (00000003:kIOGPUCommandBufferCallbackErrorHang)。
该单线程图像失败仍然存在；不能把本轮并发消费修复计为 CI GPU hang 的解决。
新增独立 metal-readback CI 任务使用同一个 macos-26 运行器和固定 Xcode 26.6，
分别执行无框架绘制代码的原生 Metal 清屏／BGRA 拷贝／共享事件信号检查，以及
框架的读回生命周期与两线程竞争。任务保持失败返回，未跳过 GPU 验收。

JALIUM_METAL_DIAGNOSTICS=1 现在明确启用各 encoder 的执行状态错误报告，
并标记 scene/effect、readback copy、presentation 阶段；默认保持原来的 command
创建方式。NSError 带有 encoder 信息时，失败日志输出其名称、状态及 signposts。
加入这些诊断后，当前主机的完整 Metal + clipboard Release 回归
**11/11**、无跳过，诊断模式实际开启；其中原生 GPU 基线检查也通过。
acf421ff 的固定 Xcode 26.6 独立检查最终为 **1/2**：
Apple Paravirtual device 的原生 Metal GPU 基线通过，框架读回在
Fetch after resize 时返回 GPU hang。scene/effect、readback copy、presentation
三个 encoder 均报告 Completed（state=1），没有给出 Faulted 阶段。
同头的 CPU 图像检查仍通过，Metal 图像检查仍失败；不能将错误归咎于尺寸
测量、完全不可用的 GPU 或已修复的并发消费。窗口呈现、MSAA 和 resize 等
差异仍需继续隔离，CI GPU hang 的根因尚未确认。

最终同源码的本机 Release 回归 **11/11**、诊断开启；普通 Debug 负载的
readback、原生 GPU 基线与 paths **3/3**、诊断默认关闭。Debug/Release 均通过
实际构建、CMake completion 和入口导出检查。本轮只修改 Metal 读回、诊断、
原生验收及 CI/文档；原生视图的呈现行为尚未调整。

所有本轮外部临时目录和测试进程已清理，两仓库的 literal artifacts 路径为 0。
初始保护快照仍完整：19,004 个受保护文件中，仅三个 .DS_Store 在运行期间
发生元数据变化，保留清理前的当前版本；其余文件、1,690 个目录和 506 个未改动
原生输入均复核。普通原生负载经真实重建，原有文件全部保留并新增 readback
程序，共 28 项；未清理 .tools、普通 bin/out 或用户的既有源码改动。


清理后的最终构建标记复查还发现 src/native 下新生成的 .DS_Store，使 native-only
Git 脏状态检测返回 1。仓库忽略规则现明确排除该 Finder 元数据；文件本身保留。
通过实际 CMake completion 确认元数据不会使负载变脏，并使用临时真实原生源码
验证 dirty=1 仍然生效，移除该验收文件后恢复 dirty=0。

### v158：虚拟列表的滚动、退化恢复与无障碍树

RazorItemsHost 的内部纵向、横向 ScrollViewer 现在明确启用 CanContentScroll，
将首尾和行／页滚动交给实际的虚拟化面板。外层 ScrollViewer 的默认物理模式仍为
false；需要外层视口驱动 @virtualize 时，示例和集成检查明确设置
CanContentScroll="True"。未获得有限视口时的 eager 退化改为临时 coercion；
再次获得视口会恢复应用的 IsVirtualizing 基值，保留显式 false 和 Binding 的最新值。

macOS AX 树直接读取虚拟化面板当前实现的容器，继续按现有可见性与控件语义规则
展开这些容器。它不再枚举整个逻辑范围或依赖已缓存的项目身份。两种滚动宿主的
百万项范围在首端、末端及返回首端均只读取已实现的行；AX 查询阶段的数据源枚举数、
索引读取数均为 **0**。将桥接器临时还原为旧版本，缩小为 128 项的相同检查
**2/2 失败**；恢复后的百万项检查 **2/2 通过**。

焦点回归保留复合 Geometry 裁剪，不再要求 RectangleGeometry 的具体类型。
检查部分可见区的包含关系、完全离开视口后的排除关系以及返回后的完整裁剪。
滚到末尾的共享测试使用 Window 的真实布局管理器消费细化后的 extent，而非只
重测单个面板。默认及显式物理模式、内容模式切换、纵横向第 5000 项的实现和回收、
退化后保留显式值与 Binding 均有共享回归。

验证记录：

- 隔离的最新 PR 源码加本轮修改：macOS **1722/1722**，无跳过；
  **961** 个原生几何上下文全部为有效 Metal。共享 Razor 文件仅在隔离测试工程
  中额外链接，未修改用户工作区的 macOS 测试工程。
- 实际 Ubuntu 20.04 x64／glibc 2.31 容器、校验 SHA-512 的 SDK 10.0.300、
  从同一隔离源码构建的 core/platform/software 及 Xvfb：相关共享测试
  **38/38**，无跳过。该容器在 Apple Silicon 上模拟 x64，未代替 Linux 各 RID CI。
- 原生 SDK 宿主 **8/8**：Native/Custom 标题栏、Self/Outer、纵向/横向，
  5000 项末尾、返回首端及缩小窗口后再次首尾定位。各独立测试包经严格签名检查；
  两个实际交互窗口的 8 个已加载原生库均与各自包内指纹一致。
- 已查看 Native/Custom 的实际截图和 AX 树，使用鼠标、Tab、Page Down、F6/F7/F8：
  键盘焦点环跟随第二行，翻页后视口外焦点环被裁剪，AX 行随滚动更新；
  520×460 最小窗口中的标题、说明、列表及全部动作可操作，独立与外层模式
  均能访问第 5000 项并返回。以上不等同于 VoiceOver 实际朗读验收。
- 使用 --window-virtualized-content 运行自动原生检查；
  --window-virtualized-content-observe 与其 -custom 版本打开相应交互窗口。
  本机 SDK 构建仍使用 ValidateXcodeVersion=false，属于当前宿主验收，
  未证明项目声明的最低系统或固定发布工具链。

上一轮头 c9a203be 的 [Linux CI](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38079824581)
中，已结束的 musl arm64、musl x64 与 glibc arm64 均剩本轮处理的 **5** 项失败：
**5539 通过／75 跳过／5619 总计**；本轮的 38 项本地回归已覆盖这五项。
glibc x64 与合并打包被取消，不能计为通过；该 x64 日志在取消前另有一个
OleDropTarget 销毁后释放 Window 根的失败，仍待独立复核。
[Apple CI](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38079824535)
已实际执行全部 22 项原生测试，**13 通过／9 失败**，不再有未构建的测试程序。
Unicode URL clipboard、实际前台激活、Window AX/resize-drag 及 Metal Retina
readback 等失败仍待处理。新 PR 头必须以其独立 CI 结果为准，未声称完整 CI 或
Window/macOS 总目标已经完成。

发布前现有 PR 分支又新增四个并行提交，头推进至 4ffb54eb。整合时保留其
AX 快照、弱注册表、容器父节点、拖放限额和 Apple 图像测试改动；本轮的已实现行
分支与新的批量快照协议合并。两个并行验收用例也在 PR 整合提交中修正：预算 8
下，3 字节项将余额降为 5，6 字节项被拒绝，随后 3 字节项仍可接收，读取预算为
8/5/5，重复快照不重读；AppKit view.accessibilityChildren 查询根节点时的重入
阶段为 Info，实际子项查询仍覆盖 BeginChildren/ReadChildren。

整合源码的完整 macOS 回归最终 **1737/1737**、无跳过（修正预算断言前为
1736 通过／1 失败）。从该源码重新构建原生负载并完成 CMake completion 与
入口导出检查后，SDK 窗口检查再次 **8/8**；原生 AX 协议检查通过，其中读取
生命周期 **100/100**、通知生命周期 **22/22**（修正根查询阶段前在读取用例 20
失败）。该构建关闭运行时 HLSL 编译器，不等同于发布 shader 工具链验收。
已再查看新批量协议下的 Native/Custom 最小窗口截图与 AX 树：两种滚动模式
均读取第 5000 项，返回首端并关闭；两个进程各自加载的 8 个库均匹配严格签名
测试包内指纹。整合树保留另外 31 个并行文件，两个验收修正仅随 PR 的整合提交
交付，不引入用户本地旧托管接口尚不支持的测试文件。

最后一组交互时，仓库内测试目录被提前删除，初始全文件保护快照也随之丢失。
已记录的自动回归结果保留为本轮证据；四个已知用户脏文件的原有 SHA-256 已复核。
随后在独立临时目录恢复当前 PR 源码、冻结原生负载和验收宿主，再继续交互与最终
清理。从恢复点起的工作区、508 个原生输入与普通原生输出另行核验；不把恢复点
快照冒充已经遗失的初始快照。测试产物、临时宿主和本轮外部文件最终统一清理。

### v157：跨平台预编辑合同、回收检查隔离与完整原生测试构建

几何查询检查现在按宿主确认组合开始后的文字，并在查询及取消前后比较完整文字、
光标和锚点。AppKit 继续保留选区，其他宿主沿用开始组合时删除选区的行为；密码
检查仍禁止查询已提交文字、字符位置及 surrounding text。六个几何用例都继续执行。

coercion 检查现在验证最大递归深度为一及嵌套读取返回当前基值。固定回调总次数
会受正常属性读取影响，不构成防重入合同。临时移除实际保护后，macOS 和 Linux
均测出最大深度五，测试失败；正式实现保留原保护。

回收检查在建立树并完成加载/卸载后，先完成该 dispatcher 的 CSS 评价，再触发
GC。定向激活 CSS 的引用追踪证实持有路径为
`CssEvaluationScheduler.s_states[CurrentDispatcher].SubtreeRoots → CssNode.Target`。
这批待执行工作完成后再判断 IME 订阅。临时增加强引用订阅时，遗漏 Unloaded
通知的检查仍失败；恢复实际弱订阅并强制重新编译后，活跃 CSS 的两项检查通过。
诊断和变异代码仅存在于隔离验证目录。

macOS 构建入口现在先构建 CMake 默认目标，再执行真正的 package-complete 目标，
由启用的 CMake 目标决定测试程序集合。导出检查与完整 CTest 入口继续执行。

| 检查 | 本轮结果及范围 |
| --- | --- |
| Linux 定向复现 | 本机 Docker 的 amd64 Ubuntu 20.04 容器使用 SHA-512 核对的 .NET SDK 10.0.300；相同 25 项从 **7 失败/17 通过/1 跳过** 到 **24 通过/1 跳过**。跳过项是启用实际 AppKit 几何的专用检查；没有以 Linux 跳过六项预编辑查询检查 |
| macOS 完整本地回归 | 隔离的最新 PR 源码和本轮改动，临时链接 DependencyProperty 与 IME 回收检查、启用原生几何：最终 **1701/1701**，无跳过。**961** 条上下文记录均为有效 Metal；首次完整运行暴露的两项回收失败已定位为待执行 CSS 工作，并完成上述隔离与变异验证 |
| Xcode 默认构建 | 新建隔离 Xcode 工程，开启平台及 Metal 测试、关闭运行时 HLSL 编译器：默认构建和完成目标均成功，全部 **21** 个已注册测试的程序存在，macOS 导出门通过。这一验证没有代替完整生产 shader 工具链及 Apple 切片构建 |
| 此前漏建的程序 | 实际 CTest **5/5**、无跳过：clipboard、macos.cursor、macos.text-font、screen-coordinate、metal.text-metrics |
| 变异检查 | 移除 coercion 保护：macOS/Linux 各 **1** 项预期失败，深度 **5**；增加强 IME 订阅：正常卸载通过、遗漏卸载失败。恢复源码后强制重编译，活跃 CSS 检查 **2/2** |

此前 v156 头的 [Apple CI](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38077148766)
已进入实际 CTest：**22** 项、**9** 通过、**13** 失败，其中上述五项未运行；其余
失败涉及前台激活、窗口无障碍/调整尺寸与 Metal 读回。当前本地五项通过只验收
漏建修复，其他失败及全部 Apple 发布切片继续保留。

同一头的 [Linux CI](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38077148783)
中，已完成的 musl x64 与 glibc arm64 主套件各 **5619** 项、**12** 失败、
**5532** 通过、**75** 跳过；失败包括本轮复现的七项断言，以及四项 Razor
虚拟化和一项滚动焦点裁剪。其余任务当时仍运行；这一 CI 运行没有包含本轮修改。

本轮改动为测试合同与构建入口，实际控件输入及绘制实现沿用既有版本。真实中文
候选窗、实体混合 DPI、多屏、VoiceOver 与完整发布验收仍开放。现有 master 和
PR 分支保持，用户的 CSS、Tooltip、输入分发、测试项目和 Gallery 工作保留。

### v156：Window 内容滚动的普通附加属性 Binding

普通 Binding 现在读取 `(ScrollViewer.CanContentScroll)`、单个 DependencyProperty
构造的 PropertyPath 以及 `(0)` 参数路径。使用注册的 owner 类型或显式属性对象
解析属性；根及中间 DependencyObject 按属性身份接收通知，源替换和解除绑定时
撤销旧订阅。附加属性后可继续通过已有 CLR/indexer 段，写回和 ConvertBack 使用
注册属性类型，IDataErrorInfo/INotifyDataErrorInfo 使用属性名接收及清除错误。
语法依据：[WPF PropertyPath 文档](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/propertypath-xaml-syntax)。

手动 `UpdateSource()` 可以提交 LostFocus/Explicit 绑定；控件自动通知走独立入口，
仍遵守原模式和触发器。Window 的原生尺寸反馈及 ListBox 的原子选择同步使用自动
通知入口，保留表达式。首次完整检查暴露的四项尺寸/SizeToContent 触发器回归已
修复，最终套件再次全部通过。

| 检查 | 本轮结果及范围 |
| --- | --- |
| 新增托管合同 | 相同 **26** 项：旧实现 **18 失败/8 通过**，最终 **26/26**。覆盖三种路径、双向触发器、转换、同名属性身份、嵌套/索引源替换、DataContext、解除订阅、非法路径及两种错误通知 |
| 完整本地回归 | 隔离的最新 PR 源码加本轮改动，临时链接九份已有 Binding/PropertyPath 测试，启用原生几何：**1741/1741**，无跳过；**961** 条记录均为有效 Metal 上下文。新用例在 macOS 项目中默认编译，同时链接到 Windows 测试项目；没有实际 Windows 宿主验收 |
| 最终 SDK | Native/Custom 标题栏 × 三种路径，**6/6**。窗口显示后检查 source/target 往返、提供者连接/断开、两种模式实际偏移、偏移重置、绑定保留及编辑/原生 responder 焦点 |
| 实际桌面 | 两种标题栏均查看普通及 **520×600** 截图；F6 三次、F7、Tab/Return、再切换后两种列表模式均滚动到 **180 DIP**。End/BackSpace 删除末尾 emoji，实际 Command-Z 恢复原值；完整 Tab 链到结束按钮后 Return 关闭 |
| 最小 Custom 布局 | 外层滚动条在较短内容区出现；Tab 可到达结束按钮，实际外层滚动后整个按钮和焦点边框可见 |
| 实际加载 | 两个桌面进程各加载自己包中的 **8** 个 Jalium 原生库。四个验收包的 **124** 个原生/运行时/资源/Jalium 程序集文件保持各自签名后指纹；最终 SDK 在校验通知修复后重新构建并执行，桌面观察覆盖未改变的滚动/布局路径 |

本地测试宿主仍使用安装的 macOS SDK **26.5**，在 Xcode **27.0** 下以
`ValidateXcodeVersion=false` 构建并实际运行；这不构成 Apple 发布包或最低系统版本
验收。可重建的宿主入口为 `--window-attached-binding`、
`--window-attached-binding-observe` 和 `--window-attached-binding-observe-custom`。

前一轮发布头 `f780d917` 的云端任务已结束：

- [Apple run 38073489333](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38073489333)
  失败于请求未启用的 `jalium.native.platform.tests`，已越过脚本权限问题。本轮
  macOS 构建明确设置 `JALIUM_PLATFORM_BUILD_TESTS=ON`，保留其他 slice 行为；实际
  隔离 Xcode 项目生成确认脚本所需八个 AppKit 测试目标及完成目标均存在。该生成
  检查关闭运行时 shader compiler，仅验证目标，完整 Apple CI 须在新头上另验。
- [Linux run 38073489360](https://github.com/VeryJokerJal/Jalium.UI/actions/runs/38073489360)
  的四种原生构建/导出门均进入后续测试；每种托管主套件 **5619** 项、**75** 跳过，
  glibc x64 **12** 失败、glibc arm64 **16** 失败、musl x64 **12** 失败、musl arm64
  **14** 失败。共同涉及 Razor 虚拟化、滚动焦点裁剪、IME 临时几何及重入 coercion；
  arm64 另有 IME 生命周期，glibc arm64 另有 Dispatcher。组合包任务跳过，Linux
  与完整发布验收仍开放。这些日志运行的是本轮 Binding 修复前的源码。

本轮普通附加路径的上述合同已验收；其他 Binding 模式/语法、ComboBox/TreeSelector
实际弹出列表、实体多屏/混合 DPI、VoiceOver、最低系统版本及签名发布保持独立缺项。
现有 master 和 PR 分支保持，用户的 CSS、Tooltip、输入分发、测试项目和 Gallery
工作不进入本轮提交。

验收后清理 **1** 个 artifacts 根、**9,539** 个文件/链接、**2,891,610,082**
逻辑字节；两个仓库不区分大小写的字面 artifacts 路径、本轮应用进程、外部临时
产物及已归属诊断均为 **0**。**18,994** 个受保护文件/链接、**1,690** 个目录、
**508** 个原生输入、正常 Debug **27** 个输出及 Gallery **14** 个既有缺失路径
在验收和清理前后保持一致；完整 `.tools` 保留。提交后只由真正的 CMake 完成目标
更新正常负载的提交标记。

### v155：Apple 构建入口与平台导出合同

v154 发布后的云端检查未进入完整验收：Apple job 在第一条 native 构建命令
因脚本权限为 644 返回 Permission denied；四个 Linux job 在真实 ELF 构建后
要求 **14** 个 Apple Window/drag/AX 专用符号，停止于导出检查。前一 PR 提交
`012101d9` 的 Apple/glibc-x64 日志也有相同错误，不能将这些 job 记为通过。

Apple 工作流现以 `bash` 调用七个 native slice 入口和 XCFramework 打包入口，
无需改变脚本或工作树权限。Linux 导出扫描从公共声明和托管 P/Invoke 中排除
`jalium_apple_`、`jalium_android_` 专用命名空间；此前平台名单只有四个早期
Apple 接口，后续 Window 接口被错误加入 Linux 必需导出。该排除不作用于
实际 ELF 符号，因此 Linux DSO 若真的额外导出 Apple/Android 符号仍会失败。
公共 Linux ABI、托管独有 import、非白名单导出和跨库引用检查保持严格。

新增标准库 Python 回归，由 glibc/musl 两个 CI job 在构建前执行。相同 **13**
项在旧检查器为 **7 失败、6 通过**，最终 **13/13**。覆盖导入别名与 EntryPoint、
未来平台接口、名称仅含 apple 的公共接口、缺失公共/托管导出、新公共声明、
额外 Apple/Android/其他导出、缺失库、跨库提供方及 readelf 的 global/weak/UND
解析。测试使用模拟导出集合及 readelf 文本，不替代真实 Linux ELF 运行。

两个完整 workflow 经 YAML 解析；Apple 的两个 shell run 块和对应脚本语法
检查通过。以 bash 调用真实的 644 权限 build-native.sh，非法目标返回其预期
参数错误（exit 2），已进入脚本；这项探针不表示七个发布 slice 全部构建通过。
现有普通 Debug 负载又通过未修改的 macOS core/platform/Metal 导出检查，仍
要求 Apple Window 接口。原生源码和负载未改动，没有新 UI、输入或窗口实测；
v154 的实际两种标题栏记录保持其原范围。本轮云端构建须在新提交上另行确认。

可重建检查：

```text
python3 eng/linux/test-native-exports.py
python3 eng/apple/check-native-exports.py src/native/bin/native/osx-arm64/Debug --configuration Debug
```

本轮沿用当前 master 和现有 PR 分支，保留此前并发的 16 个文件；用户的 CSS、
Tooltip、输入分发、测试项目及 Gallery 修改不进入提交。完整 Window/macOS
目标及实体多屏、VoiceOver、最低系统版本、Intel 和发布验收仍未完成。

清理 **1** 个 artifacts 根、**42** 个文件/链接、**53,323,352** 逻辑字节；两个
仓库的字面 artifacts 路径不区分大小写均为 **0**。**18,998** 个受保护文件/
链接、**1,690** 个目录、**508** 个原生输入、正常 Debug **27** 个输出及 Gallery
**14** 个既有缺失路径在测试和清理前后保持一致。没有新观察进程、临时应用或
外部诊断。提交后由真正的 CMake 完成目标仅刷新本地 master 的原生提交标记。

### v154：窗口位图的 DPI、裁剪与旋转

v153 集成回归留下的 144/192 DPI 焦点裁剪失败，来自软件绘图上下文未采用
目标位图的 DPI。缓冲区按像素分配，视觉树的布局、偏移和裁剪仍以 DIP 绘制，
因此高 DPI 位图的内容和蒙版只占部分缓冲区。现在在上下文的基础矩阵中应用
`DpiX/96`、`DpiY/96`，嵌套变换与 Pop 保留这个基础比例；不再分别放大偏移或
蒙版。[DIP 与像素换算](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/dpi-and-dips)
也适用于这里的目标位图坐标。非正数、NaN 和正无穷 DPI 统一回退到 96，避免
无穷矩阵。实际 GPU 窗口的显示 DPI 没有被这次合成目标 DPI 检查替代。

本轮以 PR 集成提交 `012101d9` 为验证源码，叠加下列两项生产修改及对应测试；
此前并发的三个修复全部保留。本地 master 保持原提交链，用户的 CSS、Tooltip、
输入分发、测试项目和 Gallery 工作树不参加本轮提交。

| 检查 | 本轮结果 |
| --- | --- |
| 修复前后同组 | 相同 **34** 项在旧媒体实现为 **18 失败、16 通过**，修复后 **34/34**；包含此前失败的 144/192 DPI 焦点裁剪。扩大后的针对性检查 **53/53** |
| 新增位图合同 | **21** 项覆盖 48/96/120/144/192 DPI、横纵不同 DPI、嵌套 arrange 偏移、圆角和非对称内孔、90° 旋转、清空后重复渲染、变换和裁剪 Pop 的比例恢复及无效 DPI。非对称孔洞从右上转到右下，避免旋转对称样例漏检 |
| 完整原生几何与托管合同 | **1676/1676**，零跳过，36 秒；**961** 个上下文全部为有效 Metal。另有依赖主题的共享滑块快照 **1/1** 在独立 XAML 工程通过，旧实现同项也是 **1/1**。这是两组通过结果，不记作一次 1677 项全量通过 |
| SDK 窗口入口 | 两种标题栏 × 两种旋转 × 三个目标 DPI，共 **12/12**，退出 0；每个公开 Render 路径核对五个 alpha 采样及编辑焦点。导出并查看 **12** 张位图，分别为 **120×120、180×180、240×240** 像素 |
| 构建及实际载入 | 最终 SDK 构建 **0 错误、18 项既有警告、15.26 秒**。临时包严格签名通过；**16** 份 Jalium dylib 复制的可载入段、**3** 份资源及 **10** 份 SDK 程序集一致。最终 Native/Custom 两个 PID 分别从自身 MonoBundle 加载全部 **8** 个 Jalium dylib，无 DYLD 覆盖；原生输入没有改动 |
| 实际截图与布局 | 两种标题栏均查看正常及最小尺寸，最小设置 **520×620 DIP**；三个预览保持相同 **120 DIP** 显示尺寸，圆角外和内孔显示容器背景。两次方向往返和第三次旋转后，内孔仍在正确位置；说明文字换行，编辑框、三个操作按钮和焦点轮廓均可见 |
| 真实工具键盘与 AX | Native/Custom 均通过 Tab 定位、Return 操作、F6 切换和编辑焦点恢复，各有 **3** 次旋转更新。AX 包含三个 DPI 预览的名称及编辑框/按钮；Native 输入 a 后 ⌘Z 恢复起始内容。Custom 取消预编辑后，已提交文本的 ⌘Z 也恢复起始内容，截图与 AX 一致。结束按钮经 Tab/Return 关闭，各应用及保活进程退出 0 |

验证工程最初只引用统一托管程序集，临时连接共享软件位图测试时遗漏了 XAML
依赖；一次运行 **1676 通过、1 失败**，失败在滑块主题资源断言。旧、新实现
单独运行同项均失败，补齐两个隔离工程的依赖后均通过。把依赖主题的用例与
整套 xUnit 工作线程一起运行，另一次在 **1637** 项部分通过后因 AppKit
`nextEventMatchingMask should only be called from the Main Thread` 中止，不能
作为通过记录。最终 **1676** 项使用原有原生几何测试配置执行，主题快照独立
执行；实际窗口由主线程 SDK 宿主验收。上述验证配置修正没有写入用户的测试项目。

Custom 的字符检查先出现未提交的预编辑文字，AX 仍为已提交的原值；Escape
取消后 Tab 导航恢复。工具指定粘贴的短文本与实际插入内容不一致，因此该动作
只用于核对已提交文本撤销，不作为指定内容粘贴验收，也不重新评价 v151 的
剪贴板专项。完整 IME 预编辑投影和物理键盘验收继续开放。

可重建入口：

```text
Jalium.UI.MacOS.HostSmoke --window-bitmap-dpi
Jalium.UI.MacOS.HostSmoke --window-bitmap-dpi-observe
Jalium.UI.MacOS.HostSmoke --window-bitmap-dpi-observe-custom
```

这次修复补齐软件目标位图的 DPI，实测窗口仍使用当前屏幕的显示比例。实体
高 DPI / 混合 DPI 多屏、VoiceOver、macOS 15/Intel、Spaces 和签名发布尚未
验收；普通附加属性 Binding、ComboBox/TreeSelector 弹出列表及其他 Window
待验收范围继续保留。Window 和全部 macOS 行为的目标仍未完成。

验收结束清理 **1** 个 artifacts 根及 **1** 份归属本轮 xUnit 中止的外部诊断，
合计 **9,372** 个文件/链接、**2,832,827,184** 逻辑字节。两个工作树的字面
artifacts 路径不区分大小写均为 **0**，观察及保活进程退出。**18,996** 个
受保护文件/链接、**1,690** 个目录、**508** 个原生输入及正常 Debug **27** 个
输出在测试和清理前后保持一致，Gallery **14** 个既有缺失路径也保持不变。
提交后使用真正的 CMake 完成目标只刷新原生提交标记，其余 **26** 项保持一致。
临时包、位图和日志已移除，上述宿主入口用于重建，不把删除的路径作为交付物。

### v153：内容滚动合同与无障碍滚动条

直接以 StackPanel 等 IScrollInfo 作为内容时，旧 ScrollViewer 不检查
`CanContentScroll=false`，仍使用内容 provider；固定宽度列表的横向偏移因此
不能移动实际内容。现在只在属性为 true 时连接 provider，false 使用物理偏移。
默认 false 与 [CanContentScroll 合同](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.scrollviewer.cancontentscroll?view=windowsdesktop-10.0) 一致。
运行中切换先取消弹性、平滑目标和末端锚点，再释放旧 owner、选择新 provider
并重新布局。不同 provider 的单位可能不同，切换时明确回到双轴起点。
Disabled 轴的直接滚动和运行中禁用都归零，不保留不可用的实际偏移。

ListBox、ListView、ComboBox、TreeView 和 TreeSelector 默认样式明确启用内容
滚动；内部 viewer 通过 `{TemplateBinding ScrollViewer.CanContentScroll}`
跟随控件上的附加属性，调用者仍可关闭它。TemplateBinding 现在从注册的
XAML owner 类型解析带 owner 的源属性，保留 AOT 路径。普通单段附加属性
Binding 的解析问题另行保留，不将此 TemplateBinding 修复扩展为其验收。

实际桌面还复现了另一问题：AX 把横向滑块值设置为 400 后，滑块移动，
但列表仍显示首列，内容偏移是 0。ScrollBarAutomationPeer 原先仅设置 Value；
现在所属 ScrollViewer 负责提交对应轴的内容偏移，独立 ScrollBar 保留数值
强制转换且不生成 Scroll 命令，禁用控件拒绝动作。该所属窗口路由参考
[WPF ScrollBarAutomationPeer](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Automation/Peers/ScrollBarAutomationPeer.cs)。
自动化范围设置立即提交内容，不按延迟拖动滑块处理；焦点和另一轴保持不变。

| 检查 | 本轮结果 |
| --- | --- |
| 修复前后托管回归 | provider 合同的相同 20 项测试在旧实现为 **16 失败、4 通过**；新增 AX 的相同 5 项在旧 peer 为 **5 失败、0 通过**。最终 **42/42**，包含 25 项合同/AX、5 项模板虚拟化及 12 项 BringIntoView。覆盖设置顺序、默认值、绑定更新、六次模式往返、内容更换/移除、双轴禁用及恢复、实际子元素几何、延迟拖动策略和独立滚动条 |
| 本地原提交链的完整回归 | `07866237` 加本轮源码，启用原生几何为 **1582/1582**，零跳过；**960** 个上下文全部为有效 Metal。CSS、Tooltip、输入分发和用户测试项目修改未混入 |
| 最新 PR 集成 | 发布前发现 PR 已加入 `e51e4e83`、`6d006067`、`7ecbdcfd` 的三个修复，涉及 **16** 个文件，与本轮 **10** 个文件无交集。隔离叠加后的针对性回归 **75/75**；完整回归 **1648/1650**，零跳过，**961** 个上下文全部为有效 Metal。两项失败为 FocusVisualAncestorClipTests 的 144/192 DPI 软件像素检查；未包含本轮改动的 `7ecbdcfd` 基线单独运行同组为 **1 通过、2 失败**，96 DPI 通过。集成结果不记为全绿 |
| 最终 SDK 协议 | 最新 PR 集成源码的直接内容入口 **22/22**；既有包装内容入口 **16/16**。两种标题栏均经过真实 NSView 的受控 NSEvent、原生回调和 ABI，物理双轴各 **48 DIP**，extent **1004×1096**、viewport **340×300**；再检查模式往返和 AX 提交 **96×84 DIP**。额外六项实际默认 ListBox/ListView/TreeView 模板检查通过，附加属性能关闭并恢复 provider，列表保留虚拟化 |
| 构建与载入 | 最终集成 SDK **0 错误、28 项既有警告、68.67 秒**。临时包严格签名通过，**16** 份 Jalium 原生复制的可载入段、**3** 份资源和 **10** 份 SDK 程序集一致。最终 Native、Custom 进程均从自身 MonoBundle 加载全部 **8** 个 Jalium dylib，没有 DYLD 覆盖。本轮和并发三项修复均没有原生输入改动 |
| 实际截图与键盘 | 最终集成 Native/Custom 都在 **520×590 DIP 客户区**检查；截图分别 **520×622**（含原生标题栏）与 **520×590**。按钮和编辑框可见，Tab 定位按钮、Return 打开列表。AX 设置横向 **400**、纵向 **120** 后实际文字横移，偏移和编辑焦点一致；F6 两次切换回到起点。桌面纵向滚动显示 Native 第 18 项、Custom 第 17 项，偏移分别约 **622/590 DIP** |
| 独立列表与收尾 | 两种标题栏分别完成独立列表 **两次打开、两次桌面滚动**，模式往返、Escape/关闭按钮返回编辑、输入 a 与 ⌘Z 恢复原文。每种最终集成观察记录 **3 打开/3 关闭**，closed=True、failed=False；观察和保活进程退出 0。独立列表未被工具纳入截图/AX 清单，其滚动由所属窗口反馈和真实回调核对，不算独立窗口像素/完整无障碍树验收 |

最初的非集成窗口 AX 操作只移动滑块，已作为失败证据保留在本节；后来来源
不明的滚轮事件不算该操作通过。最终独立构建和最新 PR 集成构建均另行完成
两种标题栏的上述完整实测。一次全量运行使用相对 DYLD 路径，导致测试进程
找不到原生库；改用绝对路径的完整结果才作为本轮验收。

桌面横向 scroll 仍返回原生零增量，没有验证物理横向滚轮；AX 内容横移和
受控 NSEvent 的双轴检查不代替真实滚轮或持键输入。ComboBox/TreeSelector
样式参与构建，但其实际弹出列表尚未验收；普通附加属性 Binding、VoiceOver、
多屏/混合 DPI/Spaces、macOS 15/Intel 和签名发布继续开放。最新 PR 的两项
软件 DPI 基线失败在当轮继续开放，现已由 v154 修复；仍不表示 Window 或全部 macOS 行为完成。

可重建入口：

```text
Jalium.UI.MacOS.HostSmoke --window-content-scroll
Jalium.UI.MacOS.HostSmoke --window-content-scroll-observe
Jalium.UI.MacOS.HostSmoke --window-content-scroll-observe-custom
```

两阶段验收后清理（为最新 PR 集成重建了一次），合计移除 **14,727** 个文件/
链接、**5,322,473,886** 逻辑字节。两个仓库最终不区分大小写的字面 artifacts
路径均为 **0**，本轮观察/保活进程退出，没有匹配本轮包标识的外部偏好、缓存
或诊断遗留。**18,990** 个受保护文件/链接、**1,690** 个目录、**508** 个原生
输入保持一致；完整工具链、用户未提交改动、Gallery 的 **14** 个既有缺失路径
保留。正常 Debug 的 **27** 个输出在测试和清理时不变；提交后用真正的 CMake
完成目标刷新本地 master 的提交标记，其余 **26** 项保持一致。本地 master
仅提交本轮文件；既有 PR 分支保留并发三个修复，再合并本轮提交，不切换分支。

### v152：独立弹窗滚轮的按键快照

`PopupWindow` 原先仅保留自己收到的按下/释放历史。当按键在所属窗口或其他
位置变化时，弹窗收到的滚轮包虽然已经带有原生五键快照，路由事件仍可能
报告旧状态。现在带 `HasMouseButtonStates` 的滚轮包先替换本地按键缓存，
包括明确的全释放快照；缺少标记的旧包继续沿用本地历史，未知高位忽略。
预览、冒泡、PointerWheelChanged 和全局 Mouse 状态使用同一快照，后续移动
及旧滚轮包也能读取更新后的缓存。修复不生成额外 MouseUp，不改变捕获、
焦点、横纵增量、精细滚动、修饰键与手势阶段。

macOS 普通 `Popup` 目前仍在所属窗口的 overlay 中显示；本轮 SDK fixture
明确创建已有的独立 `PopupWindow` 桥接，覆盖该路径，没有启用新的默认弹窗
策略。真实窗口内列表的检查用于保留原有路径的行为，不能将它算作独立
弹窗桥接修复的证明。

| 检查 | 本轮结果 |
| --- | --- |
| 托管针对性回归 | **26/26**；相同最终测试在旧实现为 **16 失败、10 通过**。覆盖五键、全释放、全部按下、未知高位、2× 坐标与捕获、预览/冒泡/Pointer/全局一致性、旧包回退、后续缓存、Handled/Cancel，以及销毁后的晚到事件与重建 |
| 完整托管回归 | 隔离提交源码、启用原生几何的 **1552/1552**，零跳过；**960** 个记录上下文全部为有效 Metal。用户尚未提交的 CSS、Tooltip 和输入分发改动未混入验收源码 |
| SDK 原生协议 | 两种所属窗口标题栏共 **16/16**。NSEvent 发给宿主自己的真实 NSView，经原生回调及平台 ABI 到弹窗路由，检查 AppKit 按键快照、精细/粗滚动、双轴增量和阶段、预览抑制、原生隐藏/销毁/重建与编辑焦点；五键同时按下另由明确的 ABI 包检查。物理 ScrollViewer 双轴偏移均为 **48 DIP**，extent **1004×1096**、viewport **340×300** |
| 构建与载入 | 最终 SDK 构建 **0 错误、18 项既有警告、24.57 秒**。所有临时运行库独立签名后通过严格检查；**16** 份 Jalium 原生复制的可载入段、**3** 份资源和 **10** 份 SDK 程序集一致。实际最终 Custom 进程从自身 MonoBundle 加载全部 **8** 个 Jalium dylib，没有 DYLD 覆盖 |
| 实际桌面交互 | 最终应用的 Native 与 Custom 均在 **520×540 DIP** 查看所属窗口及窗口内列表截图，操作区和 Tab 焦点可见；Return 打开列表、桌面纵向滚动、Escape 返回编辑已观察。独立列表各完成 **2** 次打开和桌面滚动，偏移分别为 Native **572 DIP**、Custom **540 DIP**；Escape 或关闭按钮返回编辑后输入 a，再用 ⌘Z 恢复原文。完整收尾记录为 Native **2 打开/2 关闭**、Custom **3 打开/3 关闭**，closed=True、failed=False，观察进程和保活进程均退出 |

Native 首次观察已核实窗口内列表的滚动和最小布局，但进程在收尾前退出 0，
没有 COMPLETE 记录；其来源未明的独立列表事件不计入实际操作验收。
随后同一最终应用的第二次 Native 观察完成独立列表的两次滚动、重开、编辑
和明确收尾。Custom 的全部上述操作在一次完整观察中完成。所有实际滚轮
记录的五键状态均为全释放；NSEvent/ABI 检查不替代真实按住鼠标键滚动。

验收仍有以下明确缺项：桌面工具的横向 scroll 返回原生零增量，未证明实际
横向滚轮；设置 AX 横向滚动条值或尝试拖动未建立内容横移的证据，原因仍需
调查。独立弹窗没有被桌面工具纳入截图/AX 窗口清单，其纵向操作由所属窗口
反馈和真实原生回调核对，不能声称已查看其实际像素或完整无障碍树。

本轮还发现独立的 `ScrollViewer` 合同问题：直接以 StackPanel 等 IScrollInfo
作为内容时，OnContentChanged 不检查 `CanContentScroll=false`，仍选择逻辑
provider，横向偏移因此没有移动。初始原生 fixture 在两种标题栏各失败一次
（合计 **14/16**）。最终滚轮 fixture 使用不实现 IScrollInfo 的 Border 包装，
隔离本轮按键输入路径；没有修复或宣称通过 provider 选择合同，后续需要单独
回归并修复。默认 macOS overlay 策略、真实持键滚动、VoiceOver、混合 DPI、
多屏/Spaces、macOS 15/Intel 和签名发布继续保持开放。

复现入口：

```text
Jalium.UI.MacOS.HostSmoke --window-popup-wheel
Jalium.UI.MacOS.HostSmoke --window-popup-wheel-observe
Jalium.UI.MacOS.HostSmoke --window-popup-wheel-observe-custom
```

观察窗口最长等待 15 分钟；按“结束检查”关闭。临时包和日志按用户要求清理，
上述路径是源码中的可重建入口，不是保留的交付应用。

本轮清理删除 **2** 个 artifacts 根、**8,744** 个文件/链接、
**4,153,015,692** 逻辑字节；没有匹配本轮独立包标识的外部偏好、缓存或
诊断需要删除。两个仓库不区分大小写的字面 artifacts 路径均为 **0**，
本轮观察/保活进程均已退出。清理前后 **18,994** 个受保护文件/链接、
**1,690** 个目录及 **508** 个原生输入完全一致，Gallery 的 **14** 个既有
缺失路径保持缺失；完整 .tools、正常原生输出与用户未提交改动保留。
本轮没有原生实现改动，正常 Debug 的 **27** 个输出在验收/清理期间保持
不变；提交后通过真正的 CMake 完成目标刷新提交标记，其余输出仍逐项核对。

### v151：文件剪贴板的原生项目与完整批次

原先 `jalium_clipboard_set_data` 把整个 URI 列表作为一个 `public.url`
写入首项；托管读取可以拆分字符串，但原生 AppKit 接收者只能看到一个项目。
现在剪贴板和拖放共用私有的项目构建器，为每个绝对 URL 建立独立
`NSPasteboardItem`，文件 URL 同时提供 `public.file-url`。
文本、HTML 和其他二进制格式保留在首项，避免多文件粘贴时重复附带文本。
实现使用 AppKit 的 [writeObjects](https://developer.apple.com/documentation/appkit/nspasteboardwriting)
项目协议，保持现有公开 ABI 与托管数据对象的所有权语义。

批次保留顺序、重复项及目录尾斜杠；中文和特殊字符由 NSURL 规范化，
再从 URI 列表解码回原路径。跳过注释、空行和没有 scheme 的相对项；
支持 UTF-8 BOM。URI 行逐行枚举，避免大量空行产生额外的拆分数组。
无效 MIME、缺失指针、非法文本 UTF-8、超限表示及没有有效 URL 的
纯 URL 写入，在取得剪贴板所有权前拒绝。每份表示上限为 **256 MiB**，
原生项目上限为 **65,536**；读取完整 URI 批次时也检查累计字节与项目数，
在拼接或跨 ABI 分配前停止超限数据。

| 检查 | 本轮结果 |
| --- | --- |
| 原生私有剪贴板 | 新 CMake 目标 `jalium.native.platform.clipboard.tests`，**47** 项断言通过。使用 AppKit 的 NSURL 读取检查多文件、Unicode、目录、重复项、混合网页/文件、首项附带格式、空文本、清空、BOM、100,000 个空行，以及上下限和无效写入后原数据保留 |
| 公开 API 对比 | 相同最终 SDK 宿主使用旧平台库为 **3/6**，三种文件批次写入均因原生项目数量错误失败；使用最终平台库为 **6/6**，覆盖 SetFileDropList、附带格式、重复批次、原生来源导入混合 URL、文本与清空 |
| 托管回归 | 新增 **13** 项共享 URI 编码/解码和拖放快照边界，完整原生启用回归 **1526/1526**、零跳过。最终 **960** 个上下文均为有效 Metal |
| 原有原生回归 | Window 拖放生命周期 **152/152**；滚动条与编辑动作通过，词导航 **924** 项原生参照及 **200** 项工作线程查询通过 |
| 原有 SDK 回归 | 私有剪贴板驱动的拖放表示专项 **12/12**，保留旧多行单项、网页、文件、空值与 Drop 后快照行为 |
| SDK 构建与载入 | 初次构建 **0 错误、28 警告、79.65 秒**，最后宿主增量构建 **0 错误、18 警告、11.17 秒**。临时应用逐个签名所有运行库，严格签名检查通过；最终 **16** 份 Jalium 库复制的可载入段、**3** 份 Metal 资源及 **10** 份 SDK 托管程序集核对通过。lsof 确认实际最终应用从自身 MonoBundle 载入全部 **8** 个 Jalium dylib，没有 DYLD 覆盖 |
| 实际交互 | 最终 Native 与 Custom 各 **2** 次复制、**2** 次粘贴和 **1** 次实际拖放，均保留四项文件及附带 HTML。实际 ⌘C、⌘V、Return、Tab/Shift+Tab、继续输入与 ⌘Z 已观察；两个窗口均在 **520×560 DIP** 检查实际截图、可见焦点和完整操作区 |
| 原剪贴板 | 原生公开专项与每个观察窗口均在内存保存全部原项目、格式和字节；结束时逐项核对完整恢复。不记录原内容；外部改写时保留外部内容。独立原生与 SDK 拖放表示回归使用私有剪贴板 |
| 清理 | 本轮全部测试 artifacts、临时应用、fixture、保活进程，以及可证明归属本轮的偏好和诊断已清理；保留 .tools、正常原生构建输出、用户改动与 Gallery 基线 |

初次临时打包遗漏了 .NET 运行库的独立签名，系统在运行剪贴板检查前以
Invalid Page 终止进程；补签全部 dylib 后正常启动。这是本轮临时验收打包
问题，不作为发布签名验收。自定义标题栏的一次坐标拖放被桌面工具报告
用户改变应用而中断；重新读取状态并 Raise 后，重试得到明确的 Drop 与
Copy 返回。普通窗口的一次边缘拖动没有改变尺寸；最小尺寸检查使用明确的
宿主按钮与实际截图，不计为原生边缘缩放通过。

实际拖放在同一应用内执行，原生项目编码另由 AppKit 的私有剪贴板检查；
不替代 Finder、其他进程或签名沙箱应用的互传验收。临时应用的 NSURL 读取
仍输出 sandbox_extension_consume 提示；本轮所有数量、路径与附带格式断言
通过，但未验证沙箱接收应用对文件内容的访问授权。真实中文候选窗、VoiceOver、
混合 DPI、多屏与 Spaces、macOS 15/Intel、签名发布仍保持开放。

复现入口：

```text
Jalium.UI.MacOS.HostSmoke --window-clipboard-files
Jalium.UI.MacOS.HostSmoke --window-clipboard-files-observe
Jalium.UI.MacOS.HostSmoke --window-clipboard-files-observe-custom
```

设置 `JALIUM_CLIPBOARD_FIXTURE_ROOT` 为专用验证目录；该目录内创建测试文件。
普通公开专项会恢复原剪贴板，观察窗口最长运行 15 分钟并在结束时恢复。
大型或含文件承诺的现有剪贴板拒绝进入通用剪贴板测试，不读取承诺内容。


### v150：消息框的响应、关闭与长文本

框架消息框现在遵循 [MessageBox 的按键与关闭规则](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-open-message-box)：
只有 OK 时，Escape 返回 OK；包含 Cancel 时，Escape 与窗口关闭返回 Cancel；
Yes/No 必须明确选择，不提供关闭按钮，普通关闭请求会被拒绝。
所属窗口已经接受关闭时仍允许完成清理，Yes/No 的强制结束回退为 No，
避免阻塞所属窗口的退出，也不会把关闭解释为 Yes。按钮触发的模态关闭
被 Closing 取消后，不再立即重复请求关闭。

指定的默认按钮在 Loaded 后获得键盘焦点；无效默认结果继续回退到该组合的
第一个按钮。Tab、Shift+Tab 可改变响应焦点，Return 激活当前焦点按钮。
短消息不增加消息区域的空 Tab 停靠点；长消息保留键盘滚动路径。

消息与图标使用受宽度约束的 Grid。消息区上限为主屏工作区高度的 60%，
超过后垂直滚动，响应按钮仍在消息区下方。RightAlign 与 RtlReading 分别
应用于消息的对齐和阅读方向，继续保留现有标题栏、字体、图标与按钮样式。

此次验收也发现并修复原生标题栏遗漏：IsShowCloseButton、
IsShowMinimizeButton 与 IsShowMaximizeButton 现在参与原生窗口创建配置，
在 macOS 上更改这些属性会同步原生按钮能力。隐藏最大化按钮不取消窗口
的边缘缩放能力；隐藏关闭按钮不阻止已授权的程序关闭。

本轮在 macOS 27.0.1、Apple Silicon、一个真实 1× 屏幕验证：

- 修复前隔离应用的 OK Escape 没有响应，Yes/No 可被关闭，对齐与方向选项
  被忽略；默认 Cancel 的 Return 原本通过。实际长消息窗口超出屏幕，
  底部响应按钮不可见。此前台 OK Escape 复现同时记录 AppKit active=true。
- 最终原生启用的托管回归 **1513/1513**，其中新增消息框检查 **38/38**；
  本次 Metal 上下文记录 960 条，全部有效，其中消息框检查对应 38 条。
  覆盖全部按钮组合与有效/无效默认值、隐藏 Cancel、正常与被取消的关闭、
  所属窗口清理、短消息 Tab 循环、对齐/方向及长文本可滚动性。
- SDK 原生消息框检查 **23/23**（26.22 秒）：使用所属原生视图的 NSEvent
  路由、按钮与关闭协议，覆盖响应结果、明确选择限制、所属关闭/取消关闭、
  长消息 PageDown、主队列入口。另检查八种实时原生标题栏按钮配置，
  均保持客户区尺寸、托管焦点与原生 firstResponder；隐藏关闭按钮后
  程序关闭仍成功。协议注入不替代下面的真实按键验收。
- CUA 实际窗口完成九次操作：OK Escape→OK，OKCancel Return→Cancel，
  Yes/No Escape 与 Command-W 保留对话框，Tab/Shift+Tab 后 Space→No，
  另一次 Tab 到 Yes 后 Return→Yes；YesNoCancel 默认 No 的 Return→No，
  Escape→Cancel；OKCancel 标题栏关闭→Cancel。
- 长消息截图显示底部 OK/Cancel 均可见。Shift+Tab 与 PageDown 把滚动条
  从 0 移到 792，再移到 2524.14453125；截图读到第 100 行及
  “最后一行：完整可读”。从滚动区域 Escape→Cancel。无障碍树能读取
  消息、响应按钮及可写滚动条；此路径的 AX 焦点报告 splitter，
  尚未据此宣称 VoiceOver 阅读顺序或焦点语义通过。
- RightAlign|RtlReading 的实际截图核对了阿拉伯文与中文行的右边缘。
  短消息实际 Tab 在 OK/Cancel 之间循环。每次返回所属窗口仍 enabled，
  最终实际输入 a 后 Command-Z 恢复原文。结束记录 actions=9、closed=true，
  应用与保持唤醒进程都已退出。
- 最终 SDK 应用严格验证临时签名；16 份原生库的可加载节、3 个 Metal
  资源及 10 个 SDK 托管程序集与本轮输入一致。运行时八个原生库实际
  来自该应用的 MonoBundle，没有 DYLD 覆盖；原生库沿用 v148/v149
  已核对的 Debug 负载，本轮没有 C++ 修改。

可通过 HostSmoke 的 --window-message-box 运行协议检查，
--window-message-box-observe 打开交互验收窗口。测试源与入口已保留，
临时隔离应用、日志、快照与辅助程序按用户要求清理。

本轮清理删除 5594 个文件/链接、2,490,193,972 个逻辑字节；UI 与 Gallery
的 artifacts 路径均为零。19000 个受保护文件、1690 个目录、506 个原生
输入及提交前的 12 个正常 Debug 负载文件保持一致；没有匹配本轮独有
应用标识及创建时间的外部偏好、缓存或崩溃报告需要删除。

本轮限制：CUA 一次坐标点击报告 noWindowsAvailable，之后使用可读出的
实际按钮完成操作；原生 Window 菜单确认为 Close disabled，但 Escape
没有可靠结束菜单跟踪，最后使用其公开 Cancel 无障碍动作结束，
这次不记为菜单键盘退出通过。长文本高度依据主屏工作区，
跨不同尺寸/DPI 屏幕的消息框布局仍待实际验证；VoiceOver、物理应用切换、
嵌套消息框、Windows 专有桌面选项及 Windows/Linux 运行行为不在此次验收结论内。
完整 macOS 行为目标仍在继续。

### v149：文件面板的选项可见性与原生 sheet 会话

带文件类型、快捷位置或只读选择的打开/文件夹面板现在默认展开选项区，
使用公开的 `NSOpenPanel.accessoryViewDisclosed` 配置；当前 SDK 没有该属性
绑定，调用公开 Objective-C selector。用户仍可点 Hide Options 折叠。
目录切换创建新面板时再次显示选项，避免原来的 Show Options 状态让
“位置快捷方式”与 inside 消失。保存面板继续显示原生附件控件。

所属面板使用 [NSSavePanel 的文件面板 sheet 接口](https://developer.apple.com/documentation/appkit/nssavepanel/beginsheetmodal(for:completionhandler:))
启动会话，由完成回调返回响应并结束 AppKit 模态循环，保留同步 ShowDialog。
不再用普通 NSWindow 的 beginSheet 与文件面板 runModal 同时启动呈现。
主队列入口仍使用原生模态循环，避免回到 v145 已失败的嵌套托管等待方式；
所属窗口关闭、确认异常、导航取消与最终 OrderOut 清理继续走既有路径。

本轮在 macOS 27.0.1、Apple Silicon、一个真实 1× 屏幕验证：

- 修复前的隔离 SDK 应用实际显示 Show Options；展开后通过真实位置菜单
  切换到 Root，新面板再次折叠，AX 树中的三个附件控件消失。此次基线复现
  不作为前台键盘恢复的失败判据，初始应用处于 inactive 状态。
- 独立 AppKit 比较确认文件面板专用会话配合原生模态循环，在普通与主队列
  入口都收到两次取消完成回调；普通窗口挂接的比较没有收到回调。修正夹具
  NSWindow 默认 releasedWhenClosed 导致的重复释放后，三种原生循环的
  六项比较均正常退出；夹具释放错误不计为产品崩溃。
- 最终 SDK 构建 **0 错误、18 项既有警告、13.85 秒**。严格临时签名、
  **16** 份原生库可加载节、**3** 份资源和 **10** 份 linked SDK DLL
  核对通过。观察进程实际加载本包八份 Jalium 库和两份 Metal 资源，没有
  DYLD 覆盖。原生库沿用已核对的 v148 Debug 负载，本轮没有 C++ 修改。
- owner/确认集合 **18/18、33.42 秒**，增加直接 AppKit 主队列的打开、
  保存、确认异常与关闭 owner 后的异常四项，保留托管 dispatcher 主队列
  入口的三项。最终面板不再挂接或可见，取消不改变公开文件名和筛选索引。
  选项/目录集合 **31/31、51.73 秒**，同时检查存在附件时的公开展开配置；
  筛选/文件名/取消集合 **15/15、21.82 秒**。这 64 项原生协议检查不替代
  下述实际按键和截图。
- 最终打开面板初次显示就有文件类型、位置快捷方式与只读选择，AX 报告
  Hide Options。inside → Root → inside 后三项仍在，只读 True 保留；
  实际截图确认附件完整可见。取消后恢复 `original.txt` 和只读 False。
- 应用通过原生 Window 菜单取得前台后，保存从 Root → inside 创建新面板，
  原生记录确认导航前后 active=True、panelKey=True；此段导航后没有再次
  Raise 或激活。Return 接受 `inside/切换后保存🙂.txt`，内容核对为
  `owned option save`。Unicode 名称经 AX 设置，不是中文候选窗输入证据。
- 前台打开面板选择 chosen.txt、只读 True，真实 Open 触发一次 FileOk
  拒绝。随后 Root → inside 仍保留附件及只读 True；无需再次置前，Escape
  返回取消并恢复原文件名与只读 False。文件夹面板 Root → inside 后，
  Return 接受当前 inside 目录，两条路径均恢复主窗口编辑焦点。
- 返回后的 Tab 到打开按钮，Shift+Tab 回编辑框；英文 a 输入及 Command-Z
  撤销后文本恢复原值。实际从 **720 × 672 DIP** 对角缩窗到
  **600 × 592 DIP**，与最小内容 **600 × 560** 加原生标题栏一致。
  全部五个操作按钮及编辑框在最小尺寸截图与 AX 树中完整可用。
- 最终观察宿主记录 **actions=4、closed=True**，文本恢复
  `对话框返回后继续编辑🙂`。基线宿主 actions=2；两应用及 keeper 均退出，
  关闭后没有通过界面工具自动重开。

两个仓库全部大小写不敏感、名称恰为 artifacts 的路径已清理，剩余 **0**；
移除 **5,162** 个文件或链接、**2,106,113,713** 字节逻辑大小。三套临时应用
的偏好以及原生比较夹具的 **10** 份自有诊断报告同时清除；报告路径被系统
匿名化时按本轮唯一 bundle 标识与创建时间核对归属。
清理前后 **19,001** 个受保护文件、**1,690** 个目录精确一致，保留工具、
并行修改及 Gallery 的既有状态。原生输入 **506** 个及冻结负载 **12** 份
未改变，普通 Debug 的 **11** 份库/资源一致；提交后通过真实 CMake 目标
刷新完成标记。本轮只提交文件面板、两项验证宿主和本记录的四个文件。

初始后台应用中的 Return 仍未获得有效键盘验收，本轮不将其归因于产品或
声称系统总会授予激活。上述自动焦点结果限定于已确认前台的三种面板导航。
更多公开选项与文件系统大小写边界、后台/多窗口/应用切换组合、真实中文
候选窗、VoiceOver、混合 DPI、停靠拖动、macOS 15、Intel 和签名发布
继续保留验收缺项；整个 macOS Window 目标尚未完成。

### v148：原生 sheet 的键盘路由与目录链接一致性

原生或自绘标题栏窗口附有 AppKit sheet 时，针对 owner 的 keyDown、keyUp、
修饰键与快捷键现在转交给 sheet，重新使用 sheet 的 windowNumber，保留
键值、修饰状态、时间与重复标记。内容 responder 收到定向输入时也走同一
窗口入口，避免编辑 sheet 后面的文档或执行 owner 的关闭、最小化、全屏与
标签切换快捷键。禁用 owner 仍拒绝输入；sheet 分离后恢复原有文档路由。
鼠标事件保持 AppKit 的坐标与分发方式。

目录范围比较同时解析 macOS 目录符号链接，包括 `/var` → `/private/var`
以及链接目标的父目录仍含链接的情况。根目录、初始目录、快捷位置去重及
完整候选批次共享这一策略；不存在的保存文件名保留在解析后的现存父目录下。
文件链接的叶名称不被此步骤解析，保留打开面板 DereferenceLinks 的选择。
此前 AppKit 返回 `/private/var` 而托管范围是 `/var`，同一目录会被误判越界并
返回 Root。此修复不声称实现 Finder alias 文件、文件权限或符号链接安全沙箱。

本轮在 macOS 27.0.1、Apple Silicon、一个真实 1× 屏幕验证：

- 最终 sheet 键盘夹具在冻结 v147 负载 `45969b5b` 上为 **10/34**，修复后
  **34/34、1.19 秒**。两类标题栏覆盖六种快捷键、owner/responder 的三类
  键事件、禁用输入及分离后的路由。自有 NSPanel 的协议检查与真实文件面板
  操作保留不同证据范围。
- 原生 CTest **6/6、0 失败、10.02 秒**，覆盖鼠标位置、Window 生命周期、
  属性、文档菜单、sheet 键盘和启动定位。首次运行缺少清理后的 cursor
  夹具，补建后以上集合完整通过；缺失程序不计入产品行为失败。
- 启用原生几何的完整托管集合 **1475/1475、0 失败、0 跳过**，测试报告
  **4 秒**，含构建的执行 **48.47 秒**。新增四项目录链接回归，覆盖物理
  根目录、初始目录、位置去重、缺失保存叶、范围外目录链接和保留文件链接。
- 最终 SDK 原生选项集合 **31/31、58.70 秒**，增加打开、文件夹与保存的
  目录链接候选；owner/可取消确认集合 **14/14、31.72 秒**，筛选/取消集合
  **15/15、21.31 秒**。夹具也按同一目录策略比较 `/var` 和 `/private/var`；
  文件夹确认使用面板一致的物理路径，分别验证允许选择及单次 FolderOk。
  定位输出已从最终 SDK 移除，未将构造路径或加载环境错误算作产品失败。
- 最终 SDK 增量构建 **0 错误、18 项既有警告、12.64 秒**。严格临时签名、
  **16** 份原生库可加载节、**3** 份资源和 **10** 份 linked SDK DLL 核对
  通过。实际观察进程加载本包八份 Jalium 库与两份 Metal 资源，没有 DYLD
  覆盖。
- 查看最终主窗口 **720 × 672 DIP**、原生打开与展开保存面板的截图和 AX
  树。打开先选择 chosen.txt、只读 True 并触发首次 FileOk 拒绝；未激活
  面板时 Escape 未取消，执行面板 AX Raise 后 Escape 取消成功，恢复
  `original.txt` 与只读 False。运行状态确认 active=True、panelKey=True，
  不能把未激活时的观察扩大为所有键盘入口。
- 保存名称通过 AX 设为 `键盘保存🙂`，真实快捷位置菜单从 Root → inside
  重新创建面板后仍保留名称和文本筛选。未置前时 Return 未接受；对新面板
  执行 AX Raise 后 Return 接受，`inside/键盘保存🙂.txt` 内容核对为
  `owned option save`。此项证明 Unicode 文件名，不代表中文候选窗输入。
- 返回主窗口后正反向 Tab、英文 A 输入与 Command-Z 撤销成功。观察宿主
  `actions=2`、文本恢复原值、`closed=True`，应用与 keeper 均已退出。一次
  实际缩窗拖动没有改变窗口，仍为 720 × 672，本轮未验收最小尺寸。

本轮清理两个仓库全部大小写不敏感、名称恰为 artifacts 的路径，剩余 **0**；
移除 **5,043** 个文件或链接、**1,945,311,893** 字节逻辑大小，
验证应用偏好、私有 Metal 缓存及本轮新增 cursor 夹具同时清除。清理前后
**18,997** 个受保护文件与 **1,690** 个目录精确一致，保留工具、
并行修改、Gallery 的既有变更与缺失文件。除三个本轮改动文件外的原生输入
**503** 个未改变，冻结基线负载核对通过；普通 Debug 负载除更新的 platform
库及构建完成标记外，其他 **10** 份库或资源未改变。

导航重建后的前台/键盘恢复与打开面板选项区域的展开恢复仍待补齐；本轮
不将手动 Raise 的成功算作自动恢复。其他公开对话框选项、大小写与更多
文件系统边界、真实中文候选窗、VoiceOver、多屏混合 DPI、停靠拖动、
macOS 15、Intel 与签名发布继续保留原有验收边界。

### v147：文件面板的目录、隐藏项目与只读选择

`Microsoft.Win32.CommonItemDialog` 的 `InitialDirectory`、`DefaultDirectory`、
`RootDirectory`、`ShowHiddenItems` 和 `CustomPlaces` 现在分别传给 macOS 面板，
不再把三个目录选项预先合成同一个初始路径。有效初始目录、文件名的有效父目录、
当前进程内同类面板最近一次完成会话的目录、默认目录及有效根目录依次作为候选，
每项须位于指定范围内。打开文件、保存与文件夹分别记忆最近目录；不声称实现
跨进程的 ClientGuid 历史。AppKit 新面板的 DirectoryUrl 可以预先是 Documents，
不能据此判断用户已经选择过目录，因此最近目录来自真正结束的面板会话。

根目录用于引导选择范围：目录切换到范围外时，在 common/modal run-loop 中
返回根目录；后续目录回调或会话结束使旧请求失效，避免覆盖后来进入的有效目录。
根目录已消失时取消面板。打开面板按范围和筛选器启用文件；祖先
目录可见，以保留原生路径显示。保存面板不会调用 ShouldEnableUrl，三个面板
均在确认候选 URL 及完整批次时复查范围，拒绝时提供原生错误说明且不触发
FileOk/FolderOk。此处是规范化后的路径范围，既不授权文件访问，也不是符号
链接安全沙箱。参考 [WPF CommonItemDialog 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/Microsoft/Win32/CommonItemDialog.cs)、
[AppKit 目录属性](https://developer.apple.com/documentation/appkit/nssavepanel/directoryurl) 与
[打开面板的启用回调](https://developer.apple.com/documentation/appkit/nsopensavepaneldelegate/panel(_:shouldenable:))。

隐藏项目使用原生 ShowsHiddenFiles，默认仍为 false。CustomPlaces 通过原生
“位置快捷方式”附件呈现：根目录在前、保留有效位置次序、去重，忽略缺失、
范围外和未知 GUID；常见文档、桌面、下载、音乐、图片、影片与应用位置映射到
当前平台目录。没有对应位置的 Windows 专属 GUID 不会制造虚假路径。
当前 SDK 的 NSSavePanel.h 明确将 directoryURL 标为 configuration-only。
真实快捷菜单曾执行回调并改变 DirectoryUrl 的 getter，但远程文件列表没有移动；
同步 getter 检查不能证明导航成功。快捷位置和越界返回现先取消内部面板会话，
重新创建面板并在配置阶段设置目录；只读选择、筛选器、当前保存名称和
隐藏项保留，
不会把内部导航当作调用方取消。owner 关闭会清除导航请求，禁止重开已关闭窗口。
在本机 macOS 27.0.1 的当前工具观察中，重新创建打开面板后原生“选项”区域
会折叠，需再次点击 Show Options；配置阶段与显示后的公开 disclosure 属性均只改变 getter，
没有改变可见区域。无效的展开恢复逻辑已移除，此项作为明确未补齐的限制，
不将这些观察扩大为其他入口、系统或硬件的结论。
只读选择使用 NSButton switch，ShowReadOnly 控制显示，ReadOnlyChecked 提供
初始值并在 FileOk 前公开；取消校验、整个面板取消或异常恢复旧公开状态，
通过时保留事件处理器的修改。它是调用方的打开方式选择，不修改文件权限。
附件与筛选器共同使用原生布局，仍保留仅有筛选器时的直接 NSPopUpButton，
避免破坏既有面板行为。参考 [隐藏项目](https://developer.apple.com/documentation/appkit/nssavepanel/showshiddenfiles) 与
[原生附件](https://developer.apple.com/documentation/appkit/nssavepanel/accessoryview)。

平台返回的多选文件或文件夹批次包含无效路径时，现在拒绝整个批次，避免通过
Where 过滤悄悄只发布其中的有效文件。单选仍只取第一项。

- 新增托管选项和状态恢复 **21/21**，相关选项、确认与筛选集合 **70/70**；
  启用原生几何的完整托管集合 **1471/1471、0 失败、0 跳过、15.61 秒**。
- 同一最终 SDK 夹具在冻结 HEAD `e21978a77a533dda4203a4724dd4fedadf4e4a7b`
  上为 **5/28、50.13 秒**，修复后 **28/28、56.67 秒**。覆盖三类面板的隐藏项、
  根目录初始夹紧及目录返回、范围外文件/文件夹/保存候选、快捷位置过滤与
  文档 GUID、只读初始值、取消/异常回滚、未显示的只读状态、目录优先级和
  取消后重开、快捷菜单动作及迟到目录回调，导航创建新面板并保留只读/筛选/保存名称/隐藏项、
  导航期间阻止旧选择确认及 owner 关闭时清除重开请求。配置检查及自有委托协议调用不
  代替真实菜单导航和选择接受。
- 原有筛选/扩展名/真实取消集合 **15/15、22.10 秒**；owner、可取消确认、
  主队列进入及异常回归 **14/14、32.43 秒**。本轮原生 C++ 的 **506** 个输入
  和冻结的 **12** 份 Debug 负载均未修改。
- 最终 SDK 增量构建 **0 错误、18 项既有警告、22.42 秒**；相同夹具的基线
  SDK 为 **0 错误、18 项警告、19.72 秒**。初版夹具把 ItemTitles 方法当成
  属性、初版附件调用 NSButton 的只读 AccessibilityLabel 绑定，修正后编译
  通过；这些编译问题没有计入旧产品负载的行为失败。
- 初版原生行为集合 **19/22** 暴露了默认目录被 Documents 掩盖、文件夹面板
  已选 URL 掩盖范围外候选两类问题；修复后以上最终集合包含追加的保存范围与
  重开目录回归。这一中间结果不作为最终通过。
- 两个 SDK 包分别核对 **16** 份原生库可加载节、**3** 份资源、**10** 份 linked
  SDK DLL，并通过严格临时签名。最终实际观察进程 **74704** 加载本包的 **8** 份
  Jalium 库与两份 Metal 资源，没有 DYLD 覆盖。
- 桌面实际验收：查看 **720 × 672 DIP** 主窗口与原生打开、文件夹和保存
  面板截图及 AX 树。打开快捷位置实际从 Root 切到 inside；通过原生路径菜单
  进入范围外的 observer-owned 后，真实文件列表返回 Root。只读 True、所有
  文件筛选和隐藏项跨内部导航保留。实际 Command 多选 `.隐藏🙂.txt` 与
  `chosen.txt`，首次 FileOk 拒绝后通过 Return 重试，第二次发布完整两项。
  另一次打开先改变只读值并触发拒绝，再点原生 Cancel，恢复只读 False 与
  `original.txt`；隐藏目录 `.hidden-folder` 选择成功。
- 保存面板取消后恢复原文件名 `范围记录🙂`；再次打开后，将名称用 AX 改为
  `切换后保存🙂`，展开文件列表并经真实快捷菜单从 Root 切到 inside，名称
  与文本筛选保留。点原生 Save 后，实际文件 `inside/切换后保存🙂.txt` 存在，
  内容核对为 `owned option save`。此项证明 Unicode 文件名与目录切换保存，
  不代表真实中文候选窗输入已验收。
- 返回主窗口后的正反向 Tab、英文 A 输入及 Command-Z 撤销成功；最终
  `actions=5`、文本恢复原值、`closed=True`，进程与自有 keeper 均退出 **0**。
  原生打开面板在首次确认拒绝后的 Escape 和 Command-. 未取消；紧凑保存
  面板 Escape 取消成功，但展开并导航后的 Return 未接受，鼠标 Save 接受
  成功。这些入口的键盘差异仍待补查。主窗口拖动缩小持续被工具返回
  `noWindowsAvailable`，本轮未核实手动缩窗或最小尺寸；v146 的既有证据不
  替代本轮实际拖动验收。

本轮已清理两个仓库内所有大小写不敏感、名称恰为 artifacts 的路径；删除
**10,266** 个文件或链接、**3,997,051,061** 字节逻辑大小，剩余路径 **0**。
同时删除两个自有 SDK 应用的偏好及用户私有 Metal 缓存，未发现新增自有
诊断文件。清理前后 **18,996** 个受保护文件与 **1,690** 个目录的内容及模式
精确一致，保留 `.tools`、并行修改及 Gallery 原有缺失文件；原生输入与普通
Debug 负载未改变。
AddToRecent、ClientGuid、CreatePrompt/CreateTestFile/OverwritePrompt 的非默认
选项、ForcePreviewPane、ValidateNames/CheckFileExists=false 的原生差异仍待
逐项补齐。路径大小写及别名边界、跨进程目录历史、真实中文候选窗、VoiceOver、
多屏混合 DPI、停靠拖动、macOS 15、Intel 与签名发布仍未全部验收。

### v146：全局鼠标位置与窗口坐标一致

`jalium_input_get_cursor_pos` 原先直接返回
[NSEvent.mouseLocation](https://developer.apple.com/documentation/appkit/nsevent/mouselocation)
的 AppKit 屏幕点。Window 的全局位置、客户区原点与屏幕几何则使用左上角像素
映射；直接相减会让浮动窗口的纵向拖动反向，Retina 上的拖动距离也不一致，
停靠高亮和脱离面板后的窗口定位同样使用了错误坐标。

现在鼠标接口复用窗口的 FrameworkScreenPoint 转换，按指针所在的完整屏幕
frame 选择屏幕，屏幕外的点取最近屏幕；使用完整 frame 而不要求 visibleFrame，
因此菜单栏、Dock 或空工作区不会改变鼠标坐标选择。启动居中继续要求有效工作区，
原有语义保留。无屏幕、空 frame 或非有限指针数据返回 InvalidState 并清零输出；
缺少输出参数仍返回 InvalidArgument，不写另一输出。ABI 形状没有变化。

- 同一公共 ABI 夹具在冻结 HEAD `f7ac8b3405bfeefba0726e37361f2eea31689fdb`
  的旧负载上为 **4/20**，修复负载为 **20/20**。包含当前真实屏幕的一次稳定
  读数、三项参数检查、十五组坐标/拓扑测试数据及一次公共方法替换恢复检查。
  测试数据覆盖 1×/2×、小数坐标、负原点、上下/左右屏幕、屏幕公共边缘、
  屏幕外点、空工作区、缺少缩放和无有效屏幕；没有移动真实系统鼠标或创建显示器。
- 当前真实硬件为 **1× 单屏**：AppKit `(1005.359, 383.371)`，旧接口原样返回；
  修复接口为 `(1005.359, 1056.629)`，与独立窗口坐标公式一致。两轮实际加载的
  platform/core 原生库路径均已核对，旧轮来自冻结目录，新轮来自本次 CMake 负载。
- 坐标专项、窗口属性、连续缩放、启动定位、拖放生命周期及基础屏幕转换的
  CTest **6/6、0 失败、27.09 秒**。最终 CMake 构建与严格警告的夹具编译均成功。
  夹具最初的辅助函数名与 SDK 的 Point 类型冲突，改名后编译通过；这不是旧产品
  负载的回归结果。
- 用户解锁后，以冻结 HEAD 加本轮原生修改的隔离 SDK 应用完成 v145 的最终
  文件面板桌面验收。保存首次确认得到 `拒绝.txt`、筛选索引 2，取消后同一面板
  保留；改成 `接受🙂` 后回车确认得到 `接受🙂.txt`，并在自有目录核对了实际写入
  内容。另一保存会话先取消校验再按 Escape，公开状态恢复为原文件名 `拒绝`、
  索引 1，拒绝的文件没有生成。
- 真实 Command 多选 `chosen.png`、`chosen.txt` 和 `folder-a`、`folder-b`，
  各自首次校验只触发一次并取消，两个选项均保留；重试只再触发一次并接受完整
  两路径集合。四个 ShowDialog 均返回主窗口，日志共记录 7 次确认事件与
  4 项结果。以上来自实际 AppKit 面板操作，未调用夹具的委托协议来替代接受。
- 观察并检查了 720 × 672 和 CUA 拖拽缩小后的 600 × 592 DIP 实际截图。
  最小窗口内所有按钮完整显示，正反向 Tab 逐项经过五个按钮并返回编辑框，
  没有插入 Tab 或空格。返回后重新聚焦编辑框，输入 `a` 与 Command-Z 撤销
  得到原文 `返回后继续输入🙂`。原生面板第一次键盘操作时未确认，执行其公开
  AX Raise 后回车完成保存和文件夹重试；不据此声称系统前台切换或自动焦点
  恢复通过。结束按钮关闭窗口，进程退出 0，最后日志保留原文且 `closed=True`。
- 此次 SDK 完整重建 **0 错误、28 项警告、53.46 秒**，与 v145 的增量结果
  分开记录。16 份原生库可加载节、3 份资源、10 份 SDK linked DLL 与严格临时
  签名均核对；运行进程实际加载本包的 8 个 Jalium 原生库和两份 Metal 资源。
  窗口、保存/打开面板、多选、取消及最小布局截图均已实际查看，AX 名称和
  焦点检查通过；没有进行 VoiceOver、真实中文输入法或量化对比度验收。
- 当前真实单屏读数和多屏测试数据不代替真实混合 DPI、多屏跨越或物理拖动
  验收。此次 CUA 窗口缩放也不代替浮动窗口拖动和停靠高亮的实际验收。
- 测试结束后，两仓库内所有名为 artifacts 的目录均已清空并移除；本次删除
  4,897 个文件或链接，逻辑大小 1,910,271,114 字节，并删除新建的 cursor
  测试目标输出两文件、两份新建 CTest 日志和一份自有应用偏好。逐项核对
  **18,997 个原文件/链接、1,690 个目录和
  502 个非本轮原生输入**，均保持原样。Gallery 原有 273 项跟踪改动、14 项
  缺失文件及工作区现有 CSS/ToolTip 修改均保留。

### v145：文件面板的所属窗口与可取消确认

`Microsoft.Win32` 文件和文件夹对话框现在把 owner 交给平台面板。macOS 将
Jalium 的原生 NSView 句柄解析到 NSWindow，也支持平台直接传入 NSWindow；
默认 owner 优先取活动窗口，再取主窗口。无 owner 时保留应用模态面板，
有 owner 时建立原生 sheet 关系；隐藏 owner 或已有 sheet 的 owner 立即拒绝。
owner 关闭会取消本次选择，结束后清理 sheet 关系、附件控件、委托和关闭观察者。

同步 ShowDialog 采用 NSWindow 的 beginSheet 与 NSSavePanel 的 runModal 配合，
而不是阻塞等待异步 completion。最初的异步 sheet 加嵌套 dispatcher 等待虽然
通过顶层原生夹具，却在桌面无障碍按钮调用中阻塞：面板关闭了，主队列内的
ShowDialog 仍等待同一主队列的完成回调。自有进程采样确认该调用栈。
最终流程参考 [Tk 的现代 macOS 原生文件面板实现](https://github.com/tcltk/tk/blob/main/macosx/tkMacOSXDialog.c)，
使用 [AppKit 的同步面板会话](https://developer.apple.com/documentation/appkit/nssavepanel/runmodal())。
关闭 owner 和校验异常所需的取消排入公开 NSRunLoop 的 common/modal 模式，
避免把取消再次排到正在等待的主 dispatch 队列；延迟执行时检查面板是否仍存活。

FileOk / FolderOk 接到
[`panel:validateURL:error:`](https://developer.apple.com/documentation/appkit/nsopensavepaneldelegate/panel(_:validate:))，
在原生面板关闭前公开完整候选路径与当前筛选索引。取消校验保留面板并恢复
公开文件名、文件名集合和索引；再次确认会重新校验。整个面板取消或回调抛错
同样恢复旧状态；异常在面板退出后以原异常返回托管调用方，不穿过 Objective-C
回调边界。同一实例的重入立即拒绝，通过校验时保留事件处理器对公开结果的修改。
多选以最后一个所选 URL 作为单次确认点，不依赖 AppKit 的逐 URL 回调顺序；
候选批次在这一轮回调中缓存，选择、目录或筛选变化后失效，避免逐 URL 复制
完整选项而产生平方级工作。基本文件/目录存在性检查覆盖整个批次。

- 新增托管确认状态集合 **13/13**，包括候选路径/索引、取消后重试、整个面板
  取消、事件修改与异常、完整多选批次、文件夹校验、同一实例重入及 owner 传递。
  原生几何已启用的完整托管集合 **1450/1450、0 失败、0 跳过**。
- 同一最终 SDK 夹具在冻结 HEAD `8820d8ef4c58923f65c4557f7a278e45ba6a4f1f`
  上为 **1/14、25.73 秒**；最终 **14/14、31.10 秒**。覆盖两种标题栏的 owner、
  无 owner、隐藏/已有 sheet 的 owner、owner 关闭、取消后重试、校验异常与重入，
  以及主 dispatch 队列调用中的取消、异常和异常前关闭 owner。
  上一轮筛选、文件名委托及真实取消回归最终 **15/15、21.22 秒**。
  委托确认属于自有协议调用，真实保存和多选接受须由桌面步骤另行记录。
- 最终 SDK 增量构建 **0 错误、0 警告、8.41 秒**；同一最终夹具的基线 SDK
  重建有 18 项既有警告。早期构建中新夹具的空值和反射警告已修复。
  早期夹具的 SDK 类型与空回调参数错误不计为产品基线；一次还原阶段的
  CoreCLR 编译工具崩溃也不计为产品失败，复用已还原依赖后构建成功。
  本轮原生 C++ 输入与 12 文件 Debug 冻结负载未变；新旧 SDK 包各自的
  16 份原生库可加载节、3 份资源、10 份 SDK linked DLL 及严格临时签名已核对。
- 初版桌面检查看到了 600 × 592 DIP 最小窗口、原生 sheet 与筛选菜单，
  同时发现并采样了异步等待死锁；这些观察不能算作最终版本通过。
  最终桌面检查被 Mac 锁屏阻断，观察进程 900 秒内没有收到验收操作并正常退出。
  实际保存中的取消校验与改名重试、打开文件及文件夹的真实多选接受、
  返回后的键盘焦点与输入、最终截图均未完成；14 个原生夹具和 15 个旧回归
  的通过不代替这些实际交互验收。
- 测试结束后清理了两个仓库内全部名为 artifacts 的目录：删除 9,941 个文件或
  链接，逻辑大小 3,762,259,719 字节，以及两个自有测试应用的偏好和一份
  本轮编译工具崩溃报告；最终 artifacts 为 0。清理前后 18,116 个受保护文件/
  链接及 14 个原有缺失文件、505 个原生输入、1,212 个 .tools 目录和本轮
  9 个源码路径均已核对；并发 CSS/ToolTip 改动、Gallery 的原有改动保留。

### v144：原生文件面板的筛选、扩展名与覆盖确认

`DefaultExt` 现在只用于无可用筛选后缀时的保存文件名回退，不再隐式限制打开
面板的可选类型。保存时优先使用当前筛选器的第一个具体后缀，再使用 DefaultExt；
`AddExtension=false` 保留无后缀文件，明确输入的后缀也保留。复合后缀、分号
分隔的多个后缀、大小写去重、所有文件选项和普通文件名通配符均有回归覆盖。
打开面板通过原生类型与 `ShouldEnableUrl` 文件名匹配配合筛选，目录继续可导航；
切换筛选器后刷新可见文件。文件夹面板不使用文件类型限制。

AppKit 保存面板会根据 AllowedContentTypes 自动补后缀，即便允许其他文件类型。
保存面板因此不设置这个隐式补后缀列表，改由
[`userEnteredFilename:confirmed:`](https://developer.apple.com/documentation/appkit/nsopensavepaneldelegate/panel(_:userenteredfilename:confirmed:))
在确认时处理文件名，先于系统覆盖检查；返回的 URL 不再在关闭面板后改写。
这也符合 [WPF DefaultExt 的筛选器优先、默认后缀回退说明](https://source.dot.net/PresentationFramework/Microsoft/Win32/FileDialog.cs.html)。

- 修改前冻结 HEAD `9b6897bd99a610e38f56895bc28d212d60134aea` 的托管筛选回归
  **5/7**，两项无筛选器默认后缀的行为断言失败；最终相关集合 **36/36**。
  原生几何已启用的完整托管集合 **1437/1437、0 跳过、0 失败**。
- 同一最终 SDK 原生面板配置、文件名委托协议及真实取消集合在旧实现上
  **2/15、19.92 秒**，最终 **15/15、21.09 秒**。覆盖筛选切换、确认前文件名、
  不改写未确认输入、复合后缀/文件名通配符、目录导航、取消不发布筛选变化及
  取消后用同一配置重开。这里的文件名确认是自有委托协议调用，实际接受保存
  及覆盖提示由下面的桌面步骤另行验证。
- 早期夹具的绑定参数和面板动作调用有误：空 sender 被 .NET 绑定拒绝，当前
  AppKit 的公开 `ok:` 又抛出“not implemented”。这两类夹具错误及相应超时不计
  为产品行为基线。最终夹具使用有效取消发送者、正常 modal run-loop timer，
  自动集合只检查配置/委托协议/取消，真实确认通过桌面工具完成。
- 当前 Apple Silicon、macOS **27.0.1** 的实际原生面板截图与 AX 树已检查。
  当前筛选器为文本而 DefaultExt 为 png 时，保存得到 **记录🙂.txt、FilterIndex=2**；
  再切到所有文件并用 Escape 取消，原文件名、索引 2 和既有 FileOk 次数保留。
  后续重开仍选文本。Return 保存分别得到 **无后缀**和 **全部文件.txt**；
  文本筛选器下明确的 **explicit.custom** 也保持原样。无筛选器且 DefaultExt=txt
  的打开面板实际选择并接受 **chosen.png**。
- 输入无后缀“已有文件”后，原生覆盖提示明确显示 **已有文件.txt**。取消这个
  提示后仍留在保存面板，读取自有文件确认原内容未变；再次保存并选择 Replace
  后接受相同最终路径。上述 **7** 次面板返回包含 **6** 次接受、**1** 次取消；
  **5** 个实际保存文件的内容与名称核对，且不存在多余 png/txt 后缀文件。
  持久保存对话框只有两次接受触发 FileOk，取消不触发。
- 返回主窗口后实际 Tab 到保存按钮、Shift+Tab 返回编辑框、输入 `a` 与 Command+Z
  恢复 **返回后继续输入🙂**；原生窗口工具角拖从 **720×692** 到最小外框
  **600×612** 后控件完整可见。原生保存字段的 Command+A/中文工具键入没有得到
  预期整段替换，未记为全选或中文 IME 通过；显式后缀场景改用已暴露的 AX Value
  设置文件名再用 Return 确认。公开前台查询为 PID **31844**、active=false、
  前台 PID **686**，这些是定向工具交互证据，真实系统前台切换仍待验收。
  验收进程通过结束按钮正常退出 **0**，保持唤醒进程也已结束。
- 两份独立 Git 导出各 **3210** 个源码文件，排除用户并发工作；**505** 个原生
  输入未修改，复用本轮前冻结的 **12** 份完整 Debug 负载。最终真实 SDK 构建
  **0 错误、18** 个既有警告、**14.30 秒**；两个包各 **16** 份原生库的 Mach-O
  节、**3** 个资源和 **10** 份 SDK linked 托管 DLL 均核对，并通过严格临时签名。
  桌面进程实际加载本包 MonoBundle 的 **8** 份库，启动时没有 DYLD 覆盖。

本轮覆盖原生文件面板的上述筛选/保存契约；所属窗口 sheet、可取消 FileOk 的
原生重显示、其余公开文件对话框选项、完整中文 IME、VoiceOver、系统前台、
macOS 15 与 Intel 仍需独立补齐或验收。这里不据此宣称整个 Window 或文件
对话框兼容性已经完整。

验收后移除两个仓库中 **9,782** 个 artifacts 文件/链接、**3,449,867,520**
逻辑字节（约 **3.45 GB**），另清理 **3** 个已核对内容与创建时间的早期夹具
临时目录、**2** 份本轮独立应用偏好；没有本轮崩溃诊断。删除前后 **18,103**
个受保护文件/链接、**14** 个既有缺失路径、**1,540** 个目录及 `.tools` 的
**1,212** 个目录均匹配，用户并发改动保留；删除前核对两份 **3210** 源码
清单、**505** 个原生输入与 **12** 份冻结文件。两个仓库字面 artifacts 路径、
本轮应用/保持唤醒进程、独立应用偏好及已归属临时目录均为 **0**。清理后只
追加本段记录，测试代码与本节验收结论保留。

### v143：URI 文件识别、读取上限与拖放数据重入

原生 `public.url` 与 `public.file-url` 都转换为 `text/uri-list`，旧的外部拖放对象
只依据 MIME 就登记 `FileDrop`。因此网页链接在 `DragEnter` 中也报告有文件，读取
之后则可能返回空数组；多种 URI 表示中先读取网页还会阻止后续有效文件表示。
现在查询 FileDrop/文件名别名或枚举格式时，才延迟解析对应 URI 表示；只有至少
一个有效文件路径才登记文件格式。网页、注释和空 URI 不登记文件格式，但原始 URI
字节仍可用；普通文本、HTML 和自定义格式继续按需读取，每种表示只读一次。
显式应用数据与同进程来源对象身份保留，Drop 快照在原生访问结束后仍可复读。

原生延迟读取、剪贴板读取及旧 Drop 事件路径原先可先分配超限缓冲区，Drop 的
长度还可能截断到 uint32。现在原生与托管复制前均使用既有 **256 MiB** 上限，
超限返回失败并保留空输出；旧 Drop 选择第一个有界表示，超限首格式不会丢弃
其余有效格式。TIFF 生成 PNG 前也拒绝超限输入。每次提供方读取后及字节访问后
重新检查窗口、访问代数与原生序号，旧访问不能发布到后继访问；延迟读取在
复制期间被替代时释放副本、清空输出并返回 InvalidState。这里限制框架边界的
复制及超限 TIFF 输入，不表示能控制外部应用生成承诺数据时的内部内存。

- URI 托管回归在未修改的 `5cfb12ca27d410fb9c0eaeec525c7f9bf4434990` 上为
  **3/17**，有 **14** 项行为断言失败。初版两个测试声明编译问题修正后才记录
  这次行为基线；未把编译失败算作复现。最终 URI **17/17**、托管复制 **8/8**，
  原生几何已启用的完整集合 **1406/1406、0 跳过、0 失败**。
- 同一最终原生夹具在修改前冻结平台库上的新增边界/重入集合 **8/30**，有
  **22** 项失败；每项独立进程，超限 NSData 只报告长度，字节访问抛自有异常，
  不实际分配相应数据。修复后最终完整拖放生命周期 **152/152、9.52 秒**。
  首版有效次格式反例在异常退出时留下引用局部数据的测试回调，造成两份夹具
  崩溃诊断；加入作用域回调清理，并使用 NSData 子类后，重新执行上述最终基线
  与回归。旧诊断按专用程序名、启动时间和冻结库 UUID 核对归属后清理。
  首次相关 CTest 窗口属性、定位、拖放及屏幕坐标 **4/4、44.05 秒**，其中拖放
  当时为 **148/148**；追加四项字节访问重入反例后只重跑拖放，其他产品库未改。
- 真实 SDK 的两种标题栏 × 六类 URI **12/12**，执行自有 NSDraggingInfo、
  唯一 NSPasteboard 与真实 NSWindow/NSView 到托管的 PreviewDragEnter、DragEnter、
  Drop 路由；核对文件别名、完整原始 URI、文本与 HTML，以及清空源 pasteboard
  后的快照。空 URL 项产生 AppKit “No data was supplied…” 日志，断言通过。
  本轮只重放自有委托协议，没有据此宣称 OS 跟踪的跨应用或物理拖放通过。
- 独立 Git 导出基于同一 HEAD，最终 **3209** 个源码文件、**8** 份自有覆盖；
  **505** 个原生源码/配置指纹只改变平台实现与拖放夹具。真实 CMake
  package.complete 标记该基线 `dirty=1`；首次 SDK 构建 **0 错误、28** 个既有警告，
  修正验收页结果文字的无障碍名称后，最终构建 **0 错误、18** 个既有警告、
  **12.61 秒**，新宿主没有警告；最终 SDK 路由重新执行仍为 **12/12**。
  严格临时签名、**16** 份原生库的 Mach-O 节、**3** 个资源与
  **10** 份自有托管 DLL 均核对；Native 观察进程加载 **8** 份本包 MonoBundle
  库且没有 DYLD 覆盖；最终 Custom 也核对相同的 **8** 份本包加载路径。
- 桌面最初锁定，随后工具恢复读取。两种最终标题栏的截图、AX 树、Tab/Shift+Tab、
  输入 `a` 与撤销回原值 `拖放后保留编辑🙂` 均实际检查。按钮及键盘触发网页/文件
  协议重放，结果分别为 **0/2/0** 个文件，原生及托管事件各 **3** 次；当前结果
  文字同步进入 AX 树。Native 从 **640×492** 缩到 **520×482** 外框后仍完整可见。
  Custom 的工具坐标角拖没有改变 **640×460** 外框，未记为最小尺寸或角拖通过；
  同时公开系统查询为目标 PID **25631**、active=false、前台 PID **686**。
  这是工具定向交互证据，未据截图推断真实系统前台切换。最终 Native PID **25525**
  经结束按钮、Custom PID **25631** 经 Tab 到结束按钮再 Enter，均正常退出 **0**；
  初次 Native 观察进程 PID **24545** 也正常结束。跨应用物理拖放、实际修饰键、
  VoiceOver、中文 IME、Custom 最小尺寸与真实角拖仍需各自验收。

本轮验收后移除 **8,544** 个 artifacts 文件/链接、**2,354,045,429** 逻辑字节
（约 **2.35 GB**）、**2** 份本轮 CTest 临时记录与 **2** 份已核对的基线夹具
诊断。两个仓库不区分大小写的字面 artifacts 路径、本轮应用/保持唤醒进程和
已归属临时记录均为 **0**。清理前后 **18,099** 个受保护文件/链接、**14** 个
既有缺失路径、**1,538** 个目录及完整 **1,212** 目录的 `.tools` 状态一致；
用户并发 CSS/ToolTip 与测试项目改动保留。删除前核对 **3207/3209** 个独立
源码文件、**505** 个最终原生输入指纹及 **24** 份前后冻结文件，删除后原生
输入与自有实现/测试/文档仍匹配。应用包、日志和探针输出不保留；源码、测试
及本节验收结论保留，清理后只追加本段记录。

### v142：初始定位验收按 AppKit 实际像素对齐校准

本轮先按 v141 的顺序执行文本协议、窗口属性、控件无障碍与 Window AX，随后
连续三个独立进程记录原始启动夹具的窗口坐标，均为 **40/40**。Window AX 为
**244/244、182.56 秒**；本次没有复现此前首轮失败的环境。当前 AppKit 显示器
为 **2560×1440、scale=1**，工作区为 **(0,90,2560,1320)**。

将相同旧夹具的客户区改为 **421×301** 后，在未修改的平台库上稳定得到
**34/40**：两种标题栏的屏幕、最大化 owner 与最大化还原居中六项均失败。
真实 `setFrame:display:` 请求 **(1069.5,583.5,421,333)**，AppKit 实际返回
**(1069,583,421,333)**；自定义标题栏相应请求 **(1069.5,599.5,421,301)**，
实际为 **(1069,599,421,301)**。中心与工作区各差 **−0.5 点**，是 AppKit 在
这个 1× 屏幕上接受的像素对齐位置；旧断言只允许 **0.01 点**，把原生取整判为错误。
这次复现覆盖同名六项失败，但 v141 原始实际坐标未保留，不能断言两次环境与
触发条件完全相同，也没有因此修改产品的居中逻辑。

原生及 SDK 定位夹具现在用独立的普通 `NSWindow` 接收数学居中外框，取得 AppKit
实际对齐后的完整外框，再以 **0.01 点**比较位置和尺寸。保留严格纯几何断言，
新增四种宽高奇偶组合，以及实际额外移动 **1 点**必须被拒绝的反例；没有把容差
扩大到任意 1 点。目标屏幕与工作区在操作前确定，失败时记录操作前后屏幕、
鼠标位置、选中屏幕 ID 和窗口 setter 的请求/返回外框；不再用放置后的隐藏窗口
屏幕属性作为唯一预期。[NSWindow.screen](https://developer.apple.com/documentation/appkit/nswindow/screen)
可能在离屏时为空，[NSScreen.visibleFrame](https://developer.apple.com/documentation/appkit/nsscreen/visibleframe)
会随系统界面设置变化。

- 原生启动夹具 **72/72**，包含 **16** 项纯几何和 **56** 项真实 AppKit 检查；
  同一新夹具在未修改的冻结平台库上也为 **72/72**。两种标题栏、四种宽高奇偶
  组合、混合标题栏 owner、最大化 owner、初始最大化后的还原、回调关闭/替代、
  位置通知和 1 点偏移反例均执行。屏幕坐标专项通过；上述原生六组分阶段各自通过，
  没有将分阶段结果写成一次全组 CTest。
- 独立源码的真实 SDK 定位宿主 **20/20**、原有 Tab 宿主 **3/3**，均退出 0。
  首版有一处 switch 表达式编译错误，修正后最终构建 **0 错误、18** 个既有警告、
  **14.88 秒**。本轮只修改验收夹具和记录，没有重新执行完整托管集合；v141 的
  **1381/1381** 保留为其原轮次证据。
- 实际查看 Native **421×333** 和 Custom **421×301** 外框截图与 AX 控件树。
  深色中文面板的说明、输入框、结束按钮完整可见，编辑框有明确的无障碍名称。
  工具定向 Tab/Shift+Tab 在编辑框与按钮间移动，中文与 emoji 保留；两种标题栏
  均实际输入 `A` 并撤销回到 `定位后保留编辑🙂`。Native 经结束按钮、Custom 经
  Tab 到按钮后 Enter 完成，PID **19846/20172** 均正常退出 **0**。
- Native 的工具 Unicode typeText 只插入了标点，工具 paste 没有改变内容，未将
  这些尝试算作中文输入或粘贴通过。Custom 观察期间公开系统查询显示
  `NSRunningApplication.active=0`、前台 PID **686** 与目标 PID **20172** 不同。
  本轮证明工具定向输入、焦点与普通编辑，实际系统前台切换、中文 IME 和系统
  粘贴仍需各自验收；没有从目标窗口截图推断系统前台状态。
- 独立 Git 导出 **3206** 个源码文件，基于
  `f9da96ff2aec2c3cee745cf06a3d3d3e163885a1` 覆盖 **2** 份验收源码。
  **505** 个原生源码/配置指纹中仅启动测试文件变化；**8** 个原生库及 **3** 个
  资源与修改前冻结负载内容一致，真实 CMake package.complete 标记为该基线
  `dirty=1`。最终 SDK 包严格临时签名、**16** 份原生副本的 Mach-O 节、**3** 个
  显式资源及 **10** 个自身程序集核对通过；两个实际 PID 加载各自应用 MonoBundle
  中的 **8** 个原生库，没有 DYLD 覆盖。资源及第二组库显式加入验收包。

当前仅有 1× 单屏的实际坐标证据。混合 DPI/多屏、Spaces、VoiceOver、实际 Dock
重开、物理拖动、菜单栏键盘、真实 IME、macOS 15/Intel 和签名发布继续待验收。

### v141：滚动按钮状态与焦点轮廓的内容裁剪

原生滚动事件此前没有按钮快照，Window 又将所有按钮强制设为 Released。
按住左、右、中键或侧键滚动时，预览、冒泡、指针提升和全局 Mouse 查询都会丢失
按下状态。AppKit 现在为精确、普通、零位移结束和惯性事件读取当前系统按钮状态；
复用 bit 31 的有效标记与五个低位，新增字段位于现有事件联合体的第 **48** 字节。
整体结构仍为 **72** 字节，原有坐标、修饰键和 phase 偏移不变。没有有效标记的旧
事件使用已经记录的 Mouse 状态；有效的全松开快照可以清除视图外释放留下的旧状态。

焦点轮廓位于独立 AdornerLayer，此前只重用祖先的公共裁剪，漏掉 ScrollViewer
对内容单独应用的滚动条留白裁剪。现在沿实际视觉子节点路径补用每一层的额外
子内容裁剪；普通横/竖滚动条、RTL 和双侧留白受到正确限制，覆盖式滚动条和
滚动条自身的轮廓保持各自视口。控件自身的裁剪仍允许焦点样式绘制外扩边框。

- 同一 **25** 个新增用例在原实现中 **4 通过、21 失败**，修复后 **25/25**。
  包含完整原生回调解码、五按钮、未知位过滤、旧协议、预览/冒泡/指针与全局状态、
  捕获、全松开、普通/覆盖滚动条、RTL、双侧留白、滚动后位置及滚动条自身轮廓。
  用新增原生协议夹具链接旧冻结平台库时，明确失败于滚动快照的有效标记。
- 独立源码的原生文本几何启用完整托管回归 **1381 通过、0 失败、0 跳过**。
  真实 SDK 宿主的输入边界 **2/2**、既有 Tab 检查 **3/3**，均退出 0。
  两种标题栏分别执行 AppKit Tab/Shift+Tab、文本不变、内容裁剪及五按钮边界事件；
  五按钮包由夹具注入，不能作为真实设备按住按钮滚动的验收。
- 新鲜 CMake 构建完成真实 package.complete。原生选取分组首轮 **5/6**：滚动/
  文本协议、窗口属性、无障碍、窗口无障碍及屏幕坐标通过；启动居中为 **34/40**，
  六个屏幕/最大化 owner/最大化还原居中断言失败。随后旧、新平台库的独立诊断
  及相同原始 CMake 专项分别 **40/40**，初次差异的原因与稳定性继续待查。
- 实际查看 **640×420 DIP** 客户区的 Native/Custom 截图和无障碍树。
  工具定向 Tab 聚焦中文输入框、Shift+Tab 返回按钮，输入内容保持
  `焦点切换保留中文🙂`。Custom 滚动 **0→180→0**，最终 Native 在按钮上滚动约
  **0→100→0 DIP**；目标离开视口时轮廓消失，返回后恢复，并避开滚动条留白。
  实际截图发现面板说明与主题对比不足，验收面板改用与现有控件一致的深色背景。
  三个实际观察进程均按结束按钮正常退出 0；系统前台应用切换另按 v139 的标准验收。
- 最终包严格临时签名、**16** 份原生副本的 Mach-O 节、**3** 份资源和 **10** 份
  SDK 链接程序集核对通过。资源及第二组原生副本显式加入验收包；最终 PID
  **15422** 实际加载其自身 MonoBundle 中的 **8** 个原生库，没有 DYLD 覆盖。
  独立 Git 导出 **3206** 个源码文件，基于 `c44522ad87dc7662a94ca76165fc4101655b4bc0`
  覆盖 **10** 份本轮代码；**515** 个原生源码/配置指纹与新构建输入一致。
  CMake 完成标记真实记录该基线及 `dirty=1`。SDK 最终构建 **0 错误、18** 个既有警告。

真实设备按住按钮滚动、系统前台离开/返回、VoiceOver、实际 Dock 重开、物理拖动、
菜单栏键盘、真实 IME、跨屏/Spaces、macOS 15/Intel 和签名发布继续保留各自的待验收
范围。共享焦点裁剪与旧事件回退在 Mac 上回归通过；Windows/Linux 桌面尚未在本轮运行。

### v140：窗口几何回写保留绑定并通知源

原生 Resize 使用 `SetCurrentValue` 保留 Width/Height 的绑定，却没有发布源更新，
窗口已变大时视图模型仍保留旧尺寸。自动尺寸计算、用户调整后切换为 Manual、
初始定位和 DPI 原点回读采用同一类回写。现在保留其值来源，只在有效值变化后
调用当前绑定表达式的源更新路径。TwoWay/OneWayToSource 的 Default 和
PropertyChanged 会同步源；OneWay 及 Explicit/LostFocus 不会被这次自动回写
立即写源，原绑定及后续模型到窗口的更新仍可用。参考
[SetCurrentValue](https://learn.microsoft.com/en-us/dotnet/api/system.windows.dependencyobject.setcurrentvalue?view=windowsdesktop-10.0)
的值来源语义和 [UpdateSourceTrigger](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/how-to-control-when-the-textbox-text-updates-the-source)
的更新时机。

新增 **19** 项专项，旧实现 **5 通过 / 14 失败**，修复后 **19/19**。覆盖绑定模式
与触发方式、表达式身份、模型反向驱动、重复/空尺寸通知、SizeChanged 中的源
一致性、用户调整后的自动尺寸模式、三种自动尺寸及其约束、**1.25×/1.5×/2×**
合成 DPI 和 DPI 原点回读。首次启用原生几何的完整组为 **1,355 通过 / 1 失败**，
失败项在 NoWrap 的两次测量间得到不同字体高度；该类单独 **26/26**。macOS 项目
未沿用共享测试项目已有的全局状态串行规则，补齐相同隔离规则，避免测试之间
替换或销毁 `RenderContext.Current`；最终完整 **1,356 通过、0 失败、0 跳过**。

真实 AppKit Window 属性组 **10/10**，两种标题栏均把 Width、Height、Left、Top
和 SizeToContent 绑定到同一个模型。AX 改变尺寸后，模型收到实际尺寸和 Manual；
再次写相同 frame 不重复通知源，随后修改模型仍驱动同一原生窗口的客户区尺寸。
位置修改保持自动尺寸模式，所有五个绑定表达式身份不变。其余禁用/隐藏、模态
owner、最大化/最小化与原生 Zoom 重入检查也通过。重开契约再次 **22/22**；
两个完整宿主均退出 0。

独立观察 PID **8836** 在 **510×310 DIP** 客户区下通过尺寸与绑定断言，已查看
实际截图和编辑框 AX 节点，中文与 emoji 保留、编辑框可见。此宿主按观察计时
正常退出 0；无 DYLD 覆盖时加载自身 MonoBundle 的 **8** 个原生库。没有将该
观察记为物理拖动、前台切换、键盘焦点、真实多屏 DPI 或 VoiceOver 验收。

独立源码由 `2363e830c913` 导出，**3,203** 个文件和四份本轮覆盖未混入并行
CSS/ToolTip 修改。复用匹配的真实 CMake 原生负载，原生输入相对 `36626bc6e000`
未改变；冻结 **12** 个文件。SDK 构建 **0 错误、28** 个既有警告，严格临时签名、
**16** 份原生副本的 Mach-O 节、**3** 份资源及 **10** 个自身 linked 程序集已核对。
所有输出已按要求清理，更新现有 PR、保留本地 `master`。v139 跨应用切换与其余
物理/硬件缺项仍保留，完整 macOS 目标继续进行。

### v139：深层 backdrop 检测与系统前台观察

`RequiresFullReplayForBackdrop` 原先在视觉树深度超过 **256** 后停止查询，导致
深层可见 Acrylic 或 LiquidGlass 没有触发 Metal 全场景重放。改为迭代遍历，
没有递归深度截断；隐藏、折叠及透明祖先仍跳过其子树，其他后端保持原行为。
旧源码的新增六项回归为 **4 通过 / 2 失败**，两个失败都位于 **512** 层之下。
修复后六项全部通过；三项既有浅层 backdrop 检查也通过。此结果验证重放判断，
没有单独验收深层窗口的实际像素合成。

独立测试项目启用 `JaliumMacNativeGeometryTests=true`，将既有三项浅层回归作为
本次构建的额外输入，最终 **1,340 通过、0 失败、0 跳过**；冻结原生负载已放在
测试程序集旁，运行无 DYLD 覆盖。最终 SDK 构建 **0 错误、28** 个既有警告，
托管测试构建 **0 错误、8** 个既有警告。最终 v139e 应用的两种标题栏重开契约
再次完成 **22/22**、退出 0；没有重新执行 v138 的八项恢复断言。

新增 `--window-application-activation` 及 `--window-application-activation-case=N`
入口，四项分别覆盖原生/自定义标题栏的普通/模态窗口，每项等待两次实际离开和
返回。初始及返回门控同时要求 AppKit active、key/main、
`NSRunningApplication.Active` 和 `NSWorkspace.FrontmostApplication` 的 PID
等于宿主，并等待修饰键释放及连续 **1 秒**稳定。观察实际激活/失活次数、窗口
身份、RestoreBounds、UTF-16 选区、编辑焦点、原生 undo/redo 和模态 owner。
`JALIUM_MACOS_ACTIVATION_ROOT` 指定唯一结果目录，缺失时最终包实测返回 **77**；
`JALIUM_MACOS_ACTIVATION_SECONDS` 为 **30–900 秒**，默认 **180 秒**。
正常结束和 AppKit 提前退出都写入 `completion.json`，进程退出 0 本身不代表通过。

桌面工具曾使 `NSApplication.Active/key/main` 为 true，但公共运行应用状态为
false，系统前台 PID 仍是其他应用。v139c 手动检查最终 PID **32769** 确实达到
系统前台，日志记录了 ready 和 initial-foreground，文本及选区 **21,3** 保留；
之后没有 background、returned-foreground 或最终通过结果。收到“完毕”回复时
宿主已退出 0，不能据此计为 **4/4**，也没有足够证据修改产品的激活实现。
最终 v139e 实际菜单 Quit 后 PID **4586** 退出 0，并明确记录 native-termination、
**0/1、未开始、Success=false**；提前退出没有被标为验收通过。原生普通窗口
**640×380 DIP** 的实际截图与控件 AX 名称已查看；自定义/模态、最小尺寸和完整
切换后的键盘恢复尚未验收。

v138 的 **8/8** 记录了 AppKit active、key/main 和恢复状态，没有独立记录系统
运行应用 active 与前台 PID；保留这些恢复断言的结果，不能替代本轮更严格的
实际跨应用切换验收。完整 macOS 目标和其余物理/硬件缺项继续保留。

独立源码由 `36626bc6e000` 导出并覆盖本轮四份源码，共 **3,202** 个文件，未混入
并行 CSS/ToolTip 修改。缓存 CMake 完成真实 `package.complete`，冻结 **12** 个
文件（八个 dylib、三份资源及完成标记）与 **516** 个原生源码/配置指纹。最终
v139e 包的严格临时签名、**16** 份原生副本的 Mach-O 节、**3** 份资源和 **10**
个自身 linked 程序集一致性均已核对。已按要求清理全部本轮输出，更新现有 PR
并保留本地 `master`；历史包及结果路径不再是当前可打开的交付文件。

### v137–v138：两种标题栏的重开契约与 AppKit 恢复

将原有 **11** 个 reopen 契约同时用于原生和自定义标题栏，覆盖隐藏、禁用/已关闭
主窗口的候选选择、显示回调关闭窗口、可见窗口默认处理、最小化及最大化状态
保留、嵌套重开、已停止宿主、Closing 重入和最小化模态窗口。最终同一 SDK
负载完成 **22/22**、退出 0。该组使用禁止应用激活的策略，只验证真实窗口的
可见性、身份、状态与激活请求，不代替前台焦点验收。

新增 `--window-reopen-foreground` 与单项入口。启动窗口先返回 AppKit 消息循环，
保留实际按钮请求，等应用 active、原生 key/main、修饰键释放且前台连续稳定
至少 **1 秒**后开始；没有满足前置条件时返回 **77**，不声称执行了断言。
`JALIUM_MACOS_REOPEN_GATE_ROOT` 保存唯一 nonce、ready/accepted 和状态记录；
可选 `JALIUM_MACOS_REOPEN_GATE_SECONDS` 为 **30–900 秒**，默认 **180 秒**。
本轮最终实际前台选择确认 `active/key/main=true`、修饰键 **0**、稳定 **1080.9 ms**。
中文启动窗口截图及 AX 树已查看，标签与原生按钮可读；本轮没有验收 VoiceOver
或系统完整键盘访问设置。

最终 PID **28104** 在同一次 AppKit 运行中完成 **8/8**、退出 0：原生/自定义标题栏
各覆盖隐藏、普通最小化、最大化后最小化及模态最小化，每项连续恢复 **2 次**。
**56** 份状态快照中 **16** 次恢复均保留同一原生/内容句柄、实际 active/key/main、
编辑框逻辑焦点、原生 first responder、编辑文本、UTF-16 选区 **17,3**、先前
WindowState 和正常 RestoreBounds；模态 owner 持续禁用，结束后恢复启用。
恢复后不重新 Focus 编辑框，通过原生 NSEvent 继续输入 `N`，原生 undo 两次与
redo 一次证明恢复前的编辑历史仍可用。输入事件和编辑 action 是宿主分发，
不是物理键盘或真实 IME 候选验收。重开调用真实宿主 delegate 方法，不是实际
Dock 点击，也没有将 v131 工具自动重开后的前台缺项改记为通过。

中间结果保留其范围：v137 门控超时，没有执行恢复断言；v138 最初为 **2/8**，
普通最小化单项诊断为 **0/1**，窗口已还原且文本、选区与输入响应者仍保留，但
应用前台及 key/main 为 false。加入前台稳定门控并改用窗口内 Tab 后，首轮为
**6/8**，原生模态第二次恢复又记录到应用失去前台，自定义模态未取得初始前台。
两种模态独立复查分别 **1/1**、退出 0，随后完整最终运行才取得上述 **8/8**。
这些前台中断没有最终归因，本轮没有修改产品的窗口激活实现或放宽恢复断言。

独立源码由 `c233bd064b6a` 导出并覆盖三份夹具，共 **3,200** 个文件，未混入并行
CSS/ToolTip 修改。匹配的缓存 CMake 构建完成真实 `package.complete`，冻结
**11** 份负载及 **516** 个原生源码/配置指纹。最终增量 SDK 构建 **0 错误、18**
个既有警告；最终应用严格临时签名、**16** 份原生副本、**3** 份资源和 **10** 个
自身 linked 程序集通过核对。实际前台进程无 DYLD 覆盖时加载自身 MonoBundle
的 **8** 个原生库。缺少门控环境变量时，最终负载实测返回 **77**。

本轮没有重跑 v136 的 **935 通过 / 2 跳过**全套、Window Lab、Gallery、完整原生
Window AX 组或字体像素专项；真实 Dock、VoiceOver、物理菜单/IME/拖动、多屏
DPI/Spaces、macOS 15/Intel 与签名发布缺项仍保留。完整 macOS 目标继续进行；
本轮更新现有 PR，保留本地 `master`。

### v136：现有 PR 合并后的最终回归

保留远端 `codex/macos-window-behavior` 已有的文件过滤选择与混合 DPI 屏幕原点
提交 `fa6bd4d4`，将本轮销毁通知修复接在其后，本地仍为 `master`。自动暂存恢复
时，测试项目的两条独立 Compile 引用发生冲突；同时保留文件过滤和并行 CSS
测试引用，原有 CSS/ToolTip 修改均保持未提交。独立测试源码由当前 Git 提交导出，
包含 **3,199** 个文件，没有混入这些工作树修改。

托管全套最终 **935 通过、2 跳过、0 失败**，共 **937** 项；新增文件过滤选择的
**5** 项均通过。首轮测试宿主未解析到冻结原生库，结果为 **838/97 失败/2 跳过**；
将校验过的 **11** 份原生负载复制到独立测试程序集目录后，使用同一程序集执行
`dotnet test --no-build --no-restore` 得到最终结果，不修改测试断言。期间两次直接
VSTest 启动分别因默认 x64 宿主和平台参数格式错误而终止，没有执行测试。

匹配的缓存 CMake 构建完成真实 `package.complete`，冻结 **11** 个文件和 **513**
个原生源码/配置指纹。公共 AX 协议、读取生命周期 **100/100**、通知生命周期
**22/22** 通过。屏幕坐标单元检查通过，覆盖合成的 1×/2× 屏幕、非零/负原点及
正反向映射；该结果没有代替实际多屏硬件验收。原生窗口前台几何组 **38/38**
通过，两种标题栏均覆盖 frame、约束、固定尺寸窗口、位置、旧接口、失效写入与
回调重入。首次前台门控未开始而超时，**没有执行断言**；重新运行后通过实际
“开始回归”操作获得 `active=1/key=1/main=1`，PID **18405** 完成全部 38 项、
退出 0。窗口 AX 宿主 **14/14**、退出 0；Lab/宿主构建均 **0 错误**，分别为
**26/18** 个既有警告。v130 九组宿主 **113/113** 保留原版本范围。

最终 Lab PID **18709** 实际使用主窗口 **540×430 DIP**、原生外层 **500×380 DIP**
和自定义内层 **600×370 DIP**。真实截图和外部 AX 同时检查：最内层拥有活动、
key/main 与编辑焦点，两个 owner 禁用；关闭内层后外层编辑焦点恢复，主窗口
仍禁用。`Nested v136 中文🙂 é` 的尾部空格与组合字符实际高亮、UTF-16 选区
**16,3**，编辑框名称稳定。首次内层 Escape、重开内层 Enter 分别关闭 **11** 个
不同节点，两次编辑框身份不同；外层首次 Escape 取消关闭时通知总数仍为 **22**。
无需重新点击即可追加 `N`，实际 ⌘Z 恢复原文；第二次 Escape 真正关闭外层后，
其 **9** 个节点各通知一次，主窗口恢复启用、活动和 main。

最终共 **31 个身份、31 个销毁通知、没有重复**，对应节点的销毁订阅均成功。
观察器记录 **77** 个事件、**284** 次订阅；**6** 个不支持结果来自两个 AppKit
原生标题文本的销毁/布局/标题订阅。观察器正常停止、退出 0 后才实际 ⌘Q，
应用退出 0。应用 **16** 份原生副本、**3** 份资源、**10** 个自身 linked 程序集及
严格临时签名通过，无 DYLD 覆盖时实际加载 **8** 个自身 MonoBundle 原生库。

本轮没有重跑 Gallery、完整原生 Window AX 前台组、字体像素专项或 v130 的
全部宿主。v131 自定义标题栏在工具自动重开后的前台/编辑焦点缺项仍保留。
VoiceOver、真实 Dock 点击、菜单栏物理键盘、其他应用的 Hide Others/Show All、
真实 IME 候选、物理拖动、多屏 DPI/Spaces、macOS 15/Intel 和签名发布仍未验收。
完整 macOS 目标保持进行中；本轮更新现有 PR，不切换本地分支。

### v131–v135：外部销毁通知与嵌套模态身份

关闭真实内层模态窗口时，v131 外部观察器成功订阅销毁事件，却没有收到该窗口的
托管节点销毁通知；焦点和文本事件仍正常送达。旧节点未实现 Apple 要求的
`accessibilityNotifiesWhenDestroyed`；基线能力探测直接调用该 getter 时触发
未识别 selector 异常，回归改为先检查 selector 是否存在。v132 补声明后、v133
仅增加发送期间身份识别后，销毁通知仍未送达，没有将中间结果记为修复通过。

v134 临时诊断确认 AppKit 先询问 `isAccessibilitySelectorAllowed:`。提供器断开
后原有检查拒绝了身份查询，发送目标的识别逻辑未被调用。v135 统一退休节点：
断开提供器、从缓存移除、只在 `NSAccessibilityPostNotification` 调用期间允许
识别通知目标，最后恢复不可访问状态；生命周期声明独立于提供器。退休处理幂等，
属性和动作仍拒绝失效提供器，旧节点不会因重新附加相同托管 ID 而复活。
使用公开 AppKit API；临时诊断已移除。协议依据为 Apple 的
[自定义节点销毁通知要求](https://developer.apple.com/documentation/objectivec/nsobject-swift.class/accessibilitynotifieswhendestroyed)。

最终原生公共 AX 协议、读取生命周期 **100/100**、通知生命周期 **22/22** 通过。
新增检查确认退休节点保留生命周期声明、拒绝动作且不再调用原提供器。最终独立
PR 源码窗口 AX 宿主 **14/14**，覆盖模态语义、取消关闭、隐藏复用和排队动作的
隐藏/禁用/移除/关闭负例。Lab 构建 **0 错误、26 个既有警告**，宿主构建 **0 错误、
18 个既有警告**。托管全套 **930/2 跳过**、九组宿主 **113/113** 仍为 v130 的结果，
本轮没有将它们记为 v135 重跑。

最终 Lab PID **13128**：主窗口 **540×430 DIP**，原生外层模态 **500×380 DIP**，
自定义内层 **600×370 DIP**。外部 AX 确认只有最内层启用、拥有 key/main，外层和
主窗口禁用；关闭内层后恢复外层编辑焦点，主窗口仍禁用。实际截图确认布局、
可见按钮和中文内容。内层编辑框现在固定命名为“内层模态窗口编辑器”，输入
`Nested v135 中文🙂 é` 后名称不变，尾部空格及组合字符的 UTF-16 选区为 **16,3**。

第一次内层通过实际 Escape 取消关闭，收到 **11** 个托管节点销毁通知；重新打开
得到新的编辑框身份，实际 Enter 确认关闭再收到 **11** 个，两个编辑框各通知一次。
外层首次 Escape 取消关闭后计数仍为 **22**，文字、活动模态和 owner 禁用状态保留；
继续输入 `N`、Shift-1 得到当前输入源的 `！`，两次 ⌘Z 恢复原文。工具直接
`typeText("!")` 没有插入字符，未计为符号输入通过；实际 Shift-1 单独验证。
外层再次 Escape 真正关闭后，**9** 个对应托管节点各通知一次，主窗口恢复启用、
活动和 main。共 **31 个不同身份、31 个销毁通知、无重复**。通知异步送达，首次
即时计数检查遇到队列尚未排空；后续使用有界等待，完整记录确认结果。

新增 `.tools/macos-window-external-ax.py` 使用公共 AX/CF API，支持只读状态、准确
bundle URL 的 Dock 节点查找和有界通知观察。记录 PID、工具源码哈希、订阅错误、
节点最后一次可读描述和结束状态；不发送输入、不调用重开代理、不请求新权限。
实际 UI 操作通过桌面工具。最终观察器已获已有 AX 权限，**83** 个事件中包括上述
**31** 个销毁通知；**290** 次订阅中 **6** 次不支持来自两个 AppKit 原生标题文本，
各为销毁/布局/标题事件，托管验收节点订阅成功。观察器正常停止、退出 0 后才 ⌘Q；
没有用应用退出后的通知缺失代替窗口存活期间的销毁验收。

v131 原生/自定义退出面板还分别实测 ⌘M、工具重新选择触发的真实 AppKit 重开、
Window.Hide 重开及编辑撤销重做，两进程最终实际 ⌘Q 均退出 0。原生面板的最小化
重开保留句柄、原文、选区 **17,3** 和尺寸；Window.Hide 重开保留该时点的编辑内容，
并恢复活动编辑焦点。自定义面板在工具自动重开
后仍为非活动窗口、编辑焦点未恢复；定向工具输入不能证明前台恢复，因此该路径
仍未验收。准确 Dock bundle URL 已只读定位，但桌面 Dock 查询仍超时，未点击。
v135 的嵌套模态结果不能替代这些恢复场景的缺项。

五轮原生构建复用既有 CMake cache、完成真实 package.complete 并冻结各 **11**
个文件；最终指纹枚举 **511** 个原生源码/配置，比 v130 范围更广，未称为干净
目录重建。v134 是保留 v133 托管程序集的诊断重打包，最终 v135 重新构建独立
源码。最终 **3,195** 个导出文件、四个覆盖文件及冻结负载匹配；应用 **16** 份
原生副本、**3** 份资源、**10** 个自身 linked 程序集及严格临时签名通过。无 DYLD
覆盖的最终 PID 实际加载 **8** 个自身 MonoBundle 原生库。桌面连接中断通过重新
观察当前窗口恢复，未重复触发已执行的打开动作。

本轮未运行 Gallery、完整原生 Window AX 前台组、字体像素专项或 v130 全套宿主。
VoiceOver、真实 Dock 点击恢复、菜单栏物理键盘、其他应用的 Hide Others/Show All、
真实 IME 候选、物理拖动、多屏 DPI/Spaces、macOS 15/Intel 和签名发布仍未验收。
完整 macOS 目标继续进行；本轮更新同一 PR，保持本地 master。

### v130：应用隐藏四场景与最小窗口焦点滚动

应用隐藏夹具在一次真实 AppKit 启动和消息循环内执行所有场景，从
DidFinishLaunching 通知调度检查；模态的 Shown 回调先返回，再在已建立的嵌套循环
里检查。此前每场景停止/重启 NSApp 和在 Shown 中同步检查的路径已移除，没有降低
Active、key window、选区和模态断言。工作区宿主和排除并行 CSS/ToolTip 修改的独立
PR 宿主均 **4/4**，每次有 **12/12** 个匹配 nonce/PID/场景/阶段的前台门控。
原生/自定义标题栏 × 普通/模态各完成两次 AppKit 菜单 PerformKeyEquivalent 隐藏/
Unhide 恢复；句柄、文字、选区、托管 Visible、撤销重做、owner 禁用状态和通知
计数通过。该自动菜单分发结果与 v129 的实际 ⌘H 按键证据分别保留。

Window Lab 的 540×430 基线固定 680 宽且没有滚动容器，实际截图确认右侧与底部
裁切。改为 MaxWidth、按钮换行和纵向 ScrollViewer 后，Tab 仍只露出目标按钮一角：
MakeVisible 漏算了内容根节点的 margin。现在纳入根节点实际布局偏移，再扣除
viewport 原点、滚动和弹性位移，统一得到 extent 坐标；支持直接物理滚动和
IScrollInfo 已移动子节点的路径。新增嵌套目标、双轴、正/零/负 margin、已有偏移、
正反向定位及重复请求回归，同一夹具旧实现 **4/12**、修复后 **12/12**。
夹具最初的坐标查询重载/类型引用编译错误已修正，没有计为产品断言结果。

独立 PR 源码托管检查 **930 通过、2 跳过、0 失败**，共 932 项，冻结目录通过
DYLD_LIBRARY_PATH 明确提供。两项跳过仍为密码预编辑取消和 AppKit 几何集成检查。
最终独立宿主九组 **113/113**：默认菜单 9、系统菜单 4、重开 11、退出 18、真实
原生退出循环 32、窗口 AX 14、Tab 3、生命周期 18、应用隐藏 4；九个进程均退出 0。
隐藏组 PID **98145** 的全部门控都满足 Active 和目标 key window，自动断言运行
期间没有桌面工具操作。最终 SDK 应用构建均 0 错误；已有裁剪/反射警告仍保留。

本轮原生构建复用匹配的既有 CMake cache，完成真实 package.complete 目标后冻结
**11** 个文件，并记录 **301** 个原生源码/配置指纹；没有称为干净目录重建。
公共 AX、读取生命周期 **100/100**、通知生命周期 **22/22** 和原生窗口菜单策略
**26/26** 通过。独立导出源码 **3,194** 个文件及四个工作区实现/夹具覆盖文件核对
稳定；最终 Lab 随后只修正程序尺寸按钮，重新构建和实际检查。两应用各自 **16**
份原生副本、**3** 份资源、**10** 个自身 linked 程序集及严格临时签名通过。

最终 Lab 原生标题栏 PID **99033**、自定义标题栏 PID **99217** 均从 **540×430 DIP**
启动，实际截图、AX 名称与焦点已查看。20 次 Tab 到达最后一个验证操作，滚动到
事件区终点后 19 次 Shift-Tab 返回首个操作，焦点按钮完整可见。中间包原生模态
按钮滚动由约 **35** 修正为 **67 DIP**，自定义同一按钮约 **99 DIP**；首次及最终
包的最小窗口都保留自动换行、完整按钮和纵向可达性。原生中间包还通过空格打开
最后一个显示/隐藏面板，关闭后返回原主窗口焦点。按最新
[界面规范](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
核对可见焦点、内容溢出、标签与键盘操作；原生界面不套用 HTML 特定规则。

程序尺寸按钮现在先切回 Manual，避免启用内容自适应后被重新测量覆盖。
最终两种标题栏都通过实际空格操作从自动尺寸（原生 **708×873**，自定义
**708×905**）切回 **Manual / 840×700**；托管与原生客户区均一致，恢复尺寸同步。
整个最终进程的主内容句柄分别保持 **509469163520**、**518023391744**，结束前为
活动 key window。无 DYLD 覆盖的两个 PID 实际加载 **8** 个自身包内 MonoBundle 库，
最终均通过实际 ⌘Q **退出 0**。基线和中间包的观察单独记录，没有用其替代最后
按钮修正后的验收。

本轮未重建或运行 Gallery，也未重新运行 v127/v128 字体像素专项。Dock 工具仍
超时；真实 Dock 恢复、菜单栏物理键盘、Hide Others/Show All 对其他应用的效果、
外部 AX 通知权限、物理拖动、多屏 DPI、macOS 15/Intel 和签名发布仍未验收。
完整 macOS 目标继续进行；本轮源代码、测试和结果更新同一 PR，保持本地 master。
测试输出在收尾按用户要求清理，删除后的历史路径不作为当前可运行交付文件。

### v129：默认应用菜单、原生重建与本轮收尾

空应用菜单现在提供 AppKit 的 Hide、Hide Others、Show All 和 Quit。Hide 使用 ⌘H，
Hide Others 使用 ⌥⌘H，三个显示命令的目标均为当前 NSApplication；已有自定义应用
菜单继续保留，重复配置不会复制菜单项。修复前默认菜单组 **1/9**，修复后 **9/9**。
Unhide 按 AppKit 文档调用不保证激活的恢复路径；重新成为活动 key window 是单独的
前台验收条件，不把窗口可见等同于激活成功。

本轮从干净原生目录成功构建后才冻结负载。修正 v128 较晚新增 AX 外部线程通知/
拆桥测试遗漏的 lambda 捕获，本轮已实际编译并通过；公共 AX、读取生命周期
**100/100**、通知生命周期 **22/22**、菜单跟踪专项及原生窗口菜单策略 **26/26**
通过。此结果不追溯替换 v128 未编译部分的验收结论。

最终宿主应用的八组检查共 **109/109**：默认菜单 9、系统菜单 4、重新打开 11、
退出 18、真实原生退出循环 32、窗口无障碍 14、Tab 焦点 3、生命周期 18。
使用同一冻结负载及各应用自己的 SDK linked 输出；删除前核对 **2,817** 份源码/
配置指纹、**11** 份冻结文件、两包 **32** 份原生库副本、**6** 份资源和 **6**
份托管程序集，并验证两包的严格临时签名。工作区源文件稳定；这些通过项不包含
另行等待前台激活的应用隐藏四场景。

提交前另从 Git 暂存区导出独立源码，排除同时进行的 CSS Authoring 工作及其测试
引用。该源码托管测试 **918 通过、2 跳过、0 失败**（共 920）；首次启动遗漏
原生库搜索路径，97 项因负载未加载失败，指定冻结目录的 DYLD_LIBRARY_PATH 后
全部失败项消失。跳过项为已明确禁用的密码预编辑取消和 AppKit 几何集成检查，
不计为本轮通过。同一 PR 源码的 macOS 宿主包独立构建成功（0 错误），并再次通过
上述八组 **109/109**；其严格临时签名、16 份原生库副本、3 份资源和 3 份自己的
SDK linked 托管程序集另行核对，未使用同时进行的 CSS Authoring 源码。

最终 Window Lab 原生标题栏 PID **72410**、自定义标题栏 PID **72759** 已实际运行。
应用菜单 AX 显示 Hide、Hide Others、Show All、Quit；两种标题栏模态窗口经实际
⌘H 隐藏、AX Raise 恢复后，中文、Miii、Emoji 和组合字符仍可编辑，⌘Z 与 ⇧⌘Z
可撤销、重做粘贴。原生模态 **640×400** 与自定义最小模态 **500×380** 截图已查看，
按钮和编辑器未裁切。自定义应用的 DidHide/DidUnhide 记录为 Hidden=True/False，
恢复前后主内容句柄 **528671025664** 不变、托管 Visibility 保持 Visible、文字不变。
原生应用已核对实际加载的八个库来自最终包。

自定义模态按钮打开的真实 Window 弹出菜单有跟踪状态：首次成功发送 Escape 关闭
菜单且不请求模态关闭，下一次 Escape 才触发首次关闭取消；随后确认关闭，主窗口
重新可用。两包最终经实际 ⌘Q，原启动命令均 **退出 0**。一次 Escape 在发送前被
桌面工具的状态更新保护拒绝，重新读取后才发送，不计为菜单动作。

新增普通/模态 × 原生/自定义四场景隐藏测试使用 nonce、PID、场景和阶段匹配的前台
许可，并要求 NSApp Active 与目标 key window。原生普通场景曾独立通过两个隐藏/
恢复循环、句柄、选区和撤销重做；最终四场景运行在前台门控超时后停止，**未完成
4/4 验收**，没有降低激活断言。菜单栏 Ctrl+F2、Hide Others/Show All 对其他应用的
实际效果、Dock 点击恢复、根窗口 540 DIP 最小布局、外部 AX 通知权限与真实设备
拖动仍为缺项。v127 的字体选区专项和 v128 的 288 像素检查保留原有范围，不声称
它们在 v129 重新运行；完整 macOS 目标继续进行。

Lab 日志路径从 BundleVersion 派生，支持默认页面的原生标题栏和最小模态入口；
模态继承当前标题栏样式，编辑器提供中文 AX 名称，事件日志记录 PID 和标题栏。
测试产物按用户要求在收尾清理，具体清理核对另记于本文前部。

### v128：AX 布局通知中的生命周期

布局通知会查询缓存节点是否仍挂载。提供程序若在查询里隐藏、关闭窗口或拆除、
替换内容桥，读取保护会返回失败；此前通知代码把这种失败也当作节点销毁，隐藏后
恢复便丢失原 AX 对象身份。真正移除的节点从缓存删除后，旧对象仍持有桥的弱引用，
还可暴露旧窗口，或在同一提供程序 ID 重新出现时访问新节点。

现在通知流程持有原视图、原 NSWindow 和原桥，并在查询及发布销毁通知后核对原内容
是否仍存活可见。原内容不可访问时终止本次通知；真实移除才断开旧对象与桥、从缓存
删除并发布销毁通知。通知及桥设置入口先检查主线程与句柄存活，查询回调之后不再
通过原生裸指针读取窗口。

修复前有效基线：自定义/原生标题栏隐藏回调各 **1** 项失败；自定义标题栏真实移除
**1** 项失败，旧对象仍暴露窗口；关闭回调对照 **1** 项通过，不声称此路径必然崩溃。
新增两种标题栏 × 首次/第二次挂载查询 × 关闭/隐藏/拆桥/替桥/禁用，共 **20** 项，
另有两种标题栏的真实节点移除及相同 ID 重挂载，共 **22/22**。隐藏恢复保留全部节点
身份；移除保留原 root、使旧 child 永久失效、新 child 使用新身份；禁用内容仍可读。
既有读取生命周期 **100/100** 和公共 AX 协议通过。v129 干净构建发现本轮较晚
新增的外部线程通知/拆桥检查漏捕获窗口，最后一次增量构建失败，后续脚本仍执行了
已有测试程序；这部分新增检查在 v128 **未编译、未验收**。22 项通知生命周期与
100 项读取生命周期已在此前成功构建的程序中运行，不包含该较晚新增检查。
Debug CTest 公共 AX / 后台 Window AX **2/2**，后者 **106.38 秒**。

外部接收权限探针使用 Apple AXObserver API：`AXIsProcessTrusted=0`，创建 observer
返回 **0**，真实 AXFocusedUIElement 查询与焦点通知订阅均返回 **-25211 /
kAXErrorAPIDisabled**。没有改变系统权限；上述原生及托管调用检查不替代跨进程
通知接收或 VoiceOver 验收。API 依据为 Apple 的
[AXObserverAddNotification](https://developer.apple.com/documentation/applicationservices/1462089-axobserveraddnotification)
及当前 Xcode `AXUIElement.h` 的 `kAXErrorAPIDisabled` 定义。

v128 真实宿主控件 AX 12、语义 8、裁剪 36、EditControl AX 16、文本导航 42、Window
AX 14、生命周期 18、Tab 3，共 **8 组、149/149**；选区字体定向四组 **4/4**，
实际像素与 IME 插入点检查 **288/288**。默认托管无 filter，**918 通过、2 既有跳过、
总计 920、0 失败、含构建 6.28 秒、退出 0**。没有把真实宿主的通知调用观察等同于
外部订阅成功。

独立原生 Window 前台组 PID **50384**，实际 Return 开始，gate 的
accepted/active/key/main 均为 **1**，完整夹具通过、原 CLI **退出 0**；原 CLI
**67.36 秒**包含前台等待。该进程实际加载包内 **2** 个原生库。断言期间没有桌面
工具操作；历史 v84 后台全组与前台记录保留各自范围，不混为同一运行。

完整原生 Window AX 前台 PID **50511**，gate 的 accepted/active/key/main 均为
**1**，**280/280、原 CLI 137.55 秒、退出 0**，时间包含前台等待。实际加载包内
**2** 个原生库，断言期间没有桌面工具操作；v127 首轮失败仍保留在其原记录中，
本轮通过不作为那次失败因果已查明的证据。

实际桌面 PID **50943**，自定义标题栏的显示/隐藏控制面板与编辑窗口：输入
`通知恢复 · 中文 Miii 🙂 é` 和第二行，实际 ⌘Z/⇧⌘Z 恢复原文/新文；隐藏及折叠的
绑定往返均保留同一原生句柄 **514762831360**，创建 **1**、关闭 **0**，原生可见状态
正确变化。恢复后不重新点击输入框即可粘贴，隐藏恢复追加 ` · 恢复输入🙂` 后撤销；
折叠恢复保留完整 `é` 选区，直接替换 `折叠后🙂 ` 后撤销还原。相关截图和外部 AX
树已查看，不把桌面工具索引当作原生 peer 的对象身份断言。

控制面板标准尺寸 **650×640**、最小 **420×380 DIP**；最小尺寸按内容换行，实际
滚动 **620.20 DIP** 可达底部操作/说明。执行关闭检查后创建 **1**、关闭 **1**，
三种再次显示请求均拒绝、关闭状态可读、句柄为 **0**；结果增加高度后再滚动至
**662.20 DIP**，关闭结果和完整底部说明仍可达。实际 ⌘Q，原 CLI **412.71 秒、
退出 0**。该桌面进程实际加载包内 **8** 个原生库。没有实际运行 Gallery 或声称
本轮两种标题栏的桌面完整验收；两种标题栏的节点通知生命周期由上述 AppKit 夹具覆盖。

Host 构建 **47.50 秒**、Lab 构建 **13.21 秒**；两包各 **16** 份原生 sections、
**3** 份资源、各自 linked 输出的 **3** 个关键程序集、JaliumMacApplication 与严格
深度签名通过。两份原生验证包各 **3** 个 Mach-O sections/签名通过。最终构建、
宿主回归、前台两组与实际桌面的 **2,814** 份源码/配置清单及冻结的 **11** 份原生
负载在验证前后核对一致。清单不证明失败构建的测试源码已进入程序：上述较晚新增的
外线程夹具是例外。产品原生目标此前已构建完成，四份应用及已运行用例的结果保留
原范围；v129 改为构建退出成功后才冻结负载或运行验证。上述权限限制仍是外部通知
接收的缺项。

完整目标保持进行中。

### v126–v127：AX 读取生命周期与选区字宽

v126 补齐内容 AX 的读取生命周期。查询回调可能关闭、隐藏窗口，拆除或替换内容桥；
现在查询及字符串、富文本、子节点遍历和命中测试保留原桥，并在回调后再次核对原视图、
原窗口和可见状态。失效查询返回空结果，部分旧子树也不再继续返回。禁用窗口仍能读取
内容与字体，但拒绝动作；已接受的关闭动作仍报告成功。外部线程不进入 AppKit 查询。

原生夹具新增两种标题栏 × 十类读取路径 × 五种回调变化，共 **100/100**。有效的
修复前定向基线 **11** 项均失败，包含关闭后的对象误用、拆桥后的 SIGSEGV 和旧标签/
富文本/子树继续返回；首版样式负载缺少必填字段的失败不计入有效基线。Debug 公共
AX 协议与完整后台 Window AX 的 CTest **2/2**，后者 **107.06 秒**。v127 原生负载
复用已核验的 v126 **11** 个文件，**302** 份原生源码清单与哈希完全相同。

用户截图的 TextBox 选区为 UTF-16 **0:18**，已包含整行 `窄斜体 · Miii 中文🙂 é`。
问题出在未换行或仅一视觉行的选区使用默认 400/正体命中测试，文字则按实际粗体与
倾斜绘制。现在 `GetCharacterXInLine` 和基类点击命中明确传递控件的字重与字体样式，
TextBox 行高也使用相同参数。既有字体来源仍携带字体族、回退列表及实际字宽，没有
靠额外扩大选区补偿误差。换行符的高亮、滚动定位及拼写下划线也共用修正后的边界。

真实宿主新增四组：SF Pro 粗体窄 Oblique、SF Pro 粗体、Helvetica Neue Italic、
SF Pro 粗体 Oblique / CSS 90.25% 字宽。修复前的有效像素基线中三组失败，一组
字宽近似的 Italic 对照通过；常规字体对照通过。修复后由同字体的原生整形段落
提供参考选区，比较控件实际 `Visual.Render` 与参考的完整 GPU 像素，并核对 IME
插入点。四组 × 常规/指定格式/切回常规 × NoWrap/Wrap（每个逻辑行仍仅一视觉行）
× 1×/2× × 六类选区，**288/288**。选区包含整行、Miii、emoji、组合音标 é、第二行
局部和跨换行范围；这些计数不冒充任意宽度的多视觉行或双向文本完整验收。

完整字体宿主 **34/34**。此前系统字体检查仍要求 `ceil(AppKit.Width)`；在修复前的
v126 原包里同样失败，实际为 **232.45477**，AppKit 为 **232.45477676391602**。
断言改为 **0.001 DIP** 浮点容差，保留真实字形 advance，不修改原生测量来满足旧
取整断言。最初参考图的垂直居中和矩形绘制通道差异已先校准，未把夹具差异当作
产品缺陷或修复通过证据。

| v127 最终检查 | 结果与边界 |
| --- | --- |
| 默认托管 | 无 filter，**918 通过、2 既有跳过、总计 920、0 失败**；含构建 **5.64 秒、退出 0**。跳过为 private pre-edit/password 和 AppKit 变换几何 |
| 相关真实宿主 | 全屏失败 16、生命周期 18、Tab 3、字素键 8、编辑动作 5、编辑菜单 14、控件 AX 12、EditControl AX 16、文本导航 42、Window AX 14、退出 18，共 **11 组、166/166**；另有上述字体 **34/34** |
| 原生前台 Window AX | 首轮 **270/280、退出 1**：一项激活完成后的正常 bounds 断言失败，九项在自定义内容焦点夹具取得前台 key 状态前失败。保持相同产品代码，前台内容焦点定向 **36/36**、激活还原定向 **2/2**；完整重跑 **280/280、118.67 秒、退出 0**。每轮 gate 均记录 accepted/active/key/main 为 1，断言期间没有桌面工具操作。首轮失败的独立因果未证实，不声称消除了全部间歇性问题 |
| 原生标题栏实际桌面 | PID **41371**，**840×690 DIP**；整行 **0:18** 与 é **16:2** 的高亮截图已查看。实际鼠标拖选 `Miii `、替换 `字宽修复🙂 `、⌘Z/⇧⌘Z 均通过。键盘进入/菜单退出、菜单进入/键盘退出两次全屏往返保留原文、选区、字体与还原尺寸；全屏无需重新点击可输入 `全屏字宽🙂`，撤销恢复 é。Tab/Space/Shift+Tab 切换只读，粘贴尝试后文字不变；窗口失活后已重新取得前台并核对原选区。实际 ⌘Q，原 CLI **395.38 秒、退出 0** |
| 自定义标题栏实际桌面 | PID **42641**，最小 **520×580 DIP**；初始第五操作按钮底部裁剪，实际滚动 **161.399 DIP** 后全部操作、状态与说明可达。整行 **0:18**、emoji **13:2**、é **16:2** 的高亮截图已查看；鼠标拖选、`最小字宽🙂 ` 替换和实际撤销/重做通过。键盘进全屏/菜单退出保留原文、选区和最小还原尺寸，全屏无需重新点击可输入 `自定义字宽🙂` 并撤销。只读切换、粘贴尝试后原文不变、Tab/Shift+Tab 返回 é。实际 ⌘Q，原 CLI **379.09 秒、退出 0** |
| 焦点与过程状态 | 两种标题栏的全屏、恢复及最终稳定状态，真实 firstResponder 为输入视图，view/window/app 三条 AX getter 返回同一 AXTextArea，选区 **16:2**。全屏动画中仍观察到窗口 root 中间态；不把稳定状态快照当作全过程焦点或通知订阅通过 |
| 构建与实际负载 | 最终 Host **10.72 秒**、Lab **2.69 秒**，均退出 0；两包各 **16** 份原生 sections、**3** 份资源、各自 linked 输出的 **3** 个关键程序集、JaliumMacApplication 与严格深度签名通过。最终构建、字体/11 宿主/默认托管、原生前台及两桌面各 **2,814** 份源码清单匹配。两桌面实际 PID 各加载包内 **8** 个 dylib，签名 SHA 与冻结 sections 一致；首轮原生 AX PID **39806** 实际加载包内 **2** 个 dylib，此后前台各轮使用同一未改动包 |
| 未计入产品结果的调用 | 中途 export 校验路径写成 `.eng` 返回 2，改用 `eng/apple/check-native-exports.py` 后通过；字体套件 flag 误写复数返回 2，改用 `--window-font` 后才取得最终 34/34；一次临时验证模块直接 import 执行了顶层参数解析而返回错误，随后只提取验证函数。它们均未计入产品验收 |
| 范围 | Gallery 本轮未重建/运行。真实输入法候选窗、VoiceOver、通知订阅、物理 trackpad、多屏/缩放切换、Intel、最低 macOS、Windows 及签名发布环境仍有缺项；整个 macOS 补齐目标保持进行中 |

### v123–v125：内容 AX 焦点与关闭回调的生命周期

全屏切换时 AppKit 可以暂时把 firstResponder 从绘制视图换成 NSWindow，托管编辑器
仍保留逻辑焦点和选区。v123 追踪确认：此时窗口和应用始终激活，窗口/应用 AX 查询
返回 NSWindow，旧内容桥却仍返回编辑器。现在内容 AX 焦点及 AXFocused 同时要求
存活、可见、启用的 key window，其真实 firstResponder 必须是绘制视图；提供程序
回调返回后再次检查这些条件。焦点查询期间保留桥对象，避免关闭回调拆桥导致释放后
继续访问；关闭后旧 NSView 的查询也不会落入 AppKit 的缓存响应者路径。

Window Lab 增加可选 `--window-focus-trace`，只观察 AppKit 通知、下一个主循环与
100 ms 状态变化，记录实际响应者、应用/key/main 状态、三条 AX 查询、原文与选区。
它不改变窗口激活、焦点或布局。全屏动画中的 root 仍是一个可观察的原生中间态，
没有以强制返回托管控件掩盖它，也没有宣称每个时刻的外部焦点均稳定。
Apple 对焦点查询及通知的说明见
[accessibilityFocusedUIElement](https://developer.apple.com/documentation/appkit/nsaccessibilitylayoutarea/accessibilityfocuseduielement?language=objc)与
[focusedUIElementChanged](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/notification/focuseduielementchanged)。
本轮验证 getter 与 Focused 状态，不把它们算作通知订阅的验收。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 有效修复前基线 | v123 前台检查 accepted=1、active/key/main=1。Native 已完成 **4 通过、6 失败**，下一项“焦点查询回调关闭窗口”发生桥对象释放后的 Objective-C 异常，原父进程记录 **-6/SIGABRT**；未完成整个 36 项，不能写成 36 项失败。桌面工具重新绑定曾自动重启同一基线并重现异常，两次没有重复计数 |
| 中途结果 | 后台初版 **0/36** 都是 key window 前置条件未成立，随后限定新组使用真实前台宿主，默认后台 CTest 保留原范围。v124 **30/36**、CLI **9.17 秒、退出 1**；剩余六项发现关闭后的旧 NSView 备用查询。v125 完整组首轮 **272/280、131.36 秒、退出 1**，八项均是旧全屏夹具把主动隐藏后的 key 状态与隐藏前比较；调整比较时点，保留禁止额外 key/main 请求的断言。首次构建目标名和 Lab 内部接口引用错误已修正，未计为通过 |
| 最终完整前台 Window AX | v125 PID **22509**，开始 accepted=1、active/key/main=1，**280/280、原 CLI 120.64 秒、退出 0**，含原有 244 项与新增 **36** 项。覆盖实际输入视图、默认窗口响应者、另一原生文本控件、隐藏/禁用、另一 key window、无托管焦点、查询内替换响应者/隐藏/禁用/关闭、旧引用、非主线程；原有 **40** 项受控全屏进入/退出/成功/失败组同时核对三条内容焦点路径、真实焦点标记和完整 é 选区。受控 delegate 组不冒充真实 Spaces 动画；断言期间无桌面工具读写 |
| 公共原生协议 | CTest Debug **1/1、1.14 秒**，CLI **1.18 秒、退出 0**。本轮未重复运行未改动的 Metal 字体/GPU 全组 |
| 默认托管回归 | 无 filter，**918 通过、2 跳过、0 失败，总计 920**；CLI 含构建 **5.10 秒、退出 0**。两项既有跳过仍为 private pre-edit/password 和 AppKit 变换几何 |
| 真实宿主 | 全屏失败 16、生命周期 18、Tab 3、字素键 8、编辑动作 5、编辑菜单 14、控件 AX 12、EditControl AX 16、文本 AX 导航 42、Window AX 14、退出 18，**11** 组共 **166/166**，父进程均退出 **0**，合计 **103.06 秒**；断言期间无桌面工具操作 |
| 构建与负载 | 最终 Host **3.41 秒**、Lab **2.56 秒**、包装 **0.92 秒**，均退出 **0**；共享串行 SDK 使用 v124 构建目录并重绑 v125 冻结库。两包各 **16** 份原生 sections、**3** 份资源、各自 linked 输出 **3** 个关键程序集、JaliumMacApplication 和严格深度签名通过。Host/Lab/托管各 **2,813** 份源码匹配；原生产品及夹具各 **305** 份，产品编译后只有一份测试源修正，单独重建并核对夹具源码清单 |
| 原生标题栏实际界面 | 最终 PID **24343**、**840×690 DIP**。TextBox 窄斜体及完整 é 选区实际截图已查看；快捷键进入、Window 菜单退出，随后 Window 菜单进入、快捷键退出，均保留内容、字体、选区与还原尺寸。全屏无需重新点击即可输入 `原生全屏输入🙂`，实际 ⌘Z 恢复；动画完成后 Tab/Space/Shift+Tab 启用只读并返回原选区，粘贴被拒绝。一次把按键紧接在退出动画后的批量尝试没有切换只读，不计通过；观察稳定状态后单独验证。实际 ⌘Q，原 CLI **退出 0** |
| 自定义标题栏实际界面 | v123 基线 PID **17214** 已观察 **520×580 DIP** 全屏往返、连续输入/撤销、还原尺寸、完整 é、只读与 Tab，实际 ⌘Q，原 CLI **退出 0**。最终 v125 PID **24734** 在最小窗口中富文本替换 `最小窗口焦点🙂`，实际撤销恢复 Miii 选区、重做后选中完整 é；快捷键进入全屏，截图已查看，无需点击即可输入 `自定义全屏输入🙂`，撤销后恢复原文及选区。首次 AX Focused 仍读 root，追踪显示该时刻 firstResponder 是 NSWindow，内容桥正确为空；随后三个 getter 均返回原 AXTextArea、选区 **20:2**。最后菜单退出动作被锁屏工具拒绝，追踪仍为 FullScreen；最终菜单完整往返、只读/Tab 和还原尺寸未完成，待解锁后重建续验。锁屏下 SIGTERM 未使本轮进程退出，随后仅终止该 PID；原 CLI 记录 **-9/SIGKILL**，不计为正常 ⌘Q 验收 |
| 实际加载与边界 | 原生前台 PID **22509** 实际加载包内 **2** 个 dylib；两个最终 Lab PID 各加载自己包内 **8** 个 dylib，实际签名 SHA 与冻结 Mach-O sections 一致。Gallery 本轮未运行。外部参数化 AX、通知订阅、VoiceOver、物理中文输入法/trackpad、多屏/DPI、Intel、最低系统版本和签名发布仍有缺项；整个 macOS 目标保持进行中 |

### v122：合成斜体导出与字体切换的选区保持

共享字体匹配器对没有斜体 face 的私有字体和部分 SF Pro 字宽实例使用水平剪切。
AppKit 的 RTF 写入会丢弃 CTFont 矩阵，原先导出会变直；现代 AX 的 italic 标记也只
读取 face trait，遗漏实际合成的倾斜。现在从实际字体读取纯水平剪切，RTF 用同一个
字体实例的单位矩阵副本与 NSObliqueness 分别表达字体和倾斜，避免重复剪切；AX
按实际 trait 或剪切判断。真实斜体 face 不额外合成，旧的无 width 私有字体入口仍
按其实际直立字形报告。没有新增公开 API、C ABI 或托管字体参数。

SF Pro 的被测字宽和字重实例可通过 AppKit 的变量 PostScript 名称往返，不需要用
NSExpansion 代替字宽轴。RTF 倾斜值存在平台量化，检查容差为 **0.0005**；相关编码
依据见 Apple 的 [RTF 扩展](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/AttributedStrings/Tasks/RTFAndAttrStrings.html)。
这项结果不表示任意字体轴、任意矩阵或私有字体嵌入已经完成。

真实 Host 另发现富文本格式切换会重置选区。写入相同的有效字体值可新增本地属性，
但不会发出内容通知，导致旧快照有变化时拒绝记录新的 caret/选区。现在原文未变时
仍记录最新位置，后续格式改变及其撤销/重做保持选区；正在进行的文字改变、变更
块与撤销事务继续采用原有路径。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 同一原生字体基线 | 校正独立 AppKit 对照的 UIFont 名称规范化和 float ABI 精度后，修复前 **35/65、0.77 秒、退出 1**；修复后同一 **65/65**。包含原有 9 组、45 组 SF Pro 字重 400/650/700 × 字宽 75/83.2/92.8/100/112.5 × Normal/Italic/Oblique、9 组私有 Andale Mono 及 2 组旧入口 |
| 原生图形与协议 | 每组核对 AX、RTF 解码字体/字号、实际倾斜、单位矩阵、无扩张替代；RTF 解码的 AppKit 绘制与独立原生 RTF 对照在 **1×/2×** 逐字节像素相同。CTest **Debug 3/3、4.06 秒，CLI 4.10 秒、退出 0**；另含公共 AX 协议和完整 Metal 字体 **29,920** 项，未把 GPU 验证冒充全部 RTF 字体轴验收 |
| 富文本选区基线 | 相同字号、族、字重或字宽写入后设置 caret/选区，再切换格式、撤销及重做；新增 **8** 项修复前 **0/8、退出 1**，修复后 **8/8**，保留文档对象、原文及选区 |
| 托管默认及编辑回归 | 默认项目无 filter，**918 通过、2 跳过、0 失败，总计 920**；CLI 含构建 **44.11 秒、退出 0**。两项既有跳过仍为 private pre-edit/password 和 AppKit 变换几何。另开启 native geometry 编译选项，仅运行富文本 Undo、TextChanged、RangeEditing、ChangeBlock 与新选区组，**132/132、3.47 秒、退出 0**；新增 8 项包含在此数中，不重复相加 |
| 新真实宿主 | 两种标题栏 × 三类编辑器 × 系统窄斜体、系统宽 Oblique、私有窄 Oblique 和真实斜体 face，最终 **24/24、18.75 秒、退出 0**；完整/部分 RTF、Normal→slanted 切换、只读查询及每步原文/选区不变。首轮 **22/24** 的两项富文本失败定位到格式更新而非格式查询，补齐托管修复后重建验证 |
| 相关真实宿主 | 新倾斜 24、字体匹配 12、样式 36、EditControl AX 16、文本导航 42、Tab 3、字素键 8、编辑菜单 14、动作 5、生命周期 18、退出 18、实际 terminate 32、控件 AX 12、裁剪 36，**14** 组共 **276/276**，所有父进程退出 **0**；断言期间没有桌面工具操作 |
| 最终构建与负载 | Host **11.17 秒**、Lab **15.17 秒**、包装 **1.26 秒**，均退出 **0**。两包各 **16** 份原生 sections、**3** 份资源、各自 linked 输出的 **3** 个关键程序集、JaliumMacApplication 与严格深度签名通过；原生 **305** 份、Host/默认托管/富文本专项/Lab 各 **2,811** 份构建源码清单一致，清单包括两个打包器 |
| 中途夹具问题 | 原生首版 **20/50** 含 UIFont 别名和 float 精度造成的错误对照，修正并扩展到 65 组后才获取上述修复前基线。Host 首次编译因只读 helper 类型引用产生 **1** 个错误，改用既有 helper 后通过；这些中途结果没有记成最终通过 |
| 原生标题栏桌面 | PID **14676**、**840×690 DIP**；三类编辑器实际窄斜体截图已查看。TextBox 的 `斜体窗口🙂` 和富文本的 `富文本斜体🙂` 替换、实际 ⌘Z/⇧⌘Z 保留字体，撤销恢复 Miii 选区。TextBox 完整 é 选区经 ⌃⌘F 进出全屏保持，恢复原尺寸；Tab/Space/Shift+Tab 切换只读并返回原选区，粘贴被拒绝。EditControl 完整 é 扩缩通过，显式 Tab 插入四空格并可撤销，未记作焦点导航。实际 ⌘Q，原 CLI **退出 0** |
| 自定义标题栏桌面 | PID **14999**、最小 **520×580 DIP**；三类编辑器实际窄斜体截图已查看。新增第五操作按钮换到第二行，初始底部有裁剪，实际向下滚动后所有操作、状态和说明完整可达。富文本替换 `最小斜体🙂`、撤销/重做、完整 é 选区及全屏往返保持字体、内容与还原尺寸；进出全屏首次 AX Focused 均读为窗口 root，截图仍显示选区，实际 Tab 到只读框、Shift+Tab 返回完整 é 并拒绝粘贴。没有宣称全过程 AX 焦点稳定。EditControl 字素扩缩通过。实际 ⌘Q，原 CLI **退出 0** |
| 实际加载及验收边界 | 两个实际 PID 各加载包内 **8** 个 dylib，路径、实际签名 SHA 与冻结 Mach-O sections 一致；收尾再次匹配上述五份源码清单。Gallery 本轮未重建/运行。RTF 倾斜保真在本进程真实 AppKit 和原生像素对照中验证，桌面工具未直接查询任意外部参数化 AX 字体/RTF；完整 CSS 逐字形回退、其他字体轴/矩阵、私有字体嵌入、Windows、Intel、最低系统版本、通知订阅、VoiceOver、真实输入法和物理 trackpad 仍有缺项，整个 macOS 目标保持进行中 |

### v121：绘制、无障碍与 RTF 共用主字体匹配

此前 AX/RTF 按原始族名称调用 NSFont，无法识别私有字体别名、带引号的候选列表或
内部 CSS 字体计划；同一段文字的绘制字体与无障碍字体可能不同，字宽也被遗漏。
现在将既有 Metal CoreText 匹配器抽到原生 core，Metal 与 AppKit 共同调用，保留私有
字体字节、TTC face、数值字重、倾斜、字宽和资源生命周期；没有新增公开 C ABI。
托管桥复用绘制格式缓存，按完整样式 run 的文字解析 CSS，传递实际主字体族、字号、
有效字重/倾斜及字宽。可选 width 支持小数，非法类型或数值拒绝读取；旧 payload
没有 width 时继续使用原来的默认匹配。

NSFont 与 CTFont 的互操作依据见 Apple 的
[CoreText 概览](https://developer.apple.com/library/archive/documentation/StringsTextFonts/Conceptual/CoreText_Programming/Overview/Overview.html)。
本轮字体快照仍表示主字体，没有把每个 glyph 的回退字体伪装成同一字体。
变量轴与字体矩阵的表达、RTF 的扩张/倾斜属性属于后续保真范围，参考
[字体属性](https://developer.apple.com/documentation/coretext/font-attributes?language=objc)与
[RTF 扩展](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/AttributedStrings/Tasks/RTFAndAttrStrings.html)。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 同一原生专项基线 | **3/9、退出 1**；私有 Andale Mono 别名、Helvetica Neue 窄粗体及四类非法 width 失败。最终同一夹具 **9/9**，包含显式 face、窄黑体、通用 monospace，以及 AX font name 与实际 AppKit RTF 解码结果 |
| 托管默认项目 | 最终无 filter，**910 通过、2 跳过、0 失败，总计 912**；新增字体 **13/13**，涵盖三类编辑器的带引号列表、私有 TTC、CSS 主 face/有效字重及 83.2%/92.8% 精确字宽，文字与选区不变；CLI 含构建 **6.08 秒、退出 0**。两个既有跳过仍是 private pre-edit/password 与 AppKit 变换几何。未启用额外 native geometry 项目开关 |
| 真实宿主 | 字体 **12/12**（两种标题栏 × 三类编辑器 × 列表/私有集合），运行时 Condensed→Normal 后 AX 与 RTF 由 HelveticaNeue-CondensedBold 同步变为 HelveticaNeue-Bold；最终专项 **6.44 秒、退出 0**。连同格式 36、字素键 8、裁剪 36、导航 42、EditControl AX 16、控件 AX 12、动作 5、菜单 14、Tab 3、文本导航 7、词导航 20、生命周期 18、退出 18、实际 terminate 32、重开 11，**16** 组共 **290/290**，所有父进程退出 **0**；断言期间无桌面工具操作 |
| 原生 C++ | CTest **Debug 3/3、4.24 秒、退出 0**：公共控件 AX 协议、字体专项与完整 Metal 字体组。Metal **29,920** 项包含系统字体、私有集合/变量配置、修改过的 TTC 字节与注销后保留、有序回退、CSS 范围、字宽/变换、caret 及 1×/2× GPU 对照；没有把这项渲染验证算作全部变量轴的 RTF 验收 |
| 最终构建与包装 | Host 增量 SDK **4.83 秒**、Lab SDK **12.06 秒**，均退出 **0**。Lab 打包器补齐 SDK 只放入 MonoBundle 时缺少的 Resources 验证副本，保留并核对原 SDK 库；最终两包各 **16** 份原生 sections、**3** 份资源、各自 linked 输出的 **3** 个关键程序集、JaliumMacApplication 及严格深度 ad-hoc 签名通过 |
| 中途验证问题 | 原生首次运行器使用错误可执行路径，CTest 首次遗漏 Debug 配置，这两次没有执行断言；随后正确路径的基线和 Debug 最终结果分别记录。托管首轮缺少 Metal 上下文，新增 **13** 项失败、原有 **897** 项通过；补齐夹具后重跑。Lab 首轮控件静态类型错误产生 **8** 个编译错误，修正后构建通过；首次包检查发现 Resources 副本缺失，补齐打包器后重新包装通过。未把这些中途结果当作最终通过 |
| 原生标题栏桌面 | PID **8886**、**840×690 DIP**，三类编辑器实际窄粗体截图与外部 AX bold 读回。TextBox 替换为 `字体验收🙂`、RichTextBox 替换为 `富文字体🙂`，实际 ⌘Z/⇧⌘Z 保留字体与文字。富文本完整 é 选区经 ⌃⌘F 进出全屏仍保持，窗口恢复原尺寸；文本框 Tab/Space/Shift+Tab 启用只读并返回原选区，粘贴被拒绝。EditControl 的 Shift+Left/Right 选中并收起完整 é；其 Tab 明确插入缩进，⌘Z 能撤销，未误记为焦点导航通过。实际 ⌘Q，原 CLI **退出 0** |
| 自定义标题栏桌面 | PID **9197**、最小 **520×580 DIP**，三类编辑器字体截图通过，四个操作按钮在宽度内；向下滚动后状态与说明完整可见。富文本替换 `自定义字体🙂`、撤销重做、é 选区及全屏进出保留字体/内容并恢复最小尺寸；还原的首次 AX Focused 为 root，实际 Tab 到只读框、Shift+Tab 返回原选区并拒绝粘贴，未声称转换全过程的外部焦点稳定。EditControl 完整字素扩缩通过；水平 wheel 工具尝试未移动，实际拖动滚动条能显示行尾 Neue 且文本/选区不变。实际 ⌘Q，原 CLI **退出 0** |
| 当前源码与实际负载 | 收尾匹配原生 **305**、托管 **2,756**、Host **2,036**、Lab **2,017** 份实际依赖源码；构建时全量清单也核对前后不变。Lab 页在 Host/托管测试完成后仅修正其本地静态控件类型，因此收尾按各自编译依赖比较，不把未编入 Host 的 Lab 源码冒充已编译。两个实际 PID 各自加载包内 **8** 个 dylib，路径、签名包 SHA 和冻结 sections 一致。Gallery 本轮未重建/运行；截图已实际查看，产物随后按用户要求清理 |
| 验收边界 | 已对齐主字体匹配，完整 CSS 逐字形回退字体、变量轴/倾斜矩阵的 RTF 保真、私有字体嵌入及公开 UIA 属性中的内部源名称仍有缺项。桌面工具第一次直接选择 TextBox 的 é 得到邻近 emoji，未记为正确选区；改用实际键盘获得完整 é，后续验收按实际 AX 结果记录。Windows、Intel、最低系统版本、任意外部参数化 AX 调用、通知订阅、VoiceOver、实际输入法与物理 trackpad 仍未完成，不表示所有 macOS 行为已经补齐 |

### v120：原生文本样式、RTF 与编辑器字素按键

TextBox、RichTextBox 和 EditControl 的可选样式来源提供实际字体族、字号、字重、倾斜、
前景色、下划线、删除线及声明的对齐、方向与语言。富文本逐段、逐内联构造 UTF-16
样式范围，包含段落换行、表格制表符与行分隔；编辑器沿用实际语法高亮缓存及画笔。
样式相同的相邻范围合并，颜色取实际纯色画笔与透明度。EditControl 原先测量使用
FontWeight/FontStyle、绘制遗漏这两个属性，现在普通文本和语法高亮文本都传给绘制格式。

AppKit 桥新增 `accessibilityAttributedStringForRange`、`accessibilityRTFForRange` 与
`accessibilityStyleRangeForIndex` 的完整公共格式来源。AX 属性采用 Apple 的字体字典、
CGColor 与线型键；RTF 另构造 NSFont、NSColor 和段落属性，不把 AX 键直接当成 RTF 键。
新系统可用时补充独立 bold/italic 属性，旧系统保留字体信息。契约分别见
[带属性文本](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityattributedstring(for:))、
[RTF 范围](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityrtf(for:))
和[样式范围](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilitystylerange(for:))。

新增操作编号 **13/14** 与 **bit 24 StyledText**，保留旧编号、Offscreen bit 23 和 C ABI
**136 字节**结构。范围和 payload 长度有上限；文本以原始 UTF-16 的 base64 传递，
AX 子串查询可以保留半个 surrogate 的原始码元。自定义高亮/画笔回调后重新验证窗口、
附着、可见性、密码状态和文档内容，拒绝陈旧快照。隐藏、分离、关闭及密码目标不公开
这些读入口，只读和禁用文本仍可读取；旧样式来源保持普通带属性字符串的兼容回退。

公开 TextRange provider 补齐常见属性、混合属性哨兵、`FindAttribute` 及 Format 单位的
展开、移动和端点移动。属性范围按查询范围裁剪，相同属性可跨其他格式差异合并；
无支撑的属性返回 unsupported，未声明方向不猜测成 LTR。Windows 包装器将混合哨兵
转成 UIA 保留对象，公开新类型沿用现有类型转发。参考
[UIA 属性编号](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-textattribute-ids)
与[范围移动契约](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-move)。

用实际 NSTextView 做对照确认：EOF 的 AppKit 样式范围为 **0:0**，空 RTF 为 nil；
AppKit 写入/读回 RTF 会把 `é` 规范化为 `é`，CRLF 转成 LF。RTF 回归只在比对时处理
这两种 AppKit 规范化，原始 AX 值与子串仍保持原始 UTF-16，未把这一变化应用到文档。

桌面验证另发现 EditControl 的物理左右键走了 `_caret.Offset ± 1`，即使公开移动命令
已经按字素处理，Shift+Left 仍会把选中的 `é` 拆成 `e`。物理左右键现在复用字素移动，
无 Shift 的左右键分别收起到已有选区的起点/终点；方向键、删除与程序 caret 对完整
CRLF 一起处理。六类组合字符覆盖重音、emoji、肤色、旗帜、ZWJ 家庭与键帽，保留
原有 Command/Option 导航优先级。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 样式基线 | 首批新增托管 **6/6 失败、退出 1**；两种标题栏 × 三类文本 × 六组真实 AppKit **0/36、退出 1**。后增断言未全部重新运行旧负载；中途 RTF 比对的规范化前提已用实际 NSTextView 核验并修正 |
| 字素按键基线 | 桌面观察到 `é` 被 Shift+Left 拆开；相同旧负载新增托管 **31/31 失败、退出 1**，真实 NSEvent 宿主 **0/8、5.03 秒、父进程退出 1** |
| 托管全项目 | 最终无 filter，**897 通过、2 跳过、0 失败，总计 899**；新增样式 **21/21**、字素键 **31/31** 均通过。CLI **54.78 秒**含构建，测试自身 **917 ms**；退出 **0**。两项跳过仍为 private pre-edit/password 与 AppKit 变换几何 |
| 最终真实宿主 | 格式 **36/36、24.11 秒**；祖先裁剪 **36/36、16.92 秒**；导航 **42/42、19.94 秒**；EditControl AX **16/16、7.85 秒**；控件 AX **12/12、6.03 秒**；菜单 **14/14、7.77 秒**；编辑动作 **5/5、2.80 秒**；Tab **3/3、1.88 秒**；文本导航 **7/7、3.91 秒**；按词导航 **20/20、11.94 秒**；新增字素键 **8/8、4.43 秒**；生命周期 **18/18、12.04 秒**；退出 **18/18、17.08 秒**；真实 NSApplication terminate **32/32、18.03 秒**。最终方向键修复负载的 **14** 组共 **267/267**，各父进程退出 **0**；断言期间无桌面工具操作 |
| 原生 C++ | 当前原生样式实现的 CTest **Debug 2/2、187.11 秒、退出 0**；控件 **2.16 秒**、Window **184.94 秒**（Window AX **244/244**）。此后只修复托管方向键，未重新执行相同 C++ 测试；**8** 个 dylib 与 **3** 份资源仍匹配冻结负载 |
| 最终构建和包装 | 方向键修复后的 Host SDK **63.11 秒**、Lab SDK **12.39 秒**、包装 **0.86 秒**，均退出 **0**；两包严格深度 ad-hoc 签名、JaliumMacApplication、各 **16** 份原生 sections、**3** 份资源、**3** 个关键程序集通过核对。程序集比较各自项目的 linked 输出 |
| 原生标题栏桌面 | 最终 PID **4625**、**840×690 DIP 客户区**。截图确认普通 21 号、红色 27 号粗体下划线与斜体，外部 AX Value 带对应格式。选择粗体中文/emoji、粘贴 `格式验收🙂`、实际 ⌘Z/⇧⌘Z 保留格式和选区；只读粘贴被拒绝。⌃⌘F 全屏 **2560×1440** 并还原后保留 é 选区、格式及尺寸，Tab 到只读框、Shift+Tab 返回且文字不变。另查 21 号 Arial Bold Italic 编辑器实际绘制，Shift+Left 收起 é、Shift+Right 选回完整 é；选择 `Bold Italic` 后 Left/Shift+Right 得到 `B`，确认收起到起点。实际 ⌘Q、原 CLI **退出 0** |
| 自定义标题栏桌面 | 最终 PID **4794**、最小客户区 **520×580 DIP**。实际混合格式截图、中文/emoji 替换为 `自定义格式🙂`、撤销重做均保留格式；滚动后操作、状态和底部说明可见。全屏与还原保持文字、格式与 é 选区，回到 **520×580**；返回首个外部 AX Focused 为 root，后续实际键盘查询为文本框，未称为转换全过程外部焦点稳定。视口扩大使外层滚动归零，未声明保存原滚动 offset。Tab/Space/Shift+Tab 启用只读并返回原选区，粘贴被拒绝；编辑器粗斜体截图及 Shift+Left/Right 完整 é 通过；`Bold Italic` 后 Right/Shift+Left 得到 `c`，确认收起到终点。实际 ⌘Q、原 CLI **退出 0** |
| 当前源码与实际负载 | 最终 Host、托管与 Lab 的同一 **2,802** 份源码清单构建前后及收尾一致，包含 CMakeLists.txt；两个实际桌面 PID 各自加载包内 **8** 个 dylib，sections 与冻结负载一致，路径和签名包 SHA 已核对；包内 **3** 个关键程序集与各自 linked 输出、**3** 份资源另核对。Gallery 本轮未重建或运行；旧图形负载的早期观察不替代方向键修复后的最终验收，截图已实际查看，输出随后按用户要求清理 |
| 验收边界 | 已实现常见公共文本格式；私有 CSS 字体集合、完整字体轴/字宽与共享 Metal 字体解析元数据未全部映射到 NSFont。波浪线 RTF 暂按单线处理，渐变等非纯色属性、上划线、背景、阴影、基线偏移、列表/链接/嵌入对象格式边界仍有缺项。第三方无样式来源保留旧 Format 回退；Windows、Intel、最低系统版本、任意外部参数化 AX 调用、通知订阅、VoiceOver、真实输入法与硬件输入仍待验收，不能称为所有 macOS 行为完成 |

### v119：祖先裁剪、可见文本范围与可滚动无障碍树

TextBox、RichTextBox 和 EditControl 的文本几何现在先按自身文本视口，再按所有有效
祖先裁剪过滤。查询复用绘制的 `GetLayoutClip`、`GetChildLayoutClip`、
`GetAdditionalChildLayoutClip`、`LayoutClipIncludesSelf` 与渲染矩阵，包含 RenderOffset、
滚动、部分 ClipEdges、GeometryGroup 子变换及原生 Window 客户区。每次查询重新取得
快照，动态修改几何后立即生效。无效矩形、隐藏/退出中的祖先、不可逆或非有限矩阵
不产生可见范围或命中。

普通轴对齐矩形走直接相交；其他路径复用渲染器展平实现，以元素空间 **1/8 DIP**
控制曲线误差。连续扫描填充区间处理 Nonzero/EvenOdd、凹形、孔洞和很窄的多边形，
不用离散点采样猜测是否可见。公开文本范围先投影实际裁剪梯形的顶点，再计算屏幕
轴对齐包络，避免旋转局部包络后越过祖先裁剪。点击点来自实际可见内部，孔洞与
裁剪掉的文字拒绝命中。通用文本范围省略不可见的来源矩形，不返回占位零矩形。
[UIA 文本矩形契约](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nf-uiautomationcore-itextrangeprovider-getboundingrectangles)
和[可见范围契约](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingtextandtextrange)
分别要求返回可见文本矩形和允许分段的可见范围；文本读取与编辑仍可操作滚动区外的文档。

通用 peer 的 Default/FromClip Offscreen 与点击点采用此计算，显式 Onscreen/Offscreen
保留覆盖语义；一般元素的完整 BoundingRectangle 未改成可见包络。macOS 桥区分
逻辑上可访问和几何上不可见：滚动裁剪的后代留在 AXChildren，可继续读取、选择和
ScrollIntoView；AXVisibleChildren 通过新增 **bit 23 Offscreen** 排除它们，显露后身份
保持不变。原有标志、操作编号及 C ABI **136 字节**结构大小不变。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 修复前基线 | 新增祖先裁剪托管场景 **6/6 失败、退出 1**；Native/Custom × 三类文本控件 × 六组真实 AppKit 场景 **0/36、17.75 秒、父进程退出 1**。后续新增的全部断言未逐一重新跑旧负载 |
| 新裁剪专项 | 最终 **36/36、17.88 秒、父进程退出 0**。覆盖部分/完全祖先裁剪、复合孔洞与填充规则变更、祖先/几何变换和 offset、旋转屏幕投影、滚动后的 AXChildren/VisibleChildren、重新显露及身份、部分 ClipEdges、隐藏及奇异矩阵；各查询不修改文字 |
| 夹具与并行修复 | 首次修复后 **19/36**。夹具的 Cocoa 点重复翻转 Y、滚动场景先缓存 AX 后移除重插目标的错误前提已改正；原生 view 的 IsFlipped 已为 true，真实移除重插应使旧 AX 附着失效。托管全组曾有 **4** 个跨 dispatcher 失败，定位到共享的未冻结 Geometry.Empty；冻结共享空几何并新增跨线程回归后通过 |
| 托管全项目 | 无 filter，**845 通过、2 跳过、0 失败，总计 847**；包含全部 **20** 项新增场景，CLI **75.06 秒**包含构建，测试自身约 **1 秒**，退出 **0**。两项跳过仍为 private pre-edit/password 与 AppKit 变换几何。不能把相对 v118 过滤组增加的总数全部算作本轮新用例 |
| 既有真实宿主 | 导航 **42/42、20.53 秒**；EditControl AX **16/16、8.17 秒**；控件 AX **12/12、6.25 秒**；菜单 **14/14、7.96 秒**；编辑动作 **5/5、2.88 秒**；Tab **3/3、1.95 秒**；文本导航 **7/7、4.02 秒**；按词导航 **20/20、11.23 秒**；生命周期 **18/18、12.13 秒**；退出契约 **18/18、17.03 秒**；真实 NSApplication terminate **32/32、85.51 秒**。含新裁剪专项共 **223/223**，各父进程退出 **0**；宿主断言期间没有桌面工具操作 |
| 原生 C++ | 独立构建两个无障碍目标后，CTest **Debug 2/2、183.98 秒、退出 0**；控件 **1.14 秒**、Window **182.83 秒**。结束后的 sample 未找到 PID，不作为采样证据或测试失败。测试构建后 **8** 个 dylib 和 **3** 份资源仍匹配冻结负载 |
| 构建与包装 | 独立原生负载和新增标志重新构建，通过完整导出检查；三份 shader 资源沿用已有输出。最终 Host SDK **82.57 秒**、Lab SDK **12.29 秒**、包装 **0.68 秒**，均退出 **0**；Lab 顺序复用同一 SDK 构建根。两包严格深度 ad-hoc 签名、JaliumMacApplication、各 **16** 份原生 sections、**3** 份资源及 **3** 个关键程序集匹配。程序集与各自项目的 linked 输出核对，应用相关的 Jalium.UI.MacOS.dll 不能要求跨包 SHA 相同 |
| 原生标题栏桌面 | 最终 PID **94388**、**840×690 DIP 客户区**。富文本实际粘贴 `中文富文本🙂 Native119` 与第二段 `第二段 é`；外部 AX 选择 emoji，粘贴替换得到 `中文富文本替换🙂 Native119`，⌘Z/⇧⌘Z 精确往返。只读后实际粘贴被拒绝、组合字符选区可读；绿灯进入全屏、⌃⌘F 恢复后文本、只读和 `é` 选区保持，截图布局无重叠；实际 ⌘Q 后原 CLI **退出 0** |
| 自定义标题栏桌面 | 最终 PID **94573**、最小客户区 **520×580 DIP**。富文本写入 `中文富文本🙂 Custom119` 与第三段 `第三段 é`，emoji 替换、撤销重做和只读拒绝粘贴已查。滚动后操作、状态与底部说明可到达；⌃⌘F 全屏与还原后 Shift+Left/Right 可收回并重新选中完整 `é`，文字不变。转换刚返回的 AX Focused 曾为窗口 root，后续键盘查询为文本控件，未称为转换全过程外部焦点稳定。另查 EditControl 的外部 AX 值写入和准确撤销重做；TextBox 的 Tab 到只读框、Shift+Tab 返回文本框，值保持 `中文文本框🙂 Custom119\nTab 不改文字 é`，无额外制表符；实际 ⌘Q 后原 CLI **退出 0** |
| 当前源码与实际负载 | 最终 Host、托管和 Lab 的同一 **2,778** 份源码清单在构建前后及收尾均匹配，清单包括程序源码/项目/资源但不包含 CMakeLists.txt。两个最终桌面 PID 各自实际加载 **8** 个 dylib，与各自签名包装 SHA 一致；**3** 个关键程序集与 **3** 份资源另按包核对。Gallery 本轮未重建或运行；桌面截图已实际查看，测试输出按用户要求清理 |
| 验收边界 | 曲线展平未证明与 GPU 像素完全相同；连续扫描可能随复杂路径边数增长，未新增大路径性能基准。重写 IsOffscreenCore 的派生 peer 未自动获得通用计算，未带布局接口的第三方 Text 来源保留旧回退。外部参数化 AX 几何/VisibleChildren、AX 通知订阅、VoiceOver、真实输入法、物理拖动、混合 DPI、多屏、Intel、最低系统版本和实际 Dock/注销/关机仍开放；本轮不表示全部 macOS 行为已经完成 |

### v118：原生文本导航、可见范围与选区替换

TextBox、RichTextBox 与 EditControl 通过可选的布局来源复用既有 AutomationTextProvider，
补齐 AppKit 的 lineForIndex、rangeForLine、rangeForIndex、rangeForPosition、
visibleCharacterRange、insertionPointLineNumber，以及 selectedText/selectedTextRanges。
行来源使用真正的软换行、富文本段落布局和编辑器折叠映射；普通 UTF-16 索引与
实际 caret 在视觉行末的 backward affinity 分别处理。
[Apple 的行范围契约](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityrange%28forline%3A%29)
说明行范围优先包含换行符；
[屏幕位置查询](https://developer.apple.com/documentation/appkit/nsaccessibilityprotocol/accessibilityrange%28for%3A%29-1iudm)
以屏幕坐标寻找字形。本轮原生桥先转换 Cocoa 屏幕点，再反转完整祖先渲染矩阵。

字符查询保留 emoji 和组合字符，CR/LF 字形分别查询。无效、超大、非有限索引/坐标，
不可逆矩阵及已隐藏、移除、关闭的目标被拒绝。只读文本可以读取和选择，禁用目标
拒绝选区与 caret 写入；密码框不公开新增文本导航方法。可选接口保留已有第三方
文本来源的兼容性；它们未自动获得布局能力。

通用 RangeFromPoint 现在返回屏幕点附近的真实 insertion range，公开范围的矩形
使用屏幕坐标及 DPI。三类控件的 GetVisibleRanges 使用文本视口和字形矩形，保留
折叠形成的分段；AppKit 的单个 NSRange 返回这些分段的首尾包络。滚动显露指定文本
而不移动选区；选中文本替换沿正常编辑事务进入撤销/重做。TextBox 的正常 SetText
使用 SetCurrentValue 保留绑定。CoreCLR 与 macOS 运行时对文本元素枚举偏移的
差异曾使后续行误用文首偏移，现显式累计文档偏移。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 原生基线 | Native/Custom × 三类编辑控件 × 六类场景，修复前 **0/36**，父进程退出 **1**。增加选区数组和回调边界后最终为 **42** 项 |
| AppKit 对照 | 在主线程实际查询 NSTextView，核对 UTF-16 emoji、组合字符、ZWJ 家庭 emoji、CRLF、软换行与行范围。此机器的 NSTextView 对 EOF/空尾行范围返回 0:0；Jalium 保留实际 EOF 偏移的零长范围，支持正确定位尾行，不把这一处称为完全相同的返回值 |
| 新原生专项 | **42/42**；补查只读/禁用、空文档 caret、软换行重排、折叠、滚动、两半字形命中、祖先变换、公开 provider 的屏幕/DPI 往返、视觉行末 affinity、单选数组与拒绝多选、抛错及关闭窗口的 TextChanged 回调。最后增量构建后再次 **42/42、20.95 秒、退出 0** |
| 既有真实宿主 | EditControl AX **16/16、9.10 秒**；原生 AX **12/12、6.77 秒**；菜单 **14/14、8.83 秒**；编辑动作 **5/5、3.06 秒**；Tab **3/3、2.09 秒**；文本导航 **7/7、4.28 秒**；单词导航 **20/20、11.94 秒**；生命周期 **18/18、12.09 秒**；退出契约 **18/18、19.40 秒**；实际 AppKit terminate **32/32、19.47 秒**。加上新专项为 **187/187**，所有父进程退出 **0**；实际 GUI 操作与宿主断言分开执行 |
| 相关托管 | **587 通过、2 跳过、0 失败**，总计 **589**；包含全部 **14** 项新导航用例，CLI **2.58 秒、退出 0**。两项跳过仍为私有 pre-edit/密码及 AppKit 几何专项 |
| 原生 C++ | CTest Debug 的控件和 Window 无障碍目标 **2/2、107.79 秒**。首次未指定配置的 Not Run 不计为测试通过；测试构建后 **8** 个 dylib 和 **3** 份资源与冻结负载仍一致 |
| 构建与包装 | v118 原生负载重新构建并通过完整导出检查；三份 shader 资源沿用已有输出。Host 最终增量 SDK **9.84 秒**，Lab SDK **47.48 秒**，均退出 **0**。严格深度 ad-hoc 签名、主应用类、**16** 份原生副本的 sections、**3** 份资源和 **3** 个关键程序集均匹配 |
| 原生标题栏桌面 | PID **88311**，840×690 DIP 客户区。TextBox 的外部 AX 值为 `中文🙂 Native118\n第二行 é`；刷新并重新选择 emoji 后，实际粘贴替换为 `中文替换🙂 Native118\n第二行 é`，⌘Z/⇧⌘Z 精确往返。只读后写入能力撤除，é 选区可读；原生绿灯全屏及 Control-Command-F 恢复后文字、只读与 é 选区保持。首次未刷新 AX 的连批操作选区截图不符，没有计为通过 |
| 实际验证待续 | Mac 在切换富文本前再次锁定。已请求手动解锁；富文本、自定义标题栏、最小尺寸和实际 ⌘Q 的本轮桌面结果仍待完成，需要重新构建后继续。真实 PID 的 **8** 个加载 dylib、**3** 个程序集与 **3** 份资源已匹配签名报告。清理时 SIGTERM 未终止进程，核对 PID/路径后 SIGKILL 关闭唯一自建 Lab，原 CLI 退出 **137**；不计为真实 ⌘Q 通过 |
| 仍待补齐或验证 | 外部 AX 参数化范围查询和通知订阅、VoiceOver、物理中文输入法、混合 DPI/多屏、Intel、最低系统版本、Dock 和注销/关机未由本轮完成；文本样式范围、RTF、RangeFromChild 和更完整 UIA 文本单位导航仍需补齐。可见范围专项覆盖控件自己的文本视口；祖先裁剪与非矩形裁剪仍需单独完善和验证。当前机器为 macOS 27.0.1，本轮未重新运行 v94 原生前台 Window AX 全组 |

最后核对的源码清单：Host **1,730**、托管及链接测试 **2,455**、Lab **1,715**、
原生 **303** 份，均与各自验证构建一致。主要源码 SHA-256：

- `AutomationTextNavigation.cs`：`b7d2d665df1e510a8c7b638d2f5f01dde92b8ed1da56075b805860471ce0f45f`
- `AutomationTextProvider.cs`：`07feb9f6be9fa1c5df4f41e88b24d738f288ce198557b0e4aaa34f2d1b61b7aa`
- `MacOSAccessibilityBridge.cs`：`120de88ba29ec5b1ca396c7a17b08bf460fd184b7a049708311cab719e7f0656`
- `TextBox.Automation.cs`：`860a2504b0142ad929d0214229ed794a5d4a7a312e5a925b28954df19de52736`
- `RichTextBox.Automation.cs`：`3e86aef84e64aaafa15e953db6fd932d024e0b9a4ff11b9a0f99e2f896c1d874`
- `EditControl.Automation.cs`：`92c6c1f58e1a09de5cfc7047e20767c9167158baa235671b440a35c06c7a8675`
- `jalium_accessibility.h`：`f78a7a38d9ed5d7dc5b617dbf1351dd522041a274619d8e3b7748974a4626b0f`
- `platform_apple_accessibility.inc`：`7d7c9be7eec8edaf309ec512ba2e2fdb6d2c61ed89a5c2f36d84fb16f180c872`

### v117：EditControl 的原生文本值、选区和可见区域

v116 实际 AX 只能读到 EditControl 的 Edit 角色，原因是通用 peer 没有 Value/Text
模式。新增专用 peer，复用现有 AutomationTextProvider，向已有 macOS 桥接提供
文档文本、UTF-16 选区、只读状态及可见矩形。空文档、单行和多行均为 AXTextArea。
[Apple 的自定义控件说明](https://developer.apple.com/library/archive/documentation/Accessibility/Conceptual/AccessibilityMacOSX/ImplementingAccessibilityforCustomControls.html)
要求分别实现值的读写、控制允许调用的方法，并发送相关变化通知；本轮在现有
NSAccessibilityElement 及通知桥接上补足 EditControl 的数据来源。

AX 值替换复用编辑事务，支持撤销/重做；Document 同步 Text 时使用 SetCurrentValue
保留绑定。值、选区和 caret 变化通知通过缓存 peer 在用户回调前发出，重复的
相同选区不再通知；用户 TextChanged 回调抛异常或关闭窗口时仍保留已提交的文档。
选区复用现有 grapheme 边界，允许只读文本选择；无效 UTF-16 范围与禁用目标被拒绝。
几何复用字体 shaping 和绘制 caret，按真实文本视口裁剪，跳过滚动区外与折叠行；
ScrollIntoView 使用现有滚动路径即时显露指定范围，不修改选区。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 原生基线 | 同一新增宿主的 Native/Custom × 8 场景，修复前 **0/16、退出 1、9.61 秒**。首次修复 **14/16**；两项失败来自后台 fixture 错误要求 AXFocused，按原生 key window 条件修正后通过 |
| 最终 EditControl 宿主 | **16/16、9.80 秒**，涵盖空/单行/多行、中文与 emoji、CRLF、组合字符选区、无效范围、只读/禁用、真实 AppKit 字形与屏幕坐标、滚动、折叠/展开、隐藏/重开、移除/重新插入、关闭及抛异常/关闭窗口的用户回调 |
| 相关真实宿主 | 既有控件 AX **12/12、6.60 秒**；编辑菜单 **14/14、8.74 秒**；编辑动作 **5/5、3.07 秒**；Tab **3/3、2.05 秒**；生命周期 **18/18、11.10 秒**；退出合同 **18/18、18.02 秒**；实际 NSApplication terminate **32/32、20.10 秒**。连同新增专项为 **118/118**，各父进程退出 **0**，执行期间没有桌面工具操作 |
| 托管回归 | 新增 **11** 项在相关回归内全部通过；相关 Window/退出/拖放/DataObject/Automation/自定义 AX/EditControl/TextBlock/属性回归 **573 通过、2 跳过、0 失败**，总计 **575**，CLI **2.60 秒、退出 0**。两项跳过仍为 private pre-edit/password composition 与 AppKit 变换几何。组合运行的逻辑字体环境可能返回最小宽度矩形，托管专项检查可见性/裁剪/滚动，精确字形宽度由真实 AppKit 专项检查 |
| 构建与负载 | 独立原生构建 **7.81 秒、退出 0**，**8** 个 dylib 完整导出检查通过；**3** 份 shader 资源沿用既有输出，未重新编译 shader。最终 Host SDK **58.97 秒**，Lab SDK **46.85 秒**，均退出 **0**；严格深度签名及 **16** 份原生 sections、**3** 份资源、**3** 个程序集核对通过 |
| Native 实际桌面 | PID **70986**，**840×690 DIP 客户区**。外部 AX 显示文本值和可写能力，写入 `中文🙂 Native117` 与第二行 `第二行 é`；⌘Z 恢复原文。重做后 Tab 保持 EditControl 的缩进语义，焦点留在编辑器，撤销 Tab 精确恢复写入值。只读后 AX 不再 settable，外部改写报 **-10005** 且文本不变，仍可选择 `é`；菜单栏 AX 中只有 Copy/Select All 可用。实际绿色按钮进入全屏，再以 ⌃⌘F 恢复原尺寸，文本、只读状态及组合字符选区保留；⌘Q 后原 CLI 退出 **0** |
| Custom 实际桌面 | PID **71826**，**520×580 DIP 客户区**。外部 AX 写入 `中文🙂 Custom117` 与第二行 `第三行 é`，分开执行 ⌘Z/⇧⌘Z，读回原文及准确写入值；AX 选择 `🙂`。只读移除 setter、外部改写报 **-10005** 且值与选区不变。滚动后操作按钮、状态与说明可到达，无重叠。⌃⌘F 全屏中 Shift+Right 把选区扩展到 `🙂 `；恢复最小窗口后，首次输入被桌面工具的应用变化保护打断，刷新 AX 并使用已暴露的 Raise 动作后，Shift+Left 回到 `🙂`，文字不变；⌘Q 后原 CLI 退出 **0** |
| 当前构建核对 | 两个最终 PID 各自实际加载 **8** 个 dylib，**3** 个关键程序集及 **3** 份资源匹配包装报告。原生 **290**、Host **1,611**、托管 **2,317**、Lab **1,597** 份源码在构建前后和收尾核对一致；Gallery 未在本轮重新构建或运行 |
| 验收边界 | 不把 Custom 转换瞬间的 AX root 焦点、工具 Raise 后的输入恢复，称作无重新激活的连续前台 AX 焦点证明。本轮只有菜单栏 AX 可用状态检查，没有新的弹出 tracking 通知日志。外部 AX 通知订阅、VoiceOver、物理中文输入法、混合 DPI、多屏、Intel、最低系统版本、实际 Dock 与注销/关机仍开放；通用 Text provider 的 RangeFromPoint/GetVisibleRanges 兼容回退及更完整原生文本导航接口也未由本轮补齐 |

五份产品源码 SHA-256（收尾仍与已验证构建一致）：

| 文件 | SHA-256 |
| --- | --- |
| `EditControl.cs` | `f8656615733552648837820c6f4c7c005bb32c6b0a261e1e810819a8a067795a` |
| `EditControl.Automation.cs` | `0468f5b2fa0a1ac3a9de072ae538f632c193970a5a5a3936175c24541b9e34d2` |
| `Automation/EditControlAutomationPeer.cs` | `e0a953b3ce6077961342462354eca01acb9ed3ea92e160272d1c2d2a914d7397` |
| `Automation/MacOS/MacOSAccessibilityBridge.cs` | `f83eaf6d0c5cfdee1280f288ddbdc8f059e4834841f60b3eea83daf9e583617f` |
| `TypeForwards.cs` | `9a9ca7464d49bfafd9a610a542c0f43b2bb1cb62727e882ad52854f45cd16e5d` |

### v114–v116：原生编辑菜单验证、自定义命令与实际窗口

此前 JaliumAppleView 实现了六种编辑 selector，却没有实现对应的可用状态查询。
AppKit 因而把响应这些 selector 的窗口视为可执行，即使没有选区、控件只读或
焦点已移到按钮。[Apple 的菜单验证说明](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/MenuList/Articles/EnablingMenuItems.html)
规定自动菜单验证向动作目标查询 validateMenuItem 或 validateUserInterfaceItem；
现在原生视图通过新的主线程查询回调读取有效托管目标，而不执行编辑动作。

查询区分 TextBox、RichTextBox、EditControl、PasswordBox、Terminal、HexEditor
和自定义标准路由命令。只读文本仍允许复制与全选，密码内容不允许复制或剪切；
异常 CanExecute 返回不可用。自定义查询可能关闭窗口，因此回调返回后再次核对
原 NSWindow 身份和注册状态，避免沿旧指针修改替代窗口。控件没有处理的 macOS
按键再交给现有 CommandManager，保留已处理按键及焦点变更后的排除检查。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 首次自动菜单基线 | Native/Custom × 七类目标，共 **14** 项真实 NSMenu 自动验证，旧实现 **0/14**、退出 **1**、**9.16 秒**。Terminal 关闭自动 shell 启动；菜单专项不修改通用剪贴板 |
| 修复过程 | 首次 **10/14** 暴露只读 TextBox/RichTextBox 不能用关闭的 IME 查询来判断选区，改读实际选区。扩展用例随后发现自定义 Copy 显示可用却未执行，补齐未处理按键的命令衔接；同时验证 PreviewKeyDown 消费后不重复执行 |
| 冷启动问题 | 共享 CSS 属性通知在默认画刷构造时访问 TextBlock，形成 Control/TextBlock 循环初始化，新进程专项一度 **2/14**。对非 UIElement/FrameworkContentElement 提前返回后，保留原布局通知逻辑；最终七类控件的两种标题栏冷启动均通过 |
| 最终真实宿主 | 菜单专项 **14/14、10.48 秒**，含自定义可用/禁用、查询不执行、异常 CanExecute、查询关闭窗口、保留旧视图、禁用/隐藏/重开；既有编辑动作 **5/5、3.26 秒**，Tab **3/3、2.14 秒**，实际 NSApplication terminate **32/32、21.46 秒**，生命周期 **18/18、10.74 秒**。合计 **72/72**，各父进程退出 **0**；执行期间没有桌面工具操作 |
| 最终相关托管回归 | 包含 Window、退出、拖放、DataObject、Automation、自定义 AX，以及 TextBlock 和属性通知，**562 通过、2 跳过、0 失败**，总计 **564**，CLI **38.45 秒、退出 0**。私有 pre-edit 与 AppKit 文本几何继续跳过 |
| 原生、SDK 与签名 | 冻结 **8** 个 dylib，完整导出检查通过，**3** 份 shader 资源沿用既有输出，没有声称重新编译 shader。最终 Host SDK **59.52 秒、退出 0**；Lab v116 SDK **11.99 秒**，包装 **0.94 秒**。包装严格核对主应用类、**16** 份原生库副本的代码/数据 sections、**3** 份资源和 **3** 个关键程序集并完成严格深度 ad-hoc 签名 |
| 验收页修正 | 复用 Window Lab 的中文、PingFang SC、浅青色和四类编辑样例。干净构建缺少三份 shader 资源时包装检查拒绝交付，补上 Lab 资源声明；v115 切换 EditControl 出现标签重叠，v116 给编辑容器与编辑控件明确高度。两种标题栏重新查看顶部和滚动后 **520×580 DIP 客户区**，操作、状态与说明可到达，无该重叠 |
| v116 原生标题栏桌面 | PID **59274**：EditControl 粘贴 `中文编辑器🙂 Native116`，经菜单栏 AX 的 Undo 回到原文、Redo 精确恢复；实际截图核对字形、布局与文本，AX 仅提供编辑器角色和焦点，不能当作 EditControl Value 验收。只读后全选，实际弹出菜单只有 Copy/Select All 可用。记录 **1** 次编辑弹出请求/返回，宿主菜单跟踪开始/结束通知总计 **2** 组；实际 ⌘Q 后原 CLI 退出 **0** |
| v116 自定义标题栏桌面 | PID **59501**：TextBox 粘贴 `中文🙂 Custom116`，菜单栏 AX Undo/Redo 精确往返；Tab 到只读开关、Shift+Tab 返回，文本不变。切换 EditControl 布局正常；密码框为 secure text field，选中后 Copy/Cut 禁用，Tab 到按钮时六项命令均禁用。RichTextBox 粘贴 `中文富文本🙂 Custom116`，实际 ⌘Z/⇧⌘Z 恢复原文与粘贴内容，检查选区、只读菜单及滚动后布局。记录 **2** 组真实弹出请求/返回与跟踪通知；实际 ⌘Q 后原 CLI 退出 **0** |
| 实际加载与源码 | 两个最终 PID 各自实际加载的 **8** 个原生库匹配签名报告，**3** 个关键程序集及 **3** 份资源哈希一致。收尾 Host **1,605**、托管 **2,359**、Lab **1,592**、原生 **288** 份源码仍匹配各自已验证构建。v116 仅复用 v115 的原生负载并修正 Lab，未重复编译原生或执行 v94 前台 Window AX 全组 |
| 验收边界 | v115 的弹出菜单点击曾被桌面工具打断；一次坐标 Undo 仅关闭菜单，未计为执行通过；一次 Return 在 Redo 后增加换行，未计为精确恢复，后由菜单栏 AX 路径明确验证。实际绘制菜单、跟踪通知与菜单栏 AX 命令执行分别记录，不替代物理键盘菜单栏验收。Dock 工具再次 **5.96 秒**超时；实际 Dock、VoiceOver、混合 DPI 多屏、物理输入法/设备、Intel、最低系统版本与系统注销/关机仍开放，当前机器为 **macOS 27.0.1**。空/非文本剪贴板与运行中 Terminal 的实际粘贴也未由本轮桌面操作覆盖 |

最终关键源码 SHA-256：

- `platform_apple.mm`：`63ef59173b3c7c9c8465a6d7ea2fc4d0b687e47e00d0759c8200f7fb8e8070fe`
- `Window.cs`：`5f2a1499d91ee45047e11f6fcab46d64b13def7f6db84f1dd153ffea8be51d73`
- `Window.MacOSEditingCommands.cs`：`01409f7a335def691f63b1e881296c6ea3c6a0b796d0fafa3d1f8decc84608d1`
- `NativePlatformWindow.MacOSEditing.cs`：`3e18b9499d003eff18b3ec8edb5d9c74900fba3dee012c32733e66a6512bf1bc`
- `CssFlowProperties.cs`：`794b7974e9c4de58419bb6a84ab432b67e4dae634bcc5f8a25671f493c163366`
- `WindowEditingLab.cs`：`ae9c211b3c51c90582b69dde2fa74b5b973faae8180430e94c8997d6f5c0fabe`

复现入口为 Host 的 `--window-editing-menu`（单项用 `--window-editing-menu-case=N`）
及 Lab 的 `--window-editing-lab --small-window`，加 `--native-titlebar` 验证另一种
标题栏。清理后需要重新构建完整负载和 SDK；GUI 夹具执行期间不操作桌面。

### v99–v113：Closing 内退出、真实 AppKit 终止与主应用类

真实桌面暴露出直接调用 ShouldTerminate 的夹具没有覆盖的问题：窗口 Closing
回调内发起退出时，TerminateLater 会进入 AppKit 的模态消息循环，而原 Closing
尚未返回；取消决定因而无法完成。旧协商也把 `_isClosing` 视为已经接受关闭，
会提前关闭其他窗口或接受退出。现在 Window 在关闭决定与清理完成后发布内部
通知，协商按顺序等待这个决定；仍在 Closing 中的退出先答复 Cancel，接受后
再排队重试，取消或异常保留窗口及先前已接受窗口的延迟销毁保护。

普通 ⌘Q 的 Closing 再次调用 Terminate 还有另一条递归路径：默认 NSApplication
会提前终止进程，绕过后续取消或资源销毁。新增 `JaliumMacApplication` 的原生
`terminate:` 入口保护；外层调用返回后才接受下一次请求。Gallery、Lab 和测试
宿主设置主应用类并通过 `Initialize()` 初始化，公开 ShutdownMode 保持原有语义。
AppKit 对 Later 模态循环的约定见
[TerminateLater 文档](https://developer.apple.com/documentation/appkit/nsapplication/terminatereply/terminatelater?language=objc)。

| 检查 | 本轮结果与范围 |
| --- | --- |
| Closing 重入托管对照 | 新增 **8** 项，相同夹具只替换旧 Managed DLL：旧负载 **0/8**、退出 **1**、**46.92 秒**；修复后 **8/8**。覆盖取消、异常、接受与延迟销毁、显式及模态推断所有权，以及前一个窗口等待销毁时后一个取消/异常；首轮与策略 15 项及既有 Quit 9 项合计 **32/32**、**49.85 秒** |
| 真实消息循环直接协商对照 | 原有 8 项加重入边界 10 项。相同夹具与原生负载，旧 Managed DLL **8/18**、退出 **1**、**45.53 秒**；最终 v113 **18/18**、退出 **0**、**17.55 秒**。真实窗口与 AppKit 循环，委托/协商直接调用；受控 render 标志和显式完成 teardown 保留为夹具边界 |
| 实际 NSApp.Terminate 对照 | Native/Custom × 8 类情形 × 托管/Objective-C 根入口，共 **32** 项。最终 v113 **32/32**、退出 **0**、**18.58 秒**；同 DLL、原生负载、资源与夹具，只把包内主应用类改回 NSApplication，**20/32**、退出 **1**、**19.62 秒**。失败的 12 项属于普通递归退出、Later 接受和等待时新建窗口后拒绝退出；窗口与资源未清完时默认主应用类提前终止 |
| 实际终止边界与断言 | 覆盖已运行 Closing 的取消、异常、接受后重试、owned 子窗口保护，普通退出回调重入，先前延迟关闭后取消，实际 Later 等待与接受，以及等待时创建新窗口后拒绝并允许新一轮取消。父进程同时要求退出 **0** 与最终 PASS 标记，子进程有 watchdog；后台真实 `[NSApp run]` 不证明前台/Dock 或实际渲染压力。运行中 KVO 主应用类为 `NSKVONotifying_JaliumMacApplication`，按原生对象身份与类继承核对 |
| 最终托管相关回归 | v111 当前源码与重新冻结原生负载，**493 通过、2 跳过、0 失败**，总计 **495**、退出 **0**、**29.58 秒**。TRX 核对重入 **8/8**、策略 **15/15**、自定义元素 AX **13/13**；私有 pre-edit 与 AppKit 文本几何仍跳过 |
| 相关真实宿主复查 | v113 重开 **11/11**、**8.64 秒**，生命周期 **18/18**、**10.24 秒**，两项退出均 **0**；加直接协商 18 项和实际终止 32 项，合计 **79/79**。执行期间没有桌面工具读写；重开委托不能替代实际 Dock 点击 |
| 原生输出与共享工作树 | 首轮完整原生构建 **60** 个任务、**12.21 秒**；后续打包校验发现共享源码新增标题栏接口，旧负载缺导出，重新构建 **9** 个任务、**1.89 秒**、退出 **0**。编译前后 **288** 份原生源码哈希一致；冻结 **8** 个 dylib，**3** 份 shader 资源沿用首轮冻结结果，没有声称重新编译 shader |
| 最终 SDK 与签名 | Host v113 **11.79 秒、7 警告**；Lab v110 **9.94 秒、11 警告**；Gallery v111 **82.79 秒、18 警告**，均 **0 错误、退出 0**。包装核对 **16** 份原生库副本的代码/数据 sections、**3** 份资源和 **3** 个关键托管程序集；逐库签名后严格深度校验。收尾再次核对 Host **1,597**、托管 **1,591**、Lab **19**、Gallery **10**、原生 **288** 份源码，均匹配对应已验证构建 |
| 打包问题及修复 | v105/v107 Lab 第二次实际 ⌘Q 提前退出 **0**，未当作通过。v108 检查实际编译 Info.plist，发现隐藏 `.tools` 项目仍打包 NSApplication；v109 包装断言再次拒绝错误主应用类。v110 显式设置 `AppBundleManifest` 后编译包为 JaliumMacApplication；两个永久包装脚本现在检查最终 plist，不能仅看源文件。v108 诊断进程结束 **137**，不计正常退出 |
| 最终 Lab 实际桌面 | v110 Custom PID **48925**、Native PID **49023**：勾选“关闭回调中发起退出”，实际标题栏关闭及 ⌘Q 各取消一次，Closing 返回并保留原进程；继续键入 ` + custom110` / ` + native110`，原中文与 emoji 保留。Tab 到保护开关、Shift+Tab 回编辑器，文本不变，AX、可见焦点框和状态一致；两种标题栏均查看 **520×580 DIP 客户区** 顶部与滚动后布局，全部操作与历史可到达。键盘解除保护后分别通过实际关闭按钮/⌘Q 接受，Closing 各 **3** 次、句柄归 **0**，CLI 均观察到退出 **0** |
| 最终 Gallery 实际桌面 | v111 PID **51570**：查看真实首页与 Inputs 截图，组件切换正常；粘贴 `中文🙂 Gallery111`，Tab 到第二个输入框、Shift+Tab 返回，文本保留且焦点框可见，⌘K 聚焦搜索。CUA typeText 首次只输入 ASCII，未计作中文键入通过；中文证据来自实际粘贴。实际 ⌘Q 后原 CLI 退出 **0**。Lab 两个 PID 和 Gallery PID 各自实际加载的 **8** 个原生库均来自对应包并匹配签名报告，Gallery **3** 个关键程序集哈希也一致 |
| 未计入通过与仍开放范围 | v99 原窗口在 Closing 内进入嵌套 AppKit 循环，采样证实回调未返回，收尾退出 **137**；早期重复 Init 与错误类身份断言已纠正，实验性 NSApplication.Main 夹具仅退出 0、没有 PASS，已撤回且不计通过。真实消费者的 NSApplication.Main 由最终 Lab/Gallery 桌面检查覆盖。实际 Dock、系统注销/关机、VoiceOver/外部通知订阅、物理设备、混合 DPI 多屏、Intel 与最低系统版本仍待验收；后台激活既有问题继续开放 |

最终产品源码 SHA-256：

- `Window.cs`：`7055cec7cd1de85dff4b164d9aa33caf26d186e5c5b69a778f5c50f68b2e9d5e`
- `MacOSWindowCloseRequest.cs`：`32d4c4b70c8fcd54615fa0f83cf22e0f985f379b5cf4bdd77db2638b035c3833`
- `JaliumMacApplicationDelegate.cs`：`a632cf0a72f7350d783426800dfeecac77381bda553fc9bb238d878d3b0c69fe`
- `JaliumMacApplication.cs`：`b0a26af688b40d7e0e2801ba687d03f7896b27fc440e80fb58f8be8b601844fc`

旧重入托管对照 DLL 为
`e9b1e5442ac04167df359afdb501365d32cb976b06e76c7be24349628fd3148e`。
上述退出修复没有重复执行 v94 前台 Window AX 全组，不能把历史 **244/244**
改写为本轮结果。

重新验收先按既有流程重建原生及 SDK，核对编译包的 NSPrincipalClass。Lab 运行
`--window-quit-lab --small-window`，加 `--native-titlebar` 检查另一种标题栏；勾选
关闭回调内退出与关闭保护，依次检查标题栏关闭、⌘Q、继续编辑和焦点，再解除保护
确认原进程正常退出。自动宿主入口包含 `--window-native-quit`、`--window-quit`、
`--window-reopen` 和 `--window-lifecycle`；完整 GUI 夹具运行期间不操作桌面。

### v95–v98：应用退出策略、延迟销毁与实际 ⌘Q

继续检查发现，旧退出协商把公开 `Application.ShutdownMode` 临时改为
OnExplicitShutdown，再恢复构造请求时保存的值。Closing 因而读取错误策略，
回调或等待销毁期间的新设置也会丢失；重复 Begin 还会再次修改策略。
现在通过内部可释放作用域推迟窗口关闭触发的自动退出，公开策略始终保留应用
最新设置，显式 Shutdown 仍可执行；Begin 只执行一次并返回已有结果。

另一个实际消息循环失败是：先前主窗口仍在等待销毁，后一个窗口取消或抛异常，
退出协商已经返回 Cancel。若此时释放推迟作用域，主窗口完成销毁会触发自动退出，
覆盖 Cancel 并提前停止 AppKit。现在取消请求继续等待已接受窗口的销毁完成后
再释放作用域，不补发异步接受答复。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 策略专项基线 | 新增 **15** 项，旧产品 **3 通过、12 失败**，命令退出 **1**、**31.32 秒**；覆盖公开策略读取、回调改策略、取消/异常、销毁期间修改、Begin 前修改和重复 Begin |
| 策略与既有 Quit 回归 | 首次修复后新专项 **15/15** 与既有 AppKit Quit **9/9**，合计 **24/24**、退出 **0**、**45.20 秒**；保留既有延迟答复与关闭断言，只纠正两处对临时公开策略的旧期待 |
| 真实 AppKit 消息循环对照 | Native/Custom × 接受、取消、后一个窗口取消、后一个窗口异常，共 **8** 项。相同新夹具和原生负载，只替换旧 `Jalium.UI.Managed.dll` 的对照包 **0/8**、退出 **1**、**6.75 秒**；修复包 **8/8**、退出 **0**、**9.10 秒**。旧接受及先前延迟关闭路径提前返回 **0**，计划退出计时器未执行；修复后每项均等到计时器显式 Shutdown 的 **91** |
| 宿主对照的边界 | 窗口和消息循环是真实 AppKit；ShouldTerminate 通过宿主公共委托直接调用，延迟销毁使用受控 render 标志并显式完成 teardown，没有声明实际渲染回调压力或 OS Quit 已由这组夹具覆盖。每项独立子进程，父进程检查实际退出码；执行期间没有桌面工具干扰 |
| 最终相关托管回归 | 显式包含策略及自定义元素 AX 类，**485 通过、2 跳过、0 失败**，总计 **487**，退出 **0**、**31.76 秒**；TRX 核对策略 **15/15**、自定义元素 AX **13/13**。私有 pre-edit 与 AppKit 文本几何继续跳过 |
| 相关真实宿主复查 | 同一修复 SDK 包的重开分组 **11/11**、**8.83 秒**，生命周期分组 **18/18**、**9.85 秒**，两个父进程均退出 **0**；与 Quit 分组合计本轮真实宿主 **37/37**。重开委托夹具不能替代实际 Dock 点击 |
| 构建与负载 | 原生完整构建 **60** 个任务、**14.21 秒**、退出 **0**，冻结 **8** 个 dylib 和 **3** 个资源；Host SDK **46.25 秒**、**17 警告、0 错误**。对照/修复包及最终 Lab 各核对 **16** 份原生库副本的代码/数据 sections、**3** 份资源和严格深度 ad-hoc 签名；编译前后相关源码哈希一致 |
| 验收页修正与最终构建 | 保留既有中文、字体和浅青色样式，三种策略、可取消关闭、状态、编辑和滚动操作区。v96–v97 实测发现焦点文字仍在焦点事件过渡中读取旧值，v98 改为监听焦点属性变化。首次源码生成配置失败已修复；最终 SDK **10.05 秒**、**11 份既有警告、0 错误**，新退出日志使用生成的 JSON 元数据并按应用版本命名 |
| 最终实际桌面操作 | v98 Custom PID **10143**、Native PID **10561**：实际 ⌘Q 返回 Cancel，Closing 读到 OnLastWindowClose，回调选择及公开策略保留 OnExplicitShutdown；继续键入 ` + custom98` / ` + native98`，Tab 到关闭保护、Shift+Tab 回到编辑框，AX 焦点、可见焦点框及状态文字一致。两种标题栏都查看 **520×580 DIP 客户区** 的顶部和滚动后布局，按钮与历史可到达。用键盘解除保护后再次 ⌘Q，关闭计数各 **2**、句柄归 **0**，CLI wait 均观察到退出 **0** |
| 实际加载与未验收项 | 两个最终 PID 的 **8** 个实际加载库均来自 v98 MonoBundle，并逐一匹配签名报告哈希。v96 按路径绑定首次 cgWindowNotFound 后，进程仍存活，按 bundle ID 取得窗口；Dock 工具读取 **5.14 秒**超时，未隐藏窗口后冒充重开验收。实际 Dock 点击、系统注销/关机、VoiceOver、物理设备、混合 DPI 多屏、Intel 和最低系统版本仍开放；后台激活既有问题也未据本轮结果关闭 |

最终测试及 SDK 对应的产品源码 SHA-256：

- `Application.cs`：`6ad55ce403c79a3d5e5a5c624a1e26d00575fac950f826ffe548d5a9b8cf0523`
- `Application.WindowShutdown.cs`：`a286b06d5de66c6d87eef6e31462b83bc81ab7c6b70855ceb11e865189800015`
- `MacOSWindowCloseRequest.cs`：`8470b753d0f3986a223f63902ad979acf5cc37d93b0d9602f0c73c4c3de56e7f`

旧托管对照 DLL 的 SHA-256 为
`8ffd375242a3eefd653b458ac8c23851518a243bb4bcfa70c2874c7e5f92b02f`；
修复宿主 DLL 为 `d1c9b47510ef317ebb5f3642756bde5cc751247eaf59ccda83153e4391860d10`。
原生窗口源码未因这项托管退出修复改动，v94 前台 **244/244** 是上一轮独立记录，
不能改写为本轮重复执行的结果。

重新验证时按既有流程重建完整原生负载与
`.tools/macos-window-lab/MacOSWindowLab.csproj`，包装新的版本后运行
`--window-quit-lab --small-window`；加 `--native-titlebar` 检查另一种标题栏。
保持“取消这次关闭”勾选，按 ⌘Q、继续输入并检查策略；再解除勾选，按 ⌘Q 确认
正常退出。自动宿主入口为 `--window-quit`、`--window-reopen` 和 `--window-lifecycle`。
完整原生 GUI 夹具运行期间不操作桌面；历史构建和日志在测试后按要求清理。

### v88–v94：前台回归与 Window 无障碍写入

独立后台 Window 夹具仍在 Accessory、active=0、key/main=null 时激活失败并以
SIGABRT 结束。原夹具在非激活窗口测试前忽略 owner 激活等待的返回值，本轮改为
必需的前置断言。另为 Window 和 Window AX 夹具增加显式选择的前台应用宿主：
只有 Info.plist 启用时采用 Regular 策略，先显示原生中文等待窗口，默认回车开始。
开始按钮同时检查 NSApp.active、实际 keyWindow 和 mainWindow；不满足条件就
拒绝开始，取消或 180 秒超时不执行测试断言。CLI/CTest 保留后台策略与原有断言。

前台实际回归发现，继承的 AX 写入没有可靠地修改真实 NSWindow 几何。现在两种
标题栏均使用实际 frame 和 origin setter，按客户区 Min/Max 夹紧尺寸，固定尺寸
窗口仍可移动；组合修改先缩放再移动，回写最终位置。缩放回调关闭窗口或修改
新几何时停止旧操作，不覆盖回调结果。旧版 Position、Size、Focused、Main 和
Minimized 写入显式接入相同路径，检查类型、有限数值、能力及存活状态；选择和
最小化到达真实原生动作。进程内 Position 的 NSValue 继续采用 AppKit 下边缘
坐标，保留现有外部 ApplicationServices 转换。旧版 Size 先夹紧再计算位置，
最小和最大约束均保持原上边缘。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 前台宿主前置条件 | v88 未取得真实选择，超时后 accepted=0、active/key/main=0、退出 3，未运行窗口断言。v89 采用公共 activate 后，实际开始 accepted=1、active/key/main=1，完整 Window 通过；没有将超时或 AXPress 请求本身算作测试通过 |
| 首轮完整前台 Window AX | v90 **225/232**；Native 的真实几何、客户区约束、固定尺寸移动、只移动、旧版 Position/Size、回调关闭共六项失败，Custom 的旧版 Position/Size 一项失败。v87 曾失败的两次 Zoom 与排队焦点用例本次通过，但未据此声称间歇根因已定位 |
| 最新源码的专项基线 | 发现并行源码修改后重新构建并核对编译前后哈希。v91 加入四类旧版入口边界，几何专项 **21/34**，Native 九项、Custom 四项失败；源码基线 SHA-256 为 `4039555b880e33969e9ec6c56665d71d1587f48c2e15e1f01f3ad93617cd8c3c` |
| 首次修复与尺寸边界 | v92 加入回调新几何保护后 **36/36**。v93 新增旧版 Size 最小/最大约束锚点，两种标题栏均失败，为 **36/38**；最小值虽达到客户区 480×360，上边缘仍移动，原生外框由 `{{200,708},{640,512}}` 到 `{{200,1018},{480,392}}`，自定义外框由 `{{200,740},{640,480}}` 到 `{{200,1050},{480,360}}`。此失败推动先夹紧、再计算锚点的最终修复 |
| 最终完整前台 Window AX | v94 **244/244**，含新增六类边界 × 两种标题栏的 **12** 项；保留原有状态、全屏、选择、隐藏/禁用、旧引用、线程拒绝与回调断言。PID **90946** 的开始检查 accepted=1、active/key/main=1；执行期间没有桌面工具读写干扰 |
| 最终完整前台 Window | v94 PID **91787** 开始 accepted=1、active/key/main=1，完整原生生命周期通过，含预先最大化、两种标题栏实际 AX 选择与 False 请求拒绝及应用指针一致性。两个应用均结束，CUA 启动的进程没有捕获外部 wait 退出码；不把进程消失写成已观察到退出 0 |
| 其余原生分组 | 最终 **9/9**、**37.41 秒**：平台、Window 属性、菜单追踪、文档菜单、通用 AX、调整尺寸/拖动、系统菜单、初始定位与拖放生命周期。两项完整前台检查单独运行，不能将这些结果改写成后台完整 CTest 全部通过 |
| 相关托管回归 | 同一 v94 冻结原生负载、设置 DYLD_LIBRARY_PATH，**470 通过、2 跳过、0 失败**，总计 472；显式包含自定义元素无障碍专项，TRX 确认 **13/13**。私有 pre-edit 与 AppKit 文本几何仍跳过，不计通过；构建及测试命令退出 **0**，共 **49.70 秒** |
| 构建、签名与实际加载 | 完整原生构建 **59** 个任务、退出 **0**，九条已有原生警告。冻结 **8** 个 dylib 和 **3** 个资源，并保留完整负载标记。两个原生应用各 **3** 份 Mach-O 的代码/数据 sections 及严格深度 ad-hoc 签名通过；运行前后负载哈希一致。AX PID 的 **2** 个实际加载库均来自本轮包且匹配签名清单。Window 进程结束过快，未捕获其 lsof 库清单，不以 AX 进程的加载证明替代 |
| 实际界面与剩余边界 | 查看 v94 原生等待窗口截图及 AX 树，中文标题、说明、状态与开始按钮可见，实际 Return 启动并通过三项前台检查。窗口动作本身由原生进程内夹具执行；未完成外部 Inspector 数值写入、通知订阅、VoiceOver、物理边缘缩放、混合 DPI 多屏、Intel 或最低系统版本验收。后台激活失败继续开放，不声称 Window 或全部 macOS 行为完整 |

v89/v90 原始平台库 SHA-256 为
`9cd8ce5177aeb170b15a437845b001810fd4340abe47583a4f0aa0b245095111`，
签名后为 `4bcbb3984cc1e37c8b119be09e2ee7a96fbe519a60a08c5b97d40a9fc1b91d29`。
当时没有准确的编译前源码快照，不能将该二进制结果绑定到随后变化的工作树源码。
v91 起每次编译均核对前后源码；最终 v94 的平台源码、头文件和夹具哈希为：

- 平台实现：`d3e0ddff6ea90fed1ffedce10f6f59d5127611837bd0287c1e535f6652e1a7e9`
- 平台头文件：`fa6c6759d1f4c306c4f3c9f948aaf1b5be851bc70f17093da1d587ea2f861c33`
- Window AX 夹具：`6756d7373c59db0354328bf54e76bd040dc6fa15187916a717c889febbb570ee`
- Window 夹具：`0dafe6522369188f4bdcf54f39c30c5c074a928ba05e5fa0b7e97821658a00d2`
- 前台等待宿主：`0ff0ef14d10c075ca9087019ebb50093aaab273cfe7c95d6ff54b90f0e13c195`

最终平台库原始哈希为
`0a20bb5cf83cbe083a82150b46ea13ea8177df3cc6bfc84ccca3e93d7a9f5a87`，
签名后为 `a302b685d0adfe6975e2c19e0d4d3de0b03850ac42d3b5660ffba2d689fe3a0d`。
当次构建、基线与日志位于 `artifacts/macos-window-v88`、托管结果位于
`artifacts/macos-window-v94`，冻结库位于 `artifacts/macos-window-native/v94/Debug`。
实际应用位于 `artifacts/macos-window-native-validation`，运行日志为
`/private/tmp/jalium-macos-window-native-v89-*` 至 `v94-*`。测试后按用户要求清理；
保留前台宿主和 `.tools/package-native-window-validation.py`，重新构建后可用
`python3 .tools/package-native-window-validation.py <版本号> <原生输出目录> window`
或 `ax` 重新包装；加 `--frame-only` 仅执行专项，不代替完整组。

### v87：自定义元素的无障碍语义与焦点

v86 实际 Tab 已聚焦拖放来源，但外部 AX Focused 返回窗口容器；具名来源和接收区
没有独立节点。macOS 现在为未提供专用 Peer 的 UIElement 缓存通用 Peer；Focusable
或显式 AutomationProperties.Name、AutomationId、LabeledBy 使它成为原生 Group。
未具备这些语义的布局容器在原生树中展平，标准按钮、编辑器等仍使用各自的 Peer，
不重复暴露内部装饰。通用分组仅提供真实状态和焦点，不虚构 Invoke 或 Value 能力。

名称、标识、标签与 Focusable 后续变化会更新树；先缓存的匿名容器仍能加入、移除
语义并恢复同一身份，子节点的父关系随之更新。CSS visibility:hidden 的显式可见
后代仍可发现，原生 Hidden/Collapsed、display:none 与退出状态继续阻断子树。
隐藏、禁用或移到另一窗口的旧引用不能越过现有动作检查。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 托管基线与专项 | 初始 10 项在旧产品上 **3/10**。首次修复 **8/10**；其中另两项夹具分别假定命中位置在原点、假定取消 Focusable 立即清焦点，改为读取实际 bounds 和处理已有延迟重验后 **10/10**。随后补充 CSS 隐藏容器及缓存语义事件，共 **13** 项，全部在最终相关回归中通过。基线、首次失败与修正后的结果分别保留 |
| 标准控件与相关托管回归 | 前 10 项连同标准 Peer、属性和标题栏回归 **116/116**；最终冻结 v87 原生负载下，窗口、拖放、DataObject、Automation 与显式包含自定义元素专项的筛选共 **470 通过、2 跳过、0 失败**，总计 472 项；TRX 逐项确认新增 **13/13**。原有私有 pre-edit 与 AppKit 文本几何两项仍跳过，不计为通过 |
| 回归筛选与环境核对 | 初次 458 通过的筛选仅命中新增专项 1/13；核对 TRX 后扩展筛选。扩展首轮未设置 DYLD_LIBRARY_PATH，为 465 通过、5 失败、2 跳过，含原生 core 库 DllNotFoundException 及文本/图像表示失败；将加载路径指向同一 v87 冻结负载后得到最终 470 通过、2 跳过，产品与断言未改。首轮结果保留，不计为成功验收 |
| 完整原生首轮 | **8/10、154.93 秒**；后台激活 active=0、policy=Accessory、key/main=null 失败。Window AX **230/232**：Native 延迟还原中的两次 Zoom 丢失普通几何、Custom 排队焦点/main 激活排除目标；其余八组通过，其中拖放 **122/122** |
| 原生独立复查 | 夹具加入按完整名称选用例和失败时的几何/key/main 诊断，保留原断言；上述两项分别以两种标题栏独立运行，**4/4**。之后完整 Window AX **232/232、104.74 秒**。本轮未更改原生产品逻辑，也未消除或定位这些间歇失败的根因；首轮失败与复查通过分开记录，不能称完整十组通过 |
| 构建与实际负载 | 新建原生构建完成 **76** 个任务；Window Lab SDK 构建 **58.77 秒、21 条已有警告、0 错误**。包装脚本严格深度 ad-hoc 签名通过，**16** 份原生库副本的 Mach-O sections、**10** 份自身 linked 程序集与 **3** 份资源一致。两个实测 PID 加载的 **8** 个原生库均来自 v87 MonoBundle，签名文件哈希与清单一致 |
| 实际自定义窗口 | PID **61011**，来源、接收区分别是“拖放样例来源”“拖放接收区”节点。Tab 与 Shift+Tab 的源 AX Focused 和绿色轮廓一致；取消框 Space 开/关为 1/0，Tab 到复查按钮、Enter 新增 RetainedRead。两次有效 Enter/Drop 接收第 1、2 次，均保留中文🙂、HTML、2 个文件和原始自定义对象 original=true，源返回 Move / LeftButton=Released；中间取消取得 Enter/Leave、None，未新增 Drop，随后重试接收第 2 次 |
| 实际原生窗口 | 独立 PID **61629** 同样取得独立源与目标节点、Tab/Shift+Tab 源焦点、取消框 Space 1/0，以及复查按钮 Enter 的 RetainedRead。两次有效 Drop、取消无新增接收、取消后重试均确认；源结果依次为 Move、None、Move，结束后 LeftButton=Released，复读仍为原始数据。独立启动后的这些结果不替代 v86 原生子窗口未取得前台选择的失败记录 |
| 最小尺寸与关闭 | 两种标题栏通过 `--small-window` 启动，输入记录 Width=520、Height=580 DIP，IsActive/nativeKey/nativeMain 均 true、scale=1。实际截图确认中文、全部选项、三项操作按钮及其焦点可见；滚动后底部 RetainedRead 完整可读。此项验收的是最小尺寸布局，未验收物理边缘缩放。两个窗口用各自实际关闭按钮结束，均记录 Closed、进程退出 **0** |
| 剩余边界 | 通用语义动态变化已做托管树和事件回归，尚未观察 VoiceOver 或外部通知订阅端；本轮未做跨窗口、跨应用、真实修饰键/Escape、物理拖动缩放、多屏 DPI、Intel 或最低 macOS 版本验收。Window 后台激活和首轮间歇 AX 失败仍保留开放 |

最终托管源码 SHA-256：

- UIElement：`b5d50ce6ad2fd277104e5a12a50bf01d0ed7eb956d946572aa61bf008a91f72f`
- AutomationPeer：`c9acec473efcb2cc44438e7837dd377a7f9a92c9f1a00061d6e7efdb032bc054`
- AutomationProperties：`ad98b96a373e14ce87f1ea10113207b0a08f094f199bd57e2eb5ecd6b881d0fa`
- MacOSAccessibilityBridge：`d79e07ab29ad118ccd8225a92f49cd53dda7de0720a4a88d2854c5c26282c0e5`
- 自定义元素夹具：`33416f5d73cf765b32257d410ffa0dbaa74b5e8238a5cd7adb13f3b4b6eaa859`

原生平台源码仍为
`2e3a697eb83b9cab1a62dd7ada7344b0d84ab60346b85480e86acadb51b74dfd`；
加入选择与诊断后的 Window AX 夹具为
`f116d8cc4aade0dc753dbc3942867d77bb985049f830ccf5aea9a22ae3d9e352`。
当次负载和日志位于 `artifacts/macos-window-native/v87/Debug`、
`artifacts/macos-window-v87` 及 `/private/tmp/jalium-macos-window-lab-v87-*`，
应用为 `artifacts/macos-window-lab/Jalium.Window.Lab.v87.app`；测试完成后按用户要求
清理，不作为现存交付路径。保留 `.tools/macos-window-lab` 与包装脚本，按原生/SDK
构建流程重新生成后，传入 `--window-drag-lab --small-window`；原生标题栏再加
`--native-titlebar`，可复查源焦点、取消/重试及最小布局。

### v85–v86：拖放启动与异常清理

AppKit 的 beginDraggingSession 可以在返回前同步处理释放或 Escape。原查询监听在
begin 返回后才安装，导致这段时间的 Continue / Cancel / Drop 未经过应用查询。
现在在 begin 之前安装，只在配置查询回调时监听；旧 `jalium_drag_begin` 无查询入口
仍让 AppKit 接收终止事件。无法注册必需监听时拒绝启动，不调用 begin 或应用回调。
begin 与嵌套运行循环的 NSException 转为 InvalidState / None，终结路径仅撤销本次
源操作并移除有效监听，不清除同一窗口独立的传入目标访问。完成后关闭窗口保留
既有结果，启动失败后可以再次拖动。

托管输入清理用 finally 释放本窗口的按下与 CSS 状态，即使 LostMouseCapture 回调
抛错也执行；回调将捕获移交另一窗口时，保留后者的捕获和鼠标按钮状态。来源结束
撤销所有匹配目标，即使某个 DragLeave 抛错也继续撤销和分离其余懒读数据；单个错误
保留原异常与堆栈，多个错误合并报告。相同窗口上的重入替代访问仍按对象身份保护。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 启动边界基线 | 在 v84 产品上新增两种标题栏各 4 项：启动内 Cancel 释放、Continue 释放、Drop Escape 和 begin 抛出 NSException，**0/8**。首次修复后的全组 **110/116**，另 6 项来自夹具以后台窗口未收到普通 MouseUp 推断监听未移除；改为记录实际注册/移除的专属监听 token，继续保留查询、事件吞吐、结果和重试断言，随后 **116/116**。首轮失败没有改写为通过 |
| 可选查询与状态隔离基线 | 两种标题栏各 3 项：旧无查询入口、必需监听注册返回 nil、源启动失败保留传入目标，**0/6**，其中 4 项断言失败、2 项因移除 nil 监听引发 NSException / SIGABRT。修复后完整原生拖放 **122/122**，每项使用独立进程和真实自有 NSWindow/NSView；受控启动和 pasteboard 不等同实际系统拖放 |
| 托管清理基线 | LostMouseCapture 抛错、另一窗口捕获保留、多个目标中的 Leave 抛错，在旧实现上 **0/3**，修复后 **3/3**；随后扩展单错误保持和多错误聚合两种路径 |
| 最终相关原生回归 | **8/8**、**35.97 秒**：基础平台、窗口属性、菜单追踪、通用 AX、调整尺寸/拖动、系统菜单、初始定位和拖放生命周期。拖放组含 **122/122**。本次未执行依赖前台条件的完整 Window/Window AX 组，不能声明完整十组通过 |
| 最终托管回归 | 冻结 v86 原生负载下 **351 通过、2 跳过、0 失败**，共 353 项；覆盖窗口行为、可见性、文本导航、拖放路由、数据契约及新清理边界。跳过的私有 pre-edit 和 AppKit 文本几何不计为通过 |
| SDK 构建与签名 | 首轮已生成应用主体，但 SDK Codesign 任务停滞；取消后退出 1，**6 分 23.22 秒**、21 条已有编译/裁剪警告及 1 条取消警告、0 编译错误，该次不计为构建成功。随后同一构建根目录用单节点、禁用构建服务器并仅在该命令设置 EnableCodeSigning=false 重试，**1.91 秒**、0 警告/错误、构建成功；原有包装脚本另行完成严格深度 ad-hoc 签名。最终应用 **16** 份原生库的 Mach-O sections、**10** 个自身 linked 程序集、**3** 个资源和导出检查通过；PID **42400** 实际加载的 **8** 个原生库均来自本轮应用且与打包清单哈希一致 |
| v86 实际自定义窗口 | 两次有效 Enter/Drop 分别接收第 1、2 次，源均返回 Move / LeftButton=Released；中间勾选取消后出现 Enter/Leave，返回 None 且无新增 Drop。取消后首条工具重试返回 Copy 但没有目标 Enter/Drop，保留该无接收结果；刷新截图、调整路径后的重试才取得第二次实际 Drop。鼠标及键盘 Enter 各复读一次中文、HTML、2 个文件和原始自定义对象，original=true；没有以 SourceReturned 代替目标接收 |
| v86 键盘与画面 | 自定义窗口 **560×680 DIP** 画面完整查看；Tab 到源区域及取消框、Shift+Tab 返回源区域的绿色轮廓可见；取消框 AX Focused 正确，Space 开/关、Tab 到复查按钮及 Enter 新增 RetainedRead 均实际观察。源区域 AX Focused 回落到窗口容器，尚未补齐其独立语义节点。右下角缩小操作未改变尺寸，因此没有接受为 **520×580 DIP** 最小布局或真实边缘缩放验收 |
| v86 原生窗口与限制 | 原生标题栏子窗口两次源启动均返回 None / LeftButton=Released，没有目标 Enter/Drop；输入记录中 IsActive、nativeKey、nativeMain 为 false。公开 Raise 动作及标题栏选择未取得已确认的前台选择，Dock 工具访问超时。原生子窗口的自拖、最小尺寸、键盘，以及本轮跨窗口/跨应用拖放均未计为通过；前一轮 v84 有效实际结果仍按其原负载单独保留。两窗口分别用实际关闭按钮结束，记录均有 Closed，最后 PID **42400** 正常退出 **0** |

本轮还核对当前 Xcode SDK 的 AppKit/NSApplication.h：公开 activate 不保证立即或
最终成为前台应用，协作激活依赖原前台应用先让出激活权。现有原生 Window 夹具
使用 Accessory policy，显示 owner 后未确认 key window 前置条件就继续验证，因此
需要在可用桌面下补查可靠的前台/协作条件。该证据解释了验证依赖，尚不能证明
v84 后台激活失败的根因已消除；本轮没有改产品以强制抢占焦点，也没有将失败跳过
改写为通过。

最终平台源码 SHA-256：
`2e3a697eb83b9cab1a62dd7ada7344b0d84ab60346b85480e86acadb51b74dfd`；
拖放夹具源码：
`d231cfaca417f8e7359badee86702ebe3e0ea7b8f2e9bf3f62aeefd8bb324cf4`。
当次冻结负载位于 `artifacts/macos-window-native/v86/Debug`；v85 构建根目录中的最终
拖放日志为 `native-drag-v86.log`，v86 最终 CTest 与 TRX 位于
`artifacts/macos-window-v86`。这些路径属于本轮验收来源，按用户要求测试后清理。

### v80–v84：接通托管拖放、完整数据与取消提交顺序

此前 macOS 原生 view 已接收拖放，但 `Window` 构造器未注册公共
`DragDrop.DoDragDrop` 平台处理器，托管源立即返回 None；鼠标移动又把全部按钮
误设为 Released。现在两个入口均接通，原生鼠标事件保留完整按钮状态，AppKit
消费终止事件后清理本窗口的按下与捕获，不合成 MouseUp 或 Button.Click。
源支持 Preview/Bubble 查询与反馈，目标覆盖 Enter、Over、Leave 和 Drop，
回调中的隐藏、禁用、销毁、重入和目标树变化均重新核对当前会话。

数据由拖放与剪贴板共用跨平台编码器。原生提供按目标 visit token 读取格式与
源身份的两个新 C ABI；promised data 回调后再次检查目标代次。其他来源保留
格式清单、按需读取，每种格式只读一次，Drop 前保存所有表示，Leave 后不再
访问原生 pasteboard。文件列表使用每个 URL 一个 NSDraggingItem，并聚合目标
多个 pasteboard item；PNG/TIFF、WAV、HTML/RTF、CSV/Xaml、原始字节/流和注册
格式各有回归。同进程源仍交付原始 IDataObject，自定义对象无需经过 JSON 往返。
Apple 的 [NSPasteboardItem 文档](https://developer.apple.com/documentation/appkit/nspasteboarditem)
说明其有效期取决于 pasteboard 所有者，因此保存 Drop 数据与清理读者分别处理。

本机 UTType 实验确认未知 MIME 会转为小写，破坏注册格式中大小写敏感的 Base64
名称；最终库用小写 hex 包装这类动态 MIME 并在回读时恢复，新增真实自有
pasteboard 的格式名及数据往返断言。内置类型仍使用对应系统 UTI。

实际 v81/v83 发现查询已返回 Cancel，MouseUp 仍导致 Drop。v82 的本地事件监听
修复仅在受控 sendEvent 路径有效，v83 桌面复测仍失败，不能算已修复。v84 新夹具
重放 AppKit 绕过本地监听而调用 movedToPoint 的释放路径，两种标题栏的基线
均失败。原生源现在保留 Cancel，请求的操作掩码立即为 None，拥有的目标拒绝
prepare/perform，迟到的结束操作不能恢复已取消结果；仍投递 Escape 让 AppKit
正常结束追踪。v84 实际窗口已分别确认没有 Drop、返回 None，随后可重新拖动。

| 检查 | 结果与范围 |
| --- | --- |
| 托管相关回归 | 最终 v84 **347 通过、2 跳过、0 失败**，包括新的公共 Window 入口、多格式、Preview/Bubble、重入、旧 token、生命周期和鼠标捕获检查。首次未指定原生路径的运行有 4 项失败；配置 DYLD_LIBRARY_PATH 后通过，初始日志单独记录，不算源码修复。两个跳过项为既有原生组合文本与文本几何检查 |
| 原生拖放 | v83 **106/106**；新增原生追踪取消基线 **0/2**，修复后最终 v84 **108/108**，随后 CTest 拖放组也通过。夹具使用真实自有窗口、pasteboard 与受控会话，不代表跨应用或物理设备验收 |
| 完整原生组 | v80 首轮 **9/10**，v81 **10/10、142.92 秒**。最终 v84 **9/10、153.67 秒**；Window 后台激活 active=0、policy=Accessory、key/main=null，独立复查仍失败 **0/1、18.21 秒**。其余属性、菜单、AX、缩放、启动及拖放组通过。没有修改产品激活逻辑，也未宣称找到或消除该问题根因 |
| 构建与负载 | 最终 Window Lab **0 错误、21 警告**；新增 C ABI 导出检查通过；签名和 16 份原生库副本、3 份着色器资源、10 份 SDK 程序集一致。PID **17408** 实际加载的 8 个原生库均来自 v84 MonoBundle，签名后的文件哈希与打包报告一致 |
| 实际 AppKit 拖入 | PID **17408**，两种标题栏均接收中文 emoji、HTML、两个文件和原始自定义对象；结束后“复查接收数据”内容与对象身份不变。自定义窗口取消从 0 次接收返回 None，随后普通拖动到 1 次、请求 Drop 到 2 次；原生窗口成功到 1 次、取消保持 1 次，复读保留数据；07:58:24 自定义→原生的跨窗口 Enter/Drop 到原生第 2 次，原始对象及全部表示保留，源返回 Move |
| 实测边界 | 反方向原生→自定义的工具尝试仅有源 Copy，没有自定义目标 Enter/Drop，目标接收及数据去向未确认，未计通过；随后自定义→原生有独立完整接收记录。新面板的自定义标题栏 **520×580 DIP** 截图确认所有操作按钮与接收区可见，记录区可滚动；键盘补查中桌面再次锁定，未计为通过。物理 Option / Option+Command、Escape、跨应用数据与反馈、外部 promised data、VoiceOver、Intel 和最低 macOS 版本仍待验收 |

最终原生源码 SHA-256：
`12797e168456e7b289c034fa32c7b066b74484f3dac5979d94254f8f985f6026`；
拖放夹具源码：
`9d67ddefaf12d13adeb76314651d9f56cdad090ac7b2e31cb8e289d5a1ae1bc1`。
两者在 v84 SDK 打包前冻结。上述 build、app、JSON、TRX 与临时日志路径属于本轮
验收来源，按用户要求测试后清理；当前保留源代码、夹具与本节结论。

重新构建 Lab 时，在现有原生/SDK构建流程中准备冻结负载，使用
`.tools/macos-window-lab/MacOSWindowLab.csproj` 和
`.tools/package-window-lab.py <新版本号> <构建根目录>`，运行应用可执行文件并传入
`--window-drag-lab`。拖动顶部样例到接收区，复查数据；勾选“移动时取消”确认接收
计数不变且源返回 None，再关闭选项重复拖动；“打开另一窗口”创建另一种标题栏。
“本地读取样例”只验证本地读取，不能替代拖放结果。执行 GUI 夹具期间不操作桌面。

### v77–v79：拖放会话隔离与实际全屏输入

继续检查发现，旧目标的 Leave / Drop 回调重入相同 AppKit 序号时，旧清理和 effect
响应仍可覆盖新目标；上一源会话的迟到回调也可能发生在新 begin 返回之前。
目标现在分开保存 AppKit 序号与每次 Enter 的非零框架响应 token，在一次访问内保持
token 稳定，回调后的清理再核对访问代次。源会话用弱引用身份集合识别已返回的旧
NSDraggingSession，不长期持有历史会话，仍接受当前会话在 begin 返回前的有效回调。

补齐 [draggingExited 的 nullable sender](https://developer.apple.com/documentation/appkit/nsdraggingdestination/draggingexited(_:))、
draggingEnded 的终结清理，以及 [prepareForDragOperation / performDragOperation](https://developer.apple.com/documentation/appkit/nsdraggingdestination/performdragoperation(_:))
的目标存活、当前会话和允许操作检查。隐藏、禁用或销毁使旧目标失效，重新显示必须
重新 Enter。目标选择的 Copy 不缩小源允许的 Copy | Move；完成回调关闭窗口仍保留
已经取得的结果。

两种标题栏各 40 项独立进程检查覆盖原有销毁路径、文本 Enter/Over/Drop 和中文
emoji UTF-8 数据、旧序号、原生零序号、隐藏/禁用中断、来源身份、重复完成、嵌套
启动、事件与线程前置条件、同序号重入的旧 effect、终结/准备/空退出回调及 begin
返回前的当前或历史源回调。有效相同夹具在初版会话修复上 **68/80**，最终 **80/80**；
12 项都是结果断言失败，没有崩溃或超时。早期夹具直接调用未实现的可选选择器，
造成的两次人工 SIGABRT 已排除；有效基线先检查 respondsToSelector，再断言缺失
终结清理。没有把这两次夹具错误计为产品崩溃。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 原生完整回归 | 首轮 **9/10**、**152.30 秒**，Window AX **210/230**。调用栈与独立 **12/32** 诊断确认，20 项失败来自夹具把 AppKit `_finishDeminiaturizeFromDock:` 的自动选择计为框架提前/重复选择；通知已完成，真实状态已还原。现在按直接调用者所属平台库区分框架与原生调用，仍调用原方法并保留真实通知、状态、几何和选择时序断言。两种标题栏的正反计数对照 **2/2** 通过，显式框架提前请求仍被捕获。产品未为此修改；修正夹具后完整十组 **10/10**、**136.26 秒**，Window AX **232/232**、拖放 **80/80**。首轮失败和诊断单独保留，不把它们改写成通过 |
| 托管回归 | 最终负载 **135/135**、**128.70 秒**：全屏失败 16、最小化 24、定位 14、菜单 4、属性 10、前台属性 2、标题栏 10、默认/取消 12、Window AX 14、生命周期 18、reopen 11；本轮两项前台检查也通过 |
| 构建与负载 | 8 个原生库、完成标记和导出检查通过；最终宿主与 Lab 各 **16** 份原生库的 Mach-O sections / 导出、**10** 个自身 SDK linked 程序集、**3** 个资源和严格深度 ad-hoc 签名通过。Lab PID **77946** 实际加载的 **8** 个原生库均来自本轮应用，逐文件与打包清单一致。宿主 **17** 条、Lab **20** 条已有编译/裁剪/反射警告，均 0 错误；首次原生全量构建保留 4 条 Metal 枚举组合弃用警告 |
| v78 实际全屏 | 独立窗口 PID **63546** 的 Custom 完整进出，PID **65123** 的 Native 完整进出，均为 **680×600 → 2560×1440 → 680×600 DIP**；进出后直接选择并粘贴中文与 emoji，无需点击编辑框。Native 另完成 Control+Command+F 的 **520×580 → 2560×1440 → 520×580** 循环。两种标题栏最小布局、Tab 到首按钮、Shift+Tab 返回编辑框和绿色焦点轮廓均已查看 |
| 最终 v79 实际全屏 | PID **77946**，Custom 按钮进出 **680×600 → 2560×1440 → 680×600**，Native 快捷键进出 **520×580 → 2560×1440 → 520×580**。四个转换后编辑记录分别验证托管/原生全屏、key/main、渲染视图响应、编辑焦点、中文 emoji 和 RestoreBounds；均未重新点击编辑框。两种 **520×580 DIP** 布局的五个按钮完整可见，必要时垂直滚动，双向 Tab 与截图一致。Cmd+Q 后原 PID 退出 0 |
| 实测中断与边界 | v77 普通 Lab 的父子窗口选择发生变化，没有接受其不完整退出序列；改用 `--window-participation-lab` 独立主窗口。v78 PID **63546** 的原生绿色按钮进入与输入通过，但进程收到 Close 后退出，未完成该次退出，原因未确认；不把工具自动重开后的默认窗口当作验收。后续独立 Native 完整循环与最终 v79 的两种循环另有有效证据。转换中状态或响应者的临时快照不代替完成后的编辑结果 |

最终平台源码 SHA-256 为
`dc6857837a84fc82296895cd6a345f73d6639b0f1f20a8cb19baa5e3e170d2b4`，
相同拖放测试源码为
`d490029eba13dbe2147c841f721484977dd67ea9b522a9906abe7270954283c4`，
已执行测试及打包的冻结 platform 库为
`aee2381de7d9a9557b7ad9acb0a29dd2ce728b74cf12df7a446e30ded06188db`。
v78 实测使用初版目标会话修复库；v79 使用上述最终库，两轮证据分别记录。

新增原生 `--selection-probe-only`（2 项），拖放 `--core-only`（32 项）、
`--extended-only`（48 项）和 `--case=N`；`JALIUM_WINDOW_ACTIVATION_TRACE=1`
可输出框架提前选择调用栈。Lab 独立入口沿用现有中文、PingFang、浅纸色与深色文字，
按[适用的界面检查规范](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
复查名称、键盘、焦点、完整内容和窄窗口布局；不将其解释为完整原生无障碍审计。

原生拖放组使用真实自有 NSWindow/NSView，受控的本地会话和 pasteboard；没有
实际 AppKit 拖放追踪或跨应用传输。托管 DragDrop 入口仍仅启用 Windows/Linux，
macOS 路由、自定义 MIME/UTI、源查询动作和外部效果反馈仍开放；物理设备、真实
输入法候选窗、VoiceOver、通知订阅、混合 DPI 多屏、Intel 和最低支持版本均未补验。
v75 的实际全屏输入、最小布局及键盘缺项已由 v78/v79 补查，整个 macOS 目标仍在进行。

### v76：拖放回调与窗口销毁

Window 销毁后，迟到的 NSDraggingSource / NSDraggingDestination 回调仍会访问
已清空的 owner；应用在 Enter、Over、Leave、Drop 或源查询回调中关闭窗口时也会
继续读取旧对象。源拖放的嵌套运行循环此前持有 Window 裸指针，完成回调关闭窗口
还可能丢失已经取得的 Copy 结果。现在先检查窗口存活、可见与启用状态，在应用
回调后重新检查窗口身份；源会话通过独立共享对象保存运行状态、允许操作和结果，
最后仅清除仍属于原生窗口的同一会话。

AppKit 的 [beginDraggingSession](https://developer.apple.com/documentation/appkit/nsview/begindraggingsession(with:event:source:))
可以在无法启动时返回 nil。此时立即返回 InvalidState 和 None，不再等待不会到达的
完成回调；完成后才关闭窗口仍保留 Copy，运行中关闭窗口则返回 None。

两种标题栏各 16 项独立进程检查覆盖七类销毁后回调、四类目标回调中关闭并创建
替代窗口、拒绝启动、启动中关闭、查询中关闭、完成时关闭及正常源完成。
使用相同有效夹具和标准 UTF-8 文本负载，旧版本为 **6/32**，包括 **22** 次
SIGSEGV、**2** 次超时及 **2** 次结果断言失败；修复版本为 **32/32**。
早期使用非标准 UTI 的夹具结果不计入。最终平台源码 SHA-256 为
`4a7c1572e0a55ee7077a07a856fc170c53764acb8e9f083c85d488b03832a891`，
测试源码为 `a085ae710bbf6e96201a2314ee9d9658e29efad80d1ef5b7e1f35e46c78a9610`，
已执行测试的 platform 库为
`6a8c9ce363c27843b2ac093e8fe4a58320a38790c87018cf3d1c9f56596c0a2e`。

本组在自有真实 NSWindow / NSView 上重放委托契约，并用本地对象代替 pasteboard
和拖放会话；没有进行物理鼠标手势、AppKit 实际拖放追踪或跨应用数据传输。
未重跑完整原生 CTest、托管回归或新的 Lab 桌面验收，v75 的实际全屏输入缺项保留。
目标暂停时将尚未完成的扩展恢复到上述已通过的源码版本，再清理产物。
目标序号复用及重入、隐藏/禁用中断的更多回调组合、自定义 MIME、托管路由、
真实系统拖放与反馈仍待补齐，不能把 32 项生命周期检查当作完整拖放验收。

### v75：全屏失败恢复与转换后的输入响应

此前 AppKit 报告进入或退出全屏失败时，立即清除转换标记并覆盖 requestedState，
没有重放转换期间的新状态或 Activate / AX Main 请求；状态回调还可能在 AppKit
失败调用栈内开始下一次转换。两种标题栏、进入/退出、普通/最大化起始及更新请求、
选择、取消和替代窗口共 64 项夹具，旧负载为 **12/64**，修复后为 **64/64**。
现在按请求序号只放弃失败的旧请求，保留更晚的相同或不同请求，下一轮主队列发布
实际状态并重放；重复及失效完成回调不能再作用于旧窗口。采用 Apple 的
[进入失败](https://developer.apple.com/documentation/appkit/nswindowdelegate/windowdidfailtoenterfullscreen(_:))
和[退出失败](https://developer.apple.com/documentation/appkit/nswindowdelegate/windowdidfailtoexitfullscreen(_:))
委托契约；这些受控失败通知不是实际诱发 macOS 全屏失败的证据。

实际 v75 Lab 另发现成功转换也会丢失输入响应：进入及退出全屏都把 firstResponder
从渲染视图改成 NSWindow，窗口仍为 key/main、TextBox 仍显示托管焦点，但直接粘贴
没有改变文本；再次点击编辑框才可输入。自有窗口的运行日志和外部 AX 树分别确认
原生响应者与文本结果。现在转换开始前记住输入视图焦点，成功或失败完成后仅在
窗口仍可见、启用且没有更晚原生输入目标时恢复；不在此选择 key/main。

新增 40 项受控检查覆盖两种标题栏、成功/失败的进入/退出，以及已有输入焦点、
原先未聚焦、隐藏、禁用和新选原生 NSTextView。修正夹具引用外层容器及基线复制
旧时间戳造成的编译缓存误用后，有效相同夹具为 **32/40 → 40/40**；此前误用的
结果不计入验收。既有原生 Window 组另新增真实 AppKit 全屏前后 firstResponder
断言；托管失败夹具在 will 通知后主动重置响应者，以免只验证原本未丢失的焦点。

| 检查 | 最终负载结果与范围 |
| --- | --- |
| 原生完整回归 | 首轮九组 **7/9**、**160.34 秒**：前台激活组为 active=0、key/main=null，Window AX 在 120 秒超时；其余通过。增加逐项日志后 AX 单组 **1/1**、**176.22 秒**，其中 **230/230** 通过；为实际 176 秒用时留出余量，CTest 时限最终为 240 秒。首轮失败和超时未抹除；实际全屏响应者断言依赖前台组的后续有效复查 |
| 托管回归 | 最终首轮 **133/135**：全屏失败 **16/16**、最小化 **24/24**、定位 **14/14**、菜单 **4/4**、属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window AX **14/14**、生命周期 **18/18**、reopen **11/11**；前台属性 **0/2** 均停在实际激活前置条件。16 项全屏夹具包括重置响应者、回调排队及关闭并创建替代窗口，仍只模拟进入失败通知 |
| 负载核对 | 原生完成标记及导出检查通过；宿主和最终 Lab 各自 **16** 份原生库的 Mach-O sections / 导出、**10** 个自身 SDK linked 程序集、**3** 个资源及严格深度 ad-hoc 签名通过。platform 库 SHA-256 为 `bdd498ae1da2f96d856d270212e9cda51cd2454061b98c82d5e02ad14dfcdcf4`，平台源码为 `f4f223084a10b109f0048741d8118536b17f85104b4435446f3acc6eed4e2a46`；冻结清单同时记录当前两份原生测试和 CMake 源码。最终宿主 7 条、Lab 10 条已有裁剪/反射警告，均 0 错误 |
| 实际桌面验收 | 最终 Lab PID **5822** 启动后，桌面工具两次报告 Mac 已锁定，尚未执行修复负载的真实全屏进出、连续输入、两种标题栏最小尺寸截图及键盘检查；修复前 PID **97048 / 98886** 的失焦诊断不能作为修复通过证据。前台原生组及两项托管选择也尚未取得本轮后续通过结果 |

新增原生入口 `--fullscreen-failure-only`（64 项）、`--fullscreen-focus-only`（40 项）和
托管 `--window-fullscreen-failure`（16 个独立进程）。Lab 的“窗口参与验证”增加
标题栏切换、实际 firstResponder 显示及状态稳定后的回读；最小客户区为 520×580 DIP，
正文使用垂直自动滚动，以便验证窄窗口的所有操作。
本轮已按用户要求清理应用、冻结库、构建、日志和 JSON；上述未验证事项保留，
解锁后重建再验。未声明前台不稳定根因已消除或所有 macOS 行为已完成。

### v74：等待最小化完成的激活与无障碍选择

此前 Activate 在真实 willMiniaturize 回调中只检查 native.miniaturized，立即调用
makeKeyAndOrderFront，随后 AppKit 动画仍把窗口最小化；在 didMiniaturize 回调中
同样可能在去最小化完成前选择窗口。异步 AX Main 请求还可能越过更新的最小化请求。
初版 32 项真实通知夹具的旧负载为 **6/32**，其中两种标题栏、普通/最大化起始、
will/did 阶段及 Activate/AX Main 路径的 16 项核心组合为 **0/16**。

现在把激活与 Main 选择排到原生状态转换之后，等待实际去最小化通知返回后执行；
Main 不调用 key 选择。隐藏、禁用、新最小化和销毁取消旧请求；异步 AX 写入记录
取消代次，执行时重验。操作跨回调还核对原 NSWindow 身份，避免影响替代窗口。
取消后的再次激活断言又发现两种标题栏的隐藏窗口进入永久去最小化等待，扩展后
首轮 **30/32**；修复为隐藏时保留状态、Show 后重放，最终 **32/32**。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 新增原生检查 | 最终 **32/32**：16 项时序组合、12 项隐藏/禁用/更新最小化取消及后续再次激活、4 项关闭并创建替代窗口。夹具记录自有窗口的真实 makeKeyAndOrderFront / makeMainWindow 调用，要求选择发生在实际去最小化完成之后；不伪造动画通知，也不将 AppKit 请求次数解释为取得前台 |
| 原生相关回归 | 首轮九组 **8/9**、**123.44 秒**，激活组再次出现 active=0、key/main=null；其余通过，Window AX **126/126**、拖动 **138/138**、菜单 **26/26**、初始定位 **40/40**。实际桌面检查后激活单组 **1/1**、**6.86 秒**通过。一次错误筛选未选中测试，仅作为工具失误保留，不计入通过；有效复查使用精确名称及 no-tests=error。Window AX 完整组实际 **86.47 秒**，CTest 超时由 60 调整到 120 秒 |
| 托管回归 | 最小化组 **24/24**，含新增 8 项实际 will/did 通知中的 Activate、状态恢复、RestoreBounds、文本与原生 firstResponder。初始定位 **14/14**、系统菜单 **4/4**、属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window AX **14/14**、生命周期 **18/18**、reopen **11/11**；首轮前台选择 **0/2** 停在实际激活前置条件，桌面操作后复查 **2/2**。合计 **119/119** 各自取得通过结果，未合并成同一轮全通过 |
| 负载核对 | 重新完成独立原生输出、完成标记、宿主与 Lab；两应用各自 **16** 份原生库的 Mach-O sections 和导出集合、**10** 个自身 SDK linked 程序集及 **3** 个资源一致，严格深度 ad-hoc 签名通过。platform 库 SHA-256 为 `4f4e99e8d0fa937783ba6ee255871d85af987a21f9d3b0ba1f865b99a7948756`，平台源码为 `0852caf49127dee0f5263114487f1e05b98bd59dbe72b79dce33ca87693a0719`；冻结清单与当前源码一致。构建成功；最终 Lab 有 10 条已有反射/裁剪警告、0 错误，没有抹除这些警告 |
| 实际激活与输入 | 初次 Lab PID **86632**，Custom/Native × Normal/Maximized 共 **4** 条路径各完成一次真实最小化、去最小化及激活，窗口与编辑焦点恢复，随后无需再次点击编辑框即可粘贴中文与 emoji。还原客户区均为 **800×760 DIP**；普通外框分别 **800×760 / 800×792 点**，最大化外框均 **2560×1320 点**。第一次 Normal 重开后的实际激活事件已在再次读取 UI 之前记录，避免把工具重新选择窗口当作激活修复证据 |
| 键盘、布局和最终面板 | 初次 Native 最小窗口 Tab 到首按钮、Return 完成第 **5** 次最小化还原、Shift+Tab 返回编辑框后继续输入。修正面板失焦后仍显示旧焦点值及标题栏切换文字，再重建最终 Lab PID **88612**；Custom Normal 与 Native Maximized 两条激活路径和继续编辑复查通过。Tab/Shift+Tab 的 AX 焦点、绿色焦点轮廓及状态文字一致。两种标题栏 **620×700 DIP** 的最终截图中说明、编辑框、五行状态和十个换行按钮完整可见，外框分别 **620×700 / 620×732 点** |
| 验收边界 | 实际界面保留既有中文、PingFang、浅纸色与深色文字，按[当前界面检查规范](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)核对适用的名称、键盘、焦点与布局。自有原生 Main 调用检查与宿主选择回读已通过，本轮没有以外部 Inspector 写入 Main 属性；通知接收、VoiceOver、真实输入法候选窗、物理设备、混合 DPI 多屏、Intel 与最低支持版本仍未覆盖。首轮前台不稳定根因未确认，不能宣称所有 macOS 行为完成 |

新增原生可重跑入口 `--activation-transition-only`（32 项）；现有宿主
`--window-minimize-transition` 扩为 24 个独立进程，Lab
`--window-minimize-lab` 新增两种激活操作，状态暴露实际焦点和真实通知计数。
清理后源码与本文结果保留，应用、冻结库、日志和 JSON 历史路径已移除。

### v73：最小化开始回调中的最新状态请求

此前仅去最小化和全屏转换有等待标记。AppKit 开始最小化的回调中先请求 Normal、
再请求 Maximized，随后请求最终 Normal 或 Maximized，动画完成后仍停在 Minimized。
同一真实 AppKit 检查覆盖两种标题栏、两种起始状态、两种最终状态，以及平台 API
和原生 performMiniaturize 路径，旧负载 **0/16**。

现在在调用 AppKit 最小化之前保存普通窗口外框和客户区尺寸，并标记正在转换；
动画期间及完成通知里的请求排队，完成后重放最新请求。原生直接操作也使用该路径。
没有 will/did 通知的拒绝请求会释放过渡标记；回调关闭目标后根据原生窗口身份停止，
异步任务不再访问替代窗口。最小化和去最小化期间的 resize 不误判为用户调整，
RestoreBounds 使用保存的普通尺寸，无障碍几何写入与原生菜单状态命令暂不可用。
这里只修复已稳定复现的最小化请求丢失，没有将此前延迟还原的不稳定失败归因于它。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 新增原生检查 | 修复后 **24/24**：原来的 16 项组合全部通过；另覆盖完成状态回调中的还原/最大化、原生拒绝请求后继续变更状态、开始回调关闭并创建替代窗口。拒绝用例仅拦截自有夹具的 NSWindow 基类请求，未伪造原生动画通知；其他用例等待真实 AppKit 通知。最初替代窗口检查过早断言，改为等待实际开始通知后通过，未修改产品断言 |
| 延迟还原诊断 | 旧的 v65/v72 RestoreBounds 不稳定失败本轮未复现：六次独立进程 **12/12**、三次旧完整 AX **70/70**、一次显式追踪 **2/2**。新增内存追踪保留真实状态、最后请求、外框、客户区、还原尺寸、DPI 与通知，失败时输出；显式追踪入口可打印成功过程。当前设备实际为单屏 **2560×1440 点、scale=1**，工作区 **2560×1320 点**；以上重复通过不证明原失败根因已经消除 |
| 原生相关回归 | 首轮九组 **8/9**、**83.92 秒**；Window 激活组再次出现 active=0、key/main=null。其余组通过，Window AX **94/94**、拖动 **138/138**、菜单 **26/26**、初始定位 **40/40**。实际桌面及托管检查后，激活单组复查 **1/1**、**6.90 秒**通过，九组各自均已有通过结果；没有将合并结果标记为同一次 CTest 全通过，也未修改激活实现或宣称消除前台不稳定 |
| 托管回归 | 新增最小化 **16/16**，初始定位 **14/14**、系统菜单 **4/4**、属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window AX **14/14**、生命周期 **18/18**、reopen **11/11**、前台选择/激活 **2/2**，合计 **111/111**；均使用本轮独立打包的负载 |
| 负载核对 | 重新构建 8 个原生库、完成标记、宿主和 Lab。两应用各自 **16** 份原生库的 Mach-O sections 与导出集合、**10** 个自身 SDK linked 程序集、**3** 个资源一致，严格深度 ad-hoc 签名通过。platform SHA-256 为 `86abef97ed20b095400c8ce889a0d50445ae40e3e54e0e95e67fe6ea392027f7`。单独重建测试目标会移除完成标记，随后完成完整原生目标构建，打包检查没有被绕过 |
| 实际状态转换 | 最终 Lab PID **74007**，Custom **6** 次、Native **4** 次，共 **10** 次。包含普通窗口最小化后最大化、最大化窗口最小化后还原，两条调用路径均操作；各次原生开始、完成和重开通知都增加一次，最终托管/原生状态与请求一致。Custom 最大化客户区 **2560×1320 DIP**，Native 为 **2560×1288 DIP**；还原均为 **800×760 DIP**，外框分别 **800×760 / 800×792 点**，中文与 emoji 文本保留 |
| 实际键盘与布局 | 初始 Custom 窗口添加 ` + queue73`，第一次自动还原后继续添加 ` 还原后继续`，Tab 的按钮焦点轮廓可见，Return 触发第二次最小化并还原。两种标题栏的 **620×700 DIP** 最小布局截图中，中文说明、编辑框、状态和八个换行按钮完整可见；原生外框 **620×732 点**，自定义为 **620×700 点**。AX 编辑名称可读，按现有 PingFang、纸色与深色文字设计，并参考[当前界面检查规范](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)核对适用的名称、焦点、键盘和内容布局 |
| 外部输入边界 | 后续原生标题栏及切回 Custom 后的键盘粘贴未改变文本；坐标点击返回 **noWindowsAvailable**，重新绑定及公开 Raise 动作没有取得可验证的输入结果。此时日志的 nativeKey=false，不能将工具请求当成继续编辑通过，也不能仅凭工具失败认定产品输入故障。上述初次 Custom 编辑是独立的已观察结果；后续真实鼠标重新激活和两种标题栏键盘输入保留待验收，混合 DPI 多屏、VoiceOver、Intel 和最低支持版本仍未覆盖 |

新增可重跑入口：原生 Window AX 的 `--minimize-transition-only`（24 项）与
`--delayed-restore-trace`，宿主 `--window-minimize-transition`（16 个独立进程），
Lab `--window-minimize-lab`。Lab 保留源码，显示中文状态和真实通知计数；没有用
计时器或模拟通知替代 AppKit 动画完成。

### v72：首次显示的外框定位、重开与还原坐标

原来的跨平台居中以客户区尺寸和物理屏幕矩形计算。macOS 原生标题栏的外框
因此偏移 16 点，混合标题栏父子窗口也有相同偏移；移动后 Hide/Show 会再次
居中，部分越界父窗口使子窗口落到工作区之外。初始化回调先最大化时，普通
窗口的还原外框仍停留在原手动位置。

现在新增 Apple 原生初始定位接口，使用真实 NSWindow 外框和 NSScreen 工作区
在统一的 AppKit 点坐标中计算。仅首次原生显示前应用定位；对非普通窗口修改
已保存的还原外框，并在需要时更新所在屏幕的最大化外框。普通父窗口按外框
居中，非普通父窗口按其工作区居中；可容纳的窗口全部限制到工作区，超大窗口
保留可操作的左上角。选择屏幕的语义参照
[WindowStartupLocation 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.windowstartuplocation?view=windowsdesktop-10.0)
及 [WPF 的定位实现](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs)。
真实 AppKit 屏幕对象没有被模拟；UIKit 分支返回 NotSupported。

实际验收还发现 `setFrame:` 在同时改变尺寸和位置时可能只通知 resize，导致
原生外框已居中而托管 Top 仍为旧值。现在 resize 回调先检查原生位置是否改变，
在尺寸回调前同步位置；回调关闭目标后立即停止。这样还原后的程序移动可以
从实际坐标继续，重入状态请求仍使用原有队列。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 初始托管基线 | 同一 14 项真实宿主在旧负载为 **5/14**；修复外框定位和首次显示后 **14/14**。实际桌面发现旧 Top 后增加托管坐标断言，补查基线为 **12/14**，最终再次 **14/14**。覆盖两种标题栏与四种父子组合、屏幕居中、移动后 Hide/Show、越界父窗口、无父窗口和 SourceInitialized 最大化后还原 |
| 原生夹具 | 最终 **40/40**，包含 **16** 项纯几何输入和 **24** 项真实 AppKit API 检查。位置通知补查基线为 **34/40**；补齐后通过。检查点坐标屏幕布局、最近屏幕、无效输入、外框装饰、工作区边缘、超大窗口、普通与最大化父窗口、主线程要求、已关闭父窗口，以及位置回调关闭并创建替代窗口、排队新的最大化请求 |
| 夹具边界 | 初版使用 NSProxy 假屏幕对象触发 AppKit 内部校验而 SIGTRAP；随后移除假屏幕和屏幕发现替换，改为单独检查纯几何输入及真实窗口 API。该失败保留为夹具问题；未作为产品通过证据，也未将输入布局标注为混合 DPI 硬件验收 |
| 原生相关回归 | 初轮九组 **7/9**、**67.69 秒**：Window 未获得前台 key/main window；Window AX 为 **69/70**，失败为已有的延迟还原后 RestoreBounds 改变。位置同步修复及桌面操作后，最终九组 **9/9**、**52.88 秒**；Window **7.01 秒**、AX **70/70**、拖动 **138/138**、菜单 **26/26**、新定位 **40/40**。单次通过不证明已有激活和延迟还原不稳定的触发原因已经消除 |
| 最终托管回归 | 初始定位 **14/14**、系统菜单 **4/4**、属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window AX **14/14**、生命周期 **18/18**、reopen **11/11**、前台属性选择/激活 **2/2**，合计 **95/95**，均使用最终重新打包的负载 |
| 负载核对 | 重新构建 8 个原生库，校验完成标记及 Metal manifest。最终 Lab 与宿主各自 **16** 份库的 Mach-O sections 和导出符号集合、**10** 个自身 SDK linked 程序集、**3** 个资源一致，严格深度 ad-hoc 签名通过。最终 platform SHA-256 为 `0992dd83a6c48a9429b11ac9a30dedcf2d571fd6f985d2fc1214d4ca02d40077`；初步负载的桌面记录与最终记录按 PID 区分 |
| 实际还原与重开 | 最终 Lab PID **61999** 初始化最大化为客户区 **2560×1288 DIP**，还原为 **720×640 DIP**、外框 **720×672 点**、Left/Top **920,354**，外框相对工作区中心 **0,0**。编辑 ` + final72` 后程序移动到 **1057,443**，Hide/Show 保留位置、外框、文本和编辑焦点，再输入 ` reopened` 成功 |
| 实际父子窗口与键盘 | 最终 Native 父窗口 + Custom 子窗口、Custom 父窗口 + Native 子窗口，树和窗口日志的外框中心差均为 **0,0 点**；截图显示对应标题栏和完整布局。分别点击编辑框继续输入，文本和焦点回读正确；自定义子窗口 Tab 到关闭按钮、Return 关闭，原生子窗口 Escape 关闭，父窗口文本与编辑焦点恢复 |
| 最小布局与范围 | 最终 **600×560 DIP** 客户区下两种标题栏的截图中，中文说明、编辑框、状态和换行按钮完整可见，焦点轮廓与 AX 名称可读。遵循现有 PingFang、纸色背景、深色文字和绿色焦点风格，并参照[当前界面检查规范](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)核对适用的名称、键盘、焦点和内容布局；原生客户端不应用网页专属规则。混合 DPI 实体多屏、Intel、最低支持版本和 VoiceOver 全流程仍未验收 |

新增可重跑入口：原生 CTest `jalium.native.platform.macos.window.startup`，
宿主 `--window-startup-location`；Lab 的 `--window-startup-lab` 可附加
`--native-titlebar --maximize-initialized`。本轮退出全部验收进程后清理输出，
保留原生、托管测试和 Lab 源码，具体清理结果记录在本文开头。

### v71：窗口上下文菜单与客户区屏幕坐标

此前 macOS 的原生 `jalium_window_show_system_menu` 返回 NotSupported，公开
`SystemCommands.ShowSystemMenu` 和自定义标题栏右键仅接入 Linux；Visual 的
屏幕转换也没有使用 macOS 原生原点。原生标题栏的 frame 原点还包含装饰区域，
不能直接作为客户区锚点。

现在使用 AppKit 的原生弹出菜单，复制宿主 Window 菜单的四项标准命令、名称和
快捷键，显式绑定请求的窗口并复用其能力验证。隐藏、禁用或销毁窗口取消追踪；
重入和非主线程请求拒绝；关闭回调销毁目标后，用原 NSWindow 身份检查收尾，
不沿可能复用的 C++ 地址修改替代窗口。全屏复用已有状态队列。
AppKit 的锚点采用 view 坐标，参见
[NSMenu 弹出接口](https://developer.apple.com/documentation/appkit/nsmenu/popup%28positioning%3Aat%3Ain%3A%29?changes=__1&language=objc)。

新增 Apple 客户区原点查询，将 flipped view 的零点经原 NSWindow 转为左上角
物理屏幕坐标；Window 和 Visual 的 macOS 屏幕转换使用该原点、DPI 与控件变换。
两个接口的 UIKit 分支仍返回 NotSupported，本轮没有实现或验收 iOS 菜单。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 新原生夹具 | 两种标题栏共 **26** 项，旧实现 **0/26**；接入后最初 **25/26**。唯一失败是夹具把 AppKit 自动追加的窗口列表误判为宿主命令变更；改为核对原命令身份、标题与 target 后 **26/26**。夹具在本进程捕获弹出请求，检查实际 NSMenu、显式目标、坐标、8 种能力组合、取消关闭、销毁、隐藏/禁用中断、重入、线程与原点；它不代替真实追踪操作 |
| 原生相关回归 | 首次七组 **6/7**、**62.96 秒**；Window AX **70/70**，拖动 **138/138**。完整 Window 首次因 active=0、Key/Main=null 失败；实际桌面验收后原负载重试 **7.04 秒**通过，保留首次失败，未声称修复其环境触发原因。菜单 Escape 协议单独 **0.24 秒**通过；最终八个分组均有通过记录 |
| 新托管宿主 | **4/4**：两种标题栏的移动、切换、Window/后代屏幕转换与往返；公开入口、实际菜单追踪取消、HasSystemMenu、隐藏/禁用拒绝与自定义 caption MouseUp。首轮在 BeginTracking 通知内立即取消，两个菜单用例超时；改为在 EventTracking mode 定时器中取消后通过，未修改产品取消路径 |
| 托管回归 | 属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window AX **14/14**，合计 **46/46**；前台属性选择和激活 **2/2**通过 |
| 负载核对 | 新构建 8 个原生库、完成标记、宿主及 Lab；Metal 两个库的大小和 SHA-256 与 manifest 一致。两应用各自 **16** 份原生库的可加载 Mach-O sections、**10** 个自身 SDK linked 程序集、**3** 个资源一致；深度严格 ad-hoc 签名通过 |
| 真实菜单与编辑 | Lab PID **45522** 的截图和树显示四项 AppKit 命令；一次 Escape 关闭菜单，关闭请求仍为 0；之后继续输入 ` + v71`。选中 Close 并 Return，第一次请求取消，窗口和文本保留。切换 Native 后客户区仍 **660×600 DIP**、焦点保留，屏幕原点 Y 从 **210** 到 **242 px**，实际菜单也随标题栏偏移 |
| 能力与 Zoom | NoResize 时实际菜单的 Minimize、Zoom 和 Enter Full Screen 均禁用，Close 可用；HasSystemMenu=false 请求没有菜单；重新启用后 Zoom 到 **2560×1288 DIP**、状态 Maximized，还原到原尺寸 |
| 全屏与最小化 | Custom 的菜单进入真实 Spaces 全屏，完成通知后为 **FullScreen / 2560×1440 DIP**；菜单变为 Exit Full Screen，退出完成后恢复 **Normal / 540×540 DIP / 950,210 px**。继续中文粘贴成功。选择 Minimize 后原进程记录 Minimized、Key/Main=false；下一次桌面工具读取重新激活，记录和树恢复 Normal、同样尺寸与文本 |
| 最小布局与接受关闭 | **540×540 DIP** 的截图中说明、编辑框、状态及换行按钮完整可见，名称和焦点可读。允许关闭后第二次 Close 请求未取消，Closed 与菜单返回各记录一次，原 PID 结束；桌面工具返回 App quit |
| 尚未验证 | 标题栏坐标右键返回 **noWindowsAvailable**，窗口树未变化；宿主的 caption 事件检查不能替代这条外部输入路径。工具 typeText 的本轮中文追加只送达 ASCII 部分，中文通过 paste 写入；不把它作为真实中文输入法验收。菜单栏物理键盘、混合 DPI、多屏、VoiceOver、Intel 与最低支持系统继续保留 |

本轮 platform 库 SHA-256 为
`e0a1c1e8690fe9b378cecf1ecbd7a7c5ae78034461a1ca0aad73ffd852ee906c`。
可复跑入口是新 CTest `jalium.native.platform.macos.window.system-menu` 与宿主
`--window-system-menu`；Lab 可用 `--window-menu-lab` 仅打开此验收窗口，避免多
窗口截图目标混淆。原生追踪取消与产品状态回归、实际截图/按键/AX 和未验证的
设备输入分别记录，以上通过不表示全体 macOS 行为已经完成。

### v70：拖动中的程序几何更新与外部连续缩放

继续核查交互调整尺寸时，复现了另一项真实几何缺陷：追踪循环总是从最初的
frame 和按下位置计算。tracking-mode 定时器或 Resize 回调已经修改窗口的位置、
客户区大小或标题栏后，下一个拖动样本覆盖应用的新 frame；标题栏变化的装饰
尺寸也继续使用最初的值。

新增 **72 项**原生检查。Native/Custom × 四种更新（尺寸、位置、标题栏、
尺寸和位置一起更新）× 八个边角，形成 64 项定时器检查；两种标题栏的四种更新
再各覆盖首次用户 Resize 回调和下一次样本，形成 8 项同步回调检查。每个样本
先保存屏幕点，再根据发送时的实际窗口 frame 转为窗口坐标，避免测试本身因
窗口移动而伪造指针增量。基线 **0/72**，72 项最终几何断言全部失败。

现在保留上一样本的实际 frame 与屏幕指针位置。应用在样本之间修改 frame 时，
以后续增量继续拖动；同步回调替换当前 frame 时也采用回调后的值。每次计算
重新获取当前标题栏装饰尺寸，同时保留 v69 的动态 Min/Max 和原有边角锚点。
健康窗口的手势不因程序更新几何而提前结束，中断与销毁检查仍逐次生效。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 几何更新回归 | 同一新增夹具由 **0/72** 到 **72/72**；完整拖动组 **138/138**、**14.84 秒**，包含已有的 66 项中断、边角、空闲、连续 view 输入与动态约束检查 |
| 原生 CTest | 平台协议、完整 Window、Window 属性、无障碍协议、Window AX 状态和拖动 **6/6 组**，**50.40 秒**；Window AX **70/70**。完整 Window **7.60 秒**，两种标题栏的实际前台 Main/Focused 选择、最小化/还原与真实 Spaces 转换通过 |
| 托管宿主 | 属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window 无障碍 **14/14**，合计 **46/46**；独立前台选择与激活 **2/2** 通过 |
| 验证负载 | 重新构建全部 8 个原生库与完整标记、托管宿主和 Lab。两应用各自的 16 份原生库、3 个程序集与 3 个资源一致，深度严格 ad-hoc 签名通过。最初 Lab PID **34077**；输入记录版重建、重新打包并核对后运行 PID **36509** |
| 主窗口实际缩放 | 当前 Lab 截图与无障碍树确认程序设为 **840×700 DIP**。Inspector 对此实际进程的主窗口提交 Main=True 后产生激活事件，输入记录为原生 Key/Main=True；随后右下角两次外部拖动分别到 **880×740** 和 **900×760 DIP**，客户区与 RestoreBounds 同步 |
| 失活输入诊断 | Main=True 前的两次边缘操作只送达 MouseMoved；记录显示原生与托管均失活，坐标及 DPI 与目标窗口一致。内容区坐标点击能收到 LeftMouseDown/Up，但没有激活窗口。激活后边缘操作成功，却仍没有托管 view 的按下记录，因此推断本次成功经过 AppKit 边缘路径；不把它作为自定义 BeginResizeDrag 或物理鼠标的验收 |
| 外部 Main/Focused 回读 | Inspector 明确选择 PID **36509** 的“窗口属性 · Window 验证”。Custom 的 Main=False 提交时编辑器暂显示 false，切换元素重新读取为 true；Lab 常规尺寸操作记录原生 Key/Main=True、托管 active=True。Custom 的 Focused=False 提交后重新读取为 true。切换 Native 后再提交两项 False，刷新属性读到 Main=True、KeyboardFocused=True；符合既有拒绝撤销选择的行为 |
| 编辑与标题栏 | 属性窗口保持 **1010,205 DIP**、**680×640 DIP**。中文、emoji 与追加的 ` + v70` 经实际输入和树确认；切换 Native 保留 Key/Main 与编辑焦点，继续输入 ` + Native` 成功 |
| 未完成的外部检查 | Inspector 的 Position 数值坐标编辑连续返回 **noWindowsAvailable**，没有观察到写入或目标位置变化；Size 尚未完成数值写入。本次多窗口 Lab 的截图包含主窗口和缩放后的属性窗口，不能据此宣称属性窗口的像素尺寸或最小布局通过。外部属性数值写入、动态几何更新的托管/物理拖动、混合 DPI 与 VoiceOver 继续保留 |

本轮 platform 库 SHA-256 为
`a986c5e3ea0510d2cfff234aeac77ac45fdb4e5ef5647566ac03d4441bd4b0d3`。
原生构建使用独立输出；未修改 Metal shader 源码，两个 Metal 资源按现有清单
核对哈希后复制。输入记录只加入忽略的验证宿主源码，不修改产品的事件分派。
CUA 的元素操作和 Raise 不保证应用激活；本轮通过正向 Main 请求确认原生与
托管活动状态后才计入连续缩放结果。v68 的失活连续操作不能继续作为已经证实
的缩放产品缺陷；本轮也没有复现并修复其全部外部触发条件。
v65 延迟 RestoreBounds 的间歇失败仍未定位，不因本轮通过而声明已修复。

### v69：拖动中的动态尺寸约束

继续核查 v68 的外部连续拖动缺项时，桌面工具仍明确报告 Mac 锁定；先在独立
原生构建中复现另一个尺寸缺陷。此前交互调整尺寸只在按下时读取 contentMinSize
和 contentMaxSize，tracking-mode 定时器或应用回调更新约束后，后续鼠标样本
仍使用旧值，能够越过新边界。

新增 **32 项**检查，两种标题栏各覆盖八个边角的最小及最大限制。在真正进入
追踪循环后，先通过 C ABI 更新约束，再排队一个超过新边界的鼠标拖动和释放。
最小值设为初始客户区宽度减 20、高度减 10 个 AppKit 点；最大值分别增加相同
距离。断言受到影响的轴停在该新边界，左侧和底部拖动保留对侧锚点，其余轴与
原点保持对应行为。基线 **34/66**：原 34 项通过，新 32 项全部失败，实际尺寸
跟随 80×60 点的拖动越过新约束。

现在处理每个拖动样本前重新读取 NSWindow 的内容尺寸约束；采用原有边角几何
和固定锚点计算，不重写窗口状态或尺寸约束的设置流程。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 动态约束回归 | 新增 **32/32**，原有中断、边角、空闲与连续 view 输入 **34/34**，完整拖动组 **66/66**、**7.86 秒** |
| 原生 CTest | 平台协议、Window 属性、无障碍协议、Window AX 状态与拖动 **5/5 组**，**36.49 秒**；Window AX **70/70**。此次没有运行需要实际前台的完整 Window 分组 |
| 实际 AppKit 队列探针 | 另建仅属于测试进程的临时探针，用 NSApplication.run 和默认模式定时器排队三次鼠标按下，再由原生 view 回调启动拖动。两种标题栏均未收到按下回调（0/3），没有进入拖动断言；在当前锁屏环境下，该探针的事件送达前提未成立，不能据此判定连续拖动产品缺陷或通过 |
| 外部与托管验收 | 本轮没有构建或运行 Gallery、托管宿主及 Lab；最新实际界面记录仍为 v68。外部连续拖动、Main 写入后的回读及新约束的托管/物理拖动验收继续保留 |

本轮只重新构建 core、platform 和五个原生测试程序，使用独立输出，没有修改
共享原生负载。platform 库 SHA-256 为
`947fd08fb9eb478868b04492e72d77dad1addfcacb48621bb84ad00a872e6b06`。
以上排队鼠标事件与 timer 检查证明 C ABI/AppKit 追踪路径，不能替代真实设备
输入、外部工具送达或托管 chrome 的验收。v65 延迟 RestoreBounds 间歇失败
与 v68 的外部连续拖动仍未定位。

### v68：调整尺寸中断与托管前台选择

旧交互调整尺寸循环无限等待下一次鼠标拖动或释放；tracking-mode 定时器已经
隐藏、禁用、固定大小、最大化、最小化或关闭窗口后，循环仍然等待释放。
首版 30 项夹具的基线为 **18/30**，十二项中断都需要 0.6 秒的兜底释放事件
才返回；八个边角及静止按住检查通过。

现在每次事件读取最多等待约一帧，空闲超时继续手势，读取返回时重验窗口的
原生身份、可见性、启用状态、缩放能力与状态转换。新的非 Normal 状态请求
终止追踪；AppKit 的最小化开始通知清除交互调整尺寸标记，防止动画期间继续
调整旧 frame。关闭后重新创建的窗口保持其自身 frame。

最初只加入有界等待时，首版检查为 **28/30**。补测直接原生最小化，并记录
操作返回与 DidMiniaturize 通知，确认最小化请求很快返回，但 AppKit 的事件
读取本身可以运行约 **0.54–0.56 秒**的动画。最终断言仍要求没有兜底鼠标释放，
并在中断生效后 0.3 秒内退出；对于该原生动画，另外记录完成通知与退出之间
的等待。本机其他中断的尾部等待为数毫秒，原生动画完成后的尾部等待约 1 毫秒。
最终兜底定时器为 1 秒，保留动画余量与原有退出断言。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 原生拖动 | **34/34**；两种标题栏各覆盖七类中断、八个边角与锚点、静止按住、同一 NSView 两次按下回调中的连续调整尺寸。测试只替换本进程 currentEvent 的读取，实际使用 AppKit 窗口、追踪循环、定时器和排队鼠标事件 |
| 原生 CTest | 平台协议、完整 Window、Window 属性、无障碍协议、Window AX 状态与拖动 **6/6 组**，**39.82 秒**；当时拖动为 32 项。随后新增连续输入检查，拖动独立 **34/34**、**4.86 秒**；没有重新运行未改动的其他五组 |
| 原生状态与前台 | Window AX **70/70**；完整 Window 前台组 **7.03 秒**，两种标题栏的 Main/Focused 选择、应用 Key/Main 指针、启用恢复、真实最小化/还原与 Spaces 转换通过 |
| 托管宿主 | 当前负载的属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window 无障碍 **14/14**，合计 **46/46**；独立前台选择与激活 **2/2** 通过，核对原生/托管激活、Main/Focused 正反向写入、另一窗口选择和输入 view 恢复 |
| Lab 负载 | 当前源码重新构建 8 个原生库及完整包标记，宿主与 Lab 均构建成功；每个应用的 16 份原生库、3 个程序集和 3 个资源一致，深度严格 ad-hoc 签名通过；运行 Lab 的 PID 为 **23421** |
| 实际窗口与输入 | v68 Lab 已启动并查看截图、无障碍树；属性窗口编辑框保留中文、emoji 与追加的 ` + v68`。首次外部右下角拖动从 **680×640** 到 **745×695 DIP**，位置保持 **940,255 DIP**；原生 Key/Main 为 True，编辑焦点保留。最小尺寸按钮设为 **540×540 DIP**，截图中全部控件可见、操作按钮自然换行 |
| 未完成的外部检查 | 随后的边缘与标题栏拖动没有产生尺寸或位置变化；原生连续 view 输入用例通过，外部工具、托管路由或产品原因仍未定位。Inspector 明确选择 v68 进程及属性窗口，提交 Main=False 后控件显示 false；随后 Lab 回读明确遇到锁屏，没有核对原生状态，不将此次写入计为通过 |

本轮 platform 库 SHA-256 为
`825c947a81e1defb5df09379af0aaafb7150a6b654694d69fad497801546b001`。
原生输出使用独立目录，没有修改共享原生负载；两个 Metal 库按现有资源清单
核对哈希后复制。托管前台用例的通过补足了 v67 因锁屏未执行的选择断言，
不能替代外部 Inspector 写入、物理拖动中断、VoiceOver 或兼容性验收。
v65 的延迟 RestoreBounds 间歇失败继续保留为未定位。

### v67：状态回调关闭后新建窗口的身份保护

本轮开始时桌面工具仍明确报告锁屏，先用当前源码构建的原生测试宿主复现一个
独立的窗口生命周期缺陷。随后 Inspector 的旧窗口引用失效，重新绑定应用后
实际截图恢复可读；立即完成以下原生前台检查，并重建托管宿主与 Lab。

普通最大化和还原通过状态通知调用用户代码；用户代码可以关闭当前窗口并立即
新建一个窗口。此前后续代码只检查平台窗口指针是否仍在注册表中，分配器复用
地址时便把新窗口误当成旧目标，继续施加旧最大化或还原的 frame。

新增四项检查覆盖两种标题栏及最大化/还原通知：回调关闭原窗口，再创建客户区
**560×360 原生像素**、位置 **500,300 原生像素**的新窗口，保存创建后的实际 frame，
核对旧请求返回后它的 frame、客户区和 Normal 状态仍保持不变。基线 **1/4**；
三项失败都实际复用了原平台窗口地址，唯一通过项没有复用地址。修复后 **4/4**，
这次四项都实际复用了地址。测试没有假定分配器必须复用地址；此处的复用结论
来自本机实际输出。

现在回调前保留原 NSWindow 强引用，回调后同时检查注册状态与该原生对象身份。
相同地址的新窗口具有另一个 NSWindow，旧操作立即停止。同样的身份检查用于
显示/激活、样式、移动/尺寸、尺寸约束、去最小化通知、交互调整尺寸及 IME 上下文
中的既有回调返回点；所有权变更在移除旧 parent 后也重验原目标。窗口创建期间
尚未注册的初始样式与 parent 绑定仍允许完成。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 新生命周期回归 | **4/4**；独立入口 `jalium.native.platform.window.accessibility.tests --replacement-only`，实际执行 AppKit 窗口的关闭、创建与状态回调 |
| 原生 Window AX 状态 | **70/70**；包含原 66 项及上述四项，保留原有断言与等待限时 |
| 原生 CTest | 平台协议、Window 属性、无障碍协议与 Window AX 状态 **4/4 组**通过，总计 **41.60 秒**；随后独立完整 Window 前台分组 **1/1** 通过，实际运行 **7.05 秒** |
| 原生实际前台行为 | 两种标题栏均核对 AX Focused/Main=False 保留实际选择、True 再选择、另一个 Main 不抢当前 Key、Focused 往返、NSApplication Key/Main 指针、激活事件、禁用后输入 view 恢复及后台重新启用。完整 Window 分组还执行真实 AppKit 最小化/还原与 Spaces 转换；这些是本进程原生调用，不是外部 Inspector 写入或物理键盘验收 |
| 托管非激活宿主 | 最新 v67 负载的属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window 无障碍 **14/14**，合计 **46/46**；运行前核对全部 16 份原生库副本 |
| 托管前台组 | v67 宿主已重建并核对 16 份原生库副本；两种标题栏的独立前台用例 **0/2**，均停在初始 Key/Main/托管激活前提，没有执行新写入断言。随后 CUA 明确报告 Mac 再次锁定；不将该结果计为通过或 Main/Focused 产品缺陷基线 |
| 外部验收 | v67 Lab 已重建并打包，16 份原生库、3 个程序集与 3 个资源核对一致，深度严格 ad-hoc 签名通过；启动请求遇到锁屏，没有观察到实际窗口，未计为启动或外部验收通过 |

本轮先仅构建 core、platform 及四个原生测试程序；桌面短暂恢复后，又完成当前
源码的 8 个原生库、完整包标记、宿主与 Lab 构建，没有修改共享原生输出。
platform 库的构建 SHA-256 为
`b96d86bda5455f61b9d2d06b2be32a8a1e005d95e7d84631cb338354247f5c03`。
v65 首轮延迟还原的 RestoreBounds 间歇失败仍未定位；本轮 70/70 不表示已经修复
该问题。交互调整尺寸、激活等路径的地址复用场景没有独立实测，四项新复现的
范围是最大化/还原状态回调；托管前台、外部写入和平台兼容性缺项继续保留。

### v65–v66：外部 Window 属性写入与实际主窗口选择

v65 Lab（PID **89444**）重新构建并启动，8 个原生库的 16 份打包副本、3 个托管
程序集和 3 个资源均核对一致，整个包通过 ad-hoc 深度严格签名检查。以下实际
外部操作均针对这份旧负载；它不包含本节随后发现问题的修复。

Accessibility Inspector 明确选择 **JaliumWindowLabv65 (89444)**，从应用的
FocusedWindow 关系进入“窗口属性 · Window 验证”，关闭取样工具后写入属性。
布尔菜单先选择并核对选中项，再按 Return 提交，随后核对实际 Lab 状态与通知，
避免把仅菜单选中、尚未提交的值计为成功。

| 外部操作 | 实际结果 |
| --- | --- |
| Minimized=True，再 False | 托管 Normal → Minimized → Normal；原生最小化状态对应改变；还原位置 **940,255**、客户区 **680×640**、原普通 RestoreBounds 与编辑内容保留。第一次未成功提交的 False 只算工具尝试，后续独立提交并观察到还原才计为成功 |
| KeyboardFocused=True | 还原后的窗口实际成为 Key/Main，原生通知与托管激活状态对应改变；查看截图确认内容和编辑框。此前 False 尝试发生在原生 Key 已为 False 时，不能证明外部撤销焦点成功 |
| Main=False，再 True | False 后实际 `NSWindow.mainWindow=False`，而 Key 与托管激活仍为 True；Inspector 随后显示 Main=True，但 Lab 仍为 Main=False，且没有 DidBecomeMain 通知。这是新发现的状态不一致，未计为通过 |
| 编辑与名称 | 外部 Lab 无障碍树读到中文名称“窗口属性验证编辑框”；实际输入 ` + v65`，截图与日志均保留追加内容 |

这次问题来自直接调用 AppKit 的 `resignMainWindow` / `resignKeyWindow` 通知方法。
Xcode 27 SDK 的 `NSWindow.h` 明确将这些方法定义为覆写点，要求通过
`makeMainWindow` / `makeKeyWindow` 改变应用的选择；AXFocused 的 SDK 属性契约只允许
用 True 选择焦点，不能用 False 撤销焦点。Main=False 的处理同时参考标准 NSWindow
的实际属性行为；其 AXMain 文档没有明确写出相同的 True-only 限制。

v66 的 `setAccessibilityFocused` / `setAccessibilityMain` 在 False 时保留 AppKit
当前选择；True 仍排队并重验目标，通过原生激活或主窗口选择执行。禁用路径也
移除直接 `resignKeyWindow` 调用，由 AppKit 在另一个窗口成为 Key 时转移选择；
重新启用当前 Key 窗口时恢复自身输入 view，后台窗口重新启用时不抢焦点。
禁用、样式替换的回调返回后通过保留的 NSWindow 身份判断窗口是否已销毁或被替换。

新增前台检查同时核对 NSWindow 标记、NSApplication 的 Key/Main 指针、AX getter
与托管激活事件，覆盖 False 拒绝、True 再选择、另一个窗口的 Main 选择与键盘焦点
独立、Focused 往返、禁用后输入恢复及后台重新启用。原生检查放入完整 Window
CTest；托管两种标题栏使用独立的 `--window-property-activation-accessibility` 分组，
单项可用 `--window-property-accessibility-case=10` / `11`。这些检查要求解锁并可实际
激活的桌面，当前未执行新负载的该组检查。

| 自动检查 | 当前证据与限制 |
| --- | --- |
| 完整原生 Window CTest | 本轮修复前的 v65 负载通过，实际运行 **8.16 秒**，包含 AppKit 激活、最小化/还原和真实 Spaces 转换；不证明 v66 新选择与禁用路径已验收 |
| 托管非激活宿主 | 最终新负载属性 **10/10**、标题栏 **10/10**、默认/取消 **12/12**、Window 无障碍 **14/14**，合计 **46/46**；运行前确认 16 份原生库副本与最终构建的 Mach-O section 一致 |
| 原生 CTest | 最终新负载的平台协议、Window 属性、无障碍协议与 Window AX 状态 **4/4 组**通过，总计 **38.34 秒**；该命令不含要求实际激活与 Spaces 的完整 Window 分组 |
| Window AX 状态组 | 首轮 **65/66**，原生标题栏“delayed deminiaturization drains the latest state request”的 RestoreBounds 断言失败；增加请求、状态、几何诊断后复跑 **66/66**，最终完整 CTest 又通过 **66/66**。再对 v65 旧负载与 v66 新负载分别运行三次独立进程，每次两种标题栏，合计 **12/12**；动态加载日志确认各进程使用对应的原生库。首轮失败仍未定位，后续通过不表示该间歇问题已经修复 |
| 前台选择新组 | 已编译，实际激活前提尚未满足；未计为通过 |

保留了 `jalium.native.platform.window.accessibility.tests --delayed-restore-only` 独立
复现入口；默认完整分组仍为 66 项。失败时输出最新请求、当前状态、最小化标记、
期望和实际 RestoreBounds 及原生 frame；没有通过延长等待或放宽断言掩盖失败。

曾尝试在非激活夹具中直接调用 `becomeKeyWindow` / `becomeMainWindow` 建立前台
前提，四项新增检查都未能建立实际原生 Key/Main，结果为 66/70；这些是夹具前提
失败，已撤销并改为上述真实前台分组，不作为产品缺陷基线。另用标准 NSWindow
做了本进程属性对照，但应用始终 inactive；只观察到 Main=False 没有撤销其 AX
主窗口值和现代/旧 Focused 路径的差异，不计为前台行为验收。

v66 冻结包构建成功，16 份原生库、3 个程序集和 3 个资源均与最新负载一致，
并通过深度严格 ad-hoc 签名检查。platform 库最终构建 SHA-256 为
`56acc53a69a487b66c428e3fa5471e53ce0e8c5c0ab712427bee1233aaa39bad`。
v66 尚未启动；锁屏发生在继续筛选 Position 之前。外部 Position/Size 写入、
修复后 Main/Focused 往返与实际禁用恢复、模态返回后的编辑、新负载的物理 Zoom
与 Spaces 截图，以及 VoiceOver、混合 DPI、Intel 和 macOS 15.0 验收继续开放。

### v63–v64：还原回调的原生 Zoom 与全屏完成通知

本轮先运行 v63 Lab（PID **76227**），随后发现并修复两处原生状态重入缺陷；
v63 桌面截图及外部读取发生在这些修复之前，不能作为 v64 新负载的实测证据。

- 还原状态通知尚未完成时执行原生 Zoom，会把仍未还原的最大化几何捕获为普通
  RestoreBounds。现在等当前转换与 AppKit 的 deminiaturize 确认结束后执行；排队
  的原生 Zoom 继续调用 AppKit 最佳尺寸路径。后来的显式状态请求替代旧 Zoom，
  两次排队 Zoom 抵消；执行前重验可见、启用和缩放能力，拒绝已失效的用户动作。
- 全屏完成通知在回调前保存旧请求，回调中的新状态随后被这个旧值覆盖。现在
  通知与样式恢复期间排队新请求，在 AppKit 完成通知返回后读取最新请求。
  回调关闭窗口时通过弱视图的 owner 身份检查终止后续操作。

新增最初四项原生断言在修复前的基线为 **52/56**，两种标题栏分别复现上述两处
问题；修复后 **56/56**。再补充最佳尺寸、替代请求、抵消、隐藏/禁用/固定窗口与
延迟还原的边界后，最终 **66/66**。Window AX 夹具实际运行 44.13 秒，CTest 总限时
调整为 60 秒，保留每项自己的等待限时。

| 检查 | 本轮结果与范围 |
| --- | --- |
| 原生 Window 属性与状态 | **66/66**；原生/自定义标题栏均覆盖正常还原回调与延迟 deminiaturize 中的 Zoom、AppKit 委托的 400×300 最佳 frame、显式请求替代、两次 Zoom 抵消和失效动作拒绝。全屏新增断言重放本进程 delegate 通知，检查退出回调的新 Maximized 请求及 RestoreBounds，未执行真实 Spaces 切换 |
| 托管 Window 属性宿主 | **10/10**；在原有 8 项之外，由实际 `NSWindow.Zoom` 发起还原，再从托管 StateChanged 请求 Zoom，核对最终 Maximized、普通 RestoreBounds 与再次还原后的客户区尺寸 |
| 既有窗口宿主 | 标题栏 **10/10**、默认/取消 **12/12**、Window 无障碍 **14/14**；连同属性宿主共 **46/46**，运行前验证宿主 16 份原生库副本均与最新构建负载的 Mach-O sections 一致 |
| 原生 CTest | 平台协议、Window 属性、无障碍协议、Window 属性与状态写入 **4/4 组**通过；完整原生 Window / 实际 Spaces 检查没有在新修复后重跑，v62 的通过记录只作历史证据 |

v64 Lab 构建成功，8 个原生库的 16 份打包副本与最终构建负载按 Mach-O section
核对一致，3 个托管程序集及两个 metallib、manifest 均与构建源一致；整个包通过
ad-hoc 深度严格签名检查。原生 platform 库的最终构建 SHA-256 为
`65aa4223dcebfc18af183c5b672d88f1dc90478751f23150aaa863ecba743ae7`，
与实际运行过的 v63 旧负载不同。v64 未启动，构建与签名检查不计为桌面验收。

实际桌面与外部读取：v63 属性窗口缩到 **540×540**，截图中说明、编辑框、状态
和两行共六个操作按钮均可见，没有观察到裁剪。点击编辑框并按 Tab 后，截图显示
按钮焦点；NSWindow Main/Key 通知与托管激活记录均为 True。Lab 状态面板新增实际
原生 Key/Main 值，观察器只接受当前窗口的通知并在关闭时移除。

外部 Accessibility Inspector 明确选中 **JaliumWindowLabv63 (76227)**；从
NSApplication 的 FocusedWindow 关系进入“窗口属性 · Window 验证”，读取到
`JaliumAppleWindow`、AXWindow / AXStandardWindow、Enabled=True、Modal=False，
Position **940,255**、Size **540×540**、Main / KeyboardFocused=True、Minimized=False，
以及三项标题栏按钮引用。这是实际外部读取，Main/Key 的形成来自桌面点击与 Tab，
不表示外部 Main/Focused 写入已经成功。

外部 Position 写入尚未完成：Inspector 虽显示数值，但 CUA 坐标操作返回
`noWindowsAvailable`；后续键盘尝试改变了 Inspector 的设备/进程选择，最终桌面工具
明确报告 Mac 锁定。未观察到目标位置改变，没有将工具错误计为产品属性写入失败。
外部 Position/Size/Minimized/Main/Focused 写入、模态返回后的实际编辑、新负载的
物理 Zoom 与真实 Spaces 转换，以及 VoiceOver、混合 DPI 和 Intel 验收继续开放。

验证界面沿用纸色背景、深色文字、青色强调与 PingFang 的现有样式。按照
[Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)
中适用于原生控件的名称、键盘焦点与内容布局规则检查，补充属性编辑框的中文
Automation 名称“窗口属性验证编辑框”；新名称只完成源码与构建检查，未在锁屏后
声称外部读取通过。HTML、ARIA、浏览器导航等规则不用于原生 AppKit 合规结论。

重复验证：HostSmoke 使用 `--window-property-accessibility`，单项参数为
`--window-property-accessibility-case=0` 至 `=9`；原生 CTest 名为
`jalium.native.platform.macos.window.accessibility`。Lab 主窗口进入“窗口属性验证”，
再执行最小尺寸、外部属性写入、标题栏切换及模态返回编辑。

本轮测试与打包检查结束后停止自己的 v63 Lab 进程，并按用户要求清理两个仓库
全部 artifacts，共移除 **3,027 个文件、约 2.65 GB**，以及 **3 份**本轮临时日志。
全量复查 artifacts 目录数量为 **0**，本轮验证进程与临时日志无残留；相关工作树
文件及 `.tools/macos-window-lab` 源码的清理前后哈希一致。桌面锁定期间通过进程
信号停止自己的 Lab，这不计为原生 Quit 生命周期验收。源码、测试、验收记录及
现有工具链依赖保留；再次运行需要重新构建。

### v62：窗口属性写入与最小化恢复队列

2026-10-08 从当前工作树构建独立原生库、HostSmoke 和 Window Lab。8 个原生库的
16 份应用副本按 Mach-O section 校验一致；3 个托管程序集与构建源包一致，两个
metallib 和 manifest 与原生负载一致，Lab 通过 ad-hoc 深度严格签名校验。
本轮没有修改 Metal 或托管窗口实现；v61 的 960 项托管与 33 项 Metal 结果作为历史
记录保留，没有计入 v62 的重新执行结果。

本轮补齐的窗口属性行为：

- `setAccessibilityFrame:` 修改实际窗口。原生标题栏沿用 AppKit；自定义无边框窗口
  使用实际框架 setter，遵守客户区最小/最大尺寸，固定大小窗口仍可移动。组合写入
  先按现有左上角缩放，再完成最终位置，确保最后的 Move 通知使用最终尺寸。真实
  客户区尺寸、托管 Left/Top/Width/Height 和用户缩放引发的 SizeToContent 回写同步。
- Frame、Minimized、Focused、Main 的方法发现与旧版属性可写元数据同步；禁用、
  隐藏和销毁对象拒绝写入，状态转换中的几何写入拒绝。没有最小化能力的普通窗口
  不接受最小化。主线程检查排除外部线程直接访问 AppKit 的 setter。
- Main/Focused getter 读取实际 AppKit main/key 状态，避免通用 setter 仅缓存 AX 值。
  设置请求返回客户端后再在主线程队列执行，排队后重新检查对象、可见性与启用状态。
  激活回调可进入托管模态循环，不在 AX 客户端的同步调用内执行用户代码。
- 最大化窗口最小化后还原时，AppKit 的 `deminiaturize:` 可能延迟确认。旧流程在确认
  前再次执行最大化，把已经最大化的尺寸保存为正常 RestoreBounds。现在等待原生
  确认再发布恢复前状态，转换期间的新请求保留最后一个；通知回调的新状态在 AppKit
  返回后处理。原生系统直接还原与平台 Activate 也使用同一状态约定。

失败基线与夹具修正分开记录：自定义框架写入、约束、固定窗口移动，以及禁用/隐藏
最小化与方法发现的初始原生行为基线为 **15/24 通过**。托管属性夹具在修正启动方式
后为 **6/8**，两项均在最大化→最小化→恢复时丢失正常 RestoreBounds；修复后为
**8/8**。夹具最初使用 Prohibited 激活策略，AppKit 完成启动时隐藏第一窗口，造成
额外几何失败；改为 Accessory、完成启动并先处理事件后才创建夹具，这些启动失败
没有计为产品修复。旧版坐标夹具也曾错误地把 AppKit NSValue 的左下角坐标当作
ApplicationServices 的左上角坐标，已按实际框架与官方约定修正。

当前自动检查：

| 检查 | 结果与范围 |
| --- | --- |
| 原生 Window 属性写入 | **52/52** 通过；旧版 AppKit Position/Size 写入及禁用/隐藏/销毁引用也已检查；原生/自定义标题栏各覆盖几何、约束、固定尺寸、隐藏/禁用/销毁、无效数值、线程拒绝、旧引用、排队激活和回调销毁。新增延迟 deminiaturize 夹具，验证最后一个 Normal/Maximized/Minimized 请求及 RestoreBounds，覆盖原生还原、回调再次请求和关闭 |
| 托管 Window 属性宿主 | **8/8**；两种标题栏的组合几何、自适应退出、只移动保留自动尺寸、模态 owner/隐藏拒绝与恢复、最大化最小化恢复及中文内容保留 |
| 既有窗口宿主 | 标题栏 **10/10**、默认/取消 **12/12**、Window 无障碍 **14/14**，共 **36/36** |
| 原生 CTest | 平台协议、Window 属性、无障碍协议和 Window 属性写入 4 组通过；完整原生 Window 检查另行通过，包含实际 Spaces 全屏与状态转换 |

旧版 NSAccessibility 属性读写检查是本进程 AppKit 接口检查；它不等于外部 Inspector
写入或 VoiceOver 验收。坐标约定见 [AppKit Position](https://developer.apple.com/documentation/appkit/nsaccessibility-swift.struct/attribute/position)
和 [ApplicationServices AXPosition](https://developer.apple.com/documentation/applicationservices/kaxpositionattribute)。

本轮桌面记录尚待完成：v62 旧负载的属性窗口已查看 680×640 DIP 截图，中文和 emoji
可见，实际输入追加 ` + v62` 成功；随后构建了包含恢复队列修复的新负载。新负载
已启动，但打开属性窗口时 Mac 锁定。Inspector 曾选中 ChatGPT，因此该目标的属性
没有计入 Window 验收。新负载的外部 Position/Size/Minimized/Main/Focused 写入、最小
窗口截图与模态返回后编辑均待解锁检查。

重复验证：HostSmoke 使用 `--window-property-accessibility`，单项参数为
`--window-property-accessibility-case=0` 至 `=7`；原生 CTest 名为
`jalium.native.platform.macos.window.accessibility`。Lab 源码在
`.tools/macos-window-lab/WindowPropertyLab.cs`，从主窗口“窗口属性验证”进入。
本轮测试后已停止验证进程，按用户要求清理两仓库的全部 artifacts，共移除
**2,702 个文件、约 2.33 GB**，以及 **3 份**本轮临时日志。
清理后全量扫描 artifacts 数量为 **0**；相关工作树源码与 Lab 源码的清理前后哈希
一致。新负载未完成外部验收，停止进程不计作 Quit 生命周期通过；再次验收需要重建。

### v59–v61：标题栏按钮、编辑字形与样式切换焦点

本轮从当前工作树重新编译独立原生和托管负载。v61 实际运行的 Lab PID 为
**48985**，没有沿用 v57 Inspector 目标。8 个原生库的 16 份打包副本与构建负载
按 Mach-O section 校验一致，3 个托管程序集与构建源包的哈希一致，整个应用通过
ad-hoc 深度签名校验；两个 metallib 与 manifest 沿用现有资源并校验一致。
这些构建、签名与资源检查不能代替最低系统版本或发布签名验收。

本轮修复三个实际缺口：

- 自定义标题栏缺少标准窗口按钮引用，按钮 Peer 虽返回 Invoke pattern，却没有
  实现 `IInvokeProvider`。新增关闭、最小化、缩放关系与子角色，并接通现有点击路径。
  关系只查找当前安装的 TitleBar，正文中的同类按钮不能取代窗口动作；原生标题栏
  保留 AppKit 返回的原生 Cell。四个名称资源为 `TitleBarCloseButtonName`、
  `TitleBarMinimizeButtonName`、`TitleBarMaximizeButtonName`、`TitleBarRestoreButtonName`；
  显式 Automation Name 与自定义内容名称仍优先。最大化切换为 Restore 时保持 Peer，
  名称变化发送 Name 属性通知。
- v59 的实际编辑框有文本、选区宽度和光标，却没有字形。GPU 检查复现了 PingFang
  16 的自然行高 **22.3999 DIP** 被取整为 **22 DIP** 后得到 **0 个文字像素**。
  v60 在 CoreText 绘制框中保证首行自然高度，并保留原点和调用方裁剪。2 种字体、
  3 种字号、1×/2× 缩放、1×/4× MSAA 的 **24 组配置、48 次捕获**均与向上取整参考
  的逐通道误差不超过 1；完整 Metal 回归 **33 项通过**。实际新构建重新显示中文、
  emoji 和追加文字，没有用 Lab 局部改色绕过缺口。
- v60 从 Custom 切换 Native 后仍显示编辑焦点框，但直接输入无效。原生检查也复现
  `styleMask` 更新丢弃内容 firstResponder。v61 仅恢复切换前已经拥有的内容输入焦点，
  不激活或聚焦未聚焦、禁用窗口。两种初始标题栏共 **10 次转换与 4 项负向检查**通过，
  宿主同时核对实际 NSView responder 与托管编辑焦点。

最终负载的自动检查：

| 检查 | 结果与范围 |
| --- | --- |
| 托管 macOS 测试，启用真实几何 | **960/960，通过，跳过 0**；其中标题栏名称、资源优先级、动态名称通知和 Invoke provider 为 9 项 |
| 标题栏真实 AppKit 宿主 | **10/10**；身份、子角色、本地化、隐藏、禁用、ResizeMode、换模板、原生/自定义切换及焦点、取消/接受关闭、隐藏重用、CSS 优先级与缓存动作 |
| 默认/取消按钮宿主 | **12/12** |
| Window 无障碍宿主 | **14/14** |
| 原生平台协议 | scrollbar/responder 编辑动作、924 项参考与 200 次线程查询、无障碍 ABI/方法/标题栏角色与销毁查询均通过 |
| 原生 Window 属性检查 | `--properties-only` 通过，包含新增样式焦点检查；本轮没有重跑依赖前台的完整 Window/Spaces 检查 |

以上宿主在本进程主线程运行并禁止应用激活。测试启动失败也与行为失败分开处理：
一次新版 `--artifacts-path` 输出目录误配导致原生库加载失败，复制到实际 DLL 同目录后
重跑全部通过；一次宿主 Xcode/workload 校验失败，沿用既有 `ValidateXcodeVersion=false`
后重新构建。两次启动失败都未计为行为检查通过。

桌面及外部验收通过 CUA 完成，截图均实际查看：

- v61 从 **680×620 DIP** 切换 Native，直接追加 `N`；切回 Custom 后直接追加 `C`。
  缩到 **540×540 DIP** 后说明、文本、状态和所有操作按钮仍完整可见。模态中 Return
  结束对话框，返回后无需重新点击便能追加 `M`。
- 自定义缩放按钮进入 **2560×1320 DIP**，原名称变为“还原窗口”；从该状态最小化，
  通过系统 Window 菜单恢复后仍为 Maximized，再还原回 **540×540 DIP**。直接追加
  `R` 成功，完整文本保留为 `标题栏切换后继续编辑🙂 + v61NCMR`。
- 三个按钮分别隐藏时从实际图像和 AX 树消失；隐藏整个 TitleBar 时其子树消失，
  再显示恢复。正文操作、布局及编辑内容保留。隐藏后的缓存 Press 拒绝由宿主检查，
  本轮未将这部分计为外部 Inspector 的缓存负向动作验收。
- 外部 Accessibility Inspector 确认目标为 **Jalium Window Lab v61**，窗口为
  `JaliumAppleWindow`、AXWindow、非模态，并实际显示非空 Close/Minimize/Zoom 引用。
  分别检查三个实际控件的中文名称、`PART_CloseButton` / `PART_MinimizeButton` /
  `PART_MaximizeButton`、AXButton、AXCloseButton / AXMinimizeButton / AXZoomButton
  与 `press` 动作；关闭与缩放目标通过窗口引用导航，最小化目标通过同一标题栏层级导航。
- 在 Inspector 对保留的缩放条目执行两次 Press，名称由“最大化窗口”变为“还原窗口”
  再恢复，Lab 事件确认 Maximized → Normal 与普通尺寸恢复。外部 Press 最小化后，
  系统菜单恢复同一窗口，Inspector 保留的最小化引用再次可读。没有将该路径称为
  Dock 重开，也没有将属性刷新视为完整通知订阅验收。
- v61 自定义关闭第一次取消、第二次接受，记录恰好两次 Closing 和一次 Closed，
  文本没有丢失；v60 原生关闭也实际检查取消与接受。最后关闭本轮 Lab 主窗口，确认
  v59/v60/v61 和宿主测试进程均已退出；Inspector 的筛选恢复为空、元素跟踪恢复关闭。

本轮仅完成以上 Window 范围。最新 Gallery 整体、Dock、VoiceOver、真实中文候选窗、
混合 DPI 多屏、其他 Spaces 组合、macOS 15、Intel 与发布签名仍未验收。

### v58：CSS 按钮关系与内层模态的外部验收

本轮复用已经运行的 Lab v57（Inspector 目标 PID **37717**），没有修改产品源码、
重建应用或重复自动测试。验收通过 CUA 操作真实窗口、Accessibility Inspector 的
关系导航和 Press，以及实际 Return/Escape；窗口截图在工具内查看，未导出 PNG。

在 **660×600 DIP** 和最小 **540×580 DIP** 两种尺寸下，各检查以下六种模式：

| CSS 模式 | AXDefaultButton 实际目标 | AXCancelButton 实际目标 | 后代按钮在外部树中 |
| --- | --- | --- | --- |
| CSS 隐藏 | 可见确认 | 可见取消 | 排除 |
| 显式显示后代 | 后代确认 | 后代取消 | 可用 |
| 原生折叠 | 可见确认 | 可见取消 | 排除 |
| display:none | 可见确认 | 可见取消 | 排除 |
| 折叠 flex 项目 | 可见确认 | 可见取消 | 排除 |
| 退出动画（60 秒） | 可见确认 | 可见取消 | 排除 |

十二组窗口均报告 AXWindow、AXStandardWindow、Enabled=true、Modal=false。每组
分别沿默认/取消关系到达实际 AXButton，确认目标名称及 Enabled=true，再分别
执行外部 Press 与编辑框中的 Return/Escape。累计 **24 次确认、24 次取消**，
隐藏按钮调用始终为 **0**；每种模式各产生四次确认、四次取消。事件 **12–81**
（UTC 11:04:23–11:28:06）的连续序号与最后的窗口状态共同核对了上述计数。
宿主日志沿用 v56 前缀，因为本轮运行的是此前复用的程序集。

两种尺寸均在“显式显示后代”时保留取消按钮的 Inspector 引用，再切到原生折叠。
刷新同一引用后，标题、角色、启用状态和矩形均为 None；Inspector 仍显示缓存的
Press 项，实际执行该项没有调用按钮，计数分别保持 **4/4/0** 和 **16/16/0**。
退出动画检查分别在切换后 **7,483 ms** 和 **15,137 ms** 内完成，截图仍绘出退出中
的后代按钮，但它们已经不参与外部树、默认/取消关系或按键动作。

常规尺寸与最小尺寸的截图中，中文说明和结果换行可读，操作按钮完整可见，编辑
焦点为绿色。此次只接受窗口尺寸、关系及动作结果；不同观测间的绝对屏幕位置
发生变化，没有据此接受跨屏坐标映射、混合 DPI 或显示器边界约束。

内层模态 **600×370 DIP** 的两项关系分别到达实际“确认”和“取消”按钮，均为
Enabled=true 的 AXButton。外部取消 Press 返回 **False**，第二次新建内层的默认
Press 返回 **True**；两次事件均显示“外层可用 True，主窗口可用 False”。取消
返回后，Inspector 实测外层仍为启用的 AXDialog、Modal=true，主窗口仍为禁用的
AXStandardWindow、Modal=false。两次返回后没有重新点击编辑框，继续实际输入，
正文依次保留为 `模态窗口焦点 + v58` 和 `模态窗口焦点 + v58 + confirmed`，
焦点截图与编辑结果一致。外层第一次 Return 取消关闭并保留内容，第二次接受
关闭返回 True，主窗口恢复 Normal、Manual、**760×860 DIP**。

共取得 **47** 次 Inspector 属性观测，其中两次仍指向应用或先前窗口，已排除出
关系目标的验收；待 Inspector 显示实际目标后才执行 Press。另有 **51** 次 CSS
状态和 **7** 次模态状态观测。一次截图失败后通过已打开窗口的 Raise 恢复，没有
重复创建验收窗口。Dock 获取仍返回 **-10005 timeoutReached**，当时的应用清单
没有 Dock；本轮没有完成真实 Dock 重开。通知接收、VoiceOver、真实 IME、多屏
及自定义标题栏的标准关闭/最小化/缩放关系继续开放。

v57 的 **951/951** 托管、**146/146** 宿主、**40/40** 可见性和 **28/28** 透明度
结果属于此前自动检查，本轮未重跑。完成本轮记录后，清理两个仓库的 artifacts，
包括主仓库的 `src/native/artifacts`；不另建证据目录或恢复已删除的应用负载。

### v57：普通窗口子角色与无障碍方法发现

v57 补齐外部无障碍检查发现的两项原生行为。v56 的普通主窗口被模态禁用后，
Inspector 报告 AXEnabled=false、AXModal=false，却把子角色和描述变为 AXDialog。
同一原生协议探针在旧平台库上失败，替换平台库后通过；普通窗口现在明确保持
AXStandardWindow，对话框仍按托管标记报告 AXDialog，禁用外层模态仍保留模态。

v56 的静态“macOS Window”还被 Inspector 显示为可编辑的 Value。原有选择器权限
已经拒绝写入，但继承的 setter 仍被方法发现公开；v57 将写入方法和动作的发现与
同一 provider 能力判断连接。相同可执行探针的八种状态从 **3/8** 提升到 **8/8**，
覆盖静态值/焦点、只读文本值/选区、可写文本值/焦点和禁用文本值/选区。探针中
自进程 AXUIElement 查询返回 **-25208**，未取得属性可写性，不能作为外部验收；
以下结果来自实际外部 Accessibility Inspector 与 CUA 的窗口操作。

最终签名 Lab v57（PID **37717**）完成外部检查：

- 主窗口启用及被外层模态禁用时，均报告 AXStandardWindow、AXModal=false。
  静态文字 Value 在 Inspector 中只读，CUA 树不再标记 settable；TextBox 保持可写。
- 外层 **640×400 DIP** 与内层 **600×370 DIP** 报告 AXDialog、AXModal=true。
  外层 AXDefaultButton、AXCancelButton 分别到达实际“确认”和“取消”；打开内层后
  两个外层按钮仍被关系引用，并报告 Enabled=false。内层两项关系存在，本轮未继续
  导航其目标。内层 Enter 返回 True，只恢复外层与原先焦点，主窗口继续禁用。
- 通过 AX 写入中文与家庭 emoji，再用实际按键追加文字；Tab 移到确认按钮，绿色
  焦点完整可见。第一次 Enter 取消关闭并保留编辑，内层返回后可继续输入。
- Inspector 保持同一个“确认”引用，外层 Hide 返回 False，主窗口恢复可用；刷新后
  旧目标的标题、角色、启用状态和矩形不可用。Inspector 仍缓存 Press 项，实际执行
  该项没有触发按钮或重开窗口。同一模态重开后，未重新选取目标，仅刷新就恢复原
  引用与可用状态，原文本保留；实际外部 Press 完成关闭，返回 True。关闭后再次
  刷新及执行缓存 Press，目标仍不可用，主窗口和事件没有变化。

**16** 次 Inspector 属性观测和操作记录保存在 `desktop-observations.json` 与
`desktop-verification.json`。截图在工具内实际查看，未导出 PNG。最终主窗口仍为
Normal、Manual、**760×860 DIP**，事件显示隐藏结果 False 和重开确认结果 True。
静态文字的 Focus 字段仍显示 Inspector 的布尔编辑器，未全面确认所有可写属性；
通知的外部接收、VoiceOver、真实 IME、Dock 和多屏验收继续开放。

本轮从冻结 v53 的 **497** 个源码文件复制隔离快照，仅替换 Window 原生实现、
无障碍桥接及其协议测试三个文件，重建 platform。其他七个原生库和三份 Metal
资源继续使用 v56 中保留的 v53 负载。Lab、Host 和完整托管夹具复用已验收的 v56
程序集，日志文件仍使用 v56 前缀；没有声称编译共享目录中其余并行改动。

新平台负载上，原生无障碍协议、滚动条/编辑动作和 **924** 次 AppKit 按词对照及
**200** 次线程查询通过。真实 AppKit/Metal 宿主 **146/146**、可见性 **40/40**、
透明度 **28/28**、同一完整托管夹具 **951/951** 通过，**802** 次上下文初始化均
为有效 Metal。程序集、冻结源码、原生副本、签名和测试结果均单独核验。
本轮没有重跑前台 Window CTest 或 v55 的二十项背景 GPU 检查，也未重复全部
透明度/材质桌面操作。Window 与整体 macOS 目标继续进行中。

v56 补齐已关闭窗口的异常后状态一致性。v55 已拒绝再次显示，但被拒绝的
Visible 值会留在属性存储中，使之后读取 Visibility 再次抛异常。冻结 v55 负载的
三种写入探针均复现；同一测试程序集在旧负载上 **1/16** 通过、**15** 项失败。

依赖属性现在对具有强制回调的属性保存单个属性的来源层及当前动画记录，在
强制回调拒绝写入时恢复。单来源属性无需额外快照对象；多来源复制该属性的层
容器，不回滚回调对其他属性的合法写入。有效值确认后再发布属性源、绑定和渲染
通知；已接受值的 PropertyChanged 异常仍保留提交结果。这是属性存储的局部修复，
未改写 AppKit 关闭流程，也未改变其他平台的 Window 可见性约定。

关闭后的 CLR setter、SetValue 和 SetCurrentValue 各重复失败三次，Visibility、
IsVisible、内容树、RestoreBounds 与句柄仍可读取；绑定拒绝后仍可更新合法值。
默认/本地/当前/样式来源、清值与清层、回调中的其他属性写入、正在运行或尚未开始
的自动过渡也有定向检查。修复后同一 **16/16** 夹具通过；相关定向回归 **31/31**。
原生可见性 **40/40**、透明度 **28/28**、既有宿主 **146/146** 通过。完整原生
文字几何模式托管检查 **951/951**，**802** 次上下文初始化全部得到有效 Metal。
相比 v55，测试净增 39 项，其中 15 项为本轮窗口/属性检查，24 项来自共享目录的
TabFocusInputTests 并行补充。本轮不将这 24 项计作自身实现。

Host、Lab、普通定向检查与完整托管检查各自保存程序集指纹；关键 Window、
DependencyObject、DependencyValueStore、UIElement 和 RenderTarget 源码分别通过
PDB 核对，**62** 项编译来源校验全部通过。原生负载继续冻结自 v53；原生及自定义
标题栏宿主检查采用 Prohibited 激活策略，不替代实际桌面验收。

Lab 新增“关闭后再显示检查”，先关闭已显示的编辑窗口，再检查三种失败请求和
可读状态。实际桌面完成重复显示/关闭循环，未出现额外创建；没有编辑窗口时显示
明确提示并保留计数。重新创建后清除旧提示，关闭后显示“编辑窗口已关闭”。
最小 **420×380 DIP** 下已查看 Tab/Shift+Tab、尾部焦点与键盘恢复尺寸；验收发现
顶部结果位于当前视口外，已在检查按钮旁增加可换行结果文字，最终包截图确认
结果、按钮和焦点可见。标准尺寸 **650×640 DIP** 也已查看。截图均在 CUA 工具
内查看，未导出为本地图片；中间签名包和源码保留。

本轮验收清单：关闭后拒绝显示和状态读取、重复失败、绑定后续更新、属性来源
恢复及动画状态由自动检查验证；已显示窗口的关闭、重复创建和无窗口分支由实际
桌面验证；最小尺寸、结果反馈与正反键盘焦点由最终包截图和操作验证。完整外部
AX 关系、VoiceOver、真实 IME、Dock、多屏和系统材质设置切换继续开放。
v55 的透明度/材质桌面结果保留为该版本证据，本轮未重复其全部操作，也未重跑
背景 GPU 或前台 CTest 套件。Window 与整体 macOS 目标继续进行中。

v55 补齐 macOS Window.Visibility 的有效值路径。此前只有直接 CLR setter 能
隐藏原生窗口，绑定、样式和依赖属性写入会留下仍可见的 NSWindow；未显示的
Window 也继承了控件的 Visible 默认值。macOS Window 现在默认 Collapsed，
同时初始化整棵内容树的 IsVisible。首次 Visible 请求排入当前窗口线程的调度队列；
排队期间隐藏或关闭会取消创建，显式 Show() 仍同步完成显示。这项选择参考
[WPF Show 的可见性和异步显示约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.show?view=windowsdesktop-10.0)。

显示和隐藏使用 SetCurrentValue 保留绑定。解绑、清值和样式切换按属性优先级
恢复有效可见性，复用既有句柄、几何与文本；已有窗口通过属性恢复不会重复触发
Shown。SourceInitialized 内隐藏后重新显示仍完成 Loaded；模态绑定隐藏或折叠
结束模态循环，并恢复 owner 与原先禁用的其他窗口。窗口关闭取消待执行的显示，
进入 Collapsed，随后拒绝重新设为 Visible。其他平台保留既有路径。

新增 `--window-visibility` 在原生和自定义标题栏上覆盖二十种场景。修复前冻结
应用通过 **2/40**，修复后的同一检查通过 **40/40**，保留 **102** 个最终托管/
原生可见性样本。透明度 **28/28**、既有宿主 **146/146**、背景 GPU **20/20**
均重新通过。最终启用原生文字几何的托管测试 **912/912**；其中七项路径几何
测试来自共享目录的并行修改，并不计作本次 Window 新增功能。原生宿主和 Lab
使用同一托管程序集；这套最终托管测试使用稍后的程序集，各自的 Window 等
关键编译源码均由 PDB 单独核对。

托管焦点测试改用显式可见的逻辑窗口根，不从 xUnit 工作线程创建 AppKit 窗口。
原生文字检查按每个测试建立 Metal 上下文，防止 Application 的退出检查销毁
全局上下文后影响后续检查；799 次初始化均获得有效 Metal。候选光标与按视觉行
导航的编辑器先完成有限尺寸布局，保留原有断言。普通托管 Window 核心模式
**186** 项通过，**2** 项明确跳过的 CoreText 几何检查在原生模式中实际执行并通过。

v55 实际桌面已经读取并查看窗口截图与外部 CUA 树：绑定隐藏恢复、Collapsed、
Show/Hide 保留绑定、清值回退、样式显示及解绑回退已检查；中文、家庭 emoji
与后续键盘输入保留，恢复后编辑器重新聚焦，整个循环只创建一次原生窗口。
新增“显示与隐藏验证”面板提供最小/标准尺寸入口。验收发现滚动内容外边距使
底部键盘焦点被截断，已改为带内边距的内容容器；重新打包后的 **420×380 DIP**
截图确认底部焦点完整可见，Tab/Shift+Tab 自动滚动到目标。

“透明与背景验证”已实际查看透明和 alpha=96 半透明背景的下层彩色窗口合成，
以及依赖属性 35%、绑定 80%、样式 50% 的整窗透明度；切换后仍可继续编辑，
客户区和 RestoreBounds 保持 **700×830 DIP**。动画中修改基值 65% 后，托管与
原生仍使用约 31% 的当前动画值，结束后都恢复 **65%**。解锁后完成补验，并在
最终包再次检查动画期间和结束值。Auto/Mica 呈深色、Mica Alt 呈中灰、Acrylic
呈浅灰原生背景；切换保留几何、中文、家庭 emoji 和编辑焦点，Acrylic 后继续
键盘输入成功。这些观察未覆盖系统外观、减少透明度或失活状态下的材质合成。

外观面板在 **520×560 DIP** 下曾横向截断最后一个按钮；将该滚动面板的横向
滚动禁用，使内容按有限宽度布局。最终包截图确认尾部按钮文字与焦点边框可见，
Tab/Shift+Tab 自动滚动，Return 恢复 **700×830 DIP** 并重新聚焦编辑器。
两个布局修复均限于 Lab。修改前及中间尝试的签名应用、源码和 PDB 保留，最终
包与原生宿主使用相同的 Window 托管负载；共 **51** 项编译来源校验通过。
截图在工具结果中查看，未导出为本地图片。
外部 AX 默认/取消关系、VoiceOver、实际 IME 候选窗、Dock 和多显示器等检查
继续开放。冻结原生负载仍来自 v53；本轮未用其他自动化绕过锁屏，也未重跑
CTest 前台窗口检查。整体 macOS 目标保持进行中。

本轮验收清单区分需求来源；“通过”只覆盖列出的证据范围。

| 来源 | 验收项 | 实际结果 |
| --- | --- | --- |
| 明确目标：先补齐 Window | 属性驱动的显示/隐藏、首次显示、重复操作、关闭后的行为 | 同一原生夹具由 2/40 提升到 40/40，102 个托管/原生样本 |
| 常规窗口能力 | 绑定/样式优先级、清值/解绑、Show/Hide 保留绑定、模态隐藏恢复 owner | 原生两种标题栏模式检查通过；桌面绑定、清值及样式回退已操作 |
| 常规边界与异常 | 隐藏/关闭取消排队显示、SourceInitialized 内隐藏、拒绝重开已关闭窗口 | 原生及托管定向检查通过；桌面没有逐项覆盖所有异常 |
| 常规可用性 | 隐藏后文本和焦点保留、恢复后继续输入 | 桌面中文/emoji、编辑焦点、继续键盘输入通过，循环复用同一原生句柄 |
| 合理补全：验收面板 | 最小尺寸、长内容滚动、正反 Tab、尾部焦点、键盘恢复尺寸 | 420×380 的可见性面板和 520×560 的外观面板截图与键盘操作通过 |
| 常规外观能力 | 背景 alpha、属性/绑定/样式透明度、动画基值、材质切换 | 20 项 GPU、28 项原生透明度和列出的桌面合成操作通过 |
| 尚缺的完整验收 | VoiceOver、外部 AX 关系、真实 IME、Dock、多屏、系统外观/减少透明度 | 继续开放；不得据本轮结果声称 Window 或全部 macOS 行为完整 |

v54 补齐动态 Window.Opacity。原先只有 CLR setter 更新原生透明度，绑定、样式、
SetValue、SetCurrentValue 和动画仅更新托管有效值。真实 AppKit 复现中，
SetValue 设置 65% 后 NSWindow.alphaValue 仍为 100%；动画保持 40% 时通过 CLR
设置基值 80%，原生窗口又错误采用 80%。Window 现在通过合并后的依赖属性元数据
推送 macOS 的有效值，保留原有组合渲染标志，其他平台的 CLR setter 路径保持原样。

验证同时暴露共享属性系统的清理问题：解绑留下绑定值，ClearValue 未清除默认值上
的 SetCurrentValue 修饰值；显式 HoldEnd 动画移除时把动画值写回基值。
ClearBinding、ClearAllBindings 与 ClearValue 现在恢复下层值并通知属性变化，
重新绑定仍直接从旧值转到新值。显式动画移除或替换丢弃动画层，保留样式、绑定和
局部基值。参考 [ClearBinding 的恢复约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.data.bindingoperations.clearbinding?view=windowsdesktop-10.0)
及 [BeginAnimation 的移除约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.beginanimation?view=windowsdesktop-10.0)。
这些共享修复影响各平台，相关兼容检查在本 Mac 执行；其他平台本轮未实机运行。

新增 `--window-opacity` 使用真实 NSWindow 读取 alphaValue，在允许透明与普通窗口
上覆盖十四种路径，并断言句柄、客户区几何与 RestoreBounds 保持。
同一验收程序的修复前结果为 **4/28**；仅加入有效值同步后为 **16/28**；
动画清理修复后为 **22/28**；补齐属性清理后为 **28/28**。三个中间版本的应用、
源码和逐步属性值均保留。新增动画检查旧版本 **0/4**，属性清理检查旧版本 **3/8**。
最终动画 **143/143**、绑定与属性 **126/126**、托管 **897/897**、既有宿主
**146/146**、背景 GPU **20/20** 和富文本兼容 **56/56** 通过。

v54 Lab 的“透明与背景验证”新增依赖属性、绑定、样式和动画入口，同时显示托管
与原生透明度。应用已编译、签名和打包，关键编译来源由 PDB 校验。
原生负载沿用已冻结的 v53，当前其他原生编辑没有混入该应用。v54 没有重跑原生
CTest 的前台窗口检查。v54 桌面工具仍报告 Mac 锁定，因此当时新入口的实际截图、最小
布局、键盘焦点与合成效果仍未验收。整体 macOS 目标保持进行中。

v53 修复了半透明 Window.Background 在 Metal 中重复预乘的问题。Window 原先先
将 RGB 乘以 alpha，Metal 的 Clear 和画刷片元随后再次预乘，导致整窗与局部背景
都偏暗。实际 GPU 复现中，alpha=96 的预期 BGRA 为 `95,94,92,96`，修复前为
`36,35,35,96`。Metal 路径现在传入原始 RGB，其他后端的传参保持原样。

新增 `--window-background-alpha` 宿主检查调用实际 Window 背景绘制路径，比较
独立原生画刷与预乘像素期望。修复前 Impeller 的 10 项中仅 4 项通过；修复后
Impeller/Vello、1×/2× DPI、alpha 0/64/96/128/255 的 20 项全部通过，局部区域
重复绘制三次仍匹配预期，区域外像素保持不变。GPU 读回对照图位于
`artifacts/macos-window-verification/v53/window-alpha-gpu-comparison.png`，它不是桌面截图。

v53 还重跑托管 **886/886**、真实宿主 **146/146**、富文本兼容 **56/56** 检查。
当前其他工作新增的托管检查一并运行，未将这些检查归为本轮新增实现。
新原生负载使用独立源码副本构建，并重新生成 Metal 资源；构建后继续变化的原生
文件单独记录，不宣称已混入或验证于这份负载。原生非前台九项检查合并重跑结果
通过，烟雾测试先超时、随后单独运行通过；超时原因未确认。首次完整窗口检查
在激活步骤失败，后续桌面工具报告 Mac 锁定，前台窗口检查尚未重新验收。

v53 Lab 新增“透明与背景验证”，用下层彩色窗口观察透明、半透明和原生材质，
同时提供整窗透明度、置顶、边框与尺寸切换，记录编辑内容和原生表面属性。
该应用已编译、打包并签名核验。当前桌面工具无法读取锁定的 Mac，因此新入口
的实际截图、输入焦点、材质合成与最小尺寸布局仍待验收；未据编译或 GPU 检查
宣称这些界面交互通过。外部默认/取消按钮关系也仍等待检查器选中真实元素。

v52 独立复查菜单 Escape。CUA 的菜单栏 AX 操作在纯 AppKit/NSTextView 对照中
同样没有产生菜单跟踪通知，Escape 直接到达编辑器；因此这条工具路径不能证明
真实菜单的按键分发。对照的真实弹出菜单则进入跟踪循环，Escape 关闭菜单而不增加
编辑器取消次数。本轮没有据此修改产品窗口逻辑。

v52 Lab 的模态窗口新增“打开原生 Window 菜单”，通过公开 PopUpMenu API 打开
现有 Window 菜单。首次诊断入口依赖 key window，在工具后台操作中没有目标，
已修正为使用该模态窗口自己的原生视图。初次未激活窗口的菜单在按键前已关闭，
其结果单独保留；正式验收先通过桌面点击选中实际模态窗口，检查可见菜单与跟踪通知。

实际 **1280×800** 截图确认原生菜单绘出，Escape 后菜单消失，状态仍是“首次关闭
会取消”，未触发 Closing。编辑器继续聚焦并接受“菜单关闭后仍可编辑 · Window A”；
后续一次 Escape 才触发首次取消关闭，再一次返回主窗口，False 结果与可用状态 True
同时确认，普通尺寸及 RestoreBounds 仍为 **760×860 DIP**。

v52 Lab 和纯 AppKit 对照均已编译、签名并检查；纳入记录的 116 份其他非原生
源码指纹及冻结原生负载与 v51 相同，v51 的 **807/807、146/146、56/56、22/22**
结果作为此前证据保留，
本轮不宣称重跑这些检查。当前其他任务的原生改动仍单独记录，未混入冻结负载。
菜单栏物理键盘、IME、VoiceOver、外部按钮关系、Dock、混合 DPI、最低系统版本、
Intel、裁剪/AOT 及 Window 以外范围仍然开放。

v52 Window Lab：`artifacts/macos-window-lab/Jalium.Window.Lab.v52.app`。

此前 **v51** 的验证记录保留，记录位于
`artifacts/macos-window-verification/v51/validation-results.json`。
本轮修复实际窗口中富文本 AX 正文正常但字形空白的问题。绘制缓存原先仅比较
FormattedText 的文字和标量字体属性，忽略原生段落的样式及所有权；重排后可能
复用已释放段落。原生排版现在独立保留到绘制快照，退出标量对象池；录制帧通过
SafeHandle 引用持有不可变段落，布局释放仍立即拒绝后续布局查询，后台回放继续有效。

相同新测试加载冻结 v50 程序集时，五项托管录制检查 **4 失败、1 对照通过**，
四项真实 GPU 重排/释放检查全部复现字形丢失。v51 托管 **807/807**、AppKit 宿主
**146/146**、富文本兼容 **56/56**、Gallery **22/22** 通过。新增检查经过实际
Visual.Render 的录制和干净缓存回放，并从后台线程回放已释放布局的原生段落。

**168 张**本轮宿主 GPU 捕获及 **10 张**旧负载对照导出 PNG；新增录制/生命周期
的 **20 张**与 **10 张**对照按原始分辨率查看。旧版改颜色后和释放后的文字空白，
新版颜色正确、释放后的字形逐字节保留。已安装与私有 Helvetica Neue 图像相同。
另外 80 张本轮富文本撤销捕获与 v50 已查看图像逐字节一致，保留该视觉检查依据。

实际桌面截图恢复到 600 DIP 的 **1200×1300** 及 320 DIP 的 **640×1300**。
初始字形、三行粘贴、撤销恢复原文与全选范围、重做恢复三行和光标均已查看；
Helvetica 900 与私有 Helvetica 900 切换继续绘出。窄窗口的中英文/希伯来文
长行自动换行、点击聚焦、撤销和重做也通过桌面检查。Command+W 返回主窗口。

未修改的原生 v50/v47 负载在独立前台 Window 检查中通过最小化及完整全屏转换。
桌面 Command+M 的事件确认 Minimized→再次激活后的 Maximized，随后普通尺寸
和 RestoreBounds 恢复 760×860 DIP；全屏菜单退出后最终 Normal 与尺寸也恢复。
这不是 v50 失败根因已修复的声明，也不算实际 Dock 点击验收。

模态菜单 Escape 尝试出现菜单仍在、选定窗口改变的现象，随后 CUA 报告用户变更
应用，并读到新开的字体窗口；该次结果保持不确定，需要独立重现。IME、VoiceOver、
跨屏 DPI、Dock、macOS 15/Intel/裁剪/AOT 及 Window 以外范围仍然开放。
当前原生源码的并行改动单独记录，未混入本轮冻结验收负载。

v51 签名应用为：

- `artifacts/macos-window-gallery/Jalium.Window.Validation.v51.app`
- `artifacts/macos-window-lab/Jalium.Window.Lab.v51.app`

此前 v50 的验证记录保留：
准备好的 Gallery 与 Window Lab 验证应用为 **v50**，记录位于
`artifacts/macos-window-verification/v50/validation-results.json`。
本轮统一富文本的 UTF-16 范围替换：全选替换移除所有选中段落，跨段删除合并保留
前后未选中的节点及继承字体；空的 Section/ListItem 容器被清理，其他容器保持归属。
嵌套 Bold/Italic/Span 内换段保留结构和输入格式。LF/CRLF 多行粘贴创建真实段落，
CR 单独出现也统一为 LF；Hyperlink 内的换行转为空格，完整文档范围末尾的隐式换行
不额外创建段落。参考 [纯文本范围赋值的段落约定](https://source.dot.net/PresentationFramework/System/Windows/Documents/TextRangeBase.cs.html)。

TextRange 赋值、粘贴、IME 范围替换、Enter 和跨段删除使用同一编辑入口，形成一次
稳定内容通知和撤销单元。绑定 Run 的同节点替换只提交最终文字；输入结束后的
光标、空选区和最后一个字素同步，向右移动不再增加最终段落终止符的光标停靠点。
Enter 覆盖全选及初始空文档仍创建两个空段落，撤销恢复原来的节点和选区。

新增 **33 项**范围编辑检查，以相同测试源码加载未改的 v49 程序集与原生负载时，
**30 项行为失败、3 项对照通过**。最终托管 **802/802**、AppKit 宿主 **142/142**、
富文本/逻辑树兼容 **56/56**、Gallery **22/22** 通过。宿主新增已安装/私有字体的
多行替换和跨段替换；十项富文本案例在 1×/2× 各重复三轮原生 undo:/redo:，共
**120 次**逐字节字形和 **120 次**光标比较，并检查提交后的 AX 内容及隐藏缓存。
最终宿主运行被中断后，保留五组已通过记录，续跑其余六组；中断日志也保留。

**166 张**本进程 GPU 捕获导出 PNG，其中 148 张来自本轮宿主，18 张原生捕获保留
v48；**80 张**单次/分组/整 Run/多行/跨段撤销图像在十张联系表上按原始分辨率查看。
多行替换保持粗体斜体；跨段合并后的尾部保持大号斜体。修改前/撤销、修改后/重做
像素逐字节相同，最终捕获哈希与查看时一致。其余 86 张本轮未声明视觉重新验收。

**605 份**源码指纹、三份严格签名应用、48 份原生库副本、9 份资源及各自 SDK
链接程序集核验通过；已签名 v49 Gallery/Lab 保持原哈希与签名。v50 的原生
11 份文件和完成标记保留 v49/v47，491 份原生源码未改。原生八项结果仍沿用 v48，
本轮重新执行的前台 Window 检查另计：首次与宿主并行失败，独立复查同样失败，
**无边框最小化后仍然可见且处于 Maximized，独立复查 active=1**。当前最小化未通过，
这项真实失败优先修复，不能被保留的八项通过记录覆盖。

当前签名应用为：

- `artifacts/macos-window-gallery/Jalium.Window.Validation.v50.app`
- `artifacts/macos-window-lab/Jalium.Window.Lab.v50.app`

桌面访问本轮已恢复。实际 Window Lab 富文本全选后粘贴三行中文、希伯来文和
emoji，外部 AX 正确报告正文；⌘Z 恢复原文及原全选范围，⇧⌘Z 恢复三行内容。
CUA 返回的截图仅 90×148，且 AX 元素点击/滚动遇到 offscreen/坐标处无窗口错误；
因此不声明详细字形、光标、选区观感或 320/600 布局通过。完整桌面图像、字体切换、
真实 IME/Spaces/模态焦点/跨屏 DPI 与 VoiceOver 仍待验收。Table/锚定/嵌入内容范围
编辑、部分 Run 格式化、SetPlainText/AppendText、变化记录合并、输入合并、可变格式
对象、完整字体描述符、macOS 15/Intel、发行/裁剪/AOT 和 Window 以外行为继续开放。

此前 v49 的验证记录保留：
准备好的 Gallery 与 Window Lab 验证应用为 **v49**，记录位于
`artifacts/macos-window-verification/v49/validation-results.json`。
本轮修复完整 Run 替换后文字写入已脱离文档节点的问题：同 Run 的非空替换直接
修改原节点，保留字号、斜体、绑定和所属容器；删除后的光标重新解析到当前文档。
前向插入在相邻 Run 边界进入后一个 Run，也覆盖嵌套 Span。分组中替换为文字相同的
新节点时，选区和光标重新指向可见节点，Undo/Redo 仍保留对应节点归属。

富文本文字及已观察到的格式/节点变化在编辑结束后发送一次 TextChanged，包含
Create、Undo、Redo、Clear 或 None 动作；无内容变化不生成通知或舍弃 Redo。
分组、直接修改 Run、撤销/重做和 Document 赋值均有回归。普通 TextBox 的键盘、
粘贴和 IME 使用同一范围替换路径，内容监听器看到最终光标和选区；撤销/重做也
正确报告动作与选区。监听器再次编辑会形成独立撤销单元，抛异常后分组保持关闭。
参考 [内容与格式通知约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.primitives.textboxbase.textchanged)。

RichTextBox 新增 Text provider，提供正文、UTF-16 选区、只读状态及实际渲染范围。
内容通知通过现有 macOS 桥发出文本变化事件；AppKit 宿主在回调内读取原生 AX
文字和选区，确认与已提交文档一致。隐藏缓存节点不提供值、字符、选区能力或范围
文字，重新显示后恢复。范围滚动、点映射及完整文档格式属性仍待完善。

新增 **33 项**检查，相同源码加载未修改的 v48 测试程序集和冻结原生负载时
**31 项失败、2 项布局检查通过**，没有缺少库或入口点。最终托管 **769/769**、
AppKit 宿主 **138/138**、富文本/逻辑树兼容 **56/56**、Gallery **22/22** 通过。
新增已安装/私有字体的整 Run 宿主案例，在 1×/2× 各重复三轮原生 undo:/redo:，
增加 **24 次**逐字节字形和 **24 次**光标比较；保留的单次/分组案例另有 48 次各类
比较。初次两项隐藏检查把合法的原生 nil 误判成有值；修正断言后仅重跑字体套件，
最终 **22/22** 通过，其余十组成功记录保留，首次失败日志也保留。

**134 张**本进程 GPU 图像导出 PNG，其中 116 张来自本轮宿主、18 张原生捕获保留
v48。**48 张**单次/分组/整 Run 撤销图像在六张联系表上按原始分辨率查看，最终
导出哈希与查看时一致。字形保留粗体斜体；分组撤销恢复 20 号单行文字，重做恢复
24 号正体和换行。其余 86 张导出图像本轮没有声称重新进行视觉验收。

v49 的 **11 份**原生文件及完成标记逐字节保留 v48/v47，**491 份**原生源码未改。
原生八项检查沿用 v48 的已验证结果：初次七项通过，Metal smoke 超时后单独重试
通过，两次记录均保留；本轮没有重新执行原生套件。v38 探针的 13920 次同字体比较
也保留 v48 结果。**597 份**相关源码指纹、三份严格签名应用、48 份原生库副本、
9 份资源及各自 SDK 链接程序集校验通过，已签名 v48 Gallery/Lab 保持原哈希与签名。

最终签名应用为：

- `artifacts/macos-window-gallery/Jalium.Window.Validation.v49.app`
- `artifacts/macos-window-lab/Jalium.Window.Lab.v49.app`

Window Lab 保留中文样式、撤销和 320/600 宽度入口，字体参与日志采用独立 v49
路径。CUA 仍返回 Mac locked，实际窗口截图、按键、外部 AX、窄窗口布局和
VoiceOver 尚未验收。重叠变化记录、输入合并、开放分组中的绑定/原始 DP 设置约束、
可变格式对象、嵌套段落/部分 Run/跨段编辑、富文本换行粘贴、完整字体描述符、
真实 IME/Spaces/跨屏 DPI、macOS 15/Intel、发行/裁剪/AOT 与 Window 以外行为仍
开放，完整目标保持进行中。

此前 v48 的验证记录保留：
准备好的 Gallery 与 Window Lab 验证应用为 **v48**，记录位于
`artifacts/macos-window-verification/v48/validation-results.json`。
本轮将富文本嵌套编辑分组接入文档快照：一组文字替换、字号和斜体修改现在可以
由一次原生 Undo/Redo 恢复，选区以 `BeginChange` 时的位置为准。分组内直接修改
Run 文本和格式属性也能撤销，指定新 Document 会舍弃旧文档历史并从新文档开始
后续记录。参考 [编辑分组约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.primitives.textboxbase.beginchange?view=windowsdesktop-10.0)。

选区事件与普通 TextBox 的 TextChanged 事件延迟到最外层 EndChange，再交给监听器；
两次文本修改保留各自的变化记录。异常退出 DeclareChangeBlock 或最终事件监听器
抛异常后，分组仍正确关闭，后续编辑和撤销可继续。分组开放期间不消费 Undo/Redo。
富文本 Document 的完整 TextChanged 通知和重叠变化记录合并仍待补齐。

禁用撤销、改变历史上限的依赖属性回调会清空两份历史，重新启用后不再撤销旧操作。
`UndoLimit=-1` 保留所有历史，`0` 不产生新撤销记录，低于 `-1` 的值会拒绝。
CLR 设置器保护开放的撤销分组，项目既有默认上限 **100** 保留；绑定或直接
SetValue 在开放分组内改变设置的约束仍待完善。参考
[UndoLimit 约定](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.primitives.textboxbase.undolimit?view=windowsdesktop-10.0)。

新增 **35 项**检查，相同源码加载未修改的 v47 程序集与原生负载时 **28 项失败、
7 项通过**，没有缺少库或入口点。最终托管 **736/736**、AppKit 宿主 **136/136**、
富文本/逻辑树兼容 **56/56**、Gallery **22/22** 通过。新增两个 AppKit 宿主案例使用
已安装与私有 Helvetica，通过原生 `undo:` / `redo:` 在 1×/2× 各重复三轮，完成
新增 **24 次**逐字节字形比较和 **24 次** IME 光标比较；保留的单次编辑案例另有
相同数量的比较。

**118 张**本进程 GPU 捕获导出 PNG，**32 张**单次/分组撤销捕获在四张联系表上
以原始分辨率查看，撤销恢复斜体、字号与原有单行，重做恢复文字和新的换行。
其余 86 张导出图像本轮没有声称重新进行视觉验收。原生检查初次 **7 项通过、
Metal smoke 1 项超时**；仅重试失败项后在 10.453 秒通过，两次记录均保留，合计
八项通过。第九项前台窗口检查仍待完成。

v48 为托管修复，**11 份**冻结原生文件和完成标记逐字节保留 v47，**491 份**原生
源码没有变化。**593 份**相关源码指纹在构建和校验后稳定；三份应用严格签名、
48 份原生库副本、9 份资源及各自 SDK 链接程序集校验通过，已签名 v47 Gallery
与 Lab 保持原哈希和签名。v38 原探针仍有 **13920 次**同字体位置比较，零差异。

最终签名应用为：

- `artifacts/macos-window-gallery/Jalium.Window.Validation.v48.app`
- `artifacts/macos-window-lab/Jalium.Window.Lab.v48.app`

Window Lab 保留 v47 的中文字体、撤销和 320/600 宽度检查入口，日志采用独立 v48
路径。CUA 再次返回 Mac locked，v48 实际窗口截图、按键、外部 AX、窄窗口布局与
VoiceOver 尚未验收。原生负载与 v47 相同，其上次前台最小化失败仍保留待解锁
复查；本轮没有重复执行不可用桌面的前台检查。嵌套内联换行、部分 Run 格式化、
跨段编辑、内联方向、完整字体描述符与合成控制、真实 IME/Spaces/跨屏 DPI、
macOS 15/Intel、发行/裁剪/AOT 与 Window 以外行为仍开放，完整目标保持进行中。

此前 v47 的验证记录保留：
准备好的 Gallery 与 Window Lab 验证应用为 **v47**，记录位于
`artifacts/macos-window-verification/v47/validation-results.json`。
本轮修复两项通过窗口验证发现的问题：富文本 `Command-Z` 会把原有粗体、斜体和
字号变成纯文本；在 `SourceInitialized` 中最大化的窗口首次显示后，还原尺寸可能
从 320×240 变成 2316×1288。

富文本撤销/重做现在恢复原有文档节点、本地格式与继承值、绑定和选区指针，保留
外部持有的 Run、Hyperlink、列表、表格和嵌入内容引用。文本替换正确转移文档的
逻辑树与事件订阅；直接指定新 Document 会清除旧历史。段落换行保留格式与同级
顺序。若历史文档或文本节点已经交给其他编辑器，撤销会保留当前内容与历史，
不会修改或抢回其他编辑器的节点。

原生窗口首次 `Show` 期间，AppKit 对初始最大化框架的约束不再被当作用户调整
尺寸，原始正常尺寸仍可还原。新增同一原生约束探针，加载未修改的 v46 负载时
在“显示时保留最大化状态”断言失败，使用 v47 时通过；320×240 的实际 AppKit
宿主生命周期案例也在修复后通过。

新增 **23 项**富文本撤销检查，相同源码加载未修改的 v46 托管程序集与原生负载
时 **23 项全部失败**，没有缺少动态库或入口点的失败。最终托管 **701/701**、
AppKit 宿主 **134/134**、富文本/逻辑树兼容 **56/56**、Gallery **22/22** 通过。
覆盖已安装/私有字体与直立/斜体几何、格式继承、双向绑定、反向选区、段落顺序、
节点归属、文档复用和撤销历史限制。

两个新增 AppKit 字体宿主案例通过原生 responder 的 `undo:` / `redo:`，在
1×/2× 各重复三轮，完成 **24 次**逐字节字形像素比较与 **24 次** IME 光标几何
比较。最终运行与已查看的 **16 张**富文本 GPU 捕获一致，撤销等于替换前、重做
等于替换后。两张联系表保留原始像素，尺寸为 1520×304 和 3040×608。另有 64 张
已导出的保留捕获，本轮没有声称全部重新进行视觉验收。这些本进程 NSView GPU
回读不能代替外部桌面窗口截图。

独立原生检查 **8/8**，字体专项 **29934 个断言**、字宽 **6560 个断言**、字体
匹配 **14759 个断言**、**42100 次** NSTextView 导航对照与 400 次线程查询通过。
未重新编译的 v38 C++ 探针加载 v47 完成 **13920 次**同字体位置比较，零差异；
这不建立变更后的具体 Metal 类直接分配布局的二进制兼容性。

**592 份**相关源码指纹，含 **491 份**原生源码，在最终构建和校验后稳定。
三份应用严格签名、48 份原生库副本、9 份离线资源及各自 SDK 链接程序集校验
通过。原生库为独立新构建，三份离线资源逐字节保留 v46。临时诊断移除后，重建
原生库的可加载节与冻结负载一致；platform 库的链接/调试 UUID 导致整文件哈希
变化，验证记录保留两份哈希，没有将它们称为整文件一致。v46 冻结负载与已签名
Gallery、Lab 保持原哈希和签名。较早的 v47 Lab 已先归档，再替换为最终构建。

解锁后的 v46 桌面补验取得了实际截图、外部 AX 树和键盘操作，确认字体显示并
发现上述富文本撤销问题；v46 的前台窗口原生套件在当时 **1/1** 通过。补验记录
位于 `artifacts/macos-window-desktop/v46-20261007/`。随后 CUA 再次报告 Mac locked，
v47 的实际截图、按键和外部 AX 检查仍待完成。第九项原生前台检查在这次桌面
不可用期间于最小化断言失败，保留失败记录并待解锁复查，没有归入通过项。

最终签名应用为：

- `artifacts/macos-window-gallery/Jalium.Window.Validation.v47.app`
- `artifacts/macos-window-lab/Jalium.Window.Lab.v47.app`

Window Lab 保留既有中文界面，字体实验增加 `Command-Z` / `Command-Shift-Z`
说明和“窄窗口 320”/“标准窗口 600”按钮。仍需实际确认窄窗口布局、选词替换后
反复撤销/重做的粗体斜体、外部 AX 选区以及关闭子窗口后的主窗口焦点。
程序调整尺寸的按钮检查不等同于指针拖动调整尺寸验收。

嵌套内联换行、部分 Run 格式化、跨段编辑与撤销分组、更多字体描述符/倾斜角度/
合成控制、前台与 Spaces/跨屏 DPI/真实 IME、VoiceOver、macOS 15/Intel、发行/
裁剪/AOT 与 Window 以外行为仍开放，完整目标保持进行中。

此前 v46 的验证记录保留：
准备好的 Gallery 与 Window Lab 验证应用为 **v46**，记录位于
`artifacts/macos-window-verification/v46/validation-results.json`。
本轮修复默认和显式 100% 字宽下的字体族匹配：Helvetica Neue 的 900 字重此前
可能选择窄体 Black，现在先匹配标准字宽，再匹配样式与字重；已安装静态族与私有
TTC 使用同一顺序。缺失字重按 [CSS Fonts 匹配规则](https://www.w3.org/TR/css-fonts-4/#font-style-matching)
查找，覆盖 400–500 特殊区间及其两侧，例如 450 选择 Medium、501 选择 Bold。
此轮不声称完整 CSS Fonts 一致性；混合变量/静态族的描述符范围仍待检查。

公开控件、FormattedText、CSS 字体计划及段落进入新匹配路径。真实系统字体保留
AppKit 的字重、斜体与光学字号映射；显式 PostScript 字面名称及旧四参数原生工厂、
构造符号保留此前行为。扩展宽度入口在 Metal 的 100% 请求也执行匹配，其他后端
的标准宽度仍使用既有工厂。

新增 **16 项**托管检查，最终 **678/678**；相同测试源码加载未修改的 v45 程序集
与冻结原生负载时 **7 项失败、9 项通过**，失败涉及高字重标准字宽、501 的字重
查找、CSS/私有集合、三类编辑器光标及显式标准宽度工厂，没有缺少动态库或入口点。
新原生探针加载 v45 时在 Helvetica Neue 的字重 199、字宽 100% 上失败，使用 v46
时新增 **14759 个断言**全部通过：26 个字重边界、三种字宽、三种基本样式、两个
已安装族共 **468 组**，以及 **8 组**私有 Helvetica TTC 和注册释放后的字节保留。
参考使用显式具名字面，比较宽度、基线、每个 UTF-16 双向光标位置及 `ch`。

最终原生字体专项 **29934 个断言**，保留字宽 **6560 个断言**、160 组系统字宽/
字重/样式、32 组独立 CTLineDraw 段落像素比较。独立原生检查 **8/8**，仍有第九项
前台激活/全屏待验收。**42100 次** NSTextView 导航对照及 400 次线程查询通过；
未重新编译的 v38 C++ 探针加载 v46 有 **13920 次**同字体位置比较，零差异。

AppKit 宿主 **132/132**、富文本/逻辑树 **56/56**、Gallery **22/22**。新增宿主
检查将已安装/私有 Helvetica 的默认与 CSS 标准字宽绘制和显式字面逐字节比较，
覆盖 199/450/501/750/900 与三种基本样式；三类编辑器在窗口中反复切换默认、
75/100/125% 字宽、字重和样式，验证 IME 光标、Command-Right 与 Option-Shift 选区。

**86 张**本进程 NSView GPU 回读已导出 PNG，并以原始像素查看两张联系表
（1128×1680、2208×3120）。新增八张已安装/私有 Helvetica 标准字宽 900 的直立/
斜体捕获，1×/2× 字形与具名字面一致；其余 78 张字宽、字重、回退、加载和 NoWrap
捕获保留通过结果。这些回读不能代替实际桌面窗口截图。

**590 份**相关源码指纹，含 **491 份**原生源码，在最终构建与校验后稳定。三份
应用严格签名、48 份原生库副本、9 份离线资源及各自 SDK 链接程序集校验通过；
v45 冻结负载、已签名 Gallery 与 Lab 保持哈希和签名不变。Window Lab 增加中文
标准字宽 Helvetica 900/450/501 与私有集合按钮，日志使用独立 v46 路径。

CUA 打开签名 v46 仍返回 Mac locked，实际窗口截图、实机按键/指针和外部 AX 树
未取得，窄窗口布局与 VoiceOver 尚未验收。合成控制、完整倾斜角度、更多描述符/
语言、AAT 相对轴和压缩集合、内联方向、富格式撤销、前台/Spaces/跨屏 DPI/真实
IME、macOS 15/Intel、发行/裁剪/AOT 与 Window 以外行为仍开放，完整目标保持进行中。

此前 v45 的验证记录保留：
准备好的 Gallery 与 Window Lab 验证应用为 **v45**，记录位于
`artifacts/macos-window-verification/v45/validation-results.json`。
本轮将 `FontStretch` 与 CSS `font-width`/`font-stretch` 的精确百分比接入测量、绘制、
段落、光标与选区。非 100% 请求的已安装族及私有集合按字宽、样式、字重依次选择；变量字体使用
OpenType `wdth` 轴并保留现有字重和光学字号。字体没有其他字宽时保留实际字面。
百分比方向和描述符范围按 [CSS Fonts 字面匹配算法](https://www.w3.org/TR/css-fonts-4/#font-style-matching)
处理，超过 100% 的极大值也保留优先方向。

精确百分比进入内部字体标识，区分既有测量与绘制缓存。83.2% 和 92.8% 等同一
FontStretch 类别内的变化仍清除内联、富文本与编辑器几何，并重新计算继承后的
CSS 字体单位。Metal 提供字面实际 x/cap 高度、下划线和 `0`/`水` 的 advance；
后两项沿用同一受限有序回退，`ch` 尺寸随实际字宽更新。

新增 C 入口 `jalium_text_format_create_with_width` 与可选 `TextFormatWidthFactory`；
既有抽象 Backend/TextFormat 接口及四参数构造符号保留。具体 Metal 类增加接口基类，
此处不声称直接分配旧具体类对象的二进制布局兼容。100% 的原生字体创建保留此前
选择路径，完整 CSS 字面匹配及合成控制仍开放。

SF 的可变字宽字面缺少对应斜体时，保留所选字宽并合成 12 度倾斜。宿主像素检查
发现冻结 v44 的绘制丢掉 run 的字体矩阵：几何检查通过，实际字形仍直立。v45 的
普通文本与富文本逐 run 绘制安装该矩阵和行原点，并恢复绘图上下文。相同的新原生
探针使用 v44 dylib 时像素断言失败，使用 v45 时 **32 组**段落像素与独立 CTLineDraw
逐字节相同，覆盖 1×/2×、直立/倾斜及仿射变换。v44 冻结负载与已签名 Lab 保留不变。

新增 **20 项**托管行为检查，合计 **662/662**；相同测试源码在未改动的 v43 程序集
与原生负载上 **20 项全部失败**，没有缺少动态库或入口点的失败。检查覆盖公开字宽、
静态窄体、CSS 优先方向/范围/描述符顺序、继承与内联、同类别精确值、字体单位和
三类编辑器 IME 几何。既有富文本/逻辑树 **56/56**，Gallery **22/22**，最终 AppKit
宿主 **130/130**。宿主还检查窗口中反复改变继承的 `10ch`、Option 按词移动、
系统/私有 SF 字宽变化及 FormattedText 与原生格式的像素一致性。

最终原生字体专项 **15175 个断言**，其中字宽 **6560 个断言**，含 **160 组**系统
字宽/字重/样式配置、字体单位、静态族、私有变量/集合与修改 advance 后的私有字节
保留。8 项独立原生检查通过，需要前台激活/全屏的第九项仍待验收。**42100 次**
NSTextView 导航对照和 400 次线程查询通过；未重新编译的 v38 C++ 探针使用 v45
dylib 有 **13920 次**同字体位置比较，零差异。

**78 张**本进程 NSView GPU 回读导出 PNG，并以原始像素查看两张联系表
（1128×1456、2208×2704）。新增 16 张字宽捕获：真实窄体及系统/私有变量字体的
75/100/125% 具有不同轮廓，倾斜字形可见；既有字重、回退、局部加载与 NoWrap
捕获保留对应行为。这些回读不能代替实际桌面窗口截图。

**588 份**相关源码指纹，含 **490 份**原生源码，在最终构建与验收后稳定。
Gallery、Host、Lab 最终构建均零错误；三个离线资源按清单保留 v44 字节。三份应用
严格签名、48 份原生动态库副本、9 份资源与各自 SDK 链接程序集通过打包校验；
此前 v43 Gallery 和 v44 Lab 的签名及负载哈希保持不变。

Window Lab 的“字体回退验证”增加 75/100/125% 与“SF 精确字宽 90.25%”按钮，
沿用既有中文风格、44 点最小高度与流式布局；事件日志改用独立 v45 路径。
CUA 打开最终签名 v45 仍返回 Mac locked，实际截图、实机按键/指针和外部 AX 树
未取得，窄窗口布局与 VoiceOver 尚未验收。完整 CSS 100% 字面匹配、合成控制、
更多字体描述符/语言、AAT 相对字宽与压缩集合、内联方向、富格式撤销、前台/Spaces/
跨屏 DPI/真实 IME、macOS 15/Intel、发行/裁剪/AOT 与 Window 以外行为继续开放，
完整目标保持进行中。

此前 v43 的验证记录保留：
最新准备好的 Gallery 与 Window Lab 验证应用为 **v43**，记录位于
`artifacts/macos-window-verification/v43/validation-results.json`。
本轮将有序逐字形回退接入 CSS。字体列表保留后续可用字体，带逗号的引号名称不再被拆开；
先按字面描述符匹配，再处理同字面的 Unicode 子集。声明的字符范围与实际 cmap 取交集，
声明的字体族遮蔽同名已安装族，已加载但缺少字形时继续查找后续族。

每个字形簇只启动首次需要的字体加载。等待期间的本地回退不会触发后续 webfont 下载；
占位文字与标题在实际测量、绘制时重新解析，避免仅根据控件 Text 属性决定下载。
按字形的 `font-display` 阻塞只隐藏等待中的字形，保留它们的回退宽度、光标和选区。
组合字符按 [CSS Fonts 当前工作草案的簇匹配规则](https://www.w3.org/TR/css-fonts-4/#cluster-matching)
选择等价的预组合字形；原始 UTF-16 文字和插入位置保留。此次直接验证 block 与 swap；
optional/fallback 既有时间策略保留，其他字体描述符与完整 CSS Fonts 一致性仍开放。

新增三个 C 入口：字符范围设置、直接 cmap 覆盖查询、逐字形显示状态设置，透过可选
`TextFontCharacterProvider` 接口接入。既有抽象 TextFormat/Backend 接口及段落构造符号
保留；具体 MetalTextFormat 增加接口基类。原始字体引用由格式保存，等价组合字符的
测量、段落与绘制使用同一字面；字体引用和私有资源具有各自的生命周期。

新增 **13 项**托管行为检查，合计 **642/642**。在未改动的 v42 程序集和原生负载上，
相应行为检查为 **12 项失败、1 项通过**；为兼容旧程序集，基线副本省略三个新计划元数据
断言，下载与几何断言保留，没有缺少动态库或入口点的失败。既有富文本/逻辑树 **56/56**，
Gallery **22/22**，最终 AppKit 宿主 **128/128**。新增宿主验证局部阻塞的段落像素、
FormattedText 可见文字的字形与基线，以及普通/富文本编辑器在子集到达前后的公开 IME 光标。

原生字体专项 **8615 个断言**：字符范围交集、不把系统回退计为直接 cmap 支持、局部和
等价组合字符的阻塞/加载、范围重叠、重置及 ABI 参数边界通过；保留 54 组有序回退和
951 组数值字重配置。最终独立原生 **8/8** 一次运行通过，需要前台激活/全屏的第九项仍待验收。
**42100 次** NSTextView 导航对照、400 次线程查询通过；未重新编译的 v38 C++ 探针使用
v43 dylib 有 **13920 次**同字体位置比较，零差异。最初的组合字符失败记录与 CoreText
描述符探针保留；修复使用直接保存的原字体，避免依赖被 CoreText 丢弃的自定义属性。

**62 张**本进程 NSView GPU 回读导出 PNG，并以原始像素查看两张联系表
（1128×1232、2208×2288）。局部等待只留下拉丁文字且位置保持，加载后希伯来字形与指定
参考字体一致；组合重音与原字体像素一致，其等待捕获为空白。既有字重、私有字形和 NoWrap
捕获保留对应行为。这些回读不能代替实际桌面窗口截图。

**579 份**相关源码指纹，含 **489 份**原生源码，在最终构建前后稳定。Gallery、Host、Lab
最终增量构建均零错误；三个离线资源按清单保留 v42 字节。三份应用的严格签名、48 份原生
动态库副本、9 份资源和各自 SDK 链接程序集由打包脚本校验；签名及冻结负载不代表发行或 AOT 验收。

Window Lab 的“字体回退验证”新增“CSS Arial 回退”“CSS Peninim 回退”和“局部字体加载”。
最后一个按钮每次以新的本地测试资源延迟 1.5 秒，让普通文本与富文本可比较加载前后字形、
光标及选区；私有变量与集合字体示例声明 100–900 字重范围。新增按钮的源代码沿用既有中文
控件风格、44 点最小高度、流式布局及焦点反馈；实际窄窗口布局与 VoiceOver 尚未验收。

CUA 打开最终签名 v43 仍返回 Mac locked，实际截图、实机按键/指针和外部 AX 树未取得。
扩展字体描述符、合成样式、拉伸/语言、AAT/压缩集合、内联方向与富格式撤销恢复、更多原生
编辑命令、前台/Spaces/跨屏 DPI/真实 IME/VoiceOver、macOS 15/Intel、发行/裁剪/AOT 与
Window 以外行为继续开放；完整目标保持进行中。

此前 v42 的验证记录保留：
最新准备好的 Gallery 与 Window Lab 验证应用为 **v42**，记录位于
`artifacts/macos-window-verification/v42/validation-results.json`。
本轮补齐直接 `FontFamily.Source` 列表的逐字形有序回退。此前首个可用字体缺少字形时，
CoreText 会采用系统默认回退，忽略列表中作者指定的后续字体。现在可用的后续字体构成
有序 CoreText cascade；测量、绘制、段落、光标与选区使用同一字体对象，切换顺序清除旧几何。

新增 C 入口 `jalium_text_format_set_font_fallbacks` 与可选 `TextFontFallbackProvider`；
既有 TextFormat、段落及 Backend 虚表保持不变，具体 MetalTextFormat 增加接口基类。
每个后续字体保留匹配的字面及私有资源引用；释放临时格式或注册句柄后仍可绘制，
清空回退恢复原字体。私有 Arial Hebrew TTC 的字宽被单独修改后，用于验证确实采用
这份数据，已安装字面不受影响；段落在别名退出目录后仍保留原字形。

新增 **27 项**托管检查，合计 **629/629**；相同新增用例在未改动的 v41 程序集与
原生负载上为 **26 项行为断言失败、1 项通过**，没有动态库或入口点缺失导致的失败。
检查包含两种回退顺序、字重与样式、组合符号、Emoji、系统回退、缓存与三类编辑器
公开 IME 光标。富文本兼容检查 **56/56**，Gallery **22/22**，AppKit 宿主 **126/126**。
宿主通过本进程 NSEvent 检查切换顺序后的 Command/Option 选择与光标。

原生字体专项 **8421 个断言**，新增 **54 组**字号/字重/样式/顺序配置，对照显式
脚本文字字体段落和公共 AppKit 字宽；保留 951 组数值字重检查。独立原生检查 **8/8**：
首次运行七项通过，Metal smoke 在并行构建期间 30 秒超时；构建结束后的单项复查通过。
初始失败 XML、日志及复查记录都保留，最终 XML 标明每项来自哪次执行。需要前台
激活/全屏的第九项仍未验收。既有 **42100 次** NSTextView 导航对照及 400 次线程查询
通过；未重新编译的 v38 C++ 探针在 v42 dylib 下有 **13920 次**同字体位置对照，零差异。

1×/2× 混合文字 GPU 像素等于指定脚本字体参考；FormattedText 的单脚本文字比较采用
首选字体的相同行框。最初把它与后续字体自身行框比较的失败记录保留，修正参考后
最终宿主全部重跑通过。**48 张**原始回读导出 PNG，并以原始像素查看两张完整联系表
（1128×896、2208×1664）；两种希伯来字体的字形和宽度不同，修改后的私有数据有更宽
字距，既有字重、字体加载/阻塞和 NoWrap 捕获保持对应行为。这些是本进程 NSView
GPU 像素，实际桌面窗口截图仍待取得。

**575 份**相关源码指纹（含 488 份原生源码）在最终构建前后稳定。三份应用的严格
签名、**48 份**动态库副本、**9 份**资源及各自 SDK 链接后的宿主程序集已核对；Gallery
四份自有程序集等于各自链接输出。原生负载使用独立目录，三个离线资源按清单保留 v41
字节；已签名 v41 Gallery 和冻结负载也保持不变。最终 Gallery、Host、Lab 构建分别为
18、5、0 条警告，均为零错误；这些构建不构成发布裁剪或 AOT 验收。

Window Lab 的“字体回退验证”提供“Arial 优先回退”和“Peninim 优先回退”，使用直接
FontFamily 列表，可比较普通文本与富文本中的 אבג，配合字重/样式及 Option/Shift 选择。
CSS computed family 列表目前仍在首个可用本地/已加载字体后截断，按字形的 webfont
加载、unicode-range 和 font-display 阻塞尚未补齐；本轮不把这些行为计为通过。
CUA 打开已签名 v42 再次返回 Mac locked，未取得实际截图、前台按键/指针和外部 AX 树。
这条工具结果没有对应锁屏截图，用户的解锁回复也未被当作当前桌面验收证据。
扩展私有字体描述符、AAT/压缩集合、字体拉伸与语言、内联方向、富格式撤销恢复、
更多系统命令、前台/Spaces/跨屏 DPI/真实 IME/VoiceOver、macOS 15/Intel 及 Window
以外行为继续开放；完整目标保持进行中。

此前 v41 的验证记录保留：
Gallery 与 Window Lab 验证应用为 **v41**，记录位于
`artifacts/macos-window-verification/v41/validation-results.json`。
本轮补齐编辑器的数值字重。此前只在字重达到 600 时附加 Bold 特征，100、500、900
不能分别取得细体、中等和黑体。系统 UI 字体现在按 AppKit 的数值权重创建；具名字体
按 CoreText 家族及数值特征选择字面，斜体转换使用 AppKit，400 保留显式指定的字面。
系统字体的中间权重选择也与公共 AppKit 接口对照，没有假定每个请求都产生一个唯一字面。

私有 OpenType 变量字体按 `wght` 的实际范围设置数值字重，保留已有变体设置；私有 TTC
从原始数据的描述符中选择同族字面。macOS 字体准备保留原始名字和完整 TTC，避免大 name
表及集合重命名失败；指定 PostScript 片段仍能选取单个字面。字面没有全局注册。
测试同时修改同名字体数据的字宽，确认私有数据被使用、已安装字面不受影响、调用方输入未被改写。
旧式 AAT 变量字体使用具名实例，不将它等同于完整可变轴及全部压缩集合的行为验收。

EditControl 查询 IME 光标前更新字体布局，字重及样式变化同时清除缓存行宽。
RichTextBox 继承字重时可能没有 Run 内容变化通知，旧段落仍以 400 排版，实际窗口的
100 字重光标因此偏离约 4.92 DIP。现在重用原生段落前同时比较字体、字号、字重和样式；
继承变化不再保留旧字形几何。新增检查覆盖 27 组继承字重/样式及字号变化，且不依赖再次 Arrange。

新增 **45 项**托管检查，合计 **602/602**；同一组检查在未改动的 v40 程序集及原生
负载上为 **37 项行为断言失败、8 项通过**，没有缺失动态库或入口点造成的失败。
既有富文本、选词与逻辑树 **56/56**，Gallery **22/22**。最终宿主重建后全部套件重跑，
AppKit **124/124**，其中三类编辑器通过本进程 NSEvent 的 Command/Option 选择与实时字重
IME 几何检查。这些事件不构成物理键盘验收。

独立原生 CTest **8/8**，字体专项 **5491 个断言**，包括 **951 组**权重配置：系统字体
567、具名字体 315、私有集合 27、私有变量字体 42。请求覆盖 1–1000 的边界、中间值及
12/20/32 的系统字号，参考为公共 AppKit、CoreText 或从相同原始数据创建的 CGFont 实例。
仍保留 **42100 次** NSTextView 字词导航对照及 400 次线程查询；未重新编译的 v38 C++
探针在 v41 dylib 下有 **13920 次**同字体位置对照、零差异。需要前台激活/全屏的第九项
CTest 继续待验收。重负载下首次 Metal smoke 超时的记录保留，单项重试及最终八项重跑均通过。

1×/2× GPU 检查确认 Avenir 具名与私有集合的各字重/样式像素等于指定字面；系统与私有
变量字体有九组不同的字重像素。保留字体到达、阻塞/加载和 NoWrap 对照。**38 张**原始
回读均导出 PNG，并重建、查看两张包含全部捕获的联系表（1128×784 与 2208×1456）。
细体、中等、黑体有可见差异，阻塞 tile 无字形，NoWrap 尾部裁剪符合检查。这些是本进程
NSView 的实际 GPU 像素，不是桌面窗口截图。

**573 份**源码指纹（含 487 份原生源码）在最终构建前后稳定。三份应用的严格签名、
**48 份**动态库副本、**9 份**资源及各自 SDK 链接后的宿主程序集已核对；Gallery 的四份
自有程序集与其 SDK 链接输出一致。原生负载使用独立输出，三个离线资源按清单保持 v40
字节不变；已签名 v40 Gallery 及冻结负载也保持不变。Gallery、Host、Lab 构建分别为
18、15、0 条警告，均为零错误；链接分析与既有警告保留，不据此声称发布裁剪或 AOT 验收。

CUA 打开已签名 v41 时仍返回 Mac locked，未取得本轮实际桌面截图、物理按键/指针和
外部无障碍树。这是工具返回结果，未另外取得锁屏画面；用户先前的解锁回复未被当作当前
桌面检查证据。Window Lab 的“字体回退验证”增加九档字重、普通/斜体/倾斜、Avenir、
私有 SFNS 变量字体及私有 Avenir 集合入口。逐字形有序回退、扩展字体描述符及 AAT/
压缩集合、字体拉伸与语言、内联方向、富格式撤销恢复、更多系统命令、前台/Spaces/
跨屏 DPI/真实 IME/VoiceOver、macOS 15/Intel 及 Window 以外行为继续开放；完整目标保持进行中。

此前 v40 的验证记录保留：
Gallery 与 Window Lab 验证应用为 **v40**，记录位于
`artifacts/macos-window-verification/v40/validation-results.json`。
本轮补齐编辑器的 macOS 字体解析。CoreText 会静默替代不存在的字体；原先字体列表的
第一项因此被误判为有效，富文本段落还把整份列表作为单个字体名。现在段落、测量、
绘制和按词几何共用解析器，按顺序跳过缺失字体，支持带逗号及转义引号的字体名。
例如 `MissingWindowFont40, Menlo` 的样本文字应为约 313 DIP，旧实现被替代字体测成 240 DIP。
全部缺失时使用系统 UI 字体。默认 `SF Pro` 与 `system-ui` 通过 CoreText 的系统字体接口
创建，macOS CSS 的等宽通用族映射至 Menlo；已安装字体及字体选择器读取 CoreText 字体目录。

进程私有字体现在验证字体数据，并通过 CGFont 创建 CTFont，Metal 实际使用注册数据，
不会把私有别名画成 Helvetica。存活的格式保留资源，段落保留 CTFont；释放原资源与
临时格式后仍可查询、选择和绘制。没有进行系统字体安装或全局字体注册。
新增字体目录与可用性 C API，没有改变已有后端虚表或段落构造函数符号。
字体缓存代次同时影响按词段落、代码编辑器几何、富文本布局和绘制格式，保持同一
文本、字号与字体列表时的字体加载也会刷新光标和字形。

FlowDocument 接入 RichTextBox 的逻辑树，替换时移除旧文档，拒绝夺取其他宿主的文档。
Run 可以读取宿主作用域的 `@font-face`，加载时按 `font-display` 决定回退字形是否可见，
加载完成后使用私有别名并重建段落。已移除的文档不继续读取旧宿主的字体规则。

新增 30 项托管检查，最终 **557/557**；可在 v39 编译的 28 项新增行为检查中
**23 项失败、5 项通过**，两项新增目录 API 检查未用于旧负载基线。
另执行既有富文本、选词与逻辑树检查 **56/56**。AppKit 宿主新增 5 项，合计
**121/121**；最终宿主重建后再次运行这 5 项。Gallery **22/22**、独立原生 CTest
**8/8**，其中字体专项 **731 个断言**通过；需要前台会话的激活/全屏第九项仍待验收。
原生保留 **42100 次** NSTextView 对照与 400 次线程查询；未重新编译的 v38 C++ 探针
在 v40 dylib 下仍有 **13920 次**同字体位置对照、零差异。

1×/2× 的本进程 NSView GPU 回读验证：带缺失字体的列表与 Menlo 像素一致；注册
Andale Mono 数据的格式和段落与已安装的同一字体像素一致；字体到达后绘制缓存改变；
富文本阻塞期间零字形，加载后与相同字体的段落像素一致。16 张回读 PNG 已导出，查看了
1× 字体列表、1× 阻塞、2× 原生私有字体及 2× 加载后富文本图。它们不是桌面窗口截图。

570 份源码指纹包含 487 份原生源码，在最终构建前后稳定。v40 新建独立的原生输出和
冻结负载，保留 v39 冻结负载与已签名 Gallery；三份应用的严格签名、48 份动态库副本、
9 份资源和各自 SDK 链接后的宿主程序集已核对。最终 Gallery、Host、Lab 构建分别为
18、15、0 条警告，均为零错误；Host 新增两条诊断反射的链接分析警告，已有三条
NoWrap 宿主警告及既有项目警告仍保留。没有据此声称发布裁剪或 AOT 已验收。

CUA 打开 v40 时仍返回 Mac locked，未取得本轮实际桌面截图、物理按键和外部无障碍树。
这是工具返回结果，并非另行观察到锁屏画面。Window Lab 增加“字体回退验证”，可切换
系统字体、等宽字体、缺失首项、带引号列表和私有字体。私有系统字体完整描述符/权重与
可变轴、逐字形有序回退、内联方向、语言、富格式恢复、更多系统编辑命令、macOS 15/Intel
及真实桌面验收继续开放；完整 macOS 目标保持进行中。

此前 v39 的验证记录保留：
Gallery 与 Window Lab 验证应用为 **v39**，记录位于
`artifacts/macos-window-verification/v39/validation-results.json`。
本轮修复真正的 NoWrap：15022 个 UTF-16 单元、Helvetica 20 的测试行宽约 216792 DIP，
NSTextView 保持一行，旧导航的 100000 宽度变成 3 行，旧绘制的 10000 宽度变成 22 行。
现在测量、绘制、视觉行边界、按词导航、光标、命中与 IME 几何均使用明确的不换行模式，
而非增加有限宽度。换行配置进入各层缓存键，清理缓存后的重试仍保持同一模式。
双精度最大宽高转换为 float 时的无穷值也经过规范化；无效 NaN 几何请求继续拒绝。

新增可选 `TextParagraphWrappingProvider` 和
`jalium_text_paragraph_create_with_wrapping`，保留原段落创建 API、虚表和 C++ 构造函数符号。
父级 Window 或 Panel 显式声明的 LTR 现在覆盖自然 RTL；方向修改、清除以及更近的
文档/段落覆盖会重建布局。代码编辑器的字宽、光标和字体度量使用实际粗细及样式。

新增 16 项托管回归，v38 程序集与冻结负载中 **13 项行为失败、3 项通过**；
最终托管 **527/527**。覆盖不受限宽度、最大双精度和默认约束、换行缓存隔离、缓存清理、
整行位置/范围/命中、Command/Shift 行尾和滚动、代码字体、硬换行与完整 Emoji、
Window/Panel 方向继承及修改/清除和近层覆盖。初次测试宿主编译修复单独保存。
原生对照保留此前 42004 次检查及 400 次线程查询，新增 **96 次** 超过 100000 DIP 的
LTR/RTL 长行对照。未重新编译的 v38 C++ 探针在 v39 dylib 下仍通过 **13920 次**
同字体位置对照，验证旧构造函数及导航路径兼容；不据此声称所有平台 ABI 已验收。

真实本进程 AppKit 检查 **116/116**、Gallery **22/22**、独立原生 CTest **7/7**
通过；完整激活/全屏的第八项仍待桌面会话。按词宿主从 15 项扩为 20 项，增加两类
纯文本编辑器的极长行 Command/Option/Shift、父级 LTR/清除和绘制回读。
在本进程未显示的 NSView Metal 表面上，长行末尾的托管不换行绘制在 **1×/2×** 与
原生同字体绘制逐字节一致；混入换行绘制不污染缓存，1.25 倍缩放后也保留末尾文字。
回读解码为 PNG，已查看 1× 常规与 2× 缩放图像；这是自有表面的 GPU 证据，不能替代桌面截图。

Gallery、宿主、Lab 构建均为 0 错误，分别 18、13、0 个警告；宿主新增反射检查产生
额外链接分析警告，实际签名宿主检查通过。冻结 v39 的 8 个 dylib 来自独立构建，
3 个离线 Metal 资源保留 v38 字节；567 个相关源码指纹（486 个原生）在最终构建前后
保持一致。3 个应用的严格签名、48 份原生库、9 份资源及各自 SDK linked 程序集已核验，
v38 签名 Gallery 与冻结负载保持不变。

Window Lab 延续现有中文界面，增加“极长不换行”和“继承从左至右”练习。
尝试实际打开 v39 时 CUA 仍返回锁定错误，未取得本轮 Gallery/Lab 桌面截图、物理按键
或外部无障碍树。这是工具返回结果，并非另行观察到了锁屏画面。内联方向、语言、
私有字体描述符、富格式恢复、其他系统编辑命令以及真实桌面验收继续开放，完整目标尚未完成。

此前 v38 的验证记录保留：
Gallery 与 Window Lab 验证应用为 **v38**，记录位于
`artifacts/macos-window-verification/v38/validation-results.json`。
本轮修复换行双向文本中的按词导航：例如 Helvetica 20、宽度 130 的
`abc אבגדה 123 xyz العربية 中文 words tail`，在偏移 5 按 Option-Left 应到 4；
原先使用不换行的默认上下文会到 3。方向或字号改变排版后也需要重新解析。

新增可选段落导航能力与 `jalium_text_paragraph_navigate_word`，保留已有段落虚表。
AppKit 的 [NSTextSelectionDataSource](https://developer.apple.com/documentation/appkit/nstextselectiondatasource)
使用 CoreText 实际行范围、整字素光标边和段落方向，语言分词继续由系统提供。
RichTextBox 直接传入保留的样式段落与全文偏移；TextBox 和 EditControl 缓存当前字体与
换行宽度对应的 CoreText 段落。返回的亲和性用于光标绘制及 IME 几何；代码编辑器的
RTL 首位光标不再错误地固定在 x=0。声明的 LTR、RTL 和文档方向会同步排版与导航，
修改方向会释放旧布局；未声明方向的 macOS 富段落继续采用自然方向。

新增 27 项托管专项检查，v37 负载中 23 项失败、4 项较宽自然方向样本通过；最终
托管回归 **511/511**。初次基线宿主缺少 System.IO.Packaging 的失败单独保存，未计作
行为回归。原生 Metal 测试集成 **42,004** 次同字体 NSTextView 对照和 **400** 次线程
查询，覆盖四个词方向、亲和性、Shift 扩展、已有选区、三种段落方向、四种宽度、
混合字号、CRLF、空段落、最终分隔符、不同方向段落及过期文本拒绝。
独立探针的 96 种配置、**13,920** 次同字体位置对照全部一致。早期仅用私有字体名字
映射 boldSystemFont 的探针仍有 24 处换行差异；使用相同字体对象后为 0，因此不据此
声称私有系统字体描述符已完整等价。

真实本进程 NSView 的按词检查从 9 项扩为 15 项，新增换行、方向、混合字号与尺寸
变化后的 Option/Shift 按键事件；完整宿主 **111/111**、Gallery **22/22**、独立原生
CTest **7/7** 已重跑。完整激活/全屏的第八项继续等待桌面会话。
Gallery、宿主、Lab 构建均为 0 错误，分别为 18、10、0 个警告。
冻结 v38 的 8 个 dylib 来自独立构建，3 个离线 Metal 资源保留 v37 字节；核对了
486 个原生、566 个全部相关源文件指纹，3 个签名应用中的 48 份原生库和 9 份资源。
Gallery 的四个程序集分别匹配其 SDK linked 输出；v37 签名应用与冻结负载保持不变。

Window Lab 的“按词与选词验证”加入双向换行样本、自动/LTR/RTL 方向和混合字号。
实际打开 v38 时 CUA 仍报告 Mac 锁定，未取得本轮窗口截图、物理按键和外部无障碍树。
本进程事件与原生对照通过不能替代这些验收。纯文本的极长无换行行（当前导航宽度
上限 100000）及父级 LTR 继承在 v39 修复；内联方向、语言、格式撤销恢复和系统编辑命令继续开放。

此前 v37 的验证记录保留：
`artifacts/macos-window-verification/v37/validation-results.json`。
该轮将 TextBox、EditControl 和 RichTextBox 的按词移动与删除接入 AppKit。Option
左右使用物理方向，Shift 保留锚点；已有选区先按系统方式选择边缘，再到词边界。
按词删除使用逻辑前后方向，继续遵守只读与撤销约束。密码框保留安全的字段边界。

TextKit 的按键词边界与鼠标双击词范围在日文、泰文和 Emoji 上可能不同，因此分别
使用系统对应的能力：按键通过 NSTextSelectionNavigation，双击和拖选通过
NSAttributedString 的鼠标词范围。参考 Apple 的
[选区导航](https://developer.apple.com/documentation/appkit/nstextlayoutmanager/textselectionnavigation)
与[双击词范围](https://developer.apple.com/documentation/foundation/nsattributedstring/doubleclick%28at%3A%29)。
UTF-16 桥接保留内嵌 NUL 和完整字素；每个线程仅缓存一份不超过 65536 字符的当前
段落上下文，较大文档查询后释放。换入不同文本会释放旧上下文。

修复前，三类编辑器的中文、日文、泰文、缩合词、Emoji 与 RTL 导航 **36/36** 失败。
新增 101 项检查后，最终托管回归 **484/484** 通过，包含已有选区、只读删除、撤销、
鼠标词范围、文档替换及 11264 字符文档中的 256 次按词移动（5 秒内）。原生查询与
未显示的本进程 NSTextView 对照 **924** 项通过，另有 **200** 次线程查询；包含四个
方向、选区边缘、双击范围、硬换行、空白/标点、字素与参数检查。

首次真实 NSView 测试为 **6/9**：更换富文档后，旧选区锚点可能留在原文档；普通
文本和代码编辑器双击字形后半部时，最近的光标边界会误选下一词。已在文档替换、
显式选区、清除、删除和 Undo 时重置富文本锚点；鼠标选词通过实际字形范围判断
指针下的字符。修复后 **9/9**，并检查第二次按下保持期间拖到后续词再释放。
完整 AppKit 宿主 **105/105**、Gallery 相关回归 **22/22**、独立原生 CTest **7/7**
均已重跑。完整激活与全屏的第八项仍待桌面会话恢复。

Gallery、宿主和 Lab 的最终构建均为 0 错误，分别为 18、0、0 个警告；宿主最后一次
为扩充拖选用例后的增量构建。核验 561 个 Window 相关源码指纹，其中冻结原生来源
483 个；八个 dylib 重新构建，三个离线 Metal 资源保留 v36 字节。非 Apple 公共
回退定义通过本机编译检查；最初全局移除 __APPLE__ 导致 Darwin C++ 头配置失败的
检查脚本已修正，这不是 Windows/Linux SDK 构建或运行验收。

Window Lab 新增“按词与选词验证”，普通文本与富文本分别提供中文、日文、泰文、
英文/Emoji 和双向示例，沿用既有配色、字号、中文提示及窄窗口滚动容器。当前原生
词导航使用默认 TextKit 逻辑段落的大宽度上下文；复杂换行、显式段落方向和更广的
样式/语言配置尚未完成全部 Option 视觉导航对照。绘制、Command 视觉行导航、光标
与 IME 几何仍使用各编辑器已有的实际排版。

本轮 CUA 再次报告 Mac 锁定，不能确认 v37 应用已在桌面打开。实际窗口截图、
物理键鼠、外部 AX、中文候选窗、系统菜单、Dock 和 VoiceOver 仍待验收；本进程
NSView 事件检查不能替代这些项目。完整 macOS 目标保持进行中。

此前准备好的 Gallery 与 Window Lab 验证应用 **v36** 的记录位于
`artifacts/macos-window-verification/v36/validation-results.json`。
本轮补齐 RichTextBox 的长 Run 软换行：CoreText 一次排版完整的样式段落，保留每行的
UTF-16 范围、样式片段、实际字形边缘及基线，绘制时复用原来的字形 Run。混合字号、
字体与颜色跨行后继续保留；普通字形和彩色 Emoji 都应用画刷透明度。完整段落不再
为每一行重复测量或重新塑形，录制的绘制命令仅保存该行文本及原生布局引用。

绘制、鼠标命中、上下移动、Command+左右及 Shift 组合、选区和已提交文本的 IME
几何使用同一份行布局。双向选区按实际字形区间绘制，候选范围矩形使用这些区间的
并集；大字号行的光标高度与候选矩形一致。显式零长度 IME 选区采用前向光标，
软换行行末仍能通过 TextPointer 的方向停在上一行。宽度、字体、颜色与画刷透明度
变化会释放旧布局；SafeHandle 与后端资源租约确保字体和上下文释放后的原生查询安全。
原生段落与绘制能力使用新增侧接口，既有 TextFormat 和 RenderTarget 虚表保持不变；
旧库或未支持的后端回退至原有路径。本轮 CoreText 段落行为适用于 macOS Metal。

修复前，英文、中文、Emoji 与希伯来文四项长 Run 用例均失败。新增 21 项段落检查后，
最终托管回归重建并使用冻结的 v36 原生库运行，**383/383** 通过。包含完整字素、双向
光标与选区、命中、硬换行、末尾空行、字号混合、颜色/透明度与宽度变化、布局资源寿命
及长段落性能。真实 AppKit 宿主 **96/96**，新增长单 Run 的 NSView 按下/释放导航检查；
宿主禁止前台激活。Gallery 相关回归 **22/22**，独立原生 CTest **7/7**，完整激活/全屏
的第八项继续等待桌面验证。

GPU 读回在 1×、2× 下验证混合颜色、每行绘制、缓存、旋转、裁剪与透明度。新透明度
用例先复现了彩色 Emoji 不随前景 alpha 淡化的问题，再改为对保留的字形 Run 应用
透明度；修复前后的六张不透明参考图字节一致。已查看 2× 常规、旋转、裁剪和淡化图，
八张当前 GPU 图保存在 `artifacts/macos-window-verification/v36/`。这些是本进程离屏
GPU 图像，不能代替 Gallery 或 Window Lab 的实际窗口截图。

Gallery、宿主与 Lab 构建均为 0 错误，分别保留 18、10、0 个警告；核验 559 个源码
指纹，其中 483 个为冻结原生来源。八个 dylib 重新构建，三个离线 Metal 资源保留
v35 字节。三个应用的签名、48 份原生副本、9 份资源及各自 SDK 链接产物按包核验；
v35 原生与已签名 Gallery 保留原负载。Window Lab 的“换行导航验证”内新增长 Run、
混合样式与双向文字示例，包含中文提示与窄窗口滚动容器。

CUA 继续报告 Mac 锁定，v36 实际窗口截图、物理键盘、外部 AX、中文候选窗、系统
菜单与 Dock 验收尚未完成。广泛的富文档方向/装饰/格式恢复、其余文本绘制路径的
彩色字体透明度、国际分词及更广的原生编辑动作仍开放；完整 macOS 目标保持进行中。

此前准备好的 Gallery 与 Window Lab 验证应用 **v35** 的记录位于
`artifacts/macos-window-verification/v35/validation-results.json`。
TextBox 的 Command+左右及 Shift 组合现在使用实际 CoreText 视觉行，双向文字采用
物理左右字形边缘。软换行行末与下一行开头共享索引时，独立保存光标方向，使重复
导航、光标绘制、滚动、鼠标命中及当前插入点的 IME 几何保持在所选行。显式设置
CaretIndex 或 Select 会恢复前向光标；取消组合文本保留原方向，提交文字重新确定位置。

新增的原生视觉行查询一次排版返回行范围、左右插入点及其方向；通过可选侧接口
保持既有 TextFormat 虚表，旧库或未实现的后端允许调用者回退。UTF-16 桥接保持整个
字素，行范围排除硬换行，支持空文本与末尾空行，并拒绝非有限尺寸和越界索引。
RichTextBox 使用现有 Run 排版行和 TextPointer 的 LogicalDirection，宽度变化重新
排版，鼠标命中与上下移动按同一方向选择共享边界。其既有渲染器仍不在长单 Run
内部自动换行，也未完成广泛的双向文档排版和选区行为。

修复前初始 **15/15** 视觉行用例失败。后续分别捕获窄 RTL 输入框错误横向偏移 356、
富文本在共享行首按 Up 跳过一行、TextBox 点击行末后光标跳到下一行的失败证据，
再完成修复。初次 fixture 编译错误、未真正换行的 Emoji 示例与遗漏提交事件的 IME
fixture 单独保留，不计为生产回归。

最终托管检查重建后 **362/362**：既有 312 项、20 项真实 CoreText 几何检查和 30 项
视觉行/光标方向检查。覆盖中文、组合字符、Emoji、物理 RTL 边缘、行末绘制命令、
只读导航、鼠标、取消/提交组合、窗口宽度变化、CRLF、空行及长段落查询。
绘制命令捕获与实际桌面截图验收分开记录。
完整 AppKit 宿主重建后 **95/95**，其中新增 TextBox 和富文本两项通过真实 NSView
按下/释放路径验证 Command 与 Shift 换行导航；已有窗口、菜单、无障碍、重开及
六类编辑动作全部重跑。Gallery 相关回归重建后 **22/22**，独立原生 CTest **7/7**。
完整 Window 激活/全屏的第八项仍待解锁。

Gallery、宿主、Lab 最终构建均为 0 错误，分别保留 0、8、0 个警告；初次完整构建
的警告记录也保留。核验 551 个源码指纹与当前证据，三个应用签名、48 份原生库副本
和 9 份资源；原生六个源文件新增 core/Metal 视觉行能力，八个 dylib 完整重建，
三个离线 Metal 资源保留 v34 字节。旧 v34 原生与已签名 Gallery 负载保持完整。
Window Lab 新增“换行导航验证”入口，带中文操作提示、编辑框名称与窄窗口滚动容器。

CUA 仍报告 Mac 锁定，没有 v35 实际截图、物理按键、外部 AX、中文候选窗、系统菜单
或 Dock 验收。完整 macOS 目标保持开放；富文本长 Run、国际分词与更广原生命令继续补齐。

此前准备好的 Gallery 与 Window Lab 验证应用 **v34** 的记录位于
`artifacts/macos-window-verification/v34/validation-results.json`。
本轮修复 v33 引入的 AppKit responder 动作回归：原生 `selectAll:` 发送旧 Control+A，
在新的物理修饰键语义下被解释为移到行首。Copy、Cut、Paste、Select All、Undo 与
Redo 现在携带 Command，Redo 同时携带 Shift，保留既有托管主快捷键路由。

同一新宿主 fixture 在冻结 v33 原生负载上为 **0/5**，三个编辑器的全选都变成
光标/锚点 6，而预期是 0..23；密码框全选也失败。修复后为 **5/5**，并检查物理
Control+A 仍移到行首、Command+A 仍全选、只读密码框拒绝删除，禁用与关闭窗口
拒绝原生动作。非编辑接收器检查六个动作的按下/释放身份，不读取或写入用户剪贴板。
新的原生回调 fixture 在旧负载上同样因 Control 标志失败，修复后覆盖六动作及禁用、
销毁后的拒绝路径。

完整真实 AppKit 宿主重新执行 **93/93**，托管检查 **312/312**，Gallery 检查 **22/22**。
后两者使用保留的测试程序集重新执行，托管 Window/编辑器和 Gallery 生产源码未变。
原生负载在独立目录重新构建，七项独立 CTest 对冻结 v34 库和资源重新执行 **7/7**。
首次 6/7 的日志保留：Metal smoke 的 Paste 检查仍断言旧 Control 标志；将该契约断言
更新为 Command 并重建 fixture 后全部通过。完整 Window 激活/全屏项仍待解锁。

Gallery、宿主、Lab 构建均为 0 错误，分别保留 18、10、0 个警告。核验 546 个源码
指纹、58 份证据、三个应用签名、48 份原生库副本及 9 份资源；Gallery 主模块等四个
程序集与本次 SDK 链接输出一致，旧签名 v33 Gallery 和原生快照保持完整。
八个 dylib 因完整重建而有新的二进制指纹；生产行为只修改原生 responder 桥接，
三个离线 Metal 资源与 v33 完全一致，未重新生成 shader 资源。

本轮应用清单及打开已签名 v34 Lab 时，CUA 仍报告 Mac 锁定。没有 v34 实际截图、
菜单/物理按键或外部 AX 验收。本轮未实现软换行导航；Command 行末还需要正确保留
视觉行的光标方向，避免与下一行开头共享的文本索引使光标跳行。完整 macOS 目标保持开放。

此前 Gallery 与 Window Lab 验证应用 **v33** 的记录位于
`artifacts/macos-window-verification/v33/validation-results.json`。
原生按下、释放事件保留 `PhysicalModifiers`，区分 Command、Control、Option 和 Shift；
已有逻辑手势仍兼容 Command，合按 Control+Command 不再被折叠成普通主快捷键。
按键释放也重新检查当前 Window 的有效焦点，拒绝发送到其他窗口。

TextBox、EditControl 和 RichTextBox 的 Command+左右移到行边界，Command+上下移到
文档边界，Option+左右按词移动，Option+Backspace/Delete 按词删除；Shift 保留选区锚点。
物理 Control+A/E/B/F/P/N/H/D 进入相应编辑操作，EditControl 的显式用户手势优先。
密码框继续拒绝周边文本读取与复制/剪切，导航使用字段边界。行为依据
[Apple 的文本快捷键约定](https://support.apple.com/en-us/102650)。
当前按词算法保留完整字素簇，但不表示与 AppKit 的国际文本分词完全一致；软换行的
视觉行边界和更广的原生编辑命令仍需补齐。Gallery 四语言的按词及文档边界提示同步更新。

新增最终 fixture 含 **99** 项检查；同一 fixture 在冻结旧 Managed.dll 上为 **18/99**
通过、81 项失败，修复后连同既有检查为 **312/312** 通过。初次 fixture 的编译错误、
只读快照与富文本隐式末尾换行误判日志单独保留，不计作生产缺陷。
Gallery 相关回归重新执行 **22/22**，复用增量测试目录而不修改已签名 v32 应用。
真实 AppKit 宿主重新执行 **88/88**，其中四项新检查通过窗口自身 NSEvent 路径验证
三类编辑器导航及 16 种修饰键组合的按下/释放。宿主禁止前台激活，这些结果不能代替
实际物理键盘、Dock 或前台焦点验收。

Gallery、宿主与 Lab 构建均为 0 错误，分别有 18、10、0 个警告；前两者保留既有
编译/裁剪警告。核验 545 个源码指纹、37 份证据、三个应用签名、48 份原生库副本和
9 份资源；另核对 Gallery 主模块等四个程序集与本次 SDK 链接输出一致。
v33 原生负载的 11 个文件、480 个源指纹及完成标记与 v32 相同，未重新构建或执行
原生 CTest；独立七项的最后执行版本仍为 v30，完整 Window 激活/全屏项仍待解锁。

用户回复已解锁后，CUA 的应用清单和打开已签名 v33 Lab 两次操作仍报告 Mac 锁定。
未获得 v33 实际窗口、截图或外部无障碍树；原生菜单 Escape、Dock 重开、语言提示
及新的物理快捷键仍不能写成实际验收通过。完整 macOS 目标保持开放。

此前 Gallery 快捷键验证应用 **v32** 的记录位于
`artifacts/macos-window-verification/v32/validation-results.json`。保留 Gallery 现有
布局、字体和配色，八份语言资源共 72 处提示采用命名快捷键标记，搜索提示在 macOS
显示 `⌘ K`，中间使用不换行空格。复制、粘贴、全选、撤销与文本编辑页的重做显示
Command 组合，编辑器原本已支持的重做提示采用 `⇧⌘Z`。菜单与自定义命令示例
引用相同的快捷键资源；标准命令按自身实际注册的 KeyGesture 显示。
Windows/Linux 保留 Ctrl 提示，开发示例中的手势声明及 KeyGesture 转换器未改动。

v32 定向回归 **22/22** 通过，包括三项新提示/资源回归、四项命令示例、十四项
现有响应式 shell 与一项缓存可见性回归；覆盖四语言反复切换、保留资源字典、
数字格式参数与显式显示覆写，以及手势匹配不因显示格式改变。
Gallery 构建为 0 错误、18 个既有警告；新 fixture 初次产生的 nullable 警告已修正，
最终测试构建只保留三个既有 xUnit 分析器警告，初次构建和结果日志仍保留。

此次仅重新构建 Gallery。v31 的签名 Lab、宿主和 Window 代码保持不变，宿主
84 项结果的最后执行版本仍为 v31；独立原生七项的最后执行版本仍为 v30。
v32 原生快照的 11 个负载文件、480 个源指纹和完成标记与 v31 完全一致，未重跑
这些未改动的检查。校验 Gallery 签名、原生负载、539 个源码指纹及各自 SDK 链接
输出中的 Gallery 模块与宿主程序集。

打开已签名 v32 Gallery 时，CUA 再次报告 Mac 锁定，未获得界面状态或截图。
提示的实际字形、语言切换、菜单与物理按键不能写成通过。
此次只调整显示；现有 Control 手势在 macOS 仍由 Command 兼容映射驱动。
Option 按词移动、Command 的行/文档边界、物理 Control/Command 区分及更广的
原生命令与文本导航语义仍需要实现和验收，完整 macOS 目标保持开放。

此前 Window 验证应用 **v31** 的记录位于
`artifacts/macos-window-verification/v31/validation-results.json`。本轮完善 AppKit reopen
delegate：主窗口禁用或关闭中时选择其他可用窗口；恢复回调关闭目标后继续尝试
快照内的其他窗口；阻止重入和已停止宿主恢复。最小化窗口虽然没有显示，AppKit
仍将其计入 hasVisibleWindows，所以先检查实际可用的显示窗口；可见但禁用的
modal owner 也不阻止对话框恢复。窗口句柄指向内容容器内的渲染视图，按其实际
NSWindow 身份关联托管状态，保留宿主自行创建的原生窗口处理。
自行恢复成功后返回 false，阻止默认 untitled 流程；已有可用显示窗口或没有可用
目标时保留正常 AppKit 处理，遵循
[Apple reopen delegate 约定](https://developer.apple.com/documentation/appkit/nsapplicationdelegate/applicationshouldhandlereopen(_:hasvisiblewindows:))。

旧实现真实宿主重开检查为 **2/10**；首轮修复通过 10 项后，追加的可见禁用
owner/最小化模态检查复现失败。最终采用 NSWindow 身份关联后 **11/11** 通过，
并保持对话框模态、owner 和独立窗口禁用状态及关闭后的恢复。已有生命周期、
无障碍、窗口按钮与菜单检查通过，v31 宿主合计 **84/84**。宿主禁止应用前台激活，
这些结果不证明实际 Dock 点击、key window 或前台焦点已验收。

v31 最终增量构建的 Gallery、Lab 与宿主均为 0 个警告和错误。初始化构建、早期
编译与模态失败日志分别保留；初始化构建有既有警告，不将所有构建尝试写成零警告。
原生源文件未改变，明确复用 v30 的 11 个负载文件和完成标记；原生 7 项结果的
最后执行版本仍为 v30，完整 Window 激活/全屏项仍待解锁，未宣称 v31 重新构建
或重跑原生。证据增加 macOS delegate 与新重开 fixture 的指纹，覆盖 30 个托管/
辅助源文件与 480 个原生源文件。应用签名、冻结负载、各自 SDK 链接程序集已校验，
v30 冻结负载保持不变。

打开已签名 v31 Lab 时，CUA 再次报告 Mac 锁定，未获得 v31 界面状态或截图。
实际 Dock、菜单 Escape、物理按键及完整 Window 转换仍未通过界面验收。
外部原始无障碍属性与通知、VoiceOver、真实中文候选窗、多显示器和更广的
macOS 行为保持开放；解锁问题仍待回复。

此前 v30 Window 验证应用的记录位于
`artifacts/macos-window-verification/v30/validation-results.json`。原生平台观察公开的
NSMenu 开始/结束跟踪通知，在内容响应者收到 Escape 时调用 `cancelTracking`，
阻止同一次按下、重复与释放事件进入托管 IsCancel 或输入法取消处理。平台最后一次
Shutdown 移除通知观察者和菜单引用；下一次新 Escape 恢复正常按键分发。

定向原生用例在冻结的 v29 负载上按预期失败；修复后有边框和无边框窗口、嵌套
子菜单结束后仍跟踪的父菜单、后续 Escape 及未提交组合文本保留全部通过。独立
原生与 GPU 检查 **7/7**，真实 AppKit 宿主 **73/73** 通过；注册的第八项真实
Window 激活/全屏检查需要解锁后运行。不能把这七项写成完整八项已通过。

v30 的 Gallery、Lab 和宿主最终构建均为 0 个错误，分别有 8、10、10 个警告。
Gallery 首次出现 MSB4166 子节点提前退出，保留失败日志；关闭构建服务器与节点
重用、降低外层并发并关闭共享编译后重试成功，未确认提前退出的具体原因。
480 个原生源文件与 28 个托管/辅助源文件指纹保持一致。Metal 及 shader 源文件
未改变，明确复用 v29 新生成并通过 GPU 检查的 shader 文件；没有宣称重新生成。
三个应用签名、48 份原生动态库副本、各自 SDK 链接程序集与 33 个证据文件已校验，
v29 冻结负载保持不变。Lab 同时记录真实菜单跟踪通知，供下一轮界面验收对照。

尝试打开已签名 v30 Lab 时，CUA 仍报告 Mac 锁定；实际菜单 Escape、截图、按键、
完整 Window 转换以及外部无障碍属性与通知没有通过 v30 验收。解锁问题保持待回复，
真实中文候选窗、VoiceOver、Dock、多显示器及更广的 macOS 范围也保持开放。

v29 完成独立原生构建，480 个原生源文件在配置、编译、CTest 和冻结期间保持不变。
原生 **7/7** 与真实 AppKit 宿主 **73/73** 通过；Gallery 构建 18 个警告、0 个错误，
Lab 0 个警告和错误，宿主 10 个警告、0 个错误。v28 的托管 213/213 和公共 149/149
未在 v29 重跑，相关托管源文件指纹未变化，不把历史执行计作 v29 新执行。
记录位于 `artifacts/macos-window-verification/v29/validation-results.json`，三个应用签名、
冻结负载、各自 SDK 链接程序集与 33 个证据文件已校验。v28 冻结负载保持不变。

v29 Lab 新增“窗口参与验证”，实际最小尺寸 520×580 DIP 时编辑框和所有操作完整
可见，Tab/Shift+Tab 的 AX 焦点与可见焦点环一致。真实导航按键完成
`abcd → abd → ad → ax`；普通窗口关闭菜单参与后仍可进入 AppKit 全屏，客户区为
2560×1440 DIP，RestoreBounds 保留 680×600。全屏中切换 ShowInTaskbar 不改变几何，
实际 Window 菜单同步增加或移除子窗口条目；退出后还原 680×600 和原编辑内容。
截图已在工具输出中查看，没有保存本地截图文件。

v29 同时复现失败：子窗口打开 Window 菜单后按 Escape，误触发其 IsCancel 按钮并
关闭窗口，菜单仍在跟踪。菜单公开 Cancel 动作可正常结束跟踪。此失败保留在
`artifacts/macos-window-verification/v29/desktop-observations.json`，不计作菜单验收通过。
退出全屏后的 `z` 显示为未提交组合文本，Return 后提交为 `v29 窗口参与z`；没有
确认独立的焦点恢复失败，也不能据此确认真实中文候选窗。之后桌面工具再次报告
锁屏，后续修复的实际菜单操作需要解锁后验证。

此前 v28 Window 验证应用的记录位于
`artifacts/macos-window-verification/v28/validation-results.json`。此前用 AX 打开模态窗口，
普通组、文字和按钮带有多余选择/展开标记；内层关闭后外层说明的 AX 值已更新，但
截图仍是初始文字。v27 的同一程序改用实际鼠标坐标打开后，两项异常都不出现。
v28 修正 Invoke 调度，使原生 AX 请求先返回，再执行可能进入 ShowDialog 的用户代码；
没有修改原生事件泵或渲染代码。

v28 的执行结果：

- 托管 Window 检查 **213/213**，公共 Window、按钮、ContentDialog、Tab 与焦点回归
  **149/149**；公共检查在 Mac 上运行，不等同于 Windows 系统窗口验收。
- 真实 AppKit/Metal 宿主 **73/73**，其中 Window 无障碍 **14/14**、默认/取消按钮
  **12/12**。八个新用例在旧实现中 **0/8 通过**，均先在同步 Invoke 断言失败；
  之后的嵌套模态、排队后目标失效与异常隔离由最终正向运行检查。
- Gallery/Lab/宿主构建均 **0 错误**，警告分别为 **18/0/10**。三个 SDK 应用的
  原生资源、各自链接程序集与严格临时签名均核对；Gallery 与 Lab 共用实现字节一致。
- 原生 11 个负载及完成标记逐字节继承 v27；**7/7 CTest 的实际执行版本仍为 v23，
  v24 至 v28 没有重跑**。本轮各项宿主检查使用完整 SDK 应用包执行。

最终核对时公共原生目录的 Metal 库与完成标记已被其他构建更新，v28 冻结副本仍匹配
清单及 v27。验证器分别记录公共输出的变化，并检查三份应用实际负载与冻结清单一致；
没有将新的公共输出复制进已验证应用，也不将公共输出计为本轮测试负载。
随后原生 Window 代码和测试夹具也出现其他修改。原始源码哈希保留，当前差异单独
记录；这些后续改动没有计入 v28 的构建或继承 CTest 结果。42 项源码记录中，9 项
原生 Window 来源严格核对冻结清单，其余 33 项严格核对当前文件；本轮托管代码和
宿主夹具仍保持构建时状态。

```bash
open "$PWD/artifacts/macos-window-gallery/Jalium.Window.Validation.v28.app"
open "$PWD/artifacts/macos-window-lab/Jalium.Window.Lab.v28.app"
```

v28 的实际记录位于 `artifacts/macos-window-verification/v28/desktop-observations.json`。
以下截图和外部树均已在 CUA 工具输出中查看，没有声称保存本地截图文件：

- AX 打开的外层和内层模态普通控件不再带 selectable/collapsed/Expand 标记。
  首次 Escape 取消关闭后仍能编辑中文；内层 Enter 返回后，外层取消关闭的说明文字
  截图与 AX 值一致，编辑器内容和焦点恢复。另一次新会话通过 AX 取消按钮最终关闭，
  主窗口截图、外部树、启用状态和 False 结果均确认。首次会话最后 Escape 遇到工具
  报告窗口变化，其事件日志不单独作为最终主窗口截图验收。
- 660×600 DIP 和最小 540×580 DIP 分别完成六种模式、退出后再切回隐藏模式的
  Enter/Escape 与 Tab/Shift+Tab。累计确认/取消各 **14 次**，隐藏调用 **0**；退出
  后代仍绘制但不暴露、不获焦点；重复隐藏后既不绘制也不暴露。最小窗口文字换行、
  编辑器、状态和操作按钮均可见。
- 非模态 CSS 窗口的 dialog/standard window 角色往返正确，普通控件没有新增
  选择或展开能力；通过 AX 关闭后实际返回主窗口。
- Gallery 系统左侧平铺时预览从截图和树中移除，恢复原尺寸保留 Inputs 选择与预览；
  通过 AX 打开完整示例后缓存首页不再暴露。中文粘贴、Command+A 后 Delete 清空、
  Tab 跳过禁用框及 Command+K 聚焦搜索均通过。验证脚本最初误把无名称编辑器的
  值要求为单独的 `Value:` 字段，检查实际树与截图后按 CUA 的合并标签格式核对。
- 标题栏实际双击使 760×860 DIP 变为 Maximized、2560×1331 DIP，再次双击恢复
  Normal、760×860 DIP，RestoreBounds 始终保留普通尺寸。随后只读检查系统设置，
  当前“窗口标题栏连按操作”为“缩放”；其余系统偏好的实际操作仍待验证。

Dock 在解锁状态下的 CUA 读取仍返回 `timeoutReached`（-10005），没有执行图标重开，
不能计为通过。系统设置确认已有 ABC 和简体拼音，切换快捷键为 Control+Space；
Gallery 的自动按键在切换尝试前后均得到普通 `ni`，Space 后为 `ni `，没有观察到
候选窗或中文提交。验证输入已清空，并请求用户在同一输入框手动确认真实候选窗。

外部 Window/默认/取消关系原始属性、静态文本仍显示的 settable 标记、通知接收和
VoiceOver 尚未验收；真实中文候选窗、Dock、多屏及后续 macOS 范围仍开放。
整个目标保持进行中，历史失败证据保留。

### v27 历史验证

v27 的记录位于
`artifacts/macos-window-verification/v27/validation-results.json`。v26 已修正 Tab 候选
收集：排除有效 CSS 隐藏和退出中的控件，继续遍历 CSS 隐藏祖先以保留显式显示后代。
实际六种模式的双向 Tab 已通过，但循环切换发现另一项可见性继承缺陷。

display 动画把布局参与状态映射到原生 Visibility；此前该动画值会覆盖 CSS
visibility 继承，退出后切回隐藏模式时仍暴露后代。v27 让有效可见性与 computed
visibility 共用镜像识别规则，忽略仅用于 display 的镜像，保留真正的本地值和显式动画。
这是可见性继承修复，不宣称更改 display 动画的取消或持续时间语义。

v27 的执行结果：

- 托管 Window 检查 **213/213**。新增四个退出后移除样式的重用用例，在旧实现中
  **0/4 通过**，最先在 IsVisible 断言失败；后续按钮路由断言的旧实现结果不作推断。
- 真实 AppKit/Metal 宿主 **65/65**，默认/取消按钮 **12/12**。新用例检查重用后隐藏
  子树、旧 AX 对象动作拒绝、默认/取消引用、Enter/Escape 回退，以及显式显示后代的身份恢复。
- 公共 Window、按钮、ContentDialog、Tab 与焦点回归 **149/149**。这些检查在当前
  Mac 上执行，不等同于 Windows 系统窗口验收。
- Gallery 构建 **0 错误/18 条已有警告**，Lab **0 错误/0 警告**，宿主 **0 错误/10 条已有警告**。
  Lab 增加最小/恢复尺寸按钮和非模态窗口的对话框语义切换，便于区分原生角色与模态循环。
- 原生 11 个负载和完成标记逐字节继承 v26；**7/7 CTest 的实际执行版本仍为 v23，
  v24 至 v27 没有重跑**。各版本的独立 SDK 输出、应用与失败证据保留。

```bash
open "$PWD/artifacts/macos-window-gallery/Jalium.Window.Validation.v27.app"
open "$PWD/artifacts/macos-window-lab/Jalium.Window.Lab.v27.app"
```

v27 使用冻结 `artifacts/macos-window-native/v27/Debug`。实际记录位于
`artifacts/macos-window-verification/v27/desktop-observations.json`，截图已在 CUA 工具输出
中查看，没有声称保存本地截图文件。

- 常规 660×600 DIP 与最小 540×580 DIP 窗口的六种模式均完成 Enter/Escape、
  Tab/Shift+Tab；每组再从退出模式切回 CSS 隐藏。累计确认/取消各 14 次、隐藏调用 0。
  退出后代仍绘制但不在外部树中，焦点到可见回退；重复隐藏后后代既不绘制也不暴露。
  最小尺寸的说明、状态、编辑器、动作和页脚均可见；恢复尺寸截图通过。
- 非模态 CSS 窗口切换对话框标记后根角色正确往返 dialog/standard window，普通控件
  没有新增选择或展开能力。真实模态与内层模态仍在普通组、文字、按钮上出现这些标记，
  因而仅切换对话框子角色不能复现该问题；原因尚未确认。
- 真实模态首次 Escape 取消关闭，中文编辑、内层 Enter 关闭与外层编辑器焦点恢复、
  第二次 Escape 最终关闭及主窗口启用恢复通过。内层关闭后，外层说明文字的 AX 值
  已更新，截图却回到初始文字；再按 Left 后仍不一致。这项刷新缺陷尚未修复。
- Gallery 的系统 Move & Resize → Left 隐藏预览并从树中移除，Return to Previous Size
  恢复 Inputs 选择与预览；打开示例后缓存 Home 不再暴露。四字中文粘贴、Command+A
  后 Delete 清空、Tab 经过预填/可用框并跳过禁用框、Command+K 聚焦搜索均实测通过。

当时模态普通控件的多余能力和嵌套返回后的文字刷新尚未解决，后由 v28 修正并实测。
外部原始属性、通知、VoiceOver、真实输入法、Dock 和多屏仍开放。
v25/v26 的实际失败与通过记录保留各自版本范围。

### v26 历史验证

v26 托管 **209/209**、真实宿主 **64/64**、公共回归 **149/149**。Tab 修复前六个
新用例为 **2/6 通过**：四个隐藏/退出用例先在预测目标断言失败，显式可见后代的两个
正向用例保留。最终 Gallery/Lab/宿主构建分别为 0 错误和 18/0/10 条已有警告。

实际六种模式的 Enter/Escape 各调用 6 次、隐藏调用为 0，Tab/Shift+Tab 均到达正确
目标，截图能看到退出后代仍绘制而焦点转到可见回退按钮。退出模式切回 CSS 隐藏的
循环仍失败，隐藏调用增加为 2，后代重新出现在外部树；Lab 的显式 display 重置并未
解决该缺陷。一次角落拖动没有改变窗口尺寸，未据此接受最小尺寸。上述失败保留在
`artifacts/macos-window-verification/v26/desktop-observations.json`，由 v27 继续修复。

### v25 历史验证

Window 验证应用 **v25** 的记录位于
`artifacts/macos-window-verification/v25/validation-results.json`。本轮在 v24 的 CSS
可见性修复上补齐退出动画状态和延迟焦点重验期间的输入边界。

框架的 `display:none` 退出动画保留绘制，已经开始停用输入。Window 默认/取消
按钮查找与 AppKit 无障碍树现在沿用该规则：立即移除按钮关系和树中的节点，拒绝
已缓存 AX 对象的动作；取消退出后恢复同一对象身份。激活或原生 FocusGained
通知也不再保留退出中的编辑器焦点。原生按键与文本到达时即时检查目标是否启用、
可见、未退出且属于当前窗口，避免调度批次末尾的焦点重验完成前收到输入。

v25 的执行结果：

- 托管 Window 检查 **203/203**。新增 12 个用例，分别覆盖按钮自身与祖先退出、
  Enter/Escape、Activate/FocusGained、CSS 隐藏与退出状态下的即时按键和文本输入。
  三组修复前检查分别 **0/4、0/4、0/4 通过**，有效夹具及失败日志保留。
- 真实 AppKit/Metal 宿主 **64/64**：默认/取消按钮 **11/11**，其余语义与绑定、
  控件与 Window 无障碍、生命周期和菜单 **53/53**。新增退出用例覆盖自身与祖先、
  旧 AX 动作拒绝、Enter/Escape 回退及重新显示身份；旧实现先在按钮关系断言失败，
  该基线不证明后续断言在旧实现中已执行。
- 最终公共窗口、按钮、ContentDialog 和键盘焦点回归 **82/82**，使用冻结 v25
  原生库路径。菜单检查的一次无效命令参数调用单独保存，最终以正确入口通过。
- Gallery 最终构建 **0 错误/18 条已有警告**，Lab **0 错误/0 警告**；Lab 添加第六种“退出动画（60 秒）”
  模式，将两组操作放在独立行，增大窗口高度并保留中文名称和调用计数。已查看实际
  普通尺寸截图；最小窗口尺寸的视觉检查仍待完成。
- 原生实现及 11 个负载文件、完成标记继续继承 v24，**7/7 CTest 的执行版本仍为
  v23，本轮未重跑**。v24 应用、原生快照及失败基线均保留。

可运行的独立应用：

```bash
open "$PWD/artifacts/macos-window-gallery/Jalium.Window.Validation.v25.app"
open "$PWD/artifacts/macos-window-lab/Jalium.Window.Lab.v25.app"
```

v25 使用 `artifacts/macos-window-native/v25/Debug` 和各自独立的 bundle ID。
解锁后实际检查六种模式的 Enter/Escape，确认/取消各调用 6 次，隐藏按钮始终为 0。
退出动画中的按钮仍绘制在截图中，但从外部树排除。物理 Tab 此时停在编辑器；
从退出模式切回 CSS 隐藏时，Lab 又残留过渡状态。这两项单独记录为失败，进入 v26 修复。

模态首次 Escape 取消关闭后仍可全选、粘贴中文；内层 Return 关闭后，外层编辑器恢复
焦点与粘贴值，第二次 Escape 最终关闭外层并恢复主窗口。实际截图和事件记录均已查看。
Gallery 系统“Move & Resize → Left”使首页预览折叠，外部树不再包含预览控件和代码；
“Return to Previous Size”恢复预览并保留 Inputs 选择。进入示例后，缓存首页从树中移除。
中文粘贴、Command+A/删除、Tab 跳过禁用输入和 Command+K 搜索焦点也已实际检查。

模态和内层窗口的外部 CUA 树仍把普通控件标为 selectable/collapsed 并提供 Expand；
普通 Lab 与 Gallery 没有这些多余状态。该外部差异尚未定位，本进程的 selector 检查
不能替代其验收。静态文本 settable、外部原始属性、通知、VoiceOver、真实输入法、
Dock 和多屏范围仍开放，整个 macOS 目标继续进行。

### v24 历史验证

Window 验证应用 **v24** 的记录位于
`artifacts/macos-window-verification/v24/validation-results.json`。本轮修复 CSS 隐藏的
默认/取消按钮遮蔽可见按钮，以及激活窗口时恢复到隐藏编辑器失败后仍保留另一窗口
键盘焦点的问题。

按钮查找和焦点恢复使用有效 `IsVisible`，同时保留原生 Visibility 对整棵子树的限制。
CSS `visibility:hidden` 的后代可以显式重新显示，参见
[CSS 可见性定义](https://www.w3.org/TR/CSS22/visufx.html#visibility)；折叠 flex 项仍排除
显式显示的后代，参见 [flex 折叠规则](https://www.w3.org/TR/css-flexbox-1/#visibility-collapse)。
恢复焦点只有在 `Focus()` 成功时才结束，否则清理旧目标。Window 的 Enter/Escape
与 AX 默认/取消关系继续使用同一查找路径。

v24 的执行结果：

- 托管 Window 检查 **191/191**，新增 10 个 CSS 按钮与焦点用例；修复前经修正的
  夹具为 **4/10 通过、6/10 失败**。最初的两项夹具错误单独保留，不作为产品缺陷。
- 真实 AppKit/Metal 宿主 **63/63**：按钮关系 **10/10**，语义与绑定 **8/8**，
  控件无障碍 **12/12**，Window 无障碍 **6/6**，生命周期 **18/18**，菜单 **9/9**。
  新增三项 CSS 按钮用例在旧实现上分别失败，修复后通过；旧基线应用继续保留。
  输入检查调用真实 Window 平台事件入口，仍属于宿主夹具，不是物理键盘验收。
- 公共窗口、按钮、ContentDialog 和键盘焦点回归 **82/82**。首次运行缺少原生库
  路径，33 项报告加载失败；设置冻结 v24 的 `DYLD_LIBRARY_PATH` 后完整通过。
- Gallery 构建 **0 错误/18 条已有警告**，Lab 最终构建 **0 错误/0 警告**。
  Lab 新增“CSS 按钮验证”，提供 CSS 隐藏、显式显示后代、原生折叠、display:none
  和折叠 flex 项五种模式，并显示确认、取消与隐藏按钮调用计数。
- 本轮没有修改原生实现。v24 的 11 个原生负载文件与完成标记逐字节继承 v23；
  **7/7 原生 CTest 是 v23 的执行证据，本轮没有重新运行**。

可运行的独立应用：

```bash
open "$PWD/artifacts/macos-window-gallery/Jalium.Window.Validation.v24.app"
open "$PWD/artifacts/macos-window-lab/Jalium.Window.Lab.v24.app"
```

两者使用 `artifacts/macos-window-native/v24/Debug`，独立 bundle ID 与临时签名。
HostSmoke 的 v24 SDK 构建已包含完整 Resources，无需人工补入。本轮实际桌面工具
再次报告 Mac 锁定，尚未查看 v24 截图、执行五种模式的物理 Enter/Escape，或完成
Gallery 首页折叠预览与 Lab 模态焦点的桌面回归。已有 v21/v23 实测记录保持历史范围；
不能将自动检查计为新的桌面验收，整个 macOS 目标仍在进行中。

### v23 历史验证

Window 验证应用 **v23** 的记录位于
`artifacts/macos-window-verification/v23/validation-results.json`。v22 的首次界面验证
发现选择 Inputs 后右侧仍显示 Buttons，已保留该预览应用和失败证据；v23 是补上
双向绑定同步并重新核对原生与托管负载后的结果。

本轮补齐以下内容：

- AppKit 属性与动作按实际 provider 能力开放。普通控件不再声明选择、展开、行集合
  或范围能力；未选中和禁用的可选项仍保留其能力，叶子行不声明展开。值、占位符、
  范围、已选子项及行披露分别按支持的模式和角色判断。
- 数据项的默认对象字符串改用已实现模板内可见文字；显式 Name、LabeledBy 及自定义
  peer 名称保留优先级。中文名称和更新后的模板文字有真实 AppKit 宿主检查。
- 使用有效 IsVisible 判断内容。CSS 隐藏容器可保留显式可见后代，后代的 AX 父节点
  跳过隐藏容器；原生 Visibility 和 display:none 仍排除整棵子树。重新显示保留身份。
- Gallery 的非活动缓存页使用 Collapsed，继续保留实例、绑定和资源作用域。切页后
  首页卡片和旧示例按钮不再进入当前窗口的无障碍树。
- 列表的 Select/Add/Remove 同步已选集合、标量值、容器状态与 SelectionChanged。
  自动化选择在批量更新后传回可写绑定源，保留绑定表达式和单向绑定限制。

v23 的执行结果：

- 托管 Window 检查 **181/181**；原生 CTest **7/7**；真实 AppKit/Metal 宿主
  **60/60**（新增语义与绑定 8 项，加既有无障碍 12 项、窗口无障碍 6 项、
  默认/取消按钮 7 项、生命周期 18 项、菜单 9 项）。
- 列表及自动化公共回归 **17/17**；Gallery 缓存、首页响应式/虚拟化和窗口布局
  检查 **49/49**。Gallery 测试使用冻结的 v23 原生媒体负载。
- 当前绑定用例在 v22 预览的托管负载上失败，在 v23 上通过。修正后已实现模板的
  七项语义基线在 v21 负载上有 6 项失败；先前未实现模板的夹具不作为名称修复证据。
  原生通用属性和 Gallery 缓存的失败基线也单独保留。
- Gallery 与 Lab 构建成功，分别为 **0 错误/18 条已有警告**和 **0 错误/0 警告**。
  原生 smoke 最初因模拟 NSEvent 缺少新增 phase 访问器而异常；补齐测试事件的
  type、phase 和 momentumPhase 后完整 7 项通过。首次失败日志保留。
- 最终打包核对通过：三个宿主的临时签名、48 份原生库副本、9 份着色器及清单资源、
  每个宿主自己的 SDK 链接程序集均一致。HostSmoke 的 SDK 包原先未带 Resources
  负载，已补入冻结资源并重签；上述 **60/60** 宿主检查在补齐后的最终包上重跑通过。

实际桌面检查已查看 v23 截图与外部 CUA 树：目录卡片名称为简短标题和类别，普通
控件无多余可选择/可展开状态；选择 Inputs 同时更新预览标题、输入框、代码和示例
按钮；进入 Inputs 后旧 ComponentList 与首页示例按钮不再披露。Command+K、中文
粘贴、Delete、Command+A 选区和清空均完成；Tab 跳过 Disabled 到 SourceTextBox，
Shift+Tab 返回 Editable，实际焦点环已查看。

系统靠左布局已查看：截图从 3000×1840 变为 2562×2642 像素。Inputs 检查器在较窄
布局中移到内容下方，不是 Collapsed，因此继续出现在 AX 树中符合当前布局设计。
返回首页检查折叠预览时 Mac 再次锁定；该项、恢复原尺寸和 v23 Lab 实际模态检查
尚待桌面恢复。普通静态文字在 CUA 输出中仍标注 settable；本进程 setter 拒绝检查
不能代替外部原始属性、通知接收和 VoiceOver 验收。

可运行的独立应用：

```bash
open "$PWD/artifacts/macos-window-gallery/Jalium.Window.Validation.v23.app"
open "$PWD/artifacts/macos-window-lab/Jalium.Window.Lab.v23.app"
```

两者使用 `artifacts/macos-window-native/v23/Debug` 冻结负载，独立 bundle ID 和临时
签名。v23 平台库保留本轮无障碍修复，并纳入共享工作区当时的滚动 phase ABI；其余
7 个原生库、两份着色器及清单与 v22 相同。Gallery 与 Lab 的托管公共程序集相同，
每个宿主分别核对自己的 SDK 链接产物。构建后另一项滚动工作更新了
ScrollViewer.MouseWheel.cs，该更新不包含在本轮已打包的 Window 结果中。
v21 和 v22 原生快照及 v22 绑定基线应用继续保留。日志、源码和负载指纹以及实际
验证范围随 v23 记录保存；整个 macOS 目标仍在进行中。

### v21 历史验证

v20 的窗口启用、模态、对话框子角色与本地化描述继续保留；
v21 补齐默认、取消按钮的无障碍关系，并修复 Enter/Escape 查找隐藏容器内按钮的问题。
本轮结果以 `artifacts/macos-window-verification/v21/validation-results.json` 记录：

- v21 托管检查 **181/181 通过**，覆盖窗口行为、关闭协商、材质、范围替换与字素边界、
  撤销、取消组合、焦点切换、默认上下文、范围几何、控件变换、密码保护与同步响应。
  v14 补齐模态隐藏、未指定关闭结果、无效 ShowDialog、可见性与激活重入；v15 新增
  子窗口取消 Quit、嵌套关闭顺序、应用模态范围、嵌套状态恢复、活动窗口恢复、
  启用回调异常与显示回调中的关闭/隐藏检查。v17 新增显示期间的状态覆盖、
  原生 StateChanged、内部属性监听器和派生属性通知中的新请求检查；新增 8 项均在
  对应修复前复现失败。v18 补齐活动窗口优先的隐式 owner、隐藏后各显示入口
  重建 parent、显示前新显式 Owner 的优先级，以及可见模态 Owner 的变更限制。
- v21 真实 AppKit/Metal 宿主生命周期检查 **18/18 通过**：SourceInitialized、Loaded、
  ContentRendered、Shown 中关闭；SourceInitialized 隐藏后使用同一窗口重开；
  初始化回调中最大化和还原保留所请求状态及原始 RestoreBounds；原生状态通知中
  StateChanged 与派生 OnPropertyChanged 在基础回调之后请求还原或最小化，最终
  托管及原生状态一致、还原尺寸保留。最小化检查处理事件至 AppKit 完成通知，
  避免在动画完成前断言。派生属性通知的两个用例在修复前等待 8 秒后仍失败。
  v18 新增 7 项真实所有权检查：隐藏模态重开换父窗口、普通显示重用、可见模态
  Owner 变更拒绝、显式 owned window 隐藏后通过 Show/Visibility/Activate 恢复
  parent，以及 SourceInitialized 的新 Owner 保留。其中 6 项修复前失败；普通显示
  重用在修复前已通过，保留为兼容检查。最初对应普通重用的模型假设与 AppKit
  隐藏行为不符，已移除。检查确认重用前后不更换原生句柄。
- 真实 CoreText 字体几何检查 **20/20 通过**，覆盖五种编辑器、RTL、emoji、组合字符、
  换行、软换行范围、候选矩形、垂直对齐、组合文本逐行装饰和 TextBox 长范围性能。
  这些是布局与绘制指令检查，没有替代真实中文候选窗验收；仅 v14 重跑通过，
  v21 没有重跑这组检查。字体几何代码及 Core、Metal 和着色器负载未修改，并核对
  相同负载哈希；平台库因新增无障碍接口改变。
- v21 原生检查共 **7 项**，锁屏期间所选 **6/6 通过**：Window 属性、滚动平台、
  无障碍协议以及 Metal smoke、regression、effects。新增协议检查覆盖 ABI 大小、
  UTF-16、稳定身份、父子关系、屏幕坐标、外部线程拒绝和动作中关闭窗口后的迟到查询。
  v20 的启用、模态和本地化检查保留；v21 新增默认/取消按钮身份复用、外部线程拒绝
  与销毁后的关系查询。完整 Window 激活/最小化/全屏检查在桌面恢复后单独执行，
  **1/1 通过，6.79 秒**。结果与锁屏期间的六项分开记录，合计注册的 **7/7 通过**。
- v21 无障碍真实宿主 **12/12 通过**：中文与显式名称、视觉按钮内容、实际按钮动作、
  UTF-16 文本与选区、只读和密码保护、Toggle/Radio、RangeValue、屏幕矩形与命中、
  移除/重插入、隐藏重开、动作中关闭、用户异常、父窗口禁用，以及树展开/折叠与选择。
  v19 数据项代理的展开缺失和折叠后仍披露子行的负例日志继续保留。
  这些是本进程 AppKit 协议检查。外部读取的实际范围见后文；通知接收与 VoiceOver
  尚未验收。
- v21 重跑 Window 无障碍宿主 **6/6 通过**：原生窗口启用/禁用、ShowDialog 的模态
  与对话框子角色、内容到原生窗口的父子关系、原生 AX 关闭取消和接受、隐藏后转普通
  显示、嵌套模态的禁用与恢复，以及显式非模态对话框标记。v20 对应负例日志保留，
  不作为 v21 新执行的负例。
- v21 新增默认/取消按钮宿主 **7/7 通过**：实际控件树身份、禁用/隐藏/重指派、移除与
  重插入、窗口隐藏重开和关闭、真实 ShowDialog 关闭取消后重试、弹层及内嵌 ContentDialog
  作用域、隐藏祖先的键盘查找。前六项在 v20 负载上均在最早的关系检查处失败；第七项
  单独复现实际键盘查找仍选择隐藏按钮的故障。后续动作断言不能算作修复前已执行。
- v21 宿主菜单重跑 **9/9 通过**，覆盖空主菜单、空应用子菜单、已有应用动作、已注册
  和已有命名的 Window 菜单、只有 Window 菜单的主菜单，以及重复配置。
- v21 Gallery Debug 构建成功，**0 错误、18 条已有编译警告**；Window Lab 构建
  成功，**0 错误、0 警告**。验证应用的临时签名
  检查通过。8 个原生库在
  MonoBundle 和 Resources 内的共 16 份副本，其加载节均与本轮原生快照一致；
  两份 Metal 着色器及清单逐字节一致，共享 Jalium.UI.Managed.dll 在两应用中相同；
  各应用的宿主程序集分别与其 SDK 链接产物一致，不要求跨应用哈希相同。
  核对记录保存在 `artifacts/macos-window-gallery/window-validation-v21.json` 和
  `artifacts/macos-window-lab/window-lab-v21.json`。实际桌面检查范围另行记录。
  本轮检查日志与各项实际执行范围保存在
  `artifacts/macos-window-verification/v21/validation-results.json`；v14 的实际模态与菜单
  验收日志另存于 `artifacts/macos-window-verification/v14/validation-results.json`。

本轮独立验证应用位于
`artifacts/macos-window-gallery/Jalium.Window.Validation.v21.app`，bundle ID 为
`com.jalium.ui.gallery.window-validation-v21`。可运行：

```bash
open "$PWD/artifacts/macos-window-gallery/Jalium.Window.Validation.v21.app"
```

它使用独立 Gallery 构建和保持不变的
`artifacts/macos-window-native/v21/Debug` 原生快照。平台库补齐默认/取消按钮关系，
其余 7 个原生库、两份着色器和清单与 v20 逐字节相同。
较早版本的负载、应用与记录单独保留。

Window Lab 位于 `artifacts/macos-window-lab/Jalium.Window.Lab.v21.app`，用于检查
自动尺寸、程序尺寸、最大化/还原、多窗口、模态与隐藏。模态页可确认/取消结果，
首次关闭故意取消，用于检查关闭协商。v15 增加独立窗口、内层模态和较高的初始窗口，
v21 已查看常规及自动尺寸截图，诊断内容完整可见。诊断事件及原生菜单日志写入 `/private/tmp`。

v21 的模态手动路径：新建独立窗口，返回主窗口打开模态，在 Window 菜单确认
独立窗口和主窗口均不可选；打开内层模态并关闭，确认只恢复外层；在新建的外层
模态窗口中按 Command+Q，首次应取消退出并保留全部窗口，再次应接受退出。
使用原 PID 与事件记录验证进程结束，避免界面读取自动重开应用干扰结果。

v21 无障碍手动路径：外部 AX 工具读取 Lab 与 Gallery，确认中文按钮和输入框出现；
确认模态窗口 AXModal=True、AXDialog 子角色与被禁用所属窗口 AXEnabled=False；
关闭取消时保留模态，隐藏后普通显示应清除模态。确认 AXDefaultButton/AXCancelButton
指向同一控件树内的实际按钮；禁用时关系保留且动作拒绝，隐藏后关系清除。
通过 AX Press、输入值和选区
操作确认真实目标与状态变化；用 VoiceOver 检查朗读、
导航顺序、焦点与通知。Gallery 输入回归保留 Command+K、Command+V、Delete、
Command+A 路径。桌面锁定期间没有执行这些步骤；桌面恢复后的实际记录如下。

v21 的实际桌面检查（CUA 读取并查看截图）：

- Lab 初始 **760×860 DIP**，启用自动尺寸后约 **744×801.40 DIP**，原生还原高度为
  **802 DIP**。两种布局的操作区和诊断历史均完整可见。
- 外部树出现中文按钮和文本输入框，模态窗口报告 `dialog`。Window 菜单确认独立
  窗口和主窗口在应用模态期间不可选；内层模态期间外层也不可选。
- 内层编辑器按 Enter 返回 True，恢复外层编辑器焦点，主窗口与独立窗口保持不可选；
  外层按 Escape 返回 False，主窗口恢复。事件 10、12 与实际截图一致。
- 在新模态中首次 Command+Q 取消退出，原 PID **82373** 保留，三窗口仍在，模态
  编辑焦点保留；第二次接受后该 PID 消失，主窗口和独立窗口关闭事件分别为 15、16。
  接受退出后没有用界面读取自动重开 Lab；较早 v14 Lab 进程保留。
- Gallery 的 Command+K 聚焦搜索，粘贴“中文 Window 验证”，Delete 退格删掉“证”，
  Command+A 的选区被外部树和截图确认；清空后恢复 121 个组件。外部设置搜索值为
  TextBox 后出现 Inputs/RichTextBox 两项。TextBox 页的 Tab 跳过禁用输入框，
  Shift+Tab 返回 Editable 输入框并显示焦点环。
- Gallery 的系统靠左布局收起详情预览和检查器；返回原尺寸后详情恢复，相关截图已查看。
- Dock 在桌面恢复后仍返回 `timeoutReached`，实际 Dock 重开没有完成；没有更换工具
  将普通启动当作 Dock 验收。中文内容来自粘贴，未验证中文候选窗或 VoiceOver。

v21 外部树还发现待修正问题：普通按钮和文本显示多余的可展开/可选择状态；数据项
名称包含 `HomeCatalogItem` 对象展开文本；进入 TextBox 页或收起检查器时仍披露旧内容。
Gallery 的 macOS 快捷键提示仍写为 Ctrl。菜单显示时的一次 Escape 触发了模态关闭
协商，需进一步区分菜单跟踪与工具时序。上述问题当时不计为通过；v23 已验证的
修复范围见本节开头。
外部默认/取消关系属性、通知订阅接收和完整语义尚未验收。

事件与菜单日志保存在 `artifacts/macos-window-verification/v21/desktop-events.jsonl`
和 `native-menu.txt`，具体观察与限制记录在同目录的 `desktop-observations.json`。

解锁后的实际界面检查：

- v7 普通窗口 **3000×1840**，Window 菜单 Zoom 后 **5120×2640**；Command+M 后
  菜单动作正确禁用，通过标题列表恢复后保持最大化。进入、退出全屏并再还原后，
  回到 **3000×1840**。
- v8 系统 Fill 后 **5120×2642**，靠左后 **2562×2642**，Return to Previous Size 后
  **3002×1842**。窄窗口收起预览并调整卡片列数。
- v7/v8 Window 菜单显示实际 Gallery 标题。v7 Tab 从编辑器跳过禁用控件到按钮，
  Shift+Tab 返回编辑器；v8 Command+K、粘贴、Command+A、Backspace 和清空后
  恢复 121 个组件均已查看截图。
- v6 Command+Q 和 v7 Command+W 后，分别确认原进程退出。界面工具读取已关闭
  应用时会自动重开，因此关闭验收使用原 PID 检查，未把重开后的窗口当作关闭失败。

v10 Window Lab 的实际尺寸以 DIP 记录，Retina 截图物理尺寸为两倍：

- 普通窗口及 RestoreBounds 为 **760×650**；启用 SizeToContent 后为 **744×732**。
  程序设置 **840×700** 时仍保留 WidthAndHeight，重新布局回到自然尺寸。
- 系统 Fill 后为 Normal/Manual **2560×1325**，RestoreBounds 跟随当前尺寸；系统返回
  原尺寸后为 **745×733**，存在 AppKit 的 1 DIP 舍入变化。
- 自动尺寸窗口最大化后靠左，先得到 Normal 状态再得到用户尺寸回写，最终为
  Manual **1281×1325**；再次最大化及还原回到该尺寸，Zoom 还原回调读取相同保存值。
- 两窗口菜单条目、切换后的逻辑焦点、保存的文本插入位置和输入目标隔离已查看截图。
  Command+W 关闭第二窗口后主窗口保留；接受主窗口关闭后 owned window 先关闭，
  原进程退出。关闭后 RestoreBounds 为 Empty。
- 无边框 Window 菜单 Close 未关闭第二窗口。该实际故障促成 v11 的回归和修复，
  v12 属性检查通过，v14 实际菜单操作已通过。

上述 v10 事件记录保存在 `artifacts/macos-window-lab/window-lab-v10-desktop-events.jsonl`。

v14 Window Lab 的实际界面与交互：

- 初始 **760×650 DIP** 下历史区被裁切；启用自动尺寸后窗口为 **744×801 DIP**，
  全部诊断内容可见。托管自然高度约 **801.40 DIP**，原生 RestoreBounds 高度为
  **801 DIP**，存在分数 DIP 舍入。v15 调整初始高度，新的布局尚待截图复核。
- 默认应用菜单可见 Quit/Command+Q。无边框第二窗口通过 Window 菜单 Close 关闭，
  主窗口保留。
- 模态窗口显示时，父窗口的 Window 菜单条目禁用；Enter 触发默认按钮，首次关闭
  取消后窗口及编辑焦点保留。
- 隐藏模态窗口返回 False，父窗口恢复可用；再次打开同一窗口保留粘贴的中文内容
  和逻辑焦点。Escape 返回 False；接受默认按钮确认返回 True；接受标题栏关闭
  返回 False。上述操作均查看了实际截图，并由事件记录确认父窗口恢复。
- Dock 读取超时，实际重开操作仍未验收。中文内容来自粘贴，不能用作中文输入法
  验收证据。AX 树只暴露窗口和菜单，未通过控件无障碍或 VoiceOver 验收。

v14 事件记录、菜单配置与检查日志保存在 `artifacts/macos-window-verification/v14/`。
较早 v10 的 Documents 权限请求按请求 ID 复查后，发现已在 13:18:43 完成；
之前按应用标识过滤漏掉了结束记录。之后完整 Window 激活检查恢复通过，具体因果
关系未确认。v21 本轮开始时仍锁屏，后续桌面读取恢复；当前结果见 v21 的实际记录。

真实中文候选窗仍待验收。v7 观察到临时组合显示及切换编辑器后丢弃组合；v8 的
自动化按键得到普通 `ni ` 文本，未观察到中文候选窗。系统输入源查询不能替代
应用输入上下文的验收。v8 的 AppKit 取消和组合期间 Command 快捷键修复已通过
原生协议检查，尚未在真实候选窗中验证。

最新版本的最短手动输入路径：Command+K 聚焦搜索框，Command+V 粘贴，Delete 删除，
Command+A 全选并再次输入；再进入 TextBox 页面，在多行换行文本和中文组合输入中
检查光标、候选窗、选区替换、取消组合以及 Tab/Shift+Tab。普通搜索与焦点步骤已有
实际证据，多行中文候选窗、系统文本服务、实际标题栏双击偏好及系统设置切换后的材质观感仍需验收。

## 后续范围

- 更广的嵌套关闭、独立/所属窗口及系统菜单组合；真实中文候选窗及 Dock 重开。
- 混合 DPI 多屏移动、显示器热插拔；更广的 Spaces/多窗口/系统布局组合；窗口轮换参与。v29 已实际验证 ShowInTaskbar 的普通/全屏菜单条目与尺寸还原。
- 标题栏系统双击的其余偏好；v28 已实测当前“缩放”设置的最大化/还原。功能键、非英文布局及侧键设备。
- 补齐其余窗口关系和可写属性的外部读取、通知接收、VoiceOver、复杂表格/虚拟化
  列表与文本行导航。v57 已实际外部检查普通/禁用 owner、嵌套模态角色与状态、外层
  默认/取消按钮目标、静态 Value 只读及旧按钮引用在 Hide/ShowDialog/Close 中的行为；
  v58 已实测六种 CSS 模式 × 两种尺寸的外部默认/取消关系、动作、折叠后的旧引用
  以及内层模态的两项按钮目标。v61 补齐标准标题栏三项关系、名称与动作，并实际
  外部读取角色、执行缩放/还原和最小化；其余可写属性和完整通知接收仍待验收。
  v23 已修正并实际检查部分通用状态、数据项名称和缓存旧页披露；
  v24/v25 补齐 CSS 隐藏与退出中的按钮、焦点及即时输入，v26/v27 修正 Tab 与退出后重用。
  v28 六种可见性模式在两种尺寸的物理按键、首页折叠预览及模态关闭/焦点已有实际证据；
  已修正并实测 AX 模态普通控件的多余选择/展开标记和嵌套返回后说明文字刷新。
  不能宣称完整无障碍或 VoiceOver 验收通过。
  v14/v21 旧树记录单独保留。
- Gallery 的 macOS 快捷键提示实际渲染与语言切换；v29 已复现系统菜单跟踪期间 Escape 误关闭，v30 实现修复并通过定向原生回归。v52 实际 PopUpMenu 已验证首个 Escape 只关闭菜单、模态仍可编辑，后续 Escape 才进入模态关闭；菜单栏的物理键盘路径仍待独立验收。
- 广泛富文档的内联方向、语言、装饰与格式恢复，私有系统字体描述符等价性，以及更广原生编辑命令与修饰键组合；v40 字体列表/系统 UI 字体/私有字体数据和加载刷新、v39 极长 NoWrap 与父级显式方向、v38 换行双向导航/混合字号/光标亲和性和 v37 国际分词/选词/锚点回归通过；物理按键与真实候选窗仍待验收。
- 其余普通文本绘制路径的彩色字体透明度回归；本轮透明度修复仅覆盖新增的富文本段落路径。
- 系统文本服务、听写和 Writing Tools；复杂文档及其余编辑器的长范围查询性能。
- RichTextBox 的 Undo/Redo 格式恢复；Terminal 的完整组合文本显示与提交验收。
- AppKit 材质的系统外观、减少透明度及失活状态合成；v55 已实际查看运行时四种材质切换与继续编辑。Application 显式关闭、会话结束和宿主生命周期仍需更广验收。
- 后续输入设备轮询与捕获、拖放/剪贴板、控件交互、媒体/浏览器及发布验证。
