#!/usr/bin/env python3
"""Exercise target selection and Mach-O/ABI rejection with actual native files."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

ARCHITECTURES = {'osx-arm64': 'arm64', 'osx-x64': 'x86_64'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--native-payload', type=Path, required=True)
    parser.add_argument('--runtime-identifier', choices=ARCHITECTURES, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    source, native, results = (p.resolve() for p in [args.source, args.native_payload, args.results_directory])
    if source in results.parents or results in source.parents or native in results.parents:
        parser.error('Use an isolated results directory outside source and native inputs')
    results.mkdir(parents=True, exist_ok=False)
    rows = []

    def run(label, command, expected=0, contains=None, env=None):
        completed = subprocess.run(command, cwd=source, capture_output=True, text=True, env=env)
        log = completed.stdout + completed.stderr
        (results / (label + '.log')).write_text(log)
        if (expected == 0) != (completed.returncode == 0) or (contains and contains not in log):
            raise RuntimeError(label + ' did not reach its expected result: ' + log[-1000:])
        rows.append({'case': label, 'exitCode': completed.returncode, 'expectedSuccess': expected == 0})
        return completed.stdout

    checker = source / 'eng/apple/check-native-exports.py'
    rid = args.runtime_identifier
    other = next(value for value in ARCHITECTURES if value != rid)
    check = ['python3', str(checker), str(native), '--repo-root', str(source), '--runtime-identifier', rid]
    run('valid-input', check)
    fixture = results / 'payload'
    fixture.mkdir()
    shutil.copy2(native / '.jalium-native-complete', fixture / '.jalium-native-complete')
    libraries = sorted(native.glob('*.dylib'))
    if not libraries:
        raise ValueError('A real, complete native payload is required')
    for library in libraries:
        (fixture / library.name).symlink_to(library)
    fixture_check = ['python3', str(checker), str(fixture), '--repo-root', str(source), '--runtime-identifier']
    run('wrong-cpu', fixture_check + [other], expected=1, contains='required ' + other + ' CPU slice')

    # Compile the opposing CPU without executing it; this works without Rosetta.
    stub_source = results / 'sentinel.c'
    stub_source.write_text('int JaliumArchitectureSentinel(void) { return 1; }\n')
    stub = results / 'sentinel.dylib'
    run('compile-opposing-cpu', ['xcrun', 'clang', '-dynamiclib', '-arch', ARCHITECTURES[other],
                                '-mmacosx-version-min=15.0', str(stub_source), '-o', str(stub)])
    extra = fixture / 'libextra.dylib'
    shutil.copy2(stub, extra)
    run('extra-module-wrong-cpu', fixture_check + [rid], expected=1, contains='libextra.dylib: does not contain')
    extra.unlink()

    # A union of nm exports would falsely let the valid slice hide missing
    # entry points in the opposing slice of this real universal Mach-O input.
    for library in libraries:
        combined = fixture / library.name
        combined.unlink()
        run('universal-' + library.stem, ['lipo', '-create', str(library), str(stub), '-output', str(combined)])
    run('universal-valid-selected-slice', fixture_check + [rid])
    run('universal-invalid-selected-abi', fixture_check + [other], expected=1,
        contains='libjalium.native.core.dylib: missing native entry points:')

    for selected in ARCHITECTURES:
        project = source / 'src/packaging/Jalium.UI.MacOS/Jalium.UI.MacOS.csproj'
        command = [args.dotnet, 'msbuild', str(project), '-nologo', '-p:RuntimeIdentifier=' + selected,
                   '-p:JaliumBuildRoot=' + str(results / 'msbuild'),
                   '-getProperty:RuntimeIdentifier,JaliumMacNativeRoot,JaliumMacNativeBuildDirectory']
        properties = json.loads(run('sdk-properties-' + selected, command))['Properties']
        if '/' + selected + '/' not in properties['JaliumMacNativeRoot'].replace('\\', '/'):
            raise ValueError('SDK native payload is not selected by its target RID')
        suffix = 'x64' if selected == 'osx-x64' else 'arm64'
        if not properties['JaliumMacNativeBuildDirectory'].endswith('apple-macos-dev-' + suffix):
            raise ValueError('SDK completion build is not selected by its target RID')
        target = command[:-1] + ['-target:ValidateMacOSNativeExports', '-p:JaliumMacNativeRoot=' + str(native)]
        run('sdk-native-validation-' + selected, target, expected=0 if selected == rid else 1,
            contains=None if selected == rid else 'required ' + selected + ' CPU slice')
        command[-1] = '-getItem:Content'
        command.insert(-1, '-p:JaliumMacNativeRoot=' + str(native))
        items = json.loads(run('sdk-runtime-items-' + selected, command))['Items']['Content']
        native_items = [item for item in items if Path(item['Identity']).suffix in ('.dylib', '.metallib')]
        if not native_items or any(item.get('Pack') != 'false' for item in native_items):
            raise ValueError('Runtime copying must not implicitly pack the current CPU')
        command[-1] = '-getItem:None'
        command.insert(-1, '-target:CollectMacOSNativePackageAssets')
        output = run('sdk-package-items-' + selected, command, expected=0 if selected == rid else 1,
                     contains=None if selected == rid else 'required ' + selected + ' CPU slice')
        if selected == rid:
            items = json.loads(output)['Items']['None']
            files = [item for item in items if item.get('Pack') == 'true' and
                     (Path(item['Identity']).suffix in ('.dylib', '.metallib') or
                      Path(item['Identity']).name == 'jalium_metal_manifest.json')]
            if not files or any(item.get('PackagePath') != 'runtimes/' + selected + '/native' or
                                item.get('CopyToOutputDirectory') != 'Never' for item in files):
                raise ValueError('Pack-only native assets are not selected by the target RID')


    helper = source / 'eng/apple/resolve-apple-target.sh'
    command = ['bash', '-c', 'set -e; source "$1"; jalium_resolve_apple_target macos; printf "%s %s" "$rid" "$arch"',
               'architecture-check', str(helper)]
    for selected, architecture in ARCHITECTURES.items():
        env = dict(os.environ, JALIUM_APPLE_ARCHITECTURE=architecture)
        if run('shell-selection-' + selected, command, env=env) != selected + ' ' + architecture:
            raise ValueError('Shell target selection differs from SDK RID selection')
    run('shell-invalid-architecture', command, expected=1, contains='unsupported macOS architecture',
        env=dict(os.environ, JALIUM_APPLE_ARCHITECTURE='invalid'))
    presets = {item['name'] for item in json.loads((source / 'src/native/CMakePresets.json').read_text())['configurePresets']}
    embedded = {'ios': 'ios-arm64', 'iossimulator': 'iossimulator-arm64',
                'tvos': 'tvos-arm64', 'tvossimulator': 'tvossimulator-arm64',
                'visionos': 'visionos-arm64', 'visionsimulator': 'visionossimulator-arm64'}
    for target, expected_rid in embedded.items():
        selected = run('shell-embedded-' + target, ['bash', '-c',
            'set -e; source "$1"; jalium_resolve_apple_target "$2"; printf "%s %s %s" "$rid" "$arch" "$preset"',
            'architecture-check', str(helper), target], env=dict(os.environ, JALIUM_APPLE_ARCHITECTURE='arm64'))
        expected_preset = 'apple-' + expected_rid
        if selected != expected_rid + ' arm64 ' + expected_preset or expected_preset not in presets:
            raise ValueError('Embedded Apple target alias or existing preset changed: ' + target)
    receipt = {'passed': True, 'runtimeIdentifier': rid, 'actualInputLibraries': len(libraries), 'cases': rows,
               'executedOpposingArchitecture': False, 'physicalInput': False}
    (results / 'receipt.json').write_text(json.dumps(receipt, indent=2))
    print(json.dumps({'passed': True, 'cases': len(rows), 'runtimeIdentifier': rid,
                      'executedOpposingArchitecture': False}))


if __name__ == '__main__':
    main()
