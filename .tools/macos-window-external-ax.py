#!/usr/bin/env python3
"""Bounded, public-API AX inspection and notification tracing for Window Lab.

The Dock command inspects one exact bundle URL. UI actions remain in cua_repl;
this helper only observes public AX APIs and never invokes reopen delegates,
posts input, requests permissions, or changes system settings.
"""

import argparse
import ctypes as C
import datetime
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import time
from urllib.parse import unquote, urlparse


CF = C.CDLL('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')
AX = C.CDLL('/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices')


def bind(lib, name, result, *arguments):
    function = getattr(lib, name)
    function.restype = result
    function.argtypes = list(arguments)
    return function


pointer = C.c_void_p
bind(CF, 'CFRelease', None, pointer)
bind(CF, 'CFRetain', pointer, pointer)
bind(CF, 'CFGetTypeID', C.c_ulong, pointer)
bind(CF, 'CFStringGetTypeID', C.c_ulong)
bind(CF, 'CFArrayGetTypeID', C.c_ulong)
bind(CF, 'CFBooleanGetTypeID', C.c_ulong)
bind(CF, 'CFNumberGetTypeID', C.c_ulong)
bind(CF, 'CFNumberGetValue', C.c_bool, pointer, C.c_int, pointer)
bind(CF, 'CFURLGetTypeID', C.c_ulong)
bind(CF, 'CFStringCreateWithCString', pointer, pointer, C.c_char_p, C.c_uint)
bind(CF, 'CFStringGetCString', C.c_bool, pointer, C.c_char_p, C.c_long, C.c_uint)
bind(CF, 'CFArrayGetCount', C.c_long, pointer)
bind(CF, 'CFArrayGetValueAtIndex', pointer, pointer, C.c_long)
bind(CF, 'CFBooleanGetValue', C.c_bool, pointer)
bind(CF, 'CFURLGetString', pointer, pointer)
bind(CF, 'CFHash', C.c_ulong, pointer)
bind(CF, 'CFRunLoopGetCurrent', pointer)
bind(CF, 'CFRunLoopAddSource', None, pointer, pointer, pointer)
bind(CF, 'CFRunLoopRemoveSource', None, pointer, pointer, pointer)
bind(CF, 'CFRunLoopRunInMode', C.c_int, pointer, C.c_double, C.c_bool)
bind(AX, 'AXIsProcessTrusted', C.c_bool)
bind(AX, 'AXUIElementCreateApplication', pointer, C.c_int)
bind(AX, 'AXUIElementSetMessagingTimeout', C.c_int, pointer, C.c_float)
bind(AX, 'AXUIElementCopyAttributeValue', C.c_int, pointer, pointer, C.POINTER(pointer))
bind(AX, 'AXUIElementGetPid', C.c_int, pointer, C.POINTER(C.c_int))
bind(AX, 'AXValueGetTypeID', C.c_ulong)
bind(AX, 'AXValueGetType', C.c_int, pointer)
bind(AX, 'AXValueGetValue', C.c_bool, pointer, C.c_int, pointer)
bind(AX, 'AXObserverGetRunLoopSource', pointer, pointer)


class Point(C.Structure):
    _fields_ = [('x', C.c_double), ('y', C.c_double)]


class Size(C.Structure):
    _fields_ = [('width', C.c_double), ('height', C.c_double)]


class Range(C.Structure):
    _fields_ = [('location', C.c_long), ('length', C.c_long)]


class Element:
    def __init__(self, value, retained=False):
        self.value = value
        if retained:
            CF.CFRetain(value)

    def __del__(self):
        if self.value:
            CF.CFRelease(self.value)
            self.value = None


names = {}


def string(value):
    buffer = C.create_string_buffer(16384)
    return buffer.value.decode('utf-8', errors='replace') if CF.CFStringGetCString(value, buffer, len(buffer), 0x08000100) else '<string-too-long>'


def name(value):
    if value not in names:
        names[value] = CF.CFStringCreateWithCString(None, value.encode(), 0x08000100)
    return names[value]


