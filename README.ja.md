# Jalium.UI

[English](README.md) | [简体中文](README.zh-CN.md) | **日本語** | [한국어](README.ko.md)

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/VeryJokerJal/Jalium.UI)

Jalium.UI は .NET 10 向けの GPU アクセラレーション対応クロスプラットフォーム UI フレームワークです。
WPF スタイルのオブジェクトモデル、Razor 構文拡張を備えた JALXAML マークアップ、
そしてプラットフォームネイティブのレンダリングバックエンド（DirectX 12、Vulkan、Metal、Software）を組み合わせています。

## プロジェクトの状況

- 活発に開発中 — v26.10.2-preview（API はマイナーバージョン間でも変化する可能性があります）
- 主要ターゲット: Windows 10/11 x64
- クロスプラットフォーム: Android（arm64-v8a、x86_64）、Linux（Vulkan）、macOS（Metal）
- ランタイムターゲット: .NET 10（`net10.0-windows`、`net10.0-android`、`net10.0`）
- レンダリング: DirectX 12（Windows）、Vulkan（Linux/Android）、Metal（macOS）、Software フォールバック

## なぜ Jalium.UI なのか

- ClearType サブピクセルテキストレンダリングを備えた GPU ネイティブのレンダリングパイプライン
- 馴染みのあるプログラミングモデル（`DependencyObject`、`UIElement`、パネル、テンプレート、リソース）
- Razor 構文拡張を備えた JALXAML マークアップ（`@Path`、`@(expr)`、`@{ ... }`、`@if/@section/@RenderSection`）
- 豊富なコントロールライブラリ: Charts、Ribbon、Docking、InkCanvas、WebView、Terminal、WindowsFormsHost を含む 80 種類以上のコントロール
- NuGet 経由のビルド時ツール（`Jalium.UI.Build`、`Jalium.UI.Xaml.SourceGenerator`）
- オートメーションピアによる UIA アクセシビリティ対応
- ビジュアルエフェクト: リキッドグラス、背景ぼかし、アクリル、マイカ、トランジションシェーダー、アニメーションビットマップ（GIF / APNG / アニメーション WebP）
- 書記素クラスタを意識したテキスト編集（UAX#29） — 絵文字、ZWJ シーケンス、肌の色修飾子、国旗が分割されることはありません
- 自己完結型の `Jalium.Extensions.*` スタック（Hosting / DI / Configuration / Options / Logging / Metrics） — `Microsoft.Extensions.Hosting` への依存なし
- WSOLA によるピッチ保持タイムストレッチを備えたネイティブオーディオパイプライン（miniaudio + dr_libs / minimp3）

## フレームワーク構成

### マネージドパッケージ

| パッケージ | 役割 |
| --- | --- |
| `Jalium.UI.Core` | 依存関係プロパティシステム、ビジュアルツリー、レイアウト、ルーティングイベント、バインディング、アニメーション |
| `Jalium.UI.Media` | ブラシ、ジオメトリ、描画プリミティブ、テキスト整形、イメージング、ビジュアルエフェクト |
| `Jalium.UI.Input` | マウス、キーボード、タッチ、スタイラスの入力抽象化とルーティング |
| `Jalium.UI.Interop` | マネージド/ネイティブ間のブリッジ、P/Invoke、ランタイムネイティブ依存関係のパッケージング |
| `Jalium.UI.Gpu` | GPU リソース管理、レンダーグラフ、マテリアル、シェーダー、バックエンド抽象化 |
| `Jalium.UI.Controls` | コントロール、パネル、テンプレート、ウィンドウ処理、テーマ、ドッキング、チャート |
| `Jalium.UI.Xaml` | JALXAML の解析/読み込みパイプライン、Razor 構文サポート、マークアップサービス |
| `Jalium.UI.Build` | JALXAML コンパイルワークフロー向けの MSBuild タスクおよびビルドアセット |
| `Jalium.UI.Xaml.SourceGenerator` | XAML/コードビハインド統合のための Roslyn ソースジェネレーター |
| `Jalium.UI.Compiler` | スタンドアロンの `jalxamlc.exe` コンパイラーツール |
| `Jalium.UI` | フレームワークスタック全体を参照するメタパッケージ |

### ネイティブモジュール

