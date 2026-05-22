// ===========================================================================
// jalium.native.demo.memprobe
//
// UI 框架「内存暴涨」排查最小复现 demo。
//
// 思路：把 native 渲染循环从 .NET / 托管 bitmap 缓存里完全剥离出来，单跑
// 「空白窗口」最小负载 —— 每帧只做 begin_draw / clear / end_draw，不画任何
// 内容。然后长时间循环，双轨采样：
//   轨 1：进程内存（Win32 GetProcessMemoryInfo 的 working set / private bytes）
//   轨 2：框架 telemetry（jalium_query_bitmap_stats / jalium_query_path_stats /
//          jalium_render_target_query_gpu_stats）
//
// 判定逻辑：如果连「什么都不画的空窗口」private bytes 都随帧数持续线性上涨，
// 说明泄漏在渲染循环 / 交换链 / 命令分配器本身，而不是某个内容缓存；此时
// telemetry 轨应保持平坦（没有 bitmap/path 缓存增长），据此可把责任面缩到
// 后端的 per-frame 资源回收路径。
//
// 退出时还会销毁 render target / context 再采一次，用来区分「稳态 per-frame
// 泄漏」与「teardown 泄漏」。所有采样同时落 CSV，便于画曲线。
// ===========================================================================

// 根 CMakeLists 已在命令行 -D 了 WIN32_LEAN_AND_MEAN / NOMINMAX，这里 ifndef
// 守护避免 C4005 宏重定义警告。
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <objbase.h>   // CoInitializeEx —— D3D12 后端的 WIC 工厂依赖 COM 套间
#include <psapi.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <cwchar>      // swprintf —— HUD 实时帧数文本
#include <string>
#include <vector>

#include "jalium_api.h"
#include "jalium_bitmap_stats.h"
#include "jalium_path_stats.h"

// d3d12 后端 dll 导出的初始化入口。链接 jalium.native.d3d12 的导入库会把该
// dll 拉进进程，其 DllMain 在 DLL_PROCESS_ATTACH 时就注册了 D3D12 后端工厂；
// 这里再显式调一次（幂等，原子 CAS 守护），同时强制产生链接引用、确保后端
// dll 不会因为“没被引用”而被裁掉。
extern "C" void jalium_d3d12_init();

// ---------------------------------------------------------------------------
// 运行参数
// ---------------------------------------------------------------------------
struct Options {
    uint64_t    frames        = 0;        // 0 = 跑到窗口关闭；否则到达帧数后退出
    uint64_t    interval      = 500;      // 每多少帧采样一次
    uint64_t    warmup        = 600;      // 基线前丢弃的预热帧（JIT / 首帧分配抖动）
    int32_t     width         = 1280;
    int32_t     height        = 800;
    bool        vsync         = true;     // 默认开 vsync：窗口平稳不撕裂/不闪；慢泄漏
                                          // 排查用 --no-vsync 不限帧率快速堆帧
    bool        hidden        = false;    // 默认显示窗口（这是个“桌面 demo”）
    bool        blank         = false;    // true = 纯 clear 不画任何内容（最极简隔离路径）
    bool        hud           = false;    // true = 叠加实时帧数文本
    bool        forceInvalidate = true;   // 默认每帧 full invalidation，保证真 present
    JaliumRenderingEngine engine = JALIUM_ENGINE_AUTO;
    std::string csvPath       = "memprobe.csv";

    // 回归验证开关：
    uint64_t    resizeEvery   = 0;        // >0：每 N 帧 jalium_render_target_resize 循环
                                          // 一组尺寸（压 swapchain ResizeBuffers 路径，
                                          // 覆盖 C 的 swapBufferCount_）
    uint64_t    switchEngineAt = 0;       // >0：到第 N 帧调 set_engine 把引擎切到
                                          // switchTo（压 B 的运行中懒建 Vello 路径）
    JaliumRenderingEngine switchTo = JALIUM_ENGINE_VELLO;
};

