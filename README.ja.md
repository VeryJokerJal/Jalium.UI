# Jalium.UI

[English](README.md) | [简体中文](README.zh-CN.md) | **日本語** | [한국어](README.ko.md)

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/VeryJokerJal/Jalium.UI)

Jalium.UI は .NET 10 向けの GPU アクセラレーション対応クロスプラットフォーム UI フレームワークです。
WPF スタイルのオブジェクトモデル、Razor 構文拡張を備えた JALXAML マークアップ、
そしてプラットフォームネイティブのレンダリングバックエンド（DirectX 12、Vulkan、Metal、Software）を組み合わせています。

## プロジェクトの状況

- 活発に開発中 — v26.10.6（API はマイナーバージョン間でも変化する可能性があります）
- 主要ターゲット: Windows 10/11（x64、ARM64）
- クロスプラットフォーム: Android（arm64-v8a、x86_64）、Linux（X11/Wayland、Vulkan または Software）、macOS（Metal）
- ランタイムターゲット: .NET 10（`net10.0-windows`、`net10.0-android`、`net10.0`）
- レンダリング: DirectX 12（Windows）、Vulkan（Linux/Android）、Metal（macOS）、Software フォールバック

## なぜ Jalium.UI なのか

- ClearType サブピクセルテキストレンダリングを備えた GPU ネイティブのレンダリングパイプライン
- 馴染みのあるプログラミングモデル（`DependencyObject`、`UIElement`、パネル、テンプレート、リソース）
- Razor 構文拡張を備えた JALXAML マークアップ（`@Path`、`@(expr)`、`@{ ... }`、`@if`/`@for`/`@foreach`、`@section`/`@RenderSection`）
- 豊富なコントロールライブラリ: Charts、Ribbon、Docking、InkCanvas、WebView、Terminal、MediaElement、MapView、PropertyGrid、WindowsFormsHost を含む 100 種類以上のコントロール
- 開発者体験: JALXAML ホットリロード（ライブビジュアルツリーへのパッチ適用）に加え、選択的に有効化できる組み込みの DevTools インスペクターとデバッグ HUD
- NuGet 経由のビルド時ツール（`Jalium.UI.Build`、`Jalium.UI.Xaml.SourceGenerator`）
- 第一級の Generic Host 統合 — `AppBuilder` は `IHostApplicationBuilder`（`Microsoft.Extensions.Hosting`）を実装しており、DI、構成、オプション、ロギング、メトリクス、さらに Jalium MVVM のビュー/ビューモデル配線を提供します
- Windows UIA と Linux AT-SPI ブリッジを備えたオートメーションピアによるアクセシビリティ対応
- ビジュアルエフェクト: リキッドグラス、背景ぼかし、アクリル、マイカ、トランジションシェーダー、アニメーションビットマップ（GIF / APNG / アニメーション WebP）
- `MediaElement` / `NativeVideoSurface` によるネイティブ GPU ビデオ（D3D12 / Vulkan サーフェス、段階的にロールアウト中）
- 完全なマルチタッチ入力 — 物理的な慣性を伴うマニピュレーション、ジェスチャ認識、リアルタイムスタイラスプレビュー
- 書記素クラスタを意識したテキスト編集（UAX#29） — 絵文字、ZWJ シーケンス、肌の色修飾子、国旗が分割されることはありません
- WSOLA によるピッチ保持タイムストレッチを備えたネイティブオーディオパイプライン（miniaudio + dr_libs / minimp3）

## フレームワーク構成

### マネージドパッケージ

