using System.Diagnostics;
using System.Runtime.InteropServices;
using JisBleInput.App;
using JisBleInput.Core;

var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"FAILED: {label}");
    passed++;
    Console.WriteLine($"PASS: {label}");
}

var macTarget = new SendTarget("synthetic-mac", 1);
var macTransport = new FakeTransport(macTarget);
var macWaits = new List<int>();
var macSender = new StagedSender(macTransport, (milliseconds, _) =>
{
    macWaits.Add(milliseconds);
    return Task.CompletedTask;
});
var macOptions = new SendOptions(JapaneseInput: JapaneseInputMethod.MacUnicodeHex);
await macSender.SendAsync("日本語ABC橋。", macTarget, options: macOptions);
var expectedCodes = new[] { "65e5", "672c", "8a9e", "0041", "0042", "0043", "6a4b", "3002" };
var captured = macTransport.Frames;
Check(macSender.State == SendState.KeysSent && captured.Count == 84
    && captured[0].All(value => value == 0)
    && captured[1].SequenceEqual(new byte[] { 0, 0, 0x6D, 0, 0, 0, 0, 0 })
    && captured[2].All(value => value == 0) && captured[^1].All(value => value == 0)
    && macWaits.Take(3).SequenceEqual(new[] { 120, 60, 500 })
    && captured.Count(frame => frame[2] == 0x6D) == 1 && macSender.ProcessedCharacters == 8,
    "Mac mixed text taps F18 once and waits for input switching before the first character");
for (var index = 0; index < expectedCodes.Length; index++)
{
    var frames = captured.Skip(3 + index * 10).Take(10).ToArray();
    Check(frames.Take(9).All(frame => frame[0] == 0x04)
        && frames[9].All(value => value == 0)
        && frames.Where((_, step) => step % 2 == 0).All(frame => frame[2] == 0)
        && frames.Where((_, step) => step % 2 == 1 && step < 9).Select(frame => frame[2])
            .SequenceEqual(expectedCodes[index].Select(digit => digit == '0' ? (byte)0x27
                : digit <= '9' ? (byte)(0x1E + digit - '1') : (byte)(0x04 + digit - 'a'))),
        $"Mac sends {expectedCodes[index]} with Option held and releases each digit without F5 or Enter");
}
Check(ImeKeyEncoder.EncodeMacUnicodeHex('日', JapaneseSpeed.Standard).Select(step => step.DelayMilliseconds)
        .SequenceEqual(new[] { 120, 60, 60, 60, 60, 60, 60, 60, 120, 180 })
    && ImeKeyEncoder.EncodeMacUnicodeHex('日', JapaneseSpeed.XiaoFastest).Select(step => step.DelayMilliseconds)
        .SequenceEqual(new[] { 40, 20, 20, 20, 20, 20, 20, 20, 40, 60 }),
    "Mac standard and trial pacing leave time before the first digit, between taps, and around Option release");
var fastMacSteps = ImeKeyEncoder.EncodeMacUnicodeHex('A', JapaneseSpeed.MacUnicodeHexFastest);
Check(fastMacSteps.Select(step => step.DelayMilliseconds).SequenceEqual(new[] { 20, 10, 10, 10, 10, 10, 10, 10, 20, 30 })
    && fastMacSteps.Select(step => step.Stroke)
        .SequenceEqual(ImeKeyEncoder.EncodeMacUnicodeHex('A', JapaneseSpeed.XiaoFastest).Select(step => step.Stroke)),
    "The 140 ms Mac preset keeps the Option and repeated-digit edges of the 280 ms preset");
var trialMacSteps = ImeKeyEncoder.EncodeMacUnicodeHex('A', JapaneseSpeed.MacUnicodeHexHighSpeedTrial);
Check(trialMacSteps.Select(step => step.DelayMilliseconds).Sum() == 70
    && trialMacSteps.SequenceEqual(fastMacSteps.Select(step => step with { DelayMilliseconds = step.DelayMilliseconds / 2 })),
    "The 70 ms Mac trial halves every 140 ms preset wait while preserving the HID sequence");
await RejectAsync(() => new StagedSender(new FakeTransport(macTarget), NoDelay).SendAsync("A", macTarget,
        options: new SendOptions(Speed: JapaneseSpeed.MacUnicodeHexHighSpeedTrial)),
    "The 70 ms trial is rejected in Windows Japanese mode");
await RejectAsync(() => new StagedSender(new FakeTransport(macTarget), NoDelay).SendAsync("A", macTarget,
        options: new SendOptions(Mode: TextInputMode.Ascii, Speed: JapaneseSpeed.MacUnicodeHexHighSpeedTrial,
            JapaneseInput: JapaneseInputMethod.MacUnicodeHex)),
    "The 70 ms trial is rejected in ASCII mode");

if (OperatingSystem.IsWindows())
{
    var measured = new List<(JapaneseSpeed Speed, double MillisecondsPerCharacter, byte[][] Frames)>();
    const string timingText = "日本語ABC橋。";
    foreach (var speed in new[] { JapaneseSpeed.MacUnicodeHexFastest, JapaneseSpeed.MacUnicodeHexHighSpeedTrial })
    {
        var timedTransport = new FakeTransport(macTarget);
        var timedMacSender = new StagedSender(timedTransport);
        var timer = new Stopwatch();
        timedMacSender.Progress += (count, _) =>
        {
            if (count == 1) timer.Start();
        };
        await timedMacSender.SendAsync(timingText, macTarget, options: macOptions with { Speed = speed });
        timer.Stop();
        Check(timedMacSender.State == SendState.KeysSent && timedMacSender.ProcessedCharacters == timingText.Length
            && timedMacSender.ReleaseReportSent == true, $"The Windows timer completes a full Mac send at {speed}");
        measured.Add((speed, timer.Elapsed.TotalMilliseconds / (timingText.Length - 1), timedTransport.Frames.ToArray()));
    }
    Check(measured[0].Frames.Select(Convert.ToHexString).SequenceEqual(measured[1].Frames.Select(Convert.ToHexString)),
        "The real Windows trial timer preserves the full report sequence including F18 and final release");
    foreach (var sample in measured)
        Console.WriteLine($"TIMING: {sample.Speed} {sample.MillisecondsPerCharacter:F1} ms/character (no Bluetooth)");
}
var macAbortTransport = new FakeTransport(macTarget);
using (var abort = new CancellationTokenSource())
{
    var delays = new List<int>();
    var macAbortSender = new StagedSender(macAbortTransport, (milliseconds, cancellation) =>
    {
        delays.Add(milliseconds);
        if (delays.Count == 5) abort.Cancel();
        cancellation.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    });
    await macAbortSender.SendSnapshotAsync(TextSnapshot.Create("日"), macTarget, macOptions, abort.Token);
    Check(delays.SequenceEqual(new[] { 120, 60, 500, 120, 60 })
        && macAbortTransport.Frames.Count == 6 && macAbortTransport.Frames[^1].All(value => value == 0)
        && macAbortSender.State == SendState.StoppedPartial && macAbortSender.ReleaseReportSent == true,
        "Stopping during a digit releases Option and the digit before another key can be sent");
}
var switchAbortTransport = new FakeTransport(macTarget);
using (var abort = new CancellationTokenSource())
{
    var switchAbortSender = new StagedSender(switchAbortTransport, (milliseconds, cancellation) =>
    {
        if (milliseconds == 500) abort.Cancel();
        cancellation.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    });
    await switchAbortSender.SendSnapshotAsync(TextSnapshot.Create("日"), macTarget, macOptions, abort.Token);
    Check(switchAbortSender.State == SendState.StoppedPartial && switchAbortSender.ProcessedCharacters == 0
        && switchAbortTransport.Frames.Count == 4 && switchAbortTransport.Frames.All(frame => frame[0] == 0)
        && switchAbortTransport.Frames[^1].All(value => value == 0),
        "cancel during the input switch leaves all keys released and sends no text");
}
captured.Clear();
await macSender.SendAsync("日\r\n本", macTarget, allowLineBreaks: true, options: macOptions);
Check(captured.Count(frame => frame[0] == 0 && frame[2] == 0x28) == 1,
    "Mac Unicode Hex sends Return only for an explicit line break and treats CRLF as one unit");

async Task RejectAsync(Func<Task> operation, string label)
{
    try { await operation(); }
    catch (ArgumentException) { Check(true, label); return; }
    catch (InvalidOperationException) { Check(true, label); return; }
    Check(false, label);
}

foreach (var architecture in Enum.GetValues<Architecture>())
{
    Check(ConnectionAvailability.IsAvailable(architecture, ConnectionMethod.LocalBluetooth)
        && ConnectionAvailability.IsAvailable(architecture, ConnectionMethod.XiaoUsb) == (architecture == Architecture.Arm64),
        $"transport availability follows process architecture {architecture}");
}

Task NoDelay(int _, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    return Task.CompletedTask;
}

var presetPath = Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data",
    "presets", Guid.NewGuid().ToString("N"), "text-presets.json");
var presetStore = new TextPresetStore(presetPath);
Check(presetStore.Load().Presets.Length == 0, "missing preset file starts empty");
var urlPreset = new TextPreset("URL", "https://example.com/", TextInputMode.Ascii, null, 1);
var messagePreset = new TextPreset("受付", "受付しました。\r\n", TextInputMode.Japanese, 0, 2);
presetStore.Save([urlPreset, messagePreset]);
Check(new TextPresetStore(presetPath).Load().Presets.SequenceEqual([urlPreset, messagePreset]),
    "presets retain exact text, mode and independent optional transport speeds across reload");
var fullLengthPresets = Enumerable.Range(1, TextPresetStore.MaxPresets)
    .Select(index => messagePreset with { Name = new string('日', 77) + $"{index:D3}", Text = new string('日', 5000) })
    .ToArray();
presetStore.Save(fullLengthPresets);
var loadedFullLengthPresets = presetStore.Load();
Check(!loadedFullLengthPresets.NeedsBackup && loadedFullLengthPresets.Presets.SequenceEqual(fullLengthPresets),
    "100 maximum-length Japanese presets fit the existing file limit and reload without loss");
presetStore.Save([urlPreset, messagePreset]);
foreach (var invalid in new[]
{
    urlPreset with { Text = "Ａ" },
    urlPreset with { BluetoothSpeed = 3 },
    urlPreset with { XiaoSpeed = -1 },
    messagePreset with { Text = new string('A', 5001) },
    messagePreset with { Name = "url" }
})
{
    await RejectAsync(() => { presetStore.Save([urlPreset, invalid]); return Task.CompletedTask; },
        "invalid preset cannot replace the existing file");
    Check(presetStore.Load().Presets.SequenceEqual([urlPreset, messagePreset]), "failed save preserves the previous presets");
}
presetStore.Save([]);
Check(presetStore.Load().Presets.Length == 0, "removing the last preset persists an empty collection");
const string legacyPresets = """
    {"Version":1,"Presets":[{"Name":"URL","Text":"https://example.com/","Mode":"Ascii","BluetoothSpeed":null,"XiaoSpeed":1},
    {"Name":"Invalid","Text":"test","Mode":"Ascii","XiaoSpeed":9},null,{"Name":"No mode","Text":"text"}]}
    """;
File.WriteAllText(presetPath, legacyPresets);
var recovered = presetStore.Load();
Check(recovered.Presets.SequenceEqual([urlPreset]) && recovered.Skipped == 3 && recovered.NeedsBackup,
    "legacy speeds migrate and malformed entries do not hide valid presets");
var backupPath = presetStore.Save(recovered.Presets, backupOriginal: true);
Check(backupPath is not null && File.ReadAllText(backupPath) == legacyPresets && File.ReadAllText(presetPath).Contains("\"standard\"")
    && !presetStore.Load().NeedsBackup, "recovery retains the original and writes stable speed identifiers");
File.WriteAllText(presetPath, "{");
Check(presetStore.Load() is { InvalidFile: true, NeedsBackup: true }, "broken JSON can be backed up for a fresh collection");
backupPath = presetStore.Save([urlPreset], backupOriginal: true);
Check(backupPath is not null && File.ReadAllText(backupPath) == "{" && presetStore.Load().Presets.SequenceEqual([urlPreset]),
    "a fresh collection preserves the unreadable original separately");

var preparation = ImeKeyEncoder.EncodePreparation();
Check(preparation.Count == 4 && preparation[0].Stroke == new KeyStroke(0, 0x90)
    && preparation[2].Stroke == new KeyStroke(1, 0x39), "IME setup uses LANG1 then Ctrl+CapsLock, without a toggle");
Check(preparation[1].Stroke == default && preparation[3].Stroke == default, "mode-selection modifiers are released before code entry");
Check(preparation[3].DelayMilliseconds >= 150, "Hiragana mode selection settles before the first code digit");
var strokes = ImeKeyEncoder.EncodeCharacter('橋');
Check(strokes.Count == 16, "complete character has explicit IME setup, four code digits, conversion and commit");
Check(strokes.Where((_, index) => index % 2 == 1).All(step => step.Stroke == default), "each key is released before the next");
Check(strokes[4].Stroke == new KeyStroke(0, 0x23) && strokes[6].Stroke == new KeyStroke(2, 0x04)
    && strokes[8].Stroke == new KeyStroke(0, 0x21) && strokes[10].Stroke == new KeyStroke(2, 0x05), "bridge uses independent expected usages for 6A4B");