static void PrintUsage() {
    std::printf(
        "jalium.native.demo.memprobe — Hello World 窗口内存暴涨排查 demo\n"
        "\n"
        "默认画一行居中的 Hello World 文本 + 一条平滑移动的色条（每帧绘制列表\n"
        "唯一→保证真 present，文本/几何 cache key 恒定不会制造假泄漏），背景常量\n"
        "且默认开 vsync，窗口平稳不闪。\n"
        "\n"
        "用法: jalium.native.demo.memprobe [选项]\n"
        "  --frames N        总渲染帧数, 0=跑到关窗 (默认 0; 排查建议给个上界, 如 200000)\n"
        "  --interval N      每 N 帧采样一次 (默认 500)\n"
        "  --warmup N        基线前丢弃的预热帧 (默认 600)\n"
        "  --width N         窗口宽 (默认 1280)\n"
        "  --height N        窗口高 (默认 800)\n"
        "  --engine E        auto|vello|impeller (默认 auto)\n"
        "  --no-vsync        关闭 vsync 不限帧率 (慢泄漏排查用, 更快堆帧; 默认开 vsync)\n"
        "  --vsync           显式开启 vsync (默认即开)\n"
        "  --blank           纯 clear 不画任何内容 (最极简隔离路径, 靠 invalidation 出帧)\n"
        "  --hud             叠加实时帧数文本\n"
        "  --hidden          不显示窗口\n"
        "  --no-invalidate   不强制每帧 full invalidation (默认强制)\n"
        "  --resize N        每 N 帧循环 resize 渲染目标 (回归压 swapchain ResizeBuffers)\n"
        "  --switch-engine-at N  到第 N 帧热切引擎 (回归压运行中懒建 Vello)\n"
        "  --switch-to E     热切目标 vello|impeller (默认 vello, 配合 --switch-engine-at)\n"
        "  --csv PATH        CSV 输出路径 (默认 memprobe.csv)\n"
        "  --help            显示本帮助\n");
}

static bool ParseArgs(int argc, char** argv, Options& o) {
    for (int i = 1; i < argc; ++i) {
        std::string a = argv[i];
        auto next = [&](const char* name) -> const char* {
            if (i + 1 >= argc) {
                std::fprintf(stderr, "[memprobe] 选项 %s 缺少参数\n", name);
                std::exit(2);
            }
            return argv[++i];
        };
        if (a == "--help" || a == "-h" || a == "/?") { PrintUsage(); std::exit(0); }
        else if (a == "--frames")        o.frames   = std::strtoull(next("--frames"), nullptr, 10);
        else if (a == "--interval")      o.interval = std::strtoull(next("--interval"), nullptr, 10);
        else if (a == "--warmup")        o.warmup   = std::strtoull(next("--warmup"), nullptr, 10);
        else if (a == "--width")         o.width    = std::atoi(next("--width"));
        else if (a == "--height")        o.height   = std::atoi(next("--height"));
        else if (a == "--csv")           o.csvPath  = next("--csv");
        else if (a == "--vsync")         o.vsync    = true;
        else if (a == "--no-vsync")      o.vsync    = false;
        else if (a == "--hidden")        o.hidden   = true;
        else if (a == "--blank")         o.blank    = true;
        else if (a == "--hud")           o.hud      = true;
        else if (a == "--no-invalidate") o.forceInvalidate = false;
        else if (a == "--engine") {
            std::string e = next("--engine");
            if (e == "vello")         o.engine = JALIUM_ENGINE_VELLO;
            else if (e == "impeller") o.engine = JALIUM_ENGINE_IMPELLER;
            else if (e == "auto")     o.engine = JALIUM_ENGINE_AUTO;
            else { std::fprintf(stderr, "[memprobe] 未知 engine: %s\n", e.c_str()); return false; }
        }
        else if (a == "--resize")        o.resizeEvery = std::strtoull(next("--resize"), nullptr, 10);
        else if (a == "--switch-engine-at") o.switchEngineAt = std::strtoull(next("--switch-engine-at"), nullptr, 10);
        else if (a == "--switch-to") {
            std::string e = next("--switch-to");
            if (e == "vello")         o.switchTo = JALIUM_ENGINE_VELLO;
            else if (e == "impeller") o.switchTo = JALIUM_ENGINE_IMPELLER;
            else { std::fprintf(stderr, "[memprobe] 未知 switch-to: %s\n", e.c_str()); return false; }
        }
        else { std::fprintf(stderr, "[memprobe] 未知选项: %s\n", a.c_str()); PrintUsage(); return false; }
        if (o.interval == 0) o.interval = 1;
    }
    return true;
}

// ---------------------------------------------------------------------------
// 窗口
// ---------------------------------------------------------------------------
static bool g_running = true;

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_CLOSE:   DestroyWindow(hwnd); return 0;
        case WM_DESTROY: g_running = false; PostQuitMessage(0); return 0;
        default:         return DefWindowProcW(hwnd, msg, wp, lp);
    }
}

