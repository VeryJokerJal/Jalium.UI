#!/usr/bin/env python3
"""Opt-in native file-panel acceptance through a separate, trusted AX client.

Pass a built Jalium.UI.MacOS.HostSmoke.app. This needs an unlocked macOS
session with Accessibility access for the launching terminal/application.
It does not request permission, synthesize keyboard input, or run in CI by default.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import signal
import subprocess
import tempfile
import time

LIBRARIES = tuple("libjalium.native." + name + ".dylib" for name in
                  ("core", "text", "metal", "software", "platform", "media.core", "media", "browser"))
ASSEMBLIES = ("Jalium.UI.MacOS.HostSmoke.dll", "Jalium.UI.Managed.dll", "Jalium.UI.MacOS.dll")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stop_owned_app(app):
    """A launcher timeout never authorizes killing an unrelated app or service."""
    prefix = str(app / "Contents/MacOS") + "/"
    rows = subprocess.check_output(["ps", "-axo", "pid=,command="], text=True)
    for row in rows.splitlines():
        pid, _, command = row.strip().partition(" ")
        if command.startswith(prefix):
            try:
                os.kill(int(pid), signal.SIGTERM)
            except ProcessLookupError:
                pass


def run_case(work, app, client, index, libraries):
    output = work / ("case-" + str(index))
    output.mkdir(mode=0o700)
    command = ["/usr/bin/open", "-n", "-W", "--env", "JALIUM_FILE_PANEL_OUTPUT=" + str(output),
               "--env", "DYLD_PRINT_LIBRARIES=1", "--env", "TMPDIR=" + str(work / "tmp"),
               "--stdout", str(output / "app.log"), "--stderr", str(output / "app.err"),
               str(app), "--args", "--window-file-panel-acceptance-case=" + str(index)]
    attempts, seen, errors = [], set(), []
    started = time.monotonic()
    with (output / "launcher.log").open("w") as log:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
        try:
            while process.poll() is None and time.monotonic() - started < 25:
                ready_path = output / "external-ready.json"
                if ready_path.exists():
                    try:
                        ready = json.loads(ready_path.read_text())
                    except json.JSONDecodeError:
                        time.sleep(.01)
                        continue
                    phase = ready["phase"]
                    if phase not in seen:
                        seen.add(phase)  # A failed or ambiguous action is never replayed.
                        call = [str(client), str(ready["processId"]), ready["intent"]]
                        if ready["intent"] != "cancel" and not ready["save"]:
                            call.append(json.dumps(ready["expectedNames"]))
                        elif phase == "retry" and ready["save"]:
                            call.append(ready["expectedNames"][0])
                        try:
                            action = subprocess.run(call, capture_output=True, text=True, timeout=6)
                            receipt = json.loads(action.stdout)
                            if receipt["targetPID"] != ready["processId"] or receipt["clientPID"] == ready["processId"]:
                                raise RuntimeError("AX client and owned host identities did not match")
                            receipt.update({"exitCode": action.returncode, "phase": phase, "hostReady": ready})
                            attempts.append(receipt)
                        except (subprocess.TimeoutExpired, ValueError, RuntimeError) as error:
                            errors.append(str(error))
                time.sleep(.02)
            if process.poll() is None:
                errors.append("owned application launcher exceeded its 25-second budget")
                stop_owned_app(app)
                process.terminate()
                process.wait(timeout=5)
        finally:
            if process.poll() is None:
                stop_owned_app(app)
                process.terminate()
                process.wait(timeout=5)

    host_path = output / "host-result.json"
    host = json.loads(host_path.read_text()) if host_path.exists() else None
    log = (output / "app.log").read_text(errors="replace") if (output / "app.log").exists() else ""
    err = (output / "app.err").read_text(errors="replace") if (output / "app.err").exists() else ""
    loaded = {name: any(str(path) in err for path in paths) for name, paths in libraries.items()}
    expected_actions = 2 if index // 2 in (1, 3, 4, 5) else 1
    correct = bool(host and host["finished"] and host["accepted"] is True and not host["failures"]
                   and host["externalAccessibility"] and host["actualMainLoop"]
                   and len(attempts) == expected_actions and all(loaded.values()) and not errors)
    for receipt in attempts:
        correct &= bool(receipt["trusted"] and receipt["exitCode"] == 0 and receipt["pressError"] == 0
                        and receipt["targetPID"] == (host or {}).get("processId"))
        if receipt["requestedNames"]:
            correct &= receipt["selectionError"] == 0 and receipt["selectionMatches"]
        if receipt["requestedName"]:
            correct &= receipt["writeError"] == 0 and receipt["readBack"] == receipt["requestedName"]
    # Keep the original 8-second host budget. The launcher budget also covers
    # process startup and exit; it is not an extension to the dialog assertions.
    return {"index": index, "passed": bool(correct), "seconds": round(time.monotonic() - started, 3),
            "host": host, "clients": attempts, "errors": errors, "nativeLoaded": loaded,
            "log": log, "stderrTail": err[-3000:] if not correct else "", "launcherExit": process.returncode}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--cases", default=",".join(map(str, range(12))))
    parser.add_argument("--work-parent", type=Path, default=Path.home() / "Library/Caches")
    parser.add_argument("--results-file", type=Path)
    args = parser.parse_args()
    source = args.app.resolve()
    if not source.is_dir() or source.suffix != ".app":
        parser.error("--app must name a built HostSmoke .app directory")
    try:
        cases = [int(value) for value in args.cases.split(",")]
    except ValueError:
        parser.error("--cases must contain integer indices")
    if not cases or len(set(cases)) != len(cases) or any(value not in range(12) for value in cases):
        parser.error("--cases must contain unique indices from 0 to 11")
    receipts = []
    with tempfile.TemporaryDirectory(prefix="jalium-file-panel-ax-", dir=args.work_parent) as name:
        work = Path(name)
        (work / "tmp").mkdir()
        app = work / "HostSmoke.app"
        subprocess.run(["/usr/bin/ditto", str(source), str(app)], check=True)
        libraries, library_hashes = {}, {}
        for filename in LIBRARIES:
            originals = list(source.rglob(filename))
            copies = list(app.rglob(filename))
            if not originals or len(originals) != len(copies):
                raise RuntimeError("Missing native payload: " + filename)
            for original in originals:
                if digest(original) != digest(app / original.relative_to(source)):
                    raise RuntimeError("Copied native payload changed: " + filename)
            libraries[filename] = copies
            library_hashes[filename] = [digest(path) for path in copies]
        assembly_hashes = {}
        for filename in ASSEMBLIES:
            originals = list(source.rglob(filename))
            if len(originals) != 1:
                raise RuntimeError("Expected one managed assembly: " + filename)
            original = originals[0]
            if digest(original) != digest(app / original.relative_to(source)):
                raise RuntimeError("Copied managed payload changed: " + filename)
            assembly_hashes[filename] = digest(original)
        client_source = Path(__file__).with_name("file-panel-ax-client.m")
        client = work / "file-panel-ax-client"
        subprocess.run(["xcrun", "clang", "-fobjc-arc", str(client_source), "-framework", "Foundation",
                        "-framework", "ApplicationServices", "-o", str(client)], check=True)
        try:
            for index in cases:
                result = run_case(work, app, client, index, libraries)
                receipts.append(result)
                print(json.dumps({"case": index, "passed": result["passed"],
                                  "checks": len((result["host"] or {}).get("checks", []))}), flush=True)
                if any(not client["trusted"] for client in result["clients"]):
                    break  # Missing system authorization is an environment gate, not twelve UI failures.
        finally:
            stop_owned_app(app)
        result = {"cases": receipts, "passed": sum(item["passed"] for item in receipts), "total": len(cases),
                  "physicalInput": False, "externalAX": True, "nativeSHA256": library_hashes,
                  "managedSHA256": assembly_hashes, "clientSHA256": digest(client),
                  "clientSourceSHA256": digest(client_source), "temporaryRoot": str(work)}
    result["temporaryRootRemoved"] = not Path(result["temporaryRoot"]).exists()
    if args.results_file:
        args.results_file.write_text(json.dumps(result, ensure_ascii=False))
    print(json.dumps({key: result[key] for key in ("passed", "total", "physicalInput", "externalAX",
                                                  "temporaryRootRemoved")}), flush=True)
    return 0 if result["passed"] == result["total"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