Check(strokes[12].Stroke == new KeyStroke(0, 0x3E) && strokes[14].Stroke == new KeyStroke(0, 0x28), "unmodified F5 precedes exactly one Enter");
Check(strokes[13].DelayMilliseconds >= 300 && strokes[15].DelayMilliseconds >= 150, "conversion and commit each have independent settling time");
Check(strokes.All(step => step.Stroke.ToReport().Length == 8), "report payload has no extra report ID byte");
var latin = ImeKeyEncoder.EncodeCharacter('A');
Check(latin[4].Stroke.Usage == 0x27 && latin[6].Stroke.Usage == 0x27
    && latin[8].Stroke.Usage == 0x21 && latin[10].Stroke.Usage == 0x1E, "ASCII A remains zero-padded 0041 through the same route");
var descriptor = InspectDescriptor(HidKeyboardMap.Descriptor);
Check(descriptor.SupportsImeOn && descriptor.InputBits == 64, "actual app descriptor accepts LANG1 and retains eight-byte input reports");

var destination = new SendTarget("selected-host", 5);
foreach (var text in new[] { "A\nB", "A\rB", "A\tB", "e\u0301", "A\u200BB", "\uE000", char.ConvertFromUtf32(0x20BB7), new string('A', 5001), "" })
{
    var transport = new FakeTransport(destination);
    var sender = new StagedSender(transport, NoDelay);
    await RejectAsync(() => sender.SendAsync(text, destination), "invalid full snapshot is rejected");
    Check(transport.Frames.Count == 0, "invalid trailing characters cause no partial text or mode change");
}

var setupReceiver = new Receiver(InputMode.Off);
var setupTransport = new FakeTransport(destination) { Receive = setupReceiver.Accept };
var setup = new StagedSender(setupTransport, NoDelay);
await setup.PrepareImeAsync(destination);
Check(setup.State == SendState.Ready && setupReceiver.Mode == InputMode.Hiragana, "optional preparation selects Hiragana without typing text");
Check(!setupTransport.HasKey(0x3E) && !setupTransport.HasKey(0x28) && setup.ProcessedCharacters == 0, "preparation alone sends neither F5 nor Enter");

var repeatMacTransport = new FakeTransport(destination);
var repeatMacSender = new StagedSender(repeatMacTransport, NoDelay);
var macSnapshot = TextSnapshot.Create("橋");
for (var sendIndex = 0; sendIndex < 2; sendIndex++)
{
    var offset = repeatMacTransport.Frames.Count;
    await repeatMacSender.SendSnapshotAsync(macSnapshot, destination, macOptions);
    Check(repeatMacTransport.Frames[offset + 1][2] == 0x6D
        && repeatMacTransport.Frames.Skip(offset).Count(report => report[2] == 0x6D) == 1,
        "each Mac Japanese send starts with one F18 tap");
}
var reportCount = repeatMacTransport.Frames.Count;
await RejectAsync(() => repeatMacSender.SendSnapshotAsync(macSnapshot,
        destination with { Generation = destination.Generation + 1 }, macOptions),
    "Mac Japanese send rejects a stale recipient generation");
Check(repeatMacTransport.Frames.Count == reportCount, "a stale recipient receives neither F18 nor the text");

var textToSend = "日本語ABC橋。 髙﨑ｱｲｳ　～−①㈱";
foreach (var initialMode in Enum.GetValues<InputMode>())
    foreach (var ignoreLang1 in new[] { false, true })
        foreach (var modeDrift in new[] { false, true })
        {
            var receiver = new Receiver(initialMode) { IgnoreLang1 = ignoreLang1, DriftOnCommit = modeDrift };
            var transport = new FakeTransport(destination) { Receive = receiver.Accept };
            var sender = new StagedSender(transport, NoDelay);
            var progress = new List<(int Done, int Total)>();
            sender.Progress += (done, total) => progress.Add((done, total));
            await sender.SendAsync(textToSend, destination);
            Check(receiver.Output == textToSend && receiver.TimestampCount == 0 && receiver.UnexpectedEnterCount == 0,
                $"one operation preserves mixed text from {initialMode}, LANG1 ignored={ignoreLang1}, commit drift={modeDrift}");
            Check(sender.State == SendState.KeysSent && sender.ProcessedCharacters == textToSend.Length
                && progress.Select(p => p.Done).SequenceEqual(Enumerable.Range(1, textToSend.Length))
                && progress.All(p => p.Total == textToSend.Length), "entire snapshot completes without per-character UI confirmations");
            Check(transport.Frames.Count(f => f[2] == 0x90) == textToSend.Length
                && transport.Frames.Count(f => f[0] == 1 && f[2] == 0x39) == textToSend.Length,
                "both mode selectors are repeated before every character, including after commit drift");
        }

var legacyReceiver = new Receiver(InputMode.Hiragana) { DriftOnCommit = true };
foreach (var c in "日本")
    foreach (var step in ImeKeyEncoder.EncodeCharacter(c).Skip(4)) legacyReceiver.Accept(step.Stroke.ToReport());
Check(legacyReceiver.Output.StartsWith("日672C") && legacyReceiver.TimestampCount == 1
    && legacyReceiver.UnexpectedEnterCount == 1, "receiver model reproduces legacy raw-code, timestamp and Enter failure after mode drift");

foreach (var length in new[] { 100, 1000, 5000 })
{
    const string block = "01日本語ABC橋。";
    var longText = string.Concat(Enumerable.Repeat(block, length / block.Length));
    var receiver = new Receiver(InputMode.Off) { IgnoreLang1 = true, DriftOnCommit = true };
    var transport = new FakeTransport(destination) { Receive = receiver.Accept };
    var sender = new StagedSender(transport, NoDelay);
    await sender.SendAsync(longText, destination);
    Check(longText.Length == length && receiver.Output == longText
        && sender.ProcessedCharacters == length && receiver.TimestampCount == 0
        && receiver.UnexpectedEnterCount == 0, $"{length}-character paragraph is exact in the independent mode-drift receiver model");
    Check(transport.Targets.All(target => target == destination), "every report remains bound to the original destination generation");
}

var singleReceiver = new Receiver(InputMode.Off);
var single = new StagedSender(new FakeTransport(destination) { Receive = singleReceiver.Accept }, NoDelay);
await single.SendAsync("日", destination);
Check(single.State == SendState.KeysSent && singleReceiver.Output == "日", "single-character and paragraph sends use the same automatic route");

var pendingTransport = new FakeTransport(destination);
var pendingSender = new StagedSender(pendingTransport, NoDelay);
var started = new TaskCompletionSource();
var continueWrite = new TaskCompletionSource();
pendingTransport.BeforeWrite = async (report, token) =>
{
    if (pendingTransport.Frames.Count == 0 && report[2] != 0)
    {
        started.TrySetResult();
        await continueWrite.Task.WaitAsync(token);
    }
};
var draft = "A橋B";
var pending = pendingSender.SendAsync(draft, destination);
await started.Task;
draft = "edited text";
await RejectAsync(() => pendingSender.SendAsync(draft, destination), "concurrent send cannot replace a running snapshot");
await RejectAsync(() => pendingSender.PrepareImeAsync(destination), "IME preparation cannot interleave with the send queue");
continueWrite.SetResult();
await pending;
Check(Decode(pendingTransport.Frames) == "A橋B", "editing the source draft does not change the in-flight snapshot");

var gracefulTransport = new FakeTransport(destination);
var gracefulSender = new StagedSender(gracefulTransport, NoDelay);
var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
gracefulTransport.BeforeWrite = async (report, token) =>
{
    if (report.All(value => value == 0)) return;
    writeStarted.TrySetResult();
    await ack.Task.WaitAsync(token);
};
var gracefulSend = gracefulSender.SendAsync("橋", destination);
await writeStarted.Task;
gracefulSender.Cancel(finishCurrentWrite: true);
Check(!gracefulSend.IsCompleted && gracefulTransport.Frames.Count == 0, "graceful stop waits for an outstanding write");
ack.SetResult();
await gracefulSend;
Check(gracefulSender.State == SendState.StoppedPartial && gracefulSender.ReleaseReportSent == true
    && gracefulTransport.Frames.Count == 2 && gracefulTransport.Frames[^1].All(value => value == 0),
    "after the outstanding write, graceful stop emits only a release");

