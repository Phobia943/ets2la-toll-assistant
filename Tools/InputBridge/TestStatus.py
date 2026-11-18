"""Cross-language contract check against the actual native fake-host snapshot."""
import pathlib
import struct
import sys

from ReadStatus import decode_status

data = pathlib.Path(sys.argv[1]).read_bytes()
now = struct.unpack_from("<q",data,64)[0]
sample = decode_status(data,now)
assert sample["initialized"] and sample["deviceActive"] is True
assert sample["framesStarted"] == 5 and sample["framesCompleted"] == 1
assert sample["returned"]["throttle"]["frame"] == 5
assert abs(sample["returned"]["throttle"]["value"]-.3) < 1e-6
assert sample["returned"]["brake"]["frame"] == 3
assert sample["returned"]["activate"]["frame"] == 2
assert sample["originalCommandQpc"] is None and sample["gameSideCommandTtlKnown"] is False
assert sample["lastCallbackAgeSeconds"] == 0
assert decode_status(data,1)["lastCallbackAgeSeconds"] is None
for offset, value in ((0,1),(4,0),(8,2),(12,215),(32,0)):
    invalid = bytearray(data)
    struct.pack_into("<I",invalid,offset,value)
    try:
        decode_status(invalid,now)
    except ValueError:
        continue
    raise AssertionError(f"Invalid diagnostic header accepted at {offset}")
print("PASS native-to-Python diagnostic contract, distinct axis frames, no invented TTL, invalid clock/header rejection")
