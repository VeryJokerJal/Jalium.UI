"""Build Window Lab with a complete dependency graph in an isolated directory."""

import argparse
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-root", required=True, type=Path)
    parser.add_argument("--native-root", required=True, type=Path)
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    args = parser.parse_args()

    # NuGet can omit ProjectReferences when /var and /private/var spellings mix
    # within one restore graph. Resolve the project and both payload paths first.
    repository = Path(__file__).resolve().parent.parent
    project = repository / ".tools/macos-window-lab/MacOSWindowLab.csproj"
    platform = repository / "src/packaging/Jalium.UI.MacOS/Jalium.UI.MacOS.csproj"
    build = args.build_root.resolve()
    native = args.native_root.resolve(strict=True)
    if not native.is_dir():
        parser.error("--native-root must be a directory")

    properties = [
        f"-p:JaliumBuildRoot={build}",
        f"-p:JaliumMacNativeRoot={native}",
        "-p:JaliumMacAutoCompleteNativePayload=false",
    ]
    subprocess.run(
        ["dotnet", "restore", str(project), "--force-evaluate", *properties],
        check=True,
    )
    assets = json.loads((build / "obj/MacOSWindowLab/project.assets.json").read_text())
    references = [
        Path(path).resolve()
        for framework in assets["project"]["restore"]["frameworks"].values()
        for path in framework.get("projectReferences", {})
    ]
    if platform not in references:
        raise RuntimeError("Window Lab restore omitted the macOS platform reference")

    subprocess.run(
        ["dotnet", "build", str(project), "--configuration", args.configuration,
         "--no-restore", *properties],
        check=True,
    )
    output = build / "bin/MacOSWindowLab" / args.configuration
    candidates = [
        output / "net10.0-macos/osx-arm64/MacOSWindowLab.app",
        build / "bin/MacOSWindowLab" / f"{args.configuration.lower()}_osx-arm64/MacOSWindowLab.app",
    ]
    app = next((candidate for candidate in candidates if candidate.is_dir()), None)
    if app is None:
        raise RuntimeError("Window Lab build did not produce an application bundle")
    if not (app / "Contents/MonoBundle/System.IO.Packaging.dll").is_file():
        raise RuntimeError("Window Lab bundle is missing its packaging runtime dependency")
    print(json.dumps({"application": str(app), "buildRoot": str(build)}, indent=2))


if __name__ == "__main__":
    main()
