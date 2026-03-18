# Jalium.UI 手动配置构建说明

本文档面向"**不修改仓库源码**，只在用户本机做配置"的场景。

结论先说：

- 通过本机配置，可以让仓库的 `restore` 正常通过。
- 通过本机配置，可以让底层 managed 项目里的 `Jalium.UI.Input` 和 `Jalium.UI.Media` 编译通过。
- **仅靠本机配置，不能保证整个仓库直接编译通过。**
- 在当前未改源码的状态下，`Jalium.UI.Interop`、`Jalium.UI.Controls`、`Jalium.UI.Xaml`、测试项目以及整套 native 方案，仍可能失败。

## 1. 前置环境

请先在目标电脑安装：

- `.NET SDK 10.0.200` 或兼容的 `net10.0-windows` SDK
- Visual Studio 的 `Desktop development with C++`

说明：

- 如果只编 managed 子项目，`dotnet build` 可以使用。
- 如果要编整套解决方案，仍建议使用 Visual Studio 或 Developer Command Prompt 下的 `MSBuild`，因为仓库包含 native/C++ 工程。

## 2. 先补本地包源目录

仓库里的 `nuget.config` 指向了相对路径 `./packages` 作为本地源。

第一次拉仓库后，请在仓库根目录手动创建：

```powershell
mkdir packages\build
```

这一步的目的不是放包，而是避免 `NU1301`：

```text
本地源"...\packages"不存在
```

## 3. 先做还原

在仓库根目录执行：

```powershell
dotnet restore .\Jalium.UI.slnx -v minimal -p:GenerateAssemblyInfo=true
```

## 4. 重要说明：不要把自定义 obj/bin 放在仓库目录里

下面这些参数：

- `BaseIntermediateOutputPath`
- `IntermediateOutputPath`
- `OutputPath`

不要指到仓库内部的 `src\managed\...\obj_local_*` 或 `bin_local_*`。

原因：

- 仓库默认只排除了 `obj` 和 `bin`
- 没有排除 `obj_local_*`
- 如果你把自定义中间目录放在项目目录下面，第二次编译别的项目时，SDK 可能会把上一次生成的 `AssemblyInfo.cs` 一起扫进去
- 然后就会出现你刚碰到的 `CS0579` 重复特性错误

所以正确做法是：

- 把临时输出放到仓库外，比如 `%TEMP%` 或 `C:\temp`

如果你已经按旧写法跑过一次，请先清掉这些目录：

```powershell
Get-ChildItem .\src\managed -Recurse -Directory | Where-Object Name -like 'obj_local_*' | Remove-Item -Recurse -Force
Get-ChildItem .\src\managed -Recurse -Directory | Where-Object Name -like 'bin_local_*' | Remove-Item -Recurse -Force
```

## 5. 推荐构建顺序

先准备一个仓库外的临时目录：

```powershell
$work = Join-Path $env:TEMP "jalium-manual-build"
New-Item -ItemType Directory -Force `
  -Path "$work\\input\\obj", "$work\\input\\bin", `
        "$work\\media\\obj", "$work\\media\\bin", `
        "$work\\interop\\obj", "$work\\interop\\bin" | Out-Null
```

### 5.1 编译 Jalium.UI.Input

```powershell
dotnet build .\src\managed\Jalium.UI.Input\Jalium.UI.Input.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true `
  -p:ProduceReferenceAssembly=false `
  -p:BaseIntermediateOutputPath="$work\input\obj\" `
  -p:IntermediateOutputPath="$work\input\obj\Debug\net10.0-windows\" `
  -p:OutputPath="$work\input\bin\Debug\net10.0-windows\"
```

### 5.2 编译 Jalium.UI.Media

```powershell
dotnet build .\src\managed\Jalium.UI.Media\Jalium.UI.Media.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true `
  -p:ProduceReferenceAssembly=false `
  -p:BaseIntermediateOutputPath="$work\media\obj\" `
  -p:IntermediateOutputPath="$work\media\obj\Debug\net10.0-windows\" `
  -p:OutputPath="$work\media\bin\Debug\net10.0-windows\"