| パッケージ | 役割 |
| --- | --- |
| `Jalium.UI.Core` | 依存関係プロパティシステム、ビジュアルツリー、レイアウト、ルーティングイベント、バインディング、アニメーション |
| `Jalium.UI.Media` | ブラシ、ジオメトリ、描画プリミティブ、テキスト整形、イメージング、ビデオ、ビジュアルエフェクト |
| `Jalium.UI.Input` | マウス、キーボード、タッチ、スタイラスの入力抽象化とルーティング |
| `Jalium.UI.Interop` | マネージド/ネイティブ間のブリッジ、P/Invoke、ランタイムネイティブ依存関係のパッケージング |
| `Jalium.UI.Gpu` | GPU リソース管理、レンダーグラフ、マテリアル、シェーダー、バックエンド抽象化 |
| `Jalium.UI.Controls` | コントロール、パネル、テンプレート、ウィンドウ処理、テーマ、ドッキング、チャート、ホスティング |
| `Jalium.UI.Xaml` | JALXAML の解析/読み込みパイプライン、Razor 構文サポート、ホットリロード、マークアップサービス |
| `Jalium.UI.Build` | JALXAML コンパイルワークフロー向けの MSBuild タスクおよびビルドアセット |
| `Jalium.UI.Xaml.SourceGenerator` | XAML/コードビハインド統合のための Roslyn ソースジェネレーター |
| `Jalium.UI` | フレームワークスタック全体を参照するメタパッケージ |

> `Jalium.UI.Compiler` はスタンドアロンの `jalxamlc` コンパイラー実行ファイルをビルドします。これはソースから利用する
> ビルドツールであり、NuGet パッケージではありません（`dotnet add package` は適用されません）。

### ネイティブモジュール

| モジュール | プラットフォーム | 役割 |
| --- | --- | --- |
| `jalium.native.core` | すべて | ネイティブコアランタイム、バックエンドレジストリ、コンテキスト管理 |
| `jalium.native.d3d12` | Windows | DirectX 12 レンダーターゲットおよび Vello GPU パイプライン |
| `jalium.native.vulkan` | Linux, Android | Vulkan レンダーバックエンド |
| `jalium.native.metal` | macOS | Metal レンダーバックエンド |
| `jalium.native.software` | すべて | CPU ベースのソフトウェアレンダリングフォールバック |
| `jalium.native.platform` | すべて | プラットフォーム抽象化（ウィンドウ、入力、イベント） |
| `jalium.native.text` | Linux, Android, macOS | 自製テキストエンジン（sfnt/cmap/glyf/CFF + OT shaper；Linux では fontconfig はフォント検出のみ） |
| `jalium.native.browser` | Windows | WebView2 ブラウザー統合 |
| `jalium.native.media.core` | すべて | クロスプラットフォームメディア C ABI + 共有オーディオ（miniaudio / dr_libs / minimp3 / stb_vorbis） |
| `jalium.native.media.windows` | Windows | Media Foundation のビデオ / カメラ / AAC デコーダー + WIC イメージング |
| `jalium.native.media.android` | Android | Android の画像 / ビデオ / カメラデコーダー + NDK 経由の YUV SIMD（NEON） |
| `jalium.native.aot` | すべて | NativeAOT アグリゲーター（media、text、バックエンドをハードリンク） |

### プラットフォームパッケージ

| パッケージ | ターゲット |
| --- | --- |
| `Jalium.UI.Desktop` | `net10.0-windows` — RID 別のネイティブ DLL（win-x64 / win-arm64）を含むデスクトップ配布 |
| `Jalium.UI.Android` | `net10.0-android` — ネイティブ .so ライブラリを含む Android 配布 |
| `Jalium.UI.Linux` | `net10.0` — Linux デスクトップ配布（Wayland/X11、Vulkan + ソフトウェアレンダリング；linux-x64 / linux-arm64 / linux-musl-x64 / linux-musl-arm64 の RID レイアウトを予約） |

ここに示す RID はパッケージレイアウトであり、4 RID と NativeAOT がすべてリリース検証済みであることを意味しません。現在の証拠と残る境界については、[Linux サポート状況マトリクス](docs/linux-parity-status.md)を参照してください。

## 機能概要

### レイアウトとビジュアルツリー

