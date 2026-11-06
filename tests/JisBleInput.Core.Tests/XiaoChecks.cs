using System.Buffers.Binary;
using System.Threading.Channels;
using JisBleInput.Core;

internal static class XiaoChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using (var resource = new StreamReader(typeof(XiaoChecks).Assembly.GetManifestResourceStream("xiao-vectors.tsv")!))
        {
            foreach (var row in (await resource.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(row => !row.StartsWith('#')))
            {
                var columns = row.TrimEnd('\r').Split('\t');
                var wire = Convert.FromHexString(columns[1]);
                try
                {
                    var decoded = XiaoProtocol.Decode(wire.AsSpan(0, wire.Length - 1));
                    check(columns[2] != "-" && XiaoProtocol.Encode(decoded).SequenceEqual(wire), $"shared Python wire fixture {columns[0]}");
                    var raw = Convert.FromHexString(columns[2]);
                    check(decoded.Command == raw[1] && decoded.Session == new Guid(raw.AsSpan(4, 16))
                        && decoded.Sequence == BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(20))
                        && decoded.Generation == BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(24))
                        && decoded.Payload.SequenceEqual(raw[28..^2]), $"shared fixture fields {columns[0]}");
                }
                catch (InvalidDataException) { check(columns[2] == "-", $"shared invalid fixture {columns[0]}"); }
            }
        }
        check(XiaoProtocol.Crc("123456789"u8) == 0x29b1, "CRC uses the independent CCITT-FALSE check value");
        var session = new Guid(Enumerable.Range(0, 16).Select(value => (byte)value).ToArray());
        for (var value = 0; value < 256; value++)
        {
            var original = new XiaoFrame(4, session, 0x12345678, 0x9abcdef0, Enumerable.Repeat((byte)value, 8).ToArray());
            var encoded = XiaoProtocol.Encode(original);
            var decoded = XiaoProtocol.Decode(encoded.AsSpan(0, encoded.Length - 1));
            check(encoded[^1] == 0 && encoded.AsSpan(0, encoded.Length - 1).IndexOf((byte)0) < 0
                && decoded.Session == original.Session && decoded.Sequence == original.Sequence && decoded.Generation == original.Generation
                && decoded.Payload.SequenceEqual(original.Payload), $"COBS/CRC all-byte fixture {value}");
        }
        var reader = new XiaoFrameReader();
        var hello = XiaoProtocol.Encode(new(1, session, 0, 0, []));
        XiaoFrame? received = null;
        foreach (var value in hello) received = reader.Push(value, 10);
        check(received?.Command == 1, "fragmented HELLO reassembles exactly once");
        for (var i = 0; i < 400; i++) reader.Push(1, 20);
        check(reader.Push(0, 20) is null, "oversized fragment discards through its terminator");
        foreach (var value in hello) received = reader.Push(value, 21);
        check(received?.Command == 1, "framing recovers after an oversized fragment");
        reader.Push(hello[0], 30); reader.Push(hello[1], 300);
        check(reader.Push(0, 301) is null, "expired fragments do not form a command");
        foreach (var invalid in new[] { new byte[] { 2 }, new byte[] { 0 }, new byte[] { 2, 0 }, new byte[130] })
        {
            try { XiaoProtocol.Decode(invalid); check(false, "reject invalid frame"); }
            catch (InvalidDataException) { check(true, "reject invalid COBS or frame length"); }
        }
        var corrupted = hello[..^1]; corrupted[^1] ^= 1;
        try { XiaoProtocol.Decode(corrupted); check(false, "reject CRC damage"); }
        catch (InvalidDataException) { check(true, "CRC damage never becomes an input command"); }
        var maximum = new XiaoFrame(4, session, 1, 1, new byte[98]);
        var maximumBytes = XiaoProtocol.Encode(maximum);
        check(XiaoProtocol.Decode(maximumBytes.AsSpan(0, maximumBytes.Length - 1)).Payload.Length == 98, "maximum frame is accepted without overflow");
        try { XiaoProtocol.Encode(maximum with { Payload = new byte[99] }); check(false, "reject oversize encode"); }
        catch (ArgumentException) { check(true, "encode bounds the payload"); }

        using (var channel = new FakeChannel())
        using (var client = new XiaoClient(channel))
        {
            await client.StartAsync(CancellationToken.None);
            check(client.Current is { Ready: true, Firmware: "0.1.0", CapsLock: false }, "HELLO validates product and status structure");
            await client.RequestAsync(XiaoCommand.Arm, [], client.Generation, CancellationToken.None);
            await client.RequestAsync(XiaoCommand.Report, [1, 0, 4, 0, 0, 0, 0, 0], client.Generation, CancellationToken.None);
            await client.RequestAsync(XiaoCommand.Release, [], client.Generation, CancellationToken.None);
            await client.RequestAsync(XiaoCommand.Disarm, [], client.Generation, CancellationToken.None);
            await Task.Delay(310);
            check(channel.Commands.Contains(XiaoCommand.Ping) && channel.Commands.Count(command => command == XiaoCommand.Report) == 1,
                "heartbeat continues without retrying input commands");
            check(channel.Sequences.SequenceEqual(Enumerable.Range(0, channel.Sequences.Count).Select(index => (uint)index)), "all commands share one monotonic writer sequence");
        }
        using (var channel = new FakeChannel { DropArm = true })
        using (var client = new XiaoClient(channel, TimeSpan.FromMilliseconds(80)))
        {
            await client.StartAsync(CancellationToken.None);
            try { await client.RequestAsync(XiaoCommand.Arm, [], client.Generation, CancellationToken.None); check(false, "ARM timeout"); }
            catch (OperationCanceledException) { check(channel.Disposed && channel.Commands.Count(command => command == XiaoCommand.Arm) == 1, "lost ACK closes actual channel and never retries"); }
        }
        using (var channel = new FakeChannel { WrongSession = true })
        using (var client = new XiaoClient(channel))
        {
            try { await client.StartAsync(CancellationToken.None); check(false, "wrong session"); }
            catch (Exception error) when (error is IOException or OperationCanceledException) { check(channel.Disposed, "wrong-session response invalidates the link"); }
        }
        var invalidSuccesses = new (XiaoCommand Command, byte[] Payload, Func<byte[], byte[]> Reply)[]
        {
            (XiaoCommand.Arm, [], _ => [0]),
            (XiaoCommand.Report, [1, 0, 4, 0, 0, 0, 0, 0], _ => [0]),
            (XiaoCommand.Disarm, [], _ => [0]),
            (XiaoCommand.Disarm, [], state => [.. state, 0]),
            (XiaoCommand.Disarm, [], state => { state[6] = 2; return state; })
        };
        foreach (var (command, payload, reply) in invalidSuccesses)
        {
            using var channel = new FakeChannel
            {
                TransformResponse = (request, state) => request == command ? reply(state) : state
            };
            using var client = new XiaoClient(channel, TimeSpan.FromSeconds(5));
            var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Faulted += () => faulted.TrySetResult();
            await client.StartAsync(CancellationToken.None);
            try
            {
                await client.RequestAsync(command, payload, client.Generation, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2));
                check(false, $"reject malformed {command} success ACK");
            }
            catch (Exception error) when (error is IOException { InnerException: InvalidDataException } or OperationCanceledException)
            {
                await faulted.Task.WaitAsync(TimeSpan.FromSeconds(1));
                check(channel.Disposed && channel.Commands.Count(request => request == command) == 1,
                    $"malformed {command} success ACK closes the channel without retrying");
            }
        }
        var preferences = new TransportPreferences();
        preferences.Validate();
        check(preferences.Method == ConnectionMethod.LocalBluetooth, "missing transport preferences preserve original Bluetooth mode");
        try { (preferences with { BoardHash = "invalid" }).Validate(); check(false, "bad board preference"); }
        catch (InvalidDataException) { check(true, "board preferences cannot contain arbitrary identifiers"); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var destination = new SendTarget("synthetic", 1);
        var transport = new FakeTransport(destination);
        var sender = new StagedSender(transport);
        try { await sender.SendSnapshotAsync(TextSnapshot.Create("A"), destination, new(), cancelled.Token); check(false, "pre-cancelled send"); }
        catch (OperationCanceledException) { check(transport.Frames.Count == 0, "cancellation before sender start emits no mode or text keys"); }
    }

    internal sealed class FakeChannel : IXiaoChannel
    {
        private readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();
        public readonly List<XiaoCommand> Commands = [];
        public readonly List<uint> Sequences = [];
        public bool DropArm { get; init; }
        public bool WrongSession { get; init; }
        public Func<XiaoCommand, byte[], byte[]>? TransformResponse { get; init; }
        public bool Disposed { get; private set; }
        public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellation)
        {
            var bytes = await incoming.Reader.ReadAsync(cancellation);
            bytes.CopyTo(buffer, 0); return bytes.Length;
        }
        public Task WriteAsync(byte[] buffer, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var request = XiaoProtocol.Decode(buffer.AsSpan(0, buffer.Length - 1));
            Commands.Add((XiaoCommand)request.Command); Sequences.Add(request.Sequence);
            if (DropArm && request.Command == (byte)XiaoCommand.Arm) return Task.CompletedTask;
            var state = new byte[XiaoStatus.Length + 1];
            "JBI1"u8.CopyTo(state.AsSpan(1)); state[5] = 7 | 16 | 64; state[6] = 1;
            state[56] = 1;
            state = TransformResponse?.Invoke((XiaoCommand)request.Command, state) ?? state;
            var response = XiaoProtocol.Encode(new((byte)(request.Command | 0x80), WrongSession ? Guid.NewGuid() : request.Session, request.Sequence, 4, state));
            foreach (var chunk in response.Chunk(3)) incoming.Writer.TryWrite(chunk);
            return Task.CompletedTask;
        }
        public void Dispose() { Disposed = true; incoming.Writer.TryComplete(); }
    }
}