foreach (var stageEnd in new[] { 1, 4, 12, 14, 16 })
{
    var transport = new FakeTransport(destination);
    StagedSender? sender = null;
    Task CancelAtStage(int _, CancellationToken token)
    {
        if (transport.Frames.Count == stageEnd) sender!.Cancel();
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    sender = new StagedSender(transport, CancelAtStage);
    await sender.SendAsync("日本", destination);
    var nonRelease = transport.Frames.Count(frame => frame.Any(value => value != 0));
    Check(sender.State == SendState.StoppedPartial && sender.ReleaseReportSent == true
        && nonRelease <= (stageEnd + 1) / 2, "cancel at IME/code/F5/commit stage stops all later keys and attempts release");
    if (stageEnd <= 14) Check(!transport.HasKey(0x28), "cancel before commit prevents later Enter");
    Check(transport.Frames.All(f => f[2] != 0x2A && (f[0] & 0x08) == 0), "stopping adds no Backspace, Windows shortcuts or automatic remote repair");
}

var changedTransport = new FakeTransport(destination);
var changed = new StagedSender(changedTransport, NoDelay);
changedTransport.BeforeWrite = (_, _) =>
{
    if (changedTransport.Frames.Count == 16) changedTransport.Current = destination with { Generation = 6 };
    return Task.CompletedTask;
};
await RejectAsync(() => changed.SendAsync("日本", destination), "reconnect between characters stops the original snapshot");
Check(changed.State == SendState.Faulted && changed.ProcessedCharacters == 1
    && changed.ReleaseReportSent == false && Decode(changedTransport.Frames) == "日", "no stale tail is sent after a connection generation change");

var releaseTransport = new FakeTransport(destination);
releaseTransport.BeforeWrite = (_, _) =>
{
    if (releaseTransport.Frames.Count == 16) throw new InvalidOperationException("release failure");
    return Task.CompletedTask;
};
var releaseSender = new StagedSender(releaseTransport, NoDelay);
await releaseSender.SendAsync("日", destination);
Check(releaseSender.State == SendState.StoppedPartial && releaseSender.ReleaseReportSent == false, "failed cleanup is never reported as successful completion");

var unsupported = new Receiver(InputMode.Off) { IgnoreLang1 = true, IgnoreCtrlCaps = true };
var unsupportedSender = new StagedSender(new FakeTransport(destination) { Receive = unsupported.Accept }, NoDelay);
await unsupportedSender.SendAsync("日", destination);
Check(unsupportedSender.State == SendState.KeysSent && unsupported.Output != "日",
    "transport success deliberately does not claim receiver-side IME conversion success");

// Pause/resume must not change the established per-character IME sequence.
var pauseReceiver = new Receiver(InputMode.Off) { DriftOnCommit = true };
var pauseTransport = new FakeTransport(destination) { Receive = pauseReceiver.Accept };
var pauseSender = new StagedSender(pauseTransport, NoDelay);
Check(!pauseSender.RequestPause(), "idle sender has no pauseable queue");
await RejectAsync(() => { pauseSender.Resume(); return Task.CompletedTask; }, "idle sender cannot resume an old queue");
pauseSender.Progress += (done, _) => { if (done == 1) pauseSender.RequestPause(); };
var pauseRun = pauseSender.SendAsync("日本語", destination);
Check(pauseSender.State == SendState.Paused && !pauseRun.IsCompleted, "pause retains the original worker instead of completing or restarting it");
Check(pauseReceiver.Output == "日" && pauseSender.ProcessedCharacters == 1
    && pauseTransport.Frames.Last().All(b => b == 0), "pause occurs after IME commit and an all-keys-up report");
var framesAtPause = pauseTransport.Frames.Count;
Check(!pauseSender.RequestPause() && pauseTransport.Frames.Count == framesAtPause, "repeated pause requests create no extra keys");
await RejectAsync(() => pauseSender.SendAsync("X", destination), "paused snapshot cannot be replaced by a new send");
await RejectAsync(() => pauseSender.PrepareImeAsync(destination), "manual IME setup cannot interleave with a paused queue");
pauseSender.Resume();
await pauseRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(pauseReceiver.Output == "日本語" && pauseSender.State == SendState.KeysSent
    && pauseSender.ProcessedCharacters == 3, "resume continues from the next unsent character without duplication");
Check(pauseTransport.Frames.Count(f => f[2] == 0x3E) == 3 && pauseTransport.Frames.Count(f => f[2] == 0x28) == 3,
    "pause/resume adds no repeated conversion or Enter");
await RejectAsync(() => { pauseSender.Resume(); return Task.CompletedTask; }, "completed queue cannot be resumed");

var midPauseReceiver = new Receiver(InputMode.Hiragana);
var midPauseTransport = new FakeTransport(destination) { Receive = midPauseReceiver.Accept };
var midPauseSender = new StagedSender(midPauseTransport, NoDelay);
var sawPausing = false;
midPauseSender.StateChanged += state => { if (state == SendState.Pausing) sawPausing = true; };
midPauseTransport.BeforeWrite = (_, _) =>
{
    if (midPauseTransport.Frames.Count == 5) midPauseSender.RequestPause();
    return Task.CompletedTask;
};
var midPauseRun = midPauseSender.SendAsync("A橋B", destination);
Check(sawPausing && midPauseSender.State == SendState.Paused && midPauseReceiver.Output == "A",
    "pause requested in a hexadecimal code finishes that character before pausing");
Check(midPauseTransport.Frames.Count == 17 && midPauseTransport.Frames.Last().All(b => b == 0),
    "pause in a code never leaves its Shift modifier or unfinished composition waiting");
midPauseSender.Resume();
await midPauseRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(midPauseReceiver.Output == "A橋B", "resuming a mid-code pause preserves the exact original paragraph");

var repeatedReceiver = new Receiver(InputMode.Off);
var repeatedSender = new StagedSender(new FakeTransport(destination) { Receive = repeatedReceiver.Accept }, NoDelay);
var pauseSignals = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
var pauseIndex = 0;
repeatedSender.StateChanged += state => { if (state == SendState.Paused) pauseSignals[pauseIndex++].TrySetResult(); };
repeatedSender.Progress += (done, _) => { if (done < 3) repeatedSender.RequestPause(); };
var repeatedRun = repeatedSender.SendAsync("日本語", destination);
await pauseSignals[0].Task.WaitAsync(TimeSpan.FromSeconds(3));
Check(repeatedSender.ProcessedCharacters == 1, "first pause records one processed character");
repeatedSender.Resume();
await pauseSignals[1].Task.WaitAsync(TimeSpan.FromSeconds(3));
Check(repeatedSender.ProcessedCharacters == 2 && repeatedReceiver.Output == "日本", "multiple pauses retain one snapshot and ordered progress");
repeatedSender.Resume();
await repeatedRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(repeatedReceiver.Output == "日本語" && pauseIndex == 2, "multiple resumes never start a second send worker");

var beforeFirstTransport = new FakeTransport(destination);
var beforeFirstSender = new StagedSender(beforeFirstTransport, NoDelay);
beforeFirstSender.StateChanged += state => { if (state == SendState.Sending) beforeFirstSender.RequestPause(); };
var beforeFirstRun = beforeFirstSender.SendAsync("日", destination);
Check(beforeFirstSender.State == SendState.Paused && beforeFirstSender.ProcessedCharacters == 0
    && beforeFirstTransport.Frames.All(f => f.All(b => b == 0)), "pause before the first character sends no input keys");
beforeFirstSender.Cancel();
await beforeFirstRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(beforeFirstSender.State == SendState.StoppedPartial && beforeFirstSender.ReleaseReportSent == true,
    "stop interrupts an indefinitely paused queue and releases keys");
await RejectAsync(() => { beforeFirstSender.Resume(); return Task.CompletedTask; }, "stop discards the paused continuation");

foreach (var changedDestination in new[] { destination with { Generation = 6 }, destination with { Id = "different-host" } })
{
    var t = new FakeTransport(destination);
    var queue = new StagedSender(t, NoDelay);
    queue.Progress += (done, _) => { if (done == 1) queue.RequestPause(); };
    var runningQueue = queue.SendAsync("日本", destination);
    var written = t.Frames.Count;
    t.Current = changedDestination;
    await RejectAsync(() => { queue.Resume(); return Task.CompletedTask; }, "resume rejects a reconnected generation or different destination");
    await runningQueue.WaitAsync(TimeSpan.FromSeconds(3));
    Check(t.Frames.Count == written && queue.State == SendState.StoppedPartial && queue.ReleaseReportSent == false,
        "reconnect while paused never resends a prefix or stale tail");
}

var failedPauseTransport = new FakeTransport(destination);
var failedPauseSender = new StagedSender(failedPauseTransport, NoDelay);
failedPauseSender.Progress += (done, _) => { if (done == 1) failedPauseSender.RequestPause(); };
failedPauseTransport.BeforeWrite = (_, _) =>
{
    if (failedPauseTransport.Frames.Count == 16) throw new InvalidOperationException("pause release failed");
    return Task.CompletedTask;
};
await RejectAsync(() => failedPauseSender.SendAsync("日本", destination), "failed all-keys-up report cannot enter a resumable paused state");
Check(failedPauseSender.State == SendState.Faulted && failedPauseSender.ReleaseReportSent == false,
    "pause cleanup failure is reported as an error, not a safe pause");

var stopPausingTransport = new FakeTransport(destination);
var stopPausingSender = new StagedSender(stopPausingTransport, NoDelay);
stopPausingTransport.BeforeWrite = (_, _) =>
{
    if (stopPausingTransport.Frames.Count == 5) { stopPausingSender.RequestPause(); stopPausingSender.Cancel(); }
    return Task.CompletedTask;
};
await stopPausingSender.SendAsync("日本", destination);
Check(stopPausingSender.State == SendState.StoppedPartial && !stopPausingTransport.HasKey(0x28),
    "immediate stop while pausing does not finish the character or add Enter");

var lastPauseSender = new StagedSender(new FakeTransport(destination), NoDelay);
lastPauseSender.Progress += (_, _) => lastPauseSender.RequestPause();
await lastPauseSender.SendAsync("日", destination);
Check(lastPauseSender.State == SendState.KeysSent, "pause requested after the last character finishes normally instead of waiting forever");

var clock = new ManualClock();
Task AdvanceClock(int milliseconds, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
    return Task.CompletedTask;
}
var timedSender = new StagedSender(new FakeTransport(destination), AdvanceClock, clock);
Check(timedSender.ProgressInfo.ActiveElapsed == TimeSpan.Zero && timedSender.ProgressInfo.EstimatedRemaining is null,
    "progress before a send contains no invented speed estimate");
timedSender.Progress += (done, _) => { if (done == 2) timedSender.RequestPause(); };
var timedRun = timedSender.SendAsync("日本語A", destination);
var timedPause = timedSender.ProgressInfo;
Check(timedPause.ProcessedCharacters == 2 && timedPause.TotalCharacters == 4
    && timedPause.ActiveElapsed == TimeSpan.FromSeconds(2.2) && timedPause.EstimatedRemaining == TimeSpan.FromSeconds(2.2),
    "elapsed time and remaining estimate use processed characters and actual active time");
clock.Advance(TimeSpan.FromMinutes(10));
Check(timedSender.ProgressInfo == timedPause, "time spent paused does not increase elapsed processing time or the estimate");
timedSender.Resume();
await timedRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(timedSender.ProgressInfo.ActiveElapsed == TimeSpan.FromSeconds(4.4)
    && timedSender.ProgressInfo.EstimatedRemaining == TimeSpan.Zero, "completion excludes paused time and reports zero remaining");
clock.Advance(TimeSpan.FromMinutes(10));
Check(timedSender.ProgressInfo.ActiveElapsed == TimeSpan.FromSeconds(4.4), "completed progress no longer accumulates time");

// Multiline opt-in is a receiver-field precondition, not an app profile.
var mixedEndings = "\r\n日\n\n本\r語\r\n";
var multilineSnapshot = TextSnapshot.Create(mixedEndings, allowLineBreaks: true);
Check(multilineSnapshot.Text == mixedEndings && multilineSnapshot.UnitCount == 8,
    "snapshot preserves source text and counts CRLF as one input unit");
Check(multilineSnapshot.Units.Select(u => u.SourceOffset).SequenceEqual(new[] { 0, 2, 3, 4, 5, 6, 7, 8 })
    && multilineSnapshot.Units.Select(u => u.SourceLength).SequenceEqual(new[] { 2, 1, 1, 1, 1, 1, 1, 2 }),
    "units retain exact source spans including paired CRLF");
Check(TextSnapshot.CountInputUnits(mixedEndings) == multilineSnapshot.UnitCount
    && TextSnapshot.CountInputUnits("") == 0, "draft count agrees with logical send progress");
Check(multilineSnapshot.Units is System.Collections.Generic.IList<TextUnit> { IsReadOnly: true },
    "snapshot unit collection cannot be mutated by callers");
var breakSteps = ImeKeyEncoder.EncodeLineBreak();
Check(breakSteps.Count == 2 && breakSteps[0].Stroke == new KeyStroke(0, 0x28)
    && breakSteps[1].Stroke == default && breakSteps[1].DelayMilliseconds >= 200,
    "line break has one unmodified Enter, release and settling time, with no Unicode conversion");
await RejectAsync(() => { ImeKeyEncoder.EncodeCharacter('\n'); return Task.CompletedTask; },
    "character encoder still rejects controls rather than encoding 000A");

foreach (var invalid in new[] { "日\r\n本\t", "日\n\u000B", "日\n\u2028", "日\n\u2029", "日\n\u0301", "日\n😀", "日\n\0", new string('日', 5000) + "\r\n", string.Concat(Enumerable.Repeat("\r\n", 5001)) })
{
    var transport = new FakeTransport(destination);
    await RejectAsync(() => new StagedSender(transport, NoDelay).SendAsync(invalid, destination, allowLineBreaks: true),
        "unsupported or oversized multiline snapshot is rejected before transmission");
    Check(transport.Frames.Count == 0, "invalid multiline text sends no partial prefix");
}

foreach (var source in new[] { "日\r\n本", "日\n本", "日\r本", "\r\n", "\n\n", mixedEndings, "日\r\n\r\n本\r\n" })
    foreach (var nativeEnding in new[] { "\r\n", "\n" })
    {
        var receiver = new Receiver(InputMode.Off) { IgnoreLang1 = true, DriftOnCommit = true, LineEnding = nativeEnding };
        var transport = new FakeTransport(destination) { Receive = receiver.Accept };
        var sender = new StagedSender(transport, NoDelay);
        var progressEvents = new List<(int Done, int Total)>();
        sender.Progress += (done, total) => progressEvents.Add((done, total));
        await sender.SendAsync(source, destination, allowLineBreaks: true);
        var expected = source.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", nativeEnding);
        var lineCount = source.Replace("\r\n", "\n").Replace('\r', '\n').Count(c => c == '\n');
        var textCount = source.Count(c => c is not '\r' and not '\n');
        Check(receiver.Output == expected && receiver.UnexpectedEnterCount == 0 && receiver.TimestampCount == 0,
            "multiline model preserves leading, trailing and blank lines in receiver-native line endings");
        Check(transport.Frames.Count(f => f[2] == 0x28) == textCount + lineCount
            && transport.Frames.Count(f => f[2] == 0x3E) == textCount
            && transport.Frames.Count(f => f[2] == 0x90) == textCount,
            "each CRLF/LF/CR adds exactly one Enter and no F5 or IME mode keys");
        Check(sender.State == SendState.KeysSent && sender.ProcessedCharacters == textCount + lineCount
            && progressEvents.Select(p => p.Done).SequenceEqual(Enumerable.Range(1, textCount + lineCount))
            && progressEvents.All(p => p.Total == textCount + lineCount),
            "multiline progress counts CRLF once and completes without adding a final newline");
    }

foreach (var limitText in new[] { string.Concat(Enumerable.Repeat("日\r\n", 2500)), string.Concat(Enumerable.Repeat("\r\n", 5000)) })
{
    var receiver = new Receiver(InputMode.Off) { LineEnding = "\r\n", DriftOnCommit = true };
    var queue = new StagedSender(new FakeTransport(destination) { Receive = receiver.Accept }, NoDelay);
    await queue.SendAsync(limitText, destination, allowLineBreaks: true);
    Check(receiver.Output == limitText && queue.TotalCharacters == 5000 && queue.ProcessedCharacters == 5000,
        "5000-unit multiline limit includes CRLF pairs without truncating source or adding lines");
}

foreach (var pauseAfter in new[] { 1, 2, 3 })
{
    var receiver = new Receiver(InputMode.Off) { LineEnding = "\r\n", DriftOnCommit = true };
    var transport = new FakeTransport(destination) { Receive = receiver.Accept };
    var queue = new StagedSender(transport, NoDelay);
    queue.Progress += (done, _) => { if (done == pauseAfter) queue.RequestPause(); };
    var running = queue.SendAsync("日\r\n\r\n本", destination, allowLineBreaks: true);
    Check(queue.State == SendState.Paused && queue.ProcessedCharacters == pauseAfter
        && transport.Frames.Last().All(b => b == 0), "pause before or after CRLF ends at a released whole-unit boundary");
    await RejectAsync(() => queue.SendAsync("別\n稿", destination, allowLineBreaks: true), "paused multiline snapshot cannot be replaced");
    queue.Resume();
    await running.WaitAsync(TimeSpan.FromSeconds(3));
    Check(receiver.Output == "日\r\n\r\n本" && transport.Frames.Count(f => f[2] == 0x28) == 4,
        "multiline resume duplicates neither CRLF nor an empty line");
}

var midBreakReceiver = new Receiver(InputMode.Off) { LineEnding = "\n" };
var midBreakTransport = new FakeTransport(destination) { Receive = midBreakReceiver.Accept };
var midBreakSender = new StagedSender(midBreakTransport, NoDelay);
midBreakTransport.BeforeWrite = (_, _) =>
{
    if (midBreakTransport.Frames.Count == 17) midBreakSender.RequestPause();
    return Task.CompletedTask;
};
var midBreakRun = midBreakSender.SendAsync("日\r\n本", destination, allowLineBreaks: true);
Check(midBreakSender.State == SendState.Paused && midBreakReceiver.Output == "日\n"
    && midBreakSender.ProcessedCharacters == 2, "pause requested after newline key-down completes its release and counts CRLF once");
midBreakSender.Resume();
await midBreakRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(midBreakReceiver.Output == "日\n本", "resuming after newline key-down does not repeat Enter");

foreach (var stopAt in new[] { 16, 17, 18 })
{
    var receiver = new Receiver(InputMode.Off) { LineEnding = "\n" };
    var transport = new FakeTransport(destination) { Receive = receiver.Accept };
    StagedSender? queue = null;
    Task StopNearBreak(int _, CancellationToken token)
    {
        if (transport.Frames.Count == stopAt) queue!.Cancel();
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    queue = new StagedSender(transport, StopNearBreak);
    await queue.SendAsync("日\r\n本", destination, allowLineBreaks: true);
    Check(queue.State == SendState.StoppedPartial && queue.ReleaseReportSent == true
        && receiver.Output == (stopAt == 16 ? "日" : "日\n"),
        "stop around a line break prevents all later characters and never repairs or replays partial input");
}

foreach (var changeAt in new[] { 16, 17, 18 })
{
    var receiver = new Receiver(InputMode.Off) { LineEnding = "\n" };
    var transport = new FakeTransport(destination) { Receive = receiver.Accept };
    transport.BeforeWrite = (_, _) =>
    {
        if (transport.Frames.Count == changeAt) transport.Current = destination with { Generation = 6 };
        return Task.CompletedTask;
    };
    var queue = new StagedSender(transport, NoDelay);
    await RejectAsync(() => queue.SendAsync("日\r\n本", destination, allowLineBreaks: true), "generation change around newline refuses stale tail");
    Check(queue.State == SendState.Faulted && queue.ReleaseReportSent == false
        && receiver.Output == (changeAt == 16 ? "日" : "日\n")
        && transport.Targets.All(t => t == destination), "disconnect around newline sends no queued text to a new connection");
}

// Speed/layout are immutable choices bound to one send operation.
Check(descriptor.SupportsImeOff && descriptor.InputBits == 64,
    "actual descriptor includes LANG2/ImeOff without changing report width");
var quickOptions = new SendOptions(Speed: JapaneseSpeed.SlightlyFaster);
foreach (var c in "日A橋 。")
{
    var standard = ImeKeyEncoder.EncodeCharacter(c);
    var quick = ImeKeyEncoder.EncodeCharacter(c, JapaneseSpeed.SlightlyFaster);
    Check(standard.Select(s => s.Stroke).SequenceEqual(quick.Select(s => s.Stroke))
        && standard.Sum(s => s.DelayMilliseconds) == 1100 && quick.Sum(s => s.DelayMilliseconds) == 900,
        "slightly-faster Japanese preserves all key strokes and reduces only fixed waiting time");
    Check(standard.Where((_, i) => i % 2 == 0).Select(s => s.DelayMilliseconds)
        .SequenceEqual(quick.Where((_, i) => i % 2 == 0).Select(s => s.DelayMilliseconds)),
        "Japanese speed option never shortens any key-down hold");
}
Check(ImeKeyEncoder.EncodePreparation(JapaneseSpeed.SlightlyFaster).Sum(s => s.DelayMilliseconds) == 300,
    "quick preparation keeps independent IME selectors with 300ms total wait");
foreach (var c in "日A橋 。")
{
    var standard = ImeKeyEncoder.EncodeCharacter(c);
    var faster = ImeKeyEncoder.EncodeCharacter(c, JapaneseSpeed.Faster);
    Check(standard.Select(s => s.Stroke).SequenceEqual(faster.Select(s => s.Stroke)) && faster.Sum(s => s.DelayMilliseconds) == 700
        && faster.All(s => s.DelayMilliseconds >= 20),
        "faster Japanese keeps the same IME key sequence with 0.70s fixed wait and no key shorter than 20ms");
}
Check(ImeKeyEncoder.EncodePreparation(JapaneseSpeed.Faster).Sum(s => s.DelayMilliseconds) == 240,
    "faster preparation still selects Hiragana with both independent selectors");
await RejectAsync(() => { ImeKeyEncoder.EncodeCharacter('A', (JapaneseSpeed)99); return Task.CompletedTask; },
    "Japanese encoder rejects unsupported speed values");
var quickReceiver = new Receiver(InputMode.Off) { IgnoreLang1 = true, DriftOnCommit = true, LineEnding = "\r\n" };
var quickClock = new ManualClock();
Task QuickDelay(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); quickClock.Advance(TimeSpan.FromMilliseconds(ms)); return Task.CompletedTask; }
var quickSender = new StagedSender(new FakeTransport(destination) { Receive = quickReceiver.Accept }, QuickDelay, quickClock);
await quickSender.SendAsync("A橋\r\n日本。", destination, allowLineBreaks: true, options: quickOptions);
Check(quickReceiver.Output == "A橋\r\n日本。" && quickSender.ProgressInfo.ActiveElapsed == TimeSpan.FromSeconds(4.85),
    "quick Japanese uses unchanged conversion and newline semantics through the sender");

foreach (var (speed, milliseconds, conversionWait) in new[] { (JapaneseSpeed.XiaoSlow, 100, 15),
    (JapaneseSpeed.XiaoStandard, 50, 15), (JapaneseSpeed.XiaoFastest, 25, 6) })
{
    const string text = "橋・箸・端 ABC！\r\n\r\n橋\r\n";
    var snapshot = TextSnapshot.Create(text, allowLineBreaks: true);
    foreach (var unit in snapshot.Units.Where(unit => !unit.IsLineBreak))
    {
        var baseline = ImeKeyEncoder.EncodeCharacter(unit.Character);
        var steps = ImeKeyEncoder.EncodeCharacter(unit.Character, speed);
        Check(steps.Sum(step => step.DelayMilliseconds) == milliseconds
            && steps.Select(step => step.Stroke).SequenceEqual(baseline.Select(step => step.Stroke)),
            $"{speed} has the advertised fixed wait and keeps the same IME key sequence");
        Check(steps.All(step => step.DelayMilliseconds > 0) && steps[13].DelayMilliseconds == conversionWait,
            $"{speed} keeps a positive wait after each report and the selected F5 conversion wait");
    }
    var receiver = new Receiver(InputMode.Off) { IgnoreLang1 = true, DriftOnCommit = true, LineEnding = "\r\n" };
    var trialClock = new ManualClock();
    var trialSender = new StagedSender(new FakeTransport(destination) { Receive = receiver.Accept },
        (ms, _) => { trialClock.Advance(TimeSpan.FromMilliseconds(ms)); return Task.CompletedTask; }, trialClock);
    await trialSender.SendSnapshotAsync(snapshot, destination, new SendOptions(Speed: speed));
    var lines = snapshot.Units.Count(unit => unit.IsLineBreak);
    Check(trialSender.State == SendState.KeysSent && receiver.Output == text
        && trialSender.ProgressInfo.ActiveElapsed == TimeSpan.FromMilliseconds((snapshot.UnitCount - lines) * milliseconds + lines * 350),
        $"{speed} preserves exact text in the receiver model and records elapsed time including Enter for each newline");
}

var jis = ReceiverKeyboardLayout.JapaneseJis;
var us = ReceiverKeyboardLayout.EnglishUs;
foreach (var expected in new (char Character, ReceiverKeyboardLayout Layout, byte Modifier, byte Usage)[]
{
    ('@', jis, 0, 0x2F), (':', jis, 0, 0x34), ('_', jis, 2, 0x87), ('\\', jis, 0, 0x87),
    ('|', jis, 2, 0x89), ('"', jis, 2, 0x1F), ('\'', jis, 2, 0x24), ('=', jis, 2, 0x2D),
    ('~', jis, 2, 0x2E), (']', jis, 0, 0x32), ('*', jis, 2, 0x34),
    ('@', us, 2, 0x1F), (':', us, 2, 0x33), ('_', us, 2, 0x2D), ('\\', us, 0, 0x31),
    ('"', us, 2, 0x34), ('\'', us, 0, 0x34), ('=', us, 0, 0x2E), ('~', us, 2, 0x35)
})
    Check(AsciiKeyEncoder.ToStroke(expected.Character, expected.Layout) == new KeyStroke(expected.Modifier, expected.Usage),
        "URL punctuation uses an independent expected JIS/US physical key");
var printableAscii = new string(Enumerable.Range(32, 95).Select(n => (char)n).ToArray());
const string url = "https://example.com/Aa_01/path?q=a%20b&x=1+2#section-3";
foreach (var layout in Enum.GetValues<ReceiverKeyboardLayout>())
{
    foreach (var c in printableAscii)
    {
        var encoded = AsciiKeyEncoder.EncodeCharacter(c, layout);
        Check(encoded.Count == 2 && encoded[0].Stroke.Modifier is 0 or 2 && encoded[1].Stroke == default
            && encoded.Sum(s => s.DelayMilliseconds) == 80, "every printable ASCII character has one direct press/release with Shift at most");
    }
    foreach (var initiallyOn in new[] { true, false })
    {
        var receiver = new AsciiReceiver(layout) { ImeOn = initiallyOn };
        var transport = new FakeTransport(destination) { Receive = receiver.Accept };
        var queue = new StagedSender(transport, NoDelay);
        var text = printableAscii + "aaAA0011://" + url;
        await queue.SendAsync(text, destination, options: new(Mode: TextInputMode.Ascii, Layout: layout));
        Check(receiver.Output == text && receiver.Composition == "" && queue.ProcessedCharacters == text.Length,
            "independent physical-key receiver reproduces all ASCII including repeated keys and URL case");
        Check(!transport.HasKey(0x3E) && !transport.HasKey(0x28) && !transport.HasKey(0x90)
            && !transport.HasKey(0x39) && transport.Frames.Count(f => f[2] == 0x91) == 1,
            "ASCII one-line send has a single ImeOff setup and no F5, commit Enter, IME toggle or extra URL submission");
        Check(transport.Targets.All(t => t == destination) && queue.State == SendState.KeysSent,
            "fast ASCII retains destination-generation binding and normal completion");
    }
    var multilineReceiver = new AsciiReceiver(layout);
    var multilineQueue = new StagedSender(new FakeTransport(destination) { Receive = multilineReceiver.Accept }, NoDelay);
    await multilineQueue.SendAsync("A\r\n\r\nb\n", destination, allowLineBreaks: true, options: new(Mode: TextInputMode.Ascii, Layout: layout));
    Check(multilineReceiver.Output == "A\r\n\r\nb\r\n" && multilineQueue.TotalCharacters == 5,
        "ASCII multiline retains CRLF pairing, blank lines and trailing newline only when explicitly permitted");
}

var asciiOptions = new SendOptions(Mode: TextInputMode.Ascii);
foreach (var bad in new[] { "abc日本", url + "　", "Aé", "Aｱ", "A\tB", "A\nB", "A\u007F", "A\u001B" })
{
    var t = new FakeTransport(destination);
    await RejectAsync(() => new StagedSender(t, NoDelay).SendAsync(bad, destination, options: asciiOptions),
        "ASCII mode rejects non-ASCII/control or unconfirmed-newline input before transmission");
    Check(t.Frames.Count == 0, "ASCII validation failure sends neither ImeOff nor a partial valid prefix");
}
foreach (var badOption in new[] { new SendOptions(Mode: (TextInputMode)99), new SendOptions(Speed: (JapaneseSpeed)99), new SendOptions(Layout: (ReceiverKeyboardLayout)99) })
{
    var t = new FakeTransport(destination);
    var queue = new StagedSender(t, NoDelay);
    await RejectAsync(() => queue.SendAsync("A", destination, options: badOption), "unknown mode, speed or layout is rejected before starting");
    await RejectAsync(() => queue.PrepareImeAsync(destination, badOption), "unknown options cannot send preparation keys");
    Check(t.Frames.Count == 0, "unknown options cause no receiver action");
}
await RejectAsync(() => { ImeKeyEncoder.EncodeCharacter('A', (JapaneseSpeed)99); return Task.CompletedTask; }, "encoder rejects unknown Japanese speed");
await RejectAsync(() => { AsciiKeyEncoder.ToStroke('A', (ReceiverKeyboardLayout)99); return Task.CompletedTask; }, "encoder rejects unknown receiver layout");

var asciiPrepareReceiver = new AsciiReceiver(jis);
var asciiPrepareTransport = new FakeTransport(destination) { Receive = asciiPrepareReceiver.Accept };
var asciiPrepare = new StagedSender(asciiPrepareTransport, NoDelay);
await asciiPrepare.PrepareImeAsync(destination, asciiOptions);
Check(asciiPrepare.State == SendState.Ready && !asciiPrepareReceiver.ImeOn && asciiPrepareReceiver.Output == ""
    && !asciiPrepareTransport.HasKey(0x28) && !asciiPrepareTransport.HasKey(0x3E), "ASCII optional preparation selects ImeOff without typing or Enter");

var asciiPauseReceiver = new AsciiReceiver(jis);
var asciiPauseTransport = new FakeTransport(destination) { Receive = asciiPauseReceiver.Accept };
var asciiPauseSender = new StagedSender(asciiPauseTransport, NoDelay);
var capturedOptions = asciiOptions;
asciiPauseTransport.BeforeWrite = (_, _) =>
{
    if (asciiPauseTransport.Frames.Count == 3) asciiPauseSender.RequestPause();
    return Task.CompletedTask;
};
var asciiPauseRun = asciiPauseSender.SendAsync("A@:_b", destination, options: capturedOptions);
Check(asciiPauseSender.State == SendState.Paused && asciiPauseReceiver.Output == "A"
    && asciiPauseTransport.Frames.Last().All(b => b == 0), "pause during a direct key-down completes its release before waiting");
capturedOptions = capturedOptions with { Mode = TextInputMode.Japanese, Layout = us };
asciiPauseReceiver.ImeOn = true;
await RejectAsync(() => asciiPauseSender.SendAsync("別", destination, options: capturedOptions), "mode changes cannot replace a paused ASCII worker");
asciiPauseSender.Resume();
await asciiPauseRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(asciiPauseReceiver.Output == "A@:_b" && asciiPauseTransport.Frames.Count(f => f[2] == 0x91) == 2,
    "resume reselects ImeOff and retains original mode/layout without repeating the typed prefix");
Check(!asciiPauseTransport.HasKey(0x28) && !asciiPauseTransport.HasKey(0x3E), "ASCII pause/resume introduces neither conversion nor submission");

foreach (var cancelAt in new[] { 1, 2, 3, 4 })
{
    var receiver = new AsciiReceiver(jis);
    var t = new FakeTransport(destination) { Receive = receiver.Accept };
    StagedSender? queue = null;
    Task CancelAscii(int _, CancellationToken token)
    {
        if (t.Frames.Count == cancelAt) queue!.Cancel();
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    queue = new StagedSender(t, CancelAscii);
    await queue.SendAsync("Ab", destination, options: asciiOptions);
    Check(queue.State == SendState.StoppedPartial && queue.ReleaseReportSent == true
        && receiver.Output == (cancelAt < 3 ? "" : "A") && !t.HasKey(0x28),
        "cancel at ImeOff or a direct key stops the ASCII tail and attempts release without repair");
}
foreach (var changeAt in new[] { 2, 3, 4 })
{
    var receiver = new AsciiReceiver(jis);
    var t = new FakeTransport(destination) { Receive = receiver.Accept };
    t.BeforeWrite = (_, _) => { if (t.Frames.Count == changeAt) t.Current = destination with { Generation = 6 }; return Task.CompletedTask; };
    var queue = new StagedSender(t, NoDelay);
    await RejectAsync(() => queue.SendAsync("Ab", destination, options: asciiOptions), "ASCII generation change stops transmission at the next report");
    Check(queue.State == SendState.Faulted && queue.ReleaseReportSent == false
        && receiver.Output == (changeAt < 3 ? "" : "A"), "ASCII disconnect never sends the queued tail to a new connection");
}

var asciiClock = new ManualClock();
Task AsciiDelay(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); asciiClock.Advance(TimeSpan.FromMilliseconds(ms)); return Task.CompletedTask; }
var asciiTimed = new StagedSender(new FakeTransport(destination), AsciiDelay, asciiClock);
await asciiTimed.SendAsync(url, destination, options: asciiOptions);
Check(asciiTimed.ProgressInfo.ActiveElapsed == TimeSpan.FromMilliseconds(300 + 80 * url.Length),
    "ASCII configured wait is 300ms setup plus 80ms per character, measured independently of real hardware speed");
var ignoredOffReceiver = new AsciiReceiver(jis) { IgnoreImeOff = true };
var ignoredOffSender = new StagedSender(new FakeTransport(destination) { Receive = ignoredOffReceiver.Accept }, NoDelay);
await ignoredOffSender.SendAsync("abc", destination, options: asciiOptions);
Check(ignoredOffSender.State == SendState.KeysSent && ignoredOffReceiver.Output != "abc",
    "ignored ImeOff demonstrates transport completion cannot guarantee correct receiver IME state");

foreach (var layout in Enum.GetValues<ReceiverKeyboardLayout>())
{
    var receiver = new AsciiReceiver(layout);
    var longAscii = string.Concat(Enumerable.Repeat("Aa_@01:/?-", 500));
    var queue = new StagedSender(new FakeTransport(destination) { Receive = receiver.Accept }, NoDelay);
    await queue.SendAsync(longAscii, destination, options: new(Mode: TextInputMode.Ascii, Layout: layout));
    Check(longAscii.Length == 5000 && receiver.Output == longAscii && queue.ProcessedCharacters == 5000,
        "5000-character ASCII snapshot preserves every direct key in either receiver layout");
}
var wrongLayoutReceiver = new AsciiReceiver(jis);
var wrongLayoutQueue = new StagedSender(new FakeTransport(destination) { Receive = wrongLayoutReceiver.Accept }, NoDelay);
await wrongLayoutQueue.SendAsync("@:_", destination, options: new(Mode: TextInputMode.Ascii, Layout: us));
Check(wrongLayoutQueue.State == SendState.KeysSent && wrongLayoutReceiver.Output != "@:_",
    "layout mismatch is observable in the receiver model and cannot be detected by transport success");

// Selection state is shared with the UI, but a running send owns its captured value.
var settings = new InputSettings();
Check(settings.Capture() == new SendOptions() && settings.GetSpeedIndex() == 0,
    "separate selectors initially retain standard Japanese and the prior defaults");
settings.SetSpeed(1);
var japaneseSelection = settings.Capture();
settings.SetMode(TextInputMode.Ascii);
Check(settings.GetSpeedIndex() == 1 && settings.Capture().DirectSpeed == AsciiSpeed.Fast,
    "first switch to ASCII selects the previously working fast timing");
settings.SetSpeed(0);
settings.SetLayout(us);
var relaxedSelection = settings.Capture();
Check(relaxedSelection.Mode == TextInputMode.Ascii && relaxedSelection.DirectSpeed == AsciiSpeed.Relaxed
    && relaxedSelection.Layout == us, "ASCII speed and receiver layout are independent draft selections");
settings.SetMode(TextInputMode.Japanese);
Check(settings.GetSpeedIndex() == 1 && settings.Capture().Speed == JapaneseSpeed.SlightlyFaster,
    "switching input content back restores that mode's Japanese speed");
settings.SetSpeed(0);
settings.SetMode(TextInputMode.Ascii);
Check(settings.GetSpeedIndex() == 0 && settings.Capture().DirectSpeed == AsciiSpeed.Relaxed
    && settings.Capture().Layout == us, "ASCII speed and layout survive a round trip through Japanese settings");
Check(japaneseSelection.Mode == TextInputMode.Japanese && japaneseSelection.Speed == JapaneseSpeed.SlightlyFaster
    && relaxedSelection.DirectSpeed == AsciiSpeed.Relaxed, "earlier captures are immutable after later selector changes");
settings.SetMode(TextInputMode.Japanese); settings.SetSpeed(2);
settings.SetMode(TextInputMode.Ascii); settings.SetSpeed(2);
Check(settings.Capture() is { Speed: JapaneseSpeed.Faster, DirectSpeed: AsciiSpeed.Fastest } && settings.GetSpeedIndex() == 2
    && InputSettings.SpeedLabels(TextInputMode.Japanese).Count == 3 && InputSettings.SpeedLabels(TextInputMode.Ascii).Count == 3,
    "the third selector entry chooses the new fastest timing for each input method");
var beforeInvalidSelection = settings.Capture();
foreach (var badIndex in new[] { -1, 3 })
{
    await RejectAsync(() => { settings.SetSpeed(badIndex); return Task.CompletedTask; }, "unsupported selector index is rejected");
    Check(settings.Capture() == beforeInvalidSelection, "invalid speed selection leaves the draft settings intact");
}
await RejectAsync(() => { settings.SetMode((TextInputMode)99); return Task.CompletedTask; }, "selection model rejects unknown input content");

var xiaoSettings = new InputSettings();
Check(xiaoSettings.GetSpeedIndex(useXiao: true) == 1
    && xiaoSettings.Capture(useXiao: true) is { Speed: JapaneseSpeed.XiaoStandard, DirectSpeed: AsciiSpeed.XiaoStandard },
    "both XIAO modes start with the standard preset");
foreach (var (index, japanese, ascii) in new[] { (0, JapaneseSpeed.XiaoSlow, AsciiSpeed.Fastest),
    (1, JapaneseSpeed.XiaoStandard, AsciiSpeed.XiaoStandard), (2, JapaneseSpeed.XiaoFastest, AsciiSpeed.XiaoFastest) })
{
    xiaoSettings.SetMode(TextInputMode.Japanese);
    xiaoSettings.SetSpeed(index, useXiao: true);
    xiaoSettings.SetMode(TextInputMode.Ascii);
    xiaoSettings.SetSpeed(index, useXiao: true);
    var selected = xiaoSettings.Capture(useXiao: true);
    Check(selected.Speed == japanese && selected.DirectSpeed == ascii, "each XIAO preset selects its timing in both modes");
}
await RejectAsync(() => { xiaoSettings.SetSpeed(3, useXiao: true); return Task.CompletedTask; },
    "the XIAO selector has exactly three presets");
Check(xiaoSettings.Capture() is { Speed: JapaneseSpeed.Standard, DirectSpeed: AsciiSpeed.Fast }
    && xiaoSettings.Capture(useXiao: true) is { Speed: JapaneseSpeed.XiaoFastest, DirectSpeed: AsciiSpeed.XiaoFastest },
    "changing XIAO speeds leaves the local Bluetooth settings independent");
await RejectAsync(() => { settings.SetLayout((ReceiverKeyboardLayout)99); return Task.CompletedTask; }, "selection model rejects unknown keyboard layout");
Check(settings.Capture() == beforeInvalidSelection, "invalid content or layout cannot overwrite the valid selection");

foreach (var layout in Enum.GetValues<ReceiverKeyboardLayout>())
    foreach (var speed in Enum.GetValues<AsciiSpeed>())
    {
        var receiver = new AsciiReceiver(layout);
        var transport = new FakeTransport(destination) { Receive = receiver.Accept };
        var sendClock = new ManualClock();
        Task TimedAscii(int ms, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); sendClock.Advance(TimeSpan.FromMilliseconds(ms)); return Task.CompletedTask;
        }
        var queue = new StagedSender(transport, TimedAscii, sendClock);
        var body = printableAscii + "\r\n" + url;
        await queue.SendAsync(body, destination, allowLineBreaks: true,
            options: new(Mode: TextInputMode.Ascii, Layout: layout, DirectSpeed: speed));
        var perCharacter = speed switch
        {
            AsciiSpeed.Relaxed => 120, AsciiSpeed.Fast => 80, AsciiSpeed.Fastest => 50,
            AsciiSpeed.XiaoStandard => 10, AsciiSpeed.XiaoFastest => 5,
            _ => throw new InvalidOperationException()
        };
        Check(receiver.Output == body && queue.State == SendState.KeysSent,
            "every ASCII speed preserves all printable characters, URL and CRLF in either layout");
        Check(queue.ProgressInfo.ActiveElapsed == TimeSpan.FromMilliseconds(300 + perCharacter * (body.Length - 2) + 350),
            "selected ASCII speed changes the worker's actual scheduled waits, not just its label");
    }
