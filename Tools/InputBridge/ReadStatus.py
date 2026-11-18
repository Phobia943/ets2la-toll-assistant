"""Open an existing diagnostic map read-only; never opens/creates command maps.

This reports SDK callback return values, not logical game inputs or physics.
"""
import argparse
import ctypes as c
import json
import pathlib
import struct

MAP_NAME = "Local\\MautInputBridgeStatus_v1"
SIZE = 216


def decode_status(raw, now_qpc):
    if len(raw) != SIZE:
        raise ValueError("Unexpected status size")
    sequence, magic, version, size, pid, initialized = struct.unpack_from("<6I", raw)
    if sequence & 1 or (magic, version, size) != (0x4D494231, 1, SIZE):
        raise ValueError("Incomplete or incompatible status")
    session, frequency, active_known, active, changes, changed, callback, frame, activated, started, completed, events, index, flags = struct.unpack_from("<qqIIQqqqqQQQII", raw, 24)
    if frequency <= 0:
        raise ValueError("Invalid clock")

    def age(stamp):
        return (now_qpc-stamp)/frequency if 0 < stamp <= now_qpc else None

    result = {"available": True, "initialized": bool(initialized), "processId": pid,
        "sessionQpc": session, "qpcFrequency": frequency,
        "deviceActive": bool(active) if active_known else None, "activityCallbacks": changes,
        "lastActivityChangeQpc": changed, "lastCallbackQpc": callback, "lastCallbackAgeSeconds": age(callback),
        "lastFrameQpc": frame, "lastFrameAgeSeconds": age(frame), "lastAfterActivationQpc": activated,
        "framesStarted": started, "framesCompleted": completed, "eventsReturned": events,
        "lastInputIndex": index, "lastCallbackFlags": flags,
        "acknowledgementScope": "SDK callback return only; logical/effective input unknown",
        "originalCommandQpc": None, "gameSideCommandTtlKnown": False, "returned": {}}
    for offset, name in enumerate(("steering", "throttle", "brake", "activate")):
        number, stamp, value, _ = struct.unpack_from("<QqfI", raw, 120+offset*24)
        result["returned"][name] = {"frame": number, "qpc": stamp,
            "ageSeconds": age(stamp), "value": value if number else None}
    return result


def read_existing():
    kernel = c.WinDLL("kernel32", use_last_error=True)
    kernel.OpenFileMappingW.argtypes = [c.c_uint32, c.c_int, c.c_wchar_p]
    kernel.OpenFileMappingW.restype = c.c_void_p
    kernel.MapViewOfFile.argtypes = [c.c_void_p,c.c_uint32,c.c_uint32,c.c_uint32,c.c_size_t]
    kernel.MapViewOfFile.restype = c.c_void_p
    kernel.UnmapViewOfFile.argtypes = [c.c_void_p]
    kernel.CloseHandle.argtypes = [c.c_void_p]
    handle = kernel.OpenFileMappingW(4, False, MAP_NAME)  # FILE_MAP_READ, no create.
    if not handle:
        return {"available": False, "win32Error": c.get_last_error()}
    address = None
    try:
        address = kernel.MapViewOfFile(handle,4,0,0,SIZE)
        if not address:
            raise OSError(c.get_last_error(), "Cannot read diagnostic mapping")
        for _ in range(20):
            before = c.c_uint32.from_address(address).value
            if before & 1:
                continue
            raw = c.string_at(address,SIZE)
            after = c.c_uint32.from_address(address).value
            if before == after and struct.unpack_from("<I",raw)[0] == before:
                now = c.c_int64()
                kernel.QueryPerformanceCounter(c.byref(now))
                return decode_status(raw,now.value)
        return {"available": True, "coherent": False}
    finally:
        if address:
            kernel.UnmapViewOfFile(address)
        kernel.CloseHandle(handle)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture",type=pathlib.Path)
    args = parser.parse_args()
    if args.fixture:
        data = args.fixture.read_bytes()
        # Offline fixture has no current-process or live-clock interpretation.
        result = decode_status(data,struct.unpack_from("<q",data,64)[0])
        result["observationKind"] = "offline fixture"
    else:
        result = read_existing()
    print(json.dumps(result,indent=2,allow_nan=False))
