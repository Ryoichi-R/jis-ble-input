import AppKit
import CoreBluetooth

func uuid(_ short: String) -> CBUUID {
    CBUUID(string: "0000\(short)-0000-1000-8000-00805F9B34FB")
}

final class KeyboardSender: NSObject, NSApplicationDelegate, NSTextViewDelegate, CBPeripheralManagerDelegate {
    private enum TransferControl { case running, pausing, paused, stopping }
    private var screen: SenderWindow!
    private lazy var templateWindow = TemplateWindow()
    private var templates = TextTemplates()
    private var input = InputSettings()
    private var transferControl = TransferControl.running
    private var safety = ConnectionSafety()
    private var releaseTimeoutTask: Task<Void, Never>?
    private var sleeping = false
    private var simulatedRelease: (() -> Bool)?
    private var now: TimeInterval { ProcessInfo.processInfo.systemUptime }
    private var connectionSummary = "Bluetoothを確認中…"
    #if UI_TEST
    private let checkingUI = true
    #else
    private let checkingUI = false
    #endif
    private var simulatedWrite: ((KeyReport) async -> Bool)?
    private var manager: CBPeripheralManager?
    private var statusItem: NSStatusItem?
    private var services: [CBMutableService] = []
    private var registered = Set<CBUUID>()
    private var inputCharacteristics: [CBMutableCharacteristic] = []
    private var subscriber: CBCentral?
    private let receiverApproval = ReceiverApproval(defaults: .standard)
    private var confirmingSubscriber = false
    private var subscriptions = Set<CBUUID>()
    private var protocolMode: UInt8 = 1
    private var sendTask: Task<Void, Never>?
    private var transferActivity: NSObjectProtocol?
    private var transferAbortedForLED = false
    private var outputCharacteristics: [CBMutableCharacteristic] = []
    private var initialized = false
    private let controlUUID = CBUUID(string: "D87CABD2-6D7B-4839-B333-F0847F7F104B")

