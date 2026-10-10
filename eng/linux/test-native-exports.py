#!/usr/bin/env python3
"""Exercise ABI requirements without needing a Linux loader on the test host."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


sys.dont_write_bytecode = True
validator_path = Path(os.environ.get(
    "JALIUM_NATIVE_EXPORTS_TEST_VALIDATOR",
    str(Path(__file__).with_name("check-native-exports.py")),
))
spec = importlib.util.spec_from_file_location("native_export_validator", validator_path)
assert spec is not None and spec.loader is not None
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class LinuxExportContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="jalium-export-contract-")
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name)
        self.payload = self.repo / "payload"
        self.payload.mkdir()
        for filename in validator.LIBRARIES:
            (self.payload / filename).touch()
        self.header("core", "JALIUM_API", (
            "jalium_shared_init", "jalium_text_copy_outline_path",
        ))
        self.platform_header = self.header("platform", "JALIUM_PLATFORM_API", (
            "jalium_platform_init", "jalium_platform_apple_words_allowed",
            "jalium_apple_window_set_style", "jalium_apple_future_window",
            "jalium_android_future_window",
        ))
        self.header("media.core", "JALIUM_MEDIA_API", (
            *sorted(validator.MEDIA_CORE_SYMBOLS), "jalium_media_probe",
        ))
        self.header("text", "JALIUM_TEXT_API", (
            "jalium_text_probe", "jalium_text_copy_outline_path",
        ))
        managed = self.repo / "src/managed/Probe.cs"
        managed.parent.mkdir(parents=True)
        managed.write_text('''
class Probe {
    const string PlatformLibrary = "libjalium.native.platform.so";
    const string PlatformAlias = PlatformLibrary;
    [DllImport(PlatformAlias, EntryPoint = "jalium_platform_pinvoke_entry")]
    static extern int RenamedImport();
    [LibraryImport(PlatformLibrary)]
    static partial int jalium_platform_unlisted_import();
    [LibraryImport(PlatformAlias, EntryPoint = "jalium_apple_future_window")]
    static partial int AppleImport();
    [DllImport(PlatformAlias, EntryPoint = "jalium_android_future_window")]
    static extern int AndroidImport();
    [DllImport("unrelated.library", EntryPoint = "jalium_other_library")]
    static extern int UnrelatedImport();
}
''', encoding="utf-8")
        self.defined = {
            "core": {"jalium_shared_init"},
            "platform": {
                "jalium_platform_init", "jalium_platform_apple_words_allowed",
                "jalium_platform_pinvoke_entry", "jalium_platform_unlisted_import",
            },
            "media_core": set(validator.MEDIA_CORE_SYMBOLS),
            "media": {"jalium_media_probe"},
            "text": {"jalium_text_probe", "jalium_text_copy_outline_path"},
            "software": {"jalium_software_init"},
            "vulkan": {"jalium_vulkan_init"},
        }
        self.undefined = {library: set() for library in self.defined}

    def header(self, module: str, macro: str, symbols: tuple[str, ...]) -> Path:
        path = self.repo / f"src/native/jalium.native.{module}/include/api.h"
        path.parent.mkdir(parents=True)
        path.write_text("\n".join(f"{macro} int {symbol}(void);" for symbol in symbols),
                        encoding="utf-8")
        return path

    def validate(self) -> tuple[int, str]:
        def symbols(path: Path) -> tuple[set[str], set[str]]:
            library = validator.LIBRARIES[path.name]
            return self.defined[library], self.undefined[library]

        output = io.StringIO()
        with mock.patch.object(sys, "argv", [str(validator_path), str(self.payload), str(self.repo)]), \
                mock.patch.object(validator, "dynamic_symbols", side_effect=symbols), \
                mock.patch.object(validator, "demangle", return_value={}), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            result = validator.main()
        return result, output.getvalue()

    def test_header_keeps_shared_names_and_excludes_platform_namespaces(self) -> None:
        self.assertEqual({"jalium_platform_init", "jalium_platform_apple_words_allowed"},
                         validator.extract_api_symbols([self.platform_header], ("JALIUM_PLATFORM_API",)))

    def test_aliased_and_renamed_imports_keep_shared_abi_requirements(self) -> None:
        self.assertEqual({"jalium_platform_pinvoke_entry", "jalium_platform_unlisted_import"},
                         validator.extract_pinvokes(self.repo)["platform"])

    def test_linux_payload_does_not_require_apple_or_android_implementations(self) -> None:
        status, output = self.validate()
        self.assertEqual(0, status, output)
        self.assertIn("Validated 7 shared libraries", output)

    def test_new_platform_specific_declaration_does_not_require_allowlist_updates(self) -> None:
        with self.platform_header.open("a", encoding="utf-8") as file:
            file.write("\nJALIUM_PLATFORM_API int jalium_apple_future_capability(void);\n")
        status, output = self.validate()
        self.assertEqual(0, status, output)

    def test_missing_public_shared_export_is_rejected(self) -> None:
        self.defined["platform"].remove("jalium_platform_init")
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn("declared/PInvoke C ABI missing:", output)
        self.assertIn("jalium_platform_init", output)

    def test_missing_managed_only_import_is_rejected(self) -> None:
        self.defined["platform"].remove("jalium_platform_unlisted_import")
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn("jalium_platform_unlisted_import", output)
        self.assertIn("declared/PInvoke C ABI missing:", output)

    def test_new_shared_declaration_is_still_required(self) -> None:
        with self.platform_header.open("a", encoding="utf-8") as file:
            file.write("\nJALIUM_PLATFORM_API int jalium_platform_future_capability(void);\n")
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn("jalium_platform_future_capability", output)

    def assert_other_platform_export_is_rejected(self, symbol: str) -> None:
        self.defined["platform"].add(symbol)
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn(f"undeclared C ABI exports: {symbol}", output)

    def test_actual_apple_export_is_rejected(self) -> None:
        self.assert_other_platform_export_is_rejected("jalium_apple_window_set_style")

    def test_actual_android_export_is_rejected(self) -> None:
        self.assert_other_platform_export_is_rejected("jalium_android_future_window")

    def test_unrelated_public_export_is_rejected(self) -> None:
        self.defined["platform"].add("accidental_public_function")
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn("non-whitelisted exports: accidental_public_function", output)

    def test_cross_library_reference_must_be_provided_by_this_payload(self) -> None:
        self.undefined["platform"].add("jalium_shared_init")
        status, output = self.validate()
        self.assertEqual(0, status, output)
        self.defined["core"].remove("jalium_shared_init")
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn("unresolved Jalium cross-library ABI: jalium_shared_init", output)

    def test_missing_library_is_rejected(self) -> None:
        (self.payload / "libjalium.native.platform.so").unlink()
        status, output = self.validate()
        self.assertEqual(1, status)
        self.assertIn("Missing shared libraries: libjalium.native.platform.so", output)

    def test_readelf_preserves_global_weak_and_undefined_symbols(self) -> None:
        text = '''
  1: 00000000 20 FUNC GLOBAL DEFAULT 12 jalium_platform_init
  2: 00000020 20 FUNC WEAK DEFAULT 12 jalium_platform_weak@@JALIUM_1
  3: 00000000 0 NOTYPE GLOBAL DEFAULT UND jalium_shared_init
  4: 00000040 20 FUNC LOCAL DEFAULT 12 hidden_local
  5: 00000000 0 OBJECT GLOBAL DEFAULT ABS JALIUM_1
'''
        completed = subprocess.CompletedProcess([], 0, stdout=text)
        with mock.patch.object(validator.subprocess, "run", return_value=completed):
            defined, undefined = validator.dynamic_symbols(self.payload / "libjalium.native.platform.so")
        self.assertEqual({"jalium_platform_init", "jalium_platform_weak"}, defined)
        self.assertEqual({"jalium_shared_init"}, undefined)


if __name__ == "__main__":
    unittest.main(verbosity=2)