foreach (var c in "A@_b0")
{
    var fast = AsciiKeyEncoder.EncodeCharacter(c, jis, AsciiSpeed.Fast);
    var relaxed = AsciiKeyEncoder.EncodeCharacter(c, jis, AsciiSpeed.Relaxed);
    var fastest = AsciiKeyEncoder.EncodeCharacter(c, jis, AsciiSpeed.Fastest);
    Check(fast.Select(s => s.Stroke).SequenceEqual(relaxed.Select(s => s.Stroke))
        && fast.Select(s => s.Stroke).SequenceEqual(fastest.Select(s => s.Stroke))
        && fast.Sum(s => s.DelayMilliseconds) == 80 && relaxed.Sum(s => s.DelayMilliseconds) == 120
        && fastest.Sum(s => s.DelayMilliseconds) == 50,
        "relaxed and fastest ASCII change only timing and preserve the established direct-key mapping");
}
var badDirectOptions = new SendOptions(DirectSpeed: (AsciiSpeed)99);
var badDirectTransport = new FakeTransport(destination);
var badDirectSender = new StagedSender(badDirectTransport, NoDelay);
await RejectAsync(() => badDirectSender.SendAsync("A", destination, options: badDirectOptions), "unknown ASCII speed is rejected before starting a send");
await RejectAsync(() => badDirectSender.PrepareImeAsync(destination, badDirectOptions), "unknown ASCII speed cannot send even preparation keys");
Check(badDirectTransport.Frames.Count == 0, "bad ASCII timing options never reach the receiver");
await RejectAsync(() => { AsciiKeyEncoder.EncodeCharacter('A', jis, (AsciiSpeed)99); return Task.CompletedTask; },
    "ASCII encoder rejects unsupported speed values");

