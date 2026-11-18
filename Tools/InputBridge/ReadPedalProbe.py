"""Read the optional SDK write observer. This never opens a control mapping."""
import ctypes as c
import math
import struct

MAP_NAME = "Local\\MautPedalProbe_v1"
SIZE = 128


def decode(raw, now_qpc):
    if len(raw) != SIZE:
        raise ValueError("Unknown pedal observer size")
    seq, magic, version, size, pid, initialized = struct.unpack_from("<6I", raw)
    if seq & 1 or not seq or (magic, version, size) != (0x4D505031, 1, SIZE) or initialized not in (0, 1):
        raise ValueError("Incomplete or incompatible pedal observer")
    session, frequency, samples, utc, begin, end, origin, requested, bt, bb, at, ab, flags = struct.unpack_from("<qqQqqqd5fI", raw, 24)
    if frequency <= 0 or session <= 0 or flags & ~1 or not pid:
        raise ValueError("Invalid observer identity")
    values = (origin, requested, bt, bb, at, ab)
    if flags & 1 and not all(math.isfinite(v) for v in values):
        raise ValueError("Inconsistent finite-values flag")
    timing = session <= begin <= end <= now_qpc
    return {"available": True, "initialized": bool(initialized), "processId": pid,
            "sessionQpc": session, "qpcFrequency": frequency, "samples": samples,
            "capturedUtcMilliseconds": utc, "beginQpc": begin, "endQpc": end,
            "ageSeconds": (now_qpc-begin)/frequency if timing and samples else None,
            "writeSpanSeconds": (end-begin)/frequency if timing and samples else None,
            "originalAccelerationTimestamp": origin if math.isfinite(origin) else None,
            "requested": requested if math.isfinite(requested) else None,
            "before": {"throttle": bt if math.isfinite(bt) else None, "brake": bb if math.isfinite(bb) else None},
            "after": {"throttle": at if math.isfinite(at) else None, "brake": ab if math.isfinite(ab) else None},
            "observationCoherent": bool(initialized and samples and flags & 1 and timing),
            "scope": "Existing input_mix write only; game input and physical effect unverified"}


def read_existing():
    kernel = c.WinDLL("kernel32", use_last_error=True)
    kernel.OpenFileMappingW.argtypes = [c.c_uint32, c.c_int, c.c_wchar_p]
    kernel.OpenFileMappingW.restype = c.c_void_p
    kernel.MapViewOfFile.argtypes = [c.c_void_p, c.c_uint32, c.c_uint32, c.c_uint32, c.c_size_t]
    kernel.MapViewOfFile.restype = c.c_void_p
    kernel.UnmapViewOfFile.argtypes = [c.c_void_p]
    kernel.CloseHandle.argtypes = [c.c_void_p]
    handle = kernel.OpenFileMappingW(4, False, MAP_NAME)
    if not handle:
        return {"available": False}
    address = None
    try:
        address = kernel.MapViewOfFile(handle, 4, 0, 0, SIZE)
        if not address:
            raise OSError(c.get_last_error(), "Cannot read optional pedal observer")
        for _ in range(20):
            before = c.c_uint32.from_address(address).value
            if before & 1:
                continue
            raw = c.string_at(address, SIZE)
            after = c.c_uint32.from_address(address).value
            if before == after == struct.unpack_from("<I", raw)[0] and not after & 1:
                now = c.c_int64()
                if not kernel.QueryPerformanceCounter(c.byref(now)):
                    raise OSError("Cannot read observer clock")
                return decode(raw, now.value)
        raise ValueError("Pedal observer changing during read")
    finally:
        if address:
            kernel.UnmapViewOfFile(address)
        kernel.CloseHandle(handle)
