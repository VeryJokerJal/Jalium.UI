#!/usr/bin/env python3
"""Build and verify a native macOS Window slice without treating translation as hardware evidence."""
import argparse
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET

PATTERN = r'^jalium[.]native[.]platform[.](clipboard|macos[.](window|thread-affinity)([.].*)?)$'
TARGETS = ['jalium.native.platform.clipboard.tests', 'jalium.native.platform.thread-affinity.tests',
           'jalium.native.platform.window.tests', 'jalium.native.platform.window.accessibility.tests',
           'jalium.native.platform.window.popup-keyboard.tests', 'jalium.native.platform.window.keyboard-focus.tests',
           'jalium.native.platform.window.owner.tests', 'jalium.native.platform.window.resize.tests',
           'jalium.native.platform.window.menu.tests', 'jalium.native.platform.window.startup.tests',
           'jalium.native.platform.window.drag.tests']
REQUIRED = {'jalium.native.platform.clipboard', 'jalium.native.platform.macos.window',
            *('jalium.native.platform.macos.thread-affinity.' + case for case in
              ['cold-create', 'cold-poll', 'initialized-create', 'initialized-poll']),
            *('jalium.native.platform.macos.window.' + case for case in
              ['properties', 'settings-notifications', 'style-reentrancy', 'scalar-reentrancy',
               'creation-lifetime', 'creation-geometry', 'owner', 'keyboard-focus', 'popup-keyboard',
               'resize-drag', 'system-menu', 'startup', 'drag-lifetime', 'text-input-lifetime'])}
CPU = {'arm64': 0x100000c, 'x86_64': 0x1000007}


def check_native_host(host, architecture, major=None):
    if host['translated'] or host['architecture'] != architecture:
        raise ValueError('Native matching hardware is required; translation cannot qualify this run')
    if major and int(host['macos'].split('.')[0]) != major:
        raise ValueError('Unexpected macOS host major version')


def native_rid(cache, architecture):
    expected = 'osx-' + ('x64' if architecture == 'x86_64' else 'arm64')
    values = [line.split('=', 1)[1] for line in cache.splitlines() if line.startswith('JALIUM_NATIVE_RID:')]
    if values != [expected]:
        raise ValueError('Configured native RID does not match the Apple target architecture')
    return expected


def configuration_controls(args, source, build, native, results):
    cases = [('arm64', '', 'osx-arm64'), ('x86_64', '', 'osx-x64'),
             ('arm64', 'osx-arm64', 'osx-arm64'), ('x86_64', 'osx-x64', 'osx-x64'),
             ('arm64', 'osx-x64', None), ('x86_64', 'osx-arm64', None),
             ('arm64;x86_64', '', None)]
    evidence = []
    for index, (architecture, requested, expected) in enumerate(cases):
        case_build = build.parent / (build.name + '-rid-' + str(index))
        command = [args.cmake, '-S', str(source / 'src/native'), '-B', str(case_build), '-G', 'Unix Makefiles',
                   '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_OSX_ARCHITECTURES=' + architecture,
                   '-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0', '-DJALIUM_NATIVE_RID=' + requested,
                   '-DJALIUM_NATIVE_OUTPUT_ROOT=' + str(native.parent / (native.name + '-rid-' + str(index))),
                   '-DJALIUM_METAL_REQUIRE_RUNTIME_SHADER_COMPILER=OFF']
        log = results / ('rid-' + str(index) + '.log')
        code = invoke(command, log, allow_failure=True)
        if expected:
            if code or native_rid((case_build / 'CMakeCache.txt').read_text(), architecture) != expected:
                raise ValueError('Positive native RID configuration failed')
        else:
            message = ' '.join(log.read_text().split())
            wanted = 'Build one Apple architecture per native RID' if ';' in architecture else 'does not match the target'
            if not code or wanted not in message:
                raise ValueError('Invalid Apple architecture/RID was not rejected at its intended guard')
        evidence.append({'architecture': architecture, 'requested': requested, 'expected': expected, 'exitCode': code})
    return evidence


