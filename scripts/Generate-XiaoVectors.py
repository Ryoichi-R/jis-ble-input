"""Independent protocol fixtures: Python standard-library CRC and byte framing.

Run only when deliberately changing protocol v1 fixtures; normal tests read TSV.
"""

import binascii
import struct
from pathlib import Path


def cobs(raw):
    result = bytearray()
    for block in raw.split(b"\0"):
        result.append(len(block) + 1)
        result.extend(block)
    return result + b"\0"


root = Path(__file__).resolve().parents[1]
target = root / "contracts/xiao-protocol-v1/vectors.tsv"
assert target.resolve().is_relative_to(root.resolve())
rows = ["# name\twire including terminator\traw including CRC, or - for invalid"]
for name, command, sequence, generation, payload in [
    ("hello", 1, 0, 0, b""),
    ("lang1", 4, 0x12345678, 0x9ABCDEF0, bytes([1, 0, 0x90, 0, 0, 0, 0, 0])),
    ("release", 5, 2, 3, b""),
    ("maximum", 4, 0xFFFFFFFE, 0xFFFFFFFF, bytes(range(98))),
]:
    raw = (
        struct.pack("<BBH16sII", 1, command, len(payload), bytes(range(16)), sequence, generation)
        + payload
    )
    raw += struct.pack("<H", binascii.crc_hqx(raw, 0xFFFF))
    rows.append(f"{name}\t{cobs(raw).hex()}\t{raw.hex()}")
    bad = raw[:-1] + bytes([raw[-1] ^ 1])
    rows.append(f"{name}-crc\t{cobs(bad).hex()}\t-")
rows.extend(["invalid-cobs\t0200\t-", "empty\t00\t-"])
target.write_text("\n".join(rows) + "\n", encoding="utf-8")
