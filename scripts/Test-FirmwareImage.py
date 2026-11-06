"""Verify application-only Intel HEX / UF2, without opening any device."""

import struct
import sys
import json
import zipfile
from pathlib import Path


def read_hex(path):
    memory = {}
    base = 0
    ended = False
    for line in path.read_text(encoding="ascii").splitlines():
        if ended or not line.startswith(":"):
            raise ValueError("invalid HEX record order")
        record = bytes.fromhex(line[1:])
        if len(record) != record[0] + 5 or sum(record) & 255:
            raise ValueError("invalid HEX checksum/length")
        count, address, kind = record[0], int.from_bytes(record[1:3], "big"), record[3]
        payload = record[4:-1]
        if kind == 0:
            for offset, value in enumerate(payload):
                location = base + address + offset
                if not 0x27000 <= location < 0xED000 or location in memory:
                    raise ValueError("image touches protected flash or overlaps")
                memory[location] = value
        elif kind == 1:
            ended = True
        elif kind == 4 and count == 2:
            base = int.from_bytes(payload, "big") << 16
        elif kind == 2 and count == 2:
            base = int.from_bytes(payload, "big") << 4
        elif kind in (3, 5) and count == 4:
            entry = (
                (int.from_bytes(payload[:2], "big") << 4) + int.from_bytes(payload[2:], "big")
                if kind == 3
                else int.from_bytes(payload, "big")
            )
            if not 0x27000 <= entry < 0xED000:
                raise ValueError("entry point outside application")
        else:
            raise ValueError("unsupported HEX record")
    if not ended or min(memory, default=0) != 0x27000:
        raise ValueError("missing application vector table/end record")
    return memory


def verify(hex_path, uf2_path=None, zip_path=None):
    memory = read_hex(hex_path)
    if uf2_path:
        data = uf2_path.read_bytes()
        if not data or len(data) % 512:
            raise ValueError("invalid UF2 size")
        recovered = {}
        for index in range(len(data) // 512):
            block = data[index * 512 : (index + 1) * 512]
            magic0, magic1, flags, address, size, number, total, family = struct.unpack_from(
                "<8I", block
            )
            if (magic0, magic1, flags, size, number, total, family) != (
                0x0A324655,
                0x9E5D5157,
                0x2000,
                256,
                index,
                len(data) // 512,
                0xADA52840,
            ) or struct.unpack_from("<I", block, 508)[0] != 0x0AB16F30:
                raise ValueError("invalid UF2 header/family/order")
            if not 0x27000 <= address or address + size > 0xED000:
                raise ValueError("UF2 touches protected flash")
            for offset, value in enumerate(block[32 : 32 + size]):
                location = address + offset
                if location in recovered:
                    raise ValueError("overlapping UF2 blocks")
                recovered[location] = value
        if any(recovered.get(address) != value for address, value in memory.items()):
            raise ValueError("HEX/UF2 mismatch")
        if any(value != 0 and address not in memory for address, value in recovered.items()):
            raise ValueError("unexpected UF2 payload")
    if zip_path:
        with zipfile.ZipFile(zip_path) as archive:
            manifest = json.loads(archive.read("manifest.json"))["manifest"]
            if set(manifest) != {"application", "dfu_version"}:
                raise ValueError("DFU contains non-application firmware")
            application = manifest["application"]
            metadata = application["init_packet_data"]
            if metadata["device_type"] != 0x52 or metadata["softdevice_req"] != [0x123]:
                raise ValueError("DFU targets a different SoftDevice/device")
            binary = archive.read(application["bin_file"])
            if len(binary) != max(memory) - 0x27000 + 1 or any(
                binary[address - 0x27000] != value for address, value in memory.items()
            ):
                raise ValueError("DFU/HEX mismatch")
            packet = archive.read(application["dat_file"])
            if len(packet) != 14 or struct.unpack("<HHIHHH", packet) != (
                0x52,
                0xFFFF,
                0xFFFFFFFF,
                1,
                0x123,
                metadata["firmware_crc16"],
            ):
                raise ValueError("DFU init packet mismatch")
    print(f"Application image: PASS ({len(memory)} bytes; protected regions excluded)")


if __name__ == "__main__":
    verify(
        Path(sys.argv[1]),
        Path(sys.argv[2]) if len(sys.argv) >= 3 else None,
        Path(sys.argv[3]) if len(sys.argv) >= 4 else None,
    )
