#!/usr/bin/env python3
"""Verify Window themes in an owned AppKit process; no system input or desktop capture."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import tempfile
import time

LIBRARIES = tuple("libjalium.native." + name + ".dylib" for name in
                  ("core", "text", "metal", "software", "platform", "media.core", "media", "browser"))
ASSEMBLIES = ("Jalium.UI.MacOS.HostSmoke.dll", "Jalium.UI.Managed.dll", "Jalium.UI.MacOS.dll",
              "Jalium.UI.Xaml.dll")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stop_owned_app(app):
    prefix = str(app / "Contents/MacOS") + "/"
    for line in subprocess.check_output(["ps", "-axo", "pid=,command="], text=True).splitlines():
        pid, _, command = line.strip().partition(" ")
        if command.startswith(prefix):
            try:
                os.kill(int(pid), signal.SIGTERM)
            except ProcessLookupError:
                pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--work-parent", type=Path, required=True)
    parser.add_argument("--results-file", type=Path, required=True)
    parser.add_argument("--image-directory", type=Path, required=True)
    parser.add_argument("--cases", default=",".join(map(str, range(8))))
    parser.add_argument("--expected-failure", help="require this parent assertion instead of a pass")
    parser.add_argument("--theme-only", action="store_true", help="isolate the parent implicit-style defect")
    args = parser.parse_args()
    source = args.app.resolve()
    cases = [int(value) for value in args.cases.split(",")]
    if not source.is_dir() or source.suffix != ".app" or not cases or len(set(cases)) != len(cases) or any(i not in range(8) for i in cases):
        parser.error("invalid app or cases")
    if args.theme_only and not args.expected_failure:
        parser.error("theme-only is a parent negative-control option")
    args.work_parent.mkdir(parents=True, exist_ok=True)
    args.image_directory.mkdir(parents=True, exist_ok=True)
    receipts = []
    with tempfile.TemporaryDirectory(prefix="jalium-window-themes-", dir=args.work_parent) as directory:
        work = Path(directory)
        (work / "tmp").mkdir()
        app = work / "HostSmoke.app"
        subprocess.run(["/usr/bin/ditto", str(source), str(app)], check=True)
        native, managed, paths = {}, {}, {}
        for filename in LIBRARIES + ASSEMBLIES:
            originals = list(source.rglob(filename))
            copies = [app / p.relative_to(source) for p in originals]
            if not originals or any(p.read_bytes() != q.read_bytes() for p, q in zip(originals, copies)):
                raise RuntimeError("copied payload differs: " + filename)
            paths[filename] = copies
            if filename in LIBRARIES:
                native[filename] = [digest(p) for p in copies]
            else:
                if len(copies) != 1:
                    raise RuntimeError("ambiguous implementation assembly: " + filename)
                managed[filename] = digest(copies[0])
        try:
            for index in cases:
                output = work / ("case-" + str(index))
                output.mkdir()
                command = ["/usr/bin/open", "-n", "-W", "--env", "JALIUM_WINDOW_THEMES_OUTPUT=" + str(output),
                           "--env", "TMPDIR=" + str(work / "tmp"), "--env", "DYLD_PRINT_LIBRARIES=1",
                           "--env", "JALIUM_WINDOW_THEMES_THEME_ONLY=" + ("1" if args.theme_only else "0"),
                           "--stdout", str(output / "app.log"), "--stderr", str(output / "app.err"),
                           str(app), "--args", "--window-theme-case=" + str(index)]
                started = time.monotonic()
                with (output / "launch.log").open("w") as log:
                    launcher = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
                    code = launcher.wait()
                host_path = output / "result.json"
                host = json.loads(host_path.read_text()) if host_path.exists() else None
                stderr = (output / "app.err").read_text(errors="replace") if (output / "app.err").exists() else ""
                loaded = [name for name in LIBRARIES if any(str(path) in stderr for path in paths[name])]
                foreign = [line for line in stderr.splitlines() if re.search(r"/libjalium\.native\..*\.dylib", line)
                           and not any(str(path) in line for name in LIBRARIES for path in paths[name])]
                failures = []
                case = host["cases"][0] if host and len(host.get("cases", [])) == 1 else None
                if case is None:
                    failures.append("completed single-case result missing")
                else:
                    if case["index"] != index or case["custom"] != bool(index % 2) or case["software"] != (index >= 4):
                        failures.append("requested style/backend case differs")
                    if case["width"] != (760 if index % 4 < 2 else 420):
                        failures.append("requested viewport differs")
                    if case["checkNull"] == args.theme_only:
                        failures.append("null-resource control mode differs")
                    observations = case.get("focus", [])
                    if not observations or any(not item["qualified"] or not item["appActive"] or item["processId"] != item["frontmost"]
                                               or not item["nativeKey"] or item["nativeKey"] != item["ownWindow"]
                                               or not item["responder"] or item["responder"] != item["ownResponder"] for item in observations):
                        failures.append("foreground/key/responder qualification failed")
                    if args.expected_failure:
                        if case["passed"] or args.expected_failure not in (case.get("failure") or ""):
                            failures.append("parent assertion did not reproduce")
                    else:
                        if not case["passed"] or host["passed"] != 1:
                            failures.append(case.get("failure") or "case did not pass")
                        if [item["phase"] for item in observations] != list(range(14)):
                            failures.append("fourteen complete qualified phases missing")
                        panels = case.get("panelInspections", [])
                        if len(panels) != 1 or any(not item["beforeCancellation"] or not item["visible"] or not item["parent"] or
                                                    item["parent"] != item["owner"] or item["actual"] != item["expected"] for item in panels):
                            failures.append("one visible owned panel inspection before cancellation required")
                        if sorted(item["phase"] for item in case.get("readbacks", [])) != ["dark", "light", "switched"]:
                            failures.append("three opaque palette readback measurements required")
                    if host.get("renderThreadOverride") is not None:
                        failures.append("normal render thread was overridden")
                    if host.get("physicalInput") or host.get("desktopScreenshot") or not host.get("offscreenRendering"):
                        failures.append("evidence boundary differs")
                    expected_managed = [paths[name][0] for name in ASSEMBLIES[:3]]
                    actual_managed = [Path(path) for path in host.get("managedLoadPaths", [])]
                    if any(path not in actual_managed for path in expected_managed) or any(not path.is_relative_to(app) for path in actual_managed):
                        failures.append("implementation assembly load paths differ")
                if len(loaded) != 8 or foreign:
                    failures.append("eight private native load paths required; no foreign native payloads")
                images = {}
                for path in output.glob("*.png"):
                    destination = args.image_directory / ("case-" + str(index) + "-" + path.name)
                    shutil.copy2(path, destination)
                    images[str(destination)] = digest(destination)
                phases = ("failure",) if args.expected_failure else ("light", "dark", "switched")
                if any(not (args.image_directory / ("case-" + str(index) + "-" + str(index) + "-" + phase + ".png")).is_file() for phase in phases):
                    failures.append("required renderer readbacks missing")
                receipt = {"index": index, "passed": not failures, "failures": failures, "hostResult": host,
                           "expectedFailure": args.expected_failure, "loadedNative": loaded, "foreignNative": foreign,
                           "launcherExitCode": code, "images": images, "seconds": round(time.monotonic() - started, 3),
                           "dyldSHA256": hashlib.sha256(stderr.encode()).hexdigest(),
                           "failureLogTails": {name: (output / name).read_text(errors="replace")[-6000:]
                                               for name in ("app.log", "app.err", "launch.log") if (output / name).exists()} if failures else {}}
                receipts.append(receipt)
                print(json.dumps({"case": index, "passed": not failures, "failures": failures,
                                  "checks": len(case["checks"]) if case else 0}), flush=True)
                if failures:
                    break
        finally:
            stop_owned_app(app)
        result = {"passed": sum(item["passed"] for item in receipts), "total": len(cases), "cases": receipts,
                  "nativeSHA256": native, "managedSHA256": managed, "temporaryRoot": str(work),
                  "physicalInput": False, "desktopScreenshot": False, "mixedDPIDevice": False}
    result["temporaryRootRemoved"] = not Path(result["temporaryRoot"]).exists()
    args.results_file.write_text(json.dumps(result, indent=2))
    print(json.dumps({key: result[key] for key in ("passed", "total", "temporaryRootRemoved")}), flush=True)
    return 0 if result["passed"] == result["total"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