```

### 5.3 编译 Jalium.UI.Interop

```powershell
dotnet build .\src\managed\Jalium.UI.Interop\Jalium.UI.Interop.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true `
  -p:ProduceReferenceAssembly=false `
  -p:BaseIntermediateOutputPath="$work\interop\obj\" `
  -p:IntermediateOutputPath="$work\interop\obj\Debug\net10.0-windows\" `
  -p:OutputPath="$work\interop\bin\Debug\net10.0-windows\"
```

### 5.4 编译 Jalium.UI.Controls

```powershell
dotnet build .\src\managed\Jalium.UI.Controls\Jalium.UI.Controls.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true
```

### 5.5 编译 Jalium.UI.Xaml

```powershell
dotnet build .\src\managed\Jalium.UI.Xaml\Jalium.UI.Xaml.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true
```

参数说明：

- `GenerateAssemblyInfo=true`
  让项目文件里的程序集元数据重新生成。
- `ProduceReferenceAssembly=false`
  避免引用程序集路径带来的内部 API 可见性问题。
- `BaseIntermediateOutputPath` / `IntermediateOutputPath` / `OutputPath`
  把本次构建输出放到独立目录里，尽量绕开已有 `obj/bin` 和编译器文件锁。

## 6. Native/C++

### 6.1 前置条件

- Visual Studio C++ 工具链
- Windows SDK
- Vulkan SDK

### 6.2 推荐命令

请在 **VS Developer Command Prompt** 或等效的 MSVC 环境中执行：

```powershell
msbuild .\src\native\Jalium.Native.sln /m /p:Configuration=Debug /p:Platform=x64 /v:minimal
```

### 6.3 说明

- `browser`、`core`、`metal`、`d3d12` 后端可以作为手动构建目标。
- `vulkan` 后端依赖 Vulkan SDK；未安装时会报 `vulkan/vulkan.h` 缺失。
- 在普通 PowerShell 里直接跑 CMake/NMake，不保证具备完整的 `rc` / `mt` / MSVC 环境。

## 7. 当前支持矩阵

- `dotnet restore .\Jalium.UI.slnx -p:GenerateAssemblyInfo=true`
  可以
- `Jalium.UI.Input`
  可以
- `Jalium.UI.Media`
  可以
- `Jalium.UI.Interop`
  可以
- `Jalium.UI.Controls`
  可以
- `Jalium.UI.Xaml`
  可以
- `src\native\Jalium.Native.sln`
  部分可以
  `vulkan` 需要额外安装 Vulkan SDK
- `src\managed\Jalium.UI.Build\Jalium.UI.Build.csproj`
  不建议用 `dotnet build`
  需要 `MSBuild.exe`
- `src\packaging\Jalium.UI.csproj`
  当前不作为手动配置目标
- `tests\Jalium.UI.Tests\Jalium.UI.Tests.csproj`
  当前不作为手动配置目标

## 8. 给用户的最小操作建议

如果你要把这套流程发给用户，建议只给下面这组最小步骤：

1. 安装 `.NET 10 SDK`
2. 安装 Visual Studio C++ 工作负载
3. 安装 Vulkan SDK
4. 在仓库根目录创建 `packages\build`
5. 执行 `dotnet restore .\Jalium.UI.slnx -p:GenerateAssemblyInfo=true`
6. 按顺序执行 `Input`、`Media`、`Interop`、`Controls`、`Xaml`
7. 如需 native，再用 `MSBuild` 执行 `src\native\Jalium.Native.sln`

不要承诺：

- "整仓一定能一键编过"
- "只要装好 SDK 就行"
- "不需要 Visual Studio / MSBuild / C++ 工具链"

这些承诺和当前仓库实际状态不一致。