def image_identity(data, architecture):
    if len(data) < 32 or struct.unpack_from('<I', data)[0] != 0xfeedfacf:
        raise ValueError('Expected a thin 64-bit Mach-O image')
    header = struct.unpack_from('<8I', data)
    if header[1] != CPU[architecture]:
        raise ValueError('Mach-O CPU does not match requested architecture')
    offset = 32
    minimum = None
    for _ in range(header[4]):
        if offset + 8 > len(data):
            raise ValueError('Truncated load command')
        command, length = struct.unpack_from('<II', data, offset)
        if length < 8 or offset + length > len(data):
            raise ValueError('Invalid load command length')
        if command == 0x32:
            if length < 24:
                raise ValueError('Truncated build-version command')
            target_platform, minimum = struct.unpack_from('<II', data, offset + 8)
            if target_platform != 1:
                raise ValueError('Image is not a macOS slice')
        offset += length
    if minimum is None or minimum > 0x000f0000:
        raise ValueError('Missing or excessive macOS build minimum')
    return {'cpu': header[1], 'subcpu': header[2], 'minimum': minimum}


def inventory_names(document):
    names = [case['name'] for case in document['tests']]
    if len(set(names)) != len(names) or not REQUIRED <= set(names):
        raise ValueError('Missing or duplicate required Window contract')
    if not all(re.fullmatch(PATTERN, name) for name in names):
        raise ValueError('Unexpected test in Window inventory')
    return names


def junit_counts(document, names):
    root = ET.fromstring(document)
    cases = root.findall('.//testcase')
    actual = [case.get('name') for case in cases]
    if len(actual) != len(set(actual)) or set(actual) != set(names):
        raise ValueError('JUnit case names do not match the complete CTest inventory')
    for case in cases:
        if case.get('status', 'run') != 'run' or any(case.find(tag) is not None for tag in ['failure', 'error', 'skipped']):
            raise ValueError('A Window contract failed, was skipped or did not execute')
    for suite in ([root] if root.tag == 'testsuite' else root.findall('.//testsuite')):
        for attribute in ['failures', 'errors', 'skipped', 'disabled']:
            if int(suite.get(attribute, '0')) != 0:
                raise ValueError('Nonzero JUnit failure or skip counter')
    if root.get('tests') is not None and int(root.get('tests')) != len(cases):
        raise ValueError('JUnit total does not match executed case names')
    if not cases:
        raise ValueError('No Window contracts executed')
    return {'total': len(cases), 'passed': len(cases), 'failed': 0, 'skipped': 0}


def invoke(command, log, allow_failure=False):
    print('RUN ' + ' '.join(map(str, command)), flush=True)
    with log.open('w') as stream:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        for line in process.stdout:
            stream.write(line)
            print(line, end='', flush=True)
        code = process.wait()
    if code and not allow_failure:
        raise RuntimeError('Command failed with exit code ' + str(code) + ': ' + str(log))
    return code


