using JisBleInput.Core;

static void Print(IEnumerable<KeyStep> reports) =>
    Console.WriteLine(string.Join(",", reports.Select(report =>
        $"{Convert.ToHexString(report.Stroke.ToReport())}/{report.DelayMilliseconds}")));

AsciiSpeed[] speeds = [AsciiSpeed.Relaxed, AsciiSpeed.Fast, AsciiSpeed.Fastest];
foreach (var layout in Enum.GetValues<ReceiverKeyboardLayout>())
    foreach (var speed in speeds)
    {
        Print(AsciiKeyEncoder.EncodePreparation());
        foreach (var code in Enumerable.Range(0x20, 95))
            Print(AsciiKeyEncoder.EncodeCharacter((char)code, layout, speed));
        Print(ImeKeyEncoder.EncodeLineBreak());
    }
Console.WriteLine($"{TextSnapshot.MaxInputUnits}/{TextSnapshot.CountInputUnits("A😀\r\nB")}");
