# Jalium.UI

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | **한국어**

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/VeryJokerJal/Jalium.UI)

Jalium.UI는 .NET 10을 위한 GPU 가속 크로스 플랫폼 UI 프레임워크입니다.
WPF 스타일의 객체 모델, Razor 구문 확장을 갖춘 JALXAML 마크업,
그리고 플랫폼 네이티브 렌더링 백엔드(DirectX 12, Vulkan, Metal, Software)를 결합합니다.

## 프로젝트 상태

- 활발히 개발 중 — v26.10.7 (마이너 버전 간에 API가 변경될 수 있음)
- 주요 대상: Windows 10/11 (x64, ARM64)
- 크로스 플랫폼: Android (arm64-v8a, x86_64), Linux (X11/Wayland, Vulkan 또는 소프트웨어), macOS (Metal)
- 런타임 대상: .NET 10 (`net10.0-windows`, `net10.0-android`, `net10.0`)
- 렌더링: DirectX 12 (Windows), Vulkan (Linux/Android), Metal (macOS), 소프트웨어 폴백

## Jalium.UI를 선택하는 이유

- ClearType 서브픽셀 텍스트 렌더링을 갖춘 GPU 네이티브 렌더링 파이프라인
- 익숙한 프로그래밍 모델 (`DependencyObject`, `UIElement`, 패널, 템플릿, 리소스)
- Razor 구문 확장을 갖춘 JALXAML 마크업 (`@Path`, `@(expr)`, `@{ ... }`, `@if`/`@for`/`@foreach`, `@section`/`@RenderSection`)
- 풍부한 컨트롤 라이브러리: Charts, Ribbon, Docking, InkCanvas, WebView, Terminal, MediaElement, MapView, PropertyGrid, WindowsFormsHost를 포함한 100개 이상의 컨트롤
- 개발자 경험: JALXAML 핫 리로드(라이브 비주얼 트리 패칭)와 함께 선택적으로 활성화하는(opt-in) 내장 DevTools 인스펙터 및 디버그 HUD
- NuGet을 통한 빌드 타임 도구 (`Jalium.UI.Build`, `Jalium.UI.Xaml.SourceGenerator`)
- 일급(first-class) Generic Host 통합 — `AppBuilder`는 DI, 구성, 옵션, 로깅 및 메트릭을 위해 `IHostApplicationBuilder`(`Microsoft.Extensions.Hosting`)를 구현하며, 여기에 더해 Jalium MVVM 뷰/뷰모델 연결을 제공합니다
- Windows UIA 및 Linux AT-SPI 브리지를 갖춘 오토메이션 피어(automation peer) 접근성 지원
- 시각 효과: 리퀴드 글래스(liquid glass), 배경 블러(backdrop blur), 아크릴(acrylic), 마이카(mica), 전환 셰이더(transition shader), 애니메이션 비트맵 (GIF / APNG / 애니메이션 WebP)
- `MediaElement` / `NativeVideoSurface`를 통한 네이티브 GPU 비디오 (D3D12 / Vulkan 서피스, 단계적 롤아웃)
- 완전한 멀티터치 입력 — 물리적 관성을 갖춘 조작(manipulation), 제스처 인식, 실시간 스타일러스 미리 보기
- 자소 클러스터(grapheme-cluster)를 인식하는 텍스트 편집 (UAX#29) — 이모지, ZWJ 시퀀스, 피부색 수정자(skin-tone modifier), 국기는 절대 분리되지 않음
- WSOLA 음높이 보존 타임 스트레칭(pitch-preserving time-stretching)을 갖춘 네이티브 오디오 파이프라인 (miniaudio + dr_libs / minimp3)

## 프레임워크 구성

### 관리 패키지(Managed Packages)

| 패키지 | 책임 |
| --- | --- |
| `Jalium.UI.Core` | 종속성 속성 시스템, 비주얼 트리, 레이아웃, 라우팅 이벤트, 바인딩, 애니메이션 |
| `Jalium.UI.Media` | 브러시, 지오메트리, 드로잉 프리미티브, 텍스트 서식, 이미징, 비디오, 시각 효과 |
| `Jalium.UI.Input` | 마우스, 키보드, 터치, 스타일러스 입력 추상화 및 라우팅 |
| `Jalium.UI.Interop` | 관리/네이티브 브리지, P/Invoke, 런타임 네이티브 종속성 패키징 |
| `Jalium.UI.Gpu` | GPU 리소스 관리, 렌더 그래프, 머티리얼, 셰이더, 백엔드 추상화 |
| `Jalium.UI.Controls` | 컨트롤, 패널, 템플릿, 윈도잉, 테마, 도킹, 차트, 호스팅 |
| `Jalium.UI.Xaml` | JALXAML 파싱/로드 파이프라인, Razor 구문 지원, 핫 리로드, 마크업 서비스 |
| `Jalium.UI.Build` | JALXAML 컴파일 워크플로를 위한 MSBuild 작업 및 빌드 자산 |
| `Jalium.UI.Xaml.SourceGenerator` | XAML/코드비하인드 통합을 위한 Roslyn 소스 생성기 |
| `Jalium.UI` | 전체 프레임워크 스택을 참조하는 메타 패키지 |

> `Jalium.UI.Compiler`는 독립 실행형 `jalxamlc` 컴파일러 실행 파일을 빌드합니다. 이것은
> 소스에서 소비되는 빌드 도구이며 NuGet 패키지가 아닙니다(`dotnet add package`가 적용되지 않음).

### 네이티브 모듈(Native Modules)

| 모듈 | 플랫폼 | 책임 |
| --- | --- | --- |
| `jalium.native.core` | 전체 | 네이티브 코어 런타임, 백엔드 레지스트리, 컨텍스트 관리 |
| `jalium.native.d3d12` | Windows | DirectX 12 렌더 타깃 및 Vello GPU 파이프라인 |
| `jalium.native.vulkan` | Linux, Android | Vulkan 렌더 백엔드 |
| `jalium.native.metal` | macOS | Metal 렌더 백엔드 |
| `jalium.native.software` | 전체 | CPU 기반 소프트웨어 렌더링 폴백 |
| `jalium.native.platform` | 전체 | 플랫폼 추상화 (윈도우, 입력, 이벤트) |
| `jalium.native.text` | Linux, Android, macOS | 자체 개발 텍스트 엔진 (sfnt/cmap/glyf/CFF + OT shaper; Linux에서는 fontconfig를 폰트 검색에만 사용) |
| `jalium.native.browser` | Windows | WebView2 브라우저 통합 |
| `jalium.native.media.core` | 전체 | 크로스 플랫폼 미디어 C ABI + 공유 오디오 (miniaudio / dr_libs / minimp3 / stb_vorbis) |
| `jalium.native.media.windows` | Windows | Media Foundation 비디오 / 카메라 / AAC 디코더 + WIC 이미징 |
| `jalium.native.media.android` | Android | NDK를 통한 Android 이미지 / 비디오 / 카메라 디코더 + YUV SIMD (NEON) |
| `jalium.native.aot` | 전체 | NativeAOT 애그리게이터 (미디어, 텍스트, 백엔드를 하드 링크) |

### 플랫폼 패키지(Platform Packages)

| 패키지 | 대상 |
| --- | --- |
| `Jalium.UI.Desktop` | `net10.0-windows` — RID별 네이티브 DLL을 포함한 데스크톱 배포판 (win-x64 / win-arm64) |
| `Jalium.UI.Android` | `net10.0-android` — 네이티브 .so 라이브러리를 포함한 Android 배포판 |
| `Jalium.UI.Linux` | `net10.0` — Linux 데스크톱 배포판 (Wayland/X11, Vulkan + 소프트웨어 렌더링; linux-x64 / linux-arm64 / linux-musl-x64 / linux-musl-arm64 RID 레이아웃 예약) |

여기에 표시된 RID는 패키지 레이아웃이며 4개 RID와 NativeAOT가 모두 릴리스 검증을 마쳤다는 의미가 아닙니다. 현재 증거와 남은 경계는 [Linux 지원 상태 매트릭스](docs/linux-parity-status.md)를 참조하세요.

## 기능 개요

### 레이아웃 및 비주얼 트리

- 코어 패널: `Grid`, `StackPanel`, `Canvas`, `DockPanel`, `WrapPanel`, `UniformGrid`
- 가상화(Virtualization): `VirtualizingStackPanel`, `VirtualizingWrapPanel`, DataGrid 프레젠터/패널
- 도킹: `DockLayout`, `DockSplitPanel`, `DockTabPanel`, `Split`
- 윈도우 수준 레이아웃 호스트, 오버레이 레이어, 제목 표시줄 구성, 크롬(chrome) 통합

### 컨트롤

- **입력**: `Button`, `TextBox`, `PasswordBox`, `NumberBox`, `AutoCompleteBox`, `ComboBox`, `Slider`, `RangeSlider`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `SplitButton`
- **선택기(Pickers)**: `ColorPicker`, `DatePicker`, `TimePicker`, `Calendar`
- **데이터**: `TreeView`, `DataGrid`, `TreeDataGrid`, `ListBox`, `ListView`, `PropertyGrid`, `JsonTreeViewer`
- **내비게이션 및 바**: `NavigationView`, `TabControl`, `Ribbon`, `CommandBar`, `MenuBar`, `ToolBar`, `StatusBar`, `GroupBox`, `Expander`, `InfoBar`
- **문서**: `DocumentViewer`, `FlowDocumentReader`, `FlowDocumentScrollViewer`, `FlowDocumentPageViewer`, `Markdown`
- **차트**: `BarChart`, `LineChart`, `PieChart`, `ScatterPlot`, `CandlestickChart`, `GaugeChart`, `Heatmap`, `GanttChart`, `NetworkGraph`, `SankeyDiagram`, `TreeMap`, `Sparkline`, 그리고 `FlowchartDiagram` / `MermaidDiagram` — 범주형 / DateTime / 로그 / 숫자 축, 범례 및 툴팁
- **미디어**: `MediaElement`, `CameraView`
- **지도**: `MapView`, `MiniMap`, `GeographicHeatmap`
- **리치**: `InkCanvas`, `WebView`/`WebBrowser`, `EditControl`, `RichTextBox`, `QRCode` (자체 호스팅 인코더), `TitleBar`, `Terminal`, `SwipeControl`
- **개발자 도구**: `DiffViewer`, `HexEditor`
- **상호 운용**: `WindowsFormsHost` (`net10.0-windows`에서 `System.Windows.Forms` 컨트롤 호스팅)
- **인쇄**: Windows에서는 Win32, Linux에서는 PDF + xdg-desktop-portal을 사용하는 `PrintDialog`
- **알림**: 토스트 스타일 알림 시스템

### 텍스트 편집

- `TextBox`, `PasswordBox`, `EditControl`, `RichTextBox`, `TextBlock`, `Label`, `Markdown`,
  `Terminal`, `TextEffectPresenter` 전반에 걸친 자소 클러스터 인식 캐럿, 선택, 단어 분리 및 삭제 —
  이모지(ZWJ / 피부색 / 국기 / 결합 문자(combining mark))는 클러스터 중간에서 절대 분리되지 않습니다
  (`StringInfo.GetTextElementEnumerator`를 통한 UAX#29).
- 비밀번호 / 읽기 전용 필드에 대한 IME 억제 — 입력 조합(composing input)이 보호된
  편집기 상태를 변경할 수 없도록 합니다.
- BOM 자동 감지를 갖춘 다중 인코딩 파일 IO (`TextBox`, `EditControl`, `Markdown`,
  `RichTextBox`의 `LoadFromFile` / `SaveToFile`). `CodePagesEncodingProvider`가
  `ModuleInitializer`를 통해 등록되어 GBK / Shift-JIS / 기타 비기본 코드
  페이지가 별도 설정 없이 동작합니다.
- `Terminal`은 `Encoding` 속성과 상태 저장(stateful) `Decoder`를 추가하여 출력 바이트가
  멀티바이트 시퀀스를 분리하지 않고 사용자의 터미널 인코딩으로 디코딩됩니다.
- WPF에 맞춰 정렬된 `TextBox.CharacterCasing` / `MinLines` / `MaxLines`, `ComboBox.StaysOpenOnEdit`.

### 텍스트 렌더링

- 듀얼 소스 블렌딩(dual-source blending)을 갖춘 ClearType 서브픽셀 텍스트 렌더링.
- CPU 래스터화 폴백 경로.
- 자체 개발 OpenType 엔진을 통한 크로스 플랫폼 텍스트 셰이핑 (Linux/Android) — FreeType/HarfBuzz 런타임 의존성 없음; fontconfig는 폰트 검색에만 사용.
- 요소별 `TextOptions.{TextRenderingMode, TextFormattingMode, TextHintingMode}`
  상속 가능 첨부 속성(attached property) — 값이 `FormattedText` → 네이티브
  `JaliumTextFormat`을 거쳐 래스터라이저까지 전달됩니다:
  - D3D12: `GlyphKey`에 `(aaMode, hintingMode)`가 포함됩니다. `RasterizeGlyph`는 `key.mode`를 따릅니다.
  - Vulkan / Windows: `LOGFONT.lfQuality`가 bilevel / smoothed / ClearType 사이를 전환하며,
    폰트 캐시 + 텍스트 캐시 + GDI 폰트 풀 키 모두 `fontQuality`를 포함합니다.
- 프로세스 전역 렌더링 모드 재정의 + 컬러 이모지 래스터화.

### 입력 파이프라인

- 히트 테스트(hit testing)를 갖춘 포인터 및 키보드 라우팅
- 물리적 관성을 갖춘 완전한 멀티터치 조작 (팬 / 핀치 / 회전)
- 컨트롤 카탈로그 전반에 걸쳐 네이티브로 연결된 `GestureRecognizer` (탭 / 스와이프 / 핀치)
- 실시간 스타일러스(RTS) 백그라운드 잉크 미리 보기
- 스크롤 및 조작(manipulation) 이벤트 처리

### GPU 렌더링 및 효과

- Vello GPU 컴퓨트 파이프라인(path, clip, tile 스테이지)을 갖춘 DirectX 12
- 배경 효과: 블러, 아크릴, 마이카, 서리 유리(frosted glass)
- 굴절(refraction), 색수차(chromatic aberration)를 갖춘 리퀴드 글래스
- 전환 셰이더 및 요소 효과 (블러, 드롭 섀도)
- 애니메이션 비트맵: GIF, APNG, 애니메이션 WebP
- 네이티브 GPU 비디오 서피스 (`NativeVideoSurface`): CPU BGRA8 스테이징과 함께 외부
  GPU 리소스 가져오기 (D3D11 공유 텍스처 / Vulkan 외부 `VkImage` / Android
  `AHardwareBuffer` / Apple `IOSurface`) — API 표면은 이미 갖추어져 있으며 백엔드별 업로드
  경로는 점진적으로 채워지고 있습니다.
- HLSL을 통한 사용자 정의 셰이더 지원
- 대형 이미지 그리드를 위한 비트맵 다운스케일 캐시 + 가상화 랩 패널(virtualizing wrap panel)
- DevTools Perf 탭에 표시되는 통합 path/bitmap 텔레메트리 C ABI

### 호스팅 / DI / 구성

- 표준 `Microsoft.Extensions.Hosting` 스택(10.0.3) 기반으로 구축됩니다. `AppBuilder`는
  `IHostApplicationBuilder`를 구현하며(`HostApplicationBuilder`를 래핑), 따라서
  익숙한 `IServiceCollection`, `IConfiguration`, `ILoggingBuilder`,
  `IHostEnvironment`, `Meter` 표면을 앱 시작 중에 사용할 수 있습니다.
- Jalium 고유의 접착 코드는 `Jalium.UI.Hosting`에 위치합니다: MVVM 뷰/뷰모델
  등록 (`AddView<TView, TViewModel>`, `AddViewsAndViewModels`),
  `JaliumRuntimeOptions`에 대한 `ConfigureJalium` 옵션 바인딩, 프레임 타임 메트릭
  (`UseJaliumMetrics` / `JaliumMeter`), 그리고 선택적으로 활성화하는(opt-in) 개발자 도구
  (`UseDevTools` / `UseDebugHud`).
- `EnableConfigurationBindingGenerator`를 통한 트리밍/AOT 안전 구성 바인딩. 리플렉션
  기반 뷰 검색 오버로드는 `[RequiresUnreferencedCode]`로 주석 처리됩니다.

### 오디오 파이프라인

- WAV / FLAC / MP3 / Vorbis (크로스 플랫폼) 및 AAC (Media Foundation을 통한 Windows)를
  포괄하는 네이티브 `MiniAudioDevice` 재생 + `NativeAudioDecoder`.
- 음높이 보존 타임 스트레칭을 위한 `WsolaSpeedProcessor`를 갖춘
  관리형 `IAudioProcessor` 체인.
- 오디오 TU(translation unit)는 각 플랫폼별 `jalium.native.media.*` 라이브러리로 컴파일되어
  심볼이 관리형 P/Invoke가 로드하는 동일한 DLL에 배치됩니다.

### 3D 애니메이션 타입

- `Point3DAnimation`, `Rotation3DAnimation`, `Size3DAnimation`,
  `Vector3DAnimation`이 WPF 애니메이션 타입 세트를 완성합니다.

### 접근성

- 코어 및 전문 컨트롤을 위한 오토메이션 피어 (Windows UIA 및 Linux AT-SPI를 통해 제공)
- Chart, DiffViewer, HexEditor, JsonTreeViewer, Map, PropertyGrid 오토메이션
- `Window.ResolveCursor`는 비활성화된 요소에 대해 표준 화살표를 반환하여
  호버 상태가 활성화된 컨트롤과 혼동되지 않도록 합니다.

### 개발자 도구

- 선택적으로 활성화하는(opt-in) DevTools 창 (`app.UseDevTools()`, F12 / Ctrl+Shift+C 요소 선택기):
  가상화 및 인라인 속성 편집을 갖춘 비주얼 / 논리 / 평면 트리 인스펙터, 그리고 Layout, Events,
  Bindings, Resources, Perf, UIA, Tools
  (자 / 색상 선택기 / 오버드로 및 더티 영역 오버레이 / 스크린샷 내보내기)와
  라이브 REPL 탭.
- 화면 표시 디버그 HUD (`app.UseDebugHud()`, F3): 프레임 타이밍, 더티 영역,
  백엔드 정보.
- 둘 다 기본적으로 꺼져 있으므로, 명시적으로 활성화하지 않는 한 릴리스 앱에 절대 포함되지 않습니다.

### 마크업 및 도구

- 런타임 파싱: `Jalium.UI.Markup.XamlReader`
- 패키징된 MSBuild 타깃/작업을 통한 빌드 통합
- 컴파일 타임 JALXAML 코드비하인드를 위한 소스 생성기
- Razor 지시문의 소스 생성기 컴파일 타임 로어링(lowering) — 아래 참조.
- JALXAML 핫 리로드: `HotReloadRuntime` / `HotReloadAgent`가 명명된 파이프를 통해
  프로세스 내에서 라이브 비주얼 트리를 패치합니다(IDE가 설정하는 `JALIUM_HOTRELOAD_PIPE`
  환경 변수를 통해 자동 시작). 독립 실행형 파일 감시기는
  `tools/Jalium.UI.HotReload.Watcher`에 있습니다.

## JALXAML의 Razor 구문

JALXAML은 기존 `{Binding ...}` 위에 추가 설탕(additive sugar)으로서 Razor 스타일 구문을 지원합니다:

- `@Path`
- `@(expr)`
- `@{ ... }`
- `@*...*@` 주석
- 혼합 텍스트 템플릿 (문자열/객체 대상용)
- `@if(expr){<Element />}` 블록 지시문 (전체 `else if` / `else` 체인 포함)
- 문(statement) / 제어 흐름 지시문: `@for`, `@foreach`, `@while`, `@switch`,
  `@using`, `@lock` (파싱 시점에 확장됨)
- 템플릿 콘텐츠를 위한 `@section`/`@RenderSection`
- 이스케이프: `@@` 및 `\@`

바인딩 소스 해석은 `DataContext`를 먼저, 그다음 코드비하인드 폴백 순으로 진행됩니다.

업데이트 동작:

- 관찰 가능 소스 (`INotifyPropertyChanged` / 종속성 속성): 실시간 업데이트.
- 관찰 불가능 CLR 소스: 로드 시 일회성 평가.

### 컴파일 타임 로어링

JALXAML 소스 생성기는 핫 경로(hot path)에서 런타임 파싱 비용이 없도록 다음을
빌드 타임에 로어링합니다:

- `@if` / `@else if` / `@else` 체인.
- `@section` / `@RenderSection`.
- 값 표현식 (`@Path`, `@(expr)`).
- `SetCompiledBinding`을 통한 `{Binding ...}` (SG의 `SplitParameters`는
  런타임 파서와 한 줄 한 줄(line-for-line) 일관성을 유지합니다).
- 사용자 정의 xmlns 요소 타입 — `.jalxaml`이 `XmlnsDefinition`을 통해 노출된
  컨트롤 라이브러리를 사용할 때, SG는 런타임 리플렉션으로 폴백하는 대신
  컴파일 타임에 CLR 타입을 해석합니다 (트리밍 / AOT에 도움이 됨).

`Setter.Value`는 의도적으로 로어링되지 않습니다.

구문 세부 사항 및 규칙은 [`docs/razor-syntax.md`](docs/razor-syntax.md)를 참조하세요.

## 설치

### 권장 (메타 패키지)

```bash
dotnet add package Jalium.UI
```

### 플랫폼별

```bash
# Windows 데스크톱
dotnet add package Jalium.UI.Desktop

# Android
dotnet add package Jalium.UI.Android

# Linux 데스크톱
dotnet add package Jalium.UI.Linux
```

### 세분화된 설치 (고급)

```bash
dotnet add package Jalium.UI.Core
dotnet add package Jalium.UI.Media
dotnet add package Jalium.UI.Input
dotnet add package Jalium.UI.Interop
dotnet add package Jalium.UI.Gpu
dotnet add package Jalium.UI.Controls
dotnet add package Jalium.UI.Xaml
dotnet add package Jalium.UI.Build
dotnet add package Jalium.UI.Xaml.SourceGenerator
```

## 빠른 시작 (C#)

```csharp
using Jalium.UI.Controls;

var app = new Application();

var window = new Window
{
    Title = "Hello Jalium.UI",
    Width = 960,
    Height = 640,
    Content = new StackPanel
    {
        Margin = new Thickness(24),
        Children =
        {
            new TextBlock { Text = "Jalium.UI", FontSize = 28 },
            new TextBlock { Text = "GPU-accelerated .NET UI framework", Margin = new Thickness(0, 8, 0, 16) },
            new Button { Content = "Start" }
        }
    }
};

app.Run(window);
```

## 빠른 시작 (JALXAML 런타임 파싱)

```csharp
using Jalium.UI.Controls;
using Jalium.UI.Markup;

var app = new Application();

var xaml = """
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="JALXAML Window" Width="800" Height="500">
  <Grid>
    <StackPanel Margin="20">
      <TextBlock Text="Hello from JALXAML" FontSize="24"/>
      <Button Content="Click" Margin="0,12,0,0"/>
    </StackPanel>
  </Grid>
</Window>
""";

var window = (Window)XamlReader.Parse(xaml);
app.Run(window);
```

## 소스에서 빌드하기

### 사전 요구 사항

- .NET 10 SDK
- C++ 워크로드를 갖춘 Visual Studio (Windows 네이티브 모듈용)
- CMake, Ninja, Clang/GCC 및 X11/Wayland 개발 패키지 (Linux 네이티브 모듈용; [Linux 가이드](docs/linux.md) 참조)
- Vulkan SDK (선택 사항, Vulkan 백엔드용)
- Android NDK (선택 사항, Android 빌드용)

### 빌드

```bash
# 전체 프레임워크 빌드
dotnet build src/packaging/Jalium.UI/Jalium.UI.csproj -c Release

# 테스트 실행
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj -c Release

# 네이티브 모듈 빌드 (VS 개발자 명령 프롬프트에서)
msbuild src/native/Jalium.Native.sln /m /p:Configuration=Release /p:Platform=x64

# Linux 네이티브 모듈 빌드 (glibc / musl 호스트)
bash eng/linux/build-native.sh linux-x64 Release

# Android용 빌드
bash src/native/build-android-deps.sh  # Android 네이티브 의존성
bash src/native/build-android.sh       # 네이티브 라이브러리
```

### NuGet 패키징

```bash
dotnet pack src/packaging/Jalium.UI/Jalium.UI.csproj -c Release -o artifacts/nuget
```

상세한 빌드 구성은 [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md)를 참조하세요.

## 저장소 레이아웃

```text
Jalium.UI/
  src/
    managed/
      Jalium.UI.Core/          # 종속성 속성 시스템, 비주얼 트리, 레이아웃
      Jalium.UI.Media/         # 브러시, 지오메트리, 드로잉, 텍스트, 이미징, 비디오
      Jalium.UI.Input/         # 입력 추상화 및 라우팅
      Jalium.UI.Interop/       # 네이티브 브리지 및 P/Invoke
      Jalium.UI.Gpu/           # GPU 리소스, 렌더 그래프, 셰이더
      Jalium.UI.Controls/      # 컨트롤, 패널, 테마, 도킹, 차트, 호스팅
      Jalium.UI.Xaml/          # JALXAML 파서, Razor 지원, 핫 리로드
      Jalium.UI.Build/         # JALXAML 컴파일을 위한 MSBuild 작업
      Jalium.UI.Xaml.SourceGenerator/  # Roslyn 소스 생성기
      Jalium.UI.Compiler/      # 독립 실행형 jalxamlc 컴파일러 (실행 파일)
    native/
      jalium.native.core/      # 네이티브 런타임 코어, 백엔드 레지스트리
      jalium.native.d3d12/     # DirectX 12 + Vello GPU 백엔드
      jalium.native.vulkan/    # Vulkan 백엔드
      jalium.native.metal/     # Metal 백엔드 (macOS)
      jalium.native.software/  # CPU 소프트웨어 렌더러
      jalium.native.platform/  # 플랫폼 추상화 레이어
      jalium.native.text/      # 자체 개발 텍스트 엔진 (비 Windows)
      jalium.native.browser/   # WebView2 통합
      jalium.native.media.core/     # 크로스 플랫폼 미디어 C ABI + 공유 오디오
      jalium.native.media.windows/  # Media Foundation 비디오 / 카메라 + WIC
      jalium.native.media.android/  # Android 미디어 디코더 + YUV SIMD
      jalium.native.aot/       # NativeAOT 애그리게이터 (미디어/텍스트/백엔드 하드 링크)
    packaging/
      Jalium.UI/               # 메인 메타 패키지
      Jalium.UI.Desktop/       # Windows 데스크톱 패키지 (win-x64 / win-arm64)
      Jalium.UI.Android/       # Android 패키지
  samples/                     # Gallery, DesktopDemo, AndroidDemo, HostingDemo,
                               #   MillionScroll, AotWindowDemo, BorderlessDemo,
                               #   TransparentBackdropDemo
  tools/
    Jalium.UI.HotReload.Watcher/  # 독립 실행형 JALXAML 핫 리로드 파일 감시기
  tests/
    Jalium.UI.Tests/           # xUnit 테스트 스위트 (70개 이상의 테스트 클래스)
    Jalium.UI.ShaderDemo/      # 셰이더 효과 데모
    Jalium.UI.ParityHarness/   # 백엔드 패리티 하네스
    Jalium.UI.DeviceLostHarness/  # 디바이스 손실 복구 하네스
    Jalium.UI.NuGetTest.Desktop/  # 패키징된 데스크톱 스모크 테스트
    Jalium.UI.NuGetTest.Android/  # 패키징된 Android 스모크 테스트
  docs/                        # razor-syntax, drawing-api, manual-build-configuration,
                               #   render-thread-design, present-pacing-design,
                               #   shell-drag-drop (+ design/ 및 reference/)
```

## 문서

| 문서 | 설명 |
| --- | --- |
| [`docs/razor-syntax.md`](docs/razor-syntax.md) | JALXAML용 Razor 구문 레퍼런스 |
| [`docs/drawing-api.md`](docs/drawing-api.md) | 드로잉 API (DrawingContext, GPU 효과, 렌더링) |
| [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) | 수동 빌드 구성 가이드 |
| [`docs/linux.md`](docs/linux.md) | Linux 데스크톱 가이드 (런타임 의존성, 윈도우 시스템, 패키징) |
| [`docs/linux-parity-status.md`](docs/linux-parity-status.md) | 검증된 Linux 지원 매트릭스, 증거 및 남은 경계 |
| [`docs/render-thread-design.md`](docs/render-thread-design.md) | 렌더 스레드 아키텍처 |
| [`docs/present-pacing-design.md`](docs/present-pacing-design.md) | Present 페이싱 / 프레임 스케줄링 |
| [`docs/shell-drag-drop.md`](docs/shell-drag-drop.md) | 셸 드래그 앤 드롭 통합 |

## Visual Studio 확장 참고 사항

VSIX는 다음 중 하나에 설치할 수 있습니다:

- 일반 인스턴스: `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>\Extensions`
- 실험적 인스턴스 (`/rootsuffix Exp`): `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>Exp\Extensions`

`.jalxaml` IntelliSense가 원시 XML 제안만 표시하는 경우, 사용 중인 동일한 인스턴스에 확장이 설치되어 있는지 확인하세요.

## 호환성 참고 사항

- Jalium.UI는 아직 WPF의 드롭인(drop-in) 대체품으로 자리매김되어 있지 않습니다.
- API 이름과 동작은 의도적으로 익숙한 WPF 개념에 가깝게 설계되었지만 차이가 존재합니다.
- 모든 `Jalium.UI.*` 패키지 전반에 걸쳐 패키지 버전을 일치시켜 유지하세요.

## 기여

이슈와 풀 리퀘스트를 환영합니다. 대규모 변경의 경우 다음을 포함해 주세요:

- 동기 및 설계 요약
- 동작 영향/위험
- 검증 단계 (테스트 또는 수동 검증)

## 커뮤니티

토론, 질문 또는 커뮤니티 지원을 원하시면 QQ 그룹에 참여하실 수 있습니다:

**QQ: 1079778999**

## 라이선스

MIT. [LICENSE](LICENSE)를 참조하세요.
