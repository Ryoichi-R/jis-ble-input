enum AsciiKeys {
    typealias Stroke = (usage: UInt8, modifier: UInt8)

    // Mirror Windows AsciiKeyEncoder. Punctuation follows the receiver's layout.
    private static let jis = punctuation(
        shiftedDigits: "!\"#$%&'()",
        pairs: [("-", "=", 0x2D), ("^", "~", 0x2E), ("@", "`", 0x2F),
                ("[", "{", 0x30), ("]", "}", 0x32), (";", "+", 0x33),
                (":", "*", 0x34), ("\\", "_", 0x87)],
        shiftedOnly: [("|", 0x89)]
    )
    private static let us = punctuation(
        shiftedDigits: "!@#$%^&*()",
        pairs: [("-", "_", 0x2D), ("=", "+", 0x2E), ("[", "{", 0x2F),
                ("]", "}", 0x30), ("\\", "|", 0x31), (";", ":", 0x33),
                ("'", "\"", 0x34), ("`", "~", 0x35)],
        shiftedOnly: []
    )

    private static func punctuation(shiftedDigits: String, pairs: [(String, String, UInt8)],
                                    shiftedOnly: [(String, UInt8)]) -> [UInt8: Stroke] {
        var table: [UInt8: Stroke] = [:]
        for (index, character) in shiftedDigits.utf8.enumerated() {
            table[character] = (0x1E + UInt8(index), 2)
        }
        for (plain, shifted, usage) in pairs + [(",", "<", 0x36), (".", ">", 0x37), ("/", "?", 0x38)] {
            table[plain.utf8.first!] = (usage, 0)
            table[shifted.utf8.first!] = (usage, 2)
        }
        for (character, usage) in shiftedOnly {
            table[character.utf8.first!] = (usage, 2)
        }
        return table
    }

    static func stroke(for ascii: UInt8, layout: ReceiverLayout) -> Stroke {
        switch ascii {
        case 0x61...0x7A: (0x04 + (ascii - 0x61), 0)
        case 0x41...0x5A: (0x04 + (ascii - 0x41), 2)
        case 0x31...0x39: (0x1E + (ascii - 0x31), 0)
        case 0x30: (0x27, 0)
        case 0x20: (0x2C, 0)
        default: (layout == .jis ? jis : us)[ascii]!
        }
    }

    static func reports(for ascii: UInt8, speed: AsciiSpeed, layout: ReceiverLayout) -> [KeyReport] {
        let stroke = stroke(for: ascii, layout: layout)
        let timing = speed.asciiTiming
        return [KeyReport(stroke.usage, modifier: stroke.modifier, delay: timing.press), KeyReport(delay: timing.release)]
    }
}
