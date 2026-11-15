import Foundation

func printReports(_ reports: [KeyReport]) {
    print(reports.map { report in
        report.bytes.map { String(format: "%02X", $0) }.joined() + "/\(report.delay)"
    }.joined(separator: ","))
}

let ascii = String(String.UnicodeScalarView((0x20...0x7e).map { Unicode.Scalar($0)! }))
for layout in ReceiverLayout.allCases {
    for speed in AsciiSpeed.allCases {
        var settings = InputSettings()
        settings.layout = layout
        precondition(settings.selectSpeed(at: speed.rawValue))
        let transfer = try TextTransfer(text: ascii + "\n", settings: settings)
        printReports(transfer.preparation)
        transfer.units.forEach(printReports)
    }
}
print("\(KeyboardText.maxUnits)/\(KeyboardText.countInputUnits("A😀\r\nB"))")
