import Foundation

// Pure state machine: callbacks and delayed work must carry the connection generation.
struct ConnectionSafety {
    enum LockState: String {
        case unknown = "不明", on = "オン", off = "オフ"
    }
    enum ReleaseState { case disconnected, pending, accepted, timedOut }

    private(set) var generation: UInt64 = 0
    private(set) var releaseTicket: UInt64 = 0
    private(set) var connected = false
    private(set) var capsLock = LockState.unknown
    private(set) var release = ReleaseState.disconnected
    private(set) var deadline: TimeInterval = 0

    mutating func begin(connected: Bool, now: TimeInterval) {
        generation &+= 1
        releaseTicket &+= 1
        self.connected = connected
        capsLock = .unknown
        release = connected ? .pending : .disconnected
        deadline = now + 1
    }

    mutating func receiveLED(_ value: UInt8, generation: UInt64) {
        guard connected, generation == self.generation, value & 0xe0 == 0 else { return }
        capsLock = value & 2 == 0 ? .off : .on
    }

    func canSend(_ mode: InputMode?) -> Bool {
        connected && release == .accepted && capsLock == .off
            && mode == .ascii
    }

    @discardableResult
    mutating func requestRelease(generation: UInt64, now: TimeInterval, manual: Bool = false) -> Bool {
        guard connected, generation == self.generation else { return false }
        expire(now: now)
        if release == .timedOut && !manual { return false }
        if release != .pending {
            releaseTicket &+= 1
            release = .pending
            deadline = now + 1
        }
        return true
    }

    mutating func expire(now: TimeInterval) {
        if release == .pending && now >= deadline { release = .timedOut }
    }

    func mayRelease(generation: UInt64, ticket: UInt64, now: TimeInterval) -> Bool {
        connected && generation == self.generation && ticket == releaseTicket
            && release == .pending && now < deadline
    }

    mutating func accepted(generation: UInt64, ticket: UInt64, now: TimeInterval) {
        guard mayRelease(generation: generation, ticket: ticket, now: now) else { return }
        release = .accepted
    }
}