static HWND CreateProbeWindow(const Options& o) {
    WNDCLASSEXW wc{};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = GetModuleHandleW(nullptr);
    wc.hCursor       = LoadCursorW(nullptr, IDC_ARROW);
    wc.lpszClassName = L"JaliumMemProbeWindow";
    RegisterClassExW(&wc);

    RECT r{ 0, 0, o.width, o.height };
    AdjustWindowRect(&r, WS_OVERLAPPEDWINDOW, FALSE);
    HWND hwnd = CreateWindowExW(
        0, wc.lpszClassName, L"Jalium MemProbe — 空白窗口内存排查",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT,
        r.right - r.left, r.bottom - r.top,
        nullptr, nullptr, wc.hInstance, nullptr);

    if (hwnd && !o.hidden) {
        ShowWindow(hwnd, SW_SHOW);
        UpdateWindow(hwnd);
    }
    return hwnd;
}

// 抽空当前消息队列。返回 false 表示窗口已关闭、应退出循环。
static bool PumpMessages() {
    MSG msg;
    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
        if (msg.message == WM_QUIT) { g_running = false; return false; }
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    return g_running;
}

// ---------------------------------------------------------------------------
// 采样
// ---------------------------------------------------------------------------
struct Sample {
    uint64_t frame        = 0;
    double   elapsedSec   = 0.0;
    double   workingSetMB = 0.0;
    double   privateMB    = 0.0;
    // GPU telemetry
    int64_t  gpuTextureBytes = 0;
    int64_t  gpuPathBytes    = 0;
    int64_t  gpuGlyphBytes   = 0;
    int32_t  gpuTextureCount = 0;
    int32_t  gpuPathEntries  = 0;
    // bitmap telemetry
    uint64_t bmpUploadCount  = 0;
    uint64_t bmpUploadBytes  = 0;
    int64_t  bmpGpuResident  = 0;
    // path telemetry
    uint64_t pathFillMiss    = 0;
    uint64_t pathStrokeMiss  = 0;
    uint64_t pathGeomMiss    = 0;
    uint64_t pathCacheEvict  = 0;
};

static double BytesToMB(uint64_t b) { return (double)b / (1024.0 * 1024.0); }
static double BytesToMB(int64_t b)  { return (double)b / (1024.0 * 1024.0); }

static Sample TakeSample(JaliumRenderTarget* rt, uint64_t frame, double elapsedSec) {
    Sample s;
    s.frame      = frame;
    s.elapsedSec = elapsedSec;

    PROCESS_MEMORY_COUNTERS_EX pmc{};
    pmc.cb = sizeof(pmc);
    if (GetProcessMemoryInfo(GetCurrentProcess(),
                             reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc),
                             sizeof(pmc))) {
        s.workingSetMB = BytesToMB((uint64_t)pmc.WorkingSetSize);
        s.privateMB    = BytesToMB((uint64_t)pmc.PrivateUsage);
    }

    JaliumGpuStats gpu{};
    if (rt && jalium_render_target_query_gpu_stats(rt, &gpu) == JALIUM_OK) {
        s.gpuTextureBytes = gpu.textureBytes;
        s.gpuPathBytes    = gpu.pathBytes;
        s.gpuGlyphBytes   = gpu.glyphBytes;
        s.gpuTextureCount = gpu.textureCount;
        s.gpuPathEntries  = gpu.pathEntries;
    }

    JaliumBitmapStats bmp{};
    jalium_query_bitmap_stats(&bmp);
    s.bmpUploadCount = bmp.uploadCount;
    s.bmpUploadBytes = bmp.uploadBytes;
    s.bmpGpuResident = bmp.gpuResidentBytes;

    JaliumPathStats path{};
    jalium_query_path_stats(&path);
    s.pathFillMiss   = path.fillMisses;
    s.pathStrokeMiss = path.strokeMisses;
    s.pathGeomMiss   = path.geometryMisses;
    s.pathCacheEvict = path.cacheEvictions;

    return s;
}