- コアパネル: `Grid`、`StackPanel`、`Canvas`、`DockPanel`、`WrapPanel`、`UniformGrid`
- 仮想化: `VirtualizingStackPanel`、`VirtualizingWrapPanel`、DataGrid のプレゼンター/パネル
- ドッキング: `DockLayout`、`DockSplitPanel`、`DockTabPanel`、`Split`
- ウィンドウレベルのレイアウトホスト、オーバーレイレイヤー、タイトルバー構成、クロム統合

### コントロール

- **入力**: `Button`、`TextBox`、`PasswordBox`、`NumberBox`、`AutoCompleteBox`、`ComboBox`、`Slider`、`RangeSlider`、`CheckBox`、`RadioButton`、`ToggleSwitch`、`SplitButton`
- **ピッカー**: `ColorPicker`、`DatePicker`、`TimePicker`、`Calendar`
- **データ**: `TreeView`、`DataGrid`、`TreeDataGrid`、`ListBox`、`ListView`、`PropertyGrid`、`JsonTreeViewer`
- **ナビゲーションとバー**: `NavigationView`、`TabControl`、`Ribbon`、`CommandBar`、`MenuBar`、`ToolBar`、`StatusBar`、`GroupBox`、`Expander`、`InfoBar`
- **ドキュメント**: `DocumentViewer`、`FlowDocumentReader`、`FlowDocumentScrollViewer`、`FlowDocumentPageViewer`、`Markdown`
- **チャート**: `BarChart`、`LineChart`、`PieChart`、`ScatterPlot`、`CandlestickChart`、`GaugeChart`、`Heatmap`、`GanttChart`、`NetworkGraph`、`SankeyDiagram`、`TreeMap`、`Sparkline`、加えて `FlowchartDiagram` / `MermaidDiagram` — カテゴリ / DateTime / 対数 / 数値の各軸、凡例とツールチップ
- **メディア**: `MediaElement`、`CameraView`
- **マップ**: `MapView`、`MiniMap`、`GeographicHeatmap`
- **リッチ**: `InkCanvas`、`WebView`/`WebBrowser`、`EditControl`、`RichTextBox`、`QRCode`（自己完結型エンコーダー）、`TitleBar`、`Terminal`、`SwipeControl`
- **開発者ツール**: `DiffViewer`、`HexEditor`
- **相互運用**: `WindowsFormsHost`（`net10.0-windows` 上で `System.Windows.Forms` コントロールをホスト）
- **印刷**: Windows では Win32、Linux では PDF + xdg-desktop-portal を使用する `PrintDialog`
- **通知**: トースト形式の通知システム

### テキスト編集

- `TextBox`、`PasswordBox`、`EditControl`、`RichTextBox`、`TextBlock`、`Label`、`Markdown`、
  `Terminal`、`TextEffectPresenter` にまたがる、書記素クラスタを意識したキャレット、選択、単語区切り、削除 —
  絵文字（ZWJ / 肌の色 / 国旗 / 結合文字）がクラスタの途中で分割されることはありません
  （`StringInfo.GetTextElementEnumerator` による UAX#29）。
- パスワード / 読み取り専用フィールドでの IME 抑制により、入力中の変換が保護されたエディター状態を変更できないようにします。
- BOM 自動検出を伴うマルチエンコーディングのファイル入出力（`TextBox`、`EditControl`、`Markdown`、`RichTextBox` の
  `LoadFromFile` / `SaveToFile`）。`CodePagesEncodingProvider` は `ModuleInitializer` 経由で登録されるため、
  GBK / Shift-JIS / その他のデフォルト以外のコードページも標準で動作します。
- `Terminal` は `Encoding` プロパティとステートフルな `Decoder` を追加し、出力バイトをマルチバイトシーケンスを分割することなく
  ユーザーのターミナルエンコーディングでデコードします。
- WPF に準拠した `TextBox.CharacterCasing` / `MinLines` / `MaxLines`、`ComboBox.StaysOpenOnEdit`。

### テキストレンダリング

