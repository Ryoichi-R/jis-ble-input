import importlib.util
import io
import contextlib
import struct
import unittest
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "firmware_image", ROOT / "scripts/Test-FirmwareImage.py"
)
IMAGE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(IMAGE)


def record(kind, address, payload):
    raw = bytes([len(payload)]) + address.to_bytes(2, "big") + bytes([kind]) + payload
    return ":" + (raw + bytes([(-sum(raw)) & 255])).hex() + "\n"


class ImageSafetyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.output = (
            ROOT / "_internal/build-cache/firmware/image-tests" / uuid.uuid4().hex
        ).resolve()
        assert cls.output.is_relative_to(ROOT.resolve())
        cls.output.mkdir(parents=True)

    def setUp(self):
        self.path = self.output / (self.id().split(".")[-1] + ".hex")
        self.hex = (
            record(4, 0, bytes([0, 2]))
            + record(0, 0x7000, bytes([0, 0, 4, 0x20, 1, 0x71, 2, 0]))
            + record(1, 0, b"")
        )
        self.path.write_text(self.hex, encoding="ascii")

    def test_application_region(self):
        self.assertEqual(len(IMAGE.read_hex(self.path)), 8)

    def test_reject_softdevice(self):
        self.path.write_text(self.hex.replace("7000", "6000", 1), encoding="ascii")
        with self.assertRaises(ValueError):
            IMAGE.read_hex(self.path)

    def test_reject_checksummed_protected_region(self):
        self.path.write_text(
            record(4, 0, b"\0\2") + record(0, 0x6000, b"\0") + record(1, 0, b""), encoding="ascii"
        )
        with self.assertRaisesRegex(ValueError, "protected"):
            IMAGE.read_hex(self.path)

    def test_reject_upper_flash(self):
        self.path.write_text(
            self.hex[:-12] + record(4, 0, b"\0\x0f") + record(0, 0, b"\0") + record(1, 0, b""),
            encoding="ascii",
        )
        with self.assertRaises(ValueError):
            IMAGE.read_hex(self.path)

    def test_reject_overlap(self):
        self.path.write_text(
            self.hex.replace(record(1, 0, b""), record(0, 0x7000, b"\0") + record(1, 0, b"")),
            encoding="ascii",
        )
        with self.assertRaisesRegex(ValueError, "overlap"):
            IMAGE.read_hex(self.path)

    def test_reject_missing_end(self):
        self.path.write_text(self.hex.replace(record(1, 0, b""), ""), encoding="ascii")
        with self.assertRaises(ValueError):
            IMAGE.read_hex(self.path)

    def uf2(self, family=0xADA52840, address=0x27000, corrupt=False):
        payload = bytearray(256)
        for location, value in IMAGE.read_hex(self.path).items():
            payload[location - 0x27000] = value
        if corrupt:
            payload[0] ^= 1
        data = (
            struct.pack("<8I", 0x0A324655, 0x9E5D5157, 0x2000, address, 256, 0, 1, family)
            + payload
            + bytes(220)
            + struct.pack("<I", 0x0AB16F30)
        )
        uf2 = self.path.with_suffix(".uf2")
        uf2.write_bytes(data)
        return uf2

    def test_valid_uf2_matches_hex(self):
        with contextlib.redirect_stdout(io.StringIO()):
            IMAGE.verify(self.path, self.uf2())

    def test_reject_wrong_family(self):
        with self.assertRaisesRegex(ValueError, "family"):
            IMAGE.verify(self.path, self.uf2(family=0xD663823C))

    def test_reject_uf2_protected_region(self):
        with self.assertRaisesRegex(ValueError, "protected"):
            IMAGE.verify(self.path, self.uf2(address=0x26000))

    def test_reject_uf2_byte_change(self):
        with self.assertRaisesRegex(ValueError, "mismatch"):
            IMAGE.verify(self.path, self.uf2(corrupt=True))


if __name__ == "__main__":
    unittest.main()