// ---------------------------------------------------------------------------
// 启动期生命周期内存分解
//
// 「Hello World 窗口稳定却占 ~100MB」不是泄漏（曲线是平的），而是固定占用大。
// 现有 GPU telemetry 只追到 ~1MB 缓存，剩下几十 MB 是 D3D12 device/驱动、
// DXGI 后台缓冲、D2D/DWrite/WIC、Impeller/Vello 引擎缓冲、命令分配器/上传堆
// 等不在 telemetry 覆盖内的固定成本。按生命周期阶段打增量，定位 MB 花在哪步。
// ---------------------------------------------------------------------------
static void ReadProcMem(double& privMB, double& wsetMB) {
    PROCESS_MEMORY_COUNTERS_EX pmc{};
    pmc.cb = sizeof(pmc);
    privMB = wsetMB = 0.0;
    if (GetProcessMemoryInfo(GetCurrentProcess(),
                             reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc),
                             sizeof(pmc))) {
        wsetMB = BytesToMB((uint64_t)pmc.WorkingSetSize);
        privMB = BytesToMB((uint64_t)pmc.PrivateUsage);
    }
}

struct MemStages {
    double prevP = 0.0, prevW = 0.0;
    bool   first = true;
    // label 用纯 ASCII 以保证 %-26s 列对齐（UTF-8 中文按字节算宽度会错位）。
    void Mark(const char* label) {
        double p, w; ReadProcMem(p, w);
        if (first) {
            std::printf("  %-26s priv=%8.2f  wset=%8.2f   (baseline)\n",
                        label, p, w);
            first = false;
        } else {
            std::printf("  %-26s priv=%8.2f  wset=%8.2f   dPriv=%+8.2f dWset=%+8.2f\n",
                        label, p, w, p - prevP, w - prevW);
        }
        prevP = p; prevW = w;
        std::fflush(stdout);
    }
};

static void PrintSampleHeader() {
    std::printf("\n%8s %8s | %10s %10s | %9s %9s %9s | %9s %12s | %9s\n",
                "frame", "sec",
                "wset(MB)", "priv(MB)",
                "gpuTex(MB)", "gpuPath", "gpuGlyph",
                "bmpUpld#", "bmpResid(MB)",
                "pathMiss");
    std::printf("--------------------------------------------------------------"
                "--------------------------------------------------\n");
}

static void PrintSample(const Sample& s) {
    std::printf("%8llu %8.1f | %10.2f %10.2f | %9.2f %9lld %9.2f | %9llu %12.2f | %9llu\n",
                (unsigned long long)s.frame, s.elapsedSec,
                s.workingSetMB, s.privateMB,
                BytesToMB(s.gpuTextureBytes), (long long)s.gpuPathEntries,
                BytesToMB(s.gpuGlyphBytes),
                (unsigned long long)s.bmpUploadCount, BytesToMB(s.bmpGpuResident),
                (unsigned long long)(s.pathFillMiss + s.pathStrokeMiss + s.pathGeomMiss));
    std::fflush(stdout);
}

static void WriteCsvHeader(std::FILE* f) {
    std::fprintf(f,
        "frame,elapsedSec,workingSetMB,privateMB,"
        "gpuTextureBytes,gpuPathBytes,gpuGlyphBytes,gpuTextureCount,gpuPathEntries,"
        "bmpUploadCount,bmpUploadBytes,bmpGpuResidentBytes,"
        "pathFillMiss,pathStrokeMiss,pathGeomMiss,pathCacheEvict\n");
}

static void WriteCsvRow(std::FILE* f, const Sample& s) {
    std::fprintf(f,
        "%llu,%.3f,%.4f,%.4f,"
        "%lld,%lld,%lld,%d,%d,"
        "%llu,%llu,%lld,"
        "%llu,%llu,%llu,%llu\n",
        (unsigned long long)s.frame, s.elapsedSec, s.workingSetMB, s.privateMB,
        (long long)s.gpuTextureBytes, (long long)s.gpuPathBytes,
        (long long)s.gpuGlyphBytes, s.gpuTextureCount, s.gpuPathEntries,
        (unsigned long long)s.bmpUploadCount, (unsigned long long)s.bmpUploadBytes,
        (long long)s.bmpGpuResident,
        (unsigned long long)s.pathFillMiss, (unsigned long long)s.pathStrokeMiss,
        (unsigned long long)s.pathGeomMiss, (unsigned long long)s.pathCacheEvict);
    std::fflush(f);
}