| モジュール | プラットフォーム | 役割 |
| --- | --- | --- |
| `jalium.native.core` | すべて | ネイティブコアランタイム、バックエンドレジストリ、コンテキスト管理 |
| `jalium.native.d3d12` | Windows | DirectX 12 レンダーターゲットおよび Vello GPU パイプライン |
| `jalium.native.vulkan` | Linux, Android | Vulkan レンダーバックエンド |
| `jalium.native.metal` | macOS | Metal レンダーバックエンド |
| `jalium.native.software` | すべて | CPU ベースのソフトウェアレンダリングフォールバック |
| `jalium.native.platform` | すべて | プラットフォーム抽象化（ウィンドウ、入力、イベント） |
| `jalium.native.text` | Linux, Android | クロスプラットフォームテキストエンジン（FreeType + HarfBuzz） |
| `jalium.native.browser` | Windows | WebView2 ブラウザー統合 |
| `jalium.native.media.core` | すべて | クロスプラットフォームメディア C ABI + 共有オーディオ（miniaudio / dr_libs / minimp3 / stb_vorbis） |
| `jalium.native.media.windows` | Windows | Media Foundation のビデオ / カメラ / AAC デコーダー + WIC イメージング |
| `jalium.native.aot` | すべて | NativeAOT アグリゲーター（media、text、バックエンドをハードリンク） |

### プラットフォームパッケージ

| パッケージ | ターゲット |
| --- | --- |
| `Jalium.UI.Desktop` | `net10.0-windows` — ネイティブ DLL を含むデスクトップ配布 |
| `Jalium.UI.Android` | `net10.0-android` — ネイティブ .so ライブラリを含む Android 配布 |

## 機能概要

### レイアウトとビジュアルツリー

- コアパネル: `Grid`、`StackPanel`、`Canvas`、`DockPanel`、`WrapPanel`、`UniformGrid`
- 仮想化: `VirtualizingStackPanel`、DataGrid のプレゼンター/パネル
- ドッキング: `DockLayout`、`DockSplitPanel`、`DockTabPanel`、`Split`
- ウィンドウレベルのレイアウトホスト、オーバーレイレイヤー、タイトルバー構成、クロム統合

### コントロール

- **入力**: `Button`、`TextBox`、`PasswordBox`、`NumberBox`、`AutoCompleteBox`、`ComboBox`、`Slider`、`CheckBox`、`RadioButton`
- **データ**: `TreeView`、`DataGrid`、`TreeDataGrid`、`ListBox`、`ListView`
- **ナビゲーション**: `NavigationView`、`TabControl`、`Ribbon`、`CommandBar`、`MenuBar`
- **ドキュメント**: `FlowDocumentViewer`、`FlowDocumentReader`、`FlowDocumentScrollViewer`、`Markdown`
- **チャート**: チャート凡例付きのカテゴリ軸、DateTime 軸、対数軸
- **リッチ**: `InkCanvas`、`WebView`/`WebBrowser`、`EditControl`、`QRCode`（自己完結型エンコーダー）、`TitleBar`、`Terminal`
- **相互運用**: `WindowsFormsHost`（`net10.0-windows` 上で `System.Windows.Forms` コントロールをホスト）
- **印刷**: ネイティブ Win32 プラットフォームレイヤーに支えられた `PrintDialog`
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
- FreeType + HarfBuzz によるクロスプラットフォームテキストシェーピング（Linux/Android）。
- 要素単位の `TextOptions.{TextRenderingMode, TextFormattingMode, TextHintingMode}`
  継承可能な添付プロパティ — 値は `FormattedText` → ネイティブの
  `JaliumTextFormat` を流れてラスタライザーに到達します:
  - D3D12: `GlyphKey` は `(aaMode, hintingMode)` を含み、`RasterizeGlyph` は `key.mode` を尊重します。
  - Vulkan / Windows: `LOGFONT.lfQuality` が bilevel / smoothed / ClearType の間で切り替わり、
    フォントキャッシュ + テキストキャッシュ + GDI フォントプールのキーはすべて `fontQuality` を含みます。
- プロセス全体のレンダリングモードのオーバーライド + カラー絵文字のラスタライズ。

### 入力パイプライン

- ヒットテストを伴うポインターおよびキーボードのルーティング
- ジェスチャ認識を伴うタッチおよびスタイラスの経路
- スクロールおよびマニピュレーションイベントの処理

### GPU レンダリングとエフェクト

- Vello GPU コンピュートパイプライン（path、clip、tile の各ステージ）を備えた DirectX 12
- 背景エフェクト: ぼかし、アクリル、マイカ、すりガラス
- 屈折、色収差を伴うリキッドグラス
- トランジションシェーダーと要素エフェクト（ぼかし、ドロップシャドウ）
- アニメーションビットマップ: GIF、APNG、アニメーション WebP
- HLSL によるカスタムシェーダーのサポート
- 大きな画像グリッド向けのビットマップ縮小キャッシュ + 仮想化対応ラップパネル
- DevTools の Perf タブに表示される統一された path/bitmap テレメトリ C ABI

### Hosting / DI / Configuration

