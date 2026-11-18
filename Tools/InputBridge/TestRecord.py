"""Offline layout checks; imports never open the live maps."""
import struct
from unittest.mock import patch
import Record
from Record import decode_commands, decode_telemetry

t = bytearray(2300)
struct.pack_into("<I", t, 40, 12)
struct.pack_into("<I", t, 52, 1)
struct.pack_into("<4f", t, 960, .8, .3, .0, .0)
struct.pack_into("<2f", t, 976, .2, .1)
decoded = decode_telemetry(t)
assert abs(decoded["logicalThrottle"]-.8) < 1e-6
assert abs(decoded["effectiveThrottle"]-.2) < 1e-6
assert abs(decoded["logicalBrake"]-.3) < 1e-6
assert abs(decoded["effectiveBrake"]-.1) < 1e-6
cmd = bytearray(342)
struct.pack_into("<3f", cmd, 118, -.4, .6, .7)
decoded = decode_commands(cmd)
assert abs(decoded["values"]["steering"]+.4) < 1e-6
assert abs(decoded["values"]["throttle"]-.6) < 1e-6
assert abs(decoded["values"]["brake"]-.7) < 1e-6
assert decoded["originalTimestampKnown"] is False
struct.pack_into("<f", t, 976, float("nan"))
assert decode_telemetry(t)["effectiveThrottle"] is None
t[40] = 11
try:
    decode_telemetry(t)
    raise AssertionError("Unknown telemetry revision accepted")
except ValueError:
    pass
with patch.object(Record, "read_existing", side_effect=[ValueError("incomplete"), {"available": True, "initialized": True, "processId": 123}]), \
     patch.object(Record, "read_map", return_value=None), patch.object(Record, "foreground_pid", return_value=123):
    first = Record.observe()
    assert first["bridge"]["available"] is False and "BridgeUnavailableOrInvalid" in first["issues"]
    second = Record.observe()
    assert second["bridge"]["available"] and second["foregroundMatchesReportedBridgePid"]
    assert second["bridgeProcessIdentityVerified"] is False and second["legacyAndTelemetryCopyAtomic"] is False
print("13 read-only recording checks passed, including recovery after incomplete status; no live maps opened.")