// ---------------------------------------------------------------------------
// 增长判定
// ---------------------------------------------------------------------------
static void PrintVerdict(const std::vector<Sample>& samples,
                         uint64_t warmup,
                         const Sample& postTeardown) {
    std::printf("\n================ 内存增长判定 ================\n");

    // 基线 = 第一个 frame >= warmup 的采样
    const Sample* base = nullptr;
    for (const auto& s : samples) {
        if (s.frame >= warmup) { base = &s; break; }
    }
    if (!base || samples.empty()) {
        std::printf("采样不足，无法判定（拉长 --frames 或调小 --warmup/--interval 重试）。\n");
        return;
    }
    const Sample& last = samples.back();

    // post-warmup 全部样本进最小二乘回归。比"首尾差"稳健：单个预热尾点异常、
    // 或一次性 settle 台阶（字形图集填充 / D3D12 PSO / 着色器编译）不会被
    // 误判成线性泄漏 —— 用斜率 + R²(线性拟合优度) 双指标定性。
    std::vector<const Sample*> pts;
    for (const auto& s : samples)
        if (s.frame >= warmup) pts.push_back(&s);

    uint64_t span  = (last.frame > base->frame) ? (last.frame - base->frame) : 0;
    double   dPriv = last.privateMB - base->privateMB;
    double   dWset = last.workingSetMB - base->workingSetMB;

    std::printf("基线  @frame %-8llu  priv=%.2f MB  wset=%.2f MB\n",
                (unsigned long long)base->frame, base->privateMB, base->workingSetMB);
    std::printf("末样  @frame %-8llu  priv=%.2f MB  wset=%.2f MB\n",
                (unsigned long long)last.frame, last.privateMB, last.workingSetMB);
    std::printf("区间  %llu 帧, %.1f 秒, post-warmup 样本 %llu 个\n",
                (unsigned long long)span, last.elapsedSec - base->elapsedSec,
                (unsigned long long)pts.size());
    std::printf("增量  Δpriv=%+.2f MB   Δwset=%+.2f MB\n", dPriv, dWset);

    // telemetry 轨：内容恒定时应平坦；非零持续增长说明内容缓存意外膨胀
    std::printf("telemetry 末值: bmpUpload#=%llu bmpResid=%.2fMB gpuTex=%.2fMB "
                "gpuGlyph=%.2fMB gpuPathEntries=%d pathMiss=%llu\n",
                (unsigned long long)last.bmpUploadCount,
                BytesToMB(last.bmpGpuResident),
                BytesToMB(last.gpuTextureBytes),
                BytesToMB(last.gpuGlyphBytes),
                last.gpuPathEntries,
                (unsigned long long)(last.pathFillMiss + last.pathStrokeMiss +
                                     last.pathGeomMiss));

    // 最小二乘：y=priv(MB) 对 x=frame，slope→MB/1000帧，R²→线性度
    double ratePerK = 0.0, r2 = 0.0;
    const size_t   MIN_PTS  = 4;       // 至少 4 个 post-warmup 采样
    const uint64_t MIN_SPAN = 20000;   // 且区间至少 2 万帧才敢定性
    if (pts.size() >= 2) {
        double n = (double)pts.size(), sx = 0, sy = 0;
        for (auto* p : pts) { sx += (double)p->frame; sy += p->privateMB; }
        double mx = sx / n, my = sy / n, Sxx = 0, Syy = 0, Sxy = 0;
        for (auto* p : pts) {
            double dx = (double)p->frame - mx, dy = p->privateMB - my;
            Sxx += dx * dx; Syy += dy * dy; Sxy += dx * dy;
        }
        if (Sxx > 0)            ratePerK = (Sxy / Sxx) * 1000.0;
        if (Sxx > 0 && Syy > 0) r2 = (Sxy * Sxy) / (Sxx * Syy);
    }
    std::printf("回归  斜率 %.4f MB/1000帧   R²=%.3f\n", ratePerK, r2);

    if (pts.size() < MIN_PTS || span < MIN_SPAN) {
        std::printf("判定：[证据不足] 样本/区间过小（需 ≥%llu 个 post-warmup 采样且区间 "
                    "≥%llu 帧）。小样本下字形图集/PSO/着色器等一次性 settle 极易被误判为"
                    "泄漏 —— 加大 --frames、把 --warmup 设到一次性开销之后再复测。\n",
                    (unsigned long long)MIN_PTS, (unsigned long long)MIN_SPAN);
    } else if (ratePerK <= 0.05) {
        std::printf("判定：[OK] 进程内存稳定，未见 per-frame 泄漏。\n");
    } else if (r2 < 0.80) {
        std::printf("判定：[噪声/非线性] 斜率为正但 R²=%.2f 偏低 —— 更像一次性台阶或"
                    " 分配器/GC 抖动而非持续泄漏，延长 run 复测确认。\n", r2);
    } else if (ratePerK <= 0.5) {
        std::printf("判定：[SUSPECT] 干净线性增长(R²=%.2f)但斜率温和，拉到百万帧级"
                    "复测定性。\n", r2);
    } else {
        std::printf("判定：[LEAK] 干净线性增长(R²=%.2f, %.3f MB/1000帧) —— 泄漏在"
                    "渲染循环/交换链/命令分配器本身，优先查 begin/end_draw per-frame "
                    "资源回收路径。\n", r2, ratePerK);
    }

    // teardown 轨
    std::printf("--- teardown 后 ---\n");
    std::printf("销毁 rt+ctx 后  priv=%.2f MB  wset=%.2f MB  (相对末样 Δpriv=%+.2f MB)\n",
                postTeardown.privateMB, postTeardown.workingSetMB,
                postTeardown.privateMB - last.privateMB);
    if (postTeardown.privateMB - last.privateMB > -1.0 && ratePerK > 0.5)
        std::printf("提示：销毁后内存几乎没回落，且循环期在涨 —— 资源很可能没在"
                    " destroy 路径释放（teardown 泄漏 / 全局墓地未清）。\n");
    std::printf("=============================================\n");
}