- デュアルソースブレンディングによる ClearType サブピクセルテキストレンダリング。
- CPU ラスタライズフォールバックパス。
- 自製 OpenType エンジンによるクロスプラットフォームテキストシェーピング（Linux/Android）— FreeType/HarfBuzz のランタイム依存なし。fontconfig はフォント検出のみに使用。
- 要素単位の `TextOptions.{TextRenderingMode, TextFormattingMode, TextHintingMode}`
  継承可能な添付プロパティ — 値は `FormattedText` → ネイティブの
  `JaliumTextFormat` を流れてラスタライザーに到達します:
  - D3D12: `GlyphKey` は `(aaMode, hintingMode)` を含み、`RasterizeGlyph` は `key.mode` を尊重します。
  - Vulkan / Windows: `LOGFONT.lfQuality` が bilevel / smoothed / ClearType の間で切り替わり、
    フォントキャッシュ + テキストキャッシュ + GDI フォントプールのキーはすべて `fontQuality` を含みます。
- プロセス全体のレンダリングモードのオーバーライド + カラー絵文字のラスタライズ。

### 入力パイプライン

- ヒットテストを伴うポインターおよびキーボードのルーティング
- 物理的な慣性を伴う完全なマルチタッチマニピュレーション（パン / ピンチ / 回転）
- コントロールカタログ全体でネイティブに配線された `GestureRecognizer`（タップ / スワイプ / ピンチ）
- リアルタイムスタイラス（RTS）のバックグラウンドインクプレビュー
- スクロールおよびマニピュレーションイベントの処理

### GPU レンダリングとエフェクト

- Vello GPU コンピュートパイプライン（path、clip、tile の各ステージ）を備えた DirectX 12
- 背景エフェクト: ぼかし、アクリル、マイカ、すりガラス
- 屈折、色収差を伴うリキッドグラス
- トランジションシェーダーと要素エフェクト（ぼかし、ドロップシャドウ）
- アニメーションビットマップ: GIF、APNG、アニメーション WebP
- ネイティブ GPU ビデオサーフェス（`NativeVideoSurface`）: CPU BGRA8 ステージングに加え、外部 GPU リソースの
  インポート（D3D11 共有テクスチャ / Vulkan 外部 `VkImage` / Android `AHardwareBuffer` / Apple `IOSurface`） —
  API サーフェスは整備済みで、バックエンド別のアップロードパスは段階的に埋めています。
- HLSL によるカスタムシェーダーのサポート
- 大きな画像グリッド向けのビットマップ縮小キャッシュ + 仮想化対応ラップパネル
- DevTools の Perf タブに表示される統一された path/bitmap テレメトリ C ABI

### Hosting / DI / Configuration

- 標準の `Microsoft.Extensions.Hosting` スタック（10.0.3）の上に構築されています。`AppBuilder` は
  `IHostApplicationBuilder`（`HostApplicationBuilder` をラップ）を実装しているため、馴染みのある
  `IServiceCollection`、`IConfiguration`、`ILoggingBuilder`、`IHostEnvironment`、`Meter` の各サーフェスが
  アプリ起動時に利用できます。
- Jalium 固有の接着コードは `Jalium.UI.Hosting` に存在します: MVVM のビュー/ビューモデル登録
  （`AddView<TView, TViewModel>`、`AddViewsAndViewModels`）、`JaliumRuntimeOptions` への `ConfigureJalium`
  オプションバインディング、フレームタイムメトリクス（`UseJaliumMetrics` / `JaliumMeter`）、および
  選択的に有効化する開発者ツール（`UseDevTools` / `UseDebugHud`）です。
- `EnableConfigurationBindingGenerator` による Trim/AOT セーフな構成バインディング。リフレクションベースの
  ビュー検出オーバーロードには `[RequiresUnreferencedCode]` が注釈されています。

### オーディオパイプライン

- WAV / FLAC / MP3 / Vorbis（クロスプラットフォーム）および AAC（Windows では Media Foundation 経由）を
  カバーするネイティブの `MiniAudioDevice` 再生 + `NativeAudioDecoder`。
