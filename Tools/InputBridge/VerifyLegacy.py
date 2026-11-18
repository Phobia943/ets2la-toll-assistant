"""Read-only identification of the installed legacy bridge; never loads the DLL.

The input table is factual ABI data, extracted only from the exact pinned PE.
Use --write-header solely when regenerating the checked-in compatibility table.
"""
import argparse
import hashlib
import json
import pathlib
import struct

import pefile

PIN = "D9FBD5ACE5E76EED70B7E10B78C450B111566AD6AA814DFF65D6244AD23E687E"
parser = argparse.ArgumentParser()
parser.add_argument("dll", type=pathlib.Path)
parser.add_argument("--write-header", type=pathlib.Path)
parser.add_argument("--check-header", type=pathlib.Path)
args = parser.parse_args()
raw = args.dll.read_bytes()
assert hashlib.sha256(raw).hexdigest().upper() == PIN, "Unrecognized DLL; no ABI inference allowed"
pe = pefile.PE(data=raw)
assert pe.FILE_HEADER.Machine == 0x8664
exports = {s.name.decode(): s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols if s.name}
assert exports["scs_input_init"] == 0x1820
assert exports["?input_event_callback@@YAHQEAUscs_input_event_t@@IQEAX@Z"] == 0x16F0
init = pe.get_data(0x1820, 0x190)
# LEA RCX,[RSP+30h]; MOV [RSP+60h],RCX. The registration receives
# device at RSP+40h, whose callback_context field is +20h on x64.
pattern = bytes.fromhex("48 8d 4c 24 30 48 89 4c 24 60")
assert pattern in init
assert bytes.fromhex("48 8d 4c 24 40 ff d0") in init
callback = pe.get_data(0x16F0, 0xC0)
assert bytes.fromhex("41 8b 00") in callback  # MOV EAX,[R8], the supplied context.
rows = []
offset = 0
for index in range(276):
    name, display, kind, padding = struct.unpack("<QQII", pe.get_data(0x3200 + index * 24, 24))
    assert kind in (1, 5) and padding == 0
    name = pe.get_string_at_rva(name - pe.OPTIONAL_HEADER.ImageBase).decode("ascii")
    display = pe.get_string_at_rva(display - pe.OPTIONAL_HEADER.ImageBase).decode("ascii")
    rows.append((index, name, display, kind, offset))
    offset += 4 if kind == 5 else 1
assert offset == 342
assert [(r[0], r[4]) for r in rows if r[1] in ("steering", "aforward", "abackward", "activate")] == [(70,118),(71,122),(72,126),(74,134)]
header = "// Factual ABI table extracted by VerifyLegacy.py from SHA256 " + PIN + ".\n"
header += "#pragma once\n#include \"scssdk_input.h\"\n"
header += "static const scs_input_device_input_t legacy_inputs[] = {\n"
header += "".join("    {" + json.dumps(n) + "," + json.dumps(d) + f",{t},0" + "},\n" for _,n,d,t,_ in rows)
header += "};\nstatic constexpr unsigned legacy_input_count = 276;\nstatic constexpr unsigned legacy_bytes = 342;\n"
header += "static constexpr unsigned legacy_offsets[] = {" + ",".join(str(r[4]) for r in rows) + "};\n"
if args.write_header:
    args.write_header.write_text(header, encoding="utf-8", newline="\n")
if args.check_header:
    assert args.check_header.read_text(encoding="utf-8") == header, "Compatibility table differs from pinned DLL"
print(json.dumps({"sha256": PIN, "machine": "x64", "inputInitRva": "0x1820", "eventCallbackRva": "0x16f0",
    "contextPointerStoreRva": hex(0x1820 + init.index(pattern)), "registrationDeviceStackOffset": "0x40",
    "callbackContextStackOffset": "0x30", "inputTableRva": "0x3200", "inputCount": len(rows), "commandBytes": offset,
    "finding": "Callback context points into scs_input_init stack after it returns; independently verified in pinned binary.",
    "scope": "No proof this lifetime defect caused the observed background failure; no DLL executed."}, indent=2))