    func applicationDidFinishLaunching(_ notification: Notification) {
        screen = SenderWindow()
        input = InputSettings(defaults: checkingUI ? nil : .standard)
        templates = TextTemplates(defaults: checkingUI ? nil : .standard)
        screen.editor.delegate = self
        screen.mode.target = self
        screen.mode.action = #selector(changeMode)
        screen.speed.target = self
        screen.speed.action = #selector(changeSpeed)
        updateInputOptions()
        let actions: [(NSButton, Selector)] = [
            (screen.send, #selector(sendText)), (screen.pause, #selector(pauseSending)),
            (screen.stop, #selector(stopSending)), (screen.clear, #selector(clearText)),
            (screen.paste, #selector(pasteText)), (screen.settings, #selector(showSettings)),
            (screen.templates, #selector(showTemplates))
        ]
        for (button, action) in actions {
            button.target = self
            button.action = action
        }

        if input.canSend {
            updateEditorState()
        } else {
            show(InputError.unavailableMode.localizedDescription)
        }
        showWindow()
        #if UI_TEST
        Task { @MainActor in
            await checkUI()
            NSApp.terminate(nil)
        }
        #else
        installStatusItem()
        installAppMenu()
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(didWake), name: NSWorkspace.didWakeNotification, object: nil)
        manager = CBPeripheralManager(delegate: self, queue: nil)
        #endif
    }

    private func installAppMenu() {
        let menu = NSMenu()
        let application = NSMenuItem()
        let commands = NSMenu()
        commands.addItem(withTitle: "設定…", action: #selector(showSettings), keyEquivalent: ",").target = self
        commands.addItem(.separator())
        commands.addItem(withTitle: "JIS BLE Inputを終了", action: #selector(NSApplication.terminate(_:)),
                         keyEquivalent: "q")
        application.submenu = commands
        menu.addItem(application)
        let edit = NSMenuItem()
        let editCommands = NSMenu(title: "編集")
        editCommands.addItem(withTitle: "取り消す", action: Selector(("undo:")), keyEquivalent: "z")
        editCommands.addItem(withTitle: "切り取り", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editCommands.addItem(withTitle: "コピー", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editCommands.addItem(withTitle: "貼り付け", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editCommands.addItem(withTitle: "すべてを選択", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        edit.submenu = editCommands
        menu.addItem(edit)
        NSApp.mainMenu = menu
    }

    @objc private func willSleep(_ notification: Notification) {
        bestEffortReleaseBeforeBoundary()
        sleeping = true
        changeConnection(connected: false)
        showConnection("スリープで中断しました。復帰後にLED通知とキー解放を確認します。")
    }

    @objc private func didWake(_ notification: Notification) {
        sleeping = false
        changeConnection(connected: activeInput != nil)
        showConnection("復帰しました。LED通知・キー解放を確認後、新しい手動送信が必要です。")
    }

    private func updateInputOptions() {
        screen.mode.removeAllItems()
        for mode in InputMode.allCases {
            screen.mode.addItem(withTitle: mode.label)
            screen.mode.lastItem?.tag = mode.rawValue
            screen.mode.lastItem?.isEnabled = mode == .ascii
        }
        screen.mode.selectItem(withTag: input.mode.rawValue)
        screen.speed.removeAllItems()
        screen.speed.addItems(withTitles: AsciiSpeed.allCases.map(\.label))
        screen.speed.selectItem(at: input.asciiSpeed.rawValue)
    }

    @objc private func changeMode() {
        guard sendTask == nil, screen.mode.selectedItem?.tag == InputMode.ascii.rawValue else {
            screen.mode.selectItem(withTag: input.mode.rawValue)
            return
        }
        input.mode = .ascii
        if !checkingUI { input.save(to: .standard) }
        show("半角英数字・記号と改行を送信できます。")
        updateEditorState()
    }

    @objc private func changeSpeed() {
        guard sendTask == nil, input.selectSpeed(at: screen.speed.indexOfSelectedItem) else { return }
        if !checkingUI { input.save(to: .standard) }
    }

    @objc private func showSettings() {
        guard sendTask == nil, screen.window.attachedSheet == nil else { return }
        showWindow()
        let alert = NSAlert()
        alert.messageText = "接続と入力の設定"
        alert.informativeText = connectionSummary
            + "\n\nWindowsの入力欄を選び、IMEを「A」にしてCapsLockを解除してください。"
            + "改行を含む本文にはメモ帳などの複数行欄を使ってください。"
        let layout = NSPopUpButton()
        layout.addItems(withTitles: ReceiverLayout.allCases.map(\.label))
        layout.selectItem(at: input.layout.rawValue)
        layout.setAccessibilityLabel("受信PCの配列")
        let layoutRow = NSStackView(views: [NSTextField(labelWithString: "受信PCの配列"), layout])
        layoutRow.spacing = 10
        layoutRow.frame = NSRect(x: 0, y: 0, width: 315, height: 27)
        alert.accessoryView = layoutRow
        alert.addButton(withTitle: "閉じる")
        let offerReleaseRetry = safety.connected && safety.release == .timedOut
        if offerReleaseRetry {
            alert.addButton(withTitle: "キー解放を再試行")
            alert.buttons[1].keyEquivalent = ""
        } else if canRetryAdvertising {
            alert.addButton(withTitle: "広告を再試行")
            alert.buttons[1].keyEquivalent = ""
        }
        alert.beginSheetModal(for: screen.window) { [weak self] response in
            guard let self else { return }
            if let selectedLayout = ReceiverLayout(rawValue: layout.indexOfSelectedItem),
               selectedLayout != self.input.layout {
                self.input.layout = selectedLayout
                if !self.checkingUI { self.input.save(to: .standard) }
                if self.input.canSend { self.show("受信配列：\(selectedLayout.label)") }
            }
            guard response == .alertSecondButtonReturn else { return }
            if offerReleaseRetry {
                self.releaseKey(manual: true)
            } else if self.canRetryAdvertising {
                self.startAdvertising()
            }
        }
    }

    @objc private func clearText() {
        guard sendTask == nil, !screen.editor.hasMarkedText() else { return }
        screen.editor.insertText("", replacementRange: NSRange(location: 0, length: screen.editor.string.utf16.count))
    }

    @objc private func pasteText() {
        guard sendTask == nil else { return }
        screen.window.makeFirstResponder(screen.editor)
        screen.editor.pasteAsPlainText(nil)
    }

    func textDidChange(_ notification: Notification) {
        updateEditorState()
    }

    private func updateEditorState() {
        if input.canSend && !screen.editor.string.isEmpty && !screen.editor.hasMarkedText() {
            do { try TextTransfer.validate(text: screen.editor.string, settings: input) }
            catch {
                lastValidationError = error.localizedDescription
                show(error.localizedDescription)
                return
            }
        }
        if screen.message.stringValue == lastValidationError {
            screen.message.stringValue = ""
        }
        lastValidationError = nil
        updateButtons()
    }

    private var lastValidationError: String?

    // 終了するとHIDサービスが消え、接続中のWindowsが登録を無効と判断する。
    // そのためウィンドウを閉じても常駐し、サービスと広告を維持する。
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWindow()
        return true
    }

    private func installStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.button?.title = "JIS"
        item.button?.setAccessibilityLabel("JIS BLE Input")
        let menu = NSMenu()
        menu.addItem(withTitle: "ウィンドウを表示", action: #selector(showWindow), keyEquivalent: "").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        item.menu = menu
        statusItem = item
    }

    @objc private func showWindow() {
        screen.window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private var receiverIsConnected: Bool {
        subscriber != nil || inputCharacteristics.contains { $0.subscribedCentrals?.isEmpty == false }
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard receiverIsConnected else { return .terminateNow }
        showWindow()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Windowsと接続中です。終了しますか？"
        alert.informativeText = "接続中に終了すると、WindowsがこのMacをキーボードとして認識しなくなり、"
            + "再ペアリングが必要になることがあります。先にWindowsのBluetoothをオフにしてから終了してください。"
        alert.addButton(withTitle: "キャンセル")
        alert.addButton(withTitle: "このまま終了")
        return alert.runModal() == .alertSecondButtonReturn ? .terminateNow : .terminateCancel
    }

    func applicationWillTerminate(_ notification: Notification) {
        bestEffortReleaseBeforeBoundary()
        changeConnection(connected: false)
        manager?.stopAdvertising()
        manager?.removeAllServices()
    }

    private var readyToSend: Bool {
        transportReady && safety.canSend(input.mode)
    }

    private var transportReady: Bool {
        !sleeping && safety.connected && safety.release == .accepted &&
            (simulatedWrite != nil || (manager?.state == .poweredOn && activeInput != nil))
    }

    private var canRetryAdvertising: Bool {
        manager?.state == .poweredOn && !services.isEmpty && registered.count == services.count
            && !receiverIsConnected && sendTask == nil
    }

    private func log(_ message: String) {
        print("\(ISO8601DateFormatter().string(from: Date())) \(message)")
        fflush(stdout)
    }

    private func showConnection(_ message: String) {
        connectionSummary = message
        screen.connection.stringValue = message
        log(message)
        updateButtons()
    }

    private func show(_ message: String) {
        screen.message.stringValue = message
        log(message)
        updateButtons()
    }

    private func updateButtons() {
        let idle = sendTask == nil
        let units = KeyboardText.countInputUnits(screen.editor.string)
        screen.count.stringValue = "\(units.formatted()) / \(KeyboardText.maxUnits.formatted())文字"
        screen.count.textColor = units > KeyboardText.maxUnits ? .systemRed : .secondaryLabelColor
        screen.recipient.stringValue = safety.connected
            ? "CapsLock：\(safety.capsLock.rawValue)"
            : "送信先PC  ·  未接続"
        switch safety.release {
        case .pending: screen.connection.stringValue = "全キー解放のキュー受理待ち（最大1秒）。送信できません。"
        case .timedOut: screen.connection.stringValue = "キー解放が未受理のため中断。設定から手動で再試行してください。"
        case .accepted:
            switch safety.capsLock {
            case .unknown: screen.connection.stringValue = "CapsLockが不明です。Windowsで一度オンにしてからオフに戻してください。"
            case .on: screen.connection.stringValue = "WindowsのCapsLockをオフにしてください。"
            case .off: screen.connection.stringValue = connectionSummary
            }
        case .disconnected: screen.connection.stringValue = connectionSummary
        }
        screen.send.title = !idle && transferControl == .paused ? "続行" : "送信"
        screen.send.isEnabled = idle
            ? readyToSend && !screen.editor.hasMarkedText()
                && (try? TextTransfer.validate(text: screen.editor.string, settings: input)) != nil
            : sendTask != nil && readyToSend && transferControl == .paused
        screen.pause.isEnabled = !idle && transferControl == .running
        screen.stop.isEnabled = !idle && transferControl != .stopping
        screen.editor.isEditable = idle
        screen.clear.isEnabled = idle && units > 0
        screen.paste.isEnabled = idle
        screen.settings.isEnabled = idle
        screen.templates.isEnabled = idle
        screen.mode.isEnabled = idle
        screen.speed.isEnabled = idle && input.canSend
    }

    private func readOnly(_ id: String, _ value: Data) -> CBMutableCharacteristic {
        CBMutableCharacteristic(type: uuid(id), properties: [.read], value: value, permissions: [.readable])
    }

    private func input(_ id: String, reportDescriptor: Bool) -> CBMutableCharacteristic {
        let input = CBMutableCharacteristic(type: uuid(id),
            properties: [.read, .notify, .notifyEncryptionRequired],
            value: nil, permissions: [.readEncryptionRequired])
        if reportDescriptor {
            input.descriptors = [CBMutableDescriptor(type: uuid("2908"), value: Data([1, 1]))]
        }
        return input
    }

    private func output(_ id: String, reportDescriptor: Bool) -> CBMutableCharacteristic {
        let output = CBMutableCharacteristic(type: uuid(id),
            properties: [.read, .write, .writeWithoutResponse], value: nil,
            permissions: [.readEncryptionRequired, .writeEncryptionRequired])
        if reportDescriptor {
            output.descriptors = [CBMutableDescriptor(type: uuid("2908"), value: Data([1, 2]))]
        }
        return output
    }

    func peripheralManagerDidUpdateState(_ peripheral: CBPeripheralManager) {
        print("STATE \(peripheral.state.rawValue) AUTHORIZATION \(CBManager.authorization.rawValue)")
        guard peripheral.state == .poweredOn else {
            changeConnection(connected: false)
            subscriber = nil
            subscriptions.removeAll()
            protocolMode = 1
            initialized = false
            registered.removeAll()
            let message: String
            switch peripheral.state {
            case .poweredOff: message = "MacのBluetoothをオンにしてください。"
            case .unauthorized: message = "システム設定でJIS BLE InputにBluetoothの使用を許可してください。"
            case .unsupported: message = "このMacではBluetoothの送信機能を利用できません。"
            default: message = "Bluetoothを準備中です。"
            }
            showConnection(message)
            return
        }
        guard !initialized else { return }
        initialized = true
        inputCharacteristics = [input("2A4D", reportDescriptor: true), input("2A22", reportDescriptor: false)]
        outputCharacteristics = [output("2A4D", reportDescriptor: true), output("2A32", reportDescriptor: false)]
        let hid = CBMutableService(type: uuid("1812"), primary: true)
        hid.characteristics = [
            readOnly("2A4A", Data([0x11, 0x01, 0, 2])),
            readOnly("2A4B", keyboardReportMap),
            CBMutableCharacteristic(type: uuid("2A4C"), properties: [.writeWithoutResponse],
                                    value: nil, permissions: [.writeEncryptionRequired]),
            CBMutableCharacteristic(type: uuid("2A4E"), properties: [.read, .writeWithoutResponse],
                                    value: nil, permissions: [.readEncryptionRequired, .writeEncryptionRequired]),
            inputCharacteristics[0], outputCharacteristics[0],
            inputCharacteristics[1], outputCharacteristics[1]
        ]
        let battery = CBMutableService(type: uuid("180F"), primary: true)
        battery.characteristics = [readOnly("2A19", Data([100]))]
        let control = CBMutableService(type: controlUUID, primary: true)
        control.characteristics = [
            CBMutableCharacteristic(type: CBUUID(string: "3E283350-FA15-4565-87A0-0BFC6A50963A"),
                                    properties: [.read], value: Data([1]), permissions: [.readable])
        ]
        services = [hid, battery, control]
        for service in services { peripheral.add(service) }
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didAdd service: CBService, error: Error?) {
        if let error = error as NSError? {
            showConnection("Bluetoothサービスの登録に失敗しました（\(error.code)）。")
            return
        }
        print("ADD OK \(service.uuid.uuidString)")
        registered.insert(service.uuid)
        if registered.count == services.count { startAdvertising() }
    }

    private func startAdvertising() {
        guard let manager, manager.state == .poweredOn else { return }
        manager.stopAdvertising()
        manager.startAdvertising([
            CBAdvertisementDataLocalNameKey: "JIS-HID",
            CBAdvertisementDataServiceUUIDsKey: [uuid("1812")]
        ])
    }

    func peripheralManagerDidStartAdvertising(_ peripheral: CBPeripheralManager, error: Error?) {
        if let error = error as NSError? {
            showConnection("Bluetooth広告を開始できませんでした（\(error.code)）。設定から再試行できます。")
        } else if subscriber == nil {
            showConnection("Bluetooth広告を開始しました。Windowsの接続を待っています。")
        }
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral,
                           didSubscribeTo characteristic: CBCharacteristic) {
        print("SUBSCRIBE \(characteristic.uuid.uuidString)")
        guard let input = inputCharacteristics.first(where: { $0 === characteristic }) else { return }
        if let subscriber, subscriber.identifier != central.identifier { return }
        guard !confirmingSubscriber else { return }
        let remembered = receiverApproval.matches(central.identifier)
        if !remembered {
            confirmingSubscriber = true
            showWindow()
            let response = confirmSubscriber()
            confirmingSubscriber = false
            guard response == .alertFirstButtonReturn else { return }
        }

        guard peripheral.state == .poweredOn,
              inputCharacteristics.contains(where: { $0 === input }) else { return }
        let subscribedInputs = inputCharacteristics.filter { characteristic in
            characteristic.subscribedCentrals?.contains { $0.identifier == central.identifier } == true
        }
        guard !subscribedInputs.isEmpty else { return }
        if !remembered {
            guard receiverApproval.remember(central.identifier) else {
                show("送信先の承認を保存できませんでした。")
                return
            }
        }
        let previousInput = activeInput
        let newConnection = subscriber !== central
        if newConnection {
            bestEffortReleaseBeforeBoundary()
            peripheral.setDesiredConnectionLatency(.low, for: central)
            log("承認済みのBluetooth接続に低遅延を要求しました。")
        }
        subscriber = central
        subscriptions = Set(subscribedInputs.map(\.uuid))
        if newConnection || previousInput !== activeInput {
            changeConnection(connected: activeInput != nil && !sleeping)
        }
        let status = remembered ? "保存済みのWindowsに自動接続しました。"
            : "このWindowsを承認し、送信先として保存しました。"
        showConnection(status)
        if sendTask == nil {
            show(self.input.canSend ? "Windowsの入力欄を選んでから送信してください。"
                : InputError.unavailableMode.localizedDescription)
        }
    }

    private func confirmSubscriber() -> NSApplication.ModalResponse {
        let alert = NSAlert()
        alert.messageText = "このWindowsを送信先として承認しますか？"
        alert.informativeText = "承認したWindowsをこのMacに記憶し、次回から自動で接続を受け付けます。"
        alert.addButton(withTitle: "送信先として承認")
        alert.addButton(withTitle: "拒否")
        return alert.runModal()
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral,
                           didUnsubscribeFrom characteristic: CBCharacteristic) {
        guard subscriber === central,
              inputCharacteristics.contains(where: { $0 === characteristic }) else { return }
        bestEffortReleaseBeforeBoundary()
        subscriptions.remove(characteristic.uuid)
        if subscriptions.isEmpty { subscriber = nil }
        changeConnection(connected: activeInput != nil && !sleeping)
        showConnection("HID購読が変更されました。LED通知・キー解放を再確認します。")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didReceiveRead request: CBATTRequest) {
        let id = request.characteristic.uuid
        let value: Data
        switch id {
        case uuid("2A4E"): value = Data([protocolMode])
        case uuid("2A22"): value = Data(repeating: 0, count: 8)
        case uuid("2A32"): value = Data([0])
        case uuid("2A4D"):
            value = request.characteristic.properties.contains(.notify) ? Data(repeating: 0, count: 8) : Data([0])
        default:
            peripheral.respond(to: request, withResult: .attributeNotFound)
            return
        }
        guard request.offset <= value.count else {
            peripheral.respond(to: request, withResult: .invalidOffset)
            return
        }
        request.value = value.dropFirst(request.offset)
        peripheral.respond(to: request, withResult: .success)
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didReceiveWrite requests: [CBATTRequest]) {
        guard let first = requests.first else { return }
        for request in requests {
            guard manager === peripheral, peripheral.state == .poweredOn,
                  !sleeping, subscriber === request.central, request.offset == 0,
                  let value = request.value, value.count == 1 else { continue }
            let id = request.characteristic.uuid
            if id == uuid("2A4E"),
               services.first?.characteristics?.contains(where: { $0 === request.characteristic }) == true,
               value[0] <= 1, protocolMode != value[0] {
                bestEffortReleaseBeforeBoundary()
                protocolMode = value[0]
                changeConnection(connected: activeInput != nil)
            }
            if outputCharacteristics.contains(where: { $0 === request.characteristic }),
               activeInput != nil, value[0] & 0xe0 == 0 {
                receiveLED(value[0], generation: safety.generation)
            }
        }
        peripheral.respond(to: first, withResult: .success)
    }

    private var activeInput: CBMutableCharacteristic? {
        guard inputCharacteristics.count == 2 else { return nil }
        let preferred = inputCharacteristics[protocolMode == 1 ? 0 : 1]
        guard let subscriber, subscriptions.contains(preferred.uuid),
              preferred.subscribedCentrals?.contains(where: { $0.identifier == subscriber.identifier }) == true
        else { return nil }
        return preferred
    }

    @objc private func showTemplates() {
        guard sendTask == nil, screen.window.attachedSheet == nil,
              !screen.editor.hasMarkedText() else { return }
        let actions: [(NSButton, Selector)] = [
            (templateWindow.insert, #selector(insertTemplate)),
            (templateWindow.remove, #selector(confirmRemoveTemplate)),
            (templateWindow.replace, #selector(confirmReplaceTemplate)),
            (templateWindow.save, #selector(saveTemplate)),
            (templateWindow.close, #selector(closeTemplates))
        ]
        for (button, action) in actions {
            button.target = self
            button.action = action
        }
        templateWindow.name.stringValue = ""
        let canSave = TextTemplates.canSaveBody(screen.editor.string)
        templateWindow.status.stringValue = canSave ? "" : InputError.invalidLength.localizedDescription
        templateWindow.name.isEnabled = canSave
        templateWindow.save.isEnabled = canSave
        updateTemplateList()
        screen.window.beginSheet(templateWindow.window)
    }

    private func updateTemplateList() {
        templateWindow.list.removeAllItems()
        let hasEntries = !templates.entries.isEmpty
        templateWindow.list.addItems(withTitles: hasEntries ? templates.entries.map(\.name) : ["定型文はありません"])
        templateWindow.list.isEnabled = hasEntries
        templateWindow.insert.isEnabled = hasEntries
        templateWindow.remove.isEnabled = hasEntries
        templateWindow.replace.isEnabled = hasEntries && TextTemplates.canSaveBody(screen.editor.string)
    }

    @objc private func closeTemplates() {
        guard screen.window.attachedSheet === templateWindow.window else { return }
        screen.window.endSheet(templateWindow.window)
    }

    @objc private func insertTemplate() {
        let index = templateWindow.list.indexOfSelectedItem
        guard screen.window.attachedSheet === templateWindow.window, templates.entries.indices.contains(index) else { return }
        closeTemplates()
        let editor = screen.editor
        editor.insertText(templates.entries[index].body, replacementRange: editor.selectedRange())
        screen.window.makeFirstResponder(editor)
        updateEditorState()
    }

    @objc private func saveTemplate() {
        guard screen.window.attachedSheet === templateWindow.window, templateWindow.save.isEnabled,
              templateWindow.window.makeFirstResponder(nil) else { return }
        do {
            try templates.add(name: templateWindow.name.stringValue, body: screen.editor.string)
            templateWindow.name.stringValue = ""
            updateTemplateList()
            templateWindow.list.selectItem(at: templates.entries.count - 1)
            templateWindow.status.stringValue = "定型文を保存しました。"
        } catch {
            templateWindow.status.stringValue = error.localizedDescription
        }
    }

    @objc private func confirmRemoveTemplate() {
        confirmTemplateChange(isRemoval: true)
    }

    @objc private func confirmReplaceTemplate() {
        guard templateWindow.replace.isEnabled else { return }
        confirmTemplateChange(isRemoval: false)
    }

    private func confirmTemplateChange(isRemoval: Bool) {
        let index = templateWindow.list.indexOfSelectedItem
        guard screen.window.attachedSheet === templateWindow.window, templateWindow.window.attachedSheet == nil,
              templates.entries.indices.contains(index) else { return }
        let alert = NSAlert()
        alert.messageText = isRemoval ? "選んだ定型文を削除しますか？" : "定型文を現在の本文で上書きしますか？"
        alert.informativeText = templates.entries[index].name
        alert.alertStyle = .warning
        alert.addButton(withTitle: "キャンセル")
        alert.addButton(withTitle: isRemoval ? "削除" : "上書き")
        let body = screen.editor.string
        alert.beginSheetModal(for: templateWindow.window) { [weak self] response in
            guard let self, response == .alertSecondButtonReturn else { return }
            if isRemoval {
                self.templates.remove(at: index)
                self.updateTemplateList()
                self.templateWindow.status.stringValue = "定型文を削除しました。"
            } else {
                self.templates.replaceBody(at: index, with: body)
                self.templateWindow.status.stringValue = "定型文を上書きしました。"
            }
        }
    }

    @objc private func sendText() {
        guard screen.window.attachedSheet == nil else { return }
        if sendTask != nil {
            guard transferControl == .paused, readyToSend else { return }
            transferControl = .running
            beginTransferActivity()
            show("続きから送信しています。")
            return
        }
        guard !screen.editor.hasMarkedText() else {
            show("Macの入力を確定してから送信してください。")
            return
        }
        do {
            let transfer = try TextTransfer(text: screen.editor.string, settings: input)
            guard readyToSend else { return }
            log("入力設定：\(input.mode.label) / \(input.asciiSpeed.label)")
            startSend(transfer.units, preparation: transfer.preparation)
        } catch {
            show(error.localizedDescription)
        }
    }

    @objc private func pauseSending() {
        guard sendTask != nil, screen.window.attachedSheet == nil, transferControl == .running else { return }
        transferControl = .pausing
        show("処理中の1文字を確定して一時停止します。")
    }

    @objc private func stopSending() {
        guard sendTask != nil else { return }
        transferControl = .stopping
        show("処理中の1文字を確定して停止します。")
    }

    private func beginTransferActivity() {
        guard transferActivity == nil else { return }
        transferActivity = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiated, .idleSystemSleepDisabled], reason: "JIS BLE Inputの送信")
    }

    private func endTransferActivity() {
        guard let activity = transferActivity else { return }
        ProcessInfo.processInfo.endActivity(activity)
        transferActivity = nil
    }

    private func formatDuration(_ seconds: TimeInterval) -> String {
        let total = Int(seconds.rounded(.up))
        return "\(total / 60)分\(total % 60)秒"
    }

    private func startSend(_ units: [[KeyReport]], preparation: [KeyReport]) {
        guard sendTask == nil, readyToSend else { return }
        let generation = safety.generation
        let simulated = simulatedWrite
        runTransfer(units, preparation: preparation) { [self] report in
            await writeReport(report, generation: generation, simulated: simulated)
        }
    }

    @MainActor private func writeReport(_ report: KeyReport, generation: UInt64,
                                       simulated: ((KeyReport) async -> Bool)?) async -> Bool {
        for _ in 0..<80 {
            guard !Task.isCancelled, generation == safety.generation, readyToSend else { return false }
            if let simulated { return await simulated(report) }
            guard let manager, let central = subscriber, let characteristic = activeInput else { return false }
            if manager.updateValue(report.bytes, for: characteristic, onSubscribedCentrals: [central]) { return true }
            do { try await Task.sleep(nanoseconds: 5_000_000) }
            catch { return false }
        }
        return false
    }

    private func runTransfer(_ units: [[KeyReport]], preparation: [KeyReport],
                             write: @escaping @MainActor (KeyReport) async -> Bool) {
        let generation = safety.generation
        transferControl = .running
        transferAbortedForLED = false
        screen.progress.maxValue = Double(units.count)
        screen.progress.doubleValue = 0
        beginTransferActivity()
        sendTask = Task { @MainActor in
            var sent = 0
            var activeSince = now
            var activeElapsed: TimeInterval = 0
            let unitWaits = units.map { $0.reduce(0) { $0 + $1.delay } }
            var remainingWait = unitWaits.reduce(0, +)
            @MainActor func emit(_ reports: [KeyReport]) async -> Bool {
                for report in reports {
                    guard !Task.isCancelled, !transferAbortedForLED, generation == safety.generation,
                          readyToSend, await write(report) else { return false }
                    guard generation == safety.generation, !Task.isCancelled else { return false }
                    do { try await Task.sleep(nanoseconds: UInt64(report.delay) * 1_000_000) }
                    catch { return false }
                }
                return generation == safety.generation && !Task.isCancelled && !transferAbortedForLED
            }
            @MainActor func transferText() async {
                var needsPreparation = !preparation.isEmpty
                for unit in units {
                    guard generation == safety.generation, !Task.isCancelled else { return }
                    repeat {
                        if transferControl == .pausing {
                            transferControl = .paused
                            needsPreparation = !preparation.isEmpty
                            activeElapsed += now - activeSince
                            endTransferActivity()
                            show("一時停止中（\(sent)/\(units.count)文字）。")
                            while transferControl == .paused {
                                do { try await Task.sleep(nanoseconds: 80_000_000) }
                                catch { return }
                            }
                            activeSince = now
                        }
                        guard transferControl != .stopping else { return }
                        if needsPreparation {
                            guard await emit(preparation) else { return }
                            needsPreparation = false
                        }
                    } while transferControl == .pausing
                    guard transferControl != .stopping, await emit(unit) else { return }
                    guard generation == safety.generation, !Task.isCancelled, !transferAbortedForLED else { return }
                    sent += 1
                    remainingWait -= unitWaits[sent - 1]
                    screen.progress.doubleValue = Double(sent)
                    if transferControl == .running {
                        screen.message.stringValue = "送信中（\(sent)/\(units.count)文字）・残り目安 \(formatDuration(Double(remainingWait) / 1000))"
                    }
                }
            }

            await transferText()
            guard generation == safety.generation else { return }
            let stopped = transferControl == .stopping && !Task.isCancelled && !transferAbortedForLED
            transferControl = .stopping
            releaseKey()
            sendTask = nil
            endTransferActivity()
            let counts = "\(sent)/\(units.count)文字"
            if sent == units.count {
                let duration = formatDuration(activeElapsed + now - activeSince)
                show("Macからの送信完了（\(counts)・\(duration)）。Windowsの入力結果を確認してください。")
            } else if stopped {
                show("停止しました（\(counts)）。受信欄を確認してから新しい手動送信を行ってください。")
            } else {
                show("送信を中断しました（\(counts)）。部分文字の確定とキー状態を確認してください。")
            }
        }
        show("送信中（0/\(units.count)文字）")
    }

    #if UI_TEST
    @MainActor private func checkUI() async {
        precondition(screen.window.isVisible)
        precondition(!screen.send.isEnabled && !screen.stop.isEnabled && screen.settings.isEnabled)
        screen.editor.string = "日本語ABC123橋。\n2行目"
        updateButtons()
        precondition(screen.clear.isEnabled && !screen.send.isEnabled && screen.count.stringValue.hasPrefix("15"))
        if let flag = CommandLine.arguments.firstIndex(of: "--check-ui"),
           CommandLine.arguments.count > flag + 1, let content = screen.window.contentView {
            screen.window.appearance = NSAppearance(named: .aqua)
            content.wantsLayer = true
            content.layer?.backgroundColor = NSColor(srgbRed: 0.94, green: 0.94, blue: 0.94, alpha: 1).cgColor
            content.layoutSubtreeIfNeeded()
            content.displayIfNeeded()
            let bitmap = content.bitmapImageRepForCachingDisplay(in: content.bounds)!
            content.cacheDisplay(in: content.bounds, to: bitmap)
            try! bitmap.representation(using: .png, properties: [:])!.write(
                to: URL(fileURLWithPath: CommandLine.arguments[flag + 1]), options: .atomic)
        }
        clearText()
        precondition(screen.editor.string.isEmpty && !screen.clear.isEnabled)
        screen.window.close()
        precondition(!screen.window.isVisible && !applicationShouldTerminateAfterLastWindowClosed(NSApp))
        showWindow()
        precondition(screen.window.isVisible)
        let suite = "local.jisbleinput.check." + UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.register(defaults: ["sender-ascii-speed": AsciiSpeed.fast.rawValue])
        var reports: [KeyReport] = []
        simulatedWrite = { report in
            reports.append(report)
            return true
        }
        simulatedRelease = { true }
        changeConnection(connected: true)
        receiveLED(1, generation: safety.generation)
        screen.editor.string = "a"
        updateButtons()
        precondition(input.mode == .ascii && screen.send.isEnabled && screen.mode.numberOfItems == 2)
        let shortcut = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: .command,
                                       timestamp: 0, windowNumber: screen.window.windowNumber, context: nil,
                                       characters: "\r", charactersIgnoringModifiers: "\r",
                                       isARepeat: false, keyCode: 36)!
        for oldMode in [0, 2, 3, 4, 5, 99] {
            defaults.register(defaults: ["sender-input-mode": oldMode])
            input = InputSettings(defaults: defaults)
            updateInputOptions()
            show(InputError.unavailableMode.localizedDescription)
            precondition(input.mode == .japanese && !screen.send.isEnabled && !screen.speed.isEnabled)
            precondition(screen.mode.titleOfSelectedItem == InputMode.japanese.label)
            precondition(screen.mode.numberOfItems == 2 && screen.mode.selectedItem?.isEnabled == false)
            precondition(screen.message.stringValue.contains("英数字・URL"))
            screen.send.performClick(nil)
            precondition(!screen.send.performKeyEquivalent(with: shortcut))
            sendText()
            startSend([[KeyReport(0x04, delay: 0)]], preparation: [KeyReport(0x91, delay: 0)])
            precondition(sendTask == nil && reports.isEmpty)
            screen.mode.selectItem(withTag: InputMode.ascii.rawValue)
            changeMode()
            precondition(input.canSend && screen.send.isEnabled && screen.speed.isEnabled)
            precondition(screen.mode.numberOfItems == 2 && screen.speed.numberOfItems == 3)
            precondition(screen.speed.indexOfSelectedItem == AsciiSpeed.fast.rawValue)
        }
        screen.editor.string = "ABC午3"
        sendText()
        precondition(sendTask == nil && reports.isEmpty)
        precondition(screen.message.stringValue.contains("4文字目"))

        screen.editor.string = "abc"
        updateButtons()
        screen.send.performClick(nil)
        let clicked = sendTask
        precondition(clicked != nil)
        await clicked?.value
        precondition(reports.map { $0.bytes[2] } == [0x91, 0, 4, 0, 5, 0, 6, 0])
        precondition(reports.dropFirst(2).map(\.delay) == Array(repeating: 25, count: 6))
        precondition(screen.message.stringValue.hasPrefix("Macからの送信完了"))

        @MainActor func startAndPause() async {
            reports = []
            simulatedWrite = { report in
                reports.append(report)
                if reports.count == 3 { self.pauseSending() }
                return true
            }
            screen.editor.string = "ab"
            updateButtons()
            precondition(screen.send.performKeyEquivalent(with: shortcut) && sendTask != nil)
            for _ in 0..<200 where transferControl != .paused {
                try! await Task.sleep(nanoseconds: 5_000_000)
            }
            precondition(transferControl == .paused && reports.map { $0.bytes[2] } == [0x91, 0, 4, 0])
            precondition(transferActivity == nil)
            precondition(screen.progress.doubleValue == 1 && screen.send.title == "続行" && screen.stop.isEnabled)
            precondition(!screen.mode.isEnabled && !screen.speed.isEnabled)
        }
        await startAndPause()
        input.mode = .japanese
        updateButtons()
        precondition(!screen.send.isEnabled)
        screen.send.performClick(nil)
        precondition(!screen.send.performKeyEquivalent(with: shortcut))
        sendText()
        precondition(transferControl == .paused && reports.count == 4)
        input.mode = .ascii
        updateButtons()
        screen.send.performClick(nil)
        let resumed = sendTask
        await resumed?.value
        precondition(reports.map { $0.bytes[2] } == [0x91, 0, 4, 0, 0x91, 0, 5, 0])
        precondition(screen.progress.doubleValue == 2 && screen.mode.isEnabled && screen.speed.isEnabled && transferActivity == nil)

        await startAndPause()
        stopSending()
        let stopped = sendTask
        await stopped?.value
        precondition(reports.count == 4 && !screen.stop.isEnabled)

        await startAndPause()
        let cancelled = sendTask
        cancelled?.cancel()
        await cancelled?.value
        precondition(reports.count == 4 && !screen.stop.isEnabled)
        await checkConnectionIntegration()
        await checkTemplates()
        simulatedWrite = nil
        simulatedRelease = nil
        changeConnection(connected: false)
        print("UI and simulated transfer checks passed")
        fflush(stdout)
    }

    @MainActor private func checkTemplates() async {
        let draft = "先頭\n午後。 "
        precondition(screen.templates.isEnabled && screen.window.attachedSheet == nil)
        var writes = 0
        simulatedWrite = { _ in writes += 1; return true }
        screen.editor.string = draft
        screen.templates.performClick(nil)
        precondition(screen.window.attachedSheet === templateWindow.window && !templateWindow.insert.isEnabled)
        templateWindow.name.stringValue = "試験用"
        templateWindow.save.performClick(nil)
        precondition(templates.entries.first?.body == draft && templateWindow.insert.isEnabled)
        templateWindow.name.stringValue = "試験用"
        templateWindow.save.performClick(nil)
        precondition(templates.entries.count == 1 && templateWindow.status.stringValue.contains("同じ名前"))
        templateWindow.close.performClick(nil)
        precondition(screen.window.attachedSheet == nil && screen.editor.string == draft)
        screen.editor.string = "A B"
        screen.editor.setSelectedRange(NSRange(location: 1, length: 1))
        screen.templates.performClick(nil)
        sendText()
        precondition(sendTask == nil)
        templateWindow.insert.performClick(nil)
        precondition(screen.editor.string == "A" + draft + "B" && sendTask == nil && writes == 0)
        precondition(screen.window.attachedSheet == nil)
        screen.templates.performClick(nil)
        templateWindow.replace.performClick(nil)
        let replacement = templateWindow.window.attachedSheet!
        templateWindow.window.endSheet(replacement, returnCode: .alertSecondButtonReturn)
        for _ in 0..<100 where templates.entries.first?.body == draft {
            try! await Task.sleep(nanoseconds: 10_000_000)
        }
        precondition(templates.entries.first?.body == "A" + draft + "B" && templates.entries.first?.name == "試験用")
        templateWindow.remove.performClick(nil)
        let confirmation = templateWindow.window.attachedSheet!
        templateWindow.window.endSheet(confirmation, returnCode: .alertSecondButtonReturn)
        for _ in 0..<100 where !templates.entries.isEmpty {
            try! await Task.sleep(nanoseconds: 10_000_000)
        }
        precondition(templates.entries.isEmpty && !templateWindow.insert.isEnabled)
        precondition(screen.editor.string == "A" + draft + "B" && sendTask == nil && writes == 0)
        templateWindow.close.performClick(nil)
        print("Template UI checks passed: save, duplicate, insert at selection, confirm overwrite/removal, no automatic send")
    }

    @MainActor private func checkConnectionIntegration() async {
        var reports: [KeyReport] = []
        var allowRelease = false
        simulatedWrite = { reports.append($0); return true }
        simulatedRelease = { allowRelease }
        changeConnection(connected: true)
        screen.editor.string = "abc"
        receiveLED(0, generation: safety.generation)
        precondition(!screen.send.isEnabled && safety.release == .pending)
        allowRelease = true
        retryRelease()
        precondition(screen.send.isEnabled)
        receiveLED(2, generation: safety.generation)
        sendText()
        precondition(!screen.send.isEnabled && sendTask == nil && reports.isEmpty)
        receiveLED(0, generation: safety.generation)

        screen.editor.string = String(repeating: "a", count: 5000)
        updateEditorState()
        precondition(screen.send.isEnabled && screen.count.stringValue.hasPrefix(5000.formatted()))
        screen.editor.string += "a"
        updateEditorState()
        precondition(!screen.send.isEnabled && screen.message.stringValue.contains(5000.formatted()))
        screen.editor.string = "abc"
        updateEditorState()
        precondition(screen.send.isEnabled)
        simulatedWrite = { report in
            reports.append(report)
            if reports.count == 3 { self.changeConnection(connected: false) }
            return true
        }
        screen.send.performClick(nil)
        await sendTask?.value
        precondition(reports.count == 3 && transferActivity == nil && !screen.send.isEnabled)
        changeConnection(connected: true)
        precondition(!screen.send.isEnabled && safety.capsLock == .unknown)
        receiveLED(1, generation: safety.generation)
        precondition(screen.send.isEnabled && reports.count == 3 && sendTask == nil)
        willSleep(Notification(name: NSWorkspace.willSleepNotification))
        didWake(Notification(name: NSWorkspace.didWakeNotification))
        precondition(!screen.send.isEnabled && sendTask == nil && reports.count == 3)
        print("ASCII boundary, CapsLock, release gating, disconnect and sleep checks passed")
    }
    #endif

    private func changeConnection(connected: Bool) {
        let wasSending = sendTask != nil
        sendTask?.cancel()
        sendTask = nil
        endTransferActivity()
        releaseTimeoutTask?.cancel()
        safety.begin(connected: connected, now: now)
        transferControl = .stopping
        if connected { releaseKey() }
        if wasSending {
            show("接続状態の変化で送信を中断しました。受信欄とキー状態を確認し、新しい手動送信を行ってください。")
        }
        updateButtons()
    }

    private func receiveLED(_ value: UInt8, generation: UInt64) {
        guard generation == safety.generation, safety.connected else { return }
        safety.receiveLED(value, generation: generation)
        if sendTask != nil, safety.capsLock != .off {
            transferAbortedForLED = true
            transferControl = .stopping
            show("LED状態の変化で中断しました。部分文字が確定した可能性があります。受信欄を確認してください。")
        }
        updateButtons()
    }

    private func writeRelease() -> Bool {
        if let simulatedRelease { return simulatedRelease() }
        guard !sleeping, let manager, manager.state == .poweredOn,
              let central = subscriber, let characteristic = activeInput else { return false }
        return manager.updateValue(Data(repeating: 0, count: 8), for: characteristic,
                                   onSubscribedCentrals: [central])
    }

    private func bestEffortReleaseBeforeBoundary() {
        // This is synchronous on the delegate queue, before invalidating the old generation.
        if safety.connected { _ = writeRelease() }
    }

    private func releaseKey(manual: Bool = false) {
        let generation = safety.generation
        guard safety.requestRelease(generation: generation, now: now, manual: manual) else {
            updateButtons()
            return
        }
        retryRelease()
        let ticket = safety.releaseTicket
        releaseTimeoutTask?.cancel()
        if safety.release == .pending {
            let delay = max(0, safety.deadline - now)
            releaseTimeoutTask = Task { @MainActor in
                do { try await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000)) }
                catch { return }
                guard generation == safety.generation, ticket == safety.releaseTicket else { return }
                safety.expire(now: now)
                updateButtons()
            }
        }
        updateButtons()
    }

    private func retryRelease() {
        defer { updateButtons() }
        safety.expire(now: now)
        let generation = safety.generation
        let ticket = safety.releaseTicket
        guard safety.mayRelease(generation: generation, ticket: ticket, now: now) else { return }
        if writeRelease() {
            safety.accepted(generation: generation, ticket: ticket, now: now)
        }
    }

    func peripheralManagerIsReady(toUpdateSubscribers peripheral: CBPeripheralManager) {
        guard manager === peripheral else { return }
        retryRelease()
    }

}

let sender = KeyboardSender()
let app = NSApplication.shared
app.setActivationPolicy(.regular)
app.delegate = sender
app.run()
