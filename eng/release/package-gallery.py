#!/usr/bin/env python3
"""Package a verified Gallery publish directory, with source provenance and hashes."""
import argparse
import hashlib
import json
import pathlib
import plistlib
import shutil
import subprocess
import tarfile
import zipfile

VERSION = "26.11.0"


def git_head(root):
    return subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("platform", choices=("linux", "macos", "windows", "android"))
    for argument in ("publish", "output", "rid", "framework-root", "gallery-root"):
        parser.add_argument("--" + argument, required=True)
    args = parser.parse_args()
    published = pathlib.Path(args.publish).resolve(strict=True)
    output = pathlib.Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    framework = pathlib.Path(args.framework_root).resolve(strict=True)
    gallery = pathlib.Path(args.gallery_root).resolve(strict=True)
    manifest = {
        "version": VERSION, "platform": args.platform, "runtimeIdentifier": args.rid,
        "frameworkCommit": git_head(framework), "galleryCommit": git_head(gallery),
    }
    label = {"osx-arm64": "macos-arm64", "android-arm64": "android-arm64", "android-x64": "android-x64"}.get(args.rid, args.rid)
    name = f"Jalium.Gallery-{VERSION}-{label}"
    if args.platform == "android":
        abi = {"android-arm64": "arm64-v8a", "android-x64": "x86_64"}[args.rid]
        with zipfile.ZipFile(published) as apk:
            entries = apk.namelist()
            abis = {p.split("/")[1] for p in entries if p.startswith("lib/") and p.endswith(".so")}
            if abis != {abi}:
                raise ValueError(f"Expected only {abi} in {published.name}, found {sorted(abis)}")
            required = {f"lib/{abi}/libjalium.native.{library}.so" for library in
                        ("core", "platform", "media.core", "text", "vulkan", "software", "media")}
            missing = required.difference(entries)
            if missing:
                raise ValueError(f"APK native payload is incomplete: {sorted(missing)}")
        asset = output / (name + ".apk")
        shutil.copy2(published, asset)
        manifest["signing"] = "SDK evaluation signing; not a production signing identity"
        asset.with_suffix(".build.json").write_text(json.dumps(manifest, indent=2) + "\n")
    else:
        stage = output.parent / "staging" / name
        if stage.exists():
            raise FileExistsError(f"Refusing to reuse stale package staging: {stage}")
        stage.mkdir(parents=True)
        shutil.copy2(framework / "LICENSE", stage / "Jalium.UI-LICENSE.txt")
        gallery_license = gallery / "LICENSE"
        if gallery_license.is_file():
            shutil.copy2(gallery_license, stage / "Gallery-LICENSE.txt")
        if args.platform == "macos":
            with (published / "Contents/Info.plist").open("rb") as source:
                info = plistlib.load(source)
            if info["CFBundleShortVersionString"] != VERSION:
                raise ValueError("macOS bundle version does not match the release")
            subprocess.run(["ditto", str(published), str(stage / "Jalium Gallery.app")], check=True)
            instructions = "macOS 15+，Apple Silicon。解压后将 Jalium Gallery.app 拖入应用程序目录。\n此构建使用本地签名，尚未进行 Developer ID 签名或公证。\n"
        else:
            executable = "Jalium.UI.Gallery" if args.platform == "linux" else "Jalium.UI.Gallery.Desktop.exe"
            if not (published / executable).is_file():
                raise FileNotFoundError(published / executable)
            shutil.copytree(published, stage, dirs_exist_ok=True)
            if args.platform == "linux":
                required = [stage / f"libjalium.native.{library}.so" for library in
                            ("core", "platform", "media.core", "text", "vulkan", "software", "media")]
                if not all(p.is_file() for p in required):
                    raise ValueError("Linux native payload is incomplete")
                start = stage / "start.sh"
                start.write_text('#!/usr/bin/env bash\nset -euo pipefail\ncd -- "$(dirname -- "$0")"\nexport LD_LIBRARY_PATH="$PWD${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"\nexec ./Jalium.UI.Gallery "$@"\n')
                start.chmod(0o755)
                diagnostic = stage / "diagnose.sh"
                diagnostic.write_text('#!/usr/bin/env bash\nset -euo pipefail\ncd -- "$(dirname -- "$0")"\n./start.sh --diagnostics-only 2>&1 | tee gallery-diagnostics.txt\n')
                diagnostic.chmod(0o755)
                instructions = "Linux x64，glibc 2.31+。此包自带 .NET 运行时，请完整解压目录。\n启动：./start.sh\n软件渲染：JALIUM_RENDER_BACKEND=software ./start.sh\n诊断：./diagnose.sh\nFedora：sudo dnf install libX11 libXext libXrandr libXi libXcursor libwayland-client libwayland-cursor libxkbcommon fontconfig vulkan-loader\nArch：sudo pacman -S libx11 libxext libxrandr libxi libxcursor wayland libxkbcommon fontconfig vulkan-icd-loader\n视频示例另需 GStreamer 及对应解码插件；GPU 渲染需可用的 Vulkan 驱动。\n"
            else:
                instructions = "Windows 10/11 x64。完整解压后运行 Jalium.UI.Gallery.Desktop.exe。\n此构建未进行发行者代码签名。WebView 示例需要 Microsoft Edge WebView2 Runtime。\n"
        (stage / "README.txt").write_text(f"Jalium Gallery {VERSION}\n\n{instructions}\n源码版本见 build-info.json；下载校验见 .sha256 文件。\n", encoding="utf-8")
        (stage / "build-info.json").write_text(json.dumps(manifest, indent=2) + "\n")
        if args.platform == "linux":
            asset = output / (name + ".tar.gz")
            with tarfile.open(asset, "w:gz", format=tarfile.USTAR_FORMAT) as archive:
                archive.add(stage, arcname=name)
        elif args.platform == "macos":
            asset = output / (name + ".zip")
            subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(stage), str(asset)], check=True)
        else:
            asset = pathlib.Path(shutil.make_archive(str(output / name), "zip", stage.parent, stage.name))
    digest = hashlib.file_digest(asset.open("rb"), "sha256").hexdigest()
    asset.with_name(asset.name + ".sha256").write_text(f"{digest}  {asset.name}\n")
    print(json.dumps({"asset": str(asset), "bytes": asset.stat().st_size, "sha256": digest, **manifest}, indent=2))


if __name__ == "__main__":
    main()