// COM 套间 RAII。D3D12 后端 Initialize() 会建 WIC 工厂
// (CoCreateInstance(CLSID_WICImagingFactory))，线程未初始化 COM 会拿到
// CO_E_NOTINITIALIZED → 后端初始化失败 → render target 创建返回 null。
// 托管 Window 宿主本就在 STA 线程且已初始化 COM；独立 demo 需自己负责。
struct ComApartment {
    bool ok = false;
    ComApartment() {
        HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        ok = SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE; // 已初始化也算可用
    }
    ~ComApartment() { if (ok) CoUninitialize(); }
};

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------
int main(int argc, char** argv) {
    // 控制台按 UTF-8 输出，中文诊断信息不乱码（源/执行字符集已由 /utf-8 统一）。
    SetConsoleOutputCP(CP_UTF8);

    std::printf("[memprobe] 启动期内存分阶段分解（定位固定占用花在哪步）:\n");
    MemStages stages;
    stages.Mark("process start");

    ComApartment com;
    stages.Mark("after CoInitialize(COM)");
    if (!com.ok) {
        std::fprintf(stderr, "[memprobe] CoInitializeEx 失败，D3D12 后端将无法创建\n");
        return 1;
    }

    Options o;
    if (!ParseArgs(argc, argv, o)) return 2;

    std::printf("[memprobe] %s 内存排查 — 后端=D3D12 引擎=%s vsync=%s "
                "invalidate=%s hud=%s\n",
                o.blank ? "纯 clear 空窗口" : "Hello World 窗口",
                o.engine == JALIUM_ENGINE_VELLO ? "vello"
                  : o.engine == JALIUM_ENGINE_IMPELLER ? "impeller" : "auto",
                o.vsync ? "on" : "off",
                o.forceInvalidate ? "on" : "off",
                o.hud ? "on" : "off");
    std::printf("[memprobe] frames=%llu interval=%llu warmup=%llu %dx%d csv=%s\n",
                (unsigned long long)o.frames, (unsigned long long)o.interval,
                (unsigned long long)o.warmup, o.width, o.height, o.csvPath.c_str());

    HWND hwnd = CreateProbeWindow(o);
    if (!hwnd) {
        std::fprintf(stderr, "[memprobe] CreateWindow 失败 (err=%lu)\n", GetLastError());
        return 1;
    }
    stages.Mark("after CreateWindow");

    // 1) 注册并加载 D3D12 后端
    jalium_d3d12_init();
    stages.Mark("after d3d12_init(loadDll)");

    // 2) 创建 context
    JaliumContext* ctx = jalium_context_create(JALIUM_BACKEND_D3D12);
    if (!ctx) {
        std::fprintf(stderr, "[memprobe] jalium_context_create(D3D12) 失败\n");
        return 1;
    }
    stages.Mark("after context_create");

    JaliumAdapterInfo ai{};
    if (jalium_context_get_adapter_info(ctx, &ai) == JALIUM_OK) {
        std::wprintf(L"[memprobe] GPU: %ls  (VRAM %.0f MB)\n",
                     ai.name, (double)ai.dedicatedVideoMemory / (1024.0 * 1024.0));
    }

    // 3) 创建 render target
    JaliumRenderTarget* rt = jalium_render_target_create_for_hwnd(
        ctx, (void*)hwnd, o.width, o.height);
    if (!rt) {
        const wchar_t* err = jalium_context_get_error_message(ctx);
        std::fprintf(stderr, "[memprobe] create_for_hwnd 失败: %ls\n",
                     err ? err : L"(无错误信息)");
        jalium_context_destroy(ctx);
        return 1;
    }
    stages.Mark("after render_target");

    jalium_render_target_set_dpi(rt, 96.0f, 96.0f);
    jalium_render_target_set_vsync(rt, o.vsync ? 1 : 0);
    if (o.engine != JALIUM_ENGINE_AUTO)
        jalium_render_target_set_engine(rt, o.engine);

    // ---- Hello World 内容资源（循环外创建一次，cache key 恒定）----
    // 标题串恒定 → 文本测量/布局/字形 cache 命中后保持平坦，不会因“每帧不同
    // 字符串”把文本缓存撑大而制造假泄漏（见 MEMORY: vulkan_text_cache_rebuild）。
    const wchar_t* kTitle = L"Hello World — Jalium MemProbe";
    JaliumTextFormat* titleFmt = jalium_text_format_create(ctx, L"Segoe UI", 30.0f, 600, 0);
    if (titleFmt) {
        jalium_text_format_set_alignment(titleFmt, JALIUM_TEXT_ALIGN_CENTER);
        jalium_text_format_set_paragraph_alignment(titleFmt, JALIUM_PARAGRAPH_ALIGN_CENTER);
    }
    JaliumTextFormat* hudFmt = o.hud
        ? jalium_text_format_create(ctx, L"Consolas", 15.0f, 400, 0) : nullptr;

    JaliumBrush* textBrush   = jalium_brush_create_solid(ctx, 0.92f, 0.94f, 0.98f, 1.0f);
    JaliumBrush* accentBrush = jalium_brush_create_solid(ctx, 0.20f, 0.65f, 0.85f, 1.0f);
    stages.Mark("after content resources");

    // CSV
    std::FILE* csv = std::fopen(o.csvPath.c_str(), "wb");
    if (csv) WriteCsvHeader(csv);
    else std::fprintf(stderr, "[memprobe] 无法打开 CSV %s，仅控制台输出\n",
                      o.csvPath.c_str());

    LARGE_INTEGER freq, t0, tnow;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&t0);

    std::vector<Sample> samples;
    samples.reserve(1024);

    uint64_t frame = 0;
    while (g_running) {
        if (!PumpMessages()) break;
        if (o.frames != 0 && frame >= o.frames) break;

        JaliumResult br = jalium_render_target_begin_draw(rt);
        if (br != JALIUM_OK) {
            std::fprintf(stderr, "[memprobe] begin_draw 返回 %d，停止 (设备丢失?)\n", br);
            break;
        }

        // 背景恒定深色（不再每帧改 clear 颜色 → 不再有亮度脉冲闪烁）。
        jalium_render_target_clear(rt, 0.07f, 0.08f, 0.11f, 1.0f);

        if (!o.blank) {
            // Hello World 文本（恒定串，居中铺满窗口）。
            if (titleFmt)
                jalium_draw_text(rt, kTitle, (uint32_t)wcslen(kTitle), titleFmt,
                                 0.0f, 0.0f, (float)o.width, (float)o.height, textBrush);

            // 平滑移动的色条：位置每帧不同 → 绘制列表每帧唯一 → 后端的
            // replay / no-op 短路无法跳过本帧，保证真 present，排查依然成立；
            // 且这是基本图元(fill_rect)，不进 path/bitmap 缓存，不污染 telemetry。
            float barW = 140.0f;
            float span = (float)o.width + barW;
            float bx   = (float)(frame % (uint64_t)span) - barW;
            float by   = (float)o.height * 0.5f + 34.0f;
            jalium_draw_fill_rectangle(rt, bx, by, barW, 5.0f, accentBrush);

            if (o.hud && hudFmt) {
                // 只放帧号：每帧变化已足以保证唯一帧，且不在热路径里调 QPC。
                wchar_t buf[64];
                swprintf(buf, 64, L"frame %llu", (unsigned long long)frame);
                jalium_draw_text(rt, buf, (uint32_t)wcslen(buf), hudFmt,
                                 16.0f, 12.0f, (float)o.width, 28.0f, textBrush);
            }
        }

        if (o.forceInvalidate)
            jalium_render_target_set_full_invalidation(rt);

        JaliumResult er = jalium_render_target_end_draw(rt);
        if (er != JALIUM_OK) {
            std::fprintf(stderr, "[memprobe] end_draw 返回 %d，停止 (设备丢失?)\n", er);
            break;
        }

        ++frame;

        if (frame == 1) {
            stages.Mark("after first frame");
            std::printf("[memprobe] 进入稳态，开始周期采样:\n");
            PrintSampleHeader();
        }

        if (frame % o.interval == 0) {
            QueryPerformanceCounter(&tnow);
            double sec = (double)(tnow.QuadPart - t0.QuadPart) / (double)freq.QuadPart;
            Sample s = TakeSample(rt, frame, sec);
            PrintSample(s);
            if (csv) WriteCsvRow(csv, s);
            samples.push_back(s);
        }

        // ---- 回归：运行中热切引擎（压 B 的懒建 Vello / 引擎切换路径）----
        if (o.switchEngineAt && frame == o.switchEngineAt) {
            const char* to = o.switchTo == JALIUM_ENGINE_VELLO ? "vello"
                           : o.switchTo == JALIUM_ENGINE_IMPELLER ? "impeller" : "auto";
            std::printf("[memprobe] @frame %llu 热切引擎 -> %s\n",
                        (unsigned long long)frame, to);
            JaliumResult sr = jalium_render_target_set_engine(rt, o.switchTo);
            std::printf("[memprobe]   set_engine 返回 %d\n", (int)sr);
            stages.Mark("after switch engine");
        }

        // ---- 回归：周期 resize（压 C 的 swapchain ResizeBuffers/swapBufferCount_）----
        if (o.resizeEvery && frame % o.resizeEvery == 0) {
            static const int kSizes[][2] = {
                {1024, 640}, {1600, 900}, {800, 600}, {1280, 800}, {1920, 1080}
            };
            static int sizeIdx = 0;
            sizeIdx = (sizeIdx + 1) % (int)(sizeof(kSizes) / sizeof(kSizes[0]));
            int nw = kSizes[sizeIdx][0], nh = kSizes[sizeIdx][1];
            JaliumResult rr = jalium_render_target_resize(rt, nw, nh);
            if (rr != JALIUM_OK) {
                std::fprintf(stderr, "[memprobe] @frame %llu resize(%dx%d) 失败 %d，停止\n",
                             (unsigned long long)frame, nw, nh, (int)rr);
                break;
            }
            o.width = nw; o.height = nh;   // 让 Hello World 内容跟随新尺寸布局
        }
    }

    // ---- teardown：销毁 rt + ctx，再采一次区分稳态/teardown 泄漏 ----
    QueryPerformanceCounter(&tnow);
    double endSec = (double)(tnow.QuadPart - t0.QuadPart) / (double)freq.QuadPart;

    std::printf("[memprobe] teardown 内存分阶段分解（看每步各释放多少）:\n");
    stages.Mark("before teardown");

    if (titleFmt)    jalium_text_format_destroy(titleFmt);
    if (hudFmt)      jalium_text_format_destroy(hudFmt);
    if (textBrush)   jalium_brush_destroy(textBrush);
    if (accentBrush) jalium_brush_destroy(accentBrush);
    stages.Mark("after destroy resources");

    jalium_render_target_destroy(rt);
    stages.Mark("after destroy render_target");
    jalium_context_destroy(ctx);
    stages.Mark("after destroy context");

    // 给 OS / 驱动一点时间真正回收（fence-gated 墓地、deferred destroy）
    Sleep(300);
    stages.Mark("after 300ms settle");
    Sample post = TakeSample(nullptr, frame, endSec);
    if (csv) {
        WriteCsvRow(csv, post);
        std::fclose(csv);
    }

    PrintVerdict(samples, o.warmup, post);

    std::printf("\n[memprobe] 结束：共 %llu 帧，CSV=%s\n",
                (unsigned long long)frame, o.csvPath.c_str());
    return 0;
}
