using System.Security.Cryptography;
using System.Threading.Channels;
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
        await io.ChangeAsync(5, 7 | 16 | 32 | 64);
        await connection.RefreshHostsAsync();
        check(!connection.IsReady(target), "CapsLock blocks nonzero input");
        await connection.WriteAsync(new byte[8], target, CancellationToken.None);
        check(io.Commands.Last() == XiaoCommand.Release, "CapsLock still permits zero release");
        await io.ChangeAsync(6, 7 | 16 | 64);
        await connection.RefreshHostsAsync();
        check(!connection.IsReady(target) && connection.Target!.Generation != target.Generation, "link generation invalidates a captured destination");
        var count = io.Commands.Count;
        try { await connection.WriteAsync([0, 0, 4, 0, 0, 0, 0, 0], target, CancellationToken.None); check(false, "reject stale send"); }
        catch (InvalidOperationException) { check(io.Commands.Count == count, "stale destination emits no USB report"); }
        await connection.ClearBondAsync(CancellationToken.None);
        check(connection.Target is null && io.Commands.Last() == XiaoCommand.ClearBond, "explicit clear removes selection without local radio operations");

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
    }

    private sealed class ChannelFake : IXiaoChannel
    {
        private readonly Channel<byte[]> input = Channel.CreateUnbounded<byte[]>();
        private Guid session;
        private uint generation = 5;
        private byte flags = 7 | 16 | 64;
        public string? Mismatch { get; init; }
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
            state[55] = 1; if (Mismatch == "firmware") state[56] = 1;
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
            input.Writer.TryWrite(XiaoProtocol.Encode(new((byte)(request.Command | 0x80), session, request.Sequence, generation, new byte[] { 0 }.Concat(State()).ToArray())));
            return Task.CompletedTask;
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
