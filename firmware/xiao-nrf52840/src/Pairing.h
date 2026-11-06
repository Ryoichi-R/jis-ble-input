#pragma once
#include <stdint.h>
#include <string.h>

namespace jbi {
static const uint32_t kPairingWindowMs = 60000;

// Pure registration policy; the sketch supplies SDK state, host tests supply fakes.
inline bool pairingWindowOpen(bool allowed, bool registered, uint32_t started, uint32_t now) {
    return allowed && !registered && uint32_t(now - started) < kPairingWindowMs;
}

inline bool pairingWindowExpired(bool allowed, uint32_t started, uint32_t now) {
    return allowed && uint32_t(now - started) >= kPairingWindowMs;
}

// Bluefruit queues bond persistence to its callback task after AUTH_STATUS, so a
// just-paired bond may not be readable yet. Keep waiting while the window is open.
enum class RegistrationStep : uint8_t { Wait, Save, Abort };
inline RegistrationStep registrationStep(bool windowOpen, bool bondLoaded) {
    return !windowOpen ? RegistrationStep::Abort : bondLoaded ? RegistrationStep::Save : RegistrationStep::Wait;
}

// A reconnect outside the window must present the bond saved for the one registered identity.
inline bool knownPeer(bool registered, bool bondLoaded, const uint8_t address[6], uint8_t type,
                      const uint8_t expected[6], uint8_t expectedType) {
    return registered && bondLoaded && type == expectedType && memcmp(address, expected, 6) == 0;
}
}
