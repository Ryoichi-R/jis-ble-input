#include "../src/Controller.h"
#include "../src/Pairing.h"
#include <stdio.h>
#include <stdlib.h>
#include <vector>
#include <fstream>
#include <string>

using namespace jbi;
static int passed = 0;
static void check(bool ok, const char *message) {
    if (!ok) {
        fprintf(stderr, "FAIL: %s\n", message);
        exit(1);
    }
    ++passed;
}
class FakeBackend : public Backend {
public:
    std::vector<Frame> responses;
    std::vector<std::vector<uint8_t>> reports;
    int result = 1;
    bool pair = false;
    bool cleared = false;
    bool canClear = true;
    void send(const Frame &frame) { responses.push_back(frame); }
    int notify(const uint8_t report[8]) {
        if (result > 0) reports.push_back(std::vector<uint8_t>(report, report + 8));
        return result;
    }
    bool openPairing() {
        pair = true;
        return true;
    }
    bool clearBond() { return cleared = canClear; }
};
static bool zero(const std::vector<uint8_t> &report) {
    for (size_t i = 0; i < report.size(); ++i)
        if (report[i]) return false;
    return true;
}
static Frame request(uint8_t command, uint32_t sequence, uint32_t generation) {
    Frame frame = {};
    frame.command = command;
    frame.session[0] = 0x12;
    frame.sequence = sequence;
    frame.generation = generation;
    return frame;
}
static std::vector<uint8_t> unhex(const std::string &text) {
    std::vector<uint8_t> bytes;
    for (size_t i = 0; i + 1 < text.size(); i += 2) bytes.push_back(uint8_t(strtoul(text.substr(i, 2).c_str(), NULL, 16)));
    return bytes;
}
int main(int argc, char **argv) {
    check(argc == 2, "shared vector path supplied");
    std::ifstream fixtures(argv[1]);
    check(fixtures.good(), "shared vectors readable");
    std::string row;
    int fixtureCount = 0;
    while (std::getline(fixtures, row)) {
        if (row.empty() || row[0] == '#') continue;
        size_t first = row.find('\t'), second = row.find('\t', first + 1);
        check(first != std::string::npos && second != std::string::npos, "fixture schema");
        std::vector<uint8_t> wire = unhex(row.substr(first + 1, second - first - 1));
        Frame frame = {};
        bool valid = decode(wire.data(), wire.size() - 1, frame);
        check(valid == (row[second + 1] != '-'), "shared valid/invalid wire fixture");
        if (valid) {
            uint8_t encoded[kMaximumFrame + 2];
            size_t length = encode(frame, encoded);
            check(length == wire.size() && !memcmp(encoded, wire.data(), length), "shared COBS fixture bytes");
            std::vector<uint8_t> raw = unhex(row.substr(second + 1));
            check(frame.command == raw[1] && !memcmp(frame.session, raw.data() + 4, 16) && frame.sequence == read32(raw.data() + 20) && frame.generation == read32(raw.data() + 24) && frame.length == raw.size() - 30 && !memcmp(frame.payload, raw.data() + 28, frame.length), "shared fixture fields");
        }
        ++fixtureCount;
    }
    check(fixtureCount == 10, "all shared fixtures executed");
    check(crc(reinterpret_cast<const uint8_t *>("123456789"), 9) == 0x29b1, "CRC independent standard check value");
    for (int value = 0; value < 256; ++value) {
        Frame frame = request(Report, 0x12345678, 0x9abcdef0);
        frame.length = 8;
        memset(frame.payload, value, 8);
        uint8_t wire[kMaximumFrame + 2];
        size_t size = encode(frame, wire);
        Frame recovered = {};
        check(size && wire[size - 1] == 0 && decode(wire, size - 1, recovered) && recovered.length == 8 && !memcmp(frame.payload, recovered.payload, 8) && recovered.sequence == frame.sequence && recovered.generation == frame.generation, "COBS all byte values");
    }
    FrameReader reader;
    Frame recovered = {};
    Frame hello = request(Hello, 0, 0);
    uint8_t wire[kMaximumFrame + 2];
    size_t wireSize = encode(hello, wire);
    int parsed = 0;
    for (size_t i = 0; i < wireSize; ++i) parsed = reader.push(wire[i], recovered);
    check(parsed == 1 && recovered.command == Hello, "fragment reassembly");
    for (int i = 0; i < 400; ++i) reader.push(1, recovered);
    check(reader.push(0, recovered) == -1, "bounded oversize frame");
    reader.push(2, recovered);
    reader.expire();
    check(reader.push(0, recovered) == -1, "expired fragment");
    wire[wireSize - 2] ^= 1;
    check(!decode(wire, wireSize - 1, recovered), "CRC damage rejected");
    uint8_t invalid[] = {2};
    check(!decode(invalid, sizeof(invalid), recovered), "invalid COBS rejected");
    check(!decode(invalid, 0, recovered), "empty frame rejected");
    Frame maximum = request(Report, 1, 1);
    maximum.length = sizeof(maximum.payload);
    size_t maximumSize = encode(maximum, wire);
    check(decode(wire, maximumSize - 1, recovered), "maximum frame");
    maximum.length++;
    check(encode(maximum, wire) == 0, "oversize encode refused");

    FakeBackend io;
    Controller device(io);
    uint8_t peer[8] = {1};
    uint8_t identity[8] = {2};
    uint8_t digest[32] = {3};
    device.identity(identity, digest);
    device.setState(7 | 16 | 64, 1, peer);
    device.handle(hello, 0);
    device.tick(0, true);
    check(io.responses.back().command == (Hello | 0x80) && io.responses.back().payload[0] == Success, "HELLO completes after initial zero");
    check(io.responses.back().length == 58 && !memcmp(io.responses.back().payload + 1, "JBI1", 4), "status wire contract");
    uint32_t sequence = 1;
    device.handle(request(Arm, sequence++, device.generation()), 10);
    device.tick(10, true);
    check(device.armed(), "ARM requires initial zero notification");
    Frame key = request(Report, sequence++, device.generation());
    key.length = 8;
    key.payload[0] = 1;
    key.payload[2] = 0x90;
    device.handle(key, 20);
    device.tick(20, true);
    check(io.reports.back()[2] == 0x90, "raw LANG1 and modifiers reach BLE backend");
    device.handle(request(Release, sequence++, device.generation()), 30);
    device.tick(30, true);
    check(device.armed() && zero(io.reports.back()), "ordinary key-up does not disarm the transfer");
    key.sequence = sequence++;
    device.handle(key, 40);
    device.tick(40, true);
    check(io.reports.back()[2] == 0x90, "next character follows an ordinary release");

    io.result = 0;
    key.sequence = sequence++;
    device.handle(key, 50);
    device.tick(50, true);
    size_t beforeStop = io.reports.size();
    device.handle(request(Ping, sequence++, device.generation()), 55);
    check(io.responses.back().command == (Ping | 0x80), "PING responds while notification resources are exhausted");
    device.handle(request(Disarm, sequence++, device.generation()), 60);
    device.tick(60, true);
    check(io.reports.size() == beforeStop && !device.armed(), "stop revokes pending nonzero work");
    io.result = 1;
    device.tick(70, true);
    check(io.responses.back().command == (Disarm | 0x80) && io.responses.back().payload[0] == Success && zero(io.reports.back()), "stop ACK follows zero notification");
    size_t afterStop = io.reports.size();
    device.tick(80, true);
    check(io.reports.size() == afterStop, "no stale report after stop-complete ACK");
    key.sequence = sequence++;
    device.handle(key, 90);
    check(io.responses.back().payload[0] == NotArmed, "post-stop nonzero command rejected");

    device.handle(request(Arm, sequence++, device.generation()), 100);
    device.tick(100, true);
    device.tick(1600, true);
    check(!device.armed() && zero(io.reports.back()), "watchdog releases and disarms");
    device.handle(hello, 1700);
    device.tick(1700, true);
    sequence = 1;
    device.handle(request(Arm, sequence++, device.generation()), 1710);
    device.tick(1710, true);
    device.setState(7 | 16 | 32 | 64, 1, peer);
    key.sequence = sequence++;
    key.generation = device.generation();
    device.handle(key, 1720);
    check(io.responses.back().payload[0] == NotReady && !device.armed(), "CapsLock blocks nonzero input");
    device.handle(request(Release, sequence++, device.generation()), 1730);
    device.tick(1730, true);
    check(io.responses.back().payload[0] == Success && zero(io.reports.back()), "CapsLock does not block release");
    Frame wrong = request(Arm, sequence + 3, device.generation());
    device.handle(wrong, 1800);
    check(io.responses.back().payload[0] == WrongSequence, "sequence gap fails closed");
    wrong = request(Arm, 1, device.generation());
    wrong.session[0] ^= 1;
    device.handle(wrong, 1810);
    check(io.responses.back().payload[0] == WrongSession, "wrong session fails closed");

    device.setState(7 | 16 | 64, 1, peer);
    device.handle(hello, 1900);
    device.tick(1900, true);
    sequence = 1;
    device.handle(request(Arm, sequence++, device.generation() - 1), 1910);
    check(io.responses.back().payload[0] == WrongGeneration, "old generation fails closed");
    device.handle(hello, 1915);
    device.tick(1915, true);
    sequence = 1;
    device.handle(request(Arm, sequence++, device.generation()), 1920);
    device.tick(1920, true);
    key.sequence = sequence++;
    key.generation = device.generation();
    key.payload[1] = 1;
    device.handle(key, 1930);
    check(io.responses.back().payload[0] == InvalidFrame, "reserved report byte validated");
    device.handle(hello, 1935);
    device.tick(1935, true);
    sequence = 1;
    device.handle(request(Arm, sequence++, device.generation()), 1940);
    device.tick(1940, true);
    key.sequence = sequence++;
    key.payload[1] = 0;
    key.payload[2] = 0x92;
    device.handle(key, 1950);
    check(io.responses.back().payload[0] == InvalidFrame, "unsupported usage rejected");

    device.handle(hello, 2000);
    device.tick(2000, true);
    sequence = 1;
    device.handle(request(Arm, sequence++, device.generation()), 2010);
    io.result = 0;
    device.tick(2510, true);
    check(io.responses.back().payload[0] == NotificationFailed && !device.armed(), "notification timeout cannot ACK success");
    io.result = -1;
    device.handle(request(Release, sequence++, device.generation()), 2520);
    device.tick(2520, true);
    check(io.responses.back().payload[0] == NotificationFailed, "backend failure cannot ACK release");
    io.result = 1;
    device.tick(2530, false);
    check(!device.armed(), "USB close revokes session");
    device.setState(0, 1, peer);
    device.handle(hello, 2600);
    sequence = 1;
    check(io.responses.back().payload[0] == Success, "HELLO works before BLE readiness");
    device.handle(request(Arm, sequence++, device.generation()), 2610);
    check(io.responses.back().payload[0] == NotReady, "unsecured/unsubscribed ARM rejected");
    device.handle(request(PairingOpen, sequence++, device.generation()), 2620);
    check(io.pair && io.responses.back().payload[0] == Success, "pairing opens only with no registered host");
    io.canClear = false;
    device.handle(request(ClearBond, sequence++, device.generation()), 2625);
    check(!io.cleared && io.responses.back().payload[0] == StorageFailed, "failed deletion while disconnected reports storage failure");
    io.canClear = true;
    device.handle(request(ClearBond, sequence++, device.generation()), 2630);
    check(io.cleared && io.responses.back().payload[0] == Success, "explicit retry clears the registration");
    Frame bad = request(99, sequence++, device.generation());
    device.handle(bad, 2640);
    check(io.responses.back().payload[0] == InvalidFrame, "unknown commands revoke session");
    device.handle(hello, 2700);
    device.invalidateSession();
    device.handle(request(Arm, 1, device.generation()), 2710);
    check(io.responses.back().payload[0] == WrongSession && !device.armed(), "framing error requires a new HELLO");
    device.setState(7 | 16 | 64, 1, peer);
    device.handle(hello, 2800);
    device.tick(2800, true);
    device.handle(request(Arm, 1, device.generation()), 2810);
    device.tick(2810, true);
    io.result = 0;
    key = request(Report, 2, device.generation());
    key.length = 8;
    key.payload[2] = 4;
    device.handle(key, 2820);
    device.tick(2820, true);
    device.setState(7 | 16 | 32 | 64, 1, peer);
    io.result = 1;
    device.tick(2830, true);
    check(!device.armed() && zero(io.reports.back()), "CapsLock arriving during blocked report discards the nonzero job");

    device.setState(7 | 16 | 64, 1, peer);
    device.handle(hello, 2900);
    device.tick(2900, true);
    device.handle(request(Arm, 1, device.generation()), 2901);
    device.tick(2901, true);
    io.canClear = false;
    device.handle(request(ClearBond, 2, device.generation()), 2902);
    device.tick(2902, true);
    check(!device.armed() && zero(io.reports.back()) && io.responses.back().payload[0] == StorageFailed,
          "connected clear releases keys and reports a storage failure");
    io.canClear = true;

    device.setState(7 | 16 | 64, 1, peer);
    device.handle(hello, 3000);
    device.tick(3000, true);
    device.handle(request(Arm, 1, device.generation()), 3010);
    device.tick(3010, true);
    key = request(Report, 2, device.generation());
    key.length = 8;
    key.payload[2] = 4;
    device.handle(key, 3020);
    device.tick(3020, true);
    size_t beforeDuplicate = io.reports.size();
    device.handle(key, 3030);
    device.tick(3030, true);
    check(io.responses.back().payload[0] == WrongSequence && !device.armed() && io.reports.size() == beforeDuplicate + 1 && zero(io.reports.back()), "duplicate sequence is rejected, not re-sent, and releases");
    device.handle(hello, 3100);
    device.tick(3100, true);
    device.handle(request(Arm, 1, device.generation()), 3110);
    device.tick(3110, true);
    key.sequence = 2;
    key.generation = device.generation();
    device.handle(key, 3120);
    device.tick(3120, true);
    size_t beforeReverse = io.reports.size();
    device.handle(request(Ping, 1, device.generation()), 3130);
    device.tick(3130, true);
    check(io.responses.back().payload[0] == WrongSequence && !device.armed() && io.reports.size() == beforeReverse + 1 && zero(io.reports.back()), "reverse sequence is rejected and releases");

    device.handle(hello, 3200);
    device.tick(3200, true);
    device.handle(request(Arm, 1, device.generation()), 3210);
    device.tick(3210, true);
    uint32_t oldGeneration = device.generation();
    io.result = 0;
    key.sequence = 2;
    key.generation = oldGeneration;
    key.payload[2] = 5;
    device.handle(key, 3220);
    device.tick(3220, true);
    size_t beforeDisconnect = io.reports.size();
    device.setState(64, 1, peer);
    io.result = 1;
    device.tick(3230, true);
    bool rejected = false;
    for (size_t i = 0; i < io.responses.size(); ++i)
        if (io.responses[i].command == (Report | 0x80) && io.responses[i].sequence == 2) rejected = io.responses[i].payload[0] == Rejected;
    check(rejected && !device.armed() && io.reports.size() == beforeDisconnect, "BLE disconnect while waiting for ACK rejects the job without sending");
    device.setState(7 | 16 | 64, 1, peer);
    device.tick(3240, true);
    device.tick(3250, true);
    bool onlyZero = io.reports.size() == beforeDisconnect + 1;
    for (size_t i = beforeDisconnect; i < io.reports.size(); ++i) onlyZero = onlyZero && zero(io.reports[i]);
    check(onlyZero && device.generation() > oldGeneration, "reconnect sends only a release and advances generation");
    key.sequence = 3;
    device.handle(key, 3260);
    check(io.responses.back().payload[0] == WrongGeneration && io.reports.size() == beforeDisconnect + 1, "old-generation report after reconnect is rejected");

    check(pairingWindowOpen(true, false, 1000, 60999) && !pairingWindowOpen(true, false, 1000, 61000), "pairing window closes at 60 seconds");
    check(!pairingWindowOpen(false, false, 0, 1) && !pairingWindowOpen(true, true, 0, 1), "pairing needs an explicit window and no registration");
    check(pairingWindowOpen(true, false, 0xffffff00u, 0x100), "pairing window survives millis wraparound");
    check(pairingWindowExpired(true, 0, 60000) && !pairingWindowExpired(true, 0, 59999) && !pairingWindowExpired(false, 0, 99999), "window expiry closes advertising only once opened");
    uint8_t registeredAddress[6] = {1, 2, 3, 4, 5, 6}, otherAddress[6] = {1, 2, 3, 4, 5, 7};
    check(knownPeer(true, true, registeredAddress, 1, registeredAddress, 1), "registered bonded identity reconnects");
    check(!knownPeer(false, true, registeredAddress, 1, registeredAddress, 1) && !knownPeer(true, false, registeredAddress, 1, registeredAddress, 1) && !knownPeer(true, true, otherAddress, 1, registeredAddress, 1) && !knownPeer(true, true, registeredAddress, 0, registeredAddress, 1),
          "unknown, unbonded, other-address and other-type peers are rejected");
    check(registrationStep(true, false) == RegistrationStep::Wait, "registration waits while the deferred bond save is not readable yet");
    check(registrationStep(true, true) == RegistrationStep::Save, "registration saves once the bond is readable inside the window");
    check(registrationStep(false, false) == RegistrationStep::Abort && registrationStep(false, true) == RegistrationStep::Abort,
          "registration aborts after the window closes even if the bond became readable");
    printf("Passed: %d\n", passed);
    return 0;
}
