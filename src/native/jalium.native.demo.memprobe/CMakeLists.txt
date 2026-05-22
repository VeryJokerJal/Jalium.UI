project(jalium.native.demo.memprobe LANGUAGES CXX)

# ---------------------------------------------------------------------------
# 内存暴涨排查 demo（控制台 + 一个真实 Win32 窗口的 D3D12 swapchain）。
#
# 目的：把 native 渲染循环从 .NET / 托管 bitmap 缓存里完全隔离出来，单跑
# 「空白窗口 begin/clear/end」最小负载，长时间循环并双轨采样进程内存与
# 框架 telemetry，用来判定内存是否在 native 渲染循环 / 交换链 / 命令分配器
# 本身泄漏（而不是某个内容缓存）。
#
# 这是 console 子系统（保留 stdout 打印日志），程序内部自己 CreateWindow
# 出一个顶层窗口给 D3D12 交换链用。
# ---------------------------------------------------------------------------

add_executable(${PROJECT_NAME} src/main.cpp)

target_include_directories(${PROJECT_NAME}
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../jalium.native.core/include
)

# 源文件是 UTF-8（含中文注释）。MSVC 默认按系统代码页(936/GBK)解析源码，
# 会把 UTF-8 中文的多字节序列误读、吞掉换行/花括号导致全文解析崩坏
# （表现为 C4819 + 一连串 C2039/C2059）。/utf-8 同时设定源与执行字符集为
# UTF-8，是这里的根因修复，不是临时绕过。
if(MSVC)
    target_compile_options(${PROJECT_NAME} PRIVATE /utf-8)
endif()

# jalium.native.core    —— jalium_context_* / render_target_* / query_*_stats 的 C ABI
# jalium.native.d3d12   —— 导出 jalium_d3d12_init()，链接它会把 d3d12 dll 拉进进程，
#                          DllMain 自动注册 D3D12 后端工厂
# psapi                 —— GetProcessMemoryInfo（working set / private bytes）
# user32                —— RegisterClass / CreateWindowEx / 消息泵
target_link_libraries(${PROJECT_NAME}
    PRIVATE
        jalium.native.core
        jalium.native.d3d12
        user32
        psapi
        ole32      # CoInitializeEx / CoUninitialize（D3D12 后端 WIC 工厂依赖 COM）
)

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "jalium.native.demo.memprobe"
    DEBUG_POSTFIX ""
)

# 继承根 CMakeLists 的 CMAKE_RUNTIME_OUTPUT_DIRECTORY：可执行文件落到
# src/native/bin/native/<Config>/，与 jalium.native.*.dll 同目录，运行时
# 无需额外拷贝即可解析依赖 DLL。