def self_test():
    good = struct.pack('<8I', 0xfeedfacf, CPU['x86_64'], 3, 2, 1, 24, 0, 0)
    good += struct.pack('<6I', 0x32, 24, 1, 0x000f0000, 0x001a0000, 0)
    assert image_identity(good, 'x86_64')['minimum'] == 0x000f0000
    rejected = 0
    for data, architecture in [(good, 'arm64'), (b'\xca\xfe\xba\xbe' + good, 'x86_64'),
                               (good[:44] + struct.pack('<I', 0x001a0000) + good[48:], 'x86_64'),
                               (good[:36] + struct.pack('<I', 0) + good[40:], 'x86_64')]:
        try:
            image_identity(data, architecture)
        except ValueError:
            rejected += 1
    assert rejected == 4
    names = ['one', 'two']
    good_xml = '<testsuite tests="2"><testcase name="one" status="run"/><testcase name="two" status="run"/></testsuite>'
    assert junit_counts(good_xml, names)['passed'] == 2
    bad = [good_xml.replace('<testcase name="two" status="run"/>', ''),
           good_xml.replace('name="two"', 'name="one"'), good_xml.replace('status="run"', 'status="notrun"', 1),
           good_xml.replace('tests="2"', 'tests="2" skipped="1"'), good_xml.replace('tests="2"', 'tests="3"')]
    bad += [good_xml.replace('<testcase name="one" status="run"/>', '<testcase name="one"><' + tag + '/></testcase>')
            for tag in ['failure', 'error', 'skipped']]
    rejected = 0
    for data in bad:
        try:
            junit_counts(data, names)
        except ValueError:
            rejected += 1
    assert rejected == len(bad)
    host = {'architecture': 'x86_64', 'translated': False, 'macos': '15.7.9'}
    check_native_host(host, 'x86_64', 15)
    assert native_rid('JALIUM_NATIVE_RID:STRING=osx-x64', 'x86_64') == 'osx-x64'
    assert inventory_names({'tests': [{'name': name} for name in sorted(REQUIRED)]})
    controls = [lambda: check_native_host({**host, 'translated': True}, 'x86_64', 15),
                lambda: check_native_host(host, 'arm64', 15), lambda: check_native_host(host, 'x86_64', 26),
                lambda: native_rid('JALIUM_NATIVE_RID:STRING=osx-arm64', 'x86_64'),
                lambda: native_rid('', 'x86_64'),
                lambda: inventory_names({'tests': [{'name': name} for name in sorted(REQUIRED)[1:]]}),
                lambda: inventory_names({'tests': [{'name': name} for name in sorted(REQUIRED)] * 2}),
                lambda: inventory_names({'tests': [{'name': name} for name in sorted(REQUIRED) + ['unrelated']]})]
    rejected = 0
    for control in controls:
        try:
            control()
        except ValueError:
            rejected += 1
    assert rejected == len(controls)
    print('Architecture/host/RID/JUnit controls: 5 positive and 20 negative checks passed.')


