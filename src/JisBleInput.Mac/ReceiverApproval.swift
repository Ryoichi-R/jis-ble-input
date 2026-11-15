import CryptoKit
import Foundation

struct ReceiverApproval {
    let defaults: UserDefaults
    private let key = "approved-receiver-sha256"

    func matches(_ identifier: UUID) -> Bool {
        defaults.data(forKey: key) == fingerprint(identifier)
    }

    func remember(_ identifier: UUID) -> Bool {
        defaults.set(fingerprint(identifier), forKey: key)
        return matches(identifier)
    }

    private func fingerprint(_ identifier: UUID) -> Data {
        Data(SHA256.hash(data: Data(identifier.uuidString.utf8)))
    }
}
