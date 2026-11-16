#!/usr/bin/env python3
"""Windows DirectInput inventory/count verification. No third-party packages required."""
import argparse
import ctypes as c
from ctypes import wintypes as w
from collections import Counter
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


def is_gex_product(vid, pid):
    return vid == 1 and 0x4701 <= pid <= 0x4804


def gex_inventory_failures(observed, expected):
    """Compare API inventories, including active IDs absent from DirectInput."""
    failures = []
    duplicates = [pid for pid, count in Counter(observed).items() if count > 1]
    if duplicates:
        failures.append('Duplicate GEX hardware PID in DirectInput enumeration: '
                        + ', '.join(f'{pid:04X}' for pid in sorted(duplicates)))
    missing = set(expected) - set(observed)
    if missing:
        failures.append('GEX devices present in raw input but missing from DirectInput/joy.cpl: '
                        + ', '.join(f'{pid:04X}' for pid in sorted(missing)))
    if observed != sorted(observed):
        failures.append('Observed GEX DirectInput order differs from numeric joystick/gamepad order: '
                        + ', '.join(f'{pid:04X}' for pid in observed))
    return failures


def raw_input_gex_products():
    """Use Windows raw input, independently of DirectInput's joystick registration.

    Multiple HID collections of one GEX product count once. GEX assigns a
    distinct VID/PID to each managed joystick and gamepad.
    """
    class RawDevice(c.Structure):
        _fields_ = [('handle', w.HANDLE), ('type', w.DWORD)]

    class DeviceInfo(c.Structure):
        # RID_DEVICE_INFO's largest union member is six DWORDs. Its HID
        # member starts with DWORD vendor, product and version identifiers.
        _fields_ = [('size', w.DWORD), ('type', w.DWORD), ('data', w.DWORD * 6)]

    user = c.WinDLL('user32.dll', use_last_error=True)
    device_list = user.GetRawInputDeviceList
    device_list.argtypes = [c.POINTER(RawDevice), c.POINTER(w.UINT), w.UINT]
    device_list.restype = w.UINT
    device_info = user.GetRawInputDeviceInfoW
    device_info.argtypes = [w.HANDLE, w.UINT, c.POINTER(DeviceInfo), c.POINTER(w.UINT)]
    device_info.restype = w.UINT
    failed = 0xFFFFFFFF
    for _ in range(3):
        count = w.UINT()
        if device_list(None, c.byref(count), c.sizeof(RawDevice)) == failed:
            raise c.WinError(c.get_last_error())
        # A non-null buffer is needed even when the first scan is empty.
        devices = (RawDevice * max(1, count.value))()
        capacity = w.UINT(len(devices))
        returned = device_list(devices, c.byref(capacity), c.sizeof(RawDevice))
        if returned != failed:
            break
        if c.get_last_error() != 122:  # ERROR_INSUFFICIENT_BUFFER during hotplug
            raise c.WinError(c.get_last_error())
    else:
        raise RuntimeError('Raw input device list kept changing; retry the verification')
    products = set()
    for row in devices[:returned]:
        if row.type != 2:  # RIM_TYPEHID
            continue
        info = DeviceInfo()
        info.size = c.sizeof(info)
        size = w.UINT(c.sizeof(info))
        if device_info(row.handle, 0x2000000B, c.byref(info), c.byref(size)) == failed:  # RIDI_DEVICEINFO
            raise c.WinError(c.get_last_error())
        vid, pid = info.data[:2]
        if is_gex_product(vid, pid):
            products.add(pid)
    return products


def main():
    if sys.platform != 'win32':
        raise SystemExit('This test requires Windows and the running real service.')
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--name', help='Match instance or product name (case insensitive substring)')
    p.add_argument('--gex-order', action='store_true',
                   help='Check GEX completeness against raw input and observed PID order; do not sort enumeration')
    p.add_argument('--count', type=int, help='Expected number of matching attached DirectInput devices')
    p.add_argument('--buttons', type=int)
    p.add_argument('--axes', type=int)
    p.add_argument('--hats', type=int)
    a = p.parse_args()
    if a.count is not None and a.count < 0:
        p.error('--count must be nonnegative')
    expected_gex = raw_input_gex_products() if a.gex_order else set()
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
    failures, matches, gex_order = [], [], []
    CALLBACK = STDCALL(w.BOOL, c.POINTER(Instance), c.c_void_p)

    @CALLBACK
    def visit(pointer, _):
        row = pointer.contents
        name = f'{row.instanceName} / {row.productName}'
        vid_pid = int.from_bytes(bytes(row.product.data)[:4], 'little')
        vid, pid = vid_pid & 0xFFFF, vid_pid >> 16
        is_gex = is_gex_product(vid, pid)
        if a.gex_order:
            if not is_gex:
                return 1
            gex_order.append(pid)
        if a.name and a.name.casefold() not in name.casefold():
            return 1
        device = c.c_void_p()
        try:
            check(method(root, 3, c.POINTER(GUID), c.POINTER(c.c_void_p), c.c_void_p)(
                root, c.byref(row.instance), c.byref(device), None), 'CreateDevice')
            caps = Caps()
            caps.size = c.sizeof(Caps)
            check(method(device, 3, c.POINTER(Caps))(device, c.byref(caps)), 'GetCapabilities')
            print(f'{len(matches) + 1}: {vid:04X}:{pid:04X} {name}: '
                  f'buttons={caps.buttons}, axes={caps.axes}, hats={caps.hats}')
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
    if not matches and a.count != 0:
        failures.append('No matching attached DirectInput controller found')
    if a.count is not None and len(matches) != a.count:
        failures.append(f'Expected {a.count} matching attached DirectInput devices, found {len(matches)}')
    if a.gex_order:
        # Scan again so arrivals/removals during enumeration cannot produce
        # a false success against an earlier, smaller inventory.
        current_gex = raw_input_gex_products()
        if current_gex != expected_gex:
            failures.append('GEX raw input inventory changed during verification; retry')
        print(f'GEX inventory: raw input={len(current_gex)}, DirectInput={len(gex_order)}')
        failures.extend(gex_inventory_failures(gex_order, current_gex))
    if failures:
        raise SystemExit('\n'.join(failures))
    print(f'PASS: {len(matches)} attached DirectInput device(s) inspected')


if __name__ == '__main__':
    main()