- ピッチ保持タイムストレッチのための `WsolaSpeedProcessor` を備えたマネージドの `IAudioProcessor` チェーン。
- オーディオの TU はプラットフォームごとの各 `jalium.native.media.*` ライブラリにコンパイルされるため、
  シンボルはマネージドの P/Invoke が読み込むのと同じ DLL に配置されます。

### 3D アニメーションの型

- `Point3DAnimation`、`Rotation3DAnimation`、`Size3DAnimation`、
  `Vector3DAnimation` が WPF のアニメーション型セットを完成させます。

### アクセシビリティ

- コアおよび専用コントロール向けのオートメーションピア（Windows UIA と Linux AT-SPI を通じて公開）
- Chart、DiffViewer、HexEditor、JsonTreeViewer、Map、PropertyGrid のオートメーション
- `Window.ResolveCursor` は無効な要素に対して標準の矢印を返すため、
  ホバー状態が有効なコントロールと混同されることはありません。

### 開発者ツール

- 選択的に有効化する DevTools ウィンドウ（`app.UseDevTools()`、F12 / Ctrl+Shift+C の要素ピッカー）:
  仮想化とインラインプロパティ編集を備えたビジュアル / 論理 / フラットツリーインスペクター、加えて
  Layout、Events、Bindings、Resources、Perf、UIA、Tools
  （ルーラー / カラーピッカー / オーバードロー・ダーティ領域オーバーレイ / スクリーンショットエクスポート）
  とライブ REPL タブ。
- オンスクリーンのデバッグ HUD（`app.UseDebugHud()`、F3）: フレームタイミング、ダーティ領域、
  バックエンド情報。
- どちらもデフォルトでは無効なので、明示的に有効化しない限りリリースアプリに同梱されることはありません。

### マークアップとツール

- ランタイム解析: `Jalium.UI.Markup.XamlReader`
- パッケージ化された MSBuild ターゲット/タスクによるビルド統合
- コンパイル時 JALXAML コードビハインドのためのソースジェネレーター
- Razor ディレクティブのソースジェネレーターによるコンパイル時ロワーリング — 下記参照。
- JALXAML ホットリロード: `HotReloadRuntime` / `HotReloadAgent` が、名前付きパイプ経由でインプロセスに
  ライブビジュアルツリーへパッチを適用します（IDE が設定する `JALIUM_HOTRELOAD_PIPE` 環境変数を通じて
  自動起動）。スタンドアロンのファイルウォッチャーは `tools/Jalium.UI.HotReload.Watcher` にあります。

## JALXAML における Razor 構文

JALXAML は、既存の `{Binding ...}` の上に付加的なシンタックスシュガーとして Razor スタイルの構文をサポートします:

- `@Path`
- `@(expr)`
- `@{ ... }`
- `@*...*@` コメント
- 混在テキストテンプレート（文字列/オブジェクトターゲット向け）
- `@if(expr){<Element />}` ブロックディレクティブ（完全な `else if` / `else` チェーンを含む）
- ステートメント / 制御フローディレクティブ: `@for`、`@foreach`、`@while`、`@switch`、
  `@using`、`@lock`（解析時に展開）
- テンプレート化されたコンテンツのための `@section`/`@RenderSection`
- エスケープ: `@@` と `\@`

バインディングソースの解決は、まず `DataContext`、次にコードビハインドへのフォールバックの順です。

更新の挙動:

- 監視可能なソース（`INotifyPropertyChanged` / 依存関係プロパティ）: リアルタイム更新。
- 監視不可能な CLR ソース: 読み込み時に一度だけ評価。

### コンパイル時ロワーリング

JALXAML ソースジェネレーターは、ホットパスでランタイム解析コストが発生しないよう、
以下をビルド時にロワーリングします:

- `@if` / `@else if` / `@else` チェーン。
- `@section` / `@RenderSection`。
- 値式（`@Path`、`@(expr)`）。
- `SetCompiledBinding` 経由の `{Binding ...}`（SG の `SplitParameters` は
  ランタイムパーサーと行単位で一致するよう維持されています）。
