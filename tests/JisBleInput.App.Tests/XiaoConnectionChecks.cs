using System.Security.Cryptography;
using System.Threading.Channels;
using System.Windows.Controls;
using JisBleInput.App;
using JisBleInput.Core;

internal static class XiaoConnectionChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var mismatch in new[] { "board", "firmware", "descriptor" })
        {
            using var channel = new ChannelFake { Mismatch = mismatch };
            using var keyboard = new XiaoSerialKeyboard(_ => channel) { Port = new("COM999", "0011223344556677") };
            try { await keyboard.StartAsync(false, CancellationToken.None); check(false, "reject incompatible board"); }
            catch (System.IO.InvalidDataException) { check(channel.Disposed && !keyboard.IsStarted, $"{mismatch} mismatch prevents ARM and closes USB"); }
        }
        using var io = new ChannelFake();
        using var connection = new XiaoSerialKeyboard(_ => io) { Port = new("COM999", "0011223344556677") };
        await connection.StartAsync(false, CancellationToken.None);
        check(!connection.UsesLocalBluetooth && connection.Hosts.Count == 1, "XIAO lists only its ready registered peer");
        connection.Select(connection.Hosts.Single().Id);
        var target = connection.Target!;
        check(connection.IsReady(target), "XIAO selected peer and generation are ready");
        await connection.BeginSendAsync(target, CancellationToken.None);
        await connection.WriteAsync([0, 0, 0x90, 0, 0, 0, 0, 0], target, CancellationToken.None);
        await connection.WriteAsync(new byte[8], target, CancellationToken.None);
        await connection.EndSendAsync(target, CancellationToken.None);
        check(io.Commands.Take(5).SequenceEqual(new[] { XiaoCommand.Hello, XiaoCommand.Arm, XiaoCommand.Report, XiaoCommand.Release, XiaoCommand.Disarm }),
            "XIAO transfer arms, sends raw usage, releases and disarms in order");
        var stopStart = io.Commands.Count;
        io.HoldReport = true;
        var sender = new StagedSender(connection, (_, _) => Task.CompletedTask);
        await connection.BeginSendAsync(target, CancellationToken.None);
        var sending = sender.SendSnapshotAsync(TextSnapshot.Create("橋"), target, new());
        await io.ReportWritten.Task.WaitAsync(TimeSpan.FromSeconds(1));
        sender.Cancel(finishCurrentWrite: true);
        check(!sending.IsCompleted && !io.Disposed, "XIAO graceful stop keeps the outstanding report's USB session alive");
        io.AcknowledgeReport();
        await sending;
        await connection.EndSendAsync(target, CancellationToken.None);
        check(connection.IsReady(target) && sender.ReleaseReportSent == true
            && io.Commands.Skip(stopStart).Where(command => command != XiaoCommand.Ping)
                .SequenceEqual(new[] { XiaoCommand.Arm, XiaoCommand.Report, XiaoCommand.Release, XiaoCommand.Disarm }),
            "XIAO stop sends release and disarm after the pending ACK and keeps the connection");
        foreach (var duringReport in new[] { false, true })
        {
            using var timedOut = new ChannelFake { HoldReport = duringReport, DropArm = !duringReport };
            var timeoutKeyboard = new XiaoSerialKeyboard(_ => timedOut) { Port = new("COM999", "0011223344556677") };
            var directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "_internal", "build-cache",
                "test-data", "xiao-timeout", Guid.NewGuid().ToString("N"));
            var window = new MainWindow(false, directory, keyboardConnection: timeoutKeyboard);
            try
            {
                await timeoutKeyboard.StartAsync(false, CancellationToken.None);
                timeoutKeyboard.Select(timeoutKeyboard.Hosts.Single().Id);
                await window.SendToAsync(timeoutKeyboard.Target!).WaitAsync(TimeSpan.FromSeconds(4));
                var message = ((Grid)window.Content).Children.OfType<StackPanel>()
                    .SelectMany(panel => panel.Children.OfType<TextBlock>()).Single(label => label.Name == "TransferMessage").Text;
                check(timedOut.Disposed && message.Contains("XIAOの応答が途絶えました")
                    && (duringReport ? message.Contains("一部入力") && message.Contains("キー解放は未確認")
                        : message.Contains("送信準備") && !message.Contains("一部入力")),
                    duringReport ? "REPORT timeout names the communication failure and possible partial input"
                        : "ARM timeout names the preparation failure without claiming partial input");
            }
            finally
            {
                window.PrepareForSessionEnd();
                window.Close();
            }
        }
        await io.ChangeAsync(5, 7 | 16 | 32 | 64);
        await connection.RefreshHostsAsync();
        check(!connection.IsReady(target), "CapsLock blocks nonzero input");
        await connection.WriteAsync(new byte[8], target, CancellationToken.None);
        check(io.Commands.Last(command => command != XiaoCommand.Ping) == XiaoCommand.Release, "CapsLock still permits zero release");
        await io.ChangeAsync(6, 7 | 16 | 64);
        await connection.RefreshHostsAsync();
        check(!connection.IsReady(target) && connection.Target!.Generation != target.Generation, "link generation invalidates a captured destination");
        var count = io.Commands.Count;
        try { await connection.WriteAsync([0, 0, 4, 0, 0, 0, 0, 0], target, CancellationToken.None); check(false, "reject stale send"); }
        catch (InvalidOperationException) { check(io.Commands.Count == count, "stale destination emits no USB report"); }
        await connection.ClearBondAsync(CancellationToken.None);
        check(connection.Target is null && io.Commands.Last(command => command != XiaoCommand.Ping) == XiaoCommand.ClearBond, "explicit clear removes selection without local radio operations");

        using (var failedStorage = new ChannelFake { FirmwarePatch = 1, FailClear = true })
        using (var updated = new XiaoSerialKeyboard(_ => failedStorage) { Port = new("COM999", "0011223344556677") })
        {
            await updated.StartAsync(false, CancellationToken.None);
            check(updated.IsStarted, "firmware 0.1.1 is accepted alongside 0.1.0");
            using var newestChannel = new ChannelFake { FirmwarePatch = 2 };
            using var newest = new XiaoSerialKeyboard(_ => newestChannel) { Port = new("COM999", "0011223344556677") };
            await newest.StartAsync(false, CancellationToken.None);
            check(newest.IsStarted, "firmware 0.1.2 is accepted");
            try { await updated.ClearBondAsync(CancellationToken.None); check(false, "storage failure must not report success"); }
            catch (System.IO.IOException error)
            {
                check(error.Message.Contains("登録ファイルを削除できません") && failedStorage.Commands.Count(command => command == XiaoCommand.ClearBond) == 1,
                    "storage failure is presented to the caller without automatic retries");
            }
        }

        using (var port = SerialPortChannel.CreatePort("COM999"))
            check(port.BaudRate == 115200 && port.BaudRate != 1200 && port.DtrEnable && port.Handshake == System.IO.Ports.Handshake.None && !port.IsOpen,
                "application USB settings are 115200bps with DTR on and never the 1200bps bootloader touch");
        check(XiaoUsbPort.Classify(@"USB\VID_2886&PID_8045&MI_00\7&1A2B&0&0000") == XiaoPortKind.Application
            && XiaoUsbPort.Classify(@"USB\VID_2886&PID_0045&MI_00\7&1A2B&0&0000") == XiaoPortKind.Bootloader
            && XiaoUsbPort.Classify(@"USB\VID_2886&PID_8044&MI_00\7&1A2B&0&0000") == XiaoPortKind.Unsupported
            && XiaoUsbPort.Classify(@"USB\VID_2886&PID_0044&MI_00\7&1A2B&0&0000") == XiaoPortKind.Unsupported
            && XiaoUsbPort.Classify(@"USB\VID_2886&PID_8046&MI_00\7&1A2B&0&0000") == XiaoPortKind.Unsupported,
            "Sense application, Sense bootloader and normal/other PIDs are classified separately");
        check(new XiaoUsbPort("COM8", "", XiaoPortKind.Bootloader).Display.Contains("書込モード")
            && new XiaoUsbPort("COM9", "", XiaoPortKind.Unsupported).Display.Contains("対象外"), "bootloader and unsupported boards are labelled, not hidden");
        var opened = 0;
        using (var bootloader = new XiaoSerialKeyboard(_ => { opened++; return new ChannelFake(); }) { Port = new("COM8", "", XiaoPortKind.Bootloader) })
        {
            try { await bootloader.StartAsync(false, CancellationToken.None); check(false, "reject bootloader port"); }
            catch (InvalidOperationException) { check(opened == 0 && !bootloader.IsStarted, "bootloader and unsupported ports are never opened"); }
        }
        using (var busy = new XiaoSerialKeyboard(_ => throw new UnauthorizedAccessException("in use")) { Port = new("COM999", "0011223344556677") })
        {
            try { await busy.StartAsync(false, CancellationToken.None); check(false, "reject busy port"); }
            catch (UnauthorizedAccessException) { check(!busy.IsStarted && busy.Target is null, "an occupied COM port leaves XIAO stopped without a destination"); }
        }
        using var unplugged = new ChannelFake();
        using var removable = new XiaoSerialKeyboard(_ => unplugged) { Port = new("COM999", "0011223344556677") };
        await removable.StartAsync(false, CancellationToken.None);
        removable.Select(removable.Hosts.Single().Id);
        var removableTarget = removable.Target!;
        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        removable.Changed += () => { if (!removable.IsStarted) faulted.TrySetResult(); };
        unplugged.Unplug();
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var beforeWrite = unplugged.Commands.Count;
        try { await removable.WriteAsync([0, 0, 4, 0, 0, 0, 0, 0], removableTarget, CancellationToken.None); check(false, "reject send after unplug"); }
        catch (InvalidOperationException) { check(!removable.IsReady(removableTarget) && removable.Target is null && unplugged.Commands.Count == beforeWrite, "USB removal stops XIAO, clears the destination and sends nothing more"); }
        await ReconnectChecksAsync(check);
    }

    private static async Task<bool> UntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        return condition();
    }

    private static async Task ReconnectChecksAsync(Action<bool, string> check)
    {
        var board = new XiaoUsbPort("COM999", "0011223344556677");
        foreach (var optOut in new[] { true, false })
        {
            var directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "_internal", "build-cache",
                "test-data", "xiao-reconnect", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            if (optOut) System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "connection-preferences.json"),
                "{\"Version\":1,\"AutoReconnect\":false,\"PeerHash\":null}");
            var channels = new List<ChannelFake>();
            var keyboard = new XiaoSerialKeyboard(_ =>
            {
                var channel = new ChannelFake { Mismatch = channels.Count >= 2 ? "firmware" : null };
                channels.Add(channel);
                return channel;
            }) { Port = board };
            var window = new MainWindow(false, directory, keyboardConnection: keyboard) { DiscoverPorts = () => [board] };
            try
            {
                await window.StartConnectionAsync(false);
                var hosts = ((Grid)window.Content).Children.OfType<Grid>().SelectMany(grid => grid.Children.OfType<ComboBox>())
                    .Single(box => box.Name == "Hosts");
                await UntilAsync(() => hosts.Items.Count == 1);
                var id = keyboard.Hosts.Single().Id;
                hosts.SelectedValue = id;
                check(keyboard.Target?.Id == id, "choosing the XIAO destination in the selector selects it");
                if (optOut)
                {
                    await channels[0].ChangeAsync(6, 1 | 64);
                    check(await UntilAsync(() => keyboard.Target is null), "a link drop clears the XIAO destination");
                    await channels[0].ChangeAsync(7, 7 | 16 | 64);
                    check(await UntilAsync(() => keyboard.Target?.Id == id),
                        "the destination chosen in this run returns after a link drop even with startup waiting disabled");
                }
                channels[0].Unplug();
                check(await UntilAsync(() => !keyboard.IsStarted), "USB removal stops the XIAO link shown in the window");
                await window.TryReconnectXiaoAsync();
                if (optOut)
                {
                    await Task.Delay(100);
                    check(!keyboard.IsStarted && channels.Count == 1, "disabling automatic connection also disables automatic USB reconnection");
                    continue;
                }
                check(await UntilAsync(() => keyboard.IsStarted && keyboard.Target?.Id == id) && channels.Count == 2,
                    "a lost XIAO USB link is reopened automatically and the chosen destination returns");
                check(channels[1].Commands[0] == XiaoCommand.Hello
                    && !channels[1].Commands.Any(command => command is XiaoCommand.Arm or XiaoCommand.Report),
                    "automatic USB reconnection starts a new session without arming or resending keys");
                channels[1].Unplug();
                await UntilAsync(() => !keyboard.IsStarted);
                await window.TryReconnectXiaoAsync();
                check(await UntilAsync(() => channels.Count == 3 && channels[2].Disposed) && !keyboard.IsStarted,
                    "an unsupported board is rejected during automatic reconnection");
                await window.TryReconnectXiaoAsync();
                await Task.Delay(100);
                check(channels.Count == 3, "automatic reconnection stops after a non-transient rejection");
            }
            finally
            {
                window.PrepareForSessionEnd();
                window.Close();
            }
        }
    }

    private sealed class ChannelFake : IXiaoChannel
    {
        private readonly Channel<byte[]> input = Channel.CreateUnbounded<byte[]>();
        private Guid session;
        private uint generation = 5;
        private byte flags = 7 | 16 | 64;
        private byte[]? heldReply;
        public string? Mismatch { get; init; }
        public byte FirmwarePatch { get; init; }
        public bool FailClear { get; init; }
        public bool HoldReport { get; set; }
        public bool DropArm { get; init; }
        public TaskCompletionSource ReportWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public List<XiaoCommand> Commands { get; } = [];
        private byte[] State()
        {
            var state = new byte[57];
            "JBI1"u8.CopyTo(state); state[4] = flags; state[5] = 1;
            Convert.FromHexString(Mismatch == "board" ? "0011223344556678" : "0011223344556677").CopyTo(state, 6);
            state[14] = 1;
            SHA256.HashData(HidKeyboardMap.Descriptor).CopyTo(state, 22);
            if (Mismatch == "descriptor") state[22] ^= 1;
            state[55] = 1; state[56] = Mismatch == "firmware" ? (byte)9 : FirmwarePatch;
            return state;
        }
        public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellation)
        {
            var bytes = await input.Reader.ReadAsync(cancellation); bytes.CopyTo(buffer, 0); return bytes.Length;
        }
        public Task WriteAsync(byte[] buffer, CancellationToken cancellation)
        {
            var request = XiaoProtocol.Decode(buffer.AsSpan(0, buffer.Length - 1)); session = request.Session;
            Commands.Add((XiaoCommand)request.Command);
            if (DropArm && request.Command == (byte)XiaoCommand.Arm) return Task.CompletedTask;
            var response = FailClear && request.Command == (byte)XiaoCommand.ClearBond
                ? new[] { (byte)XiaoResult.StorageFailed } : new byte[] { 0 }.Concat(State()).ToArray();
            var encoded = XiaoProtocol.Encode(new((byte)(request.Command | 0x80), session, request.Sequence, generation, response));
            if (HoldReport && request.Command == (byte)XiaoCommand.Report)
            {
                heldReply = encoded;
                ReportWritten.TrySetResult();
            }
            else input.Writer.TryWrite(encoded);
            return Task.CompletedTask;
        }
        public void AcknowledgeReport()
        {
            HoldReport = false;
            input.Writer.TryWrite(heldReply ?? throw new InvalidOperationException("No report is waiting."));
            heldReply = null;
        }
        public Task ChangeAsync(uint nextGeneration, byte nextFlags)
        {
            generation = nextGeneration; flags = nextFlags;
            input.Writer.TryWrite(XiaoProtocol.Encode(new(0xf0, session, 0, generation, State())));
            return Task.CompletedTask;
        }
        public void Unplug() => input.Writer.TryComplete(new System.IO.IOException("synthetic USB removal"));
        public void Dispose() { Disposed = true; input.Writer.TryComplete(); }
    }
}
