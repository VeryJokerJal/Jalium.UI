#!/usr/bin/env python3
"""Build the pinned DXC version targets with real CMake paths containing spaces."""
import argparse
import json
from pathlib import Path
import subprocess
import sys


def literal(value):
    delimiter = '='
    while ']' + delimiter + ']' in str(value):
        delimiter += '='
    return '[' + delimiter + '[' + str(value) + ']' + delimiter + ']'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--checkout', type=Path, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--cmake', default='cmake')
    args = parser.parse_args()
    checkout, results = args.checkout.resolve(), args.results_directory.resolve()
    if checkout == results or checkout in results.parents or results in checkout.parents:
        parser.error('Keep the probe outside its dependency source checkout')
    results.mkdir(parents=True, exist_ok=False)
    project, build = results / 'probe source', results / 'build with spaces'
    project.mkdir()
    (project / 'CMakeLists.txt').write_text(
        'cmake_minimum_required(VERSION 3.28)\nproject(JaliumShaderVersionPaths NONE)\n'
        'set(Python3_EXECUTABLE ' + literal(sys.executable) + ')\n'
        'set(HLSL_EMBED_VERSION ON)\n'
        'set(HLSL_VERSION_LOCATION "${CMAKE_BINARY_DIR}/generated with spaces")\n'
        'file(MAKE_DIRECTORY "${HLSL_VERSION_LOCATION}")\n'
        'add_subdirectory(' + literal(checkout / 'utils/version') + ' "${CMAKE_BINARY_DIR}/version rules")\n')
    for label, command in [
        ('configure', [args.cmake, '-S', str(project), '-B', str(build), '-G', 'Ninja']),
        ('build', [args.cmake, '--build', str(build), '--target',
                   'hlsl_dxcversion_autogen', 'hlsl_version_autogen', '--parallel', '1']),
    ]:
        with (results / (label + '.log')).open('w') as stream:
            subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, check=True)
    files = []
    for name in ['dxcversion.inc', 'version.inc']:
        path = build / 'generated with spaces' / name
        text = path.read_text()
        if '#define HLSL_VERSION_MACRO' not in text:
            raise ValueError('DXC did not generate the expected version include: ' + str(path))
        files.append({'name': name, 'bytes': path.stat().st_size})
    receipt = {'passed': True, 'generatedFiles': files, 'realPinnedGenerator': True}
    (results / 'receipt.json').write_text(json.dumps(receipt, indent=2))
    print(json.dumps(receipt))


if __name__ == '__main__':
    main()
