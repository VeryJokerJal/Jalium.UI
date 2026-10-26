#!/usr/bin/env python3
"""Apply checked, repeatable build adjustments to the pinned DXC checkout."""

import argparse
from pathlib import Path


def replace_once(source: str, original: str, replacement: str, label: str) -> str:
    if replacement in source:
        return source
    if source.count(original) != 1:
        raise ValueError(f"Pinned DXC declaration changed: {label}")
    return source.replace(original, replacement, 1)


def target_conditions(source: str) -> str:
    return replace_once(source, '#include "Unix.h"\n',
                        '#include "Unix.h"\n#if defined(__APPLE__)\n'
                        '#include <TargetConditionals.h>\n#endif\n',
                        "Apple target conditions")


def patch(checkout: Path) -> list[Path]:
    relative = ["tools/clang/tools/dxcompiler/CMakeLists.txt",
                "lib/Support/Unix/Process.inc", "lib/Support/Unix/Program.inc",
                "lib/Support/Unix/Signals.inc", "utils/version/CMakeLists.txt"]
    originals = {name: (checkout / name).read_text(encoding="utf-8-sig") for name in relative}
    updated = dict(originals)
    name = relative[0]
    updated[name] = replace_once(originals[name],
        "add_clang_library(dxcompiler SHARED ${SOURCES})",
        "if (JALIUM_DXC_STATIC_RUNTIME)\n"
        "  add_clang_library(dxcompiler STATIC ${SOURCES})\n"
        "else()\n  add_clang_library(dxcompiler SHARED ${SOURCES})\nendif()",
        "static dxcompiler target")

    name = relative[1]
    updated[name] = replace_once(target_conditions(originals[name]),
        "#if defined(HAVE_MACH_MACH_H) && !defined(__GNU__)\n"
        "  // Disable crash reporting on Mac OS X 10.0-10.4",
        "#if defined(HAVE_MACH_MACH_H) && !defined(__GNU__) && "
        "(!defined(__APPLE__) || TARGET_OS_OSX)\n"
        "  // Disable crash reporting on Mac OS X 10.0-10.4",
        "desktop exception-port handling")

    name = relative[2]
    program = target_conditions(originals[name])
    program = replace_once(program,
        "  // Create a child process.\n  int child = fork();",
        "#if defined(__APPLE__) && !TARGET_OS_OSX && !TARGET_OS_IOS\n"
        "  // These SDKs prohibit the fork/exec fallback. In-process shader\n"
        "  // compilation remains available; callers receive a normal failure.\n"
        "  return !MakeErrMsg(ErrMsg, \"Process creation is unavailable on this Apple platform\", ENOSYS);\n"
        "#else\n  // Create a child process.\n  int child = fork();",
        "unavailable fork/exec fallback")
    updated[name] = replace_once(program,
        "  PI.Pid = child;\n\n  return true;\n}",
        "  PI.Pid = child;\n\n  return true;\n"
        "#endif // Apple fork/exec availability\n}",
        "fork/exec availability scope")

    name = relative[3]
    updated[name] = replace_once(target_conditions(originals[name]),
        "#if defined(__APPLE__) && defined(ENABLE_CRASH_OVERRIDES)\n"
        "  // Environment variable to disable any kind of crash dialog.",
        "#if defined(__APPLE__) && defined(ENABLE_CRASH_OVERRIDES) && TARGET_OS_OSX\n"
        "  // Environment variable to disable any kind of crash dialog.",
        "desktop crash-dialog exception ports")

    # Keep CMake paths unescaped until its generator writes the command. The
    # native-path conversion inserts a literal backslash before spaces, which
    # Ninja later normalizes into a different output path on macOS.
    name = relative[4]
    updated[name] = replace_once(originals[name],
        'FILE(TO_NATIVE_PATH "${output_file}.gen" gen_file)',
        'set(gen_file "${output_file}.gen")',
        "version include path with spaces")

    # Check every pinned anchor before modifying the dependency checkout.
    changed = []
    for name in relative:
        if originals[name] != updated[name]:
            path = checkout / name
            path.write_text(updated[name], encoding="utf-8")
            changed.append(path)
    return changed


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkout", type=Path)
    args = parser.parse_args()
    for path in patch(args.checkout):
        print(f"Adjusted pinned DXC: {path.relative_to(args.checkout)}")


if __name__ == "__main__":
    main()