def decode(value):
    kind = CF.CFGetTypeID(value)
    if kind == CF.CFStringGetTypeID():
        return string(value)
    if kind == CF.CFBooleanGetTypeID():
        return bool(CF.CFBooleanGetValue(value))
    if kind == CF.CFNumberGetTypeID():
        result = C.c_double()
        if CF.CFNumberGetValue(value, 6, C.byref(result)):
            return int(result.value) if result.value.is_integer() else result.value
    if kind == CF.CFURLGetTypeID():
        return string(CF.CFURLGetString(value))
    if kind == CF.CFArrayGetTypeID():
        return [Element(CF.CFArrayGetValueAtIndex(value, i), retained=True)
                for i in range(min(1024, CF.CFArrayGetCount(value)))]
    if kind == AX.AXValueGetTypeID():
        value_kind = AX.AXValueGetType(value)
        struct = {1: Point, 2: Size, 4: Range}.get(value_kind)
        if struct:
            result = struct()
            if AX.AXValueGetValue(value, value_kind, C.byref(result)):
                return {field: getattr(result, field) for field, _ in result._fields_}
    return Element(value, retained=True)


def attribute(element, attribute_name):
    value = pointer()
    error = AX.AXUIElementCopyAttributeValue(element.value, name(attribute_name), C.byref(value))
    if error:
        return {'AXError': error}
    if not value.value:
        return None
    try:
        return decode(value.value)
    finally:
        CF.CFRelease(value.value)


def describe(element):
    result = {'identity': int(CF.CFHash(element.value))}
    for key in ('AXRole', 'AXSubrole', 'AXTitle', 'AXDescription', 'AXIdentifier',
                'AXEnabled', 'AXFocused', 'AXMain', 'AXModal', 'AXMinimized',
                'AXPosition', 'AXSize', 'AXValue', 'AXSelectedTextRange'):
        value = attribute(element, key)
        if isinstance(value, (str, bool, int, float)) or isinstance(value, dict) and 'AXError' not in value:
            result[key] = value
    return result


def children(element):
    result = attribute(element, 'AXChildren')
    return result if isinstance(result, list) else []


def walk(root, limit=512, depth=12):
    stack = [(root, 0)]
    seen = set()
    while stack and len(seen) < limit:
        element, level = stack.pop()
        identity = int(CF.CFHash(element.value))
        if identity in seen:
            continue
        seen.add(identity)
        yield element
        if level < depth:
            stack.extend((child, level + 1) for child in reversed(children(element)))


def application(pid):
    result = Element(AX.AXUIElementCreateApplication(pid))
    AX.AXUIElementSetMessagingTimeout(result.value, 1.0)
    return result


def snapshot(app):
    result = {}
    for key in ('AXHidden', 'AXFrontmost', 'AXFocusedWindow', 'AXMainWindow', 'AXFocusedUIElement', 'AXWindows'):
        value = attribute(app, key)
        result[key] = ([describe(item) for item in value] if isinstance(value, list)
                       else describe(value) if isinstance(value, Element) else value)
    return result


def emit(value, stream=None):
    value = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), **value}
    target = stream or __import__('sys').stdout
    target.write(json.dumps(value, ensure_ascii=False) + '\n')
    target.flush()


def dock_command(args):
    bundle = args.bundle.resolve(strict=True)
    assert bundle.suffix == '.app', 'Expected an existing application bundle'
    dock_pid = int(subprocess.check_output(['pgrep', '-x', 'Dock'], text=True).splitlines()[0])
    dock = application(dock_pid)
    matches = []
    for element in walk(dock, limit=256, depth=4):
        value = attribute(element, 'AXURL')
        if isinstance(value, str) and urlparse(value).scheme == 'file' and Path(unquote(urlparse(value).path)).resolve() == bundle:
            matches.append(element)
    assert len(matches) == 1, f'Expected exactly one Dock item for {bundle}, found {len(matches)}'
    item = matches[0]
    result = {'kind': 'dock-item', 'dockPID': dock_pid, 'bundle': str(bundle), 'element': describe(item), 'AXURL': attribute(item, 'AXURL')}
    emit(result)