- 自己完結型の `Jalium.Extensions.*` スタックは `Jalium.UI.Controls` の内部に存在します
  （`Microsoft.Extensions.Hosting` パッケージやその 18 個の推移的依存関係は一切ありません）。
- Hosting（`HostBuilder` / `Host` / `HostApplicationBuilder`）、
  DependencyInjection（keyed サービス + `ActivatorUtilities` を含む）、
  Configuration（Json / Xml / Ini / Memory / CommandLine / UserSecrets）、
  Options（`DataAnnotations` 検証を含む）、Logging（`LoggerMessage`
  ソースジェネレーターを含む）、Metrics、Caching、FileProviders、
  FileSystemGlobbing、ObjectPool、Primitives を網羅します。
- Console サポートは意図的に実装していません。

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

- コアおよび専用コントロール向けの UIA オートメーションピア
- Chart、DiffViewer、HexEditor、JsonTreeViewer、Map、PropertyGrid のオートメーション
- `Window.ResolveCursor` は無効な要素に対して標準の矢印を返すため、
  ホバー状態が有効なコントロールと混同されることはありません。

### マークアップとツール

- ランタイム解析: `Jalium.UI.Markup.XamlReader`
- パッケージ化された MSBuild ターゲット/タスクによるビルド統合
- コンパイル時 JALXAML コードビハインドのためのソースジェネレーター
- Razor ディレクティブのソースジェネレーターによるコンパイル時ロワーリング — 下記参照。

## JALXAML における Razor 構文

JALXAML は、既存の `{Binding ...}` の上に付加的なシンタックスシュガーとして Razor スタイルの構文をサポートします:

- `@Path`
- `@(expr)`
- `@{ ... }`
- 混在テキストテンプレート（文字列/オブジェクトターゲット向け）
- `@if(expr){<Element />}` ブロックディレクティブ（完全な `else if` / `else` チェーンを含む）
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
<Window xmlns="https://jalium.dev/ui" Title="JALXAML Window" Width="800" Height="500">
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

- .NET 10 SDK（`net10.0-windows`）
- C++ ワークロードを備えた Visual Studio（ネイティブモジュール用）
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

# Android 向けにビルド
bash src/native/build-android-deps.sh  # FreeType + HarfBuzz
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
      Jalium.UI.Media/         # ブラシ、ジオメトリ、描画、テキスト、イメージング
      Jalium.UI.Input/         # 入力抽象化とルーティング
      Jalium.UI.Interop/       # ネイティブブリッジと P/Invoke
      Jalium.UI.Gpu/           # GPU リソース、レンダーグラフ、シェーダー
      Jalium.UI.Controls/      # コントロール、パネル、テーマ、ドッキング、チャート
      Jalium.UI.Xaml/          # JALXAML パーサーと Razor サポート
      Jalium.UI.Build/         # JALXAML コンパイル用 MSBuild タスク
      Jalium.UI.Xaml.SourceGenerator/  # Roslyn ソースジェネレーター
      Jalium.UI.Compiler/      # スタンドアロン JALXAML コンパイラー
    native/
      jalium.native.core/      # ネイティブランタイムコア
      jalium.native.d3d12/     # DirectX 12 + Vello GPU バックエンド
      jalium.native.vulkan/    # Vulkan バックエンド
      jalium.native.metal/     # Metal バックエンド（macOS）
      jalium.native.software/  # CPU ソフトウェアレンダラー
      jalium.native.platform/  # プラットフォーム抽象化レイヤー
      jalium.native.text/      # FreeType + HarfBuzz テキストエンジン
      jalium.native.browser/   # WebView2 統合
    packaging/
      Jalium.UI/               # メインメタパッケージ
      Jalium.UI.Desktop/       # Windows デスクトップパッケージ
      Jalium.UI.Android/       # Android パッケージ
  tests/
    Jalium.UI.Tests/           # xUnit テストスイート（70 以上のテストクラス）
    Jalium.UI.ShaderDemo/      # シェーダーエフェクトのデモ
  docs/
    razor-syntax.md            # Razor 構文リファレンス
    drawing-api.md             # 描画 API ドキュメント
    manual-build-configuration.md  # ビルド構成ガイド
```

## ドキュメント

| ドキュメント | 説明 |
| --- | --- |
| [`docs/razor-syntax.md`](docs/razor-syntax.md) | JALXAML 向けの Razor 構文リファレンス |
| [`docs/drawing-api.md`](docs/drawing-api.md) | 描画 API（DrawingContext、GPU エフェクト、レンダリング） |
| [`docs/manual-build-configuration.md`](docs/manual-build-configuration.md) | 手動ビルド構成ガイド |

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