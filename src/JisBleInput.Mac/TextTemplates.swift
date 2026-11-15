import Foundation

struct TextTemplate {
    let name: String
    let body: String
}

struct TextTemplates {
    private static let key = "sender-text-templates"
    private let defaults: UserDefaults?
    private(set) var entries: [TextTemplate]

    init(defaults: UserDefaults? = nil) {
        self.defaults = defaults
        let stored = defaults?.array(forKey: Self.key) as? [[String: String]] ?? []
        entries = stored.compactMap {
            guard let name = $0["name"], let body = $0["body"] else { return nil }
            return TextTemplate(name: name, body: body)
        }
    }

    static func canSaveBody(_ body: String) -> Bool {
        (1...KeyboardText.maxUnits).contains(KeyboardText.countInputUnits(body))
    }

    mutating func add(name: String, body: String) throws {
        let title = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { throw TemplateError.emptyName }
        guard Self.canSaveBody(body) else { throw InputError.invalidLength }
        guard !entries.contains(where: { $0.name == title }) else { throw TemplateError.duplicateName }
        entries.append(TextTemplate(name: title, body: body))
        save()
    }

    mutating func remove(at index: Int) {
        guard entries.indices.contains(index) else { return }
        entries.remove(at: index)
        save()
    }

    mutating func replaceBody(at index: Int, with body: String) {
        guard entries.indices.contains(index), Self.canSaveBody(body) else { return }
        entries[index] = TextTemplate(name: entries[index].name, body: body)
        save()
    }

    private func save() {
        defaults?.set(entries.map { ["name": $0.name, "body": $0.body] }, forKey: Self.key)
    }
}

private enum TemplateError: LocalizedError {
    case emptyName, duplicateName

    var errorDescription: String? {
        switch self {
        case .emptyName: "定型文の名前を入力してください。"
        case .duplicateName: "同じ名前の定型文があります。別の名前を入力してください。"
        }
    }
}