var selectedClock = new ManualClock();
Task SelectedDelay(int ms, CancellationToken token)
{
    token.ThrowIfCancellationRequested(); selectedClock.Advance(TimeSpan.FromMilliseconds(ms)); return Task.CompletedTask;
}
var selectedReceiver = new AsciiReceiver(jis);
var selectedSender = new StagedSender(new FakeTransport(destination) { Receive = selectedReceiver.Accept }, SelectedDelay, selectedClock);
var draftSettings = new InputSettings();
draftSettings.SetMode(TextInputMode.Ascii); draftSettings.SetSpeed(0);
selectedSender.Progress += (done, _) => { if (done == 1) selectedSender.RequestPause(); };
var selectedRun = selectedSender.SendAsync("A@_B", destination, options: draftSettings.Capture());
Check(selectedSender.State == SendState.Paused && selectedReceiver.Output == "A",
    "relaxed ASCII pauses at the same complete-character boundary");
draftSettings.SetMode(TextInputMode.Japanese); draftSettings.SetSpeed(1); draftSettings.SetLayout(us);
selectedSender.Resume();
await selectedRun.WaitAsync(TimeSpan.FromSeconds(3));
Check(selectedReceiver.Output == "A@_B" && selectedSender.ProgressInfo.ActiveElapsed == TimeSpan.FromMilliseconds(1080),
    "later draft content, speed and layout edits cannot change a captured paused transfer");

