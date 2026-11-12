#pragma once
#include "Protocol.h"

#define JBI_FIRMWARE_VERSION "0.1.2"

namespace jbi {
class Backend {
public:
    virtual ~Backend() {}
    virtual void send(const Frame &frame) = 0;
    // 1: enqueued by SoftDevice, 0: resources temporarily full, -1: failed.
    virtual int notify(const uint8_t report[8]) = 0;
    virtual bool openPairing() = 0;
    virtual bool clearBond() = 0;
};

// Only the Arduino loop owns this controller and all BLE notification calls.
// BLE callbacks update an external hardware snapshot, consumed by setState().
class Controller {
    Backend &backend_;
    uint8_t session_[16] = {};
    bool hasSession_ = false;
    uint32_t expected_ = 0;
    uint32_t generation_ = 1;
    uint32_t lastActivity_ = 0;
    uint8_t status_[57] = {};
    bool armed_ = false;
    bool pending_ = false;
    bool releaseNeeded_ = false;
    Frame job_ = {};
    uint8_t report_[8] = {};
    uint32_t jobStarted_ = 0;

    bool ready() const { return (status_[4] & 15) == 7; }
    void statusTo(uint8_t *out) {
        memcpy(out, status_, sizeof(status_));
        if (armed_) out[4] |= 128;
    }
    void reply(const Frame &request, Result result) {
        Frame response = {};
        response.command = request.command | 0x80;
        memcpy(response.session, request.session, 16);
        response.sequence = request.sequence;
        response.generation = generation_;
        response.payload[0] = uint8_t(result);
        response.length = 1;
        if (result == Success) {
            statusTo(response.payload + 1);
            response.length += sizeof(status_);
        }
        backend_.send(response);
    }
    void queue(const Frame &request, const uint8_t *report, uint32_t now) {
        job_ = request;
        memcpy(report_, report, 8);
        pending_ = true;
        jobStarted_ = now;
    }
    void event() {
        if (!hasSession_) return;
        Frame frame = {};
        frame.command = 0xf0;
        memcpy(frame.session, session_, 16);
        frame.generation = generation_;
        frame.length = sizeof(status_);
        statusTo(frame.payload);
        backend_.send(frame);
    }

public:
    explicit Controller(Backend &backend) : backend_(backend) {
        constexpr char version[] = JBI_FIRMWARE_VERSION;
        static_assert(sizeof(version) == 6, "HELLO version must use three single-digit components");
        memcpy(status_, "JBI1", 4);
        status_[5] = 1;
        for (size_t i = 0; i < 3; ++i) status_[54 + i] = uint8_t(version[i * 2] - '0');
    }
    void identity(const uint8_t board[8], const uint8_t descriptor[32]) {
        memcpy(status_ + 6, board, 8);
        memcpy(status_ + 22, descriptor, 32);
    }
    void invalidate() {
        armed_ = false;
        if (pending_) {
            Frame old = job_;
            pending_ = false;
            reply(old, Rejected);
        }
        releaseNeeded_ = true;
    }
    void invalidateSession() {
        invalidate();
        hasSession_ = false;
    }
    void setState(uint8_t flags, uint8_t mode, const uint8_t peer[8]) {
        flags &= 127;
        bool linkChanged = ((status_[4] ^ flags) & 15) || status_[5] != mode || memcmp(status_ + 14, peer, 8);
        bool changed = status_[4] != flags || linkChanged;
        bool capsTurnedOn = !(status_[4] & 32) && (flags & 32);
        status_[4] = flags;
        status_[5] = mode;
        memcpy(status_ + 14, peer, 8);
        if (linkChanged) {
            ++generation_;
            invalidate();
        } else if (capsTurnedOn) invalidate();
        if (changed) event();
    }
    void handle(const Frame &request, uint32_t now) {
        if (request.command == Hello && request.sequence == 0 && request.length == 0) {
            pending_ = false;
            armed_ = false;
            releaseNeeded_ = true;
            memcpy(session_, request.session, 16);
            hasSession_ = true;
            expected_ = 1;
            lastActivity_ = now;
            uint8_t zero[8] = {};
            if (ready()) queue(request, zero, now);
            else {
                releaseNeeded_ = false;
                reply(request, Success);
            }
            return;
        }
        if (!hasSession_ || memcmp(session_, request.session, 16)) {
            invalidateSession();
            reply(request, WrongSession);
            return;
        }
        if (request.sequence != expected_ || expected_ == 0xffffffffu) {
            invalidate();
            hasSession_ = false;
            reply(request, WrongSequence);
            return;
        }
        ++expected_;
        if (request.command < Status || request.command > ClearBond || (request.command == Report ? request.length != 8 : request.length != 0)) {
            invalidate();
            hasSession_ = false;
            reply(request, InvalidFrame);
            return;
        }
        // Read-only STATUS/PING can recover an updated generation without rearming.
        if (request.command != Status && request.command != Ping && request.generation != generation_) {
            invalidateSession();
            reply(request, WrongGeneration);
            return;
        }
        lastActivity_ = now;
        if (request.command == Status || request.command == Ping) {
            reply(request, Success);
            return;
        }
        if (request.command == PairingOpen) {
            reply(request, !pending_ && !armed_ && !(status_[4] & 64) && backend_.openPairing() ? Success : Rejected);
            return;
        }
        if (request.command == Report) {
            if (!ready() || (status_[4] & 32)) {
                invalidate();
                reply(request, NotReady);
                return;
            }
            if (!armed_) {
                reply(request, NotArmed);
                return;
            }
            if (pending_ || releaseNeeded_) {
                invalidate();
                reply(request, Busy);
                return;
            }
            if (request.payload[1] != 0) {
                invalidateSession();
                reply(request, InvalidFrame);
                return;
            }
            for (int i = 2; i < 8; ++i)
                if (request.payload[i] > 0x91) {
                    invalidateSession();
                    reply(request, InvalidFrame);
                    return;
                }
            queue(request, request.payload, now);
            return;
        }
        if (request.command == Arm && (!ready() || (status_[4] & 32) || pending_)) {
            reply(request, NotReady);
            return;
        }
        if (pending_) invalidate();
        if (request.command == Disarm || request.command == ClearBond) armed_ = false;
        uint8_t zero[8] = {};
        if (!ready()) {
            if (request.command == ClearBond) reply(request, backend_.clearBond() ? Success : StorageFailed);
            else reply(request, NotReady);
            return;
        }
        queue(request, zero, now);
    }
    void tick(uint32_t now, bool usbConnected) {
        if (hasSession_ && (!usbConnected || uint32_t(now - lastActivity_) >= 1500)) {
            invalidate();
            hasSession_ = false;
        }
        if (pending_) {
            int result = ready() ? backend_.notify(report_) : -1;
            if (!result && uint32_t(now - jobStarted_) < 500) return;
            Frame completed = job_;
            pending_ = false;
            if (result > 0) {
                if (completed.command == Arm) armed_ = true;
                releaseNeeded_ = false;
                const bool cleared = completed.command != ClearBond || backend_.clearBond();
                reply(completed, cleared ? Success : StorageFailed);
            } else {
                armed_ = false;
                releaseNeeded_ = true;
                reply(completed, NotificationFailed);
            }
        } else if (releaseNeeded_ && ready()) {
            uint8_t zero[8] = {};
            if (backend_.notify(zero) > 0) releaseNeeded_ = false;
        }
    }
    uint32_t generation() const { return generation_; }
    bool armed() const { return armed_; }
};
} // namespace jbi