def qualify_appkit(args, source, build, results):
    app = build / 'appkit-environment/JaliumAppKitTransitions.app'
    executable = app / 'Contents/MacOS/JaliumAppKitTransitions'
    executable.parent.mkdir(parents=True, exist_ok=False)
    info = {'CFBundleExecutable': executable.name, 'CFBundleIdentifier': 'com.jalium.ui.validation.appkit-transitions',
            'CFBundleName': 'Jalium AppKit Transitions', 'CFBundlePackageType': 'APPL',
            'CFBundleVersion': '1', 'LSMinimumSystemVersion': '15.0', 'NSHighResolutionCapable': True}
    (app / 'Contents/Info.plist').write_bytes(plistlib.dumps(info))
    invoke(['xcrun', 'clang++', '-std=c++20', '-fobjc-arc', '-arch', args.architecture,
            '-mmacosx-version-min=15.0', '-framework', 'AppKit',
            str(source / 'eng/apple/window-appkit-transitions.mm'), '-o', str(executable)], results / 'appkit-build.log')
    identity = image_identity(executable.read_bytes(), args.architecture)
    invoke(['codesign', '--force', '--sign', '-', str(app)], results / 'appkit-sign.log')
    invoke([args.cmake, '-DHOST_APP=' + str(app), '-DHOST_WORK_DIR=' + str(results / 'appkit-protocol'),
            '-P', str(source / 'src/native/jalium.native.platform/tests/run_apple_window_validation.cmake')],
           results / 'appkit-transitions.log')
    return {'passed': True, 'frameworkLinked': False, 'identity': identity}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('--architecture', choices=CPU)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--build-directory', type=Path)
    parser.add_argument('--native-root', type=Path)
    parser.add_argument('--results-directory', type=Path)
    parser.add_argument('--build-only', action='store_true')
    parser.add_argument('--expected-macos-major', type=int)
    parser.add_argument('--expected-xcode')
    parser.add_argument('--cmake', default='cmake')
    parser.add_argument('--ctest', default='ctest')
    parser.add_argument('--jobs', type=int, default=4)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if platform.system() != 'Darwin' or not args.architecture or not all(
            [args.build_directory, args.native_root, args.results_directory]):
        parser.error('Supply a macOS architecture and isolated build/native/results directories')
    source = args.source.resolve()
    build, native, results = [p.resolve() for p in [args.build_directory, args.native_root, args.results_directory]]
    if len({source, build, native, results}) != 4 or any(source == p or source in p.parents for p in [build, native, results]):
        parser.error('Build/native/results directories must be distinct and outside the source tree')
    results.mkdir(parents=True, exist_ok=False)
    translated = subprocess.run(['sysctl', '-n', 'sysctl.proc_translated'], capture_output=True, text=True)
    xcode = subprocess.check_output(['xcodebuild', '-version'], text=True).strip()
    host = {'architecture': platform.machine(), 'macos': platform.mac_ver()[0],
            'translated': translated.returncode == 0 and translated.stdout.strip() == '1', 'xcode': xcode}
    report = {'host': host, 'requestedArchitecture': args.architecture, 'buildOnly': args.build_only,
              'executed': False, 'passed': False, 'physicalInput': False, 'source': str(source)}
    receipt = results / 'receipt.json'
    try:
        if args.expected_xcode and xcode.splitlines()[0] != 'Xcode ' + args.expected_xcode:
            raise ValueError('Unexpected Xcode toolchain')
        if not args.build_only:
            check_native_host(host, args.architecture, args.expected_macos_major)
            report['appkitEnvironment'] = qualify_appkit(args, source, build, results)
        report['ridControls'] = configuration_controls(args, source, build, native, results)
        invoke([args.cmake, '-S', str(source / 'src/native'), '-B', str(build), '-G', 'Unix Makefiles',
                '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_OSX_ARCHITECTURES=' + args.architecture,
                '-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0', '-DJALIUM_PLATFORM_BUILD_TESTS=ON',
                '-DJALIUM_METAL_REQUIRE_RUNTIME_SHADER_COMPILER=OFF', '-DJALIUM_NATIVE_OUTPUT_ROOT=' + str(native)],
               results / 'configure.log')
        rid = native_rid((build / 'CMakeCache.txt').read_text(), args.architecture)
        report['rid'] = rid
        invoke([args.cmake, '--build', str(build), '--parallel', str(args.jobs), '--target', *TARGETS], results / 'build.log')
        inventory = subprocess.check_output([args.ctest, '--test-dir', str(build), '-R', PATTERN, '--show-only=json-v1'], text=True)
        (results / 'inventory.json').write_text(inventory)
        names = inventory_names(json.loads(inventory))
        images = []
        for directory in [native, build / 'jalium.native.platform/validation-apps']:
            for path in directory.rglob('*'):
                if not path.is_file() or path.is_symlink():
                    continue
                if directory == native and path.relative_to(native).parts[0] != rid:
                    raise ValueError('Native output was written under another RID')
                with path.open('rb') as stream:
                    magic = stream.read(4)
                if magic in [b'\xcf\xfa\xed\xfe', b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca']:
                    images.append({'path': str(path), **image_identity(path.read_bytes(), args.architecture)})
        if not images or not any(Path(image['path']).name == 'libjalium.native.platform.dylib' for image in images):
            raise ValueError('Native platform slice is missing')
        report.update({'images': images, 'inventory': names})
        if args.build_only:
            report['buildPassed'] = True
            print('BUILD ONLY: binaries and inventory checked; no runtime qualification.', flush=True)
            return 0
        junit = results / 'window-contracts.xml'
        code = invoke([args.ctest, '--test-dir', str(build), '-R', PATTERN, '--no-tests=error',
                       '--output-on-failure', '--output-junit', str(junit), '-j', '1'], results / 'tests.log', allow_failure=True)
        report['executed'] = True
        report['counts'] = junit_counts(junit.read_text(), names)
        if code:
            raise ValueError('CTest failed with exit code ' + str(code))
        report['passed'] = True
        print(json.dumps({'host': host, 'counts': report['counts'], 'passed': True}), flush=True)
        return 0
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        receipt.write_text(json.dumps(report, indent=2))


if __name__ == '__main__':
    sys.exit(main())