// Reopening the app restores connection preferences, never a text queue.
const string pairedPeer = "synthetic-paired-pc-a";
const string otherPeer = "synthetic-paired-pc-b";
var rememberedPreferences = new ConnectionPreferences(PeerHash: ConnectionPreferences.HashPeer(pairedPeer));
Check(new ConnectionPreferences().ShouldAutoStart && new ConnectionPreferences().FindRememberedPeer([pairedPeer]) is null,
    "first launch starts connectable waiting but cannot guess an unremembered sending target");
Check(rememberedPreferences.FindRememberedPeer([otherPeer, pairedPeer]) == pairedPeer,
    "automatic target restore matches only the remembered PC among connected paired subscribers");
Check(rememberedPreferences.FindRememberedPeer([otherPeer]) is null
    && rememberedPreferences.FindRememberedPeer([]) is null, "a different or absent PC is never selected as a fallback");
Check((rememberedPreferences with { AutoReconnect = false }).FindRememberedPeer([pairedPeer]) is null
    && !(rememberedPreferences with { AutoReconnect = false }).ShouldAutoStart,
    "disabling startup reconnection disables both automatic publication and target selection");
Check(ConnectionPreferences.HashPeer(pairedPeer) == ConnectionPreferences.HashPeer(pairedPeer)
    && ConnectionPreferences.HashPeer(pairedPeer) != ConnectionPreferences.HashPeer(otherPeer), "remembered fingerprint is stable and distinguishes peer identities");
foreach (var bad in new[] { new ConnectionPreferences(Version: 2), new ConnectionPreferences(PeerHash: ""), new ConnectionPreferences(PeerHash: new string('Z', 64)), new ConnectionPreferences(PeerHash: new string('A', 65)) })
{
    try { bad.Validate(); Check(false, "invalid connection preferences must fail validation"); }
    catch (InvalidDataException) { Check(true, "invalid connection preference schema or fingerprint is rejected"); }
}
// Input method, speeds and layout reopen as last chosen; the text is never saved.
var inputRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-input-tests", Guid.NewGuid().ToString("N")));
var inputPath = Path.Combine(inputRoot, "input-preferences.json");
var inputStore = new InputPreferencesStore(inputPath);
Check(inputStore.Load() == new InputPreferences() && !Directory.Exists(inputRoot), "missing input preferences use the prior defaults");
Directory.CreateDirectory(inputRoot);
File.WriteAllText(inputPath, """{"Version":1,"Mode":"Ascii","JapaneseSpeed":"Faster","AsciiSpeed":"Relaxed","Layout":"EnglishUs"}""");
Check(inputStore.Load() == new InputPreferences(Mode: TextInputMode.Ascii, JapaneseSpeed: JapaneseSpeed.Faster,
    AsciiSpeed: AsciiSpeed.Relaxed, Layout: us), "a legacy file preserves its settings and defaults new XIAO presets to standard");
var closedSettings = new InputSettings();
closedSettings.SetSpeed(2);
closedSettings.SetSpeed(2, useXiao: true);
closedSettings.SetMode(TextInputMode.Ascii);
closedSettings.SetSpeed(0);
closedSettings.SetSpeed(0, useXiao: true);
closedSettings.SetLayout(us);
inputStore.Save(closedSettings.ToPreferences());
var reopenedSettings = new InputSettings();
reopenedSettings.Restore(new InputPreferencesStore(inputPath).Load());
Check(reopenedSettings.Capture() == closedSettings.Capture() && reopenedSettings.Mode == TextInputMode.Ascii
    && reopenedSettings.Capture(useXiao: true) == closedSettings.Capture(useXiao: true),
    "both transports' speeds, input method and layout reopen exactly as they were closed");
var inputJson = File.ReadAllText(inputPath);
using (var json = System.Text.Json.JsonDocument.Parse(inputJson))
    Check(json.RootElement.EnumerateObject().Select(property => property.Name).Order()
        .SequenceEqual(new[] { "AsciiSpeed", "JapaneseSpeed", "Layout", "Mode", "Version", "XiaoAsciiPreset", "XiaoJapanesePreset" }),
        "input preference file holds only the schema and selector values, no text");
Check(!Directory.EnumerateFiles(inputRoot, "*.tmp").Any(), "input preference save leaves no pending temporary file");
var goodInputBytes = File.ReadAllBytes(inputPath);
try { inputStore.Save(new InputPreferences(JapaneseSpeed: (JapaneseSpeed)99)); Check(false, "invalid input save must be rejected"); }
catch (InvalidDataException) { Check(true, "invalid input preference save is rejected before filesystem mutation"); }
Check(File.ReadAllBytes(inputPath).SequenceEqual(goodInputBytes), "rejected input save preserves the last valid file");
foreach (var malformed in new[] { "null", "{", "{\"Version\":2}", "{\"Mode\":\"Kana\"}", "{\"AsciiSpeed\":9}", "{\"AsciiSpeed\":\"XiaoFastest\"}",
    "{\"JapaneseSpeed\":\"Trial10\"}", "{\"XiaoAsciiPreset\":9}", new string(' ', 4097) })
{
    File.WriteAllText(inputPath, malformed);
    try { _ = inputStore.Load(); Check(false, "malformed input preferences must be rejected"); }
    catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
    { Check(true, "corrupted, unsupported or oversized input preferences fall back instead of applying"); }
}
var untouchedSettings = new InputSettings();
try { untouchedSettings.Restore(new InputPreferences(Layout: (ReceiverKeyboardLayout)9)); }
catch (InvalidDataException) { }
Check(untouchedSettings.Capture() == new SendOptions(), "an invalid saved value leaves every selector at its default");
Directory.Delete(inputRoot, recursive: true);

var preferencesRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "_internal", "build-cache", "test-data", "jis-ble-connection-tests", Guid.NewGuid().ToString("N")));
var preferencesPath = Path.Combine(preferencesRoot, "connection-preferences.json");
var preferenceStore = new ConnectionPreferencesStore(preferencesPath);
Check(preferenceStore.Load() == new ConnectionPreferences() && !Directory.Exists(preferencesRoot),
    "missing preferences use defaults without creating or reading any unrelated files");
preferenceStore.Save(rememberedPreferences);
var reopenedPreferences = new ConnectionPreferencesStore(preferencesPath).Load();
Check(reopenedPreferences == rememberedPreferences && reopenedPreferences.FindRememberedPeer([pairedPeer]) == pairedPeer,
    "saved connection choice survives a new store instance and restores the same eligible peer");
var savedJson = File.ReadAllText(preferencesPath);
using (var json = System.Text.Json.JsonDocument.Parse(savedJson))
    Check(json.RootElement.EnumerateObject().Count() == 3 && !savedJson.Contains(pairedPeer, StringComparison.Ordinal)
        && !savedJson.Contains(otherPeer, StringComparison.Ordinal), "preference file stores only schema, startup flag and hash, not raw peer IDs or names");
Check(!Directory.EnumerateFiles(preferencesRoot, "*.tmp").Any(), "successful atomic preference save leaves no pending temporary file");
preferenceStore.Save(reopenedPreferences with { AutoReconnect = false });
Check(!new ConnectionPreferencesStore(preferencesPath).Load().ShouldAutoStart,
    "startup opt-out persists across another reopen");
preferenceStore.Save(rememberedPreferences with { PeerHash = ConnectionPreferences.HashPeer(otherPeer) });
Check(new ConnectionPreferencesStore(preferencesPath).Load().FindRememberedPeer([pairedPeer, otherPeer]) == otherPeer,
    "explicitly choosing a different PC replaces the remembered target");
var goodPreferenceBytes = File.ReadAllBytes(preferencesPath);
try { preferenceStore.Save(new ConnectionPreferences(Version: 99)); Check(false, "invalid save must be rejected"); }
catch (InvalidDataException) { Check(true, "invalid preference save is rejected before filesystem mutation"); }
Check(File.ReadAllBytes(preferencesPath).SequenceEqual(goodPreferenceBytes), "rejected preference save preserves the last valid file");
foreach (var malformed in new[] { "null", "{", "{\"Version\":2}", "{\"PeerHash\":\"bad\"}", "{\"AutoReconnect\":\"yes\"}", new string(' ', 4097) })
{
    File.WriteAllText(preferencesPath, malformed);
    try { _ = preferenceStore.Load(); Check(false, "malformed preferences must be rejected"); }
    catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
    { Check(true, "corrupted, unsupported or oversized preferences cannot silently select a PC"); }
}
preferenceStore.Save(rememberedPreferences);
var reopenTransport = new FakeTransport(destination);
var reopenSender = new StagedSender(reopenTransport, NoDelay);
_ = preferenceStore.Load().FindRememberedPeer([pairedPeer]);
Check(reopenSender.State == SendState.Idle && reopenTransport.Frames.Count == 0,
    "loading and resolving the remembered connection does not enqueue text, IME keys or a stale continuation");

