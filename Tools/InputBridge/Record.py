"""Read-only correlation of command, SDK callback and observed game input.

Opens only existing maps, closes every observation, creates no game resources.
Values in the legacy command map have no source time; never infer a lease from
the time at which this observer read them.
"""
import argparse
import ctypes as c
import datetime as dt
import json
import math
import pathlib
import struct
import time

from ReadStatus import read_existing


def decode_telemetry(raw):
    if len(raw) != 2300 or struct.unpack_from("<I", raw, 40)[0] != 12:
        raise ValueError("Unsupported telemetry layout")
    if struct.unpack_from("<I", raw, 52)[0] not in (1, 2):
        raise ValueError("Unsupported game identity")
    result = {"active": bool(raw[0]), "paused": bool(raw[4]),
              "timestamp": struct.unpack_from("<Q", raw, 8)[0],
              "simulatedTimestamp": struct.unpack_from("<Q", raw, 16)[0],
              "renderTimestamp": struct.unpack_from("<Q", raw, 24)[0],
              "parkingBrake": bool(raw[1566])}
    for name, offset in (("speedMps", 948), ("engineRpm", 952),
                         ("logicalThrottle", 960), ("logicalBrake", 964),
                         ("effectiveThrottle", 976), ("effectiveBrake", 980)):
        value = struct.unpack_from("<f", raw, offset)[0]
        result[name] = value if math.isfinite(value) else None
    return result


def decode_commands(raw):
    if len(raw) != 342:
        raise ValueError("Unsupported legacy command layout")
    values = {}
    for name, offset in (("steering", 118), ("throttle", 122), ("brake", 126)):
        value = struct.unpack_from("<f", raw, offset)[0]
        values[name] = value if math.isfinite(value) else None
    return {"values": values, "originalTimestampKnown": False}


def read_map(name, size):
    kernel = c.WinDLL("kernel32", use_last_error=True)
    kernel.OpenFileMappingW.argtypes = [c.c_uint32, c.c_int, c.c_wchar_p]
    kernel.OpenFileMappingW.restype = c.c_void_p
    kernel.MapViewOfFile.argtypes = [c.c_void_p, c.c_uint32, c.c_uint32, c.c_uint32, c.c_size_t]
    kernel.MapViewOfFile.restype = c.c_void_p
    kernel.UnmapViewOfFile.argtypes = [c.c_void_p]
    kernel.CloseHandle.argtypes = [c.c_void_p]
    handle = kernel.OpenFileMappingW(4, False, name)
    if not handle:
        return None
    address = None
    try:
        address = kernel.MapViewOfFile(handle, 4, 0, 0, size)
        if not address:
            return None
        return c.string_at(address, size)
    finally:
        if address:
            kernel.UnmapViewOfFile(address)
        kernel.CloseHandle(handle)


def foreground_pid():
    user = c.WinDLL("user32", use_last_error=True)
    user.GetForegroundWindow.restype = c.c_void_p
    user.GetWindowThreadProcessId.argtypes = [c.c_void_p, c.POINTER(c.c_uint32)]
    window = user.GetForegroundWindow()
    pid = c.c_uint32()
    if not window or not user.GetWindowThreadProcessId(window, c.byref(pid)):
        return None
    return pid.value


def observe():
    row = {"utc": dt.datetime.now(dt.timezone.utc).isoformat(), "monotonic": time.perf_counter(),
           "vehicleOutputWritten": False, "issues": []}
    try:
        bridge = read_existing()
    except (OSError, ValueError, struct.error) as error:
        bridge = {"available": False, "observationError": str(error)}
        row["issues"].append("BridgeUnavailableOrInvalid")
    row["bridge"] = bridge
    foreground = foreground_pid()
    row["foregroundPid"] = foreground
    game_pid = bridge.get("processId") if bridge.get("initialized") else None
    row["foregroundMatchesReportedBridgePid"] = foreground == game_pid if foreground and game_pid else None
    row["bridgeProcessIdentityVerified"] = False
    for name, size, decoder, key in (("Local\\SCSTelemetry", 2300, decode_telemetry, "game"),
                                     ("Local\\SCSControls", 342, decode_commands, "legacyCommand")):
        raw = read_map(name, size)
        if raw is None:
            row[key] = None
            row["issues"].append(key + "Unavailable")
            continue
        try:
            row[key] = decoder(raw)
        except ValueError as error:
            row[key] = None
            row["issues"].append(str(error))
    # Maps are independent observations, not one atomic control frame.
    row["atomicAcrossMaps"] = False
    row["legacyAndTelemetryCopyAtomic"] = False
    return row


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--seconds", type=int, default=60)
    parser.add_argument("--interval", type=float, default=.1)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 600 or not .05 <= args.interval <= 1:
        parser.error("Duration must be 1–600 seconds; interval .05–1 second")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    started = time.perf_counter()
    samples = 0
    with args.output.open("x", encoding="utf-8") as stream:
        while time.perf_counter()-started < args.seconds:
            stream.write(json.dumps(observe(), allow_nan=False, separators=(",", ":"))+"\n")
            stream.flush()
            samples += 1
            time.sleep(args.interval)
    print(json.dumps({"samples": samples, "output": str(args.output), "vehicleOutputWritten": False}))