- カスタム xmlns の要素型 — `.jalxaml` が `XmlnsDefinition` を通じて公開されるコントロールライブラリを
  使用する場合、SG はランタイムリフレクションへフォールバックする代わりにコンパイル時に CLR 型を解決します
  （トリミング / AOT に役立ちます）。

`Setter.Value` は意図的にロワーリングされません。

構文の詳細と規則については、[`docs/razor-syntax.md`](docs/razor-syntax.md) を参照してください。

## インストール

### 推奨（メタパッケージ）

```bash
dotnet add package Jalium.UI
```

### プラットフォーム別

```bash
# Windows デスクトップ
dotnet add package Jalium.UI.Desktop

# Android
dotnet add package Jalium.UI.Android

# Linux デスクトップ
dotnet add package Jalium.UI.Linux
```

### 個別インストール（上級者向け）

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

## クイックスタート（C#）

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

## クイックスタート（JALXAML ランタイム解析）

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

## ソースからのビルド

### 前提条件

- .NET 10 SDK
- C++ ワークロードを備えた Visual Studio（Windows ネイティブモジュール用）
- CMake、Ninja、Clang/GCC、および X11/Wayland 開発パッケージ（Linux ネイティブモジュール用；[Linux ガイド](docs/linux.md)を参照）
- Vulkan SDK（オプション、Vulkan バックエンド用）
- Android NDK（オプション、Android ビルド用）

### ビルド

```bash
# フレームワーク全体をビルド
dotnet build src/packaging/Jalium.UI/Jalium.UI.csproj -c Release

# テストを実行
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj -c Release

# ネイティブモジュールをビルド（VS Developer Command Prompt 内で）
msbuild src/native/Jalium.Native.sln /m /p:Configuration=Release /p:Platform=x64

# Linux ネイティブモジュールのビルド（glibc / musl ホスト）
bash eng/linux/build-native.sh linux-x64 Release

# Android 向けにビルド
bash src/native/build-android-deps.sh  # Android ネイティブ依存
bash src/native/build-android.sh       # ネイティブライブラリ
```

### NuGet パッケージング

```bash
dotnet pack src/packaging/Jalium.UI/Jalium.UI.csproj -c Release -o artifacts/nuget
```

