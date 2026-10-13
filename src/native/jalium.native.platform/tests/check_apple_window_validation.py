#!/usr/bin/env python3
"""Exercise the real CMake completion verifier with a controlled launcher."""
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile

ACTOR = r'''
import json
import os
from pathlib import Path
import sys
mode = os.environ["JALIUM_WINDOW_PROTOCOL_CASE"]
directory = Path(sys.argv[sys.argv.index("--window-validation-directory") + 1])
nonce = sys.argv[sys.argv.index("--window-validation-nonce") + 1]
if mode == "launch_error":
    sys.exit(9)
if mode == "missing":
    sys.exit(0)
pid = os.getpid()
record = {"schema": 1, "nonce": nonce, "pid": pid, "result": 0}
if mode == "wrong_nonce": record["nonce"] = "earlier-run"
if mode == "failed_test": record["result"] = 1
if mode == "invalid_pid": record["pid"] = 0
if mode == "boolean_result": record["result"] = False
if mode == "string_schema": record["schema"] = "1"
if mode == "missing_field": del record["nonce"]
logged_pid = pid + 1 if mode == "wrong_host_pid" else pid
lines = [
    f"HOST_STARTED pid={logged_pid} bundle=com.jalium.ui.validation.window-contracts",
    f"HOST_FINISHED pid={logged_pid} result=0",
    f"HOST_EXIT pid={logged_pid} result=0",
]
if mode == "missing_start": lines = lines[1:]
if mode == "missing_finish": del lines[1]
if mode == "missing_exit": lines = lines[:2]
if mode == "fatal_after_record": lines.append("FAIL: Window validation application watchdog")
(directory / "host.log").write_text("\n".join(lines) + "\n")
(directory / "result.json").write_text("{broken" if mode == "malformed" else json.dumps(record))
'''

def main():
    cmake, application, verifier = sys.argv[1:]
    cases = [
        "valid", "launch_error", "missing", "wrong_nonce", "failed_test",
        "invalid_pid", "boolean_result", "string_schema", "missing_field",
        "malformed", "missing_start", "missing_finish", "missing_exit",
        "wrong_host_pid", "fatal_after_record",
    ]
    with tempfile.TemporaryDirectory(prefix="jalium-window-protocol-") as temporary:
        directory = Path(temporary)
        actor = directory / "actor.py"
        actor.write_text(ACTOR)
        launcher = directory / "launcher"
        launcher.write_text("#!/bin/sh\nexec " + shlex.quote(sys.executable) +
            " " + shlex.quote(str(actor)) + ' "$@"\n')
        launcher.chmod(0o700)
        runs = directory / "runs"
        for case in cases:
            environment = dict(os.environ, JALIUM_WINDOW_PROTOCOL_CASE=case)
            result = subprocess.run([
                cmake, "-DHOST_APP=" + application, "-DHOST_WORK_DIR=" + str(runs),
                "-DHOST_LAUNCHER=" + str(launcher), "-P", verifier,
            ], capture_output=True, text=True, env=environment, timeout=10)
            if (result.returncode == 0) != (case == "valid"):
                print(result.stdout + result.stderr, file=sys.stderr)
                raise AssertionError(case)
            if list(runs.iterdir()):
                raise AssertionError("Completion files leaked: " + case)
            print("PASS completion protocol: " + case)
    print("macOS Window completion protocol: 15/15 passed")

if __name__ == "__main__":
    main()
