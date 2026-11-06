#pragma once
#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace jbi {
static const size_t kMaximumFrame = 128;
static const size_t kHeader = 28;
enum Command { Hello = 1, Status = 2, Arm = 3, Report = 4, Release = 5, Disarm = 6, Ping = 7, PairingOpen = 8, ClearBond = 9 };
enum Result { Success, InvalidFrame, WrongSession, WrongSequence, WrongGeneration, NotReady, NotArmed, Busy, NotificationFailed, Rejected };
inline uint16_t read16(const uint8_t* p) { return uint16_t(p[0]) | (uint16_t(p[1]) << 8); }
inline uint32_t read32(const uint8_t* p) { return uint32_t(p[0]) | (uint32_t(p[1]) << 8) | (uint32_t(p[2]) << 16) | (uint32_t(p[3]) << 24); }
inline void write16(uint8_t* p, uint16_t v) { p[0] = uint8_t(v); p[1] = uint8_t(v >> 8); }
inline void write32(uint8_t* p, uint32_t v) { for (int i = 0; i < 4; ++i) p[i] = uint8_t(v >> (i * 8)); }
inline uint16_t crc(const uint8_t* p, size_t size) {
    uint16_t value = 0xffff;
    for (size_t i = 0; i < size; ++i) {
        value ^= uint16_t(p[i]) << 8;
        for (int bit = 0; bit < 8; ++bit) value = uint16_t(value & 0x8000 ? (value << 1) ^ 0x1021 : value << 1);
    }
    return value;
}
struct Frame {
    uint8_t command;
    uint8_t session[16];
    uint32_t sequence;
    uint32_t generation;
    uint16_t length;
    uint8_t payload[kMaximumFrame - kHeader - 2];
};
inline bool decode(const uint8_t* encoded, size_t size, Frame& frame) {
    if (!size || size > kMaximumFrame + 1) return false;
    uint8_t raw[kMaximumFrame];
    size_t count = 0;
    for (size_t i = 0; i < size;) {
        uint8_t code = encoded[i++];
        if (!code || i + code - 1 > size) return false;
        for (unsigned n = 1; n < code; ++n) {
            if (count >= sizeof(raw) || !encoded[i]) return false;
            raw[count++] = encoded[i++];
        }
        if (code != 255 && i < size) { if (count >= sizeof(raw)) return false; raw[count++] = 0; }
    }
    if (count < kHeader + 2 || raw[0] != 1 || read16(raw + 2) != count - kHeader - 2 || read16(raw + count - 2) != crc(raw, count - 2)) return false;
    frame.command = raw[1]; memcpy(frame.session, raw + 4, 16);
    frame.sequence = read32(raw + 20); frame.generation = read32(raw + 24);
    frame.length = read16(raw + 2); memcpy(frame.payload, raw + kHeader, frame.length);
    return true;
}
inline size_t encode(const Frame& frame, uint8_t* out) {
    if (frame.length > sizeof(frame.payload)) return 0;
    uint8_t raw[kMaximumFrame] = {};
    raw[0] = 1; raw[1] = frame.command; write16(raw + 2, frame.length);
    memcpy(raw + 4, frame.session, 16); write32(raw + 20, frame.sequence); write32(raw + 24, frame.generation);
    memcpy(raw + kHeader, frame.payload, frame.length);
    const size_t size = kHeader + frame.length + 2;
    write16(raw + size - 2, crc(raw, size - 2));
    size_t index = 1, codeIndex = 0; uint8_t code = 1;
    for (size_t i = 0; i < size; ++i) {
        if (!raw[i]) { out[codeIndex] = code; codeIndex = index++; code = 1; }
        else { out[index++] = raw[i]; ++code; }
    }
    out[codeIndex] = code; out[index++] = 0;
    return index;
}
class FrameReader {
    uint8_t bytes_[kMaximumFrame + 1];
    size_t size_ = 0;
    bool discard_ = false;
public:
    void reset() { size_ = 0; discard_ = false; }
    void expire() { if (size_) { size_ = 0; discard_ = true; } }
    // 1: frame, 0: incomplete, -1: invalid. Oversize discards through the terminator.
    int push(uint8_t value, Frame& frame) {
        if (value) {
            if (!discard_) {
                if (size_ == sizeof(bytes_)) { size_ = 0; discard_ = true; }
                else bytes_[size_++] = value;
            }
            return 0;
        }
        if (!size_ && !discard_) return 0;
        bool valid = !discard_ && decode(bytes_, size_, frame);
        reset(); return valid ? 1 : -1;
    }
};
}