詳細なビルド構成については、[`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) を参照してください。

## リポジトリ構成

```text
Jalium.UI/
  src/
    managed/
      Jalium.UI.Core/          # 依存関係プロパティシステム、ビジュアルツリー、レイアウト
      Jalium.UI.Media/         # ブラシ、ジオメトリ、描画、テキスト、イメージング、ビデオ
      Jalium.UI.Input/         # 入力抽象化とルーティング
      Jalium.UI.Interop/       # ネイティブブリッジと P/Invoke
      Jalium.UI.Gpu/           # GPU リソース、レンダーグラフ、シェーダー
      Jalium.UI.Controls/      # コントロール、パネル、テーマ、ドッキング、チャート、ホスティング
      Jalium.UI.Xaml/          # JALXAML パーサー、Razor サポート、ホットリロード
      Jalium.UI.Build/         # JALXAML コンパイル用 MSBuild タスク
      Jalium.UI.Xaml.SourceGenerator/  # Roslyn ソースジェネレーター
      Jalium.UI.Compiler/      # スタンドアロン jalxamlc コンパイラー（実行ファイル）
    native/
      jalium.native.core/      # ネイティブランタイムコア、バックエンドレジストリ
      jalium.native.d3d12/     # DirectX 12 + Vello GPU バックエンド
      jalium.native.vulkan/    # Vulkan バックエンド
      jalium.native.metal/     # Metal バックエンド（macOS）
      jalium.native.software/  # CPU ソフトウェアレンダラー
      jalium.native.platform/  # プラットフォーム抽象化レイヤー
      jalium.native.text/      # 自製テキストエンジン（非 Windows）
      jalium.native.browser/   # WebView2 統合
      jalium.native.media.core/     # クロスプラットフォームメディア C ABI + 共有オーディオ
      jalium.native.media.windows/  # Media Foundation のビデオ / カメラ + WIC
      jalium.native.media.android/  # Android メディアデコーダー + YUV SIMD
      jalium.native.aot/       # NativeAOT アグリゲーター（media/text/バックエンドをハードリンク）
    packaging/
      Jalium.UI/               # メインメタパッケージ
      Jalium.UI.Desktop/       # Windows デスクトップパッケージ（win-x64 / win-arm64）
      Jalium.UI.Android/       # Android パッケージ
  samples/                     # Gallery、DesktopDemo、AndroidDemo、HostingDemo、
                               #   MillionScroll、AotWindowDemo、BorderlessDemo、
                               #   TransparentBackdropDemo
  tools/
    Jalium.UI.HotReload.Watcher/  # スタンドアロン JALXAML ホットリロードファイルウォッチャー
  tests/
    Jalium.UI.Tests/           # xUnit テストスイート（70 以上のテストクラス）
    Jalium.UI.ShaderDemo/      # シェーダーエフェクトのデモ
    Jalium.UI.ParityHarness/   # バックエンドパリティハーネス
    Jalium.UI.DeviceLostHarness/  # デバイスロスト復旧ハーネス
    Jalium.UI.NuGetTest.Desktop/  # パッケージ済みデスクトップのスモークテスト
    Jalium.UI.NuGetTest.Android/  # パッケージ済み Android のスモークテスト
  docs/                        # razor-syntax、drawing-api、manual-build-configuration、
                               #   render-thread-design、present-pacing-design、
                               #   shell-drag-drop（+ design/ と reference/）
```

## ドキュメント

| ドキュメント | 説明 |
| --- | --- |
| [`docs/razor-syntax.md`](docs/razor-syntax.md) | JALXAML 向けの Razor 構文リファレンス |
| [`docs/drawing-api.md`](docs/drawing-api.md) | 描画 API（DrawingContext、GPU エフェクト、レンダリング） |
| [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) | 手動ビルド構成ガイド |
| [`docs/linux.md`](docs/linux.md) | Linux デスクトップガイド（ランタイム依存、ウィンドウシステム、パッケージング） |
| [`docs/linux-parity-status.md`](docs/linux-parity-status.md) | 検証済み Linux サポートマトリクス、証拠、残る境界 |
| [`docs/render-thread-design.md`](docs/render-thread-design.md) | レンダースレッドアーキテクチャ |
| [`docs/present-pacing-design.md`](docs/present-pacing-design.md) | プレゼントペーシング / フレームスケジューリング |
| [`docs/shell-drag-drop.md`](docs/shell-drag-drop.md) | シェルのドラッグ＆ドロップ統合 |

## Visual Studio 拡張機能に関する注意

VSIX は次のいずれかにインストールできます:

- 通常インスタンス: `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>\Extensions`
- 実験用インスタンス（`/rootsuffix Exp`）: `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_<instanceId>Exp\Extensions`

`.jalxaml` の IntelliSense が生の XML 候補しか表示しない場合は、使用しているインスタンスと同じインスタンスに拡張機能がインストールされているか確認してください。

## 互換性に関する注意

- Jalium.UI は、まだ WPF のドロップイン代替として位置づけられてはいません。
- API 名と挙動は馴染みのある WPF の概念に意図的に近づけられていますが、相違点は存在します。
- すべての `Jalium.UI.*` パッケージ間でパッケージバージョンを揃えてください。

## コントリビューション

Issue とプルリクエストを歓迎します。大きな変更の場合は、以下を含めてください:

- 動機と設計の概要
- 挙動への影響/リスク
- 検証手順（テストまたは手動検証）

## コミュニティ

ディスカッション、質問、またはコミュニティサポートについては、QQ グループに参加できます:

**QQ: 1079778999**

## ライセンス

MIT. See [LICENSE](LICENSE).
