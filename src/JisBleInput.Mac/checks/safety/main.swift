import Foundation

let defaultsName = "local.jisbleinput.ascii.check." + UUID().uuidString
let defaults = UserDefaults(suiteName: defaultsName)!
defer { defaults.removePersistentDomain(forName: defaultsName) }
for saved in [0, 2, 3, 4, 5, 9] {
    defaults.set(saved, forKey: "sender-input-mode")
    var settings = InputSettings(defaults: defaults)
    precondition(settings.mode == .japanese && !settings.selectSpeed(at: 0))
    do {
        _ = try TextTransfer(text: "ABC", settings: settings)
        preconditionFailure("inaccessible mode produced key reports")
    } catch InputError.unavailableMode {}
    settings.save(to: defaults)
    precondition(defaults.integer(forKey: "sender-input-mode") == saved)
}
defaults.set(1, forKey: "sender-input-mode")
defaults.set(3, forKey: "sender-ascii-speed")
let ascii = InputSettings(defaults: defaults)
precondition(ascii.canSend && ascii.asciiSpeed == .standard)
for text in ["ABC午", "ABC😀", "ABC\t", "ABC\u{007f}"] {
    do {
        _ = try TextTransfer(text: text, settings: ascii)
        preconditionFailure("unsupported text produced key reports")
    } catch InputError.invalidAscii(4) {}
}
let max = String(repeating: "A\r\n", count: 2500)
let fullTransfer = try TextTransfer(text: max, settings: ascii)
precondition(fullTransfer.units.count == 5000 && TextTemplates.canSaveBody(max))
precondition((try? TextTransfer(text: max + "A", settings: ascii)) == nil)
precondition(!TextTemplates.canSaveBody(max + "A"))

var safety = ConnectionSafety()
safety.begin(connected: true, now: 10)
let old = safety.generation
let ticket = safety.releaseTicket
safety.receiveLED(0, generation: old)
precondition(!safety.canSend(.ascii))
safety.accepted(generation: old, ticket: ticket, now: 10.1)
precondition(safety.canSend(.ascii) && !safety.canSend(.japanese))
safety.receiveLED(1, generation: old)
precondition(safety.canSend(.ascii))
safety.receiveLED(2, generation: old)
precondition(!safety.canSend(.ascii))
safety.begin(connected: true, now: 12)
safety.receiveLED(0, generation: old)
precondition(safety.capsLock == .unknown)
safety.expire(now: 13.1)
precondition(safety.release == .timedOut && !safety.canSend(.ascii))
precondition(!safety.requestRelease(generation: safety.generation, now: 13.1))
precondition(safety.requestRelease(generation: safety.generation, now: 13.1, manual: true))

let storedText = " ASCII \r\n 日本語 "
var templates = TextTemplates(defaults: defaults)
try templates.add(name: "Keep", body: storedText)
precondition(TextTemplates(defaults: defaults).entries[0].body.utf8.elementsEqual(storedText.utf8))
precondition((try? TextTransfer(text: storedText, settings: ascii)) == nil)
templates.replaceBody(at: 0, with: "ASCII")
precondition(TextTemplates(defaults: defaults).entries[0].body == "ASCII")
templates.remove(at: 0)
precondition(TextTemplates(defaults: defaults).entries.isEmpty)
print("ASCII product safety checks passed")
