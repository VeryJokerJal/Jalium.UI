"""Check copied native bytes and real dyld paths for fixtures that explicitly force Software."""
from pathlib import Path
import hashlib
import re

LIBRARIES = tuple('libjalium.native.' + name + '.dylib' for name in
                  ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser'))
REQUIRED = frozenset('libjalium.native.' + name + '.dylib' for name in ('core', 'text', 'software', 'platform'))
DYLD_PATH = re.compile(r'(/[^\r\n]*?/libjalium\.native\.[A-Za-z0-9_.-]+\.dylib)(?=$|\s)')

def capture_payload(libraries):
    if set(libraries) != set(LIBRARIES):
        raise ValueError('Exactly all eight copied native libraries must be present')
    result = {}
    for name, paths in libraries.items():
        if not paths:
            raise ValueError('Missing copied payload: ' + name)
        result[name] = {str(Path(p).resolve()): hashlib.sha256(Path(p).read_bytes()).hexdigest() for p in paths}
        if len(result[name]) != len(paths):
            raise ValueError('Duplicate copied payload path: ' + name)
    return result

def verify_software_payload(libraries, before, dyld_log):
    errors = []
    try:
        after = capture_payload(libraries)
        if after != before:
            errors.append('Copied native payload bytes changed during the case')
    except (OSError, ValueError) as exc:
        after = None
        errors.append('Copied native payload unavailable: ' + str(exc))
    observed = {}
    for match in DYLD_PATH.finditer(dyld_log):
        path = Path(match.group(1)).resolve()
        name = path.name
        observed.setdefault(name, set()).add(str(path))
        if str(path) not in before.get(name, {}):
            errors.append('Loaded native library outside the verified copied payload: ' + str(path))
    for name in sorted(REQUIRED):
        if not observed.get(name):
            errors.append('Required Software payload load not proven: ' + name)
    return dict(passed=not errors, errors=errors, backend='Software', requiredLoadedNames=sorted(REQUIRED),
                loadedNames=sorted(observed), observedPaths={k: sorted(v) for k, v in observed.items()},
                copiedHashesBefore=before, copiedHashesAfter=after,
                optionalNotLoaded=sorted(set(LIBRARIES) - REQUIRED - set(observed)),
                allEightCopiedLibrariesRequired=True, foreignLoadedPathsRejected=True)
