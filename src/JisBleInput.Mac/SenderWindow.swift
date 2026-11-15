import AppKit

final class SenderWindow {
    let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 440, height: 480),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
    let editor: NSTextView
    let recipient = NSTextField(labelWithString: "送信先PC  ·  確認中")
    let connection = NSTextField(wrappingLabelWithString: "Bluetoothを確認中…")
    let message = NSTextField(wrappingLabelWithString: "送信先の接続を待っています。")
    let count = NSTextField(labelWithString: "0 / \(KeyboardText.maxUnits.formatted())文字")
    let mode = NSPopUpButton()
    let speed = NSPopUpButton()
    let progress = NSProgressIndicator()
    let send = NSButton(title: "送信", target: nil, action: nil)
    let pause = NSButton(title: "一時停止", target: nil, action: nil)
    let stop = NSButton(title: "停止", target: nil, action: nil)
    let clear = NSButton(title: "クリア", target: nil, action: nil)
    let paste = NSButton(title: "貼り付け", target: nil, action: nil)
    let templates = NSButton(title: "定型文…", target: nil, action: nil)
    let settings = NSButton(title: "設定…", target: nil, action: nil)

    init() {
        let scroll = NSTextView.scrollableTextView()
        editor = scroll.documentView as! NSTextView
        editor.isRichText = false
        editor.allowsUndo = true
        editor.isAutomaticQuoteSubstitutionEnabled = false
        editor.isAutomaticDashSubstitutionEnabled = false
        editor.isAutomaticTextReplacementEnabled = false
        editor.isAutomaticSpellingCorrectionEnabled = false
        editor.font = .systemFont(ofSize: 15)
        editor.textContainerInset = NSSize(width: 8, height: 8)
        editor.setAccessibilityLabel("送信する文章")
        scroll.borderType = .bezelBorder

        recipient.font = .systemFont(ofSize: 14, weight: .semibold)
        let destination = NSStackView(views: [recipient, settings])
        destination.distribution = .equalSpacing
        connection.textColor = .secondaryLabelColor
        connection.maximumNumberOfLines = 2
        mode.menu?.autoenablesItems = false
        mode.setAccessibilityLabel("入力方法")
        speed.setAccessibilityLabel("入力速度")
        speed.toolTip = "1文字あたりの待ち時間の設定値。実際の時間にはBluetooth通信などが加わります。"
        let inputOptions = NSStackView(views: [mode, speed])
        inputOptions.distribution = .fillEqually
        inputOptions.spacing = 8

        let editorLabel = NSTextField(labelWithString: "送信する文章")
        count.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
        count.textColor = .secondaryLabelColor
        let editorHeading = NSStackView(views: [editorLabel, NSStackView(views: [count, templates])])
        editorHeading.distribution = .equalSpacing
        send.bezelColor = NSColor(srgbRed: 0.06, green: 0.46, blue: 0.43, alpha: 1)
        send.keyEquivalent = "\r"
        send.keyEquivalentModifierMask = .command
        send.toolTip = "⌘Returnで送信・続行"
        let buttons = NSStackView(views: [send, pause, stop, clear, paste])
        buttons.distribution = .equalSpacing
        progress.isIndeterminate = false
        progress.minValue = 0
        progress.maxValue = 1
        progress.setAccessibilityLabel("送信進捗")
        message.font = .systemFont(ofSize: 12)
        message.maximumNumberOfLines = 3

        let rows = [destination, connection, inputOptions, editorHeading, scroll, buttons, progress, message]
        let stack = NSStackView(views: rows)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 9
        stack.setCustomSpacing(15, after: inputOptions)
        stack.translatesAutoresizingMaskIntoConstraints = false
        let content = window.contentView!
        content.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 18),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -18),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 17),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -17),
            scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 110),
            connection.heightAnchor.constraint(greaterThanOrEqualToConstant: 34),
            message.heightAnchor.constraint(greaterThanOrEqualToConstant: 36)
        ] + rows.map { $0.widthAnchor.constraint(equalTo: stack.widthAnchor) })
        scroll.setContentHuggingPriority(.defaultLow, for: .vertical)
        for row in rows where row !== scroll {
            row.setContentHuggingPriority(.defaultHigh, for: .vertical)
        }

        window.title = "JIS BLE Input"
        window.contentMinSize = NSSize(width: 420, height: 395)
        window.isReleasedWhenClosed = false
        window.initialFirstResponder = editor
        window.center()
    }
}

final class TemplateWindow {
    let window = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 410, height: 226),
                         styleMask: .titled, backing: .buffered, defer: false)
    let list = NSPopUpButton()
    let insert = NSButton(title: "本文に挿入", target: nil, action: nil)
    let replace = NSButton(title: "本文で上書き…", target: nil, action: nil)
    let remove = NSButton(title: "削除…", target: nil, action: nil)
    let name = NSTextField()
    let save = NSButton(title: "現在の本文を保存", target: nil, action: nil)
    let close = NSButton(title: "閉じる", target: nil, action: nil)
    let status = NSTextField(wrappingLabelWithString: "")

    init() {
        window.title = "定型文"
        window.isReleasedWhenClosed = false
        list.setAccessibilityLabel("保存済みの定型文")
        name.setAccessibilityLabel("新しい定型文の名前")
        name.placeholderString = "新しい定型文の名前"
        status.maximumNumberOfLines = 2
        let saved = NSStackView(views: [list, insert, replace, remove])
        let new = NSStackView(views: [name, save])
        let footer = NSStackView(views: [NSView(), close])
        let rows = [NSTextField(labelWithString: "保存済みの定型文"), saved,
                    NSTextField(labelWithString: "現在の本文を定型文として保存"), new, status, footer]
        let stack = NSStackView(views: rows)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.translatesAutoresizingMaskIntoConstraints = false
        let content = window.contentView!
        content.addSubview(stack)
        list.setContentHuggingPriority(.defaultLow, for: .horizontal)
        name.setContentHuggingPriority(.defaultLow, for: .horizontal)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 16),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -16),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 16),
            status.heightAnchor.constraint(greaterThanOrEqualToConstant: 34)
        ] + [saved, new, status, footer].map { $0.widthAnchor.constraint(equalTo: stack.widthAnchor) })
        window.initialFirstResponder = name
    }
}