def watch_command(args):
    app = application(args.pid)
    output = args.output.open('x')
    emit({'kind': 'observer-source', 'pid': args.pid, 'observerPID': os.getpid(),
          'sourceSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}, output)
    pending = []
    callback_type = C.CFUNCTYPE(None, pointer, pointer, pointer, pointer)

    @callback_type
    def callback(observer, element, notification, context):
        # Preserve the notification target until after dispatch has returned.
        pending.append((Element(element, retained=True), string(notification)))

    bind(AX, 'AXObserverCreate', C.c_int, C.c_int, callback_type, C.POINTER(pointer))
    bind(AX, 'AXObserverAddNotification', C.c_int, pointer, pointer, pointer, pointer)
    observer = pointer()
    error = AX.AXObserverCreate(args.pid, callback, C.byref(observer))
    assert error == 0, error
    source = AX.AXObserverGetRunLoopSource(observer)
    loop = CF.CFRunLoopGetCurrent()
    mode = name('kCFRunLoopDefaultMode')
    CF.CFRunLoopAddSource(loop, source, mode)
    subscriptions = set()
    known_targets = {}
    notifications = 0
    stopped = False

    def stop(signum, frame):
        nonlocal stopped
        stopped = True

    signal.signal(signal.SIGINT, stop)
    signal.signal(signal.SIGTERM, stop)

    def subscribe(element, notification_names):
        identity = int(CF.CFHash(element.value))
        target = describe(element)
        known_targets[identity] = target
        for notification in notification_names:
            key = (identity, notification)
            if key in subscriptions:
                continue
            subscriptions.add(key)
            error = AX.AXObserverAddNotification(observer, element.value, name(notification), None)
            emit({'kind': 'subscription', 'notification': notification, 'target': target, 'AXError': error}, output)

    subscribe(app, ('AXWindowCreated', 'AXFocusedUIElementChanged', 'AXFocusedWindowChanged',
                    'AXApplicationActivated', 'AXApplicationDeactivated', 'AXApplicationHidden', 'AXApplicationShown'))
    deadline = time.monotonic() + args.seconds
    next_scan = 0.0
    try:
        while not stopped and time.monotonic() < deadline:
            if time.monotonic() >= next_scan:
                windows = attribute(app, 'AXWindows')
                for window in windows if isinstance(windows, list) else []:
                    subscribe(window, ('AXUIElementDestroyed', 'AXWindowMiniaturized', 'AXWindowDeminiaturized', 'AXMoved', 'AXResized', 'AXTitleChanged'))
                    for element in walk(window):
                        role = attribute(element, 'AXRole')
                        subscribe(element, ('AXUIElementDestroyed', 'AXLayoutChanged', 'AXTitleChanged'))
                        if role in ('AXTextField', 'AXTextArea', 'AXComboBox'):
                            subscribe(element, ('AXValueChanged', 'AXSelectedTextChanged'))
                        elif role in ('AXCheckBox', 'AXRadioButton', 'AXSlider'):
                            subscribe(element, ('AXValueChanged',))
                emit({'kind': 'snapshot', 'pid': args.pid, 'state': snapshot(app)}, output)
                next_scan = time.monotonic() + 1.0
                if not args.ready.exists():
                    args.ready.write_text(str(args.pid) + '\n')
                    emit({'kind': 'observer-ready', 'pid': args.pid, 'output': str(args.output)})
            CF.CFRunLoopRunInMode(mode, 0.1, True)
            batch, pending[:] = pending[:], []
            for element, notification in batch:
                notifications += 1
                target = describe(element)
                emit({'kind': 'notification', 'pid': args.pid, 'notification': notification,
                      'target': target, 'lastObservedTarget': known_targets.get(target['identity'])}, output)
        emit({'kind': 'observer-complete', 'pid': args.pid, 'notifications': notifications, 'stopRequested': stopped}, output)
        emit({'kind': 'observer-complete', 'pid': args.pid, 'notifications': notifications, 'output': str(args.output)})
    finally:
        CF.CFRunLoopRemoveSource(loop, source, mode)
        CF.CFRelease(observer)
        output.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    read = commands.add_parser('snapshot')
    read.add_argument('pid', type=int)
    dock = commands.add_parser('dock')
    dock.add_argument('bundle', type=Path)
    watch = commands.add_parser('watch')
    watch.add_argument('pid', type=int)
    watch.add_argument('--output', type=Path, required=True)
    watch.add_argument('--ready', type=Path, required=True)
    watch.add_argument('--seconds', type=float, default=180)
    args = parser.parse_args()
    assert AX.AXIsProcessTrusted(), 'AX permission unavailable; no permission prompt requested'
    if args.command == 'dock':
        dock_command(args)
    elif args.command == 'watch':
        watch_command(args)
    else:
        emit({'kind': 'snapshot', 'pid': args.pid, 'state': snapshot(application(args.pid))})


if __name__ == '__main__':
    main()
