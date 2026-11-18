"""Pure decoder checks, optionally against the native isolated fixture."""
import pathlib
import struct
import sys
from ReadPedalProbe import decode

raw = bytearray(128)
struct.pack_into("<6IqqQqqqd5fI", raw, 0, 2, 0x4D505031, 1, 128, 77, 1,
                 100, 1000, 1, 100000, 200, 210, 123.25, .7, .1, .2, .7, 0, 1)
d = decode(raw, 300)
assert d["observationCoherent"] and d["ageSeconds"] == .1 and d["writeSpanSeconds"] == .01
assert d["originalAccelerationTimestamp"] == 123.25 and abs(d["after"]["throttle"]-.7) < 1e-6
assert not decode(raw, 205)["observationCoherent"]  # End stamp is in the future.
for offset, fmt, value in ((0, "I", 3), (4, "I", 0), (12, "I", 256), (16, "I", 0),
                           (20, "I", 2), (32, "q", 0), (100, "I", 2), (80, "f", float("nan"))):
    broken = bytearray(raw)
    struct.pack_into("<"+fmt, broken, offset, value)
    try:
        decode(broken, 300)
        raise AssertionError("Malformed diagnostic accepted")
    except ValueError:
        pass
struct.pack_into("<I", raw, 20, 0)
assert not decode(raw, 300)["observationCoherent"]
if len(sys.argv) == 2:
    fixture = pathlib.Path(sys.argv[1]).read_bytes()
    end = struct.unpack_from("<q", fixture, 64)[0]
    d = decode(fixture, end+1)
    assert d["observationCoherent"] and d["samples"] == 10002
    assert d["after"]["throttle"] == d["requested"]
    assert d["originalAccelerationTimestamp"] == 123+d["requested"]
print("12 pure pedal observer contract cases passed; no game mapping opened.")
