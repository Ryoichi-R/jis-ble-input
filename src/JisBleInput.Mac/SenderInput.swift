import Foundation

enum InputMode: Int, CaseIterable {
    case ascii = 1, japanese = 0

    var label: String {
        switch self {
        case .ascii: "英数字・URL"
        case .japanese: "日本語（利用不可）"
        }
    }
}

enum AsciiSpeed: Int, CaseIterable {
    case slow, standard, fast

    var label: String {
        switch self {
        case .slow: "ゆっくり（0.12秒）"
        case .standard: "高速（0.08秒）"
        case .fast: "最速（0.05秒）"
        }
    }

    var asciiTiming: (press: Int, release: Int) {
        switch self {
        case .slow: (60, 60)
        case .standard: (40, 40)
        case .fast: (25, 25)
        }
    }
}

enum ReceiverLayout: Int, CaseIterable {
    case jis, us

    var label: String {
        self == .jis ? "日本語 JIS（106/109）" : "英語 US（101/104）"
    }
}

struct InputSettings {
    var mode = InputMode.ascii
    private(set) var asciiSpeed = AsciiSpeed.standard
    var layout = ReceiverLayout.jis

    var canSend: Bool { mode == .ascii }

    @discardableResult
    mutating func selectSpeed(at index: Int) -> Bool {
        guard canSend, let speed = AsciiSpeed(rawValue: index) else { return false }
        asciiSpeed = speed
        return true
    }

    init(defaults: UserDefaults? = nil) {
        guard let defaults else { return }
        if let saved = defaults.object(forKey: "sender-input-mode"), (saved as? Int) != InputMode.ascii.rawValue {
            mode = .japanese
        }
        layout = ReceiverLayout(rawValue: defaults.integer(forKey: "sender-receiver-layout")) ?? .jis
        if let value = defaults.object(forKey: "sender-ascii-speed") as? Int,
           let speed = AsciiSpeed(rawValue: value) {
            asciiSpeed = speed
        }
    }

    func save(to defaults: UserDefaults) {
        if canSend { defaults.set(mode.rawValue, forKey: "sender-input-mode") }
        defaults.set(layout.rawValue, forKey: "sender-receiver-layout")
        defaults.set(asciiSpeed.rawValue, forKey: "sender-ascii-speed")
    }
}

struct KeyReport {
    let bytes: Data
    let delay: Int

    init(_ usage: UInt8 = 0, modifier: UInt8 = 0, delay: Int) {
        bytes = Data([modifier, 0, usage, 0, 0, 0, 0, 0])
        self.delay = delay
    }
}

enum KeyboardText {
    static let maxUnits = 5000
    static let lineBreak = [KeyReport(0x28, delay: 50), KeyReport(delay: 300)]

    static func normalized(_ text: String) -> String {
        text.replacingOccurrences(of: "\r\n", with: "\n", options: .literal)
            .replacingOccurrences(of: "\r", with: "\n", options: .literal)
    }

    static func countInputUnits(_ text: String) -> Int {
        normalized(text).utf16.count
    }
}

struct TextTransfer {
    let units: [[KeyReport]]
    let preparation = [KeyReport(0x91, delay: 50), KeyReport(delay: 250)]

    @discardableResult
    static func validate(text: String, settings: InputSettings) throws -> String.UnicodeScalarView {
        guard settings.canSend else { throw InputError.unavailableMode }
        let normalized = KeyboardText.normalized(text)
        guard (1...KeyboardText.maxUnits).contains(normalized.utf16.count) else {
            throw InputError.invalidLength
        }
        for (offset, scalar) in normalized.unicodeScalars.enumerated()
            where scalar.value != 10 && !(0x20...0x7e).contains(scalar.value) {
            throw InputError.invalidAscii(offset + 1)
        }
        return normalized.unicodeScalars
    }

    init(text: String, settings: InputSettings) throws {
        units = try Self.validate(text: text, settings: settings).map { scalar in
            if scalar.value == 10 { return KeyboardText.lineBreak }
            return AsciiKeys.reports(for: UInt8(scalar.value), speed: settings.asciiSpeed, layout: settings.layout)
        }
    }
}

enum InputError: LocalizedError {
    case unavailableMode, invalidLength, invalidAscii(Int)

    var errorDescription: String? {
        switch self {
        case .unavailableMode: "入力方法で「英数字・URL」を選択してください。"
        case .invalidLength: "送信する本文を1〜\(KeyboardText.maxUnits.formatted())文字で入力してください。改行は1文字です。"
        case .invalidAscii(let index):
            "英数字・URLでは半角ASCIIと改行が使えます。\(index)文字目を確認してください。"
        }
    }
}
