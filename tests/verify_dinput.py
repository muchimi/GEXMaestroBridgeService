#!/usr/bin/env python3
"""Windows DirectInput inventory/count verification. No third-party packages required."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import sys
import uuid


class GUID(c.Structure):
    _fields_ = [('data', c.c_ubyte * 16)]

    @classmethod
    def parse(cls, value):
        return cls((c.c_ubyte * 16).from_buffer_copy(uuid.UUID(value).bytes_le))


class Instance(c.Structure):
    _fields_ = [('size', w.DWORD), ('instance', GUID), ('product', GUID), ('type', w.DWORD),
                ('instanceName', w.WCHAR * 260), ('productName', w.WCHAR * 260),
                ('ffDriver', GUID), ('usagePage', w.WORD), ('usage', w.WORD)]


class Caps(c.Structure):
    _fields_ = [(name, w.DWORD) for name in ('size', 'flags', 'type', 'axes', 'buttons', 'hats',
               'ffSamplePeriod', 'ffMinTimeResolution', 'firmwareRevision', 'hardwareRevision', 'ffDriverVersion')]


def main():
    if sys.platform != 'win32':
        raise SystemExit('This test requires Windows and the running real service.')
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--name', help='Match instance or product name (case insensitive substring)')
    p.add_argument('--buttons', type=int)
    p.add_argument('--axes', type=int)
    p.add_argument('--hats', type=int)
    a = p.parse_args()
    HRESULT = c.c_long
    STDCALL = c.WINFUNCTYPE

    def method(pointer, index, *types):
        table = c.cast(pointer, c.POINTER(c.POINTER(c.c_void_p))).contents
        return STDCALL(HRESULT, c.c_void_p, *types)(table[index])

    def check(result, operation):
        if result < 0:
            raise RuntimeError(f'{operation} failed: HRESULT 0x{result & 0xFFFFFFFF:08X}')

    library = c.WinDLL('dinput8.dll')
    create = library.DirectInput8Create
    create.argtypes = [w.HINSTANCE, w.DWORD, c.POINTER(GUID), c.POINTER(c.c_void_p), c.c_void_p]
    create.restype = HRESULT
    kernel = c.WinDLL('kernel32.dll')
    kernel.GetModuleHandleW.argtypes = [w.LPCWSTR]
    kernel.GetModuleHandleW.restype = w.HMODULE
    root = c.c_void_p()
    iid = GUID.parse('BF798031-483A-4DA2-AA99-5D64ED369700')
    check(create(kernel.GetModuleHandleW(None), 0x0800, c.byref(iid), c.byref(root), None), 'DirectInput8Create')
    failures, matches = [], []
    CALLBACK = STDCALL(w.BOOL, c.POINTER(Instance), c.c_void_p)

    @CALLBACK
    def visit(pointer, _):
        row = pointer.contents
        name = f'{row.instanceName} / {row.productName}'
        if a.name and a.name.casefold() not in name.casefold():
            return 1
        device = c.c_void_p()
        try:
            check(method(root, 3, c.POINTER(GUID), c.POINTER(c.c_void_p), c.c_void_p)(
                root, c.byref(row.instance), c.byref(device), None), 'CreateDevice')
            caps = Caps()
            caps.size = c.sizeof(Caps)
            check(method(device, 3, c.POINTER(Caps))(device, c.byref(caps)), 'GetCapabilities')
            print(f'{name}: buttons={caps.buttons}, axes={caps.axes}, hats={caps.hats}')
            matches.append(name)
            for field in ('buttons', 'axes', 'hats'):
                expected = getattr(a, field)
                if expected is not None and getattr(caps, field) != expected:
                    failures.append(f'{name}: expected {expected} {field}, got {getattr(caps, field)}')
        except Exception as exc:
            failures.append(str(exc))
        finally:
            if device:
                method(device, 2)(device)
        return 1

    try:
        check(method(root, 4, w.DWORD, CALLBACK, c.c_void_p, w.DWORD)(root, 4, visit, None, 1), 'EnumDevices')
    finally:
        method(root, 2)(root)
    if not matches:
        failures.append('No matching attached DirectInput controller found')
    if failures:
        raise SystemExit('\n'.join(failures))
    print(f'PASS: {len(matches)} attached DirectInput device(s) inspected')


if __name__ == '__main__':
    main()