// Exercise the same advertisement coordinator used by the Windows transport.
foreach (var state in new[] { AdvertisementState.Started, AdvertisementState.StartedWithoutAllAdvertisementData })
    foreach (var eventDelivered in new[] { true, false })
    {
        var publisher = new FakeAdvertisementPublisher { DeliverEvents = eventDelivered, NextStart = new(state) };
        var result = await AdvertisementStartup.StartAsync(publisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
        Check(result.State == state && result.IsRunning && publisher.StartCount == 1,
            "full or reduced advertising start completes from either event or current property");
        Check(publisher.ListenerCount == 0 && publisher.StopCount == 0,
            "successful advertising start detaches its attempt listener without stopping the publisher");
    }
var abortedPublisher = new FakeAdvertisementPublisher { NextStart = new(AdvertisementState.Aborted, 7) };
var missingCallbackPublisher = new FakeAdvertisementPublisher { DeliverEvents = false, NextStart = new(AdvertisementState.Created) };
var missingCallbackStart = AdvertisementStartup.StartAsync(missingCallbackPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
missingCallbackPublisher.Publish(new(AdvertisementState.Started));
Check((await missingCallbackStart.WaitAsync(TimeSpan.FromSeconds(3))).IsRunning && missingCallbackPublisher.ListenerCount == 0,
    "polling current state recovers an asynchronous start even when the native event never arrives");
Check(missingCallbackPublisher.Current.ErrorCode is null, "missing native error information is not fabricated as BluetoothError.Success");
try
{
    await AdvertisementStartup.StartAsync(abortedPublisher, true, TimeSpan.FromMilliseconds(300), CancellationToken.None);
    Check(false, "advertising that stays aborted must fail");
}
catch (AdvertisementStartException error)
{
    Check(error.LastReport == new AdvertisementReport(AdvertisementState.Aborted, 7) && error.Phase == "開始"
        && error.Message.Contains("中断", StringComparison.Ordinal) && error.InnerException is TimeoutException,
        "advertising that stays aborted reports the native status and error after the start wait");
}
Check(abortedPublisher.StopCount == 1 && abortedPublisher.ListenerCount == 0,
    "failed advertisement is stopped and no old attempt observer remains");
abortedPublisher.NextStart = new(AdvertisementState.Started);
await AdvertisementStartup.StartAsync(abortedPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(abortedPublisher.StartCount == 2 && abortedPublisher.Current.IsRunning,
    "retry succeeds on the same already-built publisher after an aborted attempt");

// Windows 11 on the Surface sender reports Aborted and then Started for a healthy start.
var transientAbortPublisher = new FakeAdvertisementPublisher { NextStart = new(AdvertisementState.Aborted) };
var transientAbortStart = AdvertisementStartup.StartAsync(transientAbortPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(!transientAbortStart.IsCompleted && transientAbortPublisher.ListenerCount == 1,
    "a transient Aborted status keeps waiting for the real start result");
transientAbortPublisher.Publish(new(AdvertisementState.Started));
Check((await transientAbortStart.WaitAsync(TimeSpan.FromSeconds(3))).IsRunning
    && transientAbortPublisher.StopCount == 0 && transientAbortPublisher.ListenerCount == 0,
    "Aborted followed by Started succeeds without stopping the working advertisement");

var noEventPublisher = new FakeAdvertisementPublisher { NextStart = new(AdvertisementState.Created) };
try
{
    await AdvertisementStartup.StartAsync(noEventPublisher, true, TimeSpan.Zero, CancellationToken.None);
    Check(false, "missing status must time out");
}
catch (AdvertisementStartException error)
{
    Check(error.Phase == "開始" && error.LastReport.State == AdvertisementState.Created
        && error.InnerException is TimeoutException, "timeout identifies advertisement phase and last state");
}
Check(noEventPublisher.StopCount == 1 && noEventPublisher.ListenerCount == 0,
    "timeout releases attempt listeners and stops the pending advertisement");
noEventPublisher.NextStart = new(AdvertisementState.Started);
await AdvertisementStartup.StartAsync(noEventPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(noEventPublisher.StartCount == 2 && noEventPublisher.Current.IsRunning, "timed-out startup can be retried without recreating the service");

var latePublisher = new FakeAdvertisementPublisher { NextStart = new(AdvertisementState.Created) };
var lateStart = AdvertisementStartup.StartAsync(latePublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(!lateStart.IsCompleted && latePublisher.ListenerCount == 1, "advertisement start waits while the current native state is pending");
latePublisher.Publish(new(AdvertisementState.StartedWithoutAllAdvertisementData));
Check((await lateStart).State == AdvertisementState.StartedWithoutAllAdvertisementData && latePublisher.ListenerCount == 0,
    "an asynchronous reduced-data start resolves the real startup attempt");
latePublisher.Publish(new(AdvertisementState.Aborted, 9));
Check(lateStart.IsCompletedSuccessfully && latePublisher.ListenerCount == 0,
    "a completed attempt is not mutated by later native status events");

using (var cancelledStartup = new CancellationTokenSource())
{
    var publisher = new FakeAdvertisementPublisher { NextStart = new(AdvertisementState.Created) };
    var pendingStart = AdvertisementStartup.StartAsync(publisher, true, TimeSpan.FromSeconds(1), cancelledStartup.Token);
    cancelledStartup.Cancel();
    try { await pendingStart; Check(false, "startup cancellation must interrupt waiting"); }
    catch (OperationCanceledException) { Check(true, "closing cancellation interrupts advertisement waiting"); }
    Check(publisher.ListenerCount == 0 && publisher.StopCount == 1 && !publisher.Current.IsRunning,
        "cancelled startup is stopped and cannot leave an old attempt listener alive");
}
using (var preCancelled = new CancellationTokenSource())
{
    preCancelled.Cancel();
    var publisher = new FakeAdvertisementPublisher();
    try { await AdvertisementStartup.StartAsync(publisher, true, TimeSpan.FromSeconds(1), preCancelled.Token); Check(false, "pre-cancelled startup must fail"); }
    catch (OperationCanceledException) { Check(publisher.StartCount == 0 && publisher.ListenerCount == 0, "pre-cancelled startup creates no advertising request"); }
}

var activePublisher = new FakeAdvertisementPublisher();
await AdvertisementStartup.StartAsync(activePublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
await AdvertisementStartup.StartAsync(activePublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(activePublisher.StartCount == 1 && activePublisher.StopCount == 0, "already-active matching advertisement is reused without a duplicate start");
await AdvertisementStartup.StartAsync(activePublisher, false, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(activePublisher.StartCount == 2 && activePublisher.StopCount == 1 && activePublisher.Discoverable == false,
    "discovery parameter changes wait for stop before requesting a new advertisement");

var queuedStopPublisher = new FakeAdvertisementPublisher { CompleteStop = false };
await AdvertisementStartup.StartAsync(queuedStopPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
var changeDiscovery = AdvertisementStartup.StartAsync(queuedStopPublisher, false, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(!changeDiscovery.IsCompleted && queuedStopPublisher.StartCount == 1, "new start cannot overtake an asynchronous stop");
queuedStopPublisher.Publish(new(AdvertisementState.Stopped));
await changeDiscovery;
Check(queuedStopPublisher.StartCount == 2 && queuedStopPublisher.Discoverable == false, "status-confirmed stop allows the next start in order");

var stuckStopPublisher = new FakeAdvertisementPublisher { CompleteStop = false };
await AdvertisementStartup.StartAsync(stuckStopPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
try { await AdvertisementStartup.StartAsync(stuckStopPublisher, false, TimeSpan.Zero, CancellationToken.None); Check(false, "stuck stop must fail"); }
catch (AdvertisementStartException error)
{
    Check(error.Phase == "停止" && stuckStopPublisher.StartCount == 1,
        "stop timeout is distinguished from start timeout and does not issue an overlapping start");
}
Check(stuckStopPublisher.ListenerCount == 0, "failed stop leaves no stale observer");

var throwingPublisher = new FakeAdvertisementPublisher { ThrowOnStart = true };
await RejectAsync(() => AdvertisementStartup.StartAsync(throwingPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None),
    "synchronous native start exception is propagated");
Check(throwingPublisher.ListenerCount == 0 && throwingPublisher.StopCount == 1,
    "synchronous start failure also detaches listeners and permits later recovery");
throwingPublisher.ThrowOnStart = false;
await AdvertisementStartup.StartAsync(throwingPublisher, true, TimeSpan.FromSeconds(1), CancellationToken.None);
Check(throwingPublisher.Current.IsRunning, "native start exception does not permanently lock out retry");

// Closing while the receiver is connected must drop the link before removing the HID service.
var exitLog = new List<string>();
var exitRadio = new FakeRadioSwitch(exitLog);
var exitResult = await ExitDisconnect.ReleaseAsync(true, exitRadio,
    _ => { exitLog.Add("wait"); return Task.CompletedTask; }, () => exitLog.Add("release"), TimeSpan.FromSeconds(1));
Check(exitResult == ExitDisconnectResult.DisconnectedFirst && string.Join(",", exitLog) == "off,wait,release,on",
    "connected exit turns the radio off, waits for the link to drop, releases the service, then restores the radio");

exitLog.Clear();
exitResult = await ExitDisconnect.ReleaseAsync(false, new FakeRadioSwitch(exitLog), _ => Task.CompletedTask,
    () => exitLog.Add("release"), TimeSpan.FromSeconds(1));
Check(exitResult == ExitDisconnectResult.NoHost && string.Join(",", exitLog) == "release",
    "exit without a connected receiver releases the service without touching the radio");

exitLog.Clear();
exitResult = await ExitDisconnect.ReleaseAsync(true, new FakeRadioSwitch(exitLog) { Allowed = false },
    _ => { exitLog.Add("wait"); return Task.CompletedTask; }, () => exitLog.Add("release"), TimeSpan.FromSeconds(1));
Check(exitResult == ExitDisconnectResult.RadioUnavailable && string.Join(",", exitLog) == "off,release,on-skipped",
    "denied radio access still releases the service and does not wait for a disconnect that was not requested");

exitLog.Clear();
exitResult = await ExitDisconnect.ReleaseAsync(true, new FakeRadioSwitch(exitLog),
    async cancellation => { exitLog.Add("wait"); await Task.Delay(Timeout.Infinite, cancellation); }, () => exitLog.Add("release"),
    TimeSpan.FromMilliseconds(200));
Check(exitResult == ExitDisconnectResult.DisconnectUnconfirmed && string.Join(",", exitLog) == "off,wait,release,on",
    "a link that does not report disconnection within the limit still releases the service and restores the radio");

exitLog.Clear();
exitResult = await ExitDisconnect.ReleaseAsync(true, new FakeRadioSwitch(exitLog),
    _ => Task.CompletedTask, () => throw new InvalidOperationException("synthetic release failure"), TimeSpan.FromSeconds(1));
Check(exitResult == ExitDisconnectResult.ServiceReleaseFailed, "service release failure is reported");
Check(exitLog.LastOrDefault() == "on", "the radio is restored even when releasing the service fails");

exitLog.Clear();
var throwingRadio = new FakeRadioSwitch(exitLog) { ThrowOnOff = true };
await ExitDisconnect.ReleaseAsync(true, throwingRadio, _ => Task.CompletedTask, () => exitLog.Add("release"), TimeSpan.FromSeconds(1));
Check(string.Join(",", exitLog) == "off,release,on",
    "an interrupted turn-off still releases the service and asks the switch to restore the radio");

exitLog.Clear();
var restoreFailure = new FakeRadioSwitch(exitLog) { ThrowOnOn = true };
exitResult = await ExitDisconnect.ReleaseAsync(true, restoreFailure, _ => Task.CompletedTask,
    () => exitLog.Add("release"), TimeSpan.FromSeconds(1));
Check(exitResult == ExitDisconnectResult.RadioRestoreFailed, "restore failure is returned instead of a successful disconnect");
Check(string.Join(",", exitLog) == "off,release,on", "a failed restore is attempted after releasing the service");
restoreFailure.ThrowOnOn = false;
await restoreFailure.TurnOnAsync();
Check(exitLog.Last() == "on", "failed restore retains the changed radio for an explicit retry");

exitLog.Clear();
exitResult = await ExitDisconnect.ReleaseAsync(true, new FakeRadioSwitch(exitLog) { ThrowOnOn = true },
    token => Task.Delay(Timeout.Infinite, token), () => exitLog.Add("release"), TimeSpan.FromMilliseconds(20));
Check(exitResult == ExitDisconnectResult.RadioRestoreFailed, "restore failure takes precedence over a disconnect timeout");

var instanceName = $"JisBleInput.Tests.{Guid.NewGuid():N}";
using (var first = new SingleInstanceLease(instanceName))
{
    Check(first.IsOwner, "first instance owns the process-wide lease");
    using var second = new SingleInstanceLease(instanceName);
    Check(!second.IsOwner, "second instance cannot start another HID service");
}
using (var reopened = new SingleInstanceLease(instanceName))
    Check(reopened.IsOwner, "closing the owning instance permits the next launch");

var radioPort = new FakeRadioPort();
var radioSwitch = new RadioStateSwitch(_ => Task.FromResult<IRadioPort?>(radioPort));
Check(await radioSwitch.TryTurnOffAsync(CancellationToken.None) && !radioPort.IsOn, "radio off waits for the effective off state");
radioPort.AllowOn = false;
await RejectAsync(radioSwitch.TurnOnAsync, "denied restoration is reported to the exit coordinator");
radioPort.AllowOn = true;
await radioSwitch.TurnOnAsync();
Check(radioPort.IsOn && radioPort.OnRequests == 2, "denied restoration retains the radio and allows a successful retry");

radioPort = new FakeRadioPort { AllowOff = false };
radioSwitch = new RadioStateSwitch(_ => Task.FromResult<IRadioPort?>(radioPort));
Check(!await radioSwitch.TryTurnOffAsync(CancellationToken.None), "denied off request is not a confirmed disconnection");
await radioSwitch.TurnOnAsync();
Check(radioPort.OnRequests == 0, "denied off request leaves the original radio state untouched");

radioPort = new FakeRadioPort { ApplyChanges = false };
radioSwitch = new RadioStateSwitch(_ => Task.FromResult<IRadioPort?>(radioPort));
using (var cancelOff = new CancellationTokenSource())
{
    var off = radioSwitch.TryTurnOffAsync(cancelOff.Token);
    Check(!off.IsCompleted, "accepted off request alone is not treated as the effective off state");
    cancelOff.Cancel();
    try { await off; Check(false, "cancelled state wait must end"); }
    catch (OperationCanceledException) { Check(true, "effective state wait is cancellable"); }
}
await radioSwitch.TurnOnAsync();
Check(radioPort.IsOn, "interrupted state wait still permits restoration");

radioPort = new FakeRadioPort();
radioSwitch = new RadioStateSwitch(_ => Task.FromResult<IRadioPort?>(radioPort), TimeSpan.FromMilliseconds(20));
await radioSwitch.TryTurnOffAsync(CancellationToken.None);
radioPort.ApplyChanges = false;
try { await radioSwitch.TurnOnAsync(); Check(false, "on acceptance without on state must time out"); }
catch (OperationCanceledException) { Check(!radioPort.IsOn, "accepted on request without effective on state is reported as a failure"); }
radioPort.ApplyChanges = true;
await radioSwitch.TurnOnAsync();
Check(radioPort.IsOn, "an on-state timeout retains enough information to retry restoration");

var pendingOffRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
radioPort = new FakeRadioPort { OffRequest = pendingOffRequest.Task };
radioSwitch = new RadioStateSwitch(_ => Task.FromResult<IRadioPort?>(radioPort));
using (var cancelledRequest = new CancellationTokenSource())
{
    var off = radioSwitch.TryTurnOffAsync(cancelledRequest.Token);
    cancelledRequest.Cancel();
    try { await off; Check(false, "off request wait must cancel"); }
    catch (OperationCanceledException) { }
}
var restorePending = radioSwitch.TurnOnAsync();
Check(radioPort.OnRequests == 0 && !restorePending.IsCompleted, "restoration cannot overtake a pending off request");
radioPort.IsOn = false;
pendingOffRequest.SetResult(true);
await restorePending;
Check(radioPort.IsOn && radioPort.OnRequests == 1, "a late off completion is followed by confirmed on restoration");

radioPort = new FakeRadioPort { OffRequest = Task.FromException<bool>(new IOException("synthetic off failure")) };
radioSwitch = new RadioStateSwitch(_ => Task.FromResult<IRadioPort?>(radioPort));
try { await radioSwitch.TryTurnOffAsync(CancellationToken.None); }
catch (IOException) { }
await radioSwitch.TurnOnAsync();
Check(radioPort.IsOn && radioPort.OnRequests == 1, "a completed off failure does not permanently block restoration");

// A stalled UI thread must not delay a key release: the worker may not resume
// on the caller's SynchronizationContext between key-down and key-up.
var uiContext = new CountingSynchronizationContext();
var previousContext = SynchronizationContext.Current;
SynchronizationContext.SetSynchronizationContext(uiContext);
var offThreadTransport = new AsyncTransport(destination);
Task offThreadTransfer;
try { offThreadTransfer = new StagedSender(offThreadTransport).SendAsync("AB", destination, options: new SendOptions(TextInputMode.Ascii)); }
finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
await offThreadTransfer.ConfigureAwait(false);
Check(offThreadTransport.Frames > 4 && uiContext.Posts == 0,
    "key steps continue without the caller's synchronization context");

await XiaoChecks.RunAsync(Check);
Console.WriteLine($"Passed: {passed}");

static char HexDigit(byte[] report) => report[2] switch
{
    0x27 => '0',
    >= 0x1E and <= 0x26 => (char)('1' + report[2] - 0x1E),
    >= 0x04 and <= 0x09 when report[0] == 0x02 => (char)('A' + report[2] - 0x04),
    _ => throw new InvalidOperationException("Unexpected hex key.")
};

static string Decode(List<byte[]> frames)
{
    var downs = frames.Where(f => f[2] is not 0 and not 0x90 and not 0x39).ToArray();
    if (downs.Length % 6 != 0) throw new InvalidOperationException("Incomplete encoded character.");
    var result = new System.Text.StringBuilder();
    for (var i = 0; i < downs.Length; i += 6)
    {
        var hex = new string(downs.Skip(i).Take(4).Select(HexDigit).ToArray());
        if (downs[i + 4][2] != 0x3E || downs[i + 5][2] != 0x28) throw new InvalidOperationException("Missing conversion/commit.");
        result.Append((char)Convert.ToInt32(hex, 16));
    }
    return result.ToString();
}

static (bool SupportsImeOn, bool SupportsImeOff, int InputBits) InspectDescriptor(byte[] bytes)
{
    var page = 0; var logicalMax = 0; var usageMax = 0; var count = 0; var size = 0; var bits = 0; var supports = false; var supportsOff = false;
    for (var i = 0; i < bytes.Length;)
    {
        var prefix = bytes[i++];
        var length = (prefix & 3) == 3 ? 4 : prefix & 3;
        var value = 0;
        for (var offset = 0; offset < length; offset++) value |= bytes[i++] << (8 * offset);
        switch (prefix & 0xFC)
        {
            case 0x04: page = value; break;
            case 0x24: logicalMax = value; break;
            case 0x28: usageMax = value; break;
            case 0x74: size = value; break;
            case 0x94: count = value; break;
            case 0x80:
                bits += size * count;
                if (page == 7 && size == 8 && count == 6 && logicalMax >= 144 && usageMax >= 144) supports = true;
                if (page == 7 && size == 8 && count == 6 && logicalMax >= 145 && usageMax >= 145) supportsOff = true;
                break;
        }
    }
    return (supports, supportsOff, bits);
}

internal sealed class FakeTransport(SendTarget target) : IReportTransport
{
    public SendTarget Current { get; set; } = target;
    public List<byte[]> Frames { get; } = [];
    public List<SendTarget> Targets { get; } = [];
    public Func<byte[], CancellationToken, Task>? BeforeWrite { get; set; }
    public Action<byte[]>? Receive { get; init; }
    public bool HasKey(byte usage) => Frames.Any(f => f[2] == usage);
    public bool IsReady(SendTarget destination) => destination == Current;
    public async Task WriteAsync(byte[] report, SendTarget destination, CancellationToken cancellation)
    {
        if (BeforeWrite is not null) await BeforeWrite(report, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!IsReady(destination)) throw new InvalidOperationException("Target changed.");
        Frames.Add(report.ToArray()); Targets.Add(destination); Receive?.Invoke(report);
    }
}

internal enum InputMode { Off, Hiragana, Alphanumeric }

// This model checks the key protocol and state-drift regression. It is NOT a
// replacement for Windows/Microsoft IME/Bluetooth hardware acceptance.
internal sealed class Receiver(InputMode initialMode)
{
    public bool IgnoreLang1 { get; init; }
    public bool IgnoreCtrlCaps { get; init; }
    public bool DriftOnCommit { get; init; }
    public string? LineEnding { get; init; }
    public InputMode Mode { get; private set; } = initialMode;
    public string Output { get; private set; } = "";
    public int TimestampCount { get; private set; }
    public int UnexpectedEnterCount { get; private set; }
    private bool converted;
    private string composition = "";

    public void Accept(byte[] report)
    {
        switch (report[2])
        {
            case 0: return;
            case 0x90:
                if (!IgnoreLang1) Mode = InputMode.Hiragana;
                return;
            case 0x39:
                if (report[0] != 1) throw new InvalidOperationException("Unexpected CapsLock toggle.");
                if (!IgnoreCtrlCaps) Mode = InputMode.Hiragana;
                return;
            case 0x3E:
                if (Mode == InputMode.Hiragana && composition.Length == 4)
                {
                    composition = ((char)Convert.ToInt32(composition, 16)).ToString(); converted = true;
                }
                else TimestampCount++;
                return;
            case 0x28:
                if (report[0] != 0) throw new InvalidOperationException("Unexpected modified Enter.");
                if (converted) Output += composition;
                else if (composition.Length == 0 && LineEnding is not null) Output += LineEnding;
                else UnexpectedEnterCount++;
                composition = ""; converted = false;
                if (DriftOnCommit) Mode = InputMode.Alphanumeric;
                return;
        }
        var digit = report[2] switch
        {
            0x27 => '0',
            >= 0x1E and <= 0x26 => (char)('1' + report[2] - 0x1E),
            >= 0x04 and <= 0x09 when report[0] == 0x02 => (char)('A' + report[2] - 0x04),
            _ => throw new InvalidOperationException("Unexpected receiver key.")
        };
        if (Mode == InputMode.Hiragana) composition += digit;
        else Output += digit;
    }
}

// Independent receiver fixture: rows are ordered by physical HID key position,
// rather than generated by or decoded with the production character encoder.
internal sealed class AsciiReceiver(ReceiverKeyboardLayout layout)
{
    public bool ImeOn { get; set; } = true;
    public bool IgnoreImeOff { get; init; }
    public string Output { get; private set; } = "";
    public string Composition { get; private set; } = "";
    private byte held;

    public void Accept(byte[] report)
    {
        if (report[2] == 0) { held = 0; return; }
        if (report[0] is not 0 and not 2) throw new InvalidOperationException("ASCII input contains a shortcut modifier.");
        if (held == report[2]) return;
        held = report[2];
        if (held == 0x91) { if (!IgnoreImeOff) ImeOn = false; return; }
        if (held == 0x28)
        {
            if (ImeOn) { Output += Composition; Composition = ""; }
            else Output += "\r\n";
            return;
        }
        var shifted = report[0] == 2;
        char character;
        if (held is >= 0x04 and <= 0x1D) character = (char)((shifted ? 'A' : 'a') + held - 0x04);
        else if (held is >= 0x1E and <= 0x27)
        {
            var row = !shifted ? "1234567890" : layout == ReceiverKeyboardLayout.JapaneseJis ? "!\"#$%&'()\0" : "!@#$%^&*()";
            character = row[held - 0x1E];
        }
        else if (held == 0x2C) character = ' ';
        else if (layout == ReceiverKeyboardLayout.JapaneseJis && held == 0x89) character = shifted ? '|' : '\\';
        else if (layout == ReceiverKeyboardLayout.JapaneseJis && held == 0x87) character = shifted ? '_' : '\\';
        else if (held is >= 0x2D and <= 0x38)
        {
            var row = layout == ReceiverKeyboardLayout.JapaneseJis
                ? shifted ? "=~`{\0}+*\0<>?" : "-^@[\0];:\0,./"
                : shifted ? "_+{}|\0:\"~<>?" : "-=[]\\\0;'`,./";
            character = row[held - 0x2D];
        }
        else throw new InvalidOperationException("Unexpected ASCII physical key.");
        if (character == '\0') throw new InvalidOperationException("Unassigned physical key for this receiver layout.");
        if (ImeOn) Composition += character;
        else Output += character;
    }
}

internal sealed class FakeAdvertisementPublisher : IAdvertisementPublisher
{
    private event Action<AdvertisementReport>? changed;
    public AdvertisementReport Current { get; private set; } = new(AdvertisementState.Created);
    public bool? Discoverable { get; private set; }
    public AdvertisementReport NextStart { get; set; } = new(AdvertisementState.Started);
    public bool DeliverEvents { get; init; } = true;
    public bool CompleteStop { get; init; } = true;
    public bool ThrowOnStart { get; set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int ListenerCount => changed?.GetInvocationList().Length ?? 0;
    public event Action<AdvertisementReport>? Changed { add => changed += value; remove => changed -= value; }
    public void Start(bool discoverable)
    {
        StartCount++;
        if (ThrowOnStart) throw new InvalidOperationException("synthetic native start failure");
        Discoverable = discoverable;
        Publish(NextStart);
    }
    public void Stop()
    {
        StopCount++;
        if (CompleteStop) Publish(new(AdvertisementState.Stopped));
    }
    public void Publish(AdvertisementReport report)
    {
        Current = report;
        if (DeliverEvents) changed?.Invoke(report);
    }
}

internal sealed class FakeRadioPort : IRadioPort
{
    public bool IsOn { get; set; } = true;
    public bool AllowOn { get; set; } = true;
    public bool AllowOff { get; init; } = true;
    public bool ApplyChanges { get; set; } = true;
    public int OnRequests { get; private set; }
    public Task<bool>? OffRequest { get; init; }
    public Task<bool> SetOnAsync(bool on)
    {
        if (on) OnRequests++;
        if (!on && OffRequest is not null) return OffRequest;
        var allowed = on ? AllowOn : AllowOff;
        if (allowed && ApplyChanges) IsOn = on;
        return Task.FromResult(allowed);
    }
}

internal sealed class FakeRadioSwitch(List<string> log) : IBluetoothRadioSwitch
{
    private bool changed;
    public bool Allowed { get; init; } = true;
    public bool ThrowOnOff { get; init; }
    public bool ThrowOnOn { get; set; }
    public Task<bool> TryTurnOffAsync(CancellationToken cancellation)
    {
        log.Add("off");
        if (!Allowed) return Task.FromResult(false);
        changed = true;
        if (ThrowOnOff) throw new InvalidOperationException("synthetic radio failure");
        return Task.FromResult(true);
    }
    public Task TurnOnAsync()
    {
        log.Add(changed ? "on" : "on-skipped");
        if (changed && ThrowOnOn) throw new IOException("synthetic restore failure");
        changed = false;
        return Task.CompletedTask;
    }
}

internal sealed class ManualClock : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref ticks);
    public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
}

internal sealed class CountingSynchronizationContext : SynchronizationContext
{
    private int posts;
    public int Posts => Volatile.Read(ref posts);
    public override void Post(SendOrPostCallback callback, object? state)
    {
        Interlocked.Increment(ref posts);
        ThreadPool.QueueUserWorkItem(_ => callback(state));
    }
}

// Completes every write on another thread, like the real USB and GATT transports.
internal sealed class AsyncTransport(SendTarget target) : IReportTransport
{
    private int frames;
    public int Frames => Volatile.Read(ref frames);
    public bool IsReady(SendTarget destination) => destination == target;
    public Task WriteAsync(byte[] report, SendTarget destination, CancellationToken cancellation) =>
        Task.Run(() => { Interlocked.Increment(ref frames); }, cancellation);
}
